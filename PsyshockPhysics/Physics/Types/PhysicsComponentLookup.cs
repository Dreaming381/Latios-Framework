using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    //Specifying this as a NativeContainer prevents this value from being stored in a NativeContainer.
    /// <summary>
    /// A struct representing an Entity that can potentially index a PhysicsComponentDataFromEntity safely in parallel,
    /// or will throw an error when safety checks are enabled and the safety cannot be guaranteed.
    /// This type can be implicitly converted to the Entity type.
    /// </summary>
    [NativeContainer]
    public struct SafeEntity
    {
        internal Entity entity;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public static implicit operator Entity(SafeEntity e) => new Entity
        {
            Index = math.select(e.entity.Index, math.abs(e.entity.Index + 1), e.entity.Index < 0), Version = e.entity.Version
        };
#else
        public static implicit operator Entity(SafeEntity e) => e.entity;
#endif
    }

    /// <summary>
    /// A struct which wraps ComponentLookup<typeparamref name="T"/> and allows for performing
    /// Read-Write access in parallel using SafeEntity types when it is guaranteed safe to do so.
    /// You can implicitly cast a ComponentLookup<typeparamref name="T"/> to this type.
    /// </summary>
    /// <typeparam name="T">A type implementing IComponentData</typeparam>
    public struct PhysicsComponentLookup<T> where T : unmanaged, IComponentData
    {
        [NativeDisableParallelForRestriction]
        internal ComponentLookup<T> lookup;

        /// <summary>
        /// Reads or writes the component on the entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <returns></returns>
        public T this[SafeEntity safeEntity]
        {
            get
            {
                ValidateSafeEntityIsSafe(safeEntity);
                return lookup[safeEntity.entity];
            }
            set
            {
                ValidateSafeEntityIsSafe(safeEntity);
                lookup[safeEntity.entity] = value;
            }
        }

        /// <summary>
        /// Fetches the component on the entity represented by safeEntity if the entity has the component.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <param name="componentData">The fetched component</param>
        /// <returns>True if the entity had the component</returns>
        public bool TryGetComponent(SafeEntity safeEntity, out T componentData)
        {
            ValidateSafeEntityIsSafe(safeEntity);
            return lookup.TryGetComponent(safeEntity, out componentData);
        }

        /// <summary>
        /// Checks if the entity represented by SafeEntity has the component specified.
        /// This check is always valid regardless of whether such a component would be
        /// safe to access.
        /// </summary>
        public bool HasComponent(SafeEntity safeEntity) => lookup.HasComponent(safeEntity);

        /// <summary>
        /// This is identical to ComponentDataFromEntity<typeparamref name="T"/>.DidChange().
        /// Note that neither method is deterministic and both can be prone to race conditions.
        /// </summary>
        public bool DidChange(SafeEntity safeEntity, uint version) => lookup.DidChange(safeEntity, version);

        public static implicit operator PhysicsComponentLookup<T>(ComponentLookup<T> componentDataFromEntity)
        {
            return new PhysicsComponentLookup<T> { lookup = componentDataFromEntity };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref SystemState state)
        {
            lookup.Update(ref state);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(SystemBase system)
        {
            lookup.Update(system);
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

    /// <summary>
    /// A struct which wraps BufferFromEntity<typeparamref name="T"/> and allows for performing
    /// Read-Write access in parallel using SafeEntity types when it is guaranteed safe to do so.
    /// You can implicitly cast a BufferFromEntity<typeparamref name="T"/> to this type.
    /// </summary>
    /// <typeparam name="T">A type implementing IComponentData</typeparam>
    public struct PhysicsBufferLookup<T> where T : unmanaged, IBufferElementData
    {
        [NativeDisableParallelForRestriction]
        internal BufferLookup<T> lookup;

        /// <summary>
        /// Gets a reference to the buffer on the entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <returns></returns>
        public DynamicBuffer<T> this[SafeEntity safeEntity]
        {
            get
            {
                ValidateSafeEntityIsSafe(safeEntity);
                return lookup[safeEntity.entity];
            }
        }

        /// <summary>
        /// Fetches the buffer on the entity represented by safeEntity if the entity has the buffer type.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <param name="bufferData">The fetched buffer</param>
        /// <returns>True if the entity had the buffer type</returns>
        public bool TryGetComponent(SafeEntity safeEntity, out DynamicBuffer<T> bufferData)
        {
            ValidateSafeEntityIsSafe(safeEntity);
            return lookup.TryGetBuffer(safeEntity, out bufferData);
        }

        /// <summary>
        /// Checks if the entity represented by SafeEntity has the buffer type specified.
        /// This check is always valid regardless of whether such a buffer would be
        /// safe to access.
        /// </summary>
        public bool HasBuffer(SafeEntity safeEntity) => lookup.HasBuffer(safeEntity.entity);

        /// <summary>
        /// This is identical to BufferFromEntity<typeparamref name="T"/>.DidChange().
        /// Note that neither method is deterministic and both can be prone to race conditions.
        /// </summary>
        public bool DidChange(SafeEntity safeEntity, uint version) => lookup.DidChange(safeEntity, version);

        public static implicit operator PhysicsBufferLookup<T>(BufferLookup<T> bufferFromEntity)
        {
            return new PhysicsBufferLookup<T> { lookup = bufferFromEntity };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref SystemState state)
        {
            lookup.Update(ref state);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(SystemBase system)
        {
            lookup.Update(system);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateSafeEntityIsSafe(SafeEntity safeEntity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (safeEntity.entity.Index < 0)
            {
                throw new InvalidOperationException("PhysicsBufferFromEntity cannot be used inside a RunImmediate context. Use BufferFromEntity instead.");
            }
#endif
        }
    }
}

