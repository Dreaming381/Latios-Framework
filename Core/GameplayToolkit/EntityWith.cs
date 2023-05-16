using Unity.Entities;

// Todo: The lookup methods need to be replaced with versions that work correctly with the caches.

namespace Latios
{
    public struct EntityWith<T> where T : unmanaged, IComponentData
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
    }

    public struct EntityWithBuffer<T> where T : unmanaged, IBufferElementData
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
    }
}

