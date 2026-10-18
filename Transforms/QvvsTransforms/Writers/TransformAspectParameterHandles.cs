#if !LATIOS_TRANSFORMS_UNITY
using System.Diagnostics;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

// WARNING: All types in this file are for internal use in IJobEach only.
using static Latios.IJobEach;

namespace Latios.Transforms
{
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public unsafe struct TransformAspectParameterHandle : IGroupedParameterHandle<TransformAspect>
    {
        struct Cache
        {
            public ComponentTypeHandle<WorldTransform>        transformHandle;
            public BufferTypeHandle<EntityInHierarchy>        entityInHierarchyHandle;
            public BufferTypeHandle<EntityInHierarchyCleanup> entityInHierarchyCleanupHandle;

            public NativeArray<WorldTransform>              chunkTransforms;
            public BufferAccessor<EntityInHierarchy>        chunkEntityInHierarchyAccessor;
            public BufferAccessor<EntityInHierarchyCleanup> chunkEntityInHierarchyCleanupAccessor;
            public RootReference*                           chunkRootReferences;
            public Entity*                                  chunkEntities;
            public bool                                     isRoot;
            public bool                                     isChild;
        }

        TransformsComponentLookup<WorldTransform>         transformLookup;
        [ReadOnly] BufferLookup<EntityInHierarchy>        hierarchyLookup;
        [ReadOnly] BufferLookup<EntityInHierarchyCleanup> cleanupLookup;
        [ReadOnly] ComponentTypeHandle<RootReference>     rootReferenceHandle;
        [ReadOnly] EntityStorageInfoLookup                esil;
        ThreadCache<Cache>                                threadCache;

        HasChecker<EntityInHierarchy>        hierarchyChecker;
        HasChecker<EntityInHierarchyCleanup> cleanupChecker;

        bool isGroupInitialized;
        bool isChunkInitialized;

        public FluentQuery AppendToQuery(FluentQuery query)
        {
            return query.With<WorldTransform>(false);
        }

        public void CreateForApi(ref SystemState state)
        {
            transformLookup     = state.GetComponentLookup<WorldTransform>(false);
            hierarchyLookup     = state.GetBufferLookup<EntityInHierarchy>(true);
            cleanupLookup       = state.GetBufferLookup<EntityInHierarchyCleanup>(true);
            rootReferenceHandle = state.GetComponentTypeHandle<RootReference>(true);
            esil                = state.GetEntityStorageInfoLookup();
        }

        public TransformAspect GetParameter(in JobContext context)
        {
            CheckEntityStart();
            ref var cache        = ref threadCache.cache;
            int     indexInChunk = context.indexInChunk;
            var     transform    = new RefRW<WorldTransform>(cache.chunkTransforms, indexInChunk);
            if (cache.isRoot)
            {
                var extra  = cache.chunkEntityInHierarchyCleanupAccessor.Length > 0 ? cache.chunkEntityInHierarchyCleanupAccessor[indexInChunk].GetUnsafeReadOnlyPtr() : null;
                var handle = new EntityInHierarchyHandle
                {
                    m_hierarchy      = cache.chunkEntityInHierarchyAccessor[indexInChunk].AsNativeArray(),
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
            else if (cache.isChild)
            {
                var rr     = cache.chunkRootReferences[indexInChunk];
                var handle = rr.ToHandle(ref hierarchyLookup, ref cleanupLookup);
                return new TransformAspect
                {
                    m_worldTransform = transform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                };
            }
            else
            {
                return new TransformAspect
                {
                    m_worldTransform = transform,
                    m_handle         = default,
                    m_esil           = esil,
                    m_access         = cache.chunkEntities + indexInChunk
                };
            }
        }

        public bool OnChunkBegin(in JobContext context)
        {
            CheckChunkStart(in context);
            if (!threadCache.isCreated)
            {
                threadCache                                      = new ThreadCache<Cache>(default);
                threadCache.cache.transformHandle                = transformLookup.lookup.ToHandle(false);
                threadCache.cache.entityInHierarchyHandle        = hierarchyLookup.ToHandle(true);
                threadCache.cache.entityInHierarchyCleanupHandle = cleanupLookup.ToHandle(true);
            }

            ref var cache         = ref threadCache.cache;
            var     chunk         = context.chunk;
            cache.chunkTransforms = chunk.GetNativeArray(ref cache.transformHandle);
            cache.chunkEntities   = chunk.GetEntityDataPtrRO(esil.AsEntityTypeHandle());
            cache.isRoot          = chunk.Has(ref cache.entityInHierarchyHandle);
            if (cache.isRoot)
            {
                cache.isChild                               = false;
                cache.chunkEntityInHierarchyAccessor        = chunk.GetBufferAccessorRO(ref cache.entityInHierarchyHandle);
                cache.chunkEntityInHierarchyCleanupAccessor = chunk.GetBufferAccessorRO(ref cache.entityInHierarchyCleanupHandle);
                cache.chunkRootReferences                   = default;
            }
            else if (chunk.Has(ref rootReferenceHandle))
            {
                cache.isChild                               = true;
                cache.isRoot                                = false;
                cache.chunkEntityInHierarchyAccessor        = default;
                cache.chunkEntityInHierarchyCleanupAccessor = default;
                cache.chunkRootReferences                   = chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
            }
            else
            {
                cache.isChild                               = false;
                cache.isRoot                                = false;
                cache.chunkEntityInHierarchyAccessor        = default;
                cache.chunkEntityInHierarchyCleanupAccessor = default;
                cache.chunkRootReferences                   = default;
            }

            isChunkInitialized = true;
            return true;
        }

        public void OnChunkEnd(in JobContext context, bool chunkWasExecuted)
        {
            isChunkInitialized = false;
        }

        public void OnGroupBegin(in GroupContext groupContext)
        {
            isGroupInitialized = true;
        }

        public void OnGroupEnd(in GroupContext groupContext)
        {
            isGroupInitialized = false;
        }

        public JobHandle ScheduleGroupUnions(ChunkGroupBuilder builder, JobHandle inputDeps)
        {
            return new GroupUnionsJob
            {
                builder             = builder,
                entityHandle        = esil.AsEntityTypeHandle(),
                rootReferenceHandle = rootReferenceHandle,
            }.Schedule(inputDeps);
        }

        public void UpdateForApi(ref SystemState state)
        {
            transformLookup.Update(ref state);
            hierarchyLookup.Update(ref state);
            cleanupLookup.Update(ref state);
            rootReferenceHandle.Update(ref state);
            esil.Update(ref state);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckChunkStart(in JobContext context)
        {
            if (context.mode == ScheduleMode.ScheduleParallel && !isGroupInitialized)
                throw new System.InvalidOperationException("Attempted to run a chunk when the group wasn't initialized.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckEntityStart()
        {
            if (!isChunkInitialized)
                throw new System.InvalidOperationException("Attempted to run an Entity when the chunk wasn't initialized.");
        }

        [BurstCompile]
        struct GroupUnionsJob : IJob
        {
            public ChunkGroupBuilder                             builder;
            [ReadOnly] public EntityTypeHandle                   entityHandle;
            [ReadOnly] public ComponentTypeHandle<RootReference> rootReferenceHandle;

            HasChecker<RootReference>     rootReferenceChecker;
            HasChecker<EntityInHierarchy> hierarchyChecker;

            public unsafe void Execute()
            {
                if (builder.groupRanges.IsEmpty)
                    return;

                int entitiesInHierarchyCount = 0;
                var chunksInHierarchy        = new UnsafeList<ChunkWithHierarchy>(builder.chunks.Length, Allocator.Temp);
                for (int groupIndex = 0; groupIndex < builder.groupRanges.Length; groupIndex++)
                {
                    var range = builder.groupRanges[groupIndex];
                    for (int i = 0; i < range.count; i++)
                    {
                        var chunkIndex = range.start + i;
                        var chunk      = builder.chunks[chunkIndex];
                        if (rootReferenceChecker[chunk.chunk])
                        {
                            chunksInHierarchy.AddNoResize(new ChunkWithHierarchy
                            {
                                groupIndex = groupIndex,
                                chunkIndex = chunkIndex,
                                isRoot     = false
                            });
                            entitiesInHierarchyCount += chunk.enabledEntityCount;
                        }
                        else if (hierarchyChecker[chunk.chunk])
                        {
                            chunksInHierarchy.AddNoResize(new ChunkWithHierarchy
                            {
                                groupIndex = groupIndex,
                                chunkIndex = chunkIndex,
                                isRoot     = true
                            });
                            entitiesInHierarchyCount += chunk.enabledEntityCount;
                        }
                    }
                }

                var entityToFirstGroupHashmap = new UnsafeHashMap<Entity, int>(entitiesInHierarchyCount, Allocator.Temp);
                foreach (var chunkWithHierarchy in chunksInHierarchy)
                {
                    var chunk      = builder.chunks[chunkWithHierarchy.chunkIndex];
                    var enumerator = new ChunkEntityBatchEnumerator(chunk.useEnabledMask, chunk.chunkEnabledMask, chunk.chunk.Count);
                    if (chunkWithHierarchy.isRoot)
                    {
                        var entities = chunk.chunk.GetEntityDataPtrRO(entityHandle);
                        while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                        {
                            for (int entityIndex = rangeStart, rangeEnd = rangeStart + rangeCount; entityIndex < rangeEnd; entityIndex++)
                            {
                                if (entityToFirstGroupHashmap.TryGetValue(entities[entityIndex], out var firstGroupIndex))
                                    builder.Union(chunkWithHierarchy.groupIndex, firstGroupIndex);
                                else
                                    entityToFirstGroupHashmap.Add(entities[entityIndex], chunkWithHierarchy.groupIndex);
                            }
                        }
                    }
                    else
                    {
                        var rootRefs = chunk.chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
                        while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                        {
                            for (int entityIndex = rangeStart, rangeEnd = rangeStart + rangeCount; entityIndex < rangeEnd; entityIndex++)
                            {
                                var root = rootRefs[entityIndex].rootEntity;
                                if (entityToFirstGroupHashmap.TryGetValue(root, out var firstGroupIndex))
                                    builder.Union(chunkWithHierarchy.groupIndex, firstGroupIndex);
                                else
                                    entityToFirstGroupHashmap.Add(root, chunkWithHierarchy.groupIndex);
                            }
                        }
                    }
                }
                builder.ApplyChanges();
            }

            struct ChunkWithHierarchy
            {
                public int  groupIndex;
                public int  chunkIndex;
                public bool isRoot;
            }
        }
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public unsafe struct TransformDeferableAspectParameterHandle : IGroupedParameterHandle<TransformDeferableAspect>
    {
        struct Cache
        {
            public NativeList<TransformBatchWriteCommand> deferredCommands;
        }
        TransformAspectParameterHandle handle;
        ThreadCache<Cache>             threadCache;

        public FluentQuery AppendToQuery(FluentQuery query) => handle.AppendToQuery(query);

        public void CreateForApi(ref SystemState state) => handle.CreateForApi(ref state);

        public TransformDeferableAspect GetParameter(in JobContext context)
        {
            var transform = handle.GetParameter(in context);
            return new TransformDeferableAspect
            {
                transform = transform,
                commands  = threadCache.cache.deferredCommands,
            };
        }

        public bool OnChunkBegin(in JobContext context) => handle.OnChunkBegin(in context);

        public void OnChunkEnd(in JobContext context, bool chunkWasExecuted) => handle.OnChunkEnd(in context, chunkWasExecuted);

        public void OnGroupBegin(in GroupContext groupContext)
        {
            handle.OnGroupBegin(in groupContext);
            InitializeCache();
        }

        public void OnGroupEnd(in GroupContext groupContext)
        {
            FlushCache();
            handle.OnGroupEnd(in groupContext);
        }

        public JobHandle ScheduleGroupUnions(ChunkGroupBuilder builder, JobHandle inputDeps) => handle.ScheduleGroupUnions(builder, inputDeps);

        public void UpdateForApi(ref SystemState state) => handle.UpdateForApi(ref state);

        void InitializeCache()
        {
            if (!threadCache.isCreated)
            {
                threadCache                        = new ThreadCache<Cache>(default);
                threadCache.cache.deferredCommands = new NativeList<TransformBatchWriteCommand>(Allocator.Temp);
            }
        }

        void FlushCache()
        {
            threadCache.cache.deferredCommands.ApplyTransforms();
            threadCache.cache.deferredCommands.Clear();
        }
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public unsafe struct TransformReadAspectParameterHandle : IParameterHandle<TransformReadAspect>
    {
        struct Cache
        {
            public ComponentTypeHandle<WorldTransform>        transformHandle;
            public BufferTypeHandle<EntityInHierarchy>        entityInHierarchyHandle;
            public BufferTypeHandle<EntityInHierarchyCleanup> entityInHierarchyCleanupHandle;

            public NativeArray<WorldTransform>              chunkTransforms;
            public BufferAccessor<EntityInHierarchy>        chunkEntityInHierarchyAccessor;
            public BufferAccessor<EntityInHierarchyCleanup> chunkEntityInHierarchyCleanupAccessor;
            public RootReference*                           chunkRootReferences;
            public bool                                     isRoot;
            public bool                                     isChild;
        }

        [ReadOnly] ComponentLookup<WorldTransform>        transformLookup;
        [ReadOnly] BufferLookup<EntityInHierarchy>        hierarchyLookup;
        [ReadOnly] BufferLookup<EntityInHierarchyCleanup> cleanupLookup;
        [ReadOnly] ComponentTypeHandle<RootReference>     rootReferenceHandle;
        [ReadOnly] EntityStorageInfoLookup                esil;
        ThreadCache<Cache>                                threadCache;

        HasChecker<EntityInHierarchy>        hierarchyChecker;
        HasChecker<EntityInHierarchyCleanup> cleanupChecker;

        bool isChunkInitialized;

        public FluentQuery AppendToQuery(FluentQuery query)
        {
            return query.With<WorldTransform>(true);
        }

        public void CreateForApi(ref SystemState state)
        {
            transformLookup     = state.GetComponentLookup<WorldTransform>(true);
            hierarchyLookup     = state.GetBufferLookup<EntityInHierarchy>(true);
            cleanupLookup       = state.GetBufferLookup<EntityInHierarchyCleanup>(true);
            rootReferenceHandle = state.GetComponentTypeHandle<RootReference>(true);
            esil                = state.GetEntityStorageInfoLookup();
        }

        public TransformReadAspect GetParameter(in JobContext context)
        {
            CheckEntityStart();
            ref var cache        = ref threadCache.cache;
            int     indexInChunk = context.indexInChunk;
            var     transform    = new RefRO<WorldTransform>(cache.chunkTransforms, indexInChunk);
            if (cache.isRoot)
            {
                var extra  = cache.chunkEntityInHierarchyCleanupAccessor.Length > 0 ? cache.chunkEntityInHierarchyCleanupAccessor[indexInChunk].GetUnsafeReadOnlyPtr() : null;
                var handle = new EntityInHierarchyHandle
                {
                    m_hierarchy      = cache.chunkEntityInHierarchyAccessor[indexInChunk].AsNativeArray(),
                    m_extraHierarchy = (EntityInHierarchy*)extra,
                    m_index          = 0
                };
                return new TransformReadAspect
                {
                    m_worldTransform = transform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                };
            }
            else if (cache.isChild)
            {
                var rr     = cache.chunkRootReferences[indexInChunk];
                var handle = rr.ToHandle(ref hierarchyLookup, ref cleanupLookup);
                return new TransformReadAspect
                {
                    m_worldTransform = transform,
                    m_handle         = handle,
                    m_esil           = esil,
                    m_accessType     = TransformAspect.AccessType.ComponentLookup,
                    m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                };
            }
            else
            {
                return new TransformReadAspect
                {
                    m_worldTransform = transform,
                    m_handle         = default,
                };
            }
        }

        public bool OnChunkBegin(in JobContext context)
        {
            if (!threadCache.isCreated)
            {
                threadCache                                      = new ThreadCache<Cache>(default);
                threadCache.cache.transformHandle                = transformLookup.ToHandle(true);
                threadCache.cache.entityInHierarchyHandle        = hierarchyLookup.ToHandle(true);
                threadCache.cache.entityInHierarchyCleanupHandle = cleanupLookup.ToHandle(true);
            }

            ref var cache         = ref threadCache.cache;
            var     chunk         = context.chunk;
            cache.chunkTransforms = chunk.GetNativeArray(ref cache.transformHandle);
            cache.isRoot          = chunk.Has(ref cache.entityInHierarchyHandle);
            if (cache.isRoot)
            {
                cache.isChild                               = false;
                cache.chunkEntityInHierarchyAccessor        = chunk.GetBufferAccessorRO(ref cache.entityInHierarchyHandle);
                cache.chunkEntityInHierarchyCleanupAccessor = chunk.GetBufferAccessorRO(ref cache.entityInHierarchyCleanupHandle);
                cache.chunkRootReferences                   = default;
            }
            else if (chunk.Has(ref rootReferenceHandle))
            {
                cache.isChild                               = true;
                cache.isRoot                                = false;
                cache.chunkEntityInHierarchyAccessor        = default;
                cache.chunkEntityInHierarchyCleanupAccessor = default;
                cache.chunkRootReferences                   = chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
            }
            else
            {
                cache.isChild                               = false;
                cache.isRoot                                = false;
                cache.chunkEntityInHierarchyAccessor        = default;
                cache.chunkEntityInHierarchyCleanupAccessor = default;
                cache.chunkRootReferences                   = default;
            }

            isChunkInitialized = true;
            return true;
        }

        public void OnChunkEnd(in JobContext context, bool chunkWasExecuted)
        {
            isChunkInitialized = false;
        }

        public void UpdateForApi(ref SystemState state)
        {
            transformLookup.Update(ref state);
            hierarchyLookup.Update(ref state);
            cleanupLookup.Update(ref state);
            rootReferenceHandle.Update(ref state);
            esil.Update(ref state);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckEntityStart()
        {
            if (!isChunkInitialized)
                throw new System.InvalidOperationException("Attempted to run an Entity when the chunk wasn't initialized.");
        }
    }
}

#endif

