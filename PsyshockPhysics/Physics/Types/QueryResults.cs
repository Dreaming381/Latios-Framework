using Latios.Transforms;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A struct which contains a result of calling Physics.DistanceBetween and passing in a point.
    /// It contains information about the closest surface point on the collider.
    /// </summary>
    public struct PointDistanceResult
    {
        /// <summary>
        /// The nearest point on the collider in world space
        /// </summary>
        public float3 hitpoint;
        /// <summary>
        /// The distance from the point to the collider, negative if the point was inside the collider
        /// </summary>
        public float distance;
        /// <summary>
        /// The outward normal of the collider at the nearest point on the surface in world space
        /// </summary>
        public float3 normal;
        /// <summary>
        /// If the collider is composed of multiple primitives such as a compound collider, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndex;
    }

    /// <summary>
    /// A struct which contains a result of calling Physics.DistanceBetween between two colliders.
    /// It contains information about the closest surface points on each of the colliders.
    /// </summary>
    public struct ColliderDistanceResult
    {
        /// <summary>
        /// The nearest point on colliderA in world space
        /// </summary>
        public float3 hitpointA;
        /// <summary>
        /// The nearest point on colliderB in world space
        /// </summary>
        public float3 hitpointB;
        /// <summary>
        /// The outward normal of colliderA at hitpointA in world space
        /// </summary>
        public float3 normalA;
        /// <summary>
        /// The outward normal of colliderB at hitpointB in world space
        /// </summary>
        public float3 normalB;
        /// <summary>
        /// The distance between the colliders, negative if the colliders are penetrating
        /// </summary>
        public float distance;
        /// <summary>
        /// If colliderA is composed of multiple primitives such as a compound collider, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndexA;
        /// <summary>
        /// If colliderB is composed of multiple primitives such as a compound collider, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndexB;
    }

    /// <summary>
    /// A struct which contains the result of calling Physics.Raycast.
    /// It contains information about the hit surface of the collider and the distance the ray traveled.
    /// </summary>
    public struct RaycastResult
    {
        /// <summary>
        /// Where the ray hit in world space
        /// </summary>
        public float3 position;
        /// <summary>
        /// The distance the ray traveled before hitting
        /// </summary>
        public float distance;
        /// <summary>
        /// The outward normal of the collider at position in world space
        /// </summary>
        public float3 normal;
        /// <summary>
        /// If the collider is composed of multiple primitives, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndex;
    }

    /// <summary>
    /// A struct which contains the result of calling Physics.ColliderCast.
    /// It contains information about the hit surfaces between the colliders and the distance the casted collider traveled.
    /// </summary>
    public struct ColliderCastResult
    {
        /// <summary>
        /// Where the hit occurred in world space. The hitpoint should be the same for both objects.
        /// </summary>
        public float3 hitpoint;
        /// <summary>
        /// The outward normal of the caster at the hitpoint in world space
        /// </summary>
        public float3 normalOnCaster;
        /// <summary>
        /// The outward normal of the target at the hitpoint in world space
        /// </summary>
        public float3 normalOnTarget;
        /// <summary>
        /// The distance the caster traveled before hitting
        /// </summary>
        public float distance;
        /// <summary>
        /// If the caster is composed of multiple primitives, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndexOnCaster;
        /// <summary>
        /// If the target is composed of multiple primitives, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndexOnTarget;
    }

    /// <summary>
    /// A struct which contains which collider in a CollisionLayer was used when generating a result from query.
    /// It is generated when calling a Physics.DistanceBetween, Physics.Raycast, or Physics.ColliderCast with a CollisionLayer.
    /// </summary>
    public struct LayerBodyInfo
    {
        /// <summary>
        /// The body in the CollisionLayer that generated the corresponding result
        /// </summary>
        public ColliderBody body;
        /// <summary>
        /// The AABB that was stored alongside the body in the CollisionLayer that generated the corresponding result
        /// </summary>
        public Aabb aabb;
        /// <summary>
        /// The index in the CollisionLayer of the body that generated the corresponding result
        /// </summary>
        public int bodyIndex;
        /// <summary>
        /// The index of the body that generated the corresponding result relative to the original EntityQuery or NativeArrays
        /// used to create the CollisionLayer
        /// </summary>
        public int sourceIndex;

        /// <summary>
        /// The entity in the CollisionLayer that generated the corresponding result
        /// </summary>
        public Entity entity => body.entity;
        /// <summary>
        /// The collider in the CollisionLayer that generated the corresponding result
        /// </summary>
        public Collider collider => body.collider;
        /// <summary>
        /// The transform in the CollisionLayer that generated the corresponding result
        /// </summary>
        public TransformQvvs transform => body.transform;
    }
}

