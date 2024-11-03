using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        /// <summary>
        /// A velocity comprised of linear and angular components.
        /// The linear component is in world-space while the angular
        /// ccomponent is relative to the local inertia tensor diagonal
        /// </summary>
        public struct Velocity
        {
            public float3 linear;
            public float3 angular;
        }

        /// <summary>
        /// Represents inverse mass and inertia used in solvers.
        /// Zero values make the object immovable for the corresponding
        /// component.
        /// </summary>
        public struct Mass
        {
            public float  inverseMass;
            public float3 inverseInertia;
        }

        /// <summary>
        /// A struct used to define collision search ranges for an object in motion
        /// </summary>
        public struct MotionExpansion
        {
            float4 uniformXlinearYzw;

            /// <summary>
            /// Creates a motion expansion given the velocity, timestep, and angular expansion factor of the object.
            /// Forces such as gravity should be applied to the velocity prior to calling this.
            /// </summary>
            /// <param name="velocity">The velocity of the object, after forces have been applied.</param>
            /// <param name="deltaTime">The time step across which the expansion should account for</param>
            /// <param name="angularExpansionFactor">The factor by which the AABB may expand as it rotates</param>
            public MotionExpansion(in Velocity velocity, float deltaTime, float angularExpansionFactor)
            {
                var linear = velocity.linear * deltaTime;
                // math.length(AngularVelocity) * timeStep is conservative approximation of sin((math.length(AngularVelocity) * timeStep)
                var uniform       = 0.05f + math.min(math.length(velocity.angular) * deltaTime * angularExpansionFactor, angularExpansionFactor);
                uniformXlinearYzw = new float4(uniform, linear);
            }

            /// <summary>
            /// Expands a collider's world-space Aabb obtained via AabbFrom to account for its motion
            /// </summary>
            /// <param name="aabb">The Aabb typically obtained from calling AabbFrom on a Collider</param>
            /// <returns>An expanded version of the Collider's Aabb accounting for its anticipated motion
            /// within a timestep.</returns>
            public Aabb ExpandAabb(Aabb aabb)
            {
                var linear = uniformXlinearYzw.yzw;
                aabb.min   = math.min(aabb.min, aabb.min + linear) - uniformXlinearYzw.x;
                aabb.max   = math.max(aabb.max, aabb.max + linear) + uniformXlinearYzw.x;
                return aabb;
            }

            /// <summary>
            /// Gets the max search distance between the moving collider and a static object.
            /// The search distance should be used in a call to DistanceBetween() or DistanceBetweenAll().
            /// </summary>
            /// <param name="motionExpansion">The motion expansion of the moving collider</param>
            /// <returns>The max search distance required to anticipate contact within a timestep</returns>
            public static float GetMaxDistance(in MotionExpansion motionExpansion)
            {
                return math.length(motionExpansion.uniformXlinearYzw.yzw) + motionExpansion.uniformXlinearYzw.x + 0.05f;
            }

            /// <summary>
            /// Gets the max search distance between two moving colliders.
            /// The search distance should be used in a call to DistanceBetween() or DistanceBetweenAll().
            /// </summary>
            /// <param name="motionExpansionA">The motion expansion of the first moving collider</param>
            /// <param name="motionExpansionB">The motion expansion of the second moving collider</param>
            /// <returns>The max search distance required to anticipate contact within a timestep</returns>
            public static float GetMaxDistance(in MotionExpansion motionExpansionA, in MotionExpansion motionExpansionB)
            {
                var tempB        = motionExpansionB.uniformXlinearYzw;
                tempB.x          = -tempB.x;
                var tempCombined = motionExpansionA.uniformXlinearYzw - tempB;
                return math.length(tempCombined.yzw) + tempCombined.x;
            }
        }

        /// <summary>
        /// Updates a world transform of an object and its velocity using the specified time step and damping
        /// </summary>
        /// <param name="inertialPoseWorldTransform">The world transform of the center of mass and inertia tensor diagonal</param>
        /// <param name="velocity">The linear and angular velocity to apply to the world transform while also being damped</param>
        /// <param name="linearDamping">The amount of linear damping to apply per second, typically a small value near 0</param>
        /// <param name="angularDamping">The amount of angular damping to apply per second, typically a small value near 0</param>
        /// <param name="deltaTime">The time step over which the values should be updated</param>
        public static void Integrate(ref RigidTransform inertialPoseWorldTransform, ref Velocity velocity, float linearDamping, float angularDamping, float deltaTime)
        {
            inertialPoseWorldTransform.pos += velocity.linear * deltaTime;
            var halfDeltaAngle              = velocity.angular * 0.5f * deltaTime;
            var dq                          = new quaternion(new float4(halfDeltaAngle, 1f));
            inertialPoseWorldTransform.rot  = math.normalize(math.mul(inertialPoseWorldTransform.rot, dq));

            var dampFactors   = math.clamp(1f - new float2(linearDamping, angularDamping) * deltaTime, 0f, 1f);
            velocity.linear  *= dampFactors.x;
            velocity.angular *= dampFactors.y;
        }

        /// <summary>
        /// Computes a new world transform of an object using its velocity and the specified time step
        /// </summary>
        /// <param name="inertialPoseWorldTransform">The original world transform of the center of mass and inertia tensor diagonal before the time step</param>
        /// <param name="velocity">The linear and angular velocity to apply to the world transform</param>
        /// <param name="deltaTime">The time step over which the velocity should be applied</param>
        /// <returns>A new inertialPoseWorldTransform propagated by the Velocity across the time span of deltaTime</returns>
        public static RigidTransform IntegrateWithoutDamping(RigidTransform inertialPoseWorldTransform, in Velocity velocity, float deltaTime)
        {
            inertialPoseWorldTransform.pos += velocity.linear * deltaTime;
            var halfDeltaAngle              = velocity.angular * 0.5f * deltaTime;
            var dq                          = new quaternion(new float4(halfDeltaAngle, 1f));
            inertialPoseWorldTransform.rot  = math.normalize(math.mul(inertialPoseWorldTransform.rot, dq));
            return inertialPoseWorldTransform;
        }

        /// <summary>
        /// Applies the transform delta relative to the center of mass and inertia tensor diagonal to a TransformQvvs
        /// </summary>
        /// <param name="oldWorldTransform">The previous world transform for an entity</param>
        /// <param name="oldInertialPoseWorldTransform">The previous world transform of the entity's center of mass and inertia tensor diagonal</param>
        /// <param name="newInertialPoseWorldTransform">The new world transform of the entity's center of mass and inertia tensor diagonal</param>
        /// <returns>A new world transform of the entity based on the delta of the center of mass and inertia tensor diagonal</returns>
        public static TransformQvvs ApplyInertialPoseWorldTransformDeltaToWorldTransform(in TransformQvvs oldWorldTransform,
                                                                                         in RigidTransform oldInertialPoseWorldTransform,
                                                                                         in RigidTransform newInertialPoseWorldTransform)
        {
            var oldTransform = new RigidTransform(oldWorldTransform.rotation, oldWorldTransform.position);
            // oldInertialPoseWorldTransform = oldWorldTransform * localInertial
            // newInertialPoseWorldTransfrom = newWorldTransform * localInertial
            // inverseOldWorldTransform * oldInertialWorldTransform = inverseOldWorldTransform * oldWorldTransform * localInertial
            // inverseOldWorldTransform * oldInertialWorldTransform = localInertial
            // newInertialPoseWorldTransform * inverseLocalInertial = newWorldTransform * localInertial * inverseLocalInertial
            // newInertialPoseWorldTransform * inverseLocalInertial = newWorldTransform
            // newInertialPoseWorldTransform * inverse(inverseOldWorldTransform * oldInertialWorldTransform) = newWorldTransform
            // newInertialPoseWorldTransform * inverseOldInertialWorldTransform * oldWorldTransform = newWorldTransform
            var newTransform = math.mul(newInertialPoseWorldTransform, math.mul(math.inverse(oldInertialPoseWorldTransform), oldTransform));
            return new TransformQvvs
            {
                position   = newTransform.pos,
                rotation   = newTransform.rot,
                scale      = oldWorldTransform.scale,
                stretch    = oldWorldTransform.stretch,
                worldIndex = oldWorldTransform.worldIndex
            };
        }

        /// <summary>
        /// Computes a new world transform for an entity based on the local and world transforms of the inertia tensor diagonal
        /// and center of mass
        /// </summary>
        /// <param name="oldWorldTransform">The original world transform (only scale, stretch, and worldIndex are used)</param>
        /// <param name="inertialPoseWorldTransform">The world space transform of the center of mass and inertia tensor diagonal</param>
        /// <param name="localTensorOrientation">The local-space inertia tensor diagonal orientation relative to the entity</param>
        /// <param name="localCenterOfMassUnscaled">The local-space center of mass relative to the entity</param>
        /// <returns>A new world transform of the entity that preserves the world-space center of mass and inertia tensor diagonal</returns>
        public static TransformQvvs ApplyWorldTransformFromInertialPoses(in TransformQvvs oldWorldTransform,
                                                                         in RigidTransform inertialPoseWorldTransform,
                                                                         quaternion localTensorOrientation,
                                                                         float3 localCenterOfMassUnscaled)
        {
            var localInertial     = new RigidTransform(localTensorOrientation, localCenterOfMassUnscaled * oldWorldTransform.stretch * oldWorldTransform.scale);
            var newWorldTransform = math.mul(inertialPoseWorldTransform, math.inverse(localInertial));
            return new TransformQvvs(newWorldTransform.pos, newWorldTransform.rot, oldWorldTransform.scale, oldWorldTransform.stretch, oldWorldTransform.worldIndex);
        }
    }
}

