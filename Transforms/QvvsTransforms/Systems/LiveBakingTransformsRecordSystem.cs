#if !LATIOS_TRANSFORMS_UNITY
using System;
using Latios.Systems;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(BeforeLiveBakingSuperSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public unsafe partial struct LiveBakingTransformsRecordSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_rootsQuery;
        EntityQuery          m_childrenQuery;
        EntityQuery          m_worldTransformsQuery;
        EntityQuery          m_tickedTransformsQuery;
        EntityQuery          m_dynamicParentQuery;
        bool                 m_firstUpdate;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_rootsQuery           = state.Fluent().With<LiveBakedTag, EntityInHierarchy>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_childrenQuery        = state.Fluent().With<LiveBakedTag, RootReference>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_worldTransformsQuery =
                state.Fluent().With<LiveBakedTag, WorldTransform>(true).WithAnyEnabled<EntityInHierarchy, RootReference>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_tickedTransformsQuery =
                state.Fluent().With<LiveBakedTag, TickedWorldTransform>(true).WithAnyEnabled<EntityInHierarchy,
                                                                                             RootReference>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_dynamicParentQuery = state.Fluent().WithAnyEnabled<LiveAddedParentTag, LiveRemovedParentTag>(true).Build();

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new LiveTransformCapture());

            m_firstUpdate = true;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var rootsOrderVersion                = state.EntityManager.GetComponentOrderVersion<EntityInHierarchy>();
            var childrenOrderVersion             = state.EntityManager.GetComponentOrderVersion<RootReference>();
            var worldTransformOrderVersion       = state.EntityManager.GetComponentOrderVersion<WorldTransform>();
            var tickedWorldTransformOrderVersion = state.EntityManager.GetComponentOrderVersion<TickedWorldTransform>();

            var rootCount        = m_rootsQuery.CalculateEntityCountWithoutFiltering();
            var roots            = CollectionHelper.CreateNativeArray<LiveTransformCapture.Root>(rootCount, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var rootStartIndices = m_rootsQuery.CalculateBaseEntityIndexArray(state.WorldUpdateAllocator);
            var childrenCount    = m_childrenQuery.CalculateEntityCountWithoutFiltering();
            var children         = CollectionHelper.CreateNativeArray<LiveTransformCapture.Child>(childrenCount,
                                                                                                  state.WorldUpdateAllocator,
                                                                                                  NativeArrayOptions.UninitializedMemory);
            var childrenStartIndices = m_childrenQuery.CalculateBaseEntityIndexArray(state.WorldUpdateAllocator);

            state.Dependency = new RootsJob
            {
                roots                      = roots,
                chunkStartIndices          = rootStartIndices,
                worldTransformHandle       = GetComponentTypeHandle<WorldTransform>(true),
                tickedWorldTransformHandle = GetComponentTypeHandle<TickedWorldTransform>(true),
                hierarchyHandle            = GetBufferTypeHandle<EntityInHierarchy>(true),
                allocator                  = state.WorldUpdateAllocator,
            }.ScheduleParallel(m_rootsQuery, state.Dependency);

            state.Dependency = new ChildrenJob
            {
                children                       = children,
                chunkStartIndices              = childrenStartIndices,
                entityHandle                   = GetEntityTypeHandle(),
                rootReferenceHandle            = GetComponentTypeHandle<RootReference>(true),
                worldTransformHandle           = GetComponentTypeHandle<WorldTransform>(true),
                tickedWorldTransformHandle     = GetComponentTypeHandle<TickedWorldTransform>(true),
                entityInHierarchyLookup        = GetBufferLookup<EntityInHierarchy>(true),
                entityInHierarchyCleanupLookup = GetBufferLookup<EntityInHierarchyCleanup>(true),
                esil                           = GetEntityStorageInfoLookup(),
                worldTransformLookup           = GetComponentLookup<WorldTransform>(true),
                tickedWorldTransformLookup     = GetComponentLookup<TickedWorldTransform>(true),
            }.ScheduleParallel(m_childrenQuery, state.Dependency);

            bool editorWorld       = (state.WorldUnmanaged.Flags & WorldFlags.Editor) == WorldFlags.Editor;
            bool hasDynamicParents = !m_dynamicParentQuery.IsEmptyIgnoreFilter;
            bool somethingChanged  = !m_firstUpdate;
            if (somethingChanged)
            {
                var  capture                             = latiosWorld.worldBlackboardEntity.GetCollectionComponent<LiveTransformCapture>(false);
                bool rootsChangedStructurally            = rootsOrderVersion != capture.rootsOrderVersion;
                bool childrenChangedStructurally         = childrenOrderVersion != capture.childrenOrderVersion;
                bool worldTransformsChangedStructurally  = worldTransformOrderVersion != capture.worldTransformOrderVersion;
                bool tickedTransformsChangedStructurally = tickedWorldTransformOrderVersion != capture.tickedWorldTransformOrderVersion;

                m_rootsQuery.SetChangedVersionFilter(ComponentType.ReadOnly<EntityInHierarchy>());
                m_childrenQuery.SetChangedVersionFilter(ComponentType.ReadOnly<RootReference>());
                m_worldTransformsQuery.SetChangedVersionFilter(ComponentType.ReadOnly<WorldTransform>());
                m_tickedTransformsQuery.SetChangedVersionFilter(ComponentType.ReadOnly<TickedWorldTransform>());

                m_rootsQuery.SetOverrideChangeFilterVersion(capture.changeVersion);
                m_childrenQuery.SetOverrideChangeFilterVersion(capture.changeVersion);
                m_worldTransformsQuery.SetOverrideChangeFilterVersion(capture.changeVersion);
                m_tickedTransformsQuery.SetOverrideChangeFilterVersion(capture.changeVersion);

                bool hierarchyBuffersChanged = !m_rootsQuery.IsEmpty;
                bool rootReferencesChanged   = !m_childrenQuery.IsEmpty;
                bool worldTransformsChanged  = !m_worldTransformsQuery.IsEmpty;
                bool tickedTransformsChanged = !m_tickedTransformsQuery.IsEmpty;

                somethingChanged = rootsChangedStructurally || childrenChangedStructurally || worldTransformsChangedStructurally || tickedTransformsChangedStructurally ||
                                   hierarchyBuffersChanged || rootReferencesChanged || worldTransformsChanged || tickedTransformsChanged;

                m_firstUpdate = false;
            }
            latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new LiveTransformCapture
            {
                roots                            = roots,
                children                         = children,
                changeVersion                    = state.GlobalSystemVersion,
                rootsOrderVersion                = rootsOrderVersion,
                childrenOrderVersion             = childrenOrderVersion,
                worldTransformOrderVersion       = worldTransformOrderVersion,
                tickedWorldTransformOrderVersion = tickedWorldTransformOrderVersion,
                cleanEditorWorld                 = editorWorld && !hasDynamicParents && !somethingChanged
            });
        }

        [BurstCompile]
        struct RootsJob : IJobChunk
        {
            [NativeDisableParallelForRestriction] public NativeArray<LiveTransformCapture.Root> roots;
            [ReadOnly] public NativeArray<int>                                                  chunkStartIndices;
            [ReadOnly] public ComponentTypeHandle<WorldTransform>                               worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<TickedWorldTransform>                         tickedWorldTransformHandle;
            [ReadOnly] public BufferTypeHandle<EntityInHierarchy>                               hierarchyHandle;

            public AllocatorManager.AllocatorHandle allocator;

            HasChecker<LiveRemovedParentTag> liveRemovedParentChecker;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var  dstIndex             = chunkStartIndices[unfilteredChunkIndex];
                var  worldTransforms      = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                var  tickedTransforms     = chunk.GetComponentDataPtrRO(ref tickedWorldTransformHandle);
                var  hierarchies          = chunk.GetBufferAccessorRO(ref hierarchyHandle);
                bool hasWorldTransforms   = worldTransforms != null;
                bool hasTickedTransforms  = tickedTransforms != null;
                bool hasLiveRemovedParent = liveRemovedParentChecker[chunk];

                int totalHierarchyCount = 0;
                for (int i = 0; i < chunk.Count; i++)
                    totalHierarchyCount += hierarchies[i].Length;

                var dstHierarchyBuffer = AllocatorManager.Allocate<EntityInHierarchy>(allocator, totalHierarchyCount);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var hierarchy   = hierarchies[i].AsNativeArray();
                    roots[dstIndex] = new LiveTransformCapture.Root
                    {
                        entity                  = hierarchy[0].entity,
                        hierarchyStart          = dstHierarchyBuffer,
                        hierarchyCount          = hierarchy.Length,
                        runtimeRemovedParent    = hasLiveRemovedParent,
                        hasWorldTransform       = hasWorldTransforms,
                        hasTickedWorldTransform = hasTickedTransforms,
                        worldTransform          = hasWorldTransforms ? worldTransforms[i].worldTransform : default,
                        tickedWorldTransform    = hasTickedTransforms ? tickedTransforms[i].worldTransform : default,
                    };
                    dstIndex++;

                    var buffer = new Span<EntityInHierarchy>(dstHierarchyBuffer, hierarchy.Length);
                    hierarchy.AsReadOnlySpan().CopyTo(buffer);
                    dstHierarchyBuffer += hierarchy.Length;
                }
            }
        }

        [BurstCompile]
        struct ChildrenJob : IJobChunk
        {
            [NativeDisableParallelForRestriction] public NativeArray<LiveTransformCapture.Child> children;
            [ReadOnly] public NativeArray<int>                                                   chunkStartIndices;
            [ReadOnly] public EntityTypeHandle                                                   entityHandle;
            [ReadOnly] public ComponentTypeHandle<RootReference>                                 rootReferenceHandle;
            [ReadOnly] public ComponentTypeHandle<WorldTransform>                                worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<TickedWorldTransform>                          tickedWorldTransformHandle;
            [ReadOnly] public BufferLookup<EntityInHierarchy>                                    entityInHierarchyLookup;
            [ReadOnly] public BufferLookup<EntityInHierarchyCleanup>                             entityInHierarchyCleanupLookup;
            [ReadOnly] public EntityStorageInfoLookup                                            esil;
            [ReadOnly] public ComponentLookup<WorldTransform>                                    worldTransformLookup;
            [ReadOnly] public ComponentLookup<TickedWorldTransform>                              tickedWorldTransformLookup;

            HasChecker<LiveAddedParentTag> liveAddedParentChecker;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var  dstIndex            = chunkStartIndices[unfilteredChunkIndex];
                var  entities            = chunk.GetEntityDataPtrRO(entityHandle);
                var  rootRefs            = chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
                var  worldTransforms     = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                var  tickedTransforms    = chunk.GetComponentDataPtrRO(ref tickedWorldTransformHandle);
                bool hasWorldTransforms  = worldTransforms != null;
                bool hasTickedTransforms = tickedTransforms != null;
                bool hasLiveAddedParent  = liveAddedParentChecker[chunk];

                for (int i = 0; i < chunk.Count; i++)
                {
                    var rootRef = rootRefs[i];
                    var handle  = rootRef.ToHandle(ref entityInHierarchyLookup, ref entityInHierarchyCleanupLookup);
                    var local   =
                        hasWorldTransforms ? TransformTools.Unsafe.LocalTransformFrom(in handle, in worldTransforms[i], esil, ref worldTransformLookup, out _) : default;
                    var tickedLocal = hasTickedTransforms ? TransformTools.Unsafe.TickedLocalTransformFrom(in handle,
                                                                                                           in tickedTransforms[i],
                                                                                                           esil,
                                                                                                           ref tickedWorldTransformLookup,
                                                                                                           out _) : default;
                    var parent         = handle.FindParent(esil);
                    children[dstIndex] = new LiveTransformCapture.Child
                    {
                        entity                  = entities[i],
                        runtimeAddedParent      = hasLiveAddedParent,
                        hasWorldTransform       = hasWorldTransforms,
                        hasTickedWorldTransform = hasTickedTransforms,
                        parent                  = parent.entity,
                        parentIsBlood           = !parent.isNull && parent.indexInHierarchy == handle.bloodParent.indexInHierarchy,
                        siblingIndex            = handle.bloodSiblingIndex,
                        rootRef                 = rootRef,
                        localTransform          = local,
                        worldTransform          = hasWorldTransforms ? worldTransforms[i].worldTransform : default,
                        tickedLocalTransform    = tickedLocal,
                        tickedWorldTransform    = hasTickedTransforms ? tickedTransforms[i].worldTransform : default,
                    };
                    dstIndex++;
                }
            }
        }
    }
}
#endif

