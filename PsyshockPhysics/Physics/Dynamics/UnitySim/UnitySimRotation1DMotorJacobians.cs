using Unity.Mathematics;
using UnityEngine;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        /// <summary>
        /// A struct which contains a solver-optimized form of a 1D rotation motor.
        /// </summary>
        public struct Rotation1DMotorJacobianParameters
        {
            public quaternion inertialRotationAInInertialPoseBSpace;
            public quaternion jointRotationInInertialPoseASpace;
            public quaternion jointRotationInInertialPoseBSpace;

            // Limited axis in motion A space
            public float3 axisInInertialPoseASpace;
            public float  target;

            public float minAngle;
            public float maxAngle;

            public float initialError;
            public float tau;
            public float damping;

            // Maximum impulse that can be applied to the motor before it caps out (not a breaking impulse)
            public float maxImpulseOfMotor;

            public int axisIndex;
        }

        /// <summary>
        /// Constructs a 1D rotaton motor
        /// </summary>
        /// <param name="parameters">The resulting constraint data</param>
        /// <param name="inertialPoseWorldRotationA">The current world rotation of the inertia tensor diagonal of the first body A</param>
        /// <param name="jointRotationInInertialPoseASpace">The inertial-pose relative rotation of the "joint" in A,
        /// which when the constraint is in the rest pose, the world-space version of rotation should match the world-space counterpart in B</param>
        /// <param name="inertialPoseWorldRotationB">The current world rotation of the inertia tensor diagonal of the second body B</param>
        /// <param name="jointRotationInInertialPoseBSpace">The inertial-pose relative rotation of the "joint" in B,
        /// which when the constraint is in the rest pose, the world-space version of rotation should match the world-space counterpart in A</param>
        /// <param name="target">The target angle the motor should drive to achieve</param>
        /// <param name="maxImpulse">The maximum impulse the motor may apply within the timestpe</param>
        /// <param name="minAngle">The minimum angle allowed in the range of [-2*pi, 2*pi]</param>
        /// <param name="maxAngle">The maximum angle allowed in the range of [-2*pi, 2*pi]</param>
        /// <param name="tau">The normalized stiffness factor</param>
        /// <param name="damping">The normalized damping factor</param>
        /// <param name="axisIndex">The axis within the joint that is constrained</param>
        public static void BuildJacobian(out Rotation1DMotorJacobianParameters parameters,
                                         quaternion inertialPoseWorldRotationA, quaternion jointRotationInInertialPoseASpace,
                                         quaternion inertialPoseWorldRotationB, quaternion jointRotationInInertialPoseBSpace,
                                         float target, float maxImpulse, float minAngle, float maxAngle, float tau, float damping, int axisIndex)
        {
            parameters = new Rotation1DMotorJacobianParameters
            {
                inertialRotationAInInertialPoseBSpace = math.normalize(math.InverseRotateFast(inertialPoseWorldRotationB, inertialPoseWorldRotationA)),
                jointRotationInInertialPoseASpace     = jointRotationInInertialPoseASpace,
                jointRotationInInertialPoseBSpace     = jointRotationInInertialPoseBSpace,
                axisInInertialPoseASpace              = new float3x3(jointRotationInInertialPoseASpace)[axisIndex],
                target                                = target,
                minAngle                              = minAngle,
                maxAngle                              = maxAngle,
                tau                                   = tau,
                damping                               = damping,
                maxImpulseOfMotor                     = maxImpulse,
                axisIndex                             = axisIndex
            };
            parameters.initialError = CalculateRotation1DMotorError(in parameters, parameters.inertialRotationAInInertialPoseBSpace, out _);
        }

        /// <summary>
        /// Updates the 1D rotation motor with newly integrated inertial pose world rotations
        /// </summary>
        /// <param name="parameters">The constraint data</param>
        /// <param name="inertialPoseWorldRotationA">The new world-space orientation of the first body's inertia tensor diagonal</param>
        /// <param name="inertialPoseWorldRotationB">The new world-space orientation of the second body's inertia tensor diagonal</param>
        public static void UpdateJacobian(ref Rotation1DMotorJacobianParameters parameters,
                                          quaternion inertialPoseWorldRotationA, quaternion inertialPoseWorldRotationB)
        {
            parameters.inertialRotationAInInertialPoseBSpace = math.normalize(math.InverseRotateFast(inertialPoseWorldRotationB, inertialPoseWorldRotationA));
            parameters.initialError                          = CalculateRotation1DMotorError(in parameters, parameters.inertialRotationAInInertialPoseBSpace, out _);
        }

        /// <summary>
        /// Solves the 1D rotation motor for the pair of bodies
        /// </summary>
        /// <param name="velocityA">The velocity of the first body</param>
        /// <param name="massA">The mass of the first body</param>
        /// <param name="velocityB">The velocity of the second body</param>
        /// <param name="massB">The mass of the second body</param>
        /// <param name="parameters">The constraint data</param>
        /// <param name="deltaTime">The timestep over which this constraint is being solved</param>
        /// <param name="inverseDeltaTime">The reciprocal of deltaTime, should be: 1f / deltaTime</param>
        /// <returns>The scalar impulse applied onlt to the angular velocity for the constrained axis</returns>
        public static void SolveJacobian(ref Velocity velocityA, in Mass massA, ref Velocity velocityB, in Mass massB,
                                         ref float accumulatedImpulse, in Rotation1DMotorJacobianParameters parameters, float deltaTime, float inverseDeltaTime)
        {
            // Predict the relative orientation at the end of the step
            quaternion futureMotionBFromA = IntegrateOrientationBFromA(parameters.inertialRotationAInInertialPoseBSpace, velocityA.angular, velocityB.angular, deltaTime);

            // Calculate the effective mass
            float3 axisInMotionB = math.mul(futureMotionBFromA, -parameters.axisInInertialPoseASpace);
            float  effectiveMass;
            {
                float invEffectiveMass = math.csum(parameters.axisInInertialPoseASpace * parameters.axisInInertialPoseASpace * massA.inverseInertia +
                                                   axisInMotionB * axisInMotionB * massB.inverseInertia);
                effectiveMass = math.select(1.0f / invEffectiveMass, 0.0f, invEffectiveMass == 0.0f);
            }

            // Calculate the error, adjust by tau and damping, and apply an impulse to correct it
            float futureError = CalculateRotation1DMotorError(in parameters, futureMotionBFromA, out float currentAngle);
            float correction  = CalculateCorrection(futureError, parameters.initialError, parameters.tau, parameters.damping);
            float impulse     = math.mul(effectiveMass, -correction) * inverseDeltaTime;
            impulse           = CapImpulse(impulse, ref accumulatedImpulse, parameters.maxImpulseOfMotor);

            // check if we hit a limit
            var correctedAngle = currentAngle - correction;
            var limitError     = CalculateError(correctedAngle, parameters.minAngle, parameters.maxAngle);
            if (math.abs(limitError) > 0)
            {
                impulse += math.mul(effectiveMass, -limitError) * inverseDeltaTime;
            }

            velocityA.angular += impulse * parameters.axisInInertialPoseASpace * massA.inverseInertia;
            velocityB.angular += impulse * axisInMotionB * massB.inverseInertia;
        }

        static float CalculateRotation1DMotorError(in Rotation1DMotorJacobianParameters parameters, quaternion motionBFromA, out float currentAngle)
        {
            // Calculate the relative joint frame rotation
            quaternion jointBFromA = math.mul(math.InverseRotateFast(parameters.jointRotationInInertialPoseBSpace, motionBFromA), parameters.jointRotationInInertialPoseASpace);

            // extract current axis and angle between the two joint frames
            ((Quaternion)jointBFromA).ToAngleAxis(out var angleDeg, out var axis);
            // filter out any "out of rotation axis" components between the joint frames and make sure we are accounting
            // for a potential axis flip in the to-angle-axis calculation.
            angleDeg *= axis[parameters.axisIndex];

            currentAngle = math.radians(angleDeg);
            return currentAngle - parameters.target;
        }
    }
}

