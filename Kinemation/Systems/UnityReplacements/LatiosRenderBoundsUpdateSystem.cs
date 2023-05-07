#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios;
using Latios.Psyshock;
using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation
{
    /// <summary>
    /// A system that updates the WorldRenderBounds for entities that have both a WorldTransform and RenderBounds component.
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [UpdateBefore(typeof(UpdateSceneBoundingVolumeFromRendererBounds))]  // UpdateSceneBoundingVolumeFromRendererBounds has an UpdateAfter dependency
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.EntitySceneOptimizations | WorldSystemFilterFlags.Editor)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LatiosRenderBoundsUpdateSystem : ISystem
    {
        EntityQuery m_WorldRenderBounds;

        public void OnCreate(ref SystemState state)
        {
            m_WorldRenderBounds = state.GetEntityQuery
                                  (
                new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponent<ChunkWorldRenderBounds>(), ComponentType.ReadWrite<WorldRenderBounds>(),
                              ComponentType.ReadOnly<RenderBounds>(), ComponentType.ReadOnly<WorldTransform>() },
                None = new [] { ComponentType.ChunkComponentExclude<ChunkSkinningCullingTag>() }
            }
                                  );
            m_WorldRenderBounds.SetChangedVersionFilter(new[] { ComponentType.ReadOnly<RenderBounds>(), ComponentType.ReadOnly<WorldTransform>() });
            m_WorldRenderBounds.AddOrderVersionFilter();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var boundsJob = new BoundsJob
            {
                RendererBounds         = GetComponentTypeHandle<RenderBounds>(true),
                WorldTransform         = GetComponentTypeHandle<WorldTransform>(true),
                PostProcessMatrix      = GetComponentTypeHandle<PostProcessMatrix>(true),
                WorldRenderBounds      = GetComponentTypeHandle<WorldRenderBounds>(),
                ChunkWorldRenderBounds = GetComponentTypeHandle<ChunkWorldRenderBounds>(),
            };
            state.Dependency = boundsJob.ScheduleParallelByRef(m_WorldRenderBounds, state.Dependency);
        }

        [BurstCompile]
        struct BoundsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<RenderBounds>      RendererBounds;
            [ReadOnly] public ComponentTypeHandle<WorldTransform>    WorldTransform;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix> PostProcessMatrix;
            public ComponentTypeHandle<WorldRenderBounds>            WorldRenderBounds;
            public ComponentTypeHandle<ChunkWorldRenderBounds>       ChunkWorldRenderBounds;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Unity.Assertions.Assert.IsFalse(useEnabledMask);

                var worldBounds     = chunk.GetNativeArray(ref WorldRenderBounds);
                var localBounds     = chunk.GetNativeArray(ref RendererBounds);
                var worldTransforms = chunk.GetNativeArray(ref WorldTransform);

                var chunkAabb = new Aabb(float.MaxValue, float.MinValue);

                if (chunk.Has(ref PostProcessMatrix))
                {
                    var matrices = chunk.GetNativeArray(ref PostProcessMatrix);
                    for (int i = 0; i != localBounds.Length; i++)
                    {
                        var worldAabb = Physics.TransformAabb(worldTransforms[i].worldTransform, new Aabb(localBounds[i].Value.Min, localBounds[i].Value.Max));
                        chunkAabb     = Physics.CombineAabb(chunkAabb, worldAabb);
                        Physics.GetCenterExtents(worldAabb, out var center, out var extents);
                        var matrix = new float4x4(new float4(matrices[i].postProcessMatrix.c0, 0f),
                                                  new float4(matrices[i].postProcessMatrix.c1, 0f),
                                                  new float4(matrices[i].postProcessMatrix.c2, 0f),
                                                  new float4(matrices[i].postProcessMatrix.c3, 1f));
                        worldBounds[i] = new WorldRenderBounds { Value = AABB.Transform(matrix, new AABB { Center = center, Extents = extents } )};
                    }
                }
                else
                {
                    for (int i = 0; i != localBounds.Length; i++)
                    {
                        var worldAabb = Physics.TransformAabb(worldTransforms[i].worldTransform, new Aabb(localBounds[i].Value.Min, localBounds[i].Value.Max));
                        chunkAabb     = Physics.CombineAabb(chunkAabb, worldAabb);
                        Physics.GetCenterExtents(worldAabb, out var center, out var extents);
                        worldBounds[i] = new WorldRenderBounds { Value = new AABB { Center = center, Extents = extents } };
                    }
                }
                {
                    Physics.GetCenterExtents(chunkAabb, out var center, out var extents);
                    chunk.SetChunkComponentData(ref ChunkWorldRenderBounds, new ChunkWorldRenderBounds { Value = new AABB { Center = center, Extents = extents } });
                }
            }
        }
    }
}
#endif

