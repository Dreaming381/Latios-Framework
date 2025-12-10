using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Calci
{
    public static partial class PointDistributions
    {
        /// <summary>
        /// Parameters for generating an accretion disk distribution.
        /// Points are distributed in a spiral pattern radiating from a center point,
        /// simulating the appearance of an accretion disk around a celestial body.
        /// </summary>
        public struct AccretionDiskParams
        {
            /// <summary>
            /// Inner radius of the disk. Points will not be generated closer than this distance.
            /// </summary>
            public float innerRadius;

            /// <summary>
            /// Outer radius of the disk. Points will not be generated farther than this distance.
            /// </summary>
            public float outerRadius;

            /// <summary>
            /// Number of spiral arms in the disk. Higher values create more distinct spiral patterns.
            /// </summary>
            public int numSpirals;

            /// <summary>
            /// Controls how tightly the spiral arms wind. Higher values create tighter spirals.
            /// Typical values range from 0.5 to 3.0.
            /// </summary>
            public float spiralTightness;

            /// <summary>
            /// Random number generator for point distribution.
            /// </summary>
            public Rng rng;
        }

        /// <summary>
        /// Generates points distributed in an accretion disk pattern.
        /// Points are arranged in spiral arms radiating from the center, with random scatter.
        /// All points will lie on the XY plane (Z = 0).
        /// </summary>
        /// <param name="points">Output array to fill with generated points. Must be allocated to the desired size.</param>
        /// <param name="params">Parameters controlling the disk distribution.</param>
        public static void GenerateAccretionDisk(NativeArray<float3> points, AccretionDiskParams @params)
        {
            var job = new GenerateAccretionDiskJob
            {
                points          = points,
                innerRadius     = @params.innerRadius,
                outerRadius     = @params.outerRadius,
                numSpirals      = @params.numSpirals,
                spiralTightness = @params.spiralTightness,
                rng             = @params.rng
            };

            job.Schedule(points.Length, 64).Complete();
        }

        /// <summary>
        /// Calculates a single point in an accretion disk distribution.
        /// This is useful when you need to generate points within a custom job or with additional per-point data.
        /// </summary>
        /// <param name="innerRadius">Inner radius of the disk.</param>
        /// <param name="outerRadius">Outer radius of the disk.</param>
        /// <param name="numSpirals">Number of spiral arms.</param>
        /// <param name="spiralTightness">How tightly the spirals wind.</param>
        /// <param name="rngSequence">RNG sequence for this point. Use rng.GetSequence(index) for parallel generation.</param>
        /// <returns>A point on the XY plane (Z = 0) in the accretion disk pattern.</returns>
        public static float3 CalculateAccretionDiskPoint(
            float innerRadius,
            float outerRadius,
            int numSpirals,
            float spiralTightness,
            ref Rng.RngSequence rngSequence)
        {
            // Choose which spiral arm this point belongs to
            int   armIndex     = rngSequence.NextInt(0, numSpirals);
            float armBaseAngle = armIndex * math.TAU / numSpirals;

            // Random position along the arm (radius)
            float t = innerRadius / outerRadius;
            float r = rngSequence.NextFloat() * (1.0f - t * t) + t * t;

            // Spiral offset based on radius (logarithmic spiral)
            float spiralAngle = math.log(r + 0.1f) * spiralTightness;

            // Random scatter within the arm
            float scatter = rngSequence.NextFloat(-0.01f, 0.01f);

            // Final angle = base arm angle + spiral + scatter
            float a = armBaseAngle + spiralAngle + scatter;

            // Convert polar to cartesian
            math.sincos(a, out float sin, out float cos);
            return new float3(cos, sin, 0f) * outerRadius * math.sqrt(r);
        }

        [BurstCompile]
        struct GenerateAccretionDiskJob : IJobParallelFor
        {
            [WriteOnly]
            public NativeArray<float3> points;

            public float innerRadius;
            public float outerRadius;
            public int   numSpirals;
            public float spiralTightness;
            public Rng   rng;

            public void Execute(int index)
            {
                var sequence = rng.GetSequence(index);
                points[index] = CalculateAccretionDiskPoint(
                    innerRadius,
                    outerRadius,
                    numSpirals,
                    spiralTightness,
                    ref sequence);
            }
        }
    }
}
