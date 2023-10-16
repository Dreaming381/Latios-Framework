#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

// Todo: Need to skip for multiple updates per frame.
namespace Latios.Transforms.Systems
{
    [DisableAutoCreation]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct MotionHistoryUpdateSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<WorldTransform>(true).With<PreviousTransform>(false).IncludeDisabledEntities().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                worldTransformHandle    = GetComponentTypeHandle<WorldTransform>(true),
                previousTransformHandle = GetComponentTypeHandle<PreviousTransform>(false),
                twoAgoTransformHandle   = GetComponentTypeHandle<TwoAgoTransform>(false),
                lastSystemVersion       = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<WorldTransform> worldTransformHandle;
            public ComponentTypeHandle<PreviousTransform>         previousTransformHandle;
            public ComponentTypeHandle<TwoAgoTransform>           twoAgoTransformHandle;
            public uint                                           lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool updatePrevious = chunk.Has(ref twoAgoTransformHandle) && DidChangeLastFrame(chunk.GetChangeVersion(ref previousTransformHandle));
                bool updateCurrent  = chunk.DidChange(ref worldTransformHandle, lastSystemVersion);

                if (updatePrevious && updateCurrent)
                {
                    var currents = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRO(ref worldTransformHandle);
                    var starts   = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRW(ref previousTransformHandle);
                    var prevs    = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRW(ref twoAgoTransformHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        // Need to compensate for uninitialized baked values
                        if (starts[i].worldIndex == 0)
                        {
                            prevs[i]            = currents[i];
                            prevs[i].worldIndex = 0;
                        }
                        else
                            prevs[i]         = starts[i];
                        starts[i]            = currents[i];
                        starts[i].worldIndex = prevs[i].worldIndex + math.select(1, 2, prevs[i].worldIndex + 1 == 0);
                    }
                }
                else if (updatePrevious)
                {
                    var starts = chunk.GetRequiredComponentDataPtrRO(ref previousTransformHandle);
                    var prevs  = chunk.GetRequiredComponentDataPtrRW(ref twoAgoTransformHandle);

                    UnsafeUtility.MemCpy(prevs, starts, UnsafeUtility.SizeOf<TransformQvvs>() * chunk.Count);
                }
                else if (updateCurrent)
                {
                    var currents = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRO(ref worldTransformHandle);
                    var starts   = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRW(ref previousTransformHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        int newVersion = starts[i].worldIndex + 1;
                        if (newVersion == 0)
                            newVersion++;
                        starts[i]            = currents[i];
                        starts[i].worldIndex = newVersion;
                    }
                }
            }

            bool DidChangeLastFrame(uint storedVersion)
            {
                // When a system runs for the first time, everything is considered changed.
                if (lastSystemVersion == 0)
                    return true;
                // Supporting wrap around for version numbers, change must be bigger than last system run.
                // (Never detect change of something the system itself changed)
                return (int)(storedVersion - lastSystemVersion) >= 0;
            }
        }
    }
}
#endif

