using System;
using Unity.Entities;

namespace Latios.PhysicsEngine.Tests
{
    public struct AabbEntity : IComparable<AabbEntity>
    {
        public AABB   aabb;
        public Entity entity;

        public int CompareTo(AabbEntity other)
        {
            return aabb.min.x.CompareTo(other.aabb.min.x);
        }
    }
}

