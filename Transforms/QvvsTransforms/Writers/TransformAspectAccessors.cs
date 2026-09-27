#if !LATIOS_TRANSFORMS_UNITY
using System.Diagnostics;
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
    /// It can provide TransformAspect and TransformReadAspect instances for the context of such a job.
    /// </summary>
    public unsafe struct TransformAspectLookup : ILatiosApiGettableBool
    {
        /* RW Construct Snippet
           new TransformAspectLookup(SystemAPI.GetComponentLookup<WorldTransform>(false),
                                  SystemAPI.GetComponentLookup<RootReference>(true),
                                  SystemAPI.GetBufferLookup<EntityInHierarchy>(true),
                                  SystemAPI.GetBufferLookup<EntityInHierarchyCleanup>(true),
                                  SystemAPI.GetEntityStorageInfoLookup())

           RO Construct Snippet (requires [ReadOnly] on job field)
           new TransformAspectLookup(SystemAPI.GetComponentLookup<WorldTransform>(true),
                                  SystemAPI.GetComponentLookup<RootReference>(true),
                                  SystemAPI.GetBufferLookup<EntityInHierarchy>(true),
                                  SystemAPI.GetBufferLookup<EntityInHierarchyCleanup>(true),
                                  SystemAPI.GetEntityStorageInfoLookup())
         */
        ComponentLookup<WorldTransform>                   transformLookup;
        [ReadOnly] ComponentLookup<RootReference>         rootRefLookup;
        [ReadOnly] BufferLookup<EntityInHierarchy>        eihLookup;
        [ReadOnly] BufferLookup<EntityInHierarchyCleanup> cleanupLookup;
        [ReadOnly] EntityStorageInfoLookup                esil;

        public TransformAspectLookup(ComponentLookup<WorldTransform>        worldTransformLookupRW,
                                     ComponentLookup<RootReference>         rootReferenceLookupRO,
                                     BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                     BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupRO,
                                     EntityStorageInfoLookup entityStorageInfoLookup)
        {
            transformLookup = worldTransformLookupRW;
            rootRefLookup   = rootReferenceLookupRO;
            eihLookup       = entityInHierarchyLookupRO;
            cleanupLookup   = entityInHierarchyCleanupRO;
            esil            = entityStorageInfoLookup;
        }

        /// <summary>
        /// Retrieves a TransformAspect corresponding to the EntityInHierarchyHandle
        /// </summary>
        public TransformAspect this[EntityInHierarchyHandle handle] => new TransformAspect
        {
            m_worldTransform = transformLookup.GetRefRW(handle.entity),
            m_handle         = handle,
            m_esil           = esil,
            m_accessType     = TransformAspect.AccessType.ComponentLookup,
            m_access         = UnsafeUtility.AddressOf(ref transformLookup)
        };

        /// <summary>
        /// Retrieves a TransformAspect from the entity
        /// </summary>
        public TransformAspect this[Entity entity]
        {
            get
            {
                var worldTransform = transformLookup.GetRefRW(entity);
                var handle         = TransformTools.GetHierarchyHandle(entity, ref rootRefLookup, ref eihLookup, ref cleanupLookup);
                if (handle.isNull)
                {
                    var esi       = esil[entity];
                    var entityPtr = esi.Chunk.GetEntityDataPtrRO(esil.AsEntityTypeHandle()) + esi.IndexInChunk;
                    return new TransformAspect
                    {
                        m_worldTransform = worldTransform,
                        m_handle         = handle,
                        m_access         = entityPtr
                    };
                }
                else
                {
                    return new TransformAspect
                    {
                        m_worldTransform = worldTransform,
                        m_handle         = handle,
                        m_esil           = esil,
                        m_accessType     = TransformAspect.AccessType.ComponentLookup,
                        m_access         = UnsafeUtility.AddressOf(ref transformLookup),
                    };
                }
            }
        }

        /// <summary>
        /// Retrieves a TransformReadAspect from the entity
        /// </summary>
        public TransformReadAspect ReadOnly(Entity entity)
        {
            var worldTransform = transformLookup.GetRefRO(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, ref rootRefLookup, ref eihLookup, ref cleanupLookup);
            if (handle.isNull)
                return new TransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            else
            {
                return new TransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup),
                };
            }
        }

        /// <summary>
        /// Attempts to retrieve the TransformAspect corresponding to the EntityInHierarchyHandle. Returns false
        /// if the entity does not have a WorldTransform (it might only have a TickedWorldTransform).
        /// </summary>
        public bool TryGetAspect(in EntityInHierarchyHandle handle, out TransformAspect transformAspect)
        {
            if (transformLookup.TryGetRefRW(handle.entity, out var worldTransform))
            {
                transformAspect = new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                };
                return true;
            }
            transformAspect = default;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve the TransformAspect from the entity. Returns false if the entity does not have a WorldTransform.
        /// </summary>
        public bool TryGetAspect(Entity entity, out TransformAspect transformAspect)
        {
            if (!transformLookup.TryGetRefRW(entity, out var worldTransform))
            {
                transformAspect = default;
                return false;
            }
            var handle = TransformTools.GetHierarchyHandle(entity, ref rootRefLookup, ref eihLookup, ref cleanupLookup);
            if (handle.isNull)
            {
                var esi         = esil[entity];
                var entityPtr   = esi.Chunk.GetEntityDataPtrRO(esil.AsEntityTypeHandle()) + esi.IndexInChunk;
                transformAspect = new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_access         = entityPtr
                };
            }
            else
            {
                transformAspect = new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup),
                };
            }
            return true;
        }

        /// <summary>
        /// Attempts to retrieve the TransformReadAspect corresponding to the EntityInHierarchyHandle. Returns false
        /// if the entity does not have a WorldTransform (it might only have a TickedWorldTransform).
        /// </summary>
        public bool TryGetReadAspect(in EntityInHierarchyHandle handle, out TransformReadAspect transformAspect)
        {
            if (transformLookup.TryGetRefRO(handle.entity, out var worldTransform))
            {
                transformAspect = new TransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                };
                return true;
            }
            transformAspect = default;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve the TransformReadAspect from the entity. Returns false if the entity does not have a WorldTransform.
        /// </summary>
        public bool TryGetReadAspect(Entity entity, out TransformReadAspect transformAspect)
        {
            if (!transformLookup.TryGetRefRO(entity, out var worldTransform))
            {
                transformAspect = default;
                return false;
            }
            var handle = TransformTools.GetHierarchyHandle(entity, ref rootRefLookup, ref eihLookup, ref cleanupLookup);
            if (handle.isNull)
                transformAspect = new TransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            else
            {
                transformAspect = new TransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TransformAspect.AccessType.ComponentLookup,
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
            this = new TransformAspectLookup(state.GetComponentLookup<WorldTransform>(b),
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
    /// It can provide TransformAspect and TransformReadAspect for any root or solo entities with thread-safe guarantees.
    /// For each chunk, call SetupChunk(). Then use the indexer with the index of the entity within the chunk to get the TransformAspect.
    /// If used in an IJobEntity, make sure to include WorldTransform in your query!
    /// </summary>
    public unsafe struct TransformAspectRootHandle : IJobEach.IParameterHandle<TransformAspect>, IJobEach.IParameterHandle<TransformDeferableAspect>
    {
        /* Construct Snippet
           new TransformAspectRootHandle(SystemAPI.GetComponentLookup<WorldTransform>(false),
                                      SystemAPI.GetBufferTypeHandle<EntityInHierarchy>(true),
                                      SystemAPI.GetBufferTypeHandle<EntityInHierarchyCleanup>(true),
                                      SystemAPI.GetEntityStorageInfoLookup())
         */

        #region State
        struct Cache
        {
            public ComponentTypeHandle<WorldTransform>      transformHandle;
            public NativeArray<WorldTransform>              chunkTransforms;
            public BufferAccessor<EntityInHierarchy>        entityInHierarchyAccessor;
            public BufferAccessor<EntityInHierarchyCleanup> entityInHierarchyCleanupAccessor;
            public NativeList<TransformBatchWriteCommand>   deferredCommands;
            public Entity*                                  chunkEntities;
            public int                                      chunkIndex;
        }

        TransformsComponentLookup<WorldTransform>             transformLookup;
        [ReadOnly] BufferTypeHandle<EntityInHierarchy>        hierarchyHandle;
        [ReadOnly] BufferTypeHandle<EntityInHierarchyCleanup> cleanupHandle;
        [ReadOnly] EntityStorageInfoLookup                    esil;
        ThreadCache<Cache>                                    threadCache;
        HasChecker<RootReference>                             rootRefChecker;
        #endregion

        #region API
        public TransformAspectRootHandle(ComponentLookup<WorldTransform>            worldTransformLookupRW,
                                         BufferTypeHandle<EntityInHierarchy>        entityInHierarchyHandleRO,
                                         BufferTypeHandle<EntityInHierarchyCleanup> entityInHierarchyCleanupHandleRO,
                                         EntityStorageInfoLookup entityStorageInfoLookup)
        {
            transformLookup = worldTransformLookupRW;
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
        /// Retrieves the TransformAspect for the corresponding entity index within the current chunk
        /// </summary>
        public TransformAspect this[int indexInChunk]
        {
            get
            {
                CheckInit();
                ref var cache     = ref threadCache.cache;
                var     transform = new RefRW<WorldTransform>(cache.chunkTransforms, indexInChunk);
                if (cache.entityInHierarchyAccessor.Length == 0)
                {
                    return new TransformAspect
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
                    return new TransformAspect
                    {
                        m_worldTransform = transform,
                        m_handle         = handle,
                        m_esil           = esil,
                        m_accessType     = TransformAspect.AccessType.ComponentLookup,
                        m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                    };
                }
            }
        }

        /// <summary>
        /// Access to the TransformDeferableAspect at the specified index of the currently active chunk.
        /// Use this when you want to batch writes to multiple transforms in a hierarchy from an IJobEntity or IJobChunk.
        /// </summary>
        public TransformDeferableAspect Deferable(int indexInChunk)
        {
            ref var cache     = ref threadCache.cache;
            var     transform = this[indexInChunk];
            if (!cache.deferredCommands.IsCreated)
                cache.deferredCommands = new NativeList<TransformBatchWriteCommand>(Allocator.Temp);
            return new TransformDeferableAspect
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
        public void AddDeferredCommand(in TransformBatchWriteCommand command)
        {
            CheckInit();
            ref var cache = ref threadCache.cache;
            if (!cache.deferredCommands.IsCreated)
                cache.deferredCommands = new NativeList<TransformBatchWriteCommand>(Allocator.Temp);
            cache.deferredCommands.Add(command);
        }

        /// <summary>
        /// Applies all pending deferred commands created by calls to AddDeferredCommand or from
        /// TransformDeferableAspects within hierarchies.
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
            this = new TransformAspectRootHandle(state.GetComponentLookup<WorldTransform>(false),
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
            return query.With<WorldTransform>(false).Without<RootReference>();
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

        TransformAspect IJobEach.IParameterHandle<TransformAspect>.GetParameter(in IJobEach.JobContext context)
        {
            return this[context.indexInChunk];
        }

        TransformDeferableAspect IJobEach.IParameterHandle<TransformDeferableAspect>.GetParameter(in IJobEach.JobContext context)
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

    public static class TransformAspectAccessExtensions
    {
        /// <summary>
        /// Gets the TransformAspect of the handle powered by EntityManager.
        /// </summary>
        public static unsafe TransformAspect GetTransformAspect(this EntityManager em, EntityInHierarchyHandle handle)
        {
            var worldTransform = em.GetComponentDataRW<WorldTransform>(handle.entity);
            return new TransformAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = em.GetEntityStorageInfoLookup(),
                m_accessType     = TransformAspect.AccessType.EntityManager,
                m_access         = em.GetEntityManagerPtr()
            };
        }

        /// <summary>
        /// Gets the TransformAspect of the entity powered by EntityManager.
        /// </summary>
        public static unsafe TransformAspect GetTransformAspect(this EntityManager em, Entity entity)
        {
            var worldTransform = em.GetComponentDataRW<WorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, em);
            if (handle.isNull)
            {
                var esi       = em.GetStorageInfo(entity);
                var entityPtr = esi.Chunk.GetEntityDataPtrRO(em.GetEntityTypeHandle()) + esi.IndexInChunk;
                return new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_access         = entityPtr
                };
            }
            else
            {
                return new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = em.GetEntityStorageInfoLookup(),
                    m_accessType     = TransformAspect.AccessType.EntityManager,
                    m_access         = em.GetEntityManagerPtr()
                };
            }
        }

        /// <summary>
        /// Gets the TransformReadAspect of the handle powered by EntityManager.
        /// </summary>
        public static unsafe TransformReadAspect GetTransformReadAspect(this EntityManager em, EntityInHierarchyHandle handle)
        {
            var worldTransform = em.GetComponentDataRO<WorldTransform>(handle.entity);
            return new TransformReadAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = em.GetEntityStorageInfoLookup(),
                m_accessType     = TransformAspect.AccessType.EntityManager,
                m_access         = em.GetEntityManagerPtr()
            };
        }

        /// <summary>
        /// Gets the TransformReadAspect of the entity powered by EntityManager.
        /// </summary>
        public static unsafe TransformReadAspect GetTransformReadAspect(this EntityManager em, Entity entity)
        {
            var worldTransform = em.GetComponentDataRO<WorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, em);
            if (handle.isNull)
                return new TransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            else
            {
                return new TransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = em.GetEntityStorageInfoLookup(),
                    m_accessType     = TransformAspect.AccessType.EntityManager,
                    m_access         = em.GetEntityManagerPtr()
                };
            }
        }

        [System.Obsolete("Misspelled. Use GetTransformAspect() instead.")]
        public static TransformAspect GetTransfromAspect(this EntityManager em, EntityInHierarchyHandle handle) => em.GetTransformAspect(handle);

        [System.Obsolete("Misspelled. Use GetTransformAspect() instead.")]
        public static TransformAspect GetTransfromAspect(this EntityManager em, Entity entity) => em.GetTransformAspect(entity);

        [System.Obsolete("Misspelled. Use GetTransformReadAspect() instead.")]
        public static TransformReadAspect GetTransfromReadAspect(this EntityManager em, EntityInHierarchyHandle handle) => em.GetTransformReadAspect(handle);

        [System.Obsolete("Misspelled. Use GetTransformReadAspect() instead.")]
        public static TransformReadAspect GetTransfromReadAspect(this EntityManager em, Entity entity) => em.GetTransformReadAspect(entity);

        /// <summary>
        /// Gets the TransformAspect of the handle powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TransformAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup.
        /// </summary>
        public static unsafe TransformAspect GetTransformAspect(this ref ComponentBroker broker, EntityInHierarchyHandle handle)
        {
            var worldTransform = broker.GetRW<WorldTransform>(handle.entity);
            return new TransformAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = broker.entityStorageInfoLookup,
                m_accessType     = TransformAspect.AccessType.ComponentBroker,
                m_access         = UnsafeUtility.AddressOf(ref broker)
            };
        }

        /// <summary>
        /// Gets the TransformAspect of the entity powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TransformAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup.
        /// </summary>
        public static unsafe TransformAspect GetTransformAspect(this ref ComponentBroker broker, Entity entity)
        {
            var worldTransform = broker.GetRW<WorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, ref broker);
            if (handle.isNull)
            {
                var esi       = broker.entityStorageInfoLookup[entity];
                var entityPtr = esi.Chunk.GetEntityDataPtrRO(broker.entityTypeHandle) + esi.IndexInChunk;
                return new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_access         = entityPtr
                };
            }
            else
            {
                return new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = broker.entityStorageInfoLookup,
                    m_accessType     = TransformAspect.AccessType.ComponentBroker,
                    m_access         = UnsafeUtility.AddressOf(ref broker)
                };
            }
        }

        /// <summary>
        /// Gets the TransformAspect of the handle powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TransformAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup. The aspect
        /// is verified for parallel writing by the key.
        /// </summary>
        public static unsafe TransformAspect GetTransformAspect(this ref ComponentBroker broker, EntityInHierarchyHandle handle, TransformsKey key)
        {
            key.Validate(handle.root.entity);
            var worldTransform = broker.GetRWIgnoreParallelSafety<WorldTransform>(handle.entity);
            return new TransformAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = broker.entityStorageInfoLookup,
                m_accessType     = TransformAspect.AccessType.ComponentBrokerKeyed,
                m_access         = UnsafeUtility.AddressOf(ref broker)
            };
        }

        /// <summary>
        /// Gets the TransformAspect of the entity powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TransformAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup. The aspect
        /// is verified for parallel writing by the key.
        /// </summary>
        public static unsafe TransformAspect GetTransformAspect(this ref ComponentBroker broker, Entity entity, TransformsKey key)
        {
            var worldTransform = broker.GetRWIgnoreParallelSafety<WorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, ref broker);
            if (handle.isNull)
            {
                key.Validate(entity);
                var esi       = broker.entityStorageInfoLookup[entity];
                var entityPtr = esi.Chunk.GetEntityDataPtrRO(broker.entityTypeHandle) + esi.IndexInChunk;
                return new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_access         = entityPtr
                };
            }
            else
            {
                key.Validate(handle.root.entity);
                return new TransformAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = broker.entityStorageInfoLookup,
                    m_accessType     = TransformAspect.AccessType.ComponentBrokerKeyed,
                    m_access         = UnsafeUtility.AddressOf(ref broker)
                };
            }
        }

        /// <summary>
        /// Gets the TransformReadAspect of the handle powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TransformReadAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup.
        /// </summary>
        public static unsafe TransformReadAspect GetTransformReadAspect(this ref ComponentBroker broker, EntityInHierarchyHandle handle)
        {
            var worldTransform = broker.GetRO<WorldTransform>(handle.entity);
            return new TransformReadAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = broker.entityStorageInfoLookup,
                m_accessType     = TransformAspect.AccessType.ComponentBroker,
                m_access         = UnsafeUtility.AddressOf(ref broker)
            };
        }

        /// <summary>
        /// Gets the TransformReadAspect of the entity powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TransformReadAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup.
        /// </summary>
        public static unsafe TransformReadAspect GetTransformReadAspect(this ref ComponentBroker broker, Entity entity)
        {
            var worldTransform = broker.GetRO<WorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, ref broker);
            if (handle.isNull)
                return new TransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            else
            {
                return new TransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = broker.entityStorageInfoLookup,
                    m_accessType     = TransformAspect.AccessType.ComponentBroker,
                    m_access         = UnsafeUtility.AddressOf(ref broker)
                };
            }
        }

        /// <summary>
        /// Gets the TransformReadAspect of the handle powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TransformReadAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup. The aspect
        /// is verified in parallel writing contexts by the key.
        /// </summary>
        public static unsafe TransformReadAspect GetTransformReadAspect(this ref ComponentBroker broker, EntityInHierarchyHandle handle, TransformsKey key)
        {
            key.Validate(handle.root.entity);
            var worldTransform = broker.GetROIgnoreParallelSafety<WorldTransform>(handle.entity);
            return new TransformReadAspect
            {
                m_worldTransform = worldTransform,
                m_handle         = handle,
                m_esil           = broker.entityStorageInfoLookup,
                m_accessType     = TransformAspect.AccessType.ComponentBrokerKeyed,
                m_access         = UnsafeUtility.AddressOf(ref broker)
            };
        }

        /// <summary>
        /// Gets the TransformReadAspect of the entity powered by a ComponentBroker. The ComponentBroker
        /// must have a fixed address for the lifecycle of the TransformReadAspect, such as a field in a
        /// currently executing job. The ComponentBroker requires write access to WorldTransform, and
        /// read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup. The aspect
        /// is verified in parallel writing contexts by the key.
        /// </summary>
        public static unsafe TransformReadAspect GetTransformReadAspect(this ref ComponentBroker broker, Entity entity, TransformsKey key)
        {
            var worldTransform = broker.GetROIgnoreParallelSafety<WorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, ref broker);
            if (handle.isNull)
            {
                key.Validate(entity);
                return new TransformReadAspect { m_worldTransform = worldTransform, m_handle = handle, };
            }
            else
            {
                key.Validate(handle.root.entity);
                return new TransformReadAspect
                {
                    m_worldTransform = worldTransform,
                    m_handle         = handle,
                    m_esil           = broker.entityStorageInfoLookup,
                    m_accessType     = TransformAspect.AccessType.ComponentBrokerKeyed,
                    m_access         = UnsafeUtility.AddressOf(ref broker)
                };
            }
        }
    }
}
#endif

