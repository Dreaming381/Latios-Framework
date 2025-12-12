using Unity.Mathematics;

namespace Latios.Calci
{
    public static partial class PointDistributions
    {
        /// <summary>
        /// Generates the next point in an accretion disk distribution.
        /// Points are arranged in spiral arms radiating from the center, with random scatter.
        /// All points will lie on the XY plane (Z = 0).
        /// </summary>
        /// <param name="rngSequence">RNG sequence for generating random values. Modified by this call.</param>
        /// <param name="innerRadius">Inner radius of the disk.</param>
        /// <param name="outerRadius">Outer radius of the disk.</param>
        /// <param name="numSpirals">Number of spiral arms.</param>
        /// <param name="spiralTightness">How tightly the spirals wind. Typical values: 0.5 to 3.0.</param>
        /// <returns>A point on the XY plane (Z = 0) in the accretion disk pattern.</returns>
        public static float3 NextAccretionDiskPoint(
            ref this Rng.RngSequence rngSequence,
            float innerRadius,
            float outerRadius,
            int numSpirals,
            float spiralTightness)
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
    }
}
