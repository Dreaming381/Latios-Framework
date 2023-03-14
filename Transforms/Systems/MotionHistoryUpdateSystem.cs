using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

using static Unity.Entities.SystemAPI;

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
            m_query = state.Fluent().WithAll<WorldTransform>(true).WithAll<TickStartingTransform>(false).IncludeDisabledEntities().Build();
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
                worldTransformHandle                = GetComponentTypeHandle<WorldTransform>(true),
                tickStartingTransformHandle         = GetComponentTypeHandle<TickStartingTransform>(false),
                previousTickStartingTransformHandle = GetComponentTypeHandle<PreviousTickStartingTransform>(false),
                lastSystemVersion                   = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<WorldTransform>     worldTransformHandle;
            public ComponentTypeHandle<TickStartingTransform>         tickStartingTransformHandle;
            public ComponentTypeHandle<PreviousTickStartingTransform> previousTickStartingTransformHandle;
            public uint                                               lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool updatePrevious = chunk.Has(ref previousTickStartingTransformHandle) && DidChangeLastFrame(chunk.GetChangeVersion(ref tickStartingTransformHandle));
                bool updateCurrent  = chunk.DidChange(ref worldTransformHandle, lastSystemVersion);

                if (updatePrevious && updateCurrent)
                {
                    var currents = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRO(ref worldTransformHandle);
                    var starts   = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRW(ref tickStartingTransformHandle);
                    var prevs    = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRW(ref previousTickStartingTransformHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        prevs[i]             = starts[i];
                        starts[i]            = currents[i];
                        starts[i].worldIndex = prevs[i].worldIndex + 1;
                    }
                }
                else if (updatePrevious)
                {
                    var starts = chunk.GetRequiredComponentDataPtrRO(ref tickStartingTransformHandle);
                    var prevs  = chunk.GetRequiredComponentDataPtrRW(ref previousTickStartingTransformHandle);

                    UnsafeUtility.MemCpy(prevs, starts, UnsafeUtility.SizeOf<TransformQvvs>() * chunk.Count);
                }
                else if (updateCurrent)
                {
                    var currents = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRO(ref worldTransformHandle);
                    var starts   = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRW(ref tickStartingTransformHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        int newVersion       = starts[i].worldIndex + 1;
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

