using System;
using Latios.Psyshock;
using Unity.Entities;
using Unity.Mathematics;

namespace OptimizationAdventures
{
    public struct AabbEntity : IComparable<AabbEntity>
    {
        public Aabb   aabb;
        public Entity entity;

        public int CompareTo(AabbEntity other)
        {
            return aabb.min.x.CompareTo(other.aabb.min.x);
        }
    }

    public struct AabbEntityRearranged : IComparable<AabbEntityRearranged>
    {
        public float2 minXmaxX;
        public float4 minYZmaxYZ;
        public Entity entity;

        public int CompareTo(AabbEntityRearranged other)
        {
            return minXmaxX.x.CompareTo(other.minXmaxX.x);
        }
    }

    public struct EntityPair : IEquatable<EntityPair>
    {
        public Entity a;
        public Entity b;

        public EntityPair(Entity aa, Entity bb)
        {
            a = aa;
            b = bb;
        }

        public bool Equals(EntityPair other)
        {
            return a == other.a && b == other.b;
        }

        public static bool operator ==(EntityPair a, EntityPair b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(EntityPair a, EntityPair b)
        {
            return !a.Equals(b);
        }
    }
}

