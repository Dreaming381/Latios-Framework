using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios.Psyshock
{
    //Specifying this as a NativeContainer prevents this value from being stored in a NativeContainer.
    [NativeContainer]
    public struct SafeEntity
    {
        internal Entity entity;

        public static implicit operator Entity(SafeEntity e) => e.entity;
    }

    public struct PhysicsComponentDataFromEntity<T> where T : struct, IComponentData
    {
        [NativeDisableParallelForRestriction]
        internal ComponentDataFromEntity<T> cdfe;

        public T this[SafeEntity safeEntity]
        {
            get => cdfe[safeEntity.entity];
            set => cdfe[safeEntity.entity] = value;
        }

        public bool HasComponent(SafeEntity safeEntity) => cdfe.HasComponent(safeEntity.entity);

        public bool DidChange(SafeEntity safeEntity, uint version) => cdfe.DidChange(safeEntity.entity, version);

        public static implicit operator PhysicsComponentDataFromEntity<T>(ComponentDataFromEntity<T> componentDataFromEntity)
        {
            return new PhysicsComponentDataFromEntity<T> { cdfe = componentDataFromEntity };
        }
    }

    public struct PhysicsBufferFromEntity<T> where T : struct, IBufferElementData
    {
        [NativeDisableParallelForRestriction]
        internal BufferFromEntity<T> bfe;

        public DynamicBuffer<T> this[SafeEntity safeEntity]
        {
            get => bfe[safeEntity.entity];
        }

        public bool HasComponent(SafeEntity safeEntity) => bfe.HasComponent(safeEntity.entity);

        public static implicit operator PhysicsBufferFromEntity<T>(BufferFromEntity<T> bufferFromEntity)
        {
            return new PhysicsBufferFromEntity<T> { bfe = bufferFromEntity };
        }
    }

    public static class PhysicsCdfeSystemBaseExtensions
    {
        public static PhysicsComponentDataFromEntity<T> GetPhysicsComponentDataFromEntity<T>(this SystemBase system, bool isReadOnly = false) where T : struct, IComponentData
        {
            return new PhysicsComponentDataFromEntity<T> { cdfe = system.GetComponentDataFromEntity<T>(isReadOnly) };
        }

        public static PhysicsBufferFromEntity<T> GetPhysicsBufferFromEntity<T>(this SystemBase system, bool isReadOnly = false) where T : struct, IBufferElementData
        {
            return new PhysicsBufferFromEntity<T> { bfe = system.GetBufferFromEntity<T>(isReadOnly) };
        }
    }
}

