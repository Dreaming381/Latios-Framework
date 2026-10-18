#if !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct MotionHistoryInitializeSystem : ISystem, ILatiosApi
    {
        EntityQuery m_query;
        EntityQuery m_tickedQuery;

        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);
            m_query = state.Fluent().With<WorldTransform>(true).With<PreviousTransform>(false).Build();
            m_query.SetOrderVersionFilter();
            m_tickedQuery = state.Fluent().With<TickedWorldTransform>(true).With<TickedPreviousTransform>(false).Build();
            m_tickedQuery.SetOrderVersionFilter();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api = this.GetApi(ref state);
            if (!m_query.IsEmptyIgnoreFilter)
                state.Dependency = new Job().Inject(api).ScheduleParallel(m_query, state.Dependency);
            if (!m_tickedQuery.IsEmptyIgnoreFilter)
                state.Dependency = new TickedJob().Inject(api).ScheduleParallel(m_tickedQuery, state.Dependency);
        }

        [BurstCompile]
        partial struct Job : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<WorldTransform> worldTransformHandle;
            [Inject] ComponentTypeHandle<PreviousTransform>        previousTransformHandle;
            [Inject] ComponentTypeHandle<TwoAgoTransform>          twoAgoTransformHandle;

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
                            startIndex = math.min(startIndex, i);
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

        [BurstCompile]
        partial struct TickedJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<TickedWorldTransform> worldTransformHandle;
            [Inject] ComponentTypeHandle<TickedPreviousTransform>        previousTransformHandle;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var previous   = chunk.GetComponentDataPtrRO(ref previousTransformHandle);
                int startIndex = chunk.Count;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (previous[i].rotation.value.Equals(float4.zero))
                    {
                        startIndex = i;
                        break;
                    }
                }
                if (startIndex >= chunk.Count)
                    return;
                chunk.GetComponentDataPtrRW(ref previousTransformHandle);
                var current = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                for (int i = startIndex; i < chunk.Count; i++)
                {
                    if (previous[i].rotation.value.Equals(float4.zero))
                        previous[i].worldTransform = current[i].worldTransform;
                }
            }
        }
    }
}
#endif

