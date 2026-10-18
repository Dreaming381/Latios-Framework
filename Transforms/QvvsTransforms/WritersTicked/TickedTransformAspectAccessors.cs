#if !LATIOS_TRANSFORMS_UNITY
using System.Diagnostics;
using Latios.Transforms;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Transforms
{
    /// <summary>
    /// A struct which should be a field of a (single-threaded if not read-only) job.
    /// It can provide TickedTransformAspect and TickedTransformReadAspect instances for the context of such a job.
    /// </summary>
    public unsafe struct TickedTransformAspectLookup : ILatiosApiGettableBool
    {
        /* RW Construct Snippet
           new TickedTransformAspectLookup(SystemAPI.GetComponentLookup<TickedWorldTransform>(false),
                                  SystemAPI.GetComponentLookup<RootReference>(true),
                                  SystemAPI.GetBufferLookup<EntityInHierarchy>(true),
                                  SystemAPI.GetBufferLookup<EntityInHierarchyCleanup>(true),
                                  SystemAPI.GetEntityStorageInfoLookup())

           RO Construct Snippet (requires [ReadOnly] on job field)
           new TransformAspectLookup(SystemAPI.GetComponentLookup<TickedWorldTransform>(true),
                                  SystemAPI.GetComponentLookup<RootReference>(true),
                                  SystemAPI.GetBufferLookup<EntityInHierarchy>(true),
                                  SystemAPI.GetBufferLookup<EntityInHierarchyCleanup>(true),
                                  SystemAPI.GetEntityStorageInfoLookup())
         */
        ComponentLookup<TickedWorldTransform>             transformLookup;
        [ReadOnly] ComponentLookup<RootReference>         rootRefLookup;
        [ReadOnly] BufferLookup<EntityInHierarchy>        eihLookup;
        [ReadOnly] BufferLookup<EntityInHierarchyCleanup> cleanupLookup;
        [ReadOnly] EntityStorageInfoLookup                esil;

        public TickedTransformAspectLookup(ComponentLookup<TickedWorldTransform>  tickedWorldTransformLookupRW,
                                           ComponentLookup<RootReference>         rootReferenceLookupRO,
                                           BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                           BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupRO,
                                           EntityStorageInfoLookup entityStorageInfoLookup)
        {
            transformLookup = tickedWorldTransformLookupRW;
            rootRefLookup   = rootReferenceLookupRO;
            eihLookup       = entityInHierarchyLookupRO;
            cleanupLookup   = entityInHierarchyCleanupRO;
            esil            = entityStorageInfoLookup;
        }

        /// <summary>
        /// Retrieves a TickedTransformAspect corresponding to the EntityInHierarchyHandle
        /// </summary>
        public TickedTransformAspect this[EntityInHierarchyHandle handle] => new TickedTransformAspect
        {
            m_worldTransform = transformLookup.GetRefRW(handle.entity),
            m_handle         = handle,
            m_esil           = esil,
            m_accessType     = TickedTransformAspect.AccessType.ComponentLookup,
            m_access         = UnsafeUtility.AddressOf(ref transformLookup)
        };

        /// <summary>
        /// Retrieves a TickedTransformAspect from the entity
        /// </summary>
        public TickedTransformAspect this[Entity entity]
        {
            get
            {
                var tickedWorldTransform = transformLookup.GetRefRW(entity);
                var handle               = TransformTools.GetHierarchyHandle(entity, ref rootRefLookup, ref eihLookup, ref cleanupLookup);
                if (handle.isNull)
                {
                    var esi       = esil[entity];
                    var entityPtr = esi.Chunk.GetEntityDataPtrRO(esil.AsEntityTypeHandle()) + esi.IndexInChunk;
                    return new TickedTransformAspect
                    {
                        m_worldTransform = tickedWorldTransform,
                        m_handle         = handle,
                        m_esil           = esil,
                        m_access         = entityPtr
                    };
                }
                else
                {
                    return new TickedTransformAspect
                    {
                        m_worldTransform = tickedWorldTransform,
                        m_handle         = handle,
                        m_esil           = esil,
                        m_accessType     = TickedTransformAspect.AccessType.ComponentLookup,
                        m_access         = UnsafeUtility.AddressOf(ref transformLookup),
                    };
                }
            }
        }

        /// <summary>
        /// Retrieves a TransformReadAspect from the entity
        /// </summary>
        public TickedTransformReadAspect ReadOnly(Entity entity)
        {
            var worldTransform = transformLookup.GetRefRO(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, ref rootRefLookup, ref eihLookup, ref cleanupLookup);
            if (handle.isNull)
                return new TickedTransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            else
            {
                return new TickedTransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup),
                };
            }
        }

        /// <summary>
        /// Attempts to retrieve the TickedTransformAspect corresponding to the EntityInHierarchyHandle. Returns false
        /// if the entity does not have a TickedWorldTransform (it might only have a WorldTransform).
        /// </summary>
        public bool TryGetAspect(in EntityInHierarchyHandle handle, out TickedTransformAspect tickedTransformAspect)
        {
            if (transformLookup.TryGetRefRW(handle.entity, out var worldTransform))
            {
                tickedTransformAspect = new TickedTransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                };
                return true;
            }
            tickedTransformAspect = default;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve the TickedTransformAspect from the entity. Returns false if the entity does not have a WorldTransform.
        /// </summary>
        public bool TryGetAspect(Entity entity, out TickedTransformAspect tickedTransformAspect)
        {
            if (!transformLookup.TryGetRefRW(entity, out var worldTransform))
            {
                tickedTransformAspect = default;
                return false;
            }
            var handle = TransformTools.GetHierarchyHandle(entity, ref rootRefLookup, ref eihLookup, ref cleanupLookup);
            if (handle.isNull)
            {
                var esi               = esil[entity];
                var entityPtr         = esi.Chunk.GetEntityDataPtrRO(esil.AsEntityTypeHandle()) + esi.IndexInChunk;
                tickedTransformAspect = new TickedTransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_access         = entityPtr
                };
            }
            else
            {
                tickedTransformAspect = new TickedTransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup),
                };
            }
            return true;
        }

        /// <summary>
        /// Attempts to retrieve the TickedTransformReadAspect corresponding to the EntityInHierarchyHandle. Returns false
        /// if the entity does not have a TickedWorldTransform (it might only have a WorldTransform).
        /// </summary>
        public bool TryGetReadAspect(in EntityInHierarchyHandle handle, out TickedTransformReadAspect tickedTransformAspect)
        {
            if (transformLookup.TryGetRefRO(handle.entity, out var worldTransform))
            {
                tickedTransformAspect = new TickedTransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                };
                return true;
            }
            tickedTransformAspect = default;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve the TickedTransformReadAspect from the entity. Returns false if the entity does not have a WorldTransform.
        /// </summary>
        public bool TryGetReadAspect(Entity entity, out TickedTransformReadAspect tickedTransformAspect)
        {
            if (!transformLookup.TryGetRefRO(entity, out var worldTransform))
            {
                tickedTransformAspect = default;
                return false;
            }
            var handle = TransformTools.GetHierarchyHandle(entity, ref rootRefLookup, ref eihLookup, ref cleanupLookup);
            if (handle.isNull)
                tickedTransformAspect = new TickedTransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            else
            {
                tickedTransformAspect = new TickedTransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup),
                };
            }
            return true;
        }

        /// <summary>
        /// Access to the internal EntityStorageInfoLookup for convenience
        /// </summary>
        public EntityStorageInfoLookup entityStorageInfoLookup => esil;

        void ILatiosApiGettableBool.CreateForApi(ref SystemState state, bool b)
        {
            this = new TickedTransformAspectLookup(state.GetComponentLookup<TickedWorldTransform>(b),
                                                   state.GetComponentLookup<RootReference>(true),
                                                   state.GetBufferLookup<EntityInHierarchy>(true),
                                                   state.GetBufferLookup<EntityInHierarchyCleanup>(true),
                                                   state.GetEntityStorageInfoLookup());
        }

        void ILatiosApiGettableBool.UpdateForApi(ref SystemState state)
        {
            transformLookup.Update(ref state);
            rootRefLookup.Update(ref state);
            eihLookup.Update(ref state);
            cleanupLookup.Update(ref state);
            esil.Update(ref state);
        }
    }

    /// <summary>
    /// A struct which should be a field of a parallel IJobChunk, IJobEntityChunkBeginEnd, or equivalent.
    /// It can provide TickedTransformAspect for any root or solo entities with thread-safe guarantees.
    /// For each chunk, call SetupChunk(). Then use the indexer with the index of the entity within the chunk to get the TickedTransformAspect.
    /// If used in an IJobEntity, make sure to include TickedWorldTransform in your query!
    /// </summary>
    public unsafe struct TickedTransformAspectRootHandle : IJobEach.IParameterHandle<TickedTransformAspect>, IJobEach.IParameterHandle<TickedTransformDeferableAspect>
    {
        /* Construct Snippet
           new TickedTransformAspectRootHandle(SystemAPI.GetComponentLookup<TickedWorldTransform>(false),
                                      SystemAPI.GetBufferTypeHandle<EntityInHierarchy>(true),
                                      SystemAPI.GetBufferTypeHandle<EntityInHierarchyCleanup>(true),
                                      SystemAPI.GetEntityStorageInfoLookup())
         */

        #region State
        struct Cache
        {
            public ComponentTypeHandle<TickedWorldTransform>    transformHandle;
            public NativeArray<TickedWorldTransform>            chunkTransforms;
            public BufferAccessor<EntityInHierarchy>            entityInHierarchyAccessor;
            public BufferAccessor<EntityInHierarchyCleanup>     entityInHierarchyCleanupAccessor;
            public NativeList<TickedTransformBatchWriteCommand> deferredCommands;
            public Entity*                                      chunkEntities;
            public int                                          chunkIndex;
        }

        TransformsComponentLookup<TickedWorldTransform>       transformLookup;
        [ReadOnly] BufferTypeHandle<EntityInHierarchy>        hierarchyHandle;
        [ReadOnly] BufferTypeHandle<EntityInHierarchyCleanup> cleanupHandle;
        [ReadOnly] EntityStorageInfoLookup                    esil;
        ThreadCache<Cache>                                    threadCache;
        HasChecker<RootReference>                             rootRefChecker;
        #endregion

        #region API
        public TickedTransformAspectRootHandle(ComponentLookup<TickedWorldTransform>      tickedWorldTransformLookupRW,
                                               BufferTypeHandle<EntityInHierarchy>        entityInHierarchyHandleRO,
                                               BufferTypeHandle<EntityInHierarchyCleanup> entityInHierarchyCleanupHandleRO,
                                               EntityStorageInfoLookup entityStorageInfoLookup)
        {
            transformLookup = tickedWorldTransformLookupRW;
            hierarchyHandle = entityInHierarchyHandleRO;
            cleanupHandle   = entityInHierarchyCleanupHandleRO;
            esil            = entityStorageInfoLookup;
            threadCache     = default;
            rootRefChecker  = default;
        }

        /// <summary>
        /// Sets up a chunk for proper access. You must call this once for each chunk you iterate.
        /// If you jump between chunks, you must call this every time you switch. For IJobEntity,
        /// use the IJobEntityChunkBeginEnd interface to invoke this.
        /// </summary>
        /// <param name="chunk"></param>
        public void SetupChunk(in ArchetypeChunk chunk)
        {
            CheckIsRoot(in chunk);
            if (!threadCache.isCreated)
            {
                threadCache                       = new ThreadCache<Cache>(default);
                threadCache.cache.transformHandle = transformLookup.lookup.ToHandle(false);
            }
            ref var cache                          = ref threadCache.cache;
            cache.chunkIndex                       = chunk.GetHashCode();
            cache.chunkTransforms                  = chunk.GetNativeArray(ref cache.transformHandle);
            cache.entityInHierarchyAccessor        = chunk.GetBufferAccessorRO(ref hierarchyHandle);
            bool hasEntityInHierarchy              = cache.entityInHierarchyAccessor.Length > 0;
            cache.entityInHierarchyCleanupAccessor = hasEntityInHierarchy ? chunk.GetBufferAccessorRO(ref cleanupHandle) : default;
            cache.chunkEntities                    = hasEntityInHierarchy ? null : chunk.GetEntityDataPtrRO(esil.AsEntityTypeHandle());
        }

        /// <summary>
        /// Retrieves the TickedTransformAspect for the corresponding entity index within the current chunk
        /// </summary>
        public TickedTransformAspect this[int indexInChunk]
        {
            get
            {
                CheckInit();
                ref var cache     = ref threadCache.cache;
                var     transform = new RefRW<TickedWorldTransform>(cache.chunkTransforms, indexInChunk);
                if (cache.entityInHierarchyAccessor.Length == 0)
                {
                    return new TickedTransformAspect
                    {
                        m_worldTransform = transform,
                        m_handle         = default,
                        m_access         = cache.chunkEntities + indexInChunk
                    };
                }
                else
                {
                    var extra  = cache.entityInHierarchyCleanupAccessor.Length > 0 ? cache.entityInHierarchyCleanupAccessor[indexInChunk].GetUnsafeReadOnlyPtr() : null;
                    var handle = new EntityInHierarchyHandle
                    {
                        m_hierarchy      = cache.entityInHierarchyAccessor[indexInChunk].AsNativeArray(),
                        m_extraHierarchy = (EntityInHierarchy*)extra,
                        m_index          = 0
                    };
                    return new TickedTransformAspect
                    {
                        m_worldTransform = transform,
                        m_handle         = handle,
                        m_esil           = esil,
                        m_accessType     = TickedTransformAspect.AccessType.ComponentLookup,
                        m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                    };
                }
            }
        }

        /// <summary>
        /// Access to the TickedTransformDeferableAspect at the specified index of the currently active chunk.
        /// Use this when you want to batch writes to multiple transforms in a hierarchy from an IJobEntity or IJobChunk.
        /// </summary>
        public TickedTransformDeferableAspect Deferable(int indexInChunk)
        {
            ref var cache     = ref threadCache.cache;
            var     transform = this[indexInChunk];
            if (!cache.deferredCommands.IsCreated)
                cache.deferredCommands = new NativeList<TickedTransformBatchWriteCommand>(Allocator.Temp);
            return new TickedTransformDeferableAspect
            {
                transform = transform,
                commands  = cache.deferredCommands,
            };
        }

        /// <summary>
        /// Adds a deferred command for future playback. Deferred commands are played back in hierarchy order.
        /// Refer to ApplyDeferredTransforms() for more details.
        /// </summary>
        /// <param name="command"></param>
        public void AddDeferredCommand(in TickedTransformBatchWriteCommand command)
        {
            CheckInit();
            ref var cache = ref threadCache.cache;
            if (!cache.deferredCommands.IsCreated)
                cache.deferredCommands = new NativeList<TickedTransformBatchWriteCommand>(Allocator.Temp);
            cache.deferredCommands.Add(command);
        }

        /// <summary>
        /// Applies all pending deferred commands created by calls to AddDeferredCommand or from
        /// TickedTransformDeferableAspects within hierarchies.
        /// If using IJobParallelForDefer, you should call this yourself. If using IJobChunk or IJobEntity,
        /// this will be automatically called after each batch, though you can call it yourself at any time
        /// to get an up-to-date state of all transforms.
        /// </summary>
        public void ApplyDeferredTransforms()
        {
            if (!threadCache.isCreated || !threadCache.cache.deferredCommands.IsCreated)
                return; // We never started a chunk, so there can't be any commands.
            ref var cache = ref threadCache.cache;
            cache.deferredCommands.ApplyTransforms();
            cache.deferredCommands.Clear();
        }

        /// <summary>
        /// Access to the internal EntityStorageInfoLookup for convenience
        /// </summary>
        public EntityStorageInfoLookup entityStorageInfoLookup => esil;
        #endregion

        #region Source Gen API
        void ILatiosApiGettable.CreateForApi(ref SystemState state)
        {
            this = new TickedTransformAspectRootHandle(state.GetComponentLookup<TickedWorldTransform>(false),
                                                       state.GetBufferTypeHandle<EntityInHierarchy>(true),
                                                       state.GetBufferTypeHandle<EntityInHierarchyCleanup>(true),
                                                       state.GetEntityStorageInfoLookup());
        }

        void ILatiosApiGettable.UpdateForApi(ref SystemState state)
        {
            transformLookup.Update(ref state);
            hierarchyHandle.Update(ref state);
            cleanupHandle.Update(ref state);
            esil.Update(ref state);
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public FluentQuery AppendToQuery(FluentQuery query)
        {
            return query.With<TickedWorldTransform>(false).Without<RootReference>();
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public bool OnChunkBegin(in IJobEach.JobContext context)
        {
            SetupChunk(context.chunk);
            return true;
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public void OnChunkEnd(in IJobEach.JobContext context, bool chunkWasExecuted)
        {
            ApplyDeferredTransforms();
        }

        TickedTransformAspect IJobEach.IParameterHandle<TickedTransformAspect>.GetParameter(in IJobEach.JobContext context)
        {
            return this[context.indexInChunk];
        }

        TickedTransformDeferableAspect IJobEach.IParameterHandle<TickedTransformDeferableAspect>.GetParameter(in IJobEach.JobContext context)
        {
            ApplyDeferredTransforms();
            return Deferable(context.indexInChunk);
        }
        #endregion

        #region Safety
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckInit()
        {
            if (!threadCache.isCreated)
                throw new System.InvalidOperationException(
                    "The TransformAccessRootHandle has not been set up. Use IJobEntityChunkBeginEnd or IJobChunk to pass in the current chunk to SetupChunk().");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckIsRoot(in ArchetypeChunk chunk)
        {
            if (rootRefChecker[chunk])
                throw new System.InvalidOperationException("Cannot set up a TransformAccessRootHandle for a chunk containing non-root entities.");
        }
        #endregion
    }

    public static class TickedTransformAspectAccessExtensions
    {
        /// <summary>
        /// Gets the TickedTransformAspect of the handle powered by EntityManager.
        /// </summary>
        public static unsafe TickedTransformAspect GetTickedTransformAspect(this EntityManager em, EntityInHierarchyHandle handle)
        {
            var tickedWorldTransform = em.GetComponentDataRW<TickedWorldTransform>(handle.entity);
            return new TickedTransformAspect
            {
                m_worldTransform = tickedWorldTransform,
                m_handle         = handle,
                m_esil           = em.GetEntityStorageInfoLookup(),
                m_accessType     = TickedTransformAspect.AccessType.EntityManager,
                m_access         = em.GetEntityManagerPtr()
            };
        }

        /// <summary>
        /// Gets the TickedTransformAspect of the entity powered by EntityManager.
        /// </summary>
        public static unsafe TickedTransformAspect GetTickedTransformAspect(this EntityManager em, Entity entity)
        {
            var tickedWorldTransform = em.GetComponentDataRW<TickedWorldTransform>(entity);
            var handle               = TransformTools.GetHierarchyHandle(entity, em);
            if (handle.isNull)
            {
                var esi       = em.GetStorageInfo(entity);
                var entityPtr = esi.Chunk.GetEntityDataPtrRO(em.GetEntityTypeHandle()) + esi.IndexInChunk;
                return new TickedTransformAspect
                {
                    m_worldTransform = tickedWorldTransform,
                    m_handle         = handle,
                    m_access         = entityPtr
                };
            }
            else
            {
                return new TickedTransformAspect
                {
                    m_worldTransform = tickedWorldTransform,
                    m_handle         = handle,
                    m_esil           = em.GetEntityStorageInfoLookup(),
                    m_accessType     = TickedTransformAspect.AccessType.EntityManager,
                    m_access         = em.GetEntityManagerPtr()
                };
            }
        }

        /// <summary>
        /// Gets the TickedTransformReadAspect of the handle powered by EntityManager.
        /// </summary>
        public static unsafe TickedTransformReadAspect GetTickedTransformReadAspect(this EntityManager em, EntityInHierarchyHandle handle)
        {
            var worldTransform = em.GetComponentDataRO<TickedWorldTransform>(handle.entity);
            return new TickedTransformReadAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = em.GetEntityStorageInfoLookup(),
                m_accessType     = TickedTransformAspect.AccessType.EntityManager,
                m_access         = em.GetEntityManagerPtr()
            };
        }

        /// <summary>
        /// Gets the TickedTransformReadAspect of the entity powered by EntityManager.
        /// </summary>
        public static unsafe TickedTransformReadAspect GetTickedTransformReadAspect(this EntityManager em, Entity entity)
        {
            var worldTransform = em.GetComponentDataRO<TickedWorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, em);
            if (handle.isNull)
                return new TickedTransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            else
            {
                return new TickedTransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = em.GetEntityStorageInfoLookup(),
                    m_accessType     = TickedTransformAspect.AccessType.EntityManager,
                    m_access         = em.GetEntityManagerPtr()
                };
            }
        }

        [System.Obsolete("Misspelled. Use GetTickedTransformAspect() instead.")]
        public static TickedTransformAspect GetTickedTransfromAspect(this EntityManager em, EntityInHierarchyHandle handle) => em.GetTickedTransformAspect(handle);

        [System.Obsolete("Misspelled. Use GetTickedTransformAspect() instead.")]
        public static TickedTransformAspect GetTickedTransfromAspect(this EntityManager em, Entity entity) => em.GetTickedTransformAspect(entity);

        [System.Obsolete("Misspelled. Use GetTickedTransformReadAspect() instead.")]
        public static TickedTransformReadAspect GetTickedTransfromReadAspect(this EntityManager em, EntityInHierarchyHandle handle) => em.GetTickedTransformReadAspect(handle);

        [System.Obsolete("Misspelled. Use GetTickedTransformReadAspect() instead.")]
        public static TickedTransformReadAspect GetTickedTransfromReadAspect(this EntityManager em, Entity entity) => em.GetTickedTransformReadAspect(entity);

        /// <summary>
        /// Gets the TickedTransformAspect of the handle powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TickedTransformAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to TickedWorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup.
        /// </summary>
        public static unsafe TickedTransformAspect GetTickedTransformAspect(this ref ComponentBroker broker, EntityInHierarchyHandle handle)
        {
            var tickedWorldTransform = broker.GetRW<TickedWorldTransform>(handle.entity);
            return new TickedTransformAspect
            {
                m_worldTransform = tickedWorldTransform,
                m_handle         = handle,
                m_esil           = broker.entityStorageInfoLookup,
                m_accessType     = TickedTransformAspect.AccessType.ComponentBroker,
                m_access         = UnsafeUtility.AddressOf(ref broker)
            };
        }

        /// <summary>
        /// Gets the TickedTransformAspect of the entity powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TickedTransformAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to TickedWorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup.
        /// </summary>
        public static unsafe TickedTransformAspect GetTickedTransformAspect(this ref ComponentBroker broker, Entity entity)
        {
            var tickedWorldTransform = broker.GetRW<TickedWorldTransform>(entity);
            var handle               = TransformTools.GetHierarchyHandle(entity, ref broker);
            if (handle.isNull)
            {
                var esi       = broker.entityStorageInfoLookup[entity];
                var entityPtr = esi.Chunk.GetEntityDataPtrRO(broker.entityTypeHandle) + esi.IndexInChunk;
                return new TickedTransformAspect
                {
                    m_worldTransform = tickedWorldTransform,
                    m_handle         = handle,
                    m_access         = entityPtr
                };
            }
            else
            {
                return new TickedTransformAspect
                {
                    m_worldTransform = tickedWorldTransform,
                    m_handle         = handle,
                    m_esil           = broker.entityStorageInfoLookup,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentBroker,
                    m_access         = UnsafeUtility.AddressOf(ref broker)
                };
            }
        }

        /// <summary>
        /// Gets the TickedTransformAspect of the handle powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TickedTransformAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to TickedWorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup. The aspect
        /// is verified for parallel writing by the key.
        /// </summary>
        public static unsafe TickedTransformAspect GetTickedTransformAspect(this ref ComponentBroker broker, EntityInHierarchyHandle handle, TransformsKey key)
        {
            key.Validate(handle.root.entity);
            var tickedWorldTransform = broker.GetRWIgnoreParallelSafety<TickedWorldTransform>(handle.entity);
            return new TickedTransformAspect
            {
                m_worldTransform = tickedWorldTransform,
                m_handle         = handle,
                m_esil           = broker.entityStorageInfoLookup,
                m_accessType     = TickedTransformAspect.AccessType.ComponentBrokerKeyed,
                m_access         = UnsafeUtility.AddressOf(ref broker)
            };
        }

        /// <summary>
        /// Gets the TickedTransformAspect of the entity powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TickedTransformAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to TickedWorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup. The aspect
        /// is verified for parallel writing by the key.
        /// </summary>
        public static unsafe TickedTransformAspect GetTickedTransformAspect(this ref ComponentBroker broker, Entity entity, TransformsKey key)
        {
            var tickedWorldTransform = broker.GetRWIgnoreParallelSafety<TickedWorldTransform>(entity);
            var handle               = TransformTools.GetHierarchyHandle(entity, ref broker);
            if (handle.isNull)
            {
                key.Validate(entity);
                var esi       = broker.entityStorageInfoLookup[entity];
                var entityPtr = esi.Chunk.GetEntityDataPtrRO(broker.entityTypeHandle) + esi.IndexInChunk;
                return new TickedTransformAspect
                {
                    m_worldTransform = tickedWorldTransform,
                    m_handle         = handle,
                    m_access         = entityPtr
                };
            }
            else
            {
                key.Validate(handle.root.entity);
                return new TickedTransformAspect
                {
                    m_worldTransform = tickedWorldTransform,
                    m_handle         = handle,
                    m_esil           = broker.entityStorageInfoLookup,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentBrokerKeyed,
                    m_access         = UnsafeUtility.AddressOf(ref broker)
                };
            }
        }

        /// <summary>
        /// Gets the TickedTransformReadAspect of the handle powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TickedTransformReadAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup.
        /// </summary>
        public static unsafe TickedTransformReadAspect GetTickedTransformReadAspect(this ref ComponentBroker broker, EntityInHierarchyHandle handle)
        {
            var worldTransform = broker.GetRO<TickedWorldTransform>(handle.entity);
            return new TickedTransformReadAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = broker.entityStorageInfoLookup,
                m_accessType     = TickedTransformAspect.AccessType.ComponentBroker,
                m_access         = UnsafeUtility.AddressOf(ref broker)
            };
        }

        /// <summary>
        /// Gets the TickedTransformReadAspect of the entity powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TickedTransformReadAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup.
        /// </summary>
        public static unsafe TickedTransformReadAspect GetTickedTransformReadAspect(this ref ComponentBroker broker, Entity entity)
        {
            var worldTransform = broker.GetRO<TickedWorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, ref broker);
            if (handle.isNull)
                return new TickedTransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            else
            {
                return new TickedTransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = broker.entityStorageInfoLookup,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentBroker,
                    m_access         = UnsafeUtility.AddressOf(ref broker)
                };
            }
        }

        /// <summary>
        /// Gets the TickedTransformReadAspect of the handle powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TickedTransformReadAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup. The aspect
        /// is verified in parallel writing contexts by the key.
        /// </summary>
        public static unsafe TickedTransformReadAspect GetTickedTransformReadAspect(this ref ComponentBroker broker, EntityInHierarchyHandle handle, TransformsKey key)
        {
            key.Validate(handle.root.entity);
            var worldTransform = broker.GetROIgnoreParallelSafety<TickedWorldTransform>(handle.entity);
            return new TickedTransformReadAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = broker.entityStorageInfoLookup,
                m_accessType     = TickedTransformAspect.AccessType.ComponentBrokerKeyed,
                m_access         = UnsafeUtility.AddressOf(ref broker)
            };
        }

        /// <summary>
        /// Gets the TickedTransformReadAspect of the entity powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TickedTransformReadAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup. The aspect
        /// is verified in parallel writing contexts by the key.
        /// </summary>
        public static unsafe TickedTransformReadAspect GetTickedTransformReadAspect(this ref ComponentBroker broker, Entity entity, TransformsKey key)
        {
            var worldTransform = broker.GetROIgnoreParallelSafety<TickedWorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, ref broker);
            if (handle.isNull)
            {
                key.Validate(entity);
                return new TickedTransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            }
            else
            {
                key.Validate(handle.root.entity);
                return new TickedTransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = broker.entityStorageInfoLookup,
                    m_accessType     = TickedTransformAspect.AccessType.ComponentBrokerKeyed,
                    m_access         = UnsafeUtility.AddressOf(ref broker)
                };
            }
        }
    }
}
#endif

