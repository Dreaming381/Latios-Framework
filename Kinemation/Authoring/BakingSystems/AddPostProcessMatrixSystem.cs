using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;
using Unity.Rendering;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup))]
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
    [UpdateAfter(typeof(Latios.Transforms.Authoring.Systems.TransformBakingSystem))]
#endif
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AddPostProcessMatrixSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<TransformAuthoring>().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            state.Dependency = new Job
            {
                entityHandle      = GetEntityTypeHandle(),
                taHandle          = GetComponentTypeHandle<TransformAuthoring>(true),
                ppmHandle         = GetComponentTypeHandle<PostProcessMatrix>(false),
                ecb               = ecb.AsParallelWriter(),
                lastSystemVersion = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
            state.CompleteDependency();

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                        entityHandle;
            [ReadOnly] public ComponentTypeHandle<TransformAuthoring> taHandle;
            public ComponentTypeHandle<PostProcessMatrix>             ppmHandle;
            public EntityCommandBuffer.ParallelWriter                 ecb;
            public uint                                               lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidChange(ref taHandle, lastSystemVersion))
                    return;

                var entities = chunk.GetNativeArray(entityHandle);
                var tas      = chunk.GetNativeArray(ref taHandle);
                if (chunk.Has(ref ppmHandle))
                {
                    var ppms = chunk.GetNativeArray(ref ppmHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var ta = tas[i];
                        if ((ta.RuntimeTransformUsage & RuntimeTransformComponentFlags.ManualOverride) != 0)
                        {
                            if (ChangeVersionUtility.DidChange(ta.ChangeVersion, lastSystemVersion))
                            {
                                // For now, we just assume that we triggered into manual override and should remove the component.
                                // Todo: Get the previous state from Latios Transforms
                                ecb.RemoveComponent<PostProcessMatrix>(unfilteredChunkIndex, entities[i]);
                            }
                            continue;
                        }
                        if ((ta.RuntimeTransformUsage & RuntimeTransformComponentFlags.PostTransformMatrix) != 0)
                        {
                            ppms[i] = new PostProcessMatrix { postProcessMatrix = float3x4.identity };
                        }
                        else
                        {
                            ecb.RemoveComponent<PostProcessMatrix>(unfilteredChunkIndex, entities[i]);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var ta = tas[i];
                        if ((ta.RuntimeTransformUsage & RuntimeTransformComponentFlags.ManualOverride) != 0)
                        {
                            continue;
                        }
                        if ((ta.RuntimeTransformUsage & RuntimeTransformComponentFlags.PostTransformMatrix) != 0)
                        {
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new PostProcessMatrix { postProcessMatrix = float3x4.identity });
                        }
                    }
                }
            }
        }
    }
}

