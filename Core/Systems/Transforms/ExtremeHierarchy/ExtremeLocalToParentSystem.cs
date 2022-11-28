using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// This system uses PreviousParent in all cases because it is guaranteed to be updated
// (ParentSystem just ran) and it is updated when the entity is enabled so change filters
// work correctly.
namespace Latios.Systems
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TRSToLocalToParentSystem))]
    [UpdateAfter(typeof(TRSToLocalToWorldSystem))]
    [UpdateBefore(typeof(LocalToParentSystem))]
    [BurstCompile]
    public unsafe partial struct ExtremeLocalToParentSystem : ISystem, ISystemShouldUpdate
    {
        EntityQuery     m_childWithParentDependencyQuery;
        EntityQueryMask m_childWithParentDependencyMask;

        EntityQuery m_childQuery;
        EntityQuery m_metaQuery;

        // For a 32-bit depth mask, the upper 16 bits are used as a scratch list if updates are needed.
        const int kMaxDepthIterations = 16;

        ComponentTypeHandle<ChunkHeader>    m_headerHandle;
        ComponentTypeHandle<ChunkDepthMask> m_depthMaskHandle;
        ComponentTypeHandle<Depth>          m_depthHandle;
        ComponentTypeHandle<LocalToParent>  m_ltpHandle;
        ComponentTypeHandle<PreviousParent> m_parentHandle;
        ComponentTypeHandle<LocalToWorld>   m_ltwHandle;
        BufferTypeHandle<Child>             m_childHandle;
        EntityTypeHandle                    m_entityHandle;

        public void OnCreate(ref SystemState state)
        {
            m_childWithParentDependencyQuery = state.Fluent().WithAll<LocalToWorld>(false).WithAll<LocalToParent>(true).WithAll<Parent>(true).UseWriteGroups().Build();
            m_childWithParentDependencyMask  = m_childWithParentDependencyQuery.GetEntityQueryMask();
            m_childQuery                     = state.Fluent().WithAll<Parent>(true).WithAll<Depth>(true).Build();
            m_metaQuery                      = state.Fluent().WithAll<ChunkHeader>(true).WithAll<ChunkDepthMask>().Build();

            m_headerHandle    = state.GetComponentTypeHandle<ChunkHeader>(true);
            m_depthMaskHandle = state.GetComponentTypeHandle<ChunkDepthMask>(false);
            m_depthHandle     = state.GetComponentTypeHandle<Depth>(true);
            m_ltpHandle       = state.GetComponentTypeHandle<LocalToParent>(true);
            m_parentHandle    = state.GetComponentTypeHandle<PreviousParent>(true);
            m_ltwHandle       = state.GetComponentTypeHandle<LocalToWorld>(false);
            m_childHandle     = state.GetBufferTypeHandle<Child>(true);
            m_entityHandle    = state.GetEntityTypeHandle();
        }

        public bool ShouldUpdateSystem(ref SystemState state)
        {
            return !m_childWithParentDependencyQuery.IsEmptyIgnoreFilter;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var unmanaged      = state.WorldUnmanaged;
            var chunkListArray = new ChunkListArray(ref unmanaged);
            var blockLists     = unmanaged.UpdateAllocator.AllocateNativeArray<UnsafeParallelBlockList>(kMaxDepthIterations);

            m_headerHandle.Update(ref state);
            m_depthMaskHandle.Update(ref state);
            m_depthHandle.Update(ref state);
            m_entityHandle.Update(ref state);
            m_ltpHandle.Update(ref state);
            m_parentHandle.Update(ref state);
            m_ltwHandle.Update(ref state);
            m_childHandle.Update(ref state);
            m_entityHandle.Update(ref state);
            var ltwLookup = SystemAPI.GetComponentLookup<LocalToWorld>(false);

            state.Dependency = new AllocateBlockListsJob
            {
                chunkBlockLists = blockLists
            }.Schedule(kMaxDepthIterations, 1, state.Dependency);

            state.Dependency = new ClassifyChunksAndResetMasksJob
            {
                headerHandle    = m_headerHandle,
                depthMaskHandle = m_depthMaskHandle,
                chunkBlockLists = blockLists
            }.ScheduleParallel(m_metaQuery, state.Dependency);

            state.Dependency = new FlattenBlocklistsJob
            {
                chunkBlockLists = blockLists,
                chunkListArray  = chunkListArray
            }.ScheduleParallel(kMaxDepthIterations, 1, state.Dependency);

            for (int i = 0; i < kMaxDepthIterations; i++)
            {
                var chunkList    = chunkListArray[i];
                state.Dependency = new CheckIfMatricesShouldUpdateForSingleDepthLevelJob
                {
                    chunkList         = chunkList.AsDeferredJobArray(),
                    depth             = i,
                    depthHandle       = m_depthHandle,
                    depthMaskHandle   = m_depthMaskHandle,
                    entityHandle      = m_entityHandle,
                    lastSystemVersion = state.LastSystemVersion,
                    ltpHandle         = m_ltpHandle,
                    ltwLookup         = ltwLookup,
                    parentHandle      = m_parentHandle,
                    shouldUpdateMask  = m_childWithParentDependencyMask
                }.Schedule(chunkList, 1, state.Dependency);

                state.Dependency = new UpdateMatricesOfSingleDepthLevelJob
                {
                    chunkList       = chunkList.AsDeferredJobArray(),
                    depth           = i,
                    depthHandle     = m_depthHandle,
                    depthMaskHandle = m_depthMaskHandle,
                    ltpHandle       = m_ltpHandle,
                    ltwLookup       = ltwLookup,
                    ltwHandle       = m_ltwHandle,
                    parentHandle    = m_parentHandle
                }.Schedule(chunkList, 1, state.Dependency);
            }

            var finalChunkList = chunkListArray[kMaxDepthIterations - 1];

            state.Dependency = new UpdateMatricesOfDeepChildrenJob
            {
                childLookup       = SystemAPI.GetBufferLookup<Child>(true),
                childHandle       = m_childHandle,
                chunkList         = finalChunkList.AsDeferredJobArray(),
                depthHandle       = m_depthHandle,
                depthLevel        = kMaxDepthIterations - 1,
                lastSystemVersion = state.LastSystemVersion,
                ltpLookup         = SystemAPI.GetComponentLookup<LocalToParent>(true),
                ltwLookup         = ltwLookup,
                ltwHandle         = m_ltwHandle,
                ltwWriteGroupMask = m_childWithParentDependencyMask,
                parentLookup      = SystemAPI.GetComponentLookup<PreviousParent>(true)
            }.Schedule(finalChunkList, 1, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state) {
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

            public ChunkListArray(ref WorldUnmanaged world)
            {
                var allocator = world.UpdateAllocator.ToAllocator;
                l0            = new NativeList<ArchetypeChunk>(allocator);
                l1            = new NativeList<ArchetypeChunk>(allocator);
                l2            = new NativeList<ArchetypeChunk>(allocator);
                l3            = new NativeList<ArchetypeChunk>(allocator);
                l4            = new NativeList<ArchetypeChunk>(allocator);
                l5            = new NativeList<ArchetypeChunk>(allocator);
                l6            = new NativeList<ArchetypeChunk>(allocator);
                l7            = new NativeList<ArchetypeChunk>(allocator);
                l8            = new NativeList<ArchetypeChunk>(allocator);
                l9            = new NativeList<ArchetypeChunk>(allocator);
                l10           = new NativeList<ArchetypeChunk>(allocator);
                l11           = new NativeList<ArchetypeChunk>(allocator);
                l12           = new NativeList<ArchetypeChunk>(allocator);
                l13           = new NativeList<ArchetypeChunk>(allocator);
                l14           = new NativeList<ArchetypeChunk>(allocator);
                l15           = new NativeList<ArchetypeChunk>(allocator);
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

            public void Execute(int i)
            {
                chunkBlockLists[i] = new UnsafeParallelBlockList(sizeof(ArchetypeChunk), 64, Allocator.TempJob);
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
                chunkBlockLists[index].Dispose();
            }
        }

        [BurstCompile]
        struct CheckIfMatricesShouldUpdateForSingleDepthLevelJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunkList;

            [ReadOnly] public ComponentTypeHandle<LocalToParent>  ltpHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousParent> parentHandle;
            [ReadOnly] public ComponentTypeHandle<Depth>          depthHandle;
            [ReadOnly] public EntityTypeHandle                    entityHandle;
            [ReadOnly] public ComponentLookup<LocalToWorld>       ltwLookup;

            public ComponentTypeHandle<ChunkDepthMask> depthMaskHandle;

            public EntityQueryMask shouldUpdateMask;
            public int             depth;
            public uint            lastSystemVersion;

            public void Execute(int index)
            {
                var chunk = chunkList[index];

                if (!shouldUpdateMask.MatchesIgnoreFilter(chunk.GetNativeArray(entityHandle)[0]))
                {
                    return;
                }

                var parents = chunk.GetNativeArray(ref parentHandle);
                var depths  = chunk.GetNativeArray(ref depthHandle);

                if (chunk.DidChange(ref parentHandle, lastSystemVersion) || chunk.DidChange(ref ltpHandle, lastSystemVersion))
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
                            var parent = parents[i].Value;
                            if (ltwLookup.DidChange(parent, lastSystemVersion))
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
                depthMask.chunkDepthMask.SetBits(depth + kMaxDepthIterations, true);
                chunk.SetChunkComponentData(ref depthMaskHandle, depthMask);
            }
        }

        [BurstCompile]
        struct UpdateMatricesOfSingleDepthLevelJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>                                      chunkList;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<LocalToWorld> ltwHandle;

            [ReadOnly] public ComponentTypeHandle<LocalToParent>  ltpHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousParent> parentHandle;
            [ReadOnly] public ComponentTypeHandle<Depth>          depthHandle;
            [ReadOnly] public ComponentLookup<LocalToWorld>       ltwLookup;

            [ReadOnly] public ComponentTypeHandle<ChunkDepthMask> depthMaskHandle;

            public int depth;

            public void Execute(int index)
            {
                var chunk = chunkList[index];
                if (!chunk.GetChunkComponentData(ref depthMaskHandle).chunkDepthMask.IsSet(depth + kMaxDepthIterations))
                    return;

                var parents = chunk.GetNativeArray(ref parentHandle);
                var depths  = chunk.GetNativeArray(ref depthHandle);
                var ltps    = chunk.GetNativeArray(ref ltpHandle);
                var ltws    = chunk.GetNativeArray(ref ltwHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (depth == depths[i].depth)
                    {
                        ltws[i] = new LocalToWorld { Value = math.mul(ltwLookup[parents[i].Value].Value, ltps[i].Value) };
                    }
                }
            }
        }

        [BurstCompile]
        struct UpdateMatricesOfDeepChildrenJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>       chunkList;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> ltwHandle;
            [ReadOnly] public ComponentTypeHandle<Depth>        depthHandle;
            [ReadOnly] public BufferTypeHandle<Child>           childHandle;
            [ReadOnly] public BufferLookup<Child>               childLookup;
            [ReadOnly] public ComponentLookup<LocalToParent>    ltpLookup;
            [ReadOnly] public ComponentLookup<PreviousParent>   parentLookup;
            [ReadOnly] public EntityQueryMask                   ltwWriteGroupMask;
            public uint                                         lastSystemVersion;
            public int                                          depthLevel;

            [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<LocalToWorld> ltwLookup;

            void ChildLocalToWorld(ref float4x4 parentLocalToWorld,
                                   Entity entity,
                                   bool updateChildrenTransform,
                                   Entity parent,
                                   ref bool parentLtwValid,
                                   bool parentsChildBufferChanged)
            {
                updateChildrenTransform = updateChildrenTransform || ltpLookup.DidChange(entity, lastSystemVersion);
                updateChildrenTransform = updateChildrenTransform || (parentsChildBufferChanged && parentLookup.DidChange(entity, lastSystemVersion));

                float4x4 localToWorldMatrix = default;
                bool     ltwIsValid         = false;

                bool isDependent = ltwWriteGroupMask.MatchesIgnoreFilter(entity);
                if (updateChildrenTransform && isDependent)
                {
                    if (!parentLtwValid)
                    {
                        parentLocalToWorld = ltwLookup[parent].Value;
                        parentLtwValid     = true;
                    }
                    var localToParent  = ltpLookup[entity];
                    localToWorldMatrix = math.mul(parentLocalToWorld, localToParent.Value);
                    ltwIsValid         = true;
                    ltwLookup[entity]  = new LocalToWorld { Value = localToWorldMatrix };
                }
                else if (!isDependent)  //This entity has a component with the WriteGroup(LocalToWorld)
                {
                    updateChildrenTransform = updateChildrenTransform || ltwLookup.DidChange(entity, lastSystemVersion);
                }
                if (childLookup.HasBuffer(entity))
                {
                    var children        = childLookup[entity];
                    var childrenChanged = updateChildrenTransform || childLookup.DidChange(entity, lastSystemVersion);
                    for (int i = 0; i < children.Length; i++)
                    {
                        ChildLocalToWorld(ref localToWorldMatrix, children[i].Value, updateChildrenTransform, entity, ref ltwIsValid, childrenChanged);
                    }
                }
            }

            public void Execute(int index)
            {
                var chunk = chunkList[index];

                if (!chunk.Has(ref childHandle))
                    return;

                bool updateChildrenTransform =
                    chunk.DidChange(ref ltwHandle, lastSystemVersion) ||
                    chunk.DidChange(ref childHandle, lastSystemVersion);

                var  chunkLocalToWorld = chunk.GetNativeArray(ref ltwHandle);
                var  depths            = chunk.GetNativeArray(ref depthHandle);
                var  chunkChildren     = chunk.GetBufferAccessor(ref childHandle);
                bool ltwIsValid        = true;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (depths[i].depth == depthLevel)
                    {
                        var localToWorldMatrix = chunkLocalToWorld[i].Value;
                        var children           = chunkChildren[i];
                        for (int j = 0; j < children.Length; j++)
                        {
                            ChildLocalToWorld(ref localToWorldMatrix, children[j].Value, updateChildrenTransform, Entity.Null, ref ltwIsValid,
                                              chunk.DidChange(ref childHandle, lastSystemVersion));
                        }
                    }
                }
            }
        }
    }
}

