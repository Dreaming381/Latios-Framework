using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        /// <summary>
        /// A struct which contains a solver-optimized form of a 2D rotation constraint.
        /// </summary>
        public struct Rotation2DConstraintJacobianParameters
        {
            public quaternion inertialPoseAInInertialPoseBSpace;

            public float3 axisAInInertialPoseASpace;
            public float3 axisBInInertialPoseBSpace;

            public float minAngle;
            public float maxAngle;

            public float initialError;
            public float tau;
            public float damping;
        }

        /// <summary>
        /// Constructs a 2D rotaton constraint
        /// </summary>
        /// <param name="parameters">The resulting constraint data</param>
        /// <param name="inertialPoseWorldRotationA">The current world rotation of the inertia tensor diagonal of the first body A</param>
        /// <param name="jointRotationInInertialPoseASpace">The inertial-pose relative rotation of the "joint" in A,
        /// which when the constraint is in the rest pose, the world-space version of rotation should match the world-space counterpart in B</param>
        /// <param name="inertialPoseWorldRotationB">The current world rotation of the inertia tensor diagonal of the second body B</param>
        /// <param name="jointRotationInInertialPoseBSpace">The inertial-pose relative rotation of the "joint" in B,
        /// which when the constraint is in the rest pose, the world-space version of rotation should match the world-space counterpart in A</param>
        /// <param name="minAngle">The minimum angle allowed in the range of [-2*pi, 2*pi]</param>
        /// <param name="maxAngle">The maximum angle allowed in the range of [-2*pi, 2*pi]</param>
        /// <param name="tau">The normalized stiffness factor</param>
        /// <param name="damping">The normalized damping factor</param>
        /// <param name="freeAxisIndex">The axis within the joint that is unconstrained</param>
        public static void BuildJacobian(out Rotation2DConstraintJacobianParameters parameters,
                                         quaternion inertialPoseWorldRotationA, quaternion jointRotationInInertialPoseASpace,
                                         quaternion inertialPoseWorldRotationB, quaternion jointRotationInInertialPoseBSpace,
                                         float minAngle, float maxAngle, float tau, float damping, int freeAxisIndex)
        {
            parameters = new Rotation2DConstraintJacobianParameters
            {
                inertialPoseAInInertialPoseBSpace = math.normalize(math.InverseRotateFast(inertialPoseWorldRotationB, inertialPoseWorldRotationA)),
                axisAInInertialPoseASpace         = new float3x3(jointRotationInInertialPoseASpace)[freeAxisIndex],
                axisBInInertialPoseBSpace         = new float3x3(jointRotationInInertialPoseBSpace)[freeAxisIndex],
                minAngle                          = minAngle,
                maxAngle                          = maxAngle,
                tau                               = tau,
                damping                           = damping,
            };
            // Calculate the initial error
            {
                float3 axisAinB         = math.mul(parameters.inertialPoseAInInertialPoseBSpace, parameters.axisAInInertialPoseASpace);
                float  sinAngle         = math.length(math.cross(axisAinB, parameters.axisBInInertialPoseBSpace));
                float  cosAngle         = math.dot(axisAinB, parameters.axisBInInertialPoseBSpace);
                float  angle            = math.atan2(sinAngle, cosAngle);
                parameters.initialError = CalculateError(angle, parameters.minAngle, parameters.maxAngle);
            }
        }

        /// <summary>
        /// Used to determine the indices within a float3 angular velocity that a pair of impulses apply to from a 2D rotation constraint given a free axis
        /// </summary>
        /// <param name="freeIndex">The free unconstrained axis index</param>
        /// <returns>A pair of values in the range [0, 2] each that specify the ordinate index corresponding to an impulse.</returns>
        public static int2 ConvertRotation2DJacobianFreeRotationIndexToImpulseIndices(int freeIndex) => (freeIndex + new int2(1, 2)) % 3;

        /// <summary>
        /// Solves the 2D rotation constraint for the pair of bodies
        /// </summary>
        /// <param name="velocityA">The velocity of the first body</param>
        /// <param name="massA">The mass of the first body</param>
        /// <param name="velocityB">The velocity of the second body</param>
        /// <param name="massB">The mass of the second body</param>
        /// <param name="parameters">The constraint data</param>
        /// <param name="deltaTime">The timestep over which this constraint is being solved</param>
        /// <param name="inverseDeltaTime">The reciprocal of deltaTime, should be: 1f / deltaTime</param>
        /// <returns>The scalar impulses applied only to the angular velocity for each of the constrained axes, whose ordinates can be determined via
        /// ConvertRotation2DJacobianFreeRotationIndexToImpulseIndices()</returns>
        public static float2 SolveJacobian(ref Velocity velocityA, in Mass massA, ref Velocity velocityB, in Mass massB,
                                           in Rotation2DConstraintJacobianParameters parameters, float deltaTime, float inverseDeltaTime)
        {
            // Predict the relative orientation at the end of the step
            quaternion futureBFromA = IntegrateOrientationBFromA(parameters.inertialPoseAInInertialPoseBSpace, velocityA.angular, velocityB.angular, deltaTime);

            // Calculate the jacobian axis and angle
            float3 axisAinB     = math.mul(futureBFromA, parameters.axisAInInertialPoseASpace);
            float3 jacB0        = math.cross(axisAinB, parameters.axisBInInertialPoseBSpace);
            float3 jacA0        = math.mul(math.inverse(futureBFromA), -jacB0);
            float  jacLengthSq  = math.lengthsq(jacB0);
            float  invJacLength = RSqrtSafe(jacLengthSq);
            float  futureAngle;
            {
                float sinAngle = jacLengthSq * invJacLength;
                float cosAngle = math.dot(axisAinB, parameters.axisBInInertialPoseBSpace);
                futureAngle    = math.atan2(sinAngle, cosAngle);
            }

            // Choose a second jacobian axis perpendicular to A
            float3 jacB1 = math.cross(jacB0, axisAinB);
            float3 jacA1 = math.mul(math.inverse(futureBFromA), -jacB1);

            // Calculate effective mass
            float2 effectiveMass;  // First column of the 2x2 matrix, we don't need the second column because the second component of error is zero
            {
                // Calculate the inverse effective mass matrix, then invert it
                float invEffMassDiag0   = math.csum(jacA0 * jacA0 * massA.inverseInertia + jacB0 * jacB0 * massB.inverseInertia);
                float invEffMassDiag1   = math.csum(jacA1 * jacA1 * massA.inverseInertia + jacB1 * jacB1 * massB.inverseInertia);
                float invEffMassOffDiag = math.csum(jacA0 * jacA1 * massA.inverseInertia + jacB0 * jacB1 * massB.inverseInertia);
                float det               = invEffMassDiag0 * invEffMassDiag1 - invEffMassOffDiag * invEffMassOffDiag;
                float invDet            = math.select(jacLengthSq / det, 0.0f, det == 0.0f);  // scale by jacLengthSq because the jacs were not normalized
                effectiveMass           = invDet * new float2(invEffMassDiag1, -invEffMassOffDiag);
            }

            // Normalize the jacobians
            jacA0 *= invJacLength;
            jacB0 *= invJacLength;
            jacA1 *= invJacLength;
            jacB1 *= invJacLength;

            // Calculate the error, adjust by tau and damping, and apply an impulse to correct it
            float  futureError  = CalculateError(futureAngle, parameters.minAngle, parameters.maxAngle);
            float  solveError   = CalculateCorrection(futureError, parameters.initialError, parameters.tau, parameters.damping);
            float2 impulse      = -effectiveMass * solveError * inverseDeltaTime;
            velocityA.angular  += massA.inverseInertia * (impulse.x * jacA0 + impulse.y * jacA1);
            velocityB.angular  += massB.inverseInertia * (impulse.x * jacB0 + impulse.y * jacB1);

            return impulse;
        }
    }
}

