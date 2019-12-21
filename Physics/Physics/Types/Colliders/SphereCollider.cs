using System;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    [Serializable]
    public struct SphereCollider : ICollider
    {
        public float3 center;
        public float  radius;

        public SphereCollider(float3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }

        public AABB CalculateAABB(RigidTransform transform)
        {
            float3 wc   = math.transform(transform, center);
            AABB   aabb = new AABB(wc - radius, wc + radius);
            return aabb;
        }

        public float3 GetPointBySupportIndex(int index)
        {
            return center;
        }

        public AABB GetSupportAabb()
        {
            return new AABB(center - radius, center + radius);
        }

        public GjkSupportPoint GetSupportPoint(float3 direction)
        {
            return new GjkSupportPoint
                   {
                       pos = center,
                       id  = 0
                   };
        }

        public GjkSupportPoint GetSupportPoint(float3 direction, RigidTransform bInASpace)
        {
            return new GjkSupportPoint
                   {
                       pos = math.transform(bInASpace, center),
                       id  = 0
                   };
        }
    }
}

