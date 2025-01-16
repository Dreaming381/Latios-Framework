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
        EntityQuery m_unityTransformsBindSkeletonRootsQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
#if !UNITY_EDITOR
            state.Enabled = false;
            return;
#endif
            m_reinitMeshesQuery                     = state.Fluent().With<MeshDeformDataBlobReference, BoundMesh>(true).Build();
            m_unityTransformsBindSkeletonRootsQuery = state.Fluent().With<Unity.Transforms.LocalTransform>(false).With<BindSkeletonRoot>(true).Build();
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

            if (!m_unityTransformsBindSkeletonRootsQuery.IsEmptyIgnoreFilter)
            {
                // Clean up Unity local transforms that we parent to skeletons in case we don't rebind but transforms get rebaked and diffed over.
                state.Dependency = new CleanupUnityLocalTransformsJob
                {
                    localTransformHandle = GetComponentTypeHandle<Unity.Transforms.LocalTransform>(false)
                }.ScheduleParallel(m_unityTransformsBindSkeletonRootsQuery, default);
            }
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

        [BurstCompile]
        struct CleanupUnityLocalTransformsJob : IJobChunk
        {
            public ComponentTypeHandle<Unity.Transforms.LocalTransform> localTransformHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var locals = chunk.GetNativeArray(ref localTransformHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    locals[i] = Unity.Transforms.LocalTransform.Identity;
                }
            }
        }
    }
}

