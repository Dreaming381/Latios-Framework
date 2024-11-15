using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Latios.Transforms;
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
        internal Entity m_entity;

        public Entity entity => (Entity)this;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public static implicit operator Entity(SafeEntity e) => new Entity
        {
            Index = math.select(e.m_entity.Index, math.abs(e.m_entity.Index + 1), e.m_entity.Index < 0), Version = e.m_entity.Version
        };
#else
        public static implicit operator Entity(SafeEntity e) => e.m_entity;
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
        public T this[SafeEntity safeEntity]
        {
            get
            {
                ValidateSafeEntityIsSafe(safeEntity);
                return lookup[safeEntity.m_entity];
            }
            set
            {
                ValidateSafeEntityIsSafe(safeEntity);
                lookup[safeEntity.m_entity] = value;
            }
        }

        /// <summary>
        /// Acquires a RefRW to the component on the entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <returns>A RefRW that provides direct access to component storage.</returns>
        public RefRW<T> GetRW(SafeEntity safeEntity)
        {
            ValidateSafeEntityIsSafe(safeEntity);
            return lookup.GetRefRW(safeEntity);
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

        /// <summary>
        /// Fetches the enabled bit of the component on the entity represented by SafeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The component must be of type IEnableableComponent.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <returns>The enabled state as a boolean</returns>
        public bool IsEnabled(SafeEntity safeEntity)
        {
            ValidateSafeEntityIsSafe(safeEntity);
            return lookup.IsComponentEnabled(safeEntity);
        }

        /// <summary>
        /// Sets the enabled bit of the component on the entity represented by SafeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The component must be of type IEnableableComponent.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <param name="enabled">The new enabled state of the component</param>
        /// <remarks>This method performs an atomic operation which may suffer from worse performance than setting a normal bool field.</remarks>
        public void SetEnabled(SafeEntity safeEntity, bool enabled)
        {
            ValidateSafeEntityIsSafe(safeEntity);
            lookup.SetComponentEnabled(safeEntity, enabled);
        }

        public static implicit operator PhysicsComponentLookup<T>(ComponentLookup<T> componentLookup)
        {
            return new PhysicsComponentLookup<T> { lookup = componentLookup };
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
            if (safeEntity.m_entity.Index < 0)
            {
                throw new InvalidOperationException("PhysicsComponentLookup cannot be used inside a RunImmediate context. Use ComponentLookup instead.");
            }
#endif
        }
    }

    /// <summary>
    /// A struct which wraps BufferLookup<typeparamref name="T"/> and allows for performing
    /// Read-Write access in parallel using SafeEntity types when it is guaranteed safe to do so.
    /// You can implicitly cast a BufferLookup<typeparamref name="T"/> to this type.
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
        public DynamicBuffer<T> this[SafeEntity safeEntity]
        {
            get
            {
                ValidateSafeEntityIsSafe(safeEntity);
                return lookup[safeEntity.m_entity];
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
        public bool HasBuffer(SafeEntity safeEntity) => lookup.HasBuffer(safeEntity.m_entity);

        /// <summary>
        /// This is identical to BufferFromEntity<typeparamref name="T"/>.DidChange().
        /// Note that neither method is deterministic and both can be prone to race conditions.
        /// </summary>
        public bool DidChange(SafeEntity safeEntity, uint version) => lookup.DidChange(safeEntity, version);

        /// <summary>
        /// Fetches the enabled bit of the buffer on the entity represented by SafeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The buffer must be of type IEnableableComponent.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <returns>The enabled state as a boolean</returns>
        public bool IsEnabled(SafeEntity safeEntity)
        {
            ValidateSafeEntityIsSafe(safeEntity);
            return lookup.IsBufferEnabled(safeEntity);
        }

        /// <summary>
        /// Sets the enabled bit of the buffer on the entity represented by SafeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The buffer must be of type IEnableableComponent.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <param name="enabled">The new enabled state of the buffer</param>
        /// <remarks>This method performs an atomic operation which may suffer from worse performance than setting a normal bool field.</remarks>
        public void SetEnabled(SafeEntity safeEntity, bool enabled)
        {
            ValidateSafeEntityIsSafe(safeEntity);
            lookup.SetBufferEnabled(safeEntity, enabled);
        }

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
            if (safeEntity.m_entity.Index < 0)
            {
                throw new InvalidOperationException("PhysicsBufferLookup cannot be used inside a RunImmediate context. Use BufferLookup instead.");
            }
#endif
        }
    }

    /// <summary>
    /// A struct which wraps an IAspect.Lookup and allows for performing
    /// Read-Write access in parallel using SafeEntity types when it is guaranteed safe to do so.
    /// </summary>
    /// <typeparam name="TAspectType">A type implementing IAspect</typeparam>
    /// <typeparam name="TLookupType">The IAspect.Lookup for the specific IAspect type</typeparam>
    public struct PhysicsAspectLookup<TAspectType, TLookupType> where TAspectType : unmanaged, IAspect where TLookupType : unmanaged,
                                                                      Unity.Entities.Internal.InternalCompilerInterface.IAspectLookup<TAspectType>
    {
        [NativeDisableParallelForRestriction] TLookupType m_lookup;

        /// <summary>
        /// Constructs the wrapper around the specified initialized IAspect.Lookup
        /// </summary>
        /// <param name="lookup">An initialized IAspect.Lookup</param>
        public PhysicsAspectLookup(TLookupType lookup)
        {
            m_lookup = lookup;
        }

        public static implicit operator PhysicsAspectLookup<TAspectType, TLookupType>(TLookupType lookup) => new PhysicsAspectLookup<TAspectType, TLookupType>(lookup);

        /// <summary>
        /// Updates the type handles of the wrapped IAspect.Lookup
        /// </summary>
        /// <param name="state">The SystemState to update the handles</param>
        public void Update(ref SystemState state) => m_lookup.Update(ref state);

        /// <summary>
        /// Gets an Aspect on the entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        public TAspectType this[SafeEntity entity]
        {
            get
            {
                ValidateSafeEntityIsSafe(entity);
                return m_lookup[entity];
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateSafeEntityIsSafe(SafeEntity safeEntity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (safeEntity.m_entity.Index < 0)
            {
                throw new InvalidOperationException("AspectLookup cannot be used inside a RunImmediate context. Use <TAspectType>.Lookup instead.");
            }
#endif
        }
    }

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
    /// <summary>
    /// A lookup struct for performing read/write access on a TransformAspect via SafeEntity
    /// when it is safe to do so.
    /// </summary>
    public struct PhysicsTransformAspectLookup
    {
        [NativeDisableParallelForRestriction] ComponentLookup<LocalTransform> m_localTransformLookup;
        [ReadOnly]  ComponentLookup<ParentToWorldTransform>                   m_parentToWorldLookup;
        [NativeDisableParallelForRestriction] ComponentLookup<WorldTransform> m_worldTransformLookup;

        /// <summary>
        /// Create the aspect lookup from an system state.
        /// </summary>
        /// <param name="state">The system state to create the aspect lookup from.</param>
        public PhysicsTransformAspectLookup(ref SystemState state)
        {
            m_localTransformLookup = state.GetComponentLookup<LocalTransform>(false);
            m_parentToWorldLookup  = state.GetComponentLookup<ParentToWorldTransform>(true);
            m_worldTransformLookup = state.GetComponentLookup<WorldTransform>(false);
        }

        /// <summary>
        /// Update the lookup container.
        /// Must be called every frames before using the lookup.
        /// </summary>
        /// <param name="state">The system state the aspect lookup was created from.</param>
        public void Update(ref SystemState state)
        {
            m_localTransformLookup.Update(ref state);
            m_parentToWorldLookup.Update(ref state);
            m_worldTransformLookup.Update(ref state);
        }

        /// <summary>
        /// Gets the TransformAspect instance corresponding to the entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// </summary>
        /// <param name="entity">The entity to create the aspect struct from.</param>
        /// <returns>Instance of the aspect struct pointing at a specific entity's components data.</returns>
        public TransformAspect this[SafeEntity entity]
        {
            get
            {
                ValidateSafeEntityIsSafe(entity);
                return new TransformAspect(m_localTransformLookup.GetRefRWOptional(entity),
                                           m_parentToWorldLookup.GetRefROOptional(entity),
                                           m_worldTransformLookup.GetRefRW(entity));
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateSafeEntityIsSafe(SafeEntity safeEntity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (safeEntity.m_entity.Index < 0)
            {
                throw new InvalidOperationException("PhysicsBufferFromEntity cannot be used inside a RunImmediate context. Use BufferFromEntity instead.");
            }
#endif
        }
    }
#endif

    public static class ComponentBrokerPhysicsExtensions
    {
        /// <summary>
        /// Acquires an optional RefRO to the component on the entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <returns>A RefRO instance which may or may not be valid (use IsValid to check)</returns>
        public static RefRO<T> GetRO<T>(ref this ComponentBroker broker, SafeEntity entity) where T : unmanaged, IComponentData
        {
            if (broker.IsReadOnlyAccessType<T>())
                return broker.GetRO<T>(entity);
            ValidateSafeEntityIsSafe(entity);
            return broker.GetROIgnoreParallelSafety<T>(entity);
        }

        /// <summary>
        /// Acquires an optional RefRW to the component on the entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <returns>A RefRW instance which may or may not be valid (use IsValid to check)</returns>
        public static RefRW<T> GetRW<T>(ref this ComponentBroker broker, SafeEntity entity) where T : unmanaged, IComponentData
        {
            ValidateSafeEntityIsSafe(entity);
            return broker.GetRWIgnoreParallelSafety<T>(entity);
        }

        /// <summary>
        /// Acquires an optional DynamicBuffer on the entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="safeEntity">A safeEntity representing an entity that may be safe to access</param>
        /// <returns>A DynamicBuffer instance which may or may not be valid (use IsCreated to check)</returns>
        public static DynamicBuffer<T> GetBuffer<T>(ref this ComponentBroker broker, SafeEntity entity) where T : unmanaged, IBufferElementData
        {
            if (broker.IsReadOnlyAccessType<T>())
                return broker.GetBuffer<T>(entity);
            ValidateSafeEntityIsSafe(entity);
            return broker.GetBufferIgnoreParallelSafety<T>(entity);
        }

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        /// <summary>
        /// Tries to get a TransformAspect on the specified entity represented by safeEntity.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="entity">The entity to get the component from.</param>
        /// <param name="transformAspect">The TransformAspect from the entity, or default if not valid</param>
        /// <returns>True if the entity has the required components to form the TransformAspect, false otherwise</returns>
        public static bool TryGetTransformAspect(ref this ComponentBroker broker, SafeEntity entity, out TransformAspect transformAspect)
        {
            ValidateSafeEntityIsSafe(entity);
            return broker.TryGetTransformAspectIgnoreParallelSafety(entity, out transformAspect);
        }
#endif

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateSafeEntityIsSafe(SafeEntity safeEntity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (safeEntity.m_entity.Index < 0)
            {
                throw new InvalidOperationException("SafeEntity for a ComponentBroker cannot be used inside a RunImmediate context. Use SafeEntity.entity instead.");
            }
#endif
        }
    }
}

