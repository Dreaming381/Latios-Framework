using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        /// <summary>
        /// A struct which contains a solver-optimized form of a 3D rotation constraint.
        /// </summary>
        public struct Rotation3DConstraintJacobianParameters
        {
            public quaternion inertialPoseAInInertialPoseBSpace;
            public quaternion jointOrientationBindFrame;

            public float minAngle;
            public float maxAngle;

            public float initialError;
            public float tau;
            public float damping;
        }

        /// <summary>
        /// Constructs a 3D rotaton constraint
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
        public static void BuildJacobian(out Rotation3DConstraintJacobianParameters parameters,
                                         quaternion inertialPoseWorldRotationA, quaternion jointRotationInInertialPoseASpace,
                                         quaternion inertialPoseWorldRotationB, quaternion jointRotationInInertialPoseBSpace,
                                         float minAngle, float maxAngle, float tau, float damping)
        {
            parameters = new Rotation3DConstraintJacobianParameters
            {
                inertialPoseAInInertialPoseBSpace = math.normalize(math.InverseRotateFast(inertialPoseWorldRotationB, inertialPoseWorldRotationA)),
                jointOrientationBindFrame         = math.inverse(math.mul(jointRotationInInertialPoseBSpace, jointRotationInInertialPoseASpace)),
                minAngle                          = minAngle,
                maxAngle                          = maxAngle,
                tau                               = tau,
                damping                           = damping,
            };
            // Calculate the initial error
            {
                quaternion jointOrientation = math.mul(parameters.jointOrientationBindFrame, parameters.inertialPoseAInInertialPoseBSpace);
                float      initialAngle     = math.asin(math.length(jointOrientation.value.xyz)) * 2.0f;
                parameters.initialError     = CalculateError(initialAngle, parameters.minAngle, parameters.maxAngle);
            }
        }

        /// <summary>
        /// Solves the 3D rotation constraint for the pair of bodies
        /// </summary>
        /// <param name="velocityA">The velocity of the first body</param>
        /// <param name="massA">The mass of the first body</param>
        /// <param name="velocityB">The velocity of the second body</param>
        /// <param name="massB">The mass of the second body</param>
        /// <param name="parameters">The constraint data</param>
        /// <param name="deltaTime">The timestep over which this constraint is being solved</param>
        /// <param name="inverseDeltaTime">The reciprocal of deltaTime, should be: 1f / deltaTime</param>
        /// <returns>The scalar impulses applied only to the angular velocity for each of the three rotational axes</returns>
        public static float3 SolveJacobian(ref Velocity velocityA, in Mass massA, ref Velocity velocityB, in Mass massB,
                                           in Rotation3DConstraintJacobianParameters parameters, float deltaTime, float inverseDeltaTime)
        {
            // Predict the relative orientation at the end of the step
            quaternion futureBFromA = IntegrateOrientationBFromA(parameters.inertialPoseAInInertialPoseBSpace, velocityA.angular, velocityB.angular, deltaTime);

            // Find the future axis and angle of rotation between the free axes
            float3     jacA0, jacA1, jacA2, jacB0, jacB1, jacB2;
            quaternion jointOrientation;
            float3     effectiveMass;  // first column of 3x3 effective mass matrix, don't need the others because only jac0 can have nonzero error
            float      futureAngle;
            {
                // Calculate the relative rotation between joint spaces
                jointOrientation = math.mul(parameters.jointOrientationBindFrame, futureBFromA);

                // Find the axis and angle of rotation
                jacA0                 = jointOrientation.value.xyz;
                float sinHalfAngleSq  = math.lengthsq(jacA0);
                float invSinHalfAngle = RSqrtSafe(sinHalfAngleSq);
                float sinHalfAngle    = sinHalfAngleSq * invSinHalfAngle;
                futureAngle           = math.asin(sinHalfAngle) * 2.0f;

                // jacA0: triple-axis defined by rotation (jointOrientation).
                // jacA1: triple-axis perpendicular to jacA0
                // jacA2: triple-axis perpendicular to BOTH jacA0 AND jacA1
                //    None of these axes are axis-aligned (ie: to the x,y,z triple-axis)
                jacA0 = math.select(jacA0 * invSinHalfAngle, new float3(1, 0, 0), invSinHalfAngle == 0.0f);
                jacA0 = math.select(jacA0, -jacA0, jointOrientation.value.w < 0.0f);  // determines rotation direction
                mathex.GetDualPerpendicularNormalized(jacA0, out jacA1, out jacA2);

                //jacB are the same axes but from Body B's reference frame (ie: negative jacA)
                jacB0 = math.mul(futureBFromA, -jacA0);
                jacB1 = math.mul(futureBFromA, -jacA1);
                jacB2 = math.mul(futureBFromA, -jacA2);

                // A0 * A0  ,  A0 * A1  ,  A0 * A2
                //          ,  A1 * A1  ,  A1 * A2
                //          ,           ,  A2 * A2

                // All forces applied that are axis-aligned have a directly additive effect: diagonal elements
                // All other forces (off-diagonal elements) have are component forces
                //      ie: if you have a xy-plane and you are applying a force relative to the x-axis at 30degrees
                //      Then you are applying force:
                //              in the x-direction: cos(30) * force
                //              in the y-direction: sin(30) * force
                //      The off-diagonal elements are analogous to this force breakdown from the perspective of different
                //      reference axes. So A1 * A2 would be the relative forces between y and z
                // A check: adding all x-component forces should add to the magnitude of x

                // Calculate the effective mass
                float3 invEffectiveMassDiag = new float3(
                    math.csum(jacA0 * jacA0 * massA.inverseInertia + jacB0 * jacB0 * massB.inverseInertia),
                    math.csum(jacA1 * jacA1 * massA.inverseInertia + jacB1 * jacB1 * massB.inverseInertia),
                    math.csum(jacA2 * jacA2 * massA.inverseInertia + jacB2 * jacB2 * massB.inverseInertia));
                float3 invEffectiveMassOffDiag = new float3(
                    math.csum(jacA0 * jacA1 * massA.inverseInertia + jacB0 * jacB1 * massB.inverseInertia),
                    math.csum(jacA0 * jacA2 * massA.inverseInertia + jacB0 * jacB2 * massB.inverseInertia),
                    math.csum(jacA1 * jacA2 * massA.inverseInertia + jacB1 * jacB2 * massB.inverseInertia));

                InvertSymmetricMatrix(invEffectiveMassDiag, invEffectiveMassOffDiag, out float3 effectiveMassDiag, out float3 effectiveMassOffDiag);

                effectiveMass = BuildSymmetricMatrix(effectiveMassDiag, effectiveMassOffDiag).c0;
                // effectiveMass is column0 of matrix: [diag.x, offdiag.x, offdiag.y]
            }

            // Calculate the error, adjust by tau and damping, and apply an impulse to correct it
            // The errors (initial/future/solve) are floats because they are relative to the jac0 rotation frame
            float  futureError    = CalculateError(futureAngle, parameters.minAngle, parameters.maxAngle);
            float  solveError     = CalculateCorrection(futureError, parameters.initialError, parameters.tau, parameters.damping);
            float  solveVelocity  = -solveError * inverseDeltaTime;
            float3 impulseA       = solveVelocity * (jacA0 * effectiveMass.x + jacA1 * effectiveMass.y + jacA2 * effectiveMass.z);
            float3 impulseB       = solveVelocity * (jacB0 * effectiveMass.x + jacB1 * effectiveMass.y + jacB2 * effectiveMass.z);
            velocityA.angular    += impulseA * massA.inverseInertia;
            velocityB.angular    += impulseB * massB.inverseInertia;

            return solveVelocity * jointOrientation.value.xyz;
        }
    }
}

