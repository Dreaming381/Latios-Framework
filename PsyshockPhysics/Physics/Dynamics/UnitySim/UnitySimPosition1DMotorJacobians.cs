using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        /// <summary>
        /// A struct which contains a solver-optimized form of a prismatic (position) motor along a single axis.
        /// </summary>
        public struct Position1DMotorJacobianParameters
        {
            public float3 jointPositionInInertialPoseASpace;
            public float3 jointPositionInInertialPoseBSpace;

            public float3 axisInB;

            public float3 targetInIntertialPoseBSpace;

            // Position error at the beginning of the step
            public float initialError;

            // Fraction of the position error to correct per step
            public float tau;

            // Fraction of the velocity error to correct per step
            public float damping;

            // Maximum impulse that can be applied to the motor before it caps out (not a breaking impulse)
            public float maxImpulseOfMotor;
        }

        /// <summary>
        /// Constructs a position motor along a single axis
        /// </summary>
        /// <param name="parameters">The resulting constraint data</param>
        /// <param name="inertialPoseWorldTransformA">The current world-space center of mass and inertia tensor diagonal orientation of the first body A</param>
        /// <param name="jointPositionInInertialPoseASpace">The inertial-pose relative position of the "joint" in A,
        /// which is the position on A used to measure distance between A and B</param>
        /// <param name="inertialPoseWorldTransformB">The current world-space center of mass and inertia tensor diagonal orientation of the second body B</param>
        /// <param name="jointTransformInInertialPoseBSpace">The inertial-pose relative position of the "joint" in B,
        /// which is the position on B used to measure distance between A and B, and the rotation which specifies the coordinate axes of the motor</param>
        /// <param name="targetDistance">The distance the motor aims to achieve between the joint positions</param>
        /// <param name="maxImpulse">The maximum impulse the motor may apply within the timestep</param>
        /// <param name="motorizedAxisIndex">The axis index (0 = x, 1 = y, 2 = z) based on B's joint rotation that the motor drives along</param>
        /// <param name="tau">The normalized stiffness factor</param>
        /// <param name="damping">The normalized damping factor</param>
        public static void BuildJacobian(out Position1DMotorJacobianParameters parameters,
                                         in RigidTransform inertialPoseWorldTransformA, float3 jointPositionInInertialPoseASpace,
                                         in RigidTransform inertialPoseWorldTransformB, in RigidTransform jointTransformInInertialPoseBSpace,
                                         float targetDistance, float maxImpulse, int motorizedAxisIndex, float tau, float damping)
        {
            parameters = default;

            parameters.tau                               = tau;
            parameters.damping                           = damping;
            parameters.jointPositionInInertialPoseASpace = jointPositionInInertialPoseASpace;
            parameters.jointPositionInInertialPoseBSpace = jointTransformInInertialPoseBSpace.pos;
            parameters.maxImpulseOfMotor                 = maxImpulse;
            parameters.axisInB                           = new float3x3(jointTransformInInertialPoseBSpace.rot)[motorizedAxisIndex];
            // (constraint.target[axis of movement] = target) where direction is relative to bodyB. Therefore add this to the pivot of bodyB to anchor the target
            parameters.targetInIntertialPoseBSpace = parameters.axisInB * targetDistance + parameters.jointPositionInInertialPoseBSpace;

            UpdateJacobian(ref parameters, in inertialPoseWorldTransformA, in inertialPoseWorldTransformB);
        }

        /// <summary>
        /// Updates a position motor with newly integrated inertial pose world transforms
        /// </summary>
        /// <param name="parameters">The constraint data</param>
        /// <param name="inertialPoseWorldTransformA">The new world-space center of mass and inertia tensor diagonal orientation of the first body</param>
        /// <param name="inertialPoseWorldTransformB">The new world-space center of mass and inertia tensor diagonal orientation of the second body</param>
        public static void UpdateJacobian(ref Position1DMotorJacobianParameters parameters,
                                          in RigidTransform inertialPoseWorldTransformA, in RigidTransform inertialPoseWorldTransformB)
        {
            // Calculate the initial distance between bodyA and bodyB in World-space
            parameters.initialError = CalculatePositionMotorError(in parameters, in inertialPoseWorldTransformA, in inertialPoseWorldTransformB, out _);
        }

        /// <summary>
        /// Solves the position motor for the pair of bodies
        /// </summary>
        /// <param name="velocityA">The velocity of the first body</param>
        /// <param name="inertialPoseWorldTransformA">The world-space center of mass and inertia tensor diagonal orientation of the first body</param>
        /// <param name="massA">The mass of the first body</param>
        /// <param name="velocityB">The velocity of the second body</param>
        /// <param name="inertialPoseWorldTransformB">The world-space center of mass and inertia tensor diagonal orientation of the second body</param>
        /// <param name="massB">The mass of the second body</param>
        /// <param name="parameters">The constraint data</param>
        /// <param name="deltaTime">The timestep over which this constraint is being solved</param>
        /// <param name="inverseDeltaTime">The reciprocal of deltaTime, should be: 1f / deltaTime</param>
        /// <returns>The world-space impulse applied to A. The world-space impulse applied to B is simply the negative of this value.</returns>
        public static float3 SolveJacobian(ref Velocity velocityA, in RigidTransform inertialPoseWorldTransformA, in Mass massA,
                                           ref Velocity velocityB, in RigidTransform inertialPoseWorldTransformB, in Mass massB,
                                           ref float3 accumulatedImpulse, in Position1DMotorJacobianParameters parameters, float deltaTime, float inverseDeltaTime)
        {
            var futureTransformA = IntegrateWithoutDamping(inertialPoseWorldTransformA, in velocityA, deltaTime);
            var futureTransformB = IntegrateWithoutDamping(inertialPoseWorldTransformB, in velocityB, deltaTime);

            // Calculate the angulars
            CalculateAngulars(parameters.jointPositionInInertialPoseASpace, futureTransformA.rot, out float3 angA0, out float3 angA1, out float3 angA2);
            CalculateAngulars(parameters.jointPositionInInertialPoseBSpace, futureTransformB.rot, out float3 angB0, out float3 angB1, out float3 angB2);

            // Calculate effective mass
            float3 effectiveMassDiag, effectiveMassOffDiag;
            {
                // Calculate the inverse effective mass matrix
                float3 invEffectiveMassDiag = new float3(
                    CalculateInvEffectiveMassDiag(angA0, massA.inverseInertia, massA.inverseMass, angB0, massB.inverseInertia, massB.inverseMass),
                    CalculateInvEffectiveMassDiag(angA1, massA.inverseInertia, massA.inverseMass, angB1, massB.inverseInertia, massB.inverseMass),
                    CalculateInvEffectiveMassDiag(angA2, massA.inverseInertia, massA.inverseMass, angB2, massB.inverseInertia, massB.inverseMass));

                float3 invEffectiveMassOffDiag = new float3(
                    CalculateInvEffectiveMassOffDiag(angA0, angA1, massA.inverseInertia, angB0, angB1, massB.inverseInertia),
                    CalculateInvEffectiveMassOffDiag(angA0, angA2, massA.inverseInertia, angB0, angB2, massB.inverseInertia),
                    CalculateInvEffectiveMassOffDiag(angA1, angA2, massA.inverseInertia, angB1, angB2, massB.inverseInertia));

                // Invert to get the effective mass matrix
                InvertSymmetricMatrix(invEffectiveMassDiag, invEffectiveMassOffDiag, out effectiveMassDiag, out effectiveMassOffDiag);
            }

            // Find the predicted direction and distance between bodyA and bodyB at the end of the step,
            // determine the difference between this and the initial difference, apply softening to this
            // impulse is what is required to move from the initial to the predicted (all in world space)
            float futureDistanceError = CalculatePositionMotorError(in parameters,
                                                                    futureTransformA,
                                                                    futureTransformB,
                                                                    out float3 futureDirection);
            float solveDistanceError = CalculateCorrection(futureDistanceError, parameters.initialError, parameters.tau, parameters.damping);

            // Calculate the impulse to correct the error
            float3   solveError    = solveDistanceError * futureDirection;
            float3x3 effectiveMass = BuildSymmetricMatrix(effectiveMassDiag, effectiveMassOffDiag);
            float3   impulse       = math.mul(effectiveMass, solveError) * inverseDeltaTime;
            impulse                = CapImpulse(impulse, ref accumulatedImpulse, parameters.maxImpulseOfMotor);

            // Apply the impulse
            ApplyImpulse(ref velocityA, in massA, impulse,  angA0, angA1, angA2);
            ApplyImpulse(ref velocityB, in massB, -impulse, angB0, angB1, angB2);
            return impulse;

            static void CalculateAngulars(float3 pivotInMotion, quaternion worldFromMotionRotation, out float3 ang0, out float3 ang1, out float3 ang2)
            {
                // Jacobian directions are i, j, k
                // Angulars are pivotInMotion x (motionFromWorld * direction)
                float3x3 motionFromWorldRotation = math.transpose(new float3x3(worldFromMotionRotation));
                ang0                             = math.cross(pivotInMotion, motionFromWorldRotation.c0);
                ang1                             = math.cross(pivotInMotion, motionFromWorldRotation.c1);
                ang2                             = math.cross(pivotInMotion, motionFromWorldRotation.c2);
            }

            static void ApplyImpulse(ref Velocity velocity, in Mass mass, in float3 impulse, in float3 ang0, in float3 ang1, in float3 ang2)
            {
                velocity.linear       += impulse * mass.inverseMass;
                float3 angularImpulse  = impulse.x * ang0 + impulse.y * ang1 + impulse.z * ang2;
                velocity.angular      += angularImpulse * mass.inverseInertia;
            }
        }

        // Given two bodies (in world space), determine the direction (in world space) that the impulse needs to act on
        // and the distance remaining between the bodyA and the target
        static float CalculatePositionMotorError(in Position1DMotorJacobianParameters parameters,
                                                 in RigidTransform inertialPoseWorldTransformA,
                                                 in RigidTransform inertialPoseWorldTransformB,
                                                 out float3 directionInWorld)
        {
            // Find the direction from pivot A to B and the distance between them
            float3 anchorAInWorld = math.transform(inertialPoseWorldTransformA, parameters.jointPositionInInertialPoseASpace);
            float3 targetInWorld  = math.transform(inertialPoseWorldTransformB, parameters.jointPositionInInertialPoseBSpace);
            float3 axisInWorld    = math.mul(inertialPoseWorldTransformB.rot, parameters.axisInB);

            var   toTargetOffset = targetInWorld - anchorAInWorld;
            float distance       = math.dot(toTargetOffset, axisInWorld);

            directionInWorld = -axisInWorld;

            return distance;
        }
    }
}

