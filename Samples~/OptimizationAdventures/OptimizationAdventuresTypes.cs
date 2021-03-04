using System;
using Latios.Psyshock;
using Unity.Entities;

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

