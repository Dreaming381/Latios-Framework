using System;
using Unity.Collections;
using Unity.Entities;

namespace Latios
{
    public struct EntityWith<T> : IEquatable<EntityWith<T>>, IComparable<EntityWith<T>>, IEquatable<Entity>, IComparable<Entity> where T : unmanaged, IComponentData
    {
        public Entity entity;

        public EntityWith(Entity entity)
        {
            this.entity = entity;
        }

        public T RO(ref ComponentLookup<T> lookup) => lookup[entity];
        public RefRO<T> RefRO(ref ComponentLookup<T> lookup) => lookup.GetRefRO(entity);
        public RefRW<T> RefRW(ref ComponentLookup<T> lookup) => lookup.GetRefRW(entity);

        public bool IsValid(ref ComponentLookup<T> cdfe) => cdfe.HasComponent(entity);

        public bool DidChange(ref ComponentLookup<T> cdfe, uint version) => cdfe.DidChange(entity, version);

        public static implicit operator Entity(EntityWith<T> entityWith) => entityWith.entity;

        public static implicit operator EntityWith<T>(Entity entity) => new EntityWith<T>(entity);

        public static bool operator ==(EntityWith<T> lhs, EntityWith<T> rhs)
        {
            return lhs.entity == rhs.entity;
        }

        public static bool operator ==(Entity lhs, EntityWith<T> rhs)
        {
            return lhs == rhs.entity;
        }

        public static bool operator ==(EntityWith<T> lhs, Entity rhs)
        {
            return lhs.entity == rhs;
        }

        public static bool operator !=(EntityWith<T> lhs, EntityWith<T> rhs)
        {
            return !(lhs == rhs);
        }

        public static bool operator !=(EntityWith<T> lhs, Entity rhs)
        {
            return !(lhs == rhs);
        }

        public static bool operator !=(Entity lhs, EntityWith<T> rhs)
        {
            return !(lhs == rhs);
        }

        public int CompareTo(EntityWith<T> other)
        {
            return entity.CompareTo(other);
        }

        public int CompareTo(Entity other)
        {
            return entity.CompareTo(other);
        }

        public override bool Equals(object compare)
        {
            return (compare is EntityWith<T> compareEntityWith && Equals(compareEntityWith)) || (compare is Entity compareEntity && Equals(compareEntity));
        }

        public bool Equals(EntityWith<T> compareEntity)
        {
            return entity == compareEntity.entity;
        }

        public bool Equals(Entity compareEntity)
        {
            return entity == compareEntity;
        }

        public override int GetHashCode()
        {
            return entity.GetHashCode();
        }

        public override String ToString()
        {
            return entity.ToString();
        }

        [GenerateTestsForBurstCompatibility]
        public FixedString64Bytes ToFixedString()
        {
            return entity.ToFixedString();
        }
    }

    public struct EntityWithBuffer<T> : IEquatable<EntityWithBuffer<T>>, IComparable<EntityWithBuffer<T>>, IEquatable<Entity>, IComparable<Entity> where T : unmanaged, IBufferElementData
    {
        public Entity entity;

        public EntityWithBuffer(Entity entity)
        {
            this.entity = entity;
        }

        public DynamicBuffer<T> Buffer(ref BufferLookup<T> lookup) => lookup[entity];

        public bool IsValid(BufferLookup<T> bfe) => bfe.HasBuffer(entity);

        public bool DidChange(BufferLookup<T> bfe, uint version) => bfe.DidChange(entity, version);

        public static implicit operator Entity(EntityWithBuffer<T> entityWithBuffer) => entityWithBuffer.entity;

        public static implicit operator EntityWithBuffer<T>(Entity entity) => new EntityWithBuffer<T>(entity);

        public static bool operator ==(EntityWithBuffer<T> lhs, EntityWithBuffer<T> rhs)
        {
            return lhs.entity == rhs.entity;
        }

        public static bool operator ==(Entity lhs, EntityWithBuffer<T> rhs)
        {
            return lhs == rhs.entity;
        }

        public static bool operator ==(EntityWithBuffer<T> lhs, Entity rhs)
        {
            return lhs.entity == rhs;
        }

        public static bool operator !=(EntityWithBuffer<T> lhs, EntityWithBuffer<T> rhs)
        {
            return !(lhs == rhs);
        }

        public static bool operator !=(EntityWithBuffer<T> lhs, Entity rhs)
        {
            return !(lhs == rhs);
        }

        public static bool operator !=(Entity lhs, EntityWithBuffer<T> rhs)
        {
            return !(lhs == rhs);
        }

        public int CompareTo(EntityWithBuffer<T> other)
        {
            return entity.CompareTo(other);
        }

        public int CompareTo(Entity other)
        {
            return entity.CompareTo(other);
        }

        public override bool Equals(object compare)
        {
            return (compare is EntityWithBuffer<T> compareEntityWithBuffer && Equals(compareEntityWithBuffer)) || (compare is Entity compareEntity && Equals(compareEntity));
        }

        public bool Equals(EntityWithBuffer<T> compareEntity)
        {
            return entity == compareEntity.entity;
        }

        public bool Equals(Entity compareEntity)
        {
            return entity == compareEntity;
        }

        public override int GetHashCode()
        {
            return entity.GetHashCode();
        }

        public override String ToString()
        {
            return entity.ToString();
        }

        [GenerateTestsForBurstCompatibility]
        public FixedString64Bytes ToFixedString()
        {
            return entity.ToFixedString();
        }
    }
}
