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

        public T this[ComponentLookup<T> cdfe]
        {
            get => cdfe[entity];
            set => cdfe[entity] = value;
        }

        public bool IsValid(ComponentLookup<T> cdfe) => cdfe.HasComponent(entity);

        public bool DidChange(ComponentLookup<T> cdfe, uint version) => cdfe.DidChange(entity, version);

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

        public DynamicBuffer<T> this[BufferLookup<T> bfe] => bfe[entity];

        public bool IsValid(BufferLookup<T> bfe) => bfe.HasBuffer(entity);

        public bool DidChange(BufferLookup<T> bfe, uint version) => bfe.DidChange(entity, version);

        public static implicit operator Entity(EntityWithBuffer<T> entityWithBuffer) => entityWithBuffer.entity;

        public static implicit operator EntityWithBuffer<T>(Entity entity) => new EntityWithBuffer<T>(entity);
    }
}

