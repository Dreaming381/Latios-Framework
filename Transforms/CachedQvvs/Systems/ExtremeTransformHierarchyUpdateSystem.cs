#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

// This system uses PreviousParent in all cases because it is guaranteed to be updated
// (ParentSystem just ran) and it is updated when the entity is enabled so change filters
// work correctly.
namespace Latios.Transforms.Systems
{
    [DisableAutoCreation]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public unsafe partial struct ExtremeTransformHierarchyUpdateSystem : ISystem
    {
        EntityQuery m_metaQuery;

        // For a 32-bit depth mask, the upper 16 bits are used as a scratch list if updates are needed.
        const int kMaxDepthIterations = 16;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_metaQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkDepthMask>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var chunkListArray = new ChunkListArray(state.WorldUpdateAllocator);
            var blockLists     =
                CollectionHelper.CreateNativeArray<UnsafeParallelBlockList>(kMaxDepthIterations, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

            var worldTransformLookup = GetComponentLookup<WorldTransform>(false);
            var depthHandle          = GetComponentTypeHandle<Depth>(true);
            var depthMaskHandle      = GetComponentTypeHandle<ChunkDepthMask>(false);
            var localTransformHandle = GetComponentTypeHandle<LocalTransform>(false);
            var parentHandle         = GetComponentTypeHandle<PreviousParent>(true);
            var worldTransformHandle = GetComponentTypeHandle<WorldTransform>(false);

            state.Dependency = new AllocateBlockListsJob
            {
                chunkBlockLists = blockLists,
                allocator       = state.WorldUpdateAllocator
            }.Schedule(kMaxDepthIterations, 1, state.Dependency);

            state.Dependency = new ClassifyChunksAndResetMasksJob
            {
                headerHandle    = GetComponentTypeHandle<ChunkHeader>(true),
                depthMaskHandle = depthMaskHandle,
                chunkBlockLists = blockLists
            }.ScheduleParallel(m_metaQuery, state.Dependency);

            state.Dependency = new FlattenBlocklistsJob
            {
                chunkBlockLists = blockLists,
                chunkListArray  = chunkListArray
            }.ScheduleParallel(kMaxDepthIterations, 1, state.Dependency);

            var checkJob = new CheckIfTransformsShouldUpdateForSingleDepthLevelJob
            {
                depthHandle          = depthHandle,
                depthMaskHandle      = depthMaskHandle,
                lastSystemVersion    = state.LastSystemVersion,
                localTransformHandle = localTransformHandle,
                worldTransformLookup = worldTransformLookup,
                parentHandle         = parentHandle,
            };

            var updateJob = new UpdateTransformsOfSingleDepthLevelJob
            {
                depthHandle                  = depthHandle,
                depthMaskHandle              = depthMaskHandle,
                localTransformHandle         = localTransformHandle,
                worldTransformLookup         = worldTransformLookup,
                worldTransformHandle         = worldTransformHandle,
                parentHandle                 = parentHandle,
                parentToWorldTransformHandle = GetComponentTypeHandle<ParentToWorldTransform>(false),
                hierarchyUpdateModeHandle    = GetComponentTypeHandle<HierarchyUpdateMode>(true)
            };

            for (int i = 0; i < kMaxDepthIterations; i++)
            {
                var chunkList       = chunkListArray[i];
                checkJob.depth      = i;
                checkJob.chunkList  = chunkList.AsDeferredJobArray();
                state.Dependency    = checkJob.Schedule(chunkList, 1, state.Dependency);
                updateJob.depth     = i;
                updateJob.chunkList = chunkList.AsDeferredJobArray();
                state.Dependency    = updateJob.Schedule(chunkList, 1, state.Dependency);
            }

            var finalChunkList = chunkListArray[kMaxDepthIterations - 1];

            state.Dependency = new UpdateMatricesOfDeepChildrenJob
            {
                childLookup               = GetBufferLookup<Child>(true),
                childHandle               = GetBufferTypeHandle<Child>(true),
                chunkList                 = finalChunkList.AsDeferredJobArray(),
                depthHandle               = depthHandle,
                depthLevel                = kMaxDepthIterations - 1,
                lastSystemVersion         = state.LastSystemVersion,
                localTransformLookup      = GetComponentLookup<LocalTransform>(false),
                worldTransformLookup      = worldTransformLookup,
                worldTransformHandle      = worldTransformHandle,
                parentLookup              = GetComponentLookup<PreviousParent>(true),
                parentToWorldLookup       = GetComponentLookup<ParentToWorldTransform>(false),
                hierarchyUpdateModeLookup = GetComponentLookup<HierarchyUpdateMode>(true)
            }.Schedule(finalChunkList, 1, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        struct ChunkListArray
        {
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l0;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l1;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l2;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l3;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l4;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l5;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l6;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l7;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l8;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l9;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l10;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l11;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l12;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l13;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l14;
            [NativeDisableParallelForRestriction] NativeList<ArchetypeChunk> l15;

            public ChunkListArray(AllocatorManager.AllocatorHandle allocator)
            {
                l0  = new NativeList<ArchetypeChunk>(allocator);
                l1  = new NativeList<ArchetypeChunk>(allocator);
                l2  = new NativeList<ArchetypeChunk>(allocator);
                l3  = new NativeList<ArchetypeChunk>(allocator);
                l4  = new NativeList<ArchetypeChunk>(allocator);
                l5  = new NativeList<ArchetypeChunk>(allocator);
                l6  = new NativeList<ArchetypeChunk>(allocator);
                l7  = new NativeList<ArchetypeChunk>(allocator);
                l8  = new NativeList<ArchetypeChunk>(allocator);
                l9  = new NativeList<ArchetypeChunk>(allocator);
                l10 = new NativeList<ArchetypeChunk>(allocator);
                l11 = new NativeList<ArchetypeChunk>(allocator);
                l12 = new NativeList<ArchetypeChunk>(allocator);
                l13 = new NativeList<ArchetypeChunk>(allocator);
                l14 = new NativeList<ArchetypeChunk>(allocator);
                l15 = new NativeList<ArchetypeChunk>(allocator);
            }

            public NativeList<ArchetypeChunk> this[int index]
            {
                get
                {
                    return index switch
                           {
                               0 => l0,
                               1 => l1,
                               2 => l2,
                               3 => l3,
                               4 => l4,
                               5 => l5,
                               6 => l6,
                               7 => l7,
                               8 => l8,
                               9 => l9,
                               10 => l10,
                               11 => l11,
                               12 => l12,
                               13 => l13,
                               14 => l14,
                               _ => l15,
                           };
                }
            }
        }

        [BurstCompile]
        struct AllocateBlockListsJob : IJobParallelFor
        {
            public NativeArray<UnsafeParallelBlockList> chunkBlockLists;
            public AllocatorManager.AllocatorHandle     allocator;

            public void Execute(int i)
            {
                chunkBlockLists[i] = new UnsafeParallelBlockList(sizeof(ArchetypeChunk), 64, allocator);
            }
        }

        [BurstCompile]
        struct ClassifyChunksAndResetMasksJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkHeader> headerHandle;
            public ComponentTypeHandle<ChunkDepthMask>         depthMaskHandle;

            [NativeDisableParallelForRestriction]
            public NativeArray<UnsafeParallelBlockList> chunkBlockLists;

            [NativeSetThreadIndex]
            int threadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var headers    = chunk.GetNativeArray(ref headerHandle);
                var depthMasks = chunk.GetNativeArray(ref depthMaskHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var mask                   = depthMasks[i];
                    mask.chunkDepthMask.Value &= 0xffff;
                    depthMasks[i]              = mask;

                    var dynamicMask = mask.chunkDepthMask;
                    int j;
                    while ((j = dynamicMask.CountTrailingZeros()) < kMaxDepthIterations)
                    {
                        chunkBlockLists[j].Write(headers[i].ArchetypeChunk, threadIndex);
                        dynamicMask.SetBits(j, false);
                    }
                }
            }
        }

        [BurstCompile]
        struct FlattenBlocklistsJob : IJobFor
        {
            public NativeArray<UnsafeParallelBlockList>                 chunkBlockLists;
            [NativeDisableParallelForRestriction] public ChunkListArray chunkListArray;

            public void Execute(int index)
            {
                var list = chunkListArray[index];
                list.ResizeUninitialized(chunkBlockLists[index].Count());
                chunkBlockLists[index].GetElementValues(list.AsArray());
            }
        }

        [BurstCompile]
        struct CheckIfTransformsShouldUpdateForSingleDepthLevelJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunkList;

            [ReadOnly] public ComponentTypeHandle<LocalTransform> localTransformHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousParent> parentHandle;
            [ReadOnly] public ComponentTypeHandle<Depth>          depthHandle;
            [ReadOnly] public ComponentLookup<WorldTransform>     worldTransformLookup;

            public ComponentTypeHandle<ChunkDepthMask> depthMaskHandle;

            public int  depth;
            public uint lastSystemVersion;

            public void Execute(int index)
            {
                var chunk = chunkList[index];

                var parents = chunk.GetNativeArray(ref parentHandle);
                var depths  = chunk.GetNativeArray(ref depthHandle);

                bool hasLocalTransform = chunk.Has(ref localTransformHandle);

                if (chunk.DidChange(ref parentHandle, lastSystemVersion) || chunk.DidChange(ref localTransformHandle, lastSystemVersion) ||
                    (!hasLocalTransform && chunk.DidOrderChange(lastSystemVersion)))  // Catches addition of CopyParentWorldTransformTag
                {
                    // Fast path. No need to check for changes on parent.
                    SetNeedsUpdate(chunk);
                }
                else
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (depth == depths[i].depth)
                        {
                            var parent = parents[i].previousParent;
                            if (worldTransformLookup.DidChange(parent, lastSystemVersion))
                            {
                                SetNeedsUpdate(chunk);
                                return;
                            }
                        }
                    }
                }
            }

            void SetNeedsUpdate(ArchetypeChunk chunk)
            {
                var depthMask = chunk.GetChunkComponentData(ref depthMaskHandle);
                depthMask.chunkDepthMask.SetBits(depth + 16, true);
                chunk.SetChunkComponentData(ref depthMaskHandle, depthMask);
            }
        }

        [BurstCompile]
        struct UpdateTransformsOfSingleDepthLevelJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>                                        chunkList;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<WorldTransform> worldTransformHandle;
            public ComponentTypeHandle<ParentToWorldTransform>                                   parentToWorldTransformHandle;
            public ComponentTypeHandle<LocalTransform>                                           localTransformHandle;

            [ReadOnly] public ComponentTypeHandle<PreviousParent>      parentHandle;
            [ReadOnly] public ComponentTypeHandle<Depth>               depthHandle;
            [ReadOnly] public ComponentTypeHandle<HierarchyUpdateMode> hierarchyUpdateModeHandle;
            [ReadOnly] public ComponentLookup<WorldTransform>          worldTransformLookup;

            [ReadOnly] public ComponentTypeHandle<ChunkDepthMask> depthMaskHandle;

            public int depth;

            public unsafe void Execute(int index)
            {
                var chunk = chunkList[index];
                if (!chunk.GetChunkComponentData(ref depthMaskHandle).chunkDepthMask.IsSet(depth + 16))
                    return;

                var parents         = chunk.GetNativeArray(ref parentHandle);
                var depths          = chunk.GetNativeArray(ref depthHandle);
                var worldTransforms = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRW(ref worldTransformHandle);

                if (chunk.Has(ref localTransformHandle))
                {
                    var flags           = chunk.GetComponentDataPtrRO(ref hierarchyUpdateModeHandle);
                    var hasFlags        = flags != null;
                    var localTransforms = (TransformQvs*)(hasFlags ? chunk.GetRequiredComponentDataPtrRW(ref localTransformHandle) :
                                                          chunk.GetRequiredComponentDataPtrRO(ref localTransformHandle));
                    var parentTransforms = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRW(ref parentToWorldTransformHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (depth == depths[i].depth)
                        {
                            ref readonly var parentWorldTransform = ref worldTransformLookup.GetRefRO(parents[i].previousParent).ValueRO.worldTransform;
                            parentTransforms[i]                   = parentWorldTransform;

                            if (hasFlags)
                            {
                                HierarchyInternalUtilities.UpdateTransform(ref worldTransforms[i],
                                                                           ref localTransforms[i],
                                                                           in parentWorldTransform,
                                                                           flags[i].modeFlags);
                            }
                            else
                            {
                                qvvs.mul(ref worldTransforms[i], in parentWorldTransform, in localTransforms[i]);
                            }
                        }
                    }
                }
                else
                {
                    // Assume this is CopyParentWorldTransformTag
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (depth == depths[i].depth)
                        {
                            worldTransforms[i] = worldTransformLookup[parents[i].previousParent].worldTransform;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct UpdateMatricesOfDeepChildrenJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunkList;
            [ReadOnly] public ComponentTypeHandle<Depth>  depthHandle;

            [ReadOnly] public ComponentTypeHandle<WorldTransform>  worldTransformHandle;
            [ReadOnly] public BufferTypeHandle<Child>              childHandle;
            [ReadOnly] public BufferLookup<Child>                  childLookup;
            [ReadOnly] public ComponentLookup<LocalTransform>      localTransformLookup;
            [ReadOnly] public ComponentLookup<PreviousParent>      parentLookup;
            [ReadOnly] public ComponentLookup<HierarchyUpdateMode> hierarchyUpdateModeLookup;

            [NativeDisableParallelForRestriction] public ComponentLookup<ParentToWorldTransform> parentToWorldLookup;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<WorldTransform>     worldTransformLookup;

            public uint lastSystemVersion;
            public int  depthLevel;

            public unsafe void Execute(int index)
            {
                var chunk = chunkList[index];

                if (!chunk.Has(ref childHandle))
                    return;

                var worldTransformArrayPtr = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRO(ref worldTransformHandle);
                var childAccessor          = chunk.GetBufferAccessor(ref childHandle);
                var depths                 = chunk.GetNativeArray(ref depthHandle);

                bool worldTransformsDirty = chunk.DidChange(ref worldTransformHandle, lastSystemVersion);
                bool childBufferDirty     = chunk.DidChange(ref childHandle, lastSystemVersion);

                bool worldTransformValid = true;

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (depths[i].depth == depthLevel)
                    {
                        foreach (var child in childAccessor[i])
                        {
                            // We can safely pass in default for the parent argument since parentWorldTransformValid is forced true.
                            // The child doesn't need to lazy load the parentWorldTransform.
                            UpdateChildRecurse(ref worldTransformArrayPtr[i], ref worldTransformValid, default, child.child, worldTransformsDirty, childBufferDirty);
                        }
                    }
                }
            }

            void UpdateChildRecurse(ref TransformQvvs parentWorldTransform,
                                    ref bool parentWorldTransformValid,
                                    Entity parent,
                                    Entity entity,
                                    bool parentTransformDirty,
                                    bool childBufferDirty)
            {
                bool needsUpdate              = parentTransformDirty;
                bool hasMutableLocalTransform = localTransformLookup.HasComponent(entity);
                if (!parentTransformDirty && hasMutableLocalTransform)
                {
                    needsUpdate  = localTransformLookup.DidChange(entity, lastSystemVersion);
                    needsUpdate |= parentTransformDirty;
                    needsUpdate |= parentLookup.DidChange(entity, lastSystemVersion) && childBufferDirty;
                }

                TransformQvvs worldTransformToPropagate = default;

                if (needsUpdate)
                {
                    if (!parentWorldTransformValid)
                    {
                        parentWorldTransform      = worldTransformLookup[parent].worldTransform;
                        parentWorldTransformValid = true;
                    }

                    if (hasMutableLocalTransform)
                    {
                        parentToWorldLookup[entity] = new ParentToWorldTransform { parentToWorldTransform = parentWorldTransform };
                        ref var worldTransform                                                            = ref worldTransformLookup.GetRefRW(entity).ValueRW;
                        if (hierarchyUpdateModeLookup.TryGetComponent(entity, out var flags))
                        {
                            HierarchyInternalUtilities.UpdateTransform(ref worldTransform.worldTransform,
                                                                       ref localTransformLookup.GetRefRW(entity).ValueRW.localTransform,
                                                                       in parentWorldTransform,
                                                                       flags.modeFlags);
                        }
                        else
                        {
                            qvvs.mul(ref worldTransform.worldTransform, in parentWorldTransform, in localTransformLookup.GetRefRO(entity).ValueRO.localTransform);
                        }
                        worldTransformToPropagate = worldTransform.worldTransform;
                    }
                    else
                    {
                        worldTransformLookup[entity] = new WorldTransform { worldTransform = parentWorldTransform };
                        worldTransformToPropagate                                          = parentWorldTransform;
                    }
                }
                // If we had a WriteGroup, we would apply it here.

                if (childLookup.HasBuffer(entity))
                {
                    bool childBufferChanged    = childLookup.DidChange(entity, lastSystemVersion);
                    bool worldTransformIsValid = needsUpdate;
                    foreach (var child in childLookup[entity])
                        UpdateChildRecurse(ref worldTransformToPropagate, ref worldTransformIsValid, entity, child.child, needsUpdate, childBufferChanged);
                }
            }
        }
    }
}
#endif

