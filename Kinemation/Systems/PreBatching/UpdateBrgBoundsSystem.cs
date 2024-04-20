using Latios.Psyshock;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

// Todo: Currently we don't pad the per-thread AABBs because we only touch them once per chunk.
// But maybe we should?
namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateBrgBoundsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_metaSkeletonsQuery;
        EntityQuery m_metaMeshesQuery;
        EntityQuery m_postProcessMatricesQuery;
        EntityQuery m_exposedSkeletonBoundsOffsetsQuery;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            latiosWorld.worldBlackboardEntity.AddComponent<BrgAabb>();

            m_metaSkeletonsQuery =
                state.Fluent().With<ChunkHeader>(true).WithAnyEnabled<ChunkOptimizedSkeletonWorldBounds>(true).WithAnyEnabled<ChunkBoneWorldBounds>(true).Build();

            m_metaMeshesQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkWorldRenderBounds>(true).Without<ChunkSkinningCullingTag>().Build();

            m_postProcessMatricesQuery = state.Fluent().With<PostProcessMatrix>(true).With<ChunkSkinningCullingTag>(true, true).Build();

            m_exposedSkeletonBoundsOffsetsQuery = state.Fluent().With<ExposedSkeletonCullingIndex>(true).With<SkeletonBoundsOffsetFromMeshes>(true).Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var skeletonAabbs     = CollectionHelper.CreateNativeArray<Aabb>(JobsUtility.MaxJobThreadCount, state.WorldUpdateAllocator);
            var meshAabbs         = CollectionHelper.CreateNativeArray<Aabb>(JobsUtility.MaxJobThreadCount, state.WorldUpdateAllocator);
            var postProcessAabbs  = CollectionHelper.CreateNativeArray<Aabb>(JobsUtility.MaxJobThreadCount, state.WorldUpdateAllocator);
            var exposedOffsets    = CollectionHelper.CreateNativeArray<float>(JobsUtility.MaxJobThreadCount, state.WorldUpdateAllocator);
            var skeletonsCombined = new NativeReference<Aabb>(state.WorldUpdateAllocator);

            var initJh = new InitializeAabbsJob
            {
                skeletonAabbs    = skeletonAabbs,
                meshAabbs        = meshAabbs,
                postProcessAabbs = postProcessAabbs,
                exposedOffsets   = exposedOffsets,
            }.Schedule(JobsUtility.MaxJobThreadCount, default);

            initJh = JobHandle.CombineDependencies(initJh, state.Dependency);

            var skeletonsJh = new SkeletonsJob
            {
                optimizedBoundsHandle  = GetComponentTypeHandle<ChunkOptimizedSkeletonWorldBounds>(true),
                exposedBoundsHandle    = GetComponentTypeHandle<ChunkBoneWorldBounds>(true),
                skeletonPerThreadAabbs = skeletonAabbs,
            }.ScheduleParallel(m_metaSkeletonsQuery, initJh);

            skeletonsJh = new ExposedSkeletonsBoundsOffsetJob
            {
                offsetsHandle  = GetComponentTypeHandle<SkeletonBoundsOffsetFromMeshes>(true),
                exposedOffsets = exposedOffsets
            }.ScheduleParallel(m_exposedSkeletonBoundsOffsetsQuery, skeletonsJh);

            skeletonsJh = new CombineSkeletonsJob
            {
                aabbs             = skeletonAabbs,
                skeletonsCombined = skeletonsCombined,
                exposedOffsets    = exposedOffsets
            }.Schedule(skeletonsJh);

            var postProcessJh = new PostProcessMatricesJob
            {
                meshPerThreadAabbs = postProcessAabbs,
                skeletonAabb       = skeletonsCombined,
                matricesHandle     = GetComponentTypeHandle<PostProcessMatrix>(true)
            }.ScheduleParallel(m_postProcessMatricesQuery, skeletonsJh);

            var meshesJh = new MeshesJob
            {
                meshBoundsHandle   = GetComponentTypeHandle<ChunkWorldRenderBounds>(true),
                meshPerThreadAabbs = meshAabbs
            }.ScheduleParallel(m_metaMeshesQuery, initJh);

            state.Dependency = new CombineAllAabbs
            {
                brgAabbLookup         = GetComponentLookup<BrgAabb>(false),
                meshAabbs             = meshAabbs,
                postProcessAabbs      = postProcessAabbs,
                skeletonAabb          = skeletonsCombined,
                worldBlackboardEntity = latiosWorld.worldBlackboardEntity
            }.Schedule(JobHandle.CombineDependencies(postProcessJh, meshesJh));
        }

        [BurstCompile]
        struct InitializeAabbsJob : IJobFor
        {
            public NativeArray<Aabb>  skeletonAabbs;
            public NativeArray<Aabb>  meshAabbs;
            public NativeArray<Aabb>  postProcessAabbs;
            public NativeArray<float> exposedOffsets;

            public void Execute(int i)
            {
                Aabb aabb           = new Aabb(float.MaxValue, float.MinValue);
                skeletonAabbs[i]    = aabb;
                meshAabbs[i]        = aabb;
                postProcessAabbs[i] = aabb;
                exposedOffsets[i]   = 0f;
            }
        }

        [BurstCompile]
        struct SkeletonsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkOptimizedSkeletonWorldBounds> optimizedBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkBoneWorldBounds>              exposedBoundsHandle;

            [NativeDisableParallelForRestriction] public NativeArray<Aabb> skeletonPerThreadAabbs;

            [NativeSetThreadIndex]
            int m_NativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.Has(ref optimizedBoundsHandle))
                {
                    var aabb        = skeletonPerThreadAabbs[m_NativeThreadIndex];
                    var boundsArray = chunk.GetNativeArray(ref optimizedBoundsHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var bounds = boundsArray[i];
                        aabb       = Physics.CombineAabb(new Aabb(bounds.chunkBounds.Min, bounds.chunkBounds.Max), aabb);
                    }
                    skeletonPerThreadAabbs[m_NativeThreadIndex] = aabb;
                }
                else
                {
                    var aabb        = skeletonPerThreadAabbs[m_NativeThreadIndex];
                    var boundsArray = chunk.GetNativeArray(ref exposedBoundsHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var bounds = boundsArray[i];
                        aabb       = Physics.CombineAabb(new Aabb(bounds.chunkBounds.Min, bounds.chunkBounds.Max), aabb);
                    }
                    skeletonPerThreadAabbs[m_NativeThreadIndex] = aabb;
                }
            }
        }

        [BurstCompile]
        struct ExposedSkeletonsBoundsOffsetJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<SkeletonBoundsOffsetFromMeshes> offsetsHandle;

            [NativeDisableParallelForRestriction] public NativeArray<float> exposedOffsets;

            [NativeSetThreadIndex]
            int m_NativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunkOffsets = chunk.GetNativeArray(ref offsetsHandle);

                for (int i = 0; i < chunk.Count; i++)
                    exposedOffsets[m_NativeThreadIndex] = math.max(chunkOffsets[i].radialBoundsInWorldSpace, exposedOffsets[m_NativeThreadIndex]);
            }
        }

        [BurstCompile]
        struct CombineSkeletonsJob : IJob
        {
            [ReadOnly] public NativeArray<Aabb>  aabbs;
            [ReadOnly] public NativeArray<float> exposedOffsets;
            public NativeReference<Aabb>         skeletonsCombined;

            public void Execute()
            {
                Aabb aabb = new Aabb(float.MaxValue, float.MinValue);
                foreach (var skeletonAabb in aabbs)
                    aabb               = Physics.CombineAabb(aabb, skeletonAabb);
                float maxExposedOffset = 0f;
                foreach (var offset in exposedOffsets)
                    maxExposedOffset     = math.max(maxExposedOffset, offset);
                aabb.min                -= maxExposedOffset;
                aabb.max                += maxExposedOffset;
                skeletonsCombined.Value  = aabb;
            }
        }

        [BurstCompile]
        struct MeshesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkWorldRenderBounds> meshBoundsHandle;

            [NativeDisableParallelForRestriction] public NativeArray<Aabb> meshPerThreadAabbs;

            [NativeSetThreadIndex]
            int m_NativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var aabb        = meshPerThreadAabbs[m_NativeThreadIndex];
                var boundsArray = chunk.GetNativeArray(ref meshBoundsHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var bounds = boundsArray[i];
                    aabb       = Physics.CombineAabb(new Aabb(bounds.Value.Min, bounds.Value.Max), aabb);
                }
                meshPerThreadAabbs[m_NativeThreadIndex] = aabb;
            }
        }

        [BurstCompile]
        struct PostProcessMatricesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix> matricesHandle;
            [ReadOnly] public NativeReference<Aabb>                  skeletonAabb;

            [NativeDisableParallelForRestriction] public NativeArray<Aabb> meshPerThreadAabbs;

            [NativeSetThreadIndex]
            int m_NativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Physics.GetCenterExtents(skeletonAabb.Value, out var center, out var extents);
                var aabb     = meshPerThreadAabbs[m_NativeThreadIndex];
                var matrices = chunk.GetNativeArray(ref matricesHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    float3x4 matrix3x4 = matrices[i].postProcessMatrix;
                    float4x4 matrix4x4 = new float4x4(new float4(matrix3x4.c0, 0f),
                                                      new float4(matrix3x4.c1, 0f),
                                                      new float4(matrix3x4.c2, 0f),
                                                      new float4(matrix3x4.c3, 1f));

                    aabb = Physics.CombineAabb(Physics.TransformAabb(matrix4x4, center, extents), aabb);
                }
                meshPerThreadAabbs[m_NativeThreadIndex] = aabb;
            }
        }

        [BurstCompile]
        struct CombineAllAabbs : IJob
        {
            [ReadOnly] public NativeReference<Aabb> skeletonAabb;
            [ReadOnly] public NativeArray<Aabb>     meshAabbs;
            [ReadOnly] public NativeArray<Aabb>     postProcessAabbs;

            public ComponentLookup<BrgAabb> brgAabbLookup;
            public Entity                   worldBlackboardEntity;

            public void Execute()
            {
                var aabb = skeletonAabb.Value;
                foreach (var newAabb in meshAabbs)
                    aabb = Physics.CombineAabb(aabb, newAabb);
                foreach (var newAabb in postProcessAabbs)
                    aabb                                                   = Physics.CombineAabb(aabb, newAabb);
                brgAabbLookup.GetRefRW(worldBlackboardEntity).ValueRW.aabb = aabb;
            }
        }
    }
}

