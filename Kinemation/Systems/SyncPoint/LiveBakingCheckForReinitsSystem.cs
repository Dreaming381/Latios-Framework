using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LiveBakingCheckForReinitsSystem : ISystem
    {
        EntityQuery m_reinitMeshesQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
#if !UNITY_EDITOR
            state.Enabled = false;
            return;
#endif
            m_reinitMeshesQuery = state.Fluent().With<MeshDeformDataBlobReference, BoundMesh>(true).Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb          = new EntityCommandBuffer(state.WorldUpdateAllocator);
            state.Dependency = new Job
            {
                entityHandle        = GetEntityTypeHandle(),
                meshReferenceHandle = GetComponentTypeHandle<MeshDeformDataBlobReference>(true),
                boundMeshHandle     = GetComponentTypeHandle<BoundMesh>(true),
                ecb                 = ecb.AsParallelWriter()
            }.ScheduleParallel(m_reinitMeshesQuery, state.Dependency);
            state.CompleteDependency();
            ecb.Playback(state.EntityManager);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                                 entityHandle;
            [ReadOnly] public ComponentTypeHandle<MeshDeformDataBlobReference> meshReferenceHandle;
            [ReadOnly] public ComponentTypeHandle<BoundMesh>                   boundMeshHandle;

            public EntityCommandBuffer.ParallelWriter ecb;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(entityHandle);
                var old      = chunk.GetNativeArray(ref boundMeshHandle);
                var target   = chunk.GetNativeArray(ref meshReferenceHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (old[i].meshBlob != target[i].blob)
                        ecb.AddComponent<BoundMeshNeedsReinit>(unfilteredChunkIndex, entities[i]);
                }
            }
        }
    }
}

