using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// A builder API for creating a ComponentBroker. You can specify both component types and aspects to be included.
    /// </summary>
    public unsafe struct ComponentBrokerBuilder : IDisposable
    {
        internal UnsafeHashSet<ComponentType> types;
        internal UnsafeList<ComponentType>    aspectCache;

        /// <summary>
        /// Starts a builder using the allocator. Usually you pass in Allocator.Temp.
        /// </summary>
        /// <param name="allocator"></param>
        public ComponentBrokerBuilder(AllocatorManager.AllocatorHandle allocator)
        {
            types       = new UnsafeHashSet<ComponentType>(128, allocator);
            aspectCache = new UnsafeList<ComponentType>(16, allocator);
        }

        /// <summary>
        /// Disposes the builder if you used an allocator type that requires explicit disposal.
        /// </summary>
        public void Dispose()
        {
            types.Dispose();
            aspectCache.Dispose();
        }

        /// <summary>
        /// Add a component type to the builder. It uses the ReadOnly or ReadWrite mode of the type to determine
        /// whether the component type will be read-only or read-write in the ComponentBroker.
        /// If the same component type is added with both ReadOnly and ReadWrite, then the component will be
        /// read-write.
        /// </summary>
        /// <param name="type">The type of component to add, along with its ReadOnly or ReadWrite status.</param>
        public ComponentBrokerBuilder With(ComponentType type)
        {
            bool readOnly      = type.AccessModeType == ComponentType.AccessMode.ReadOnly;
            var  readOnlyType  = ComponentType.ReadOnly(type.TypeIndex);
            var  readWriteType = ComponentType.ReadWrite(type.TypeIndex);
            if (!readOnly && types.Contains(readOnlyType))
            {
                types.Remove(readOnlyType);
                types.Add(readWriteType);
            }
            else if (!readOnly || !types.Contains(readWriteType))
                types.Add(readOnly ? readOnlyType : readWriteType);
            return this;
        }

        /// <summary>
        /// Adds a component type to the builder, and specify if it should be read-only or read-write.
        /// If the same component type is added with both ReadOnly and ReadWrite, then the component will be
        /// read-write.
        /// </summary>
        /// <typeparam name="T">The component type to add</typeparam>
        /// <param name="readOnly">Whether the component should be read-only. If false, it will be read-write.</param>
        public ComponentBrokerBuilder With<T>(bool readOnly = false) => With(readOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
        /// <summary>
        /// Adds the component types to the builder, and specify if they should be read-only or read-write.
        /// If the same component type is added with both ReadOnly and ReadWrite, then the component will be
        /// read-write.
        /// </summary>
        /// <typeparam name="T0">The first component type to add</typeparam>
        /// <typeparam name="T1">The second component type to add</typeparam>
        /// <param name="readOnly">Whether the components should be read-only. If false, they will be read-write.</param>
        public ComponentBrokerBuilder With<T0, T1>(bool readOnly = false) => With<T0>(readOnly).With<T1>(readOnly);
        /// <summary>
        /// Adds the component types to the builder, and specify if they should be read-only or read-write.
        /// If the same component type is added with both ReadOnly and ReadWrite, then the component will be
        /// read-write.
        /// </summary>
        /// <typeparam name="T0">The first component type to add</typeparam>
        /// <typeparam name="T1">The second component type to add</typeparam>
        /// <typeparam name="T2">The third component type to add</typeparam>
        /// <param name="readOnly">Whether the components should be read-only. If false, they will be read-write.</param>
        public ComponentBrokerBuilder With<T0, T1, T2>(bool readOnly = false) => With<T0, T1>(readOnly).With<T2>(readOnly);
        /// <summary>
        /// Adds the component types to the builder, and specify if they should be read-only or read-write.
        /// If the same component type is added with both ReadOnly and ReadWrite, then the component will be
        /// read-write.
        /// </summary>
        /// <typeparam name="T0">The first component type to add</typeparam>
        /// <typeparam name="T1">The second component type to add</typeparam>
        /// <typeparam name="T2">The third component type to add</typeparam>
        /// <typeparam name="T3">The fourth component type to add</typeparam>
        /// <param name="readOnly">Whether the components should be read-only. If false, they will be read-write.</param>
        public ComponentBrokerBuilder With<T0, T1, T2, T3>(bool readOnly = false) => With<T0, T1>(readOnly).With<T2, T3>(readOnly);
        /// <summary>
        /// Adds the component types to the builder, and specify if they should be read-only or read-write.
        /// If the same component type is added with both ReadOnly and ReadWrite, then the component will be
        /// read-write.
        /// </summary>
        /// <typeparam name="T0">The first component type to add</typeparam>
        /// <typeparam name="T1">The second component type to add</typeparam>
        /// <typeparam name="T2">The third component type to add</typeparam>
        /// <typeparam name="T3">The fourth component type to add</typeparam>
        /// <typeparam name="T4">The fifth component type to add</typeparam>
        /// <param name="readOnly">Whether the components should be read-only. If false, they will be read-write.</param>
        public ComponentBrokerBuilder With<T0, T1, T2, T3, T4>(bool readOnly = false) => With<T0, T1>(readOnly).With<T2, T3, T4>(readOnly);

        // Todo: These only give us required components (which are kinda buggy anyways). We need something better, which would probably require source generation or reflection.
        // /// <summary>
        // /// Adds the component types required by the aspect to the builder, using the same read-only and read-write
        // /// requirements the aspect specifies
        // /// </summary>
        // /// <typeparam name="T">The aspect type to add</typeparam>
        // public ComponentBrokerBuilder WithAspect<T>() where T : unmanaged, IAspect, IAspectCreate<T>
        // {
        //     aspectCache.Clear();
        //     default(T).AddComponentRequirementsTo(ref aspectCache);
        //     foreach (var c in aspectCache)
        //     {
        //         With(c);
        //     }
        //     return this;
        // }
        // /// <summary>
        // /// Adds the component types required by the aspects to the builder, using the same read-only and read-write
        // /// requirements the aspects specify
        // /// </summary>
        // /// <typeparam name="T0">The first aspect type to add</typeparam>
        // /// <typeparam name="T1">The second aspect type to add</typeparam>
        // public ComponentBrokerBuilder WithAspect<T0, T1>() where T0 : unmanaged, IAspect, IAspectCreate<T0>
        //     where T1 : unmanaged, IAspect, IAspectCreate<T1> =>
        // WithAspect<T0>().WithAspect<T1>();
        // /// <summary>
        // /// Adds the component types required by the aspects to the builder, using the same read-only and read-write
        // /// requirements the aspects specify
        // /// </summary>
        // /// <typeparam name="T0">The first aspect type to add</typeparam>
        // /// <typeparam name="T1">The second aspect type to add</typeparam>
        // /// <typeparam name="T2">The third aspect type to add</typeparam>
        // public ComponentBrokerBuilder WithAspect<T0, T1, T2>() where T0 : unmanaged, IAspect, IAspectCreate<T0>
        //     where T1 : unmanaged, IAspect, IAspectCreate<T1>
        //     where T2 : unmanaged, IAspect, IAspectCreate<T2> =>
        // WithAspect<T0>().WithAspect<T1>().WithAspect<T2>();
        /// <summary>
        /// Finalize and construct the ComponentBroker
        /// </summary>
        /// <param name="state">The systemState to use for registering dependencies</param>
        /// <param name="allocator">The allocator to use for the ComponentBroker. This should persist through the lifetime of the Broker.</param>
        /// <returns>The fully constructed ComponentBroker</returns>
        public ComponentBroker Build(ref SystemState state, AllocatorManager.AllocatorHandle allocator) => new ComponentBroker(ref state, this, allocator);
    }

    /// <summary>
    /// A struct which allows for storing handles for many different components at once, and allowing those components to be accessed via generics.
    /// This is NOT as performant as using explicit typed handles directly in a job, and it is also more error-prone. It's purpose is to provide a
    /// convenient API for situations where function pointers are involved and the component types accessed are somewhat unpredictable. Unika is one
    /// such use case.
    /// </summary>
    public unsafe struct ComponentBroker : IDisposable
    {
        #region API
        /// <summary>
        /// Provides an EntityStorageInfoLookup instance for convenience.
        /// </summary>
        public EntityStorageInfoLookup entityStorageInfoLookup => esil;
        /// <summary>
        /// Provides an EntityTypeHandle for convenience.
        /// </summary>
        public EntityTypeHandle entityTypeHandle => esil.AsEntityTypeHandle();

        /// <summary>
        /// Sets up the ComponentBroker to permit read-write access for components on the entity in a parallel job.
        /// Using this incorrectly (when you don't have exclusive access to the entity's chunk) will result in race conditions.
        /// An example of correct usage is inside an IJobChunk using the ArchetypeChunk passed into the Execute() method.
        /// </summary>
        /// <param name="chunk">The chunk in which the calling context has exclusive access to</param>
        /// <param name="indexInChunk">The entity index within the chunk</param>
        public void SetupEntity(in ArchetypeChunk chunk, int indexInChunk)
        {
            if (chunk != currentChunk)
            {
                currentChunk        = chunk;
                currentEntity       = chunk.GetEntityDataPtrRO(entityTypeHandle)[indexInChunk];
                currentIndexInChunk = indexInChunk;
            }

            if (indexInChunk != currentIndexInChunk)
            {
                currentIndexInChunk = indexInChunk;
                currentEntity       = chunk.GetEntityDataPtrRO(entityTypeHandle)[indexInChunk];
            }
        }

        /// <summary>
        /// Warning: Only ever call this method inside a job that is scheduled single-threaded!
        ///
        /// IJobFor will specify its job range identically when scheduling single-threaded vs scheduling
        /// parallel with an arrayLength <= innerloopBatchCount, as it always uses parallel scheduling internally.
        /// Because of this, ComponentBroker has to either treat this scenario either as safe or unsafe. By default,
        /// it treats it as unsafe, but calling this method will relax it. The result of this method is if you have
        /// an IJobFor that only touches a single element (a single ArchetypeChunk or a single found pair in Psyshock),
        /// then no exceptions will be thrown even if the ComponentBroker safety rule is violated.
        ///
        /// </summary>
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public void RelaxSafetyForIJobFor()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            singleThreadedSafetyChecker.RelaxForIJobFor();
#endif
        }

        /// <summary>
        /// Returns true if the entity exists
        /// </summary>
        public bool Exists(Entity entity) => esil.Exists(entity);

        /// <summary>
        /// Checks if the entity has the specific component type. The type does not necessarily need to be a type
        /// in the ComponentBroker, but checking such a type may be slower.
        /// </summary>
        /// <typeparam name="T">The component type to check</typeparam>
        /// <param name="entity">The entity to check for</param>
        /// <returns>True if the entity has the type, false otherwise</returns>
        public bool Has<T>(Entity entity)
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            if (typeIndex >= handleIndices.Length || !handleIndices[typeIndex.Index].isValid)
            {
                // Fallback to slow path
                if (entity == currentEntity)
                {
                    return currentChunk.Has<T>();
                }
                else
                {
                    var info = esil[entity];
                    return info.Chunk.Has<T>();
                }
            }
            if (typeIndex.IsSharedComponentType)
            {
                fixed (DynamicSharedComponentTypeHandle* s0Ptr = &s0)
                {
                    ref var handle = ref s0Ptr[handleIndices[typeIndex.Index].index];
                    if (entity == currentEntity)
                    {
                        return currentChunk.Has(ref handle);
                    }
                    else
                    {
                        var info = esil[entity];
                        return info.Chunk.Has(ref handle);
                    }
                }
            }
            else
            {
                fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
                {
                    ref var handle = ref c0Ptr[handleIndices[typeIndex.Index].index];
                    if (entity == currentEntity)
                    {
                        return currentChunk.Has(ref handle);
                    }
                    else
                    {
                        var info = esil[entity];
                        return info.Chunk.Has(ref handle);
                    }
                }
            }
        }

        /// <summary>
        /// Gets an optional RefRO to the component on the specified entity. If the component is a read-write type
        /// and the entity is not the entity used last in SetupEntity(), then a parallel job safety check
        /// may trigger an exception.
        /// </summary>
        /// <typeparam name="T">The type of component to get</typeparam>
        /// <param name="entity">The entity to get the component from.</param>
        /// <returns>A RefRO instance which may or may not be valid (use IsValid to check)</returns>
        public RefRO<T> GetRO<T>(Entity entity) where T : unmanaged, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    var array = currentChunk.GetDynamicComponentDataArrayReinterpret<T>(ref handle, UnsafeUtility.SizeOf<T>());
                    return RefRO<T>.Optional(array, currentIndexInChunk);
                }
                else
                {
                    CheckSafeAccessForForeignEntity(ref handle);
                    var info  = esil[entity];
                    var array = info.Chunk.GetDynamicComponentDataArrayReinterpret<T>(ref handle, UnsafeUtility.SizeOf<T>());
                    return RefRO<T>.Optional(array, info.IndexInChunk);
                }
            }
        }

        /// <summary>
        /// Gets an optional RefRW to the component on the specified entity. If  the entity is not the
        /// entity used last in SetupEntity(), then a parallel job safety check may trigger an exception.
        /// </summary>
        /// <typeparam name="T">The type of component to get</typeparam>
        /// <param name="entity">The entity to get the component from.</param>
        /// <returns>A RefRW instance which may or may not be valid (use IsValid to check)</returns>
        public RefRW<T> GetRW<T>(Entity entity) where T : unmanaged, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    var array = currentChunk.GetDynamicComponentDataArrayReinterpret<T>(ref handle, UnsafeUtility.SizeOf<T>());
                    return RefRW<T>.Optional(array, currentIndexInChunk);
                }
                else
                {
                    CheckSafeAccessForForeignEntity(ref handle);
                    var info  = esil[entity];
                    var array = info.Chunk.GetDynamicComponentDataArrayReinterpret<T>(ref handle, UnsafeUtility.SizeOf<T>());
                    return RefRW<T>.Optional(array, info.IndexInChunk);
                }
            }
        }

        /// <summary>
        /// Gets an optional DynamicBuffer on the specified entity. If the buffer is a read-write type
        /// and the entity is not the entity used last in SetupEntity(), then a parallel job safety check
        /// may trigger an exception.
        /// </summary>
        /// <typeparam name="T">The type of buffer element to get</typeparam>
        /// <param name="entity">The entity to get the buffer from.</param>
        /// <returns>A DynamicBuffer instance which may or may not be valid (use IsCreated to check)</returns>
        public DynamicBuffer<T> GetBuffer<T>(Entity entity) where T : unmanaged, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    var access = currentChunk.GetBufferAccessor<T>(ref handle);
                    if (access.Length == 0)
                        return default;
                    return access[currentIndexInChunk];
                }
                else
                {
                    CheckSafeAccessForForeignEntity(ref handle);
                    var info   = esil[entity];
                    var access = info.Chunk.GetBufferAccessor<T>(ref handle);
                    if (access.Length == 0)
                        return default;
                    return access[info.IndexInChunk];
                }
            }
        }

        /// <summary>
        /// Sets the component enabled state for the type atomically, if present.
        /// </summary>
        /// <typeparam name="T">The type to set the enabled state for</typeparam>
        /// <param name="entity">The entity for which to set the enabled state for the type</param>
        /// <param name="enabled">The new enabled state for the type</param>
        /// <returns>True if the entity had the type, false otherwise</returns>
        public bool TrySetComponentEnabled<T>(Entity entity, bool enabled) where T : unmanaged, IEnableableComponent
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    if (currentChunk.Has(ref handle))
                    {
                        currentChunk.SetComponentEnabled(ref handle, currentIndexInChunk, enabled);
                        return true;
                    }
                }
                else
                {
                    var info = esil[entity];
                    if (info.Chunk.Has(ref handle))
                    {
                        info.Chunk.SetComponentEnabled(ref handle, info.IndexInChunk, enabled);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Gets an optional EnabledRefRO to the component on the specified entity. If the component is a read-write type
        /// and the entity is not the entity used last in SetupEntity(), then a parallel job safety check
        /// may trigger an exception. This flavor does not perform atomic operations, and therefore you need exclusive
        /// access to the entity for the full duration of the job to use safely if the type is read-write.
        /// </summary>
        /// <typeparam name="T">The type of component to get</typeparam>
        /// <param name="entity">The entity to get the component from.</param>
        /// <returns>An EnabledRefRO instance which may or may not be valid (use IsValid to check)</returns>
        public EnabledRefRO<T> GetEnabledRO<T>(Entity entity) where T : unmanaged, IEnableableComponent
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    var mask = currentChunk.GetEnabledMask(ref handle);
                    return mask.GetOptionalEnabledRefRO<T>(currentIndexInChunk);
                }
                else
                {
                    CheckSafeAccessForForeignEntity(ref handle);
                    var info = esil[entity];
                    var mask = info.Chunk.GetEnabledMask(ref handle);
                    return mask.GetOptionalEnabledRefRO<T>(info.IndexInChunk);
                }
            }
        }

        /// <summary>
        /// Gets an optional EnabledRefRW to the component on the specified entity. If  the entity is not the
        /// entity used last in SetupEntity(), then a parallel job safety check may trigger an exception.
        /// This flavor does not perform atomic operations, and therefore you need exclusive access to the
        /// whole chunk for the full duration of the job to use safely if the type is read-write.
        /// </summary>
        /// <typeparam name="T">The type of component to get</typeparam>
        /// <param name="entity">The entity to get the component from.</param>
        /// <returns>An EnabledRefRW instance which may or may not be valid (use IsValid to check)</returns>
        public EnabledRefRW<T> GetEnabledRW<T>(Entity entity) where T : unmanaged, IEnableableComponent
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    var mask = currentChunk.GetEnabledMask(ref handle);
                    return mask.GetOptionalEnabledRefRW<T>(currentIndexInChunk);
                }
                else
                {
                    CheckSafeAccessForForeignEntity(ref handle);
                    var info = esil[entity];
                    var mask = info.Chunk.GetEnabledMask(ref handle);
                    return mask.GetOptionalEnabledRefRW<T>(info.IndexInChunk);
                }
            }
        }

        /// <summary>
        /// Gets the ISharedComponentData value for the entity, if it exists. Returns true if the result is valid.
        /// </summary>
        /// <typeparam name="T">The type of shared component to get</typeparam>
        /// <param name="entity">The entity to get the shared component from</param>
        /// <param name="sharedComponentOrDefault">The shared component value if found, default otherwise</param>
        /// <returns>True if the entity has the shared component type, false otherwise</returns>
        public bool TryGetSharedComponent<T>(Entity entity, out T sharedComponentOrDefault) where T : unmanaged, ISharedComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicSharedComponentTypeHandle* s0Ptr = &s0)
            {
                ref var handle = ref s0Ptr[handleIndices[typeIndex].index];
                T*      ptr    = default;
                if (entity == currentEntity)
                {
                    ptr = (T*)currentChunk.GetDynamicSharedComponentDataAddress(ref handle);
                }
                else
                {
                    var info = esil[entity];
                    ptr      = (T*)info.Chunk.GetDynamicSharedComponentDataAddress(ref handle);
                }
                if (ptr != null)
                {
                    sharedComponentOrDefault = *ptr;
                    return true;
                }
                else
                {
                    sharedComponentOrDefault = default;
                    return false;
                }
            }
        }

        /// <summary>
        /// Queries whether the type in the ComponentBroker is marked as read-only access or read-write access.
        /// </summary>
        /// <typeparam name="T">The type of component in the ComponentBroker to query</typeparam>
        /// <returns>True if the component is only allowed to be read as read-only</returns>
        public bool IsReadOnlyAccessType<T>() where T : unmanaged
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            CheckTypeIndexIsInComponentList(typeIndex.Index);
            return typeIndex.IsSharedComponentType || handleIndices[typeIndex.Index].index >= 128;
        }

        /// <summary>
        /// Gets an optional RefRO to the component on the specified entity, ignoring parallel safety checks as if
        /// [NativeDisableParallelForRestriction] was used.
        /// </summary>
        /// <typeparam name="T">The type of component to get</typeparam>
        /// <param name="entity">The entity to get the component from.</param>
        /// <returns>A RefRO instance which may or may not be valid (use IsValid to check)</returns>
        public RefRO<T> GetROIgnoreParallelSafety<T>(Entity entity) where T : unmanaged, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    var array = currentChunk.GetDynamicComponentDataArrayReinterpret<T>(ref handle, UnsafeUtility.SizeOf<T>());
                    return RefRO<T>.Optional(array, currentIndexInChunk);
                }
                else
                {
                    var info  = esil[entity];
                    var array = info.Chunk.GetDynamicComponentDataArrayReinterpret<T>(ref handle, UnsafeUtility.SizeOf<T>());
                    return RefRO<T>.Optional(array, info.IndexInChunk);
                }
            }
        }

        /// <summary>
        /// Gets an optional RefRW to the component on the specified entity, ignoring parallel safety checks as if
        /// [NativeDisableParallelForRestriction] was used.
        /// </summary>
        /// <typeparam name="T">The type of component to get</typeparam>
        /// <param name="entity">The entity to get the component from.</param>
        /// <returns>A RefRW instance which may or may not be valid (use IsValid to check)</returns>
        public RefRW<T> GetRWIgnoreParallelSafety<T>(Entity entity) where T : unmanaged, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    var array = currentChunk.GetDynamicComponentDataArrayReinterpret<T>(ref handle, UnsafeUtility.SizeOf<T>());
                    return RefRW<T>.Optional(array, currentIndexInChunk);
                }
                else
                {
                    var info  = esil[entity];
                    var array = info.Chunk.GetDynamicComponentDataArrayReinterpret<T>(ref handle, UnsafeUtility.SizeOf<T>());
                    return RefRW<T>.Optional(array, info.IndexInChunk);
                }
            }
        }

        /// <summary>
        /// Gets an optional DynamicBuffer on the specified entity, ignoring parallel safety checks as if
        /// [NativeDisableParallelForRestriction] was used.
        /// </summary>
        /// <typeparam name="T">The type of buffer element to get</typeparam>
        /// <param name="entity">The entity to get the buffer from.</param>
        /// <returns>A DynamicBuffer instance which may or may not be valid (use IsCreated to check)</returns>
        public DynamicBuffer<T> GetBufferIgnoreParallelSafety<T>(Entity entity) where T : unmanaged, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
            {
                ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
                if (entity == currentEntity)
                {
                    var access = currentChunk.GetBufferAccessor<T>(ref handle);
                    if (access.Length == 0)
                        return default;
                    return access[currentIndexInChunk];
                }
                else
                {
                    var info   = esil[entity];
                    var access = info.Chunk.GetBufferAccessor<T>(ref handle);
                    if (access.Length == 0)
                        return default;
                    return access[info.IndexInChunk];
                }
            }
        }

        // Todo: These are disabled as they are not atomic
        //public EnabledRefRO<T> GetEnabledROIgnoreParallelSafety<T>(Entity entity) where T : unmanaged, IEnableableComponent
        //{
        //    var typeIndex = TypeManager.GetTypeIndex<T>().Index;
        //    CheckTypeIndexIsInComponentList(typeIndex);
        //    fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
        //    {
        //        ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
        //        if (entity == currentEntity)
        //        {
        //            var mask = currentChunk.GetEnabledMask(ref handle);
        //            return mask.GetOptionalEnabledRefRO<T>(currentIndexInChunk);
        //        }
        //        else
        //        {
        //            var info = esil[entity];
        //            var mask = info.Chunk.GetEnabledMask(ref handle);
        //            return mask.GetOptionalEnabledRefRO<T>(info.IndexInChunk);
        //        }
        //    }
        //}
        //
        //public EnabledRefRW<T> GetEnabledRWIgnoreParallelSafety<T>(Entity entity) where T : unmanaged, IEnableableComponent
        //{
        //    var typeIndex = TypeManager.GetTypeIndex<T>().Index;
        //    CheckTypeIndexIsInComponentList(typeIndex);
        //    fixed (DynamicComponentTypeHandle* c0Ptr = &c0)
        //    {
        //        ref var handle = ref c0Ptr[handleIndices[typeIndex].index];
        //        if (entity == currentEntity)
        //        {
        //            var mask = currentChunk.GetEnabledMask(ref handle);
        //            return mask.GetOptionalEnabledRefRW<T>(currentIndexInChunk);
        //        }
        //        else
        //        {
        //            var info = esil[entity];
        //            var mask = info.Chunk.GetEnabledMask(ref handle);
        //            return mask.GetOptionalEnabledRefRW<T>(info.IndexInChunk);
        //        }
        //    }
        //}
        #endregion

        #region Constructor, Update, and Dispose
        /// <summary>
        /// Constructs a ComponentBroker using the system's state, a builder, and the allocator for the the ComponentBroker's lifecycle
        /// </summary>
        /// <param name="state">The systemState for the system this ComponentBroker should be used within</param>
        /// <param name="builder">The builder which contains all the component types and read-write access modes that should be included</param>
        /// <param name="allocator">The allocator that dictates the lifecycle of this ComponentBroker, typically the same as the system it is used in</param>
        public ComponentBroker(ref SystemState state, ComponentBrokerBuilder builder, AllocatorManager.AllocatorHandle allocator)
        {
            this = default;
            esil = state.GetEntityStorageInfoLookup();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            singleThreadedSafetyChecker = new ParallelDetector(allocator);
#endif

            int maxTypeIndex = 0;
            foreach (var c in builder.types)
                maxTypeIndex = math.max(maxTypeIndex, c.TypeIndex.Index);

            handleIndices = CollectionHelper.CreateNativeArray<DynamicIndex>(maxTypeIndex + 1, allocator, NativeArrayOptions.UninitializedMemory);
            handleIndices.AsSpan().Fill(new DynamicIndex(0xff));
            int                                cCountRW = 0;
            int                                cCountRO = 0;
            int                                sCount   = 0;
            fixed (DynamicComponentTypeHandle* c0Ptr    = &c0)
            {
                fixed(DynamicSharedComponentTypeHandle* s0Ptr = &s0)
                {
                    foreach (var c in builder.types)
                    {
                        if (c.IsSharedComponent)
                        {
                            if (sCount >= 16)
                            {
                                throw new InvalidOperationException(
                                    $"ComponentBroker can only store up to 16 ISharedComponentData types. If you need more than this, please make a request! Total count = {builder.types.Count}");
                            }
                            s0Ptr[sCount]                    = state.GetDynamicSharedComponentTypeHandle(c);
                            handleIndices[c.TypeIndex.Index] = new DynamicIndex(sCount);
                            sCount++;
                        }
                        else if (c.AccessModeType == ComponentType.AccessMode.ReadWrite)
                        {
                            if (cCountRW >= 128)
                            {
                                throw new InvalidOperationException(
                                    $"ComponentBroker can only store up to 128 IComponentData and IBufferElementData types with read-write access. If you need more than this, please make a request! Total count = {builder.types.Count}");
                            }
                            c0Ptr[cCountRW]                  = state.GetDynamicComponentTypeHandle(c);
                            handleIndices[c.TypeIndex.Index] = new DynamicIndex(cCountRW);
                            cCountRW++;
                        }
                        else
                        {
                            if (cCountRO >= 127)
                            {
                                throw new InvalidOperationException(
                                    $"ComponentBroker can only store up to 255 IComponentData and IBufferElementData types with read-only access. If you need more than this, please make a request! Total count = {builder.types.Count}");
                            }
                            c0Ptr[cCountRO + 128]            = state.GetDynamicComponentTypeHandle(c);
                            handleIndices[c.TypeIndex.Index] = new DynamicIndex(cCountRO + 128);
                            cCountRO++;
                        }
                    }

                    while (sCount < 16)
                    {
                        s0Ptr[sCount] = state.GetDynamicSharedComponentTypeHandle(ComponentType.ReadOnly<DummySharedComponent>());
                        sCount++;
                    }
                }
            }
            readWriteCount = cCountRW;
            readOnlyCount  = cCountRO;
            sharedCount    = sCount;
        }

        /// <summary>
        /// Updates the handles of all ComponentTypes in the ComponentBroker. Call this in the system's OnUpdate() method before scheduling jobs.
        /// </summary>
        /// <param name="state">The SystemState of the system typically passed into OnUpdate()</param>
        public void Update(ref SystemState state)
        {
            esil.Update(ref state);

            fixed (DynamicComponentTypeHandle* ptr = &c0)
            {
                for (int i = 0; i < readWriteCount; i++)
                    ptr[i].Update(ref state);
                for (int i = 0; i < readOnlyCount; i++)
                    ptr[i + 128].Update(ref state);
            }
            fixed (DynamicSharedComponentTypeHandle* ptr = &s0)
            {
                for (int i = 0; i < sharedCount; i++)
                    ptr[i].Update(ref state);
            }
        }

        /// <summary>
        /// Disposes the ComponentBroker. Typically called in the system's OnDestroy() method.
        /// </summary>
        public void Dispose()
        {
            handleIndices.Dispose();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            singleThreadedSafetyChecker.Dispose();
#endif
        }
        #endregion

        #region Fields
        internal struct DummySharedComponent : ISharedComponentData { public byte dummyValue; }

        internal struct DynamicIndex
        {
            byte packed;
            public int index => packed;
            public bool isValid => packed != 0xff;
            public DynamicIndex(int index)
            {
                packed = (byte)index;
            }
        }

        ArchetypeChunk currentChunk;
        Entity         currentEntity;
        int            currentIndexInChunk;

        int readWriteCount;
        int readOnlyCount;
        int sharedCount;

        [ReadOnly] internal NativeArray<DynamicIndex> handleIndices;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        ParallelDetector singleThreadedSafetyChecker;
#endif

        [ReadOnly] EntityStorageInfoLookup esil;

        internal DynamicComponentTypeHandle c0;
        DynamicComponentTypeHandle          c1;
        DynamicComponentTypeHandle          c2;
        DynamicComponentTypeHandle          c3;
        DynamicComponentTypeHandle          c4;
        DynamicComponentTypeHandle          c5;
        DynamicComponentTypeHandle          c6;
        DynamicComponentTypeHandle          c7;
        DynamicComponentTypeHandle          c8;
        DynamicComponentTypeHandle          c9;

        DynamicComponentTypeHandle c10;
        DynamicComponentTypeHandle c11;
        DynamicComponentTypeHandle c12;
        DynamicComponentTypeHandle c13;
        DynamicComponentTypeHandle c14;
        DynamicComponentTypeHandle c15;
        DynamicComponentTypeHandle c16;
        DynamicComponentTypeHandle c17;
        DynamicComponentTypeHandle c18;
        DynamicComponentTypeHandle c19;

        DynamicComponentTypeHandle c20;
        DynamicComponentTypeHandle c21;
        DynamicComponentTypeHandle c22;
        DynamicComponentTypeHandle c23;
        DynamicComponentTypeHandle c24;
        DynamicComponentTypeHandle c25;
        DynamicComponentTypeHandle c26;
        DynamicComponentTypeHandle c27;
        DynamicComponentTypeHandle c28;
        DynamicComponentTypeHandle c29;

        DynamicComponentTypeHandle c30;
        DynamicComponentTypeHandle c31;
        DynamicComponentTypeHandle c32;
        DynamicComponentTypeHandle c33;
        DynamicComponentTypeHandle c34;
        DynamicComponentTypeHandle c35;
        DynamicComponentTypeHandle c36;
        DynamicComponentTypeHandle c37;
        DynamicComponentTypeHandle c38;
        DynamicComponentTypeHandle c39;

        DynamicComponentTypeHandle c40;
        DynamicComponentTypeHandle c41;
        DynamicComponentTypeHandle c42;
        DynamicComponentTypeHandle c43;
        DynamicComponentTypeHandle c44;
        DynamicComponentTypeHandle c45;
        DynamicComponentTypeHandle c46;
        DynamicComponentTypeHandle c47;
        DynamicComponentTypeHandle c48;
        DynamicComponentTypeHandle c49;

        DynamicComponentTypeHandle c50;
        DynamicComponentTypeHandle c51;
        DynamicComponentTypeHandle c52;
        DynamicComponentTypeHandle c53;
        DynamicComponentTypeHandle c54;
        DynamicComponentTypeHandle c55;
        DynamicComponentTypeHandle c56;
        DynamicComponentTypeHandle c57;
        DynamicComponentTypeHandle c58;
        DynamicComponentTypeHandle c59;

        DynamicComponentTypeHandle c60;
        DynamicComponentTypeHandle c61;
        DynamicComponentTypeHandle c62;
        DynamicComponentTypeHandle c63;
        DynamicComponentTypeHandle c64;
        DynamicComponentTypeHandle c65;
        DynamicComponentTypeHandle c66;
        DynamicComponentTypeHandle c67;
        DynamicComponentTypeHandle c68;
        DynamicComponentTypeHandle c69;

        DynamicComponentTypeHandle c70;
        DynamicComponentTypeHandle c71;
        DynamicComponentTypeHandle c72;
        DynamicComponentTypeHandle c73;
        DynamicComponentTypeHandle c74;
        DynamicComponentTypeHandle c75;
        DynamicComponentTypeHandle c76;
        DynamicComponentTypeHandle c77;
        DynamicComponentTypeHandle c78;
        DynamicComponentTypeHandle c79;

        DynamicComponentTypeHandle c80;
        DynamicComponentTypeHandle c81;
        DynamicComponentTypeHandle c82;
        DynamicComponentTypeHandle c83;
        DynamicComponentTypeHandle c84;
        DynamicComponentTypeHandle c85;
        DynamicComponentTypeHandle c86;
        DynamicComponentTypeHandle c87;
        DynamicComponentTypeHandle c88;
        DynamicComponentTypeHandle c89;

        DynamicComponentTypeHandle c90;
        DynamicComponentTypeHandle c91;
        DynamicComponentTypeHandle c92;
        DynamicComponentTypeHandle c93;
        DynamicComponentTypeHandle c94;
        DynamicComponentTypeHandle c95;
        DynamicComponentTypeHandle c96;
        DynamicComponentTypeHandle c97;
        DynamicComponentTypeHandle c98;
        DynamicComponentTypeHandle c99;

        DynamicComponentTypeHandle c100;
        DynamicComponentTypeHandle c101;
        DynamicComponentTypeHandle c102;
        DynamicComponentTypeHandle c103;
        DynamicComponentTypeHandle c104;
        DynamicComponentTypeHandle c105;
        DynamicComponentTypeHandle c106;
        DynamicComponentTypeHandle c107;
        DynamicComponentTypeHandle c108;
        DynamicComponentTypeHandle c109;

        DynamicComponentTypeHandle c110;
        DynamicComponentTypeHandle c111;
        DynamicComponentTypeHandle c112;
        DynamicComponentTypeHandle c113;
        DynamicComponentTypeHandle c114;
        DynamicComponentTypeHandle c115;
        DynamicComponentTypeHandle c116;
        DynamicComponentTypeHandle c117;
        DynamicComponentTypeHandle c118;
        DynamicComponentTypeHandle c119;

        DynamicComponentTypeHandle            c120;
        DynamicComponentTypeHandle            c121;
        DynamicComponentTypeHandle            c122;
        DynamicComponentTypeHandle            c123;
        DynamicComponentTypeHandle            c124;
        DynamicComponentTypeHandle            c125;
        DynamicComponentTypeHandle            c126;
        DynamicComponentTypeHandle            c127;
        [ReadOnly] DynamicComponentTypeHandle c128;
        [ReadOnly] DynamicComponentTypeHandle c129;

        [ReadOnly] DynamicComponentTypeHandle c130;
        [ReadOnly] DynamicComponentTypeHandle c131;
        [ReadOnly] DynamicComponentTypeHandle c132;
        [ReadOnly] DynamicComponentTypeHandle c133;
        [ReadOnly] DynamicComponentTypeHandle c134;
        [ReadOnly] DynamicComponentTypeHandle c135;
        [ReadOnly] DynamicComponentTypeHandle c136;
        [ReadOnly] DynamicComponentTypeHandle c137;
        [ReadOnly] DynamicComponentTypeHandle c138;
        [ReadOnly] DynamicComponentTypeHandle c139;

        [ReadOnly] DynamicComponentTypeHandle c140;
        [ReadOnly] DynamicComponentTypeHandle c141;
        [ReadOnly] DynamicComponentTypeHandle c142;
        [ReadOnly] DynamicComponentTypeHandle c143;
        [ReadOnly] DynamicComponentTypeHandle c144;
        [ReadOnly] DynamicComponentTypeHandle c145;
        [ReadOnly] DynamicComponentTypeHandle c146;
        [ReadOnly] DynamicComponentTypeHandle c147;
        [ReadOnly] DynamicComponentTypeHandle c148;
        [ReadOnly] DynamicComponentTypeHandle c149;

        [ReadOnly] DynamicComponentTypeHandle c150;
        [ReadOnly] DynamicComponentTypeHandle c151;
        [ReadOnly] DynamicComponentTypeHandle c152;
        [ReadOnly] DynamicComponentTypeHandle c153;
        [ReadOnly] DynamicComponentTypeHandle c154;
        [ReadOnly] DynamicComponentTypeHandle c155;
        [ReadOnly] DynamicComponentTypeHandle c156;
        [ReadOnly] DynamicComponentTypeHandle c157;
        [ReadOnly] DynamicComponentTypeHandle c158;
        [ReadOnly] DynamicComponentTypeHandle c159;

        [ReadOnly] DynamicComponentTypeHandle c160;
        [ReadOnly] DynamicComponentTypeHandle c161;
        [ReadOnly] DynamicComponentTypeHandle c162;
        [ReadOnly] DynamicComponentTypeHandle c163;
        [ReadOnly] DynamicComponentTypeHandle c164;
        [ReadOnly] DynamicComponentTypeHandle c165;
        [ReadOnly] DynamicComponentTypeHandle c166;
        [ReadOnly] DynamicComponentTypeHandle c167;
        [ReadOnly] DynamicComponentTypeHandle c168;
        [ReadOnly] DynamicComponentTypeHandle c169;

        [ReadOnly] DynamicComponentTypeHandle c170;
        [ReadOnly] DynamicComponentTypeHandle c171;
        [ReadOnly] DynamicComponentTypeHandle c172;
        [ReadOnly] DynamicComponentTypeHandle c173;
        [ReadOnly] DynamicComponentTypeHandle c174;
        [ReadOnly] DynamicComponentTypeHandle c175;
        [ReadOnly] DynamicComponentTypeHandle c176;
        [ReadOnly] DynamicComponentTypeHandle c177;
        [ReadOnly] DynamicComponentTypeHandle c178;
        [ReadOnly] DynamicComponentTypeHandle c179;

        [ReadOnly] DynamicComponentTypeHandle c180;
        [ReadOnly] DynamicComponentTypeHandle c181;
        [ReadOnly] DynamicComponentTypeHandle c182;
        [ReadOnly] DynamicComponentTypeHandle c183;
        [ReadOnly] DynamicComponentTypeHandle c184;
        [ReadOnly] DynamicComponentTypeHandle c185;
        [ReadOnly] DynamicComponentTypeHandle c186;
        [ReadOnly] DynamicComponentTypeHandle c187;
        [ReadOnly] DynamicComponentTypeHandle c188;
        [ReadOnly] DynamicComponentTypeHandle c189;

        [ReadOnly] DynamicComponentTypeHandle c190;
        [ReadOnly] DynamicComponentTypeHandle c191;
        [ReadOnly] DynamicComponentTypeHandle c192;
        [ReadOnly] DynamicComponentTypeHandle c193;
        [ReadOnly] DynamicComponentTypeHandle c194;
        [ReadOnly] DynamicComponentTypeHandle c195;
        [ReadOnly] DynamicComponentTypeHandle c196;
        [ReadOnly] DynamicComponentTypeHandle c197;
        [ReadOnly] DynamicComponentTypeHandle c198;
        [ReadOnly] DynamicComponentTypeHandle c199;

        [ReadOnly] DynamicComponentTypeHandle c200;
        [ReadOnly] DynamicComponentTypeHandle c201;
        [ReadOnly] DynamicComponentTypeHandle c202;
        [ReadOnly] DynamicComponentTypeHandle c203;
        [ReadOnly] DynamicComponentTypeHandle c204;
        [ReadOnly] DynamicComponentTypeHandle c205;
        [ReadOnly] DynamicComponentTypeHandle c206;
        [ReadOnly] DynamicComponentTypeHandle c207;
        [ReadOnly] DynamicComponentTypeHandle c208;
        [ReadOnly] DynamicComponentTypeHandle c209;

        [ReadOnly] DynamicComponentTypeHandle c210;
        [ReadOnly] DynamicComponentTypeHandle c211;
        [ReadOnly] DynamicComponentTypeHandle c212;
        [ReadOnly] DynamicComponentTypeHandle c213;
        [ReadOnly] DynamicComponentTypeHandle c214;
        [ReadOnly] DynamicComponentTypeHandle c215;
        [ReadOnly] DynamicComponentTypeHandle c216;
        [ReadOnly] DynamicComponentTypeHandle c217;
        [ReadOnly] DynamicComponentTypeHandle c218;
        [ReadOnly] DynamicComponentTypeHandle c219;

        [ReadOnly] DynamicComponentTypeHandle c220;
        [ReadOnly] DynamicComponentTypeHandle c221;
        [ReadOnly] DynamicComponentTypeHandle c222;
        [ReadOnly] DynamicComponentTypeHandle c223;
        [ReadOnly] DynamicComponentTypeHandle c224;
        [ReadOnly] DynamicComponentTypeHandle c225;
        [ReadOnly] DynamicComponentTypeHandle c226;
        [ReadOnly] DynamicComponentTypeHandle c227;
        [ReadOnly] DynamicComponentTypeHandle c228;
        [ReadOnly] DynamicComponentTypeHandle c229;

        [ReadOnly] DynamicComponentTypeHandle c230;
        [ReadOnly] DynamicComponentTypeHandle c231;
        [ReadOnly] DynamicComponentTypeHandle c232;
        [ReadOnly] DynamicComponentTypeHandle c233;
        [ReadOnly] DynamicComponentTypeHandle c234;
        [ReadOnly] DynamicComponentTypeHandle c235;
        [ReadOnly] DynamicComponentTypeHandle c236;
        [ReadOnly] DynamicComponentTypeHandle c237;
        [ReadOnly] DynamicComponentTypeHandle c238;
        [ReadOnly] DynamicComponentTypeHandle c239;

        [ReadOnly] DynamicComponentTypeHandle c240;
        [ReadOnly] DynamicComponentTypeHandle c241;
        [ReadOnly] DynamicComponentTypeHandle c242;
        [ReadOnly] DynamicComponentTypeHandle c243;
        [ReadOnly] DynamicComponentTypeHandle c244;
        [ReadOnly] DynamicComponentTypeHandle c245;
        [ReadOnly] DynamicComponentTypeHandle c246;
        [ReadOnly] DynamicComponentTypeHandle c247;
        [ReadOnly] DynamicComponentTypeHandle c248;
        [ReadOnly] DynamicComponentTypeHandle c249;

        [ReadOnly] DynamicComponentTypeHandle c250;
        [ReadOnly] DynamicComponentTypeHandle c251;
        [ReadOnly] DynamicComponentTypeHandle c252;
        [ReadOnly] DynamicComponentTypeHandle c253;
        [ReadOnly] DynamicComponentTypeHandle c254;

        [ReadOnly] internal DynamicSharedComponentTypeHandle s0;
        [ReadOnly] DynamicSharedComponentTypeHandle          s1;
        [ReadOnly] DynamicSharedComponentTypeHandle          s2;
        [ReadOnly] DynamicSharedComponentTypeHandle          s3;
        [ReadOnly] DynamicSharedComponentTypeHandle          s4;
        [ReadOnly] DynamicSharedComponentTypeHandle          s5;
        [ReadOnly] DynamicSharedComponentTypeHandle          s6;
        [ReadOnly] DynamicSharedComponentTypeHandle          s7;
        [ReadOnly] DynamicSharedComponentTypeHandle          s8;
        [ReadOnly] DynamicSharedComponentTypeHandle          s9;
        [ReadOnly] DynamicSharedComponentTypeHandle          s10;
        [ReadOnly] DynamicSharedComponentTypeHandle          s11;
        [ReadOnly] DynamicSharedComponentTypeHandle          s12;
        [ReadOnly] DynamicSharedComponentTypeHandle          s13;
        [ReadOnly] DynamicSharedComponentTypeHandle          s14;
        [ReadOnly] DynamicSharedComponentTypeHandle          s15;
        #endregion

        #region Safety
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckIndexInChunkWithinChunkCount(int index, int count)
        {
            if (index >= count || index < 0)
                throw new System.ArgumentOutOfRangeException($"The index in chunk {index} is outside the chunk's range [0, {count})");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void CheckTypeIndexIsInComponentList(int index)
        {
            if (index >= handleIndices.Length)
                throw new ArgumentOutOfRangeException(
                    $"The specified component type does not exist in this ComponentBroker. Component index: {index}, handleIndices.Length: {handleIndices.Length}");
            if (!handleIndices[index].isValid)
                throw new ArgumentOutOfRangeException(
                    $"The specified component type does not exist in this ComponentBroker. Component index: {index}, handleIndices value: {handleIndices[index].index}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckSafeAccessForForeignEntity(ref DynamicComponentTypeHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!handle.IsReadOnly && singleThreadedSafetyChecker.isParallel)
                throw new System.InvalidOperationException(
                    "Attempted to access a component from an external entity inside a parallel job. This is not thread-safe. Call SetupEntity() to specify an entity that is safe to access in a parallel job, such as one from an IJobChunk or IJobEntity");
#endif
        }
        #endregion
    }

    public static unsafe class ArchetypeChunkBrokerExtensions
    {
        /// <summary>
        /// Provides a native array interface to components stored in this chunk.
        /// </summary>
        /// <remarks>The native array returned by this method references existing data, not a copy.</remarks>
        /// <param name="broker">The ComponentBroker containing type and job safety information.</param>
        /// <typeparam name="T">The data type of the component.</typeparam>
        /// <exception cref="ArgumentException">If you call this function on a "tag" component type (which is an empty
        /// component with no fields).</exception>
        /// <returns>A native array containing the components in the chunk.</returns>
        /// <summary>
        public static NativeArray<T> GetNativeArray<T>(in this ArchetypeChunk chunk, ref ComponentBroker broker) where T : unmanaged, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            broker.CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &broker.c0)
            {
                return chunk.GetDynamicComponentDataArrayReinterpret<T>(ref c0Ptr[broker.handleIndices[typeIndex].index], UnsafeUtility.SizeOf<T>());
            }
        }

        /// <summary>
        /// Provides access to a chunk's array of component values for a specific buffer component type.
        /// </summary>
        /// <param name="broker">The ComponentBroker containing type and job safety information.</param>
        /// <typeparam name="T">The target component type, which must inherit <see cref="IBufferElementData"/>.</typeparam>
        /// <returns>An interface to this chunk's component values for type <typeparamref name="T"/></returns>
        public static BufferAccessor<T> GetBufferAccessor<T>(in this ArchetypeChunk chunk, ref ComponentBroker broker) where T : unmanaged, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>().Index;
            broker.CheckTypeIndexIsInComponentList(typeIndex);
            fixed (DynamicComponentTypeHandle* c0Ptr = &broker.c0)
            {
                return chunk.GetBufferAccessor<T>(ref c0Ptr[broker.handleIndices[typeIndex].index]);
            }
        }

        public static EntityTypeHandle AsEntityTypeHandle(this EntityStorageInfoLookup esil)
        {
            // EntityStorageInfoLookup is a safety handle for Entity plus the EntityDataAccess pointer.
            // EntityTypeHandle is just the safety handle for Entity.
            // Therefore, EntityStorageInfoLookup can downcast to an EntityTypeHandle.
            return UnsafeUtility.As<EntityStorageInfoLookup, EntityTypeHandle>(ref esil);
        }
    }
}

