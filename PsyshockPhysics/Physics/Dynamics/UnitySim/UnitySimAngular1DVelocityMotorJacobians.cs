using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        public struct Angular1DVelocityMotorJacobianParameters
        {
            // Relative orientation of the motions before solving. Needs to be updated at start of each substep
            public quaternion inertialRotationAInInertialPoseBSpace;

            // Rotation axis in motion A space
            public float3 axisInInertialPoseASpace;

            public float target;  // in rad/s

            // Index of the limited axis
            public int axisIndex;

            // Maximum impulse that can be applied to the motor before it caps out (not a breaking impulse)
            public float maxImpulseOfMotor;

            // Fraction of the velocity error to correct per step
            public float damping;
        }

        /// <summary>
        /// Constructs a 1D angular velocity motor constraint
        /// </summary>
        /// <param name="parameters">The resulting constraint data</param>
        /// <param name="inertialPoseWorldRotationA">The current world rotation of the inertia tensor diagonal of the first body A</param>
        /// <param name="jointRotationInInertialPoseASpace">The inertial-pose relative rotation of the "joint" in A,
        /// which when the constraint is in the rest pose, the world-space version of rotation should match the world-space counterpart in B</param>
        /// <param name="inertialPoseWorldRotationB">The current world rotation of the inertia tensor diagonal of the second body B</param>
        /// <param name="target">The target angular velocity between the bodies around the axis, in rad/s</param>
        /// <param name="maxImpulse">The maximum impulse the motor may apply within the timestep</param>
        /// <param name="damping">The normalized damping factor</param>
        /// <param name="axisIndex">The axis within the joint that is motorized</param>
        public static void BuildJacobian(out Angular1DVelocityMotorJacobianParameters parameters,
                                         quaternion inertialPoseWorldRotationA, quaternion jointRotationInInertialPoseASpace, quaternion inertialPoseWorldRotationB,
                                         float target, float maxImpulse, float damping, int axisIndex)
        {
            parameters = new Angular1DVelocityMotorJacobianParameters
            {
                inertialRotationAInInertialPoseBSpace = math.normalize(math.InverseRotateFast(inertialPoseWorldRotationB, inertialPoseWorldRotationA)),
                axisIndex                             = axisIndex,
                axisInInertialPoseASpace              = new float3x3(jointRotationInInertialPoseASpace)[axisIndex],
                target                                = target,
                damping                               = damping,
                maxImpulseOfMotor                     = maxImpulse
            };
        }

        /// <summary>
        /// Updates the 1D angular velocity motor constraint with newly integrated inertial pose world rotations
        /// </summary>
        /// <param name="parameters">The constraint data</param>
        /// <param name="inertialPoseWorldRotationA">The new world-space orientation of the first body's inertia tensor diagonal</param>
        /// <param name="inertialPoseWorldRotationB">The new world-space orientation of the second body's inertia tensor diagonal</param>
        public static void UpdateJacobian(ref Angular1DVelocityMotorJacobianParameters parameters,
                                          quaternion inertialPoseWorldRotationA, quaternion inertialPoseWorldRotationB)
        {
            parameters.inertialRotationAInInertialPoseBSpace = math.normalize(math.InverseRotateFast(inertialPoseWorldRotationB, inertialPoseWorldRotationA));
        }

        /// <summary>
        /// Solves the 1D angular velocity motor constraint for the pair of bodies
        /// </summary>
        /// <param name="velocityA">The velocity of the first body</param>
        /// <param name="massA">The mass of the first body</param>
        /// <param name="velocityB">The velocity of the second body</param>
        /// <param name="massB">The mass of the second body</param>
        /// <param name="accumulatedImpulse">The impulse of the motor accumulated over the timestep. Set to zero for the first iteration of the solver.</param>
        /// <param name="parameters">The constraint data</param>
        /// <param name="deltaTime">The timestep over which this constraint is being solved</param>
        /// <param name="inverseDeltaTime">The reciprocal of deltaTime, should be: 1f / deltaTime</param>
        /// <returns>The scalar impulse applied onlt to the angular velocity for the constrained axis</returns>
        public static void SolveJacobian(ref Velocity velocityA, in Mass massA, ref Velocity velocityB, in Mass massB,
                                         ref float accumulatedImpulse, in Angular1DVelocityMotorJacobianParameters parameters,
                                         float deltaTime, float inverseDeltaTime)
        {
            // Predict the relative orientation at the end of the step
            quaternion futureMotionBFromA = IntegrateOrientationBFromA(parameters.inertialRotationAInInertialPoseBSpace, velocityA.angular, velocityB.angular, deltaTime);

            // Calculate the effective mass
            // Todo: Unity flips the sign of axisInIertialPoseASpace in Rotation1D but not here. Why?
            float3 axisInMotionB = math.mul(futureMotionBFromA, parameters.axisInInertialPoseASpace);
            float  effectiveMass;
            {
                float invEffectiveMass = math.csum(parameters.axisInInertialPoseASpace * parameters.axisInInertialPoseASpace * massA.inverseInertia +
                                                   axisInMotionB * axisInMotionB * massB.inverseInertia);
                effectiveMass = math.select(1.0f / invEffectiveMass, 0.0f, invEffectiveMass == 0.0f);
            }

            // Compute the current relative angular velocity between the two bodies about the rotation axis
            var relativeVelocity = math.dot(velocityA.angular, parameters.axisInInertialPoseASpace) -
                                   math.dot(velocityB.angular, axisInMotionB);

            // Compute the error between the target relative velocity and the current relative velocity
            var   velocityError      = parameters.target - relativeVelocity;
            float velocityCorrection = velocityError * parameters.damping;

            float impulse = math.mul(effectiveMass, velocityCorrection);
            impulse       = CapImpulse(impulse, ref accumulatedImpulse, parameters.maxImpulseOfMotor);

            velocityA.angular += impulse * parameters.axisInInertialPoseASpace * massA.inverseInertia;
            velocityB.angular += impulse * axisInMotionB * massB.inverseInertia;
        }
    }
}

