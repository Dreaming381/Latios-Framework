using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    //Specifying this as a NativeContainer prevents this value from being stored in a NativeContainer.
    [NativeContainer]
    public struct SafeEntity
    {
        internal Entity entity;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public static implicit operator Entity(SafeEntity e) => new Entity {
            Index = math.abs(e.entity.Index), Version = e.entity.Version
        };
#else
        public static implicit operator Entity(SafeEntity e) => e.entity;
#endif
    }

    public struct PhysicsComponentDataFromEntity<T> where T : struct, IComponentData
    {
        [NativeDisableParallelForRestriction]
        internal ComponentDataFromEntity<T> cdfe;

        public T this[SafeEntity safeEntity]
        {
            get
            {
                ValidateSafeEntityIsSafe(safeEntity);
                return cdfe[safeEntity.entity];
            }
            set
            {
                ValidateSafeEntityIsSafe(safeEntity);
                cdfe[safeEntity.entity] = value;
            }
        }

        public bool HasComponent(SafeEntity safeEntity) => cdfe.HasComponent(safeEntity.entity);

        public bool DidChange(SafeEntity safeEntity, uint version) => cdfe.DidChange(safeEntity.entity, version);

        public static implicit operator PhysicsComponentDataFromEntity<T>(ComponentDataFromEntity<T> componentDataFromEntity)
        {
            return new PhysicsComponentDataFromEntity<T> { cdfe = componentDataFromEntity };
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateSafeEntityIsSafe(SafeEntity safeEntity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (safeEntity.entity.Index < 0)
            {
                throw new InvalidOperationException("PhysicsComponentDataFromEntity cannot be used inside a RunImmediate context. Use ComponentDataFromEntity instead.");
            }
#endif
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

