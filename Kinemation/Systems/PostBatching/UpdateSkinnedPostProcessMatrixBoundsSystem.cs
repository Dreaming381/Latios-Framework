using Latios.Kinemation.InternalSourceGen;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateSkinnedPostProcessMatrixBoundsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_query = state.Fluent().With<WorldRenderBounds>(false).With<PostProcessMatrix>(true).With<SkeletonDependent>(true)
                      .With<ChunkWorldRenderBounds>(false, true).With<ChunkSkinningCullingTag>(true, true).Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var exposedAabbs = latiosWorld.worldBlackboardEntity.GetCollectionComponent<ExposedSkeletonBoundsArrays>(true).allAabbs;
            state.Dependency = new Job
            {
                postProcessMatrixHandle = GetComponentTypeHandle<PostProcessMatrix>(true),
                skeletonDependentHandle = GetComponentTypeHandle<SkeletonDependent>(true),
                optimizedBoundsLookup   = GetComponentLookup<OptimizedSkeletonWorldBounds>(true),
                exposedIndexLookup      = GetComponentLookup<ExposedSkeletonCullingIndex>(true),
                exposedAabbs            = exposedAabbs.AsDeferredJobArray(),
                worldBoundsHandle       = GetComponentTypeHandle<WorldRenderBounds>(false),
                chunkWorldBoundsHandle  = GetComponentTypeHandle<ChunkWorldRenderBounds>(false)
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>        postProcessMatrixHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>        skeletonDependentHandle;
            [ReadOnly] public ComponentLookup<OptimizedSkeletonWorldBounds> optimizedBoundsLookup;
            [ReadOnly] public ComponentLookup<ExposedSkeletonCullingIndex>  exposedIndexLookup;
            [ReadOnly] public NativeArray<AABB>                             exposedAabbs;

            public ComponentTypeHandle<WorldRenderBounds>      worldBoundsHandle;
            public ComponentTypeHandle<ChunkWorldRenderBounds> chunkWorldBoundsHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // Note: Exposed bounds have no change filter, so we always assume changes.
                var chunkAabb = MinMaxAABB.Empty;

                var matrices    = chunk.GetNativeArray(ref postProcessMatrixHandle);
                var worldBounds = chunk.GetNativeArray(ref worldBoundsHandle).Reinterpret<AABB>();
                var deps        = chunk.GetNativeArray(ref skeletonDependentHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var aabb = GetSkeletonAabb(deps[i].root, out bool valid);
                    if (!valid)
                        continue;
                    float3x4 matrix3x4 = matrices[i].postProcessMatrix;
                    float4x4 matrix4x4 = new float4x4(new float4(matrix3x4.c0, 0f),
                                                      new float4(matrix3x4.c1, 0f),
                                                      new float4(matrix3x4.c2, 0f),
                                                      new float4(matrix3x4.c3, 1f));
                    var result     = AABB.Transform(matrix4x4, aabb);
                    worldBounds[i] = result;
                    chunkAabb.Encapsulate(result);
                }
                chunk.SetChunkComponentData(ref chunkWorldBoundsHandle, new ChunkWorldRenderBounds { Value = chunkAabb });
            }

            AABB GetSkeletonAabb(Entity entity, out bool valid)
            {
                valid = true;
                if (optimizedBoundsLookup.TryGetComponent(entity, out var optimizedResult))
                    return optimizedResult.bounds;
                if (exposedIndexLookup.TryGetComponent(entity, out var arrayIndex))
                    return exposedAabbs[arrayIndex.cullingIndex];
                valid = false;
                return default;
            }
        }
    }
}

