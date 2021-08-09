using Unity.Entities;

namespace Latios
{
    public struct EntityWith<T> where T : struct, IComponentData
    {
        public Entity entity;

        public EntityWith(Entity entity)
        {
            this.entity = entity;
        }

        public T this[ComponentDataFromEntity<T> cdfe]
        {
            get => cdfe[entity];
            set => cdfe[entity] = value;
        }

        public bool IsValid(ComponentDataFromEntity<T> cdfe) => cdfe.HasComponent(entity);

        public bool DidChange(ComponentDataFromEntity<T> cdfe, uint version) => cdfe.DidChange(entity, version);

        public static implicit operator Entity(EntityWith<T> entityWith) => entityWith.entity;

        public static implicit operator EntityWith<T>(Entity entity) => new EntityWith<T>(entity);
    }

    public struct EntityWithBuffer<T> where T : struct, IBufferElementData
    {
        public Entity entity;

        public EntityWithBuffer(Entity entity)
        {
            this.entity = entity;
        }

        public DynamicBuffer<T> this[BufferFromEntity<T> bfe] => bfe[entity];

        public bool IsValid(BufferFromEntity<T> bfe) => bfe.HasComponent(entity);

        public bool DidChange(BufferFromEntity<T> bfe, uint version) => bfe.DidChange(entity, version);

        public static implicit operator Entity(EntityWithBuffer<T> entityWithBuffer) => entityWithBuffer.entity;

        public static implicit operator EntityWithBuffer<T>(Entity entity) => new EntityWithBuffer<T>(entity);
    }
}

