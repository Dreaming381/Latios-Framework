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
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(ExtremeParentSystem))]
    [UpdateBefore(typeof(ExtremeLocalToParentSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ExtremeChildDepthsSystem : ISystem
    {
        EntityQuery m_query;

        // For a 32-bit depth mask, the upper 16 bits are used as a scratch list if updates are needed.
        const int kMaxDepthIterations = 16;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().WithAll<Parent>(true).WithAll<Depth>(false).WithAll<ChunkDepthMask>(false, true).Build();
        }

        [BurstCompile] public void OnDestroy(ref SystemState state) {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new UpdateDepthsJob
            {
                parentHandle      = state.GetComponentTypeHandle<PreviousParent>(true),
                parentCdfe        = state.GetComponentDataFromEntity<PreviousParent>(true),
                childHandle       = state.GetBufferTypeHandle<Child>(true),
                childBfe          = state.GetBufferFromEntity<Child>(true),
                depthCdfe         = state.GetComponentDataFromEntity<Depth>(false),
                depthHandle       = state.GetComponentTypeHandle<Depth>(false),
                lastSystemVersion = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);

            state.Dependency = new UpdateChunkDepthMasksJob
            {
                depthHandle          = state.GetComponentTypeHandle<Depth>(true),
                chunkDepthMaskHandle = state.GetComponentTypeHandle<ChunkDepthMask>(false),
                lastSystemVersion    = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        // The way this job works is for each child with a dirty parent chunk,
        // it walks up its ancestry to see if any ancestor has a dirty parent chunk.
        // If so, the child is skipped as the ancestor will update it.
        // If not, then it is responsible for walking all the way down the hierarchy
        // and updating all depths after capturing the depth from its walk upward.
        // In the case of new hierarchies, all but the first-level children will see
        // a dirty ancestry just one level up and stop walking upwards. This is as
        // efficient as it can get for chunk-granular change tracking.
        //
        // Todo: We could however capture the list of changed entities from ExtremeParentSystem
        // and using either a bit array or a hashset run this algorithm with entity granularity.
        [BurstCompile]
        struct UpdateDepthsJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<PreviousParent>                           parentHandle;
            [ReadOnly] public ComponentDataFromEntity<PreviousParent>                       parentCdfe;
            [ReadOnly] public BufferTypeHandle<Child>                                       childHandle;
            [ReadOnly] public BufferFromEntity<Child>                                       childBfe;
            [NativeDisableContainerSafetyRestriction] public ComponentDataFromEntity<Depth> depthCdfe;
            public ComponentTypeHandle<Depth>                                               depthHandle;

            public uint lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (!batchInChunk.DidChange(parentHandle, lastSystemVersion))
                    return;

                var parents = batchInChunk.GetNativeArray(parentHandle);

                BufferAccessor<Child> childAccess         = default;
                bool                  hasChildrenToUpdate = batchInChunk.Has(childHandle);
                if (hasChildrenToUpdate)
                    childAccess           = batchInChunk.GetBufferAccessor(childHandle);
                NativeArray<Depth> depths = default;

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    if (IsDepthChangeRoot(parents[i].Value, out var depth))
                    {
                        if (!depths.IsCreated)
                            depths = batchInChunk.GetNativeArray(depthHandle);

                        var startDepth = new Depth { depth = depth };
                        depths[i]                          = startDepth;
                        startDepth.depth++;

                        if (hasChildrenToUpdate)
                        {
                            foreach (var child in childAccess[i])
                            {
                                WriteDepthAndRecurse(child.Value, startDepth);
                            }
                        }
                    }
                }
            }

            bool IsDepthChangeRoot(Entity parent, out byte depth)
            {
                var current = parent;
                depth       = 0;
                while (parentCdfe.HasComponent(current))
                {
                    if (parentCdfe.DidChange(current, lastSystemVersion))
                    {
                        return false;
                    }
                    depth++;
                    current = parentCdfe[current].Value;
                }
                return true;
            }

            void WriteDepthAndRecurse(Entity child, Depth depth)
            {
                depthCdfe[child] = depth;
                depth.depth++;
                if (childBfe.HasComponent(child))
                {
                    foreach (var c in childBfe[child])
                    {
                        WriteDepthAndRecurse(c.Value, depth);
                    }
                }
            }
        }

        [BurstCompile]
        struct UpdateChunkDepthMasksJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<Depth> depthHandle;
            public ComponentTypeHandle<ChunkDepthMask>   chunkDepthMaskHandle;
            public uint                                  lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (batchInChunk.DidChange(depthHandle, lastSystemVersion) || batchInChunk.DidOrderChange(lastSystemVersion))
                {
                    BitField32 depthMask = default;
                    var        depths    = batchInChunk.GetNativeArray(depthHandle);
                    for (int i = 0; i < batchInChunk.Count; i++)
                    {
                        if (depths[i].depth < kMaxDepthIterations)
                            depthMask.SetBits(depths[i].depth, true);
                    }

                    batchInChunk.SetChunkComponentData(chunkDepthMaskHandle, new ChunkDepthMask { chunkDepthMask = depthMask });
                }
            }
        }
    }
}

