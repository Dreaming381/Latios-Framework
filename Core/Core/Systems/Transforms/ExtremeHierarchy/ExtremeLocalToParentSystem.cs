using Latios.Unsafe;
using Unity.Burst;
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
    public unsafe partial class ExtremeLocalToParentSystem : SubSystem
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

        protected override void OnCreate()
        {
            m_childWithParentDependencyQuery = Fluent.WithAll<LocalToWorld>(false).WithAll<LocalToParent>(true).WithAll<Parent>(true).UseWriteGroups().Build();
            m_childWithParentDependencyMask  = m_childWithParentDependencyQuery.GetEntityQueryMask();
            m_childQuery                     = Fluent.WithAll<Parent>(true).WithAll<Depth>(true).Build();
            m_metaQuery                      = Fluent.WithAll<ChunkHeader>(true).WithAll<ChunkDepthMask>().Build();

            m_headerHandle    = GetComponentTypeHandle<ChunkHeader>(true);
            m_depthMaskHandle = GetComponentTypeHandle<ChunkDepthMask>(false);
            m_depthHandle     = GetComponentTypeHandle<Depth>(true);
            m_ltpHandle       = GetComponentTypeHandle<LocalToParent>(true);
            m_parentHandle    = GetComponentTypeHandle<PreviousParent>(true);
            m_ltwHandle       = GetComponentTypeHandle<LocalToWorld>(false);
        }

        public override bool ShouldUpdateSystem()
        {
            return !m_childWithParentDependencyQuery.IsEmptyIgnoreFilter;
        }

        protected override void OnUpdate()
        {
            var unmanaged      = latiosWorld.Unmanaged;
            var chunkListArray = new ChunkListArray(ref unmanaged);
            var blockLists     = unmanaged.UpdateAllocator.AllocateNativeArray<UnsafeParallelBlockList>(kMaxDepthIterations);

            m_headerHandle.Update(this);
            m_depthMaskHandle.Update(this);
            m_depthHandle.Update(this);
            var entityHandle = GetEntityTypeHandle();
            m_ltpHandle.Update(this);
            m_parentHandle.Update(this);
            m_ltwHandle.Update(this);
            var ltwCdfe = GetComponentDataFromEntity<LocalToWorld>(false);

            Dependency = new AllocateBlockListsJob
            {
                chunkBlockLists = blockLists
            }.ScheduleParallel(kMaxDepthIterations, 1, Dependency);

            Dependency = new ClassifyChunksAndResetMasksJob
            {
                headerHandle    = m_headerHandle,
                depthMaskHandle = m_depthMaskHandle,
                chunkBlockLists = blockLists
            }.ScheduleParallel(m_metaQuery, Dependency);

            Dependency = new FlattenBlocklistsJob
            {
                chunkBlockLists = blockLists,
                chunkListArray  = chunkListArray
            }.ScheduleParallel(kMaxDepthIterations, 1, Dependency);

            for (int i = 0; i < kMaxDepthIterations; i++)
            {
                var chunkList = chunkListArray[i];
                Dependency    = new CheckIfMatricesShouldUpdateForSingleDepthLevelJob
                {
                    chunkList         = chunkList.AsDeferredJobArray(),
                    depth             = i,
                    depthHandle       = m_depthHandle,
                    depthMaskHandle   = m_depthMaskHandle,
                    entityHandle      = entityHandle,
                    lastSystemVersion = LastSystemVersion,
                    ltpHandle         = m_ltpHandle,
                    ltwCdfe           = ltwCdfe,
                    parentHandle      = m_parentHandle,
                    shouldUpdateMask  = m_childWithParentDependencyMask
                }.Schedule(chunkList, 1, Dependency);

                Dependency = new UpdateMatricesOfSingleDepthLevelJob
                {
                    chunkList       = chunkList.AsDeferredJobArray(),
                    depth           = i,
                    depthHandle     = m_depthHandle,
                    depthMaskHandle = m_depthMaskHandle,
                    ltpHandle       = m_ltpHandle,
                    ltwCdfe         = ltwCdfe,
                    ltwHandle       = m_ltwHandle,
                    parentHandle    = m_parentHandle
                }.Schedule(chunkList, 1, Dependency);
            }

            var finalChunkList = chunkListArray[kMaxDepthIterations - 1];

            Dependency = new UpdateMatricesOfDeepChildrenJob
            {
                childBfe          = GetBufferFromEntity<Child>(true),
                childHandle       = GetBufferTypeHandle<Child>(true),
                chunkList         = finalChunkList.AsDeferredJobArray(),
                depthHandle       = m_depthHandle,
                depthLevel        = kMaxDepthIterations - 1,
                lastSystemVersion = LastSystemVersion,
                ltpCdfe           = GetComponentDataFromEntity<LocalToParent>(true),
                ltwCdfe           = ltwCdfe,
                ltwHandle         = m_ltwHandle,
                ltwWriteGroupMask = m_childWithParentDependencyMask,
                parentCdfe        = GetComponentDataFromEntity<PreviousParent>(true)
            }.Schedule(finalChunkList, 1, Dependency);
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
        struct AllocateBlockListsJob : IJobFor
        {
            public NativeArray<UnsafeParallelBlockList> chunkBlockLists;

            public void Execute(int i)
            {
                chunkBlockLists[i] = new UnsafeParallelBlockList(sizeof(ArchetypeChunk), 64, Allocator.TempJob);
            }
        }

        [BurstCompile]
        struct ClassifyChunksAndResetMasksJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<ChunkHeader> headerHandle;
            public ComponentTypeHandle<ChunkDepthMask>         depthMaskHandle;

            [NativeDisableParallelForRestriction]
            public NativeArray<UnsafeParallelBlockList> chunkBlockLists;

            [NativeSetThreadIndex]
            int threadIndex;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var headers    = batchInChunk.GetNativeArray(headerHandle);
                var depthMasks = batchInChunk.GetNativeArray(depthMaskHandle);

                for (int i = 0; i < batchInChunk.Count; i++)
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

            [ReadOnly] public ComponentTypeHandle<LocalToParent>    ltpHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousParent>   parentHandle;
            [ReadOnly] public ComponentTypeHandle<Depth>            depthHandle;
            [ReadOnly] public EntityTypeHandle                      entityHandle;
            [ReadOnly] public ComponentDataFromEntity<LocalToWorld> ltwCdfe;

            public ComponentTypeHandle<ChunkDepthMask> depthMaskHandle;

            public EntityQueryMask shouldUpdateMask;
            public int             depth;
            public uint            lastSystemVersion;

            public void Execute(int index)
            {
                var chunk = chunkList[index];

                if (!shouldUpdateMask.Matches(chunk.GetNativeArray(entityHandle)[0]))
                {
                    return;
                }

                var parents = chunk.GetNativeArray(parentHandle);
                var depths  = chunk.GetNativeArray(depthHandle);

                if (chunk.DidChange(parentHandle, lastSystemVersion) || chunk.DidChange(ltpHandle, lastSystemVersion))
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
                            if (ltwCdfe.DidChange(parent, lastSystemVersion))
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
                var depthMask = chunk.GetChunkComponentData(depthMaskHandle);
                depthMask.chunkDepthMask.SetBits(depth + kMaxDepthIterations, true);
                chunk.SetChunkComponentData(depthMaskHandle, depthMask);
            }
        }

        [BurstCompile]
        struct UpdateMatricesOfSingleDepthLevelJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>                                      chunkList;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<LocalToWorld> ltwHandle;

            [ReadOnly] public ComponentTypeHandle<LocalToParent>    ltpHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousParent>   parentHandle;
            [ReadOnly] public ComponentTypeHandle<Depth>            depthHandle;
            [ReadOnly] public ComponentDataFromEntity<LocalToWorld> ltwCdfe;

            [ReadOnly] public ComponentTypeHandle<ChunkDepthMask> depthMaskHandle;

            public int depth;

            public void Execute(int index)
            {
                var chunk = chunkList[index];
                if (!chunk.GetChunkComponentData(depthMaskHandle).chunkDepthMask.IsSet(depth + kMaxDepthIterations))
                    return;

                var parents = chunk.GetNativeArray(parentHandle);
                var depths  = chunk.GetNativeArray(depthHandle);
                var ltps    = chunk.GetNativeArray(ltpHandle);
                var ltws    = chunk.GetNativeArray(ltwHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (depth == depths[i].depth)
                    {
                        ltws[i] = new LocalToWorld { Value = math.mul(ltwCdfe[parents[i].Value].Value, ltps[i].Value) };
                    }
                }
            }
        }

        [BurstCompile]
        struct UpdateMatricesOfDeepChildrenJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>             chunkList;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>       ltwHandle;
            [ReadOnly] public ComponentTypeHandle<Depth>              depthHandle;
            [ReadOnly] public BufferTypeHandle<Child>                 childHandle;
            [ReadOnly] public BufferFromEntity<Child>                 childBfe;
            [ReadOnly] public ComponentDataFromEntity<LocalToParent>  ltpCdfe;
            [ReadOnly] public ComponentDataFromEntity<PreviousParent> parentCdfe;
            [ReadOnly] public EntityQueryMask                         ltwWriteGroupMask;
            public uint                                               lastSystemVersion;
            public int                                                depthLevel;

            [NativeDisableContainerSafetyRestriction]
            public ComponentDataFromEntity<LocalToWorld> ltwCdfe;

            void ChildLocalToWorld(ref float4x4 parentLocalToWorld,
                                   Entity entity,
                                   bool updateChildrenTransform,
                                   Entity parent,
                                   ref bool parentLtwValid,
                                   bool parentsChildBufferChanged)
            {
                updateChildrenTransform = updateChildrenTransform || ltpCdfe.DidChange(entity, lastSystemVersion);
                updateChildrenTransform = updateChildrenTransform || (parentsChildBufferChanged && parentCdfe.DidChange(entity, lastSystemVersion));

                float4x4 localToWorldMatrix = default;
                bool     ltwIsValid         = false;

                bool isDependent = ltwWriteGroupMask.Matches(entity);
                if (updateChildrenTransform && isDependent)
                {
                    if (!parentLtwValid)
                    {
                        parentLocalToWorld = ltwCdfe[parent].Value;
                        parentLtwValid     = true;
                    }
                    var localToParent  = ltpCdfe[entity];
                    localToWorldMatrix = math.mul(parentLocalToWorld, localToParent.Value);
                    ltwIsValid         = true;
                    ltwCdfe[entity]    = new LocalToWorld { Value = localToWorldMatrix };
                }
                else if (!isDependent)  //This entity has a component with the WriteGroup(LocalToWorld)
                {
                    updateChildrenTransform = updateChildrenTransform || ltwCdfe.DidChange(entity, lastSystemVersion);
                }
                if (childBfe.HasComponent(entity))
                {
                    var children        = childBfe[entity];
                    var childrenChanged = updateChildrenTransform || childBfe.DidChange(entity, lastSystemVersion);
                    for (int i = 0; i < children.Length; i++)
                    {
                        ChildLocalToWorld(ref localToWorldMatrix, children[i].Value, updateChildrenTransform, entity, ref ltwIsValid, childrenChanged);
                    }
                }
            }

            public void Execute(int index)
            {
                var batchInChunk = chunkList[index];

                if (!batchInChunk.Has(childHandle))
                    return;

                bool updateChildrenTransform =
                    batchInChunk.DidChange(ltwHandle, lastSystemVersion) ||
                    batchInChunk.DidChange(childHandle, lastSystemVersion);

                var  chunkLocalToWorld = batchInChunk.GetNativeArray(ltwHandle);
                var  depths            = batchInChunk.GetNativeArray(depthHandle);
                var  chunkChildren     = batchInChunk.GetBufferAccessor(childHandle);
                bool ltwIsValid        = true;
                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    if (depths[i].depth == depthLevel)
                    {
                        var localToWorldMatrix = chunkLocalToWorld[i].Value;
                        var children           = chunkChildren[i];
                        for (int j = 0; j < children.Length; j++)
                        {
                            ChildLocalToWorld(ref localToWorldMatrix, children[j].Value, updateChildrenTransform, Entity.Null, ref ltwIsValid,
                                              batchInChunk.DidChange(childHandle, lastSystemVersion));
                        }
                    }
                }
            }
        }
    }
}

