using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        public struct PositionConstraintJacobianParameters
        {
            public float3 jointPositionInInertialPoseASpace;
            public float3 jointPositionInInertialPoseBSpace;

            // If the constraint limits 1 DOF, this is the constrained axis.
            // If the constraint limits 2 DOF, this is the free axis.
            // If the constraint limits 3 DOF, this is unused and set to float3.zero
            public float3 axisInB;

            // Pivot distance limits
            public float minDistance;
            public float maxDistance;

            // Position error at the beginning of the step
            public float initialError;

            // Fraction of the position error to correct per step
            public float tau;

            // Fraction of the velocity error to correct per step
            public float damping;

            // True if the jacobian limits one degree of freedom
            public bool is1D;
        }

        public static void BuildJacobian(out PositionConstraintJacobianParameters parameters,
                                         in RigidTransform inertialPoseWorldTransformA, float3 jointPositionInInertialPoseASpace,
                                         in RigidTransform inertialPoseWorldTransformB, in RigidTransform jointTransformInInertialPoseBSpace,
                                         float minDistance, float maxDistance, float tau, float damping, bool3 constrainedAxes)
        {
            parameters = default;

            parameters.minDistance                       = minDistance;
            parameters.maxDistance                       = maxDistance;
            parameters.tau                               = tau;
            parameters.damping                           = damping;
            parameters.jointPositionInInertialPoseASpace = jointPositionInInertialPoseASpace;
            parameters.jointPositionInInertialPoseBSpace = jointTransformInInertialPoseBSpace.pos;

            // TODO.ma - this code is not always correct in its choice of pivotB.
            // The constraint model is asymmetrical.  B is the master, and the constraint feature is defined in B-space as a region affixed to body B.
            // For example, we can conceive of a 1D constraint as a plane attached to body B through constraint.PivotB, and constraint.PivotA is constrained to that plane.
            // A 2D constraint is a line attached to body B.  A 3D constraint is a point.
            // So, while we always apply an impulse to body A at pivotA, we apply the impulse to body B somewhere on the constraint region.
            // This code chooses that point by projecting pivotA onto the point, line or plane, which seems pretty reasonable and also analogous to how contact constraints work.
            // However, if the limits are nonzero, then the region is not a point, line or plane.  It is a spherical shell, cylindrical shell, or the space between two parallel planes.
            // In that case, it is not projecting A to a point on the constraint region.  This will not prevent solving the constraint, but the solution may not look correct.
            // For now I am leaving it because it is not important to get the most common constraint situations working.  If you use a ball and socket, or a prismatic constraint with a
            // static master body, or a stiff spring, then there's no problem.  However, I think it should eventually be fixed.  The min and max limits have different projections, so
            // probably the best solution is to make two jacobians whenever min != max.  My assumption is that 99% of these are ball and sockets with min = max = 0, so I would rather have
            // some waste in the min != max case than generalize this code to deal with different pivots and effective masses depending on which limit is hit.

            if (!math.all(constrainedAxes))
            {
                parameters.is1D = constrainedAxes.x ^ constrainedAxes.y ^ constrainedAxes.z;

                // Project pivot A onto the line or plane in B that it is attached to
                RigidTransform bFromA     = math.mul(math.inverse(inertialPoseWorldTransformB), inertialPoseWorldTransformA);
                float3         pivotAinB  = math.transform(bFromA, jointPositionInInertialPoseASpace);
                float3         diff       = pivotAinB - jointTransformInInertialPoseBSpace.pos;
                var            jointBAxes = new float3x3(jointTransformInInertialPoseBSpace.rot);
                for (int i = 0; i < 3; i++)
                {
                    float3 column      = jointBAxes[i];
                    parameters.axisInB = math.select(column, parameters.axisInB, parameters.is1D ^ constrainedAxes[i]);

                    float3 dot                                    = math.select(math.dot(column, diff), 0.0f, constrainedAxes[i]);
                    parameters.jointPositionInInertialPoseBSpace += column * dot;
                }
            }

            // Calculate the current error
            parameters.initialError = CalculatePositionConstraintError(in parameters, in inertialPoseWorldTransformA, in inertialPoseWorldTransformB, out _);
        }

        // Returns the world-space impulse applied to A. The world-space impulse applied to B is simply the negative of this value.
        public static float3 SolveJacobian(ref Velocity velocityA, in RigidTransform inertialPoseWorldTransformA, in Mass massA,
                                           ref Velocity velocityB, in RigidTransform inertialPoseWorldTransformB, in Mass massB,
                                           in PositionConstraintJacobianParameters parameters, float deltaTime, float inverseDeltaTime)
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

            // Predict error at the end of the step and calculate the impulse to correct it
            float3 impulse;
            {
                // Find the difference between the future distance and the limit range, then apply tau and damping
                float futureDistanceError = CalculatePositionConstraintError(in parameters,
                                                                             futureTransformA,
                                                                             futureTransformB,
                                                                             out float3 futureDirection);
                float solveDistanceError = CalculateCorrection(futureDistanceError, parameters.initialError, parameters.tau, parameters.damping);

                // Calculate the impulse to correct the error
                float3   solveError    = solveDistanceError * futureDirection;
                float3x3 effectiveMass = BuildSymmetricMatrix(effectiveMassDiag, effectiveMassOffDiag);
                impulse                = math.mul(effectiveMass, solveError) * inverseDeltaTime;
            }
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

        static float CalculatePositionConstraintError(in PositionConstraintJacobianParameters parameters,
                                                      in RigidTransform inertialPoseWorldTransformA,
                                                      in RigidTransform inertialPoseWorldTransformB,
                                                      out float3 direction)
        {
            // Find the direction from pivot A to B and the distance between them
            float3 pivotA = math.transform(inertialPoseWorldTransformA, parameters.jointPositionInInertialPoseASpace);
            float3 pivotB = math.transform(inertialPoseWorldTransformB, parameters.jointPositionInInertialPoseBSpace);
            float3 axis   = math.mul(inertialPoseWorldTransformB.rot, parameters.axisInB);
            direction     = pivotB - pivotA;
            float dot     = math.dot(direction, axis);

            // Project for lower-dimension joints
            float distance;
            if (parameters.is1D)
            {
                // In 1D, distance is signed and measured along the axis
                distance  = -dot;
                direction = -axis;
            }
            else
            {
                // In 2D / 3D, distance is nonnegative.  In 2D it is measured perpendicular to the axis.
                direction               -= axis * dot;
                float futureDistanceSq   = math.lengthsq(direction);
                float invFutureDistance  = math.select(math.rsqrt(futureDistanceSq), 0.0f, futureDistanceSq == 0.0f);
                distance                 = futureDistanceSq * invFutureDistance;
                direction               *= invFutureDistance;
            }

            // Find the difference between the future distance and the limit range
            return CalculateError(distance, parameters.minDistance, parameters.maxDistance);
        }
    }
}

