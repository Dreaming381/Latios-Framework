using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        public struct Rotation1DConstraintJacobianParameters
        {
            public quaternion inertialPoseAInInertialPoseBSpace;
            public quaternion jointRotationInInertialPoseASpace;
            public quaternion jointRotationInInertialPoseBSpace;

            // Limited axis in motion A space
            public float3 axisInInertialPoseASpace;

            public float minAngle;
            public float maxAngle;

            public float initialError;
            public float tau;
            public float damping;

            public int axisIndex;
        }

        public static void BuildJacobian(out Rotation1DConstraintJacobianParameters parameters,
                                         quaternion inertialPoseWorldRotationA, quaternion jointRotationInInertialPoseASpace,
                                         quaternion inertialPoseWorldRotationB, quaternion jointRotationInInertialPoseBSpace,
                                         float minAngle, float maxAngle, float tau, float damping, int axisIndex)
        {
            parameters = new Rotation1DConstraintJacobianParameters
            {
                inertialPoseAInInertialPoseBSpace = math.normalize(math.InverseRotateFast(inertialPoseWorldRotationB, inertialPoseWorldRotationA)),
                jointRotationInInertialPoseASpace = jointRotationInInertialPoseASpace,
                jointRotationInInertialPoseBSpace = jointRotationInInertialPoseBSpace,
                axisInInertialPoseASpace          = new float3x3(jointRotationInInertialPoseASpace)[axisIndex],
                minAngle                          = minAngle,
                maxAngle                          = maxAngle,
                tau                               = tau,
                damping                           = damping,
                axisIndex                         = axisIndex
            };
            parameters.initialError = CalculateRotation1DConstraintError(in parameters, parameters.inertialPoseAInInertialPoseBSpace);
        }

        // Returns the scalar impulse applied only to the angular velocity for the constrained axis.
        public static float SolveJacobian(ref Velocity velocityA, in Mass massA, ref Velocity velocityB, in Mass massB,
                                          in Rotation1DConstraintJacobianParameters parameters, float deltaTime, float inverseDeltaTime)
        {
            // Predict the relative orientation at the end of the step
            quaternion futureMotionBFromA = IntegrateOrientationBFromA(parameters.inertialPoseAInInertialPoseBSpace, velocityA.angular, velocityB.angular, deltaTime);

            // Calculate the effective mass
            float3 axisInMotionB = math.mul(futureMotionBFromA, -parameters.axisInInertialPoseASpace);
            float  effectiveMass;
            {
                float invEffectiveMass = math.csum(parameters.axisInInertialPoseASpace * parameters.axisInInertialPoseASpace * massA.inverseInertia +
                                                   axisInMotionB * axisInMotionB * massB.inverseInertia);
                effectiveMass = math.select(1.0f / invEffectiveMass, 0.0f, invEffectiveMass == 0.0f);
            }

            // Calculate the error, adjust by tau and damping, and apply an impulse to correct it
            float futureError  = CalculateRotation1DConstraintError(in parameters, futureMotionBFromA);
            float solveError   = CalculateCorrection(futureError, parameters.initialError, parameters.tau, parameters.damping);
            float impulse      = math.mul(effectiveMass, -solveError) * inverseDeltaTime;
            velocityA.angular += impulse * parameters.axisInInertialPoseASpace * massA.inverseInertia;
            velocityB.angular += impulse * axisInMotionB * massB.inverseInertia;

            return impulse;
        }

        static float CalculateRotation1DConstraintError(in Rotation1DConstraintJacobianParameters parameters, quaternion motionBFromA)
        {
            // Calculate the relative joint frame rotation
            quaternion jointBFromA = math.mul(math.InverseRotateFast(parameters.jointRotationInInertialPoseBSpace, motionBFromA), parameters.jointRotationInInertialPoseASpace);

            // Find the twist angle of the rotation.
            //
            // There is no one correct solution for the twist angle. Suppose the joint models a pair of bodies connected by
            // three gimbals, one of which is limited by this jacobian. There are multiple configurations of the gimbals that
            // give the bodies the same relative orientation, so it is impossible to determine the configuration from the
            // bodies' orientations alone, nor therefore the orientation of the limited gimbal.
            //
            // This code instead makes a reasonable guess, the twist angle of the swing-twist decomposition of the bodies'
            // relative orientation. It always works when the limited axis itself is unable to rotate freely, as in a limited
            // hinge. It works fairly well when the limited axis can only rotate a small amount, preferably less than 90
            // degrees. It works poorly at higher angles, especially near 180 degrees where it is not continuous. For systems
            // that require that kind of flexibility, the gimbals should be modeled as separate bodies.
            float angle = CalculateTwistAngle(jointBFromA, parameters.axisIndex);

            // Angle is in [-2pi, 2pi].
            // For comparison against the limits, find k so that angle + 2k * pi is as close to [min, max] as possible.
            float centerAngle = (parameters.minAngle + parameters.maxAngle) / 2.0f;
            bool  above       = angle > (centerAngle + math.PI);
            bool  below       = angle < (centerAngle - math.PI);
            angle             = math.select(angle, angle - 2.0f * math.PI, above);
            angle             = math.select(angle, angle + 2.0f * math.PI, below);

            // Calculate the relative angle about the twist axis
            return CalculateError(angle, parameters.minAngle, parameters.maxAngle);
        }
    }
}

