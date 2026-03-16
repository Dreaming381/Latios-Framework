#if !LATIOS_TRANSFORMS_UNITY
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

// Todo: Support ComponentBroker

namespace Latios.Transforms
{
    /// <summary>
    /// A key which can be used to promise thread-safe access to entities within a hierarchy for reading and writing.
    /// You can obtain one of these from an ArchetypeChunk or via the static method CreateFromExclusivelyAccessedRoot
    /// </summary>
    public ref struct TransformsKey
    {
        /// <summary>
        /// WARNING: This can be UNSAFE if you break the promise.
        /// This method promises that in your current context, you have safe exclusive access to the root entity passed in.
        /// </summary>
        /// <param name="root">The hierarchy-free or hierarchy root entity you promise to have exclusive access to</param>
        /// <returns>A key which can be used to access components from this entity or any entity within its hierarchy</returns>
        public static TransformsKey CreateFromExclusivelyAccessedRoot(Entity root, EntityStorageInfoLookup entityStorageInfoLookup)
        {
            ValidateIsRoot(root, entityStorageInfoLookup);
            return new TransformsKey { chunkIndex = -1, entityIndex = root.Index };
        }

        internal EntityStorageInfoLookup esil;
        internal int                     chunkIndex;
        internal int                     entityIndex;

        // True if this key was created, false if it is the default
        public bool isCreated => chunkIndex < 0 || esil.IsCreated();

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void Validate(Entity root)
        {
            if (!isCreated)
                throw new System.ArgumentException("The TransformsKey is not valid.");
            if (root.Index == entityIndex && entityIndex >= 0)
                return;
            if (chunkIndex < 0)
                throw new System.InvalidOperationException(
                    $"The root of the hierarchy has not been safely secured. Root of hierarchy: {root.ToFixedString()}  Secured root Entity.Index: {entityIndex}");
            var esi = esil[root];
            if (esi.Chunk.GetHashCode() == chunkIndex)
            {
                entityIndex = root.Index;
                return;
            }
            throw new System.InvalidOperationException(
                $"The root of the hierarchy does not belong to the secured ArchetypeChunk. Root of hierarchy: {root.ToFixedString()}  Secured chunk hashcode: {chunkIndex}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateIsRoot(Entity candidate, EntityStorageInfoLookup esil)
        {
            if (esil[candidate].Chunk.Has<RootReference>())
                throw new System.ArgumentException($"{candidate.ToFixedString()} is not a solo or root entity.");
        }
    }

    /// <summary>
    /// A struct which wraps ComponentLookup<typeparamref name="T"/> and allows for performing
    /// Read-Write access in parallel when it is guaranteed safe to do so.
    /// You can implicitly cast a ComponentLookup<typeparamref name="T"/> to this type.
    /// </summary>
    /// <typeparam name="T">A type implementing IComponentData</typeparam>
    public struct TransformsComponentLookup<T> where T : unmanaged, IComponentData
    {
        [NativeDisableParallelForRestriction]
        internal ComponentLookup<T> lookup;

        /// <summary>
        /// Reads or writes the component on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        public T this[Entity soloOrRootEntity, TransformsKey key]
        {
            get
            {
                key.Validate(soloOrRootEntity);
                return lookup[soloOrRootEntity];
            }
            set
            {
                key.Validate(soloOrRootEntity);
                lookup[soloOrRootEntity] = value;
            }
        }

        /// <summary>
        /// Reads or writes the component on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        public T this[EntityInHierarchyHandle handle, TransformsKey key]
        {
            get
            {
                key.Validate(handle.root.entity);
                return lookup[handle.entity];
            }
            set
            {
                key.Validate(handle.root.entity);
                lookup[handle.entity] = value;
            }
        }

        /// <summary>
        /// Acquires a RefRW to the component on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>A RefRW that provides direct access to component storage.</returns>
        public RefRW<T> GetRW(Entity soloOrRootEntity, TransformsKey key)
        {
            key.Validate(soloOrRootEntity);
            return lookup.GetRefRW(soloOrRootEntity);
        }

        /// <summary>
        /// Acquires a RefRW to the component on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>A RefRW that provides direct access to component storage.</returns>
        public RefRW<T> GetRW(EntityInHierarchyHandle handle, TransformsKey key)
        {
            key.Validate(handle.root.entity);
            return lookup.GetRefRW(handle.entity);
        }

        /// <summary>
        /// Fetches the component on the solo or root entity secured with the key has the component.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <param name="componentData">The fetched component</param>
        /// <returns>True if the entity had the component</returns>
        public bool TryGetComponent(Entity soloOrRootEntity, TransformsKey key, out T componentData)
        {
            key.Validate(soloOrRootEntity);
            return lookup.TryGetComponent(soloOrRootEntity, out componentData);
        }

        /// <summary>
        /// Fetches the component on the entity handle secured with the key has the component.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <param name="componentData">The fetched component</param>
        /// <returns>True if the entity had the component</returns>
        public bool TryGetComponent(EntityInHierarchyHandle handle, TransformsKey key, out T componentData)
        {
            key.Validate(handle.root.entity);
            return lookup.TryGetComponent(handle.entity, out componentData);
        }

        /// <summary>
        /// Checks entity has the component specified.
        /// This check is always valid regardless of whether such a component would be
        /// safe to access.
        /// </summary>
        public bool HasComponent(Entity entity) => lookup.HasComponent(entity);

        /// <summary>
        /// Checks if the entity handle has the component specified.
        /// This check is always valid regardless of whether such a component would be
        /// safe to access.
        /// </summary>
        public bool HasComponent(EntityInHierarchyHandle handle) => lookup.HasComponent(handle.entity);

        /// <summary>
        /// This is identical to ComponentDataFromEntity<typeparamref name="T"/>.DidChange().
        /// Note that neither method is deterministic and both can be prone to race conditions.
        /// </summary>
        public bool DidChange(Entity entity, uint version) => lookup.DidChange(entity, version);

        /// <summary>
        /// This is identical to ComponentDataFromEntity<typeparamref name="T"/>.DidChange().
        /// Note that neither method is deterministic and both can be prone to race conditions.
        /// </summary>
        public bool DidChange(EntityInHierarchyHandle handle, uint version) => lookup.DidChange(handle.entity, version);

        /// <summary>
        /// Fetches the enabled bit of the component on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The component must be of type IEnableableComponent.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>The enabled state as a boolean</returns>
        public bool IsEnabled(Entity soloOrRootEntity, TransformsKey key)
        {
            key.Validate(soloOrRootEntity);
            return lookup.IsComponentEnabled(soloOrRootEntity);
        }

        /// <summary>
        /// Fetches the enabled bit of the component on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The component must be of type IEnableableComponent.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>The enabled state as a boolean</returns>
        public bool IsEnabled(EntityInHierarchyHandle handle, TransformsKey key)
        {
            key.Validate(handle.root.entity);
            return lookup.IsComponentEnabled(handle.entity);
        }

        /// <summary>
        /// Sets the enabled bit of the component on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The component must be of type IEnableableComponent.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <param name="enabled">The new enabled state of the component</param>
        /// <remarks>This method performs an atomic operation which may suffer from worse performance than setting a normal bool field.</remarks>
        public void SetEnabled(Entity soloOrRootEntity, TransformsKey key, bool enabled)
        {
            key.Validate(soloOrRootEntity);
            lookup.SetComponentEnabled(soloOrRootEntity, enabled);
        }

        /// <summary>
        /// Sets the enabled bit of the component on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The component must be of type IEnableableComponent.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <param name="enabled">The new enabled state of the component</param>
        /// <remarks>This method performs an atomic operation which may suffer from worse performance than setting a normal bool field.</remarks>
        public void SetEnabled(EntityInHierarchyHandle handle, TransformsKey key, bool enabled)
        {
            key.Validate(handle.root.entity);
            lookup.SetComponentEnabled(handle.entity, enabled);
        }

        public static implicit operator TransformsComponentLookup<T>(ComponentLookup<T> componentLookup)
        {
            return new TransformsComponentLookup<T> { lookup = componentLookup };
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
    }

    /// <summary>
    /// A struct which wraps BufferLookup<typeparamref name="T"/> and allows for performing
    /// Read-Write access in parallel when it is guaranteed safe to do so.
    /// You can implicitly cast a BufferLookup<typeparamref name="T"/> to this type.
    /// </summary>
    /// <typeparam name="T">A type implementing IBufferElementData</typeparam>
    public struct TransformsBufferLookup<T> where T : unmanaged, IBufferElementData
    {
        [NativeDisableParallelForRestriction]
        internal BufferLookup<T> lookup;

        /// <summary>
        /// Gets a reference to the buffer on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        public DynamicBuffer<T> this[Entity soloOrRootEntity, TransformsKey key]
        {
            get
            {
                key.Validate(soloOrRootEntity);
                return lookup[soloOrRootEntity];
            }
        }

        /// <summary>
        /// Gets a reference to the buffer on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        public DynamicBuffer<T> this[EntityInHierarchyHandle handle, TransformsKey key]
        {
            get
            {
                key.Validate(handle.root.entity);
                return lookup[handle.entity];
            }
        }

        /// <summary>
        /// Fetches the buffer on the solo or root entity secured with the key if the entity has the buffer type.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <param name="bufferData">The fetched buffer</param>
        /// <returns>True if the entity had the buffer type</returns>
        public bool TryGetComponent(Entity soloOrRootEntity, TransformsKey key, out DynamicBuffer<T> bufferData)
        {
            key.Validate(soloOrRootEntity);
            return lookup.TryGetBuffer(soloOrRootEntity, out bufferData);
        }

        /// <summary>
        /// Fetches the buffer on the entity handle secured with the key if the entity has the buffer type.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <param name="bufferData">The fetched buffer</param>
        /// <returns>True if the entity had the buffer type</returns>
        public bool TryGetComponent(EntityInHierarchyHandle handle, TransformsKey key, out DynamicBuffer<T> bufferData)
        {
            key.Validate(handle.root.entity);
            return lookup.TryGetBuffer(handle.entity, out bufferData);
        }

        /// <summary>
        /// Checks if the entity has the buffer type specified.
        /// This check is always valid regardless of whether such a buffer would be
        /// safe to access.
        /// </summary>
        public bool HasBuffer(Entity entity) => lookup.HasBuffer(entity);

        /// <summary>
        /// Checks if the entity handle has the buffer type specified.
        /// This check is always valid regardless of whether such a buffer would be
        /// safe to access.
        /// </summary>
        public bool HasBuffer(EntityInHierarchyHandle handle) => lookup.HasBuffer(handle.entity);

        /// <summary>
        /// This is identical to BufferFromEntity<typeparamref name="T"/>.DidChange().
        /// Note that neither method is deterministic and both can be prone to race conditions.
        /// </summary>
        public bool DidChange(Entity entity, uint version) => lookup.DidChange(entity, version);

        /// <summary>
        /// This is identical to BufferFromEntity<typeparamref name="T"/>.DidChange().
        /// Note that neither method is deterministic and both can be prone to race conditions.
        /// </summary>
        public bool DidChange(EntityInHierarchyHandle handle, uint version) => lookup.DidChange(handle.entity, version);

        /// <summary>
        /// Fetches the enabled bit of the buffer on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The buffer must be of type IEnableableComponent.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>The enabled state as a boolean</returns>
        public bool IsEnabled(Entity soloOrRootEntity, TransformsKey key)
        {
            key.Validate(soloOrRootEntity);
            return lookup.IsBufferEnabled(soloOrRootEntity);
        }

        /// <summary>
        /// Fetches the enabled bit of the buffer on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The buffer must be of type IEnableableComponent.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>The enabled state as a boolean</returns>
        public bool IsEnabled(EntityInHierarchyHandle handle, TransformsKey key)
        {
            key.Validate(handle.root.entity);
            return lookup.IsBufferEnabled(handle.entity);
        }

        /// <summary>
        /// Sets the enabled bit of the buffer on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The buffer must be of type IEnableableComponent.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <param name="enabled">The new enabled state of the buffer</param>
        /// <remarks>This method performs an atomic operation which may suffer from worse performance than setting a normal bool field.</remarks>
        public void SetEnabled(Entity soloOrRootEntity, TransformsKey key, bool enabled)
        {
            key.Validate(soloOrRootEntity);
            lookup.SetBufferEnabled(soloOrRootEntity, enabled);
        }

        /// <summary>
        /// Sets the enabled bit of the buffer on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot be guaranteed.
        /// The buffer must be of type IEnableableComponent.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <param name="enabled">The new enabled state of the buffer</param>
        /// <remarks>This method performs an atomic operation which may suffer from worse performance than setting a normal bool field.</remarks>
        public void SetEnabled(EntityInHierarchyHandle handle, TransformsKey key, bool enabled)
        {
            key.Validate(handle.root.entity);
            lookup.SetBufferEnabled(handle.entity, enabled);
        }

        public static implicit operator TransformsBufferLookup<T>(BufferLookup<T> bufferFromEntity)
        {
            return new TransformsBufferLookup<T> { lookup = bufferFromEntity };
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
    }

    public static class TransformsKeyExtensions
    {
        /// <summary>
        /// Gets a key that allows safe access to all hierarchies and solo entities whose roots exist in this chunk.
        /// </summary>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup used to validate root entities belong to the chunk</param>
        /// <returns>A key which can be used to access components from any solo or root entity within this chunk or any entity within their hierarchies</returns>
        public static TransformsKey GetChunkTransformsKey(in this ArchetypeChunk chunk, in EntityStorageInfoLookup entityStorageInfoLookup)
        {
            ValidateRootChunk(in chunk);
            return new TransformsKey
            {
                chunkIndex  = chunk.GetHashCode(),
                entityIndex = -1,
                esil        = entityStorageInfoLookup
            };
        }

        /// <summary>
        /// Acquires an optional RefRO to the component on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>A RefRO instance which may or may not be valid (use IsValid to check)</returns>
        public static RefRO<T> GetRO<T>(ref this ComponentBroker broker, Entity soloOrRootEntity, TransformsKey key) where T : unmanaged, IComponentData
        {
            if (broker.IsReadOnlyAccessType<T>())
                return broker.GetRO<T>(soloOrRootEntity);
            key.Validate(soloOrRootEntity);
            return broker.GetROIgnoreParallelSafety<T>(soloOrRootEntity);
        }

        /// <summary>
        /// Acquires an optional RefRO to the component on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>A RefRO instance which may or may not be valid (use IsValid to check)</returns>
        public static RefRO<T> GetRO<T>(ref this ComponentBroker broker, EntityInHierarchyHandle handle, TransformsKey key) where T : unmanaged, IComponentData
        {
            if (broker.IsReadOnlyAccessType<T>())
                return broker.GetRO<T>(handle.entity);
            key.Validate(handle.root.entity);
            return broker.GetROIgnoreParallelSafety<T>(handle.entity);
        }

        /// <summary>
        /// Acquires an optional RefRW to the component on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>A RefRW instance which may or may not be valid (use IsValid to check)</returns>
        public static RefRW<T> GetRW<T>(ref this ComponentBroker broker, Entity soloOrRootEntity, TransformsKey key) where T : unmanaged, IComponentData
        {
            key.Validate(soloOrRootEntity);
            return broker.GetRWIgnoreParallelSafety<T>(soloOrRootEntity);
        }

        /// <summary>
        /// Acquires an optional RefRW to the component on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>A RefRW instance which may or may not be valid (use IsValid to check)</returns>
        public static RefRW<T> GetRW<T>(ref this ComponentBroker broker, EntityInHierarchyHandle handle, TransformsKey key) where T : unmanaged, IComponentData
        {
            key.Validate(handle.root.entity);
            return broker.GetRWIgnoreParallelSafety<T>(handle.entity);
        }

        /// <summary>
        /// Acquires an optional DynamicBuffer on the solo or root entity secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="soloOrRootEntity">The entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>A DynamicBuffer instance which may or may not be valid (use IsCreated to check)</returns>
        public static DynamicBuffer<T> GetBuffer<T>(ref this ComponentBroker broker, Entity soloOrRootEntity, TransformsKey key) where T : unmanaged, IBufferElementData
        {
            if (broker.IsReadOnlyAccessType<T>())
                return broker.GetBuffer<T>(soloOrRootEntity);
            key.Validate(soloOrRootEntity);
            return broker.GetBufferIgnoreParallelSafety<T>(soloOrRootEntity);
        }

        /// <summary>
        /// Acquires an optional DynamicBuffer on the entity handle secured with the key.
        /// When safety checks are enabled, this throws when parallel safety cannot
        /// be guaranteed.
        /// </summary>
        /// <param name="handle">The hierarchy handle of the entity to read or write</param>
        /// <param name="key">A key that validates the entity is safe to access</param>
        /// <returns>A DynamicBuffer instance which may or may not be valid (use IsCreated to check)</returns>
        public static DynamicBuffer<T> GetBuffer<T>(ref this ComponentBroker broker, EntityInHierarchyHandle handle, TransformsKey key) where T : unmanaged,
        IBufferElementData
        {
            if (broker.IsReadOnlyAccessType<T>())
                return broker.GetBuffer<T>(handle.entity);
            key.Validate(handle.root.entity);
            return broker.GetBufferIgnoreParallelSafety<T>(handle.entity);
        }

        internal static ref ComponentLookup<T> GetCheckedLookup<T>(ref this TransformsComponentLookup<T> locked, Entity rootEntity,
                                                                   TransformsKey key) where T : unmanaged, IComponentData
        {
            key.Validate(rootEntity);
            return ref locked.lookup;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ValidateRootChunk(in this ArchetypeChunk chunk)
        {
            if (chunk.Has<RootReference>())
                throw new System.ArgumentException($"The chunk does not contain solo or root entities.");
        }
    }
}
#endif

