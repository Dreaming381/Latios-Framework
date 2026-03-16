#if !LATIOS_TRANSFORMS_UNITY
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Transforms
{
    /// <summary>
    /// A struct which should be a field of a single-threaded job. It can provide TransformAspect instances for the context of such a job.
    /// </summary>
    public unsafe struct TransformAspectLookup
    {
        /* Construct Snippet
           new TransformAspectLookup(SystemAPI.GetComponentLookup<WorldTransform>(false),
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
                    return new TransformAspect { m_worldTransform = worldTransform, m_handle = handle, };
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
        /// Access to the internal EntityStorageInfoLookup for convenience
        /// </summary>
        public EntityStorageInfoLookup entityStorageInfoLookup => esil;
        /// <summary>
        /// Tries to look up a WorldTransform with read-only access
        /// </summary>
        public bool TryGetWorldTransformRO(Entity entity, out RefRO<WorldTransform> worldTransform) => transformLookup.TryGetRefRO(entity, out worldTransform);
    }

    /// <summary>
    /// A struct which should be a field of a parallel IJobChunk, IJobEntityChunkBeginEnd, or equivalent.
    /// It can provide TransformAspect for any root or solo entities with thread-safe guarantees.
    /// For each chunk, call SetupChunk(). Then use the indexer with the index of the entity within the chunk to get the TransformAspect.
    /// If used in an IJobEntity, make sure to include WorldTransform in your query!
    /// </summary>
    public unsafe struct TransformAspectRootHandle
    {
        /* Construct Snippet
           new TransformAspectRootHandle(SystemAPI.GetComponentLookup<WorldTransform>(false),
                                      SystemAPI.GetBufferTypeHandle<EntityInHierarchy>(true),
                                      SystemAPI.GetBufferTypeHandle<EntityInHierarchyCleanup>(true),
                                      SystemAPI.GetEntityStorageInfoLookup())
         */

        struct ThreadCache
        {
            public ComponentTypeHandle<WorldTransform>      transformHandle;
            public NativeArray<WorldTransform>              chunkTransforms;
            public BufferAccessor<EntityInHierarchy>        entityInHierarchyAccessor;
            public BufferAccessor<EntityInHierarchyCleanup> entityInHierarchyCleanupAccessor;
            public int                                      chunkIndex;
        }

        TransformsComponentLookup<WorldTransform>             transformLookup;
        [ReadOnly] BufferTypeHandle<EntityInHierarchy>        hierarchyHandle;
        [ReadOnly] BufferTypeHandle<EntityInHierarchyCleanup> cleanupHandle;
        [ReadOnly] EntityStorageInfoLookup                    esil;
        [NativeDisableUnsafePtrRestriction] ThreadCache*      cache;
        HasChecker<RootReference>                             rootRefChecker;

        public TransformAspectRootHandle(ComponentLookup<WorldTransform>            worldTransformLookupRW,
                                         BufferTypeHandle<EntityInHierarchy>        entityInHierarchyHandleRO,
                                         BufferTypeHandle<EntityInHierarchyCleanup> entityInHierarchyCleanupHandleRO,
                                         EntityStorageInfoLookup entityStorageInfoLookup)
        {
            transformLookup = worldTransformLookupRW;
            hierarchyHandle = entityInHierarchyHandleRO;
            cleanupHandle   = entityInHierarchyCleanupHandleRO;
            esil            = entityStorageInfoLookup;
            cache           = null;
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
            if (cache == null)
            {
                cache                  = AllocatorManager.Allocate<ThreadCache>(Allocator.Temp);
                cache->transformHandle = transformLookup.lookup.ToHandle(false);
            }
            cache->chunkIndex                       = chunk.GetHashCode();
            cache->chunkTransforms                  = chunk.GetNativeArray(ref cache->transformHandle);
            cache->entityInHierarchyAccessor        = chunk.GetBufferAccessorRO(ref hierarchyHandle);
            cache->entityInHierarchyCleanupAccessor = chunk.GetBufferAccessorRO(ref cleanupHandle);
        }

        /// <summary>
        /// Retrieves the TransformAspect for the corresponding entity index within the current chunk
        /// </summary>
        public TransformAspect this[int indexInChunk]
        {
            get
            {
                CheckInit();
                var transform = new RefRW<WorldTransform>(cache->chunkTransforms, indexInChunk);
                if (cache->entityInHierarchyAccessor.Length == 0)
                {
                    return new TransformAspect
                    {
                        m_worldTransform = transform,
                        m_handle         = default
                    };
                }
                else
                {
                    var extra  = cache->entityInHierarchyCleanupAccessor.Length > 0 ? cache->entityInHierarchyCleanupAccessor[indexInChunk].GetUnsafeReadOnlyPtr() : null;
                    var handle = new EntityInHierarchyHandle
                    {
                        m_hierarchy      = cache->entityInHierarchyAccessor[indexInChunk].AsNativeArray(),
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
        /// Access to the TransformsKey for the current chunk
        /// </summary>
        public TransformsKey transformsKey
        {
            get
            {
                CheckInit();
                return new TransformsKey
                {
                    chunkIndex  = cache->chunkIndex,
                    entityIndex = -1,
                    esil        = esil,
                };
            }
        }

        /// <summary>
        /// Access to the internal EntityStorageInfoLookup for convenience
        /// </summary>
        public EntityStorageInfoLookup entityStorageInfoLookup => esil;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckInit()
        {
            if (cache == null)
                throw new System.InvalidOperationException(
                    "The TransformAccessRootHandle has not been set up. Use IJobEntityChunkBeginEnd or IJobChunk to pass in the current chunk to SetupChunk().");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckIsRoot(in ArchetypeChunk chunk)
        {
            if (rootRefChecker[chunk])
                throw new System.InvalidOperationException("Cannot set up a TransformAccessRootHandle for a chunk containing non-root entities.");
        }
    }

    public static class TransformAspectAccessExtensions
    {
        /// <summary>
        /// Gets the TransformAspect of the handle powered by the system's EntityManager.
        /// </summary>
        public static unsafe TransformAspect GetTransfromAspect(this EntityManager em, EntityInHierarchyHandle handle)
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
        /// Gets the TransformAspect of the entity powered by the system's EntityManager.
        /// </summary>
        public static unsafe TransformAspect GetTransfromAspect(this EntityManager em, Entity entity)
        {
            var worldTransform = em.GetComponentDataRW<WorldTransform>(entity);
            var handle         = TransformTools.GetHierarchyHandle(entity, em);
            if (handle.isNull)
                return new TransformAspect { m_worldTransform = worldTransform, m_handle = handle, };
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
                return new TransformAspect { m_worldTransform = worldTransform, m_handle = handle, };
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
                return new TransformAspect { m_worldTransform = worldTransform, m_handle = handle, };
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
    }
}
#endif

