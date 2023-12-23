using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class XpbdSim
    {
        /// <summary>
        /// Solves a distance constraint between two point masses
        /// </summary>
        /// <param name="positionA">The location of the first point mass</param>
        /// <param name="inverseMassA">The inverse mass of the first point mass such that 0 corresponds to infinite mass (stuck in place)</param>
        /// <param name="positionB">The location of the second point mass</param>
        /// <param name="inverseMassB">The inverse mass of the second point mass such that 0 corresponds to infinite mass (stuck in place)</param>
        /// <param name="targetDistance">The desired distance between the point masses that should cause the point masses to move towards or away from each other to try to achieve</param>
        /// <param name="compliance">Physically-based inverse of stiffness, where larger values result in less adherence to the constraint</param>
        /// <param name="inverseSubstepDeltaTimeSquared">The squared inverse of the simulation step time used for all XPBD constraint solvers</param>
        /// <returns>Two position delta values in a matrix, with c0 corresponding to the first point mass, and c1 corresponding to the second</returns>
        public static float3x2 SolveDistanceConstraint(float3 positionA,
                                                       float inverseMassA,
                                                       float3 positionB,
                                                       float inverseMassB,
                                                       float targetDistance,
                                                       float compliance,
                                                       float inverseSubstepDeltaTimeSquared)
        {
            var constraint = math.distance(positionA, positionB) - targetDistance;
            var lagrange   = -constraint / (inverseMassA + inverseMassB + compliance * inverseSubstepDeltaTimeSquared);
            if (Hint.Unlikely(math.isinf(lagrange)))
                return default;
            var abDir  = math.normalizesafe(positionB - positionA, float3.zero);
            var deltaA = lagrange * inverseMassA * -abDir;
            var deltaB = lagrange * inverseMassB * abDir;
            return new float3x2(deltaA, deltaB);
        }

        /// <summary>
        /// Solves a volume constraint between four point masses arranged to form a tetrahedron
        /// </summary>
        /// <param name="tetrehedralPositions">The four point mass positions, arranged identical to their original ordering</param>
        /// <param name="inverseMasses">The corresponding inverse masses of the four point masses, such that a 0 corresponds to infinite mass (stuck in place)</param>
        /// <param name="targetVolumeShape">A tetrahedron whose volume matches the target volume and whose winding orders of each surface triangle matches
        /// the desired winding order of the triangles for the four point masses.</param>
        /// <param name="compliance">Physically-based inverse of stiffness, where larger values result in less adherence to the constraint</param>
        /// <param name="inverseSubstepDeltaTimeSquared">The squared inverse of the simulation step time used for all XPBD constraint solvers</param>
        /// <returns>Four position delta values for the four point masses</returns>
        public static simdFloat3 SolveVolumeConstraint(simdFloat3 tetrehedralPositions,
                                                       float4 inverseMasses,
                                                       simdFloat3 targetVolumeShape,
                                                       float compliance,
                                                       float inverseSubstepDeltaTimeSquared)
        {
            var targetEdges     = targetVolumeShape - targetVolumeShape.a;
            var targetVolume    = math.dot(math.cross(targetEdges.b, targetEdges.c), targetEdges.d);
            var sign            = math.select(1f, -1f, targetVolume < 0f);
            targetVolume       *= sign;
            var currentEdges    = tetrehedralPositions - tetrehedralPositions.a;
            var bRelativeEdges  = tetrehedralPositions - tetrehedralPositions.b;
            var gradientLhs     = currentEdges.acdb;
            gradientLhs.a       = bRelativeEdges.d;
            var gradientRhs     = currentEdges.adbc;
            gradientRhs.a       = bRelativeEdges.c;
            var gradients       = sign * simd.cross(gradientLhs, gradientRhs);
            var currentVolume   = math.dot(gradients.d, gradientRhs.b);
            var constraint      = currentVolume - targetVolume;  // Values are premultiplied by 6
            var lagrange        = -constraint / (math.dot(simd.lengthsq(gradients), inverseMasses) + compliance * inverseSubstepDeltaTimeSquared);
            lagrange            = math.select(lagrange, 0f, !math.isfinite(lagrange));
            return (lagrange * inverseMasses) * gradients;
        }
    }
}

