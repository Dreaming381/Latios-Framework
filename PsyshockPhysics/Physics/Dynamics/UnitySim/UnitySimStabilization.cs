using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        public const float kDefaultVelocityClippingFactor = 1f;
        public const float kDefaultInertialScalingFactor  = 1f;

        /// <summary>
        /// Per-body stabilization state
        /// </summary>
        public struct MotionStabilizer
        {
            public Velocity inputVelocity;
            public float    inverseInertiaScale;

            public static readonly MotionStabilizer kDefault = new MotionStabilizer
            {
                inputVelocity       = default,
                inverseInertiaScale = 1.0f
            };
        }

        /// <summary>
        /// Returns true if the other body potentially in contact is considered significant and should be included in the stabilization count.
        /// </summary>
        /// <param name="inverseMass">This body's inverse mass, typically from the Mass struct</param>
        /// <param name="otherBodyInverseMass">The other body's inverse mass</param>
        /// <returns>True if the other body should be counted, false otherwise</returns>
        public static bool IsStabilizerSignificantBody(float inverseMass, float otherBodyInverseMass) => otherBodyInverseMass * 0.5f <= inverseMass;

        /// <summary>
        /// Updates the motion stabilizer and the velocity of a body after performing a full solver iteration over all bodies
        /// </summary>
        /// <param name="motionStabilizer">The motion stabilizer for the body</param>
        /// <param name="velocity">The velocity of the body</param>
        /// <param name="inverseMass">The inverse mass of the body, typically from the Mass struct</param>
        /// <param name="angularExpansionFactor">The angular expansion factor of the body</param>
        /// <param name="numOtherSignificantBodiesInContact">The number of other bodies that are potentially in contact with this body (number involved in contact jacobian solves)
        /// that this stabilizer cares about</param>
        /// <param name="timestepScaledGravity">Gravity applied to this body multiplied by the timestep</param>
        /// <param name="gravityDirection">The normalized gravity vector, or float3.zero if no gravity</param>
        /// <param name="stabilizationVelocityClippingFactor">A heuristic factor [0, 5] for when to clip velocity to zero. Higher is more aggressive clipping. Typically 1f</param>
        /// <param name="stabilizationInertiaScalingFactor">A heuristic factor [0, 5] for when to scale inertia to reduce motion. Higher is more aggressive scaling. Typically 1f</param>
        /// <param name="isFirstIteration">True if this is the first time being called this simulation update.</param>
        public static void UpdateStabilizationAfterSolverIteration(ref MotionStabilizer motionStabilizer,
                                                                   ref Velocity velocity,
                                                                   float inverseMass,
                                                                   float angularExpansionFactor,
                                                                   int numOtherSignificantBodiesInContact,
                                                                   float3 timestepScaledGravity,
                                                                   float3 gravityDirection,
                                                                   float stabilizationVelocityClippingFactor,
                                                                   float stabilizationInertiaScalingFactor,
                                                                   bool isFirstIteration)
        {
            if (numOtherSignificantBodiesInContact <= 0 || inverseMass == 0f)
                return;

            // Scale up inertia for other iterations
            if (isFirstIteration && numOtherSignificantBodiesInContact > 1)
            {
                float inertiaScale                   = 1.0f + 0.2f * (numOtherSignificantBodiesInContact - 1) * stabilizationInertiaScalingFactor;
                motionStabilizer.inverseInertiaScale = math.rcp(inertiaScale);
            }

            // Don't stabilize velocity component along the gravity vector
            float3 linVelVertical = math.dot(velocity.linear, gravityDirection) * gravityDirection;
            float3 linVelSideways = velocity.linear - linVelVertical;

            // Choose a very small gravity coefficient for clipping threshold
            float gravityCoefficient = (numOtherSignificantBodiesInContact == 1 ? 0.1f : 0.25f) * stabilizationVelocityClippingFactor;

            // Linear velocity threshold
            float smallLinVelThresholdSq = math.lengthsq(timestepScaledGravity * gravityCoefficient);

            // Stabilize the velocities
            if (math.lengthsq(linVelSideways) < smallLinVelThresholdSq)
            {
                velocity.linear = linVelVertical;

                // Only clip angular if in contact with at least 2 bodies
                if (numOtherSignificantBodiesInContact > 1)
                {
                    // Angular velocity threshold
                    if (angularExpansionFactor > 0.0f)
                    {
                        float angularFactorSq        = math.rcp(angularExpansionFactor * angularExpansionFactor) * 0.01f;
                        float smallAngVelThresholdSq = smallLinVelThresholdSq * angularFactorSq;
                        if (math.lengthsq(velocity.angular) < smallAngVelThresholdSq)
                        {
                            velocity.angular = float3.zero;
                        }
                    }
                }
            }
        }
    }
}

