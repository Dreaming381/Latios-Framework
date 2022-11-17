using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
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
        /// If the collider is composed of multiple primitives, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndex;
    }

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
        /// If colliderA is composed of multiple primitives, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndexA;
        /// <summary>
        /// If colliderB is composed of multiple primitives, this is the index of the primitive that generated the result
        /// </summary>
        public int subColliderIndexB;
    }

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

    public struct ColliderCastResult
    {
        /// <summary>
        /// Where the hit occurred on the translated caster in world space. Should be the same as hitpointOnTarget
        /// </summary>
        public float3 hitpointOnCaster;
        /// <summary>
        /// Where the hit occurred on the stationary target in world space. Should be the same as hitpointOnCaster
        /// </summary>
        public float3 hitpointOnTarget;
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
        public RigidTransform transform => body.transform;
    }
}

