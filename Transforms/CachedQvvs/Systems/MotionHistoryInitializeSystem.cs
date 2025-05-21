#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct MotionHistoryInitializeSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<WorldTransform>(true).With<PreviousTransform>(false).IncludeDisabledEntities().Build();
            m_query.SetOrderVersionFilter();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                worldTransformHandle    = GetComponentTypeHandle<WorldTransform>(true),
                previousTransformHandle = GetComponentTypeHandle<PreviousTransform>(false),
                twoAgoTransformHandle   = GetComponentTypeHandle<TwoAgoTransform>(false),
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<WorldTransform> worldTransformHandle;
            public ComponentTypeHandle<PreviousTransform>         previousTransformHandle;
            public ComponentTypeHandle<TwoAgoTransform>           twoAgoTransformHandle;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var previousRO = chunk.GetComponentDataPtrRO(ref previousTransformHandle);
                var twoAgoRO   = chunk.GetComponentDataPtrRO(ref twoAgoTransformHandle);

                if (twoAgoRO != null)
                {
                    int  startIndex    = chunk.Count;
                    bool needsPrevious = false;
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (previousRO[i].rotation.value.Equals(float4.zero))
                        {
                            startIndex    = math.min(startIndex, i);
                            needsPrevious = true;
                            break;
                        }
                        else if (twoAgoRO[i].rotation.value.Equals(float4.zero))
                        {
                            startIndex = i;
                        }
                    }

                    if (startIndex >= chunk.Count)
                        return;

                    if (needsPrevious)
                    {
                        var current  = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                        var previous = chunk.GetComponentDataPtrRW(ref previousTransformHandle);
                        var twoAgo   = chunk.GetComponentDataPtrRW(ref twoAgoTransformHandle);

                        for (int i = startIndex; i < chunk.Count; i++)
                        {
                            if (previous[i].rotation.value.Equals(float4.zero))
                            {
                                previous[i].worldTransform = current[i].worldTransform;
                            }
                            if (twoAgo[i].rotation.value.Equals(float4.zero))
                            {
                                twoAgo[i].worldTransform = previous[i].worldTransform;
                            }
                        }
                    }
                    else
                    {
                        var twoAgo = chunk.GetComponentDataPtrRW(ref twoAgoTransformHandle);
                        for (int i = startIndex; i < chunk.Count; i++)
                        {
                            if (twoAgo[i].rotation.value.Equals(float4.zero))
                            {
                                twoAgo[i].worldTransform = previousRO[i].worldTransform;
                            }
                        }
                    }
                }
                else
                {
                    int startIndex = chunk.Count;
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (previousRO[i].rotation.value.Equals(float4.zero))
                        {
                            startIndex = math.min(startIndex, i);
                            break;
                        }
                    }

                    if (startIndex >= chunk.Count)
                        return;

                    var current  = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                    var previous = chunk.GetComponentDataPtrRW(ref previousTransformHandle);

                    for (int i = startIndex; i < chunk.Count; i++)
                    {
                        if (previous[i].rotation.value.Equals(float4.zero))
                        {
                            previous[i].worldTransform = current[i].worldTransform;
                        }
                    }
                }
            }
        }
    }
}
#endif

