using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
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

        public static int2 ConvertRotation2DJacobianFreeRotationIndexToImpulseIndices(int freeIndex) => (freeIndex + new int2(1, 2)) % 3;

        // Returns the impulse applied only to the angular velocity for the constrained axes, whose indices can be obtained from the above method.
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

