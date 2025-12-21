#if !LATIOS_TRANSFORMS_UNITY
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
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                worldTransformHandle    = GetComponentTypeHandle<WorldTransform>(true),
                previousTransformHandle = GetComponentTypeHandle<PreviousTransform>(false),
                twoAgoTransformHandle   = GetComponentTypeHandle<TwoAgoTransform>(false),
                lastSystemVersion       = state.GetLiveBakeSafeLastSystemVersion()
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
                bool updateTwoAgo = chunk.Has(ref twoAgoTransformHandle) && DidChangeLastFrame(chunk.GetChangeVersion(ref previousTransformHandle));

                if (chunk.DidChange(ref worldTransformHandle, lastSystemVersion))
                {
                    var currents = chunk.GetNativeArray(ref worldTransformHandle).Reinterpret<TransformQvvs>();
                    var prevs    = chunk.GetNativeArray(ref previousTransformHandle).Reinterpret<TransformQvvs>();

                    if (updateTwoAgo)
                    {
                        var twoAgos = chunk.GetNativeArray(ref twoAgoTransformHandle).Reinterpret<TransformQvvs>();
                        twoAgos.CopyFrom(prevs);
                    }

                    prevs.CopyFrom(currents);
                }
                else if (updateTwoAgo)
                {
                    var prevs   = chunk.GetRequiredComponentDataPtrRO(ref previousTransformHandle);
                    var twoAgos = chunk.GetRequiredComponentDataPtrRW(ref twoAgoTransformHandle);

                    UnsafeUtility.MemCpy(twoAgos, prevs, UnsafeUtility.SizeOf<TransformQvvs>() * chunk.Count);
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

