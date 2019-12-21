using System;
using Unity.Mathematics;

//Todo: Assert A != B
namespace Latios.PhysicsEngine
{
    [Serializable]
    public struct CapsuleCollider : ICollider
    {
        public float3 pointA;
        public float  radius;
        public float3 pointB;

        public CapsuleCollider(float3 pointA, float3 pointB, float radius)
        {
            this.pointA = pointA;
            this.pointB = pointB;
            this.radius = radius;
        }

        public AABB CalculateAABB(RigidTransform transform)
        {
            float3 a = math.transform(transform, pointA);
            float3 b = math.transform(transform, pointB);
            return new AABB(math.min(a, b) - radius, math.max(a, b) + radius);
        }

        public float3 GetPointBySupportIndex(int index)
        {
            return math.select(pointA, pointB, index > 0);
        }

        public AABB GetSupportAabb()
        {
            return new AABB(math.min(pointA, pointB), math.max(pointA, pointB));
        }

        public GjkSupportPoint GetSupportPoint(float3 direction)
        {
            float dotA = math.dot(direction, pointA);
            float dotB = math.dot(direction, pointB);
            bool  useB = dotB > dotA;
            return new GjkSupportPoint
                   {
                       pos = math.select(pointA, pointB, useB),
                       id  = (uint)math.select(0, 1, useB)
                   };
        }

        public GjkSupportPoint GetSupportPoint(float3 direction, RigidTransform bInASpace)
        {
            float3 newA = math.transform(bInASpace, pointA);
            float3 newB = math.transform(bInASpace, pointB);
            float  dotA = math.dot(direction, newA);
            float  dotB = math.dot(direction, newB);
            bool   useB = dotB > dotA;
            return new GjkSupportPoint
                   {
                       pos = math.select(newA, newB, useB),
                       id  = (uint)math.select(0, 1, useB)
                   };
        }
    }
}

