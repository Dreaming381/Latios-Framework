using Latios.Psyshock;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
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
        EntityQuery                             m_WorldRenderBounds;
        WorldTransformReadOnlyAspect.TypeHandle m_worldTransformHandle;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_WorldRenderBounds = state.Fluent().With<ChunkWorldRenderBounds>(false, true).With<WorldRenderBounds>(false).With<RenderBounds>(true)
                                  .WithWorldTransformReadOnly().Without<ChunkSkinningCullingTag>(true).Build();
            m_WorldRenderBounds.AddChangedVersionFilter(ComponentType.ReadOnly<RenderBounds>());
            m_WorldRenderBounds.AddWorldTranformChangeFilter();
            m_WorldRenderBounds.AddOrderVersionFilter();

            m_worldTransformHandle = new WorldTransformReadOnlyAspect.TypeHandle(ref state);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_worldTransformHandle.Update(ref state);

            var boundsJob = new BoundsJob
            {
                RendererBounds                 = GetComponentTypeHandle<RenderBounds>(true),
                WorldTransform                 = m_worldTransformHandle,
                PostProcessMatrix              = GetComponentTypeHandle<PostProcessMatrix>(true),
                WorldRenderBounds              = GetComponentTypeHandle<WorldRenderBounds>(),
                ChunkWorldRenderBounds         = GetComponentTypeHandle<ChunkWorldRenderBounds>(),
                shaderEffectRadialBoundsHandle = GetComponentTypeHandle<ShaderEffectRadialBounds>(true),
            };
            state.Dependency = boundsJob.ScheduleParallel(m_WorldRenderBounds, state.Dependency);
        }

        [BurstCompile]
        struct BoundsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<RenderBounds>             RendererBounds;
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle       WorldTransform;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>        PostProcessMatrix;
            [ReadOnly] public ComponentTypeHandle<ShaderEffectRadialBounds> shaderEffectRadialBoundsHandle;
            public ComponentTypeHandle<WorldRenderBounds>                   WorldRenderBounds;
            public ComponentTypeHandle<ChunkWorldRenderBounds>              ChunkWorldRenderBounds;

            [NoAlias, NativeDisableContainerSafetyRestriction] NativeArray<RenderBounds> tempBoundsBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Unity.Assertions.Assert.IsFalse(useEnabledMask);

                var worldBounds     = chunk.GetNativeArray(ref WorldRenderBounds);
                var localBounds     = chunk.GetNativeArray(ref RendererBounds);
                var worldTransforms = WorldTransform.Resolve(chunk);
                var shaderBounds    = chunk.GetNativeArray(ref shaderEffectRadialBoundsHandle);

                if (shaderBounds.Length > 0)
                {
                    if (!tempBoundsBuffer.IsCreated)
                        tempBoundsBuffer = new NativeArray<RenderBounds>(128, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int i = 0; i < shaderBounds.Length; i++)
                    {
                        var bounds            = localBounds[i];
                        bounds.Value.Extents += shaderBounds[i].radialBounds;
                        tempBoundsBuffer[i]   = bounds;
                    }
                    localBounds = tempBoundsBuffer;
                }

                var chunkAabb = new Aabb(float.MaxValue, float.MinValue);

                if (chunk.Has(ref PostProcessMatrix))
                {
                    var matrices = chunk.GetNativeArray(ref PostProcessMatrix);
                    for (int i = 0; i != localBounds.Length; i++)
                    {
                        var worldAabb = Physics.TransformAabb(worldTransforms[i], new Aabb(localBounds[i].Value.Min, localBounds[i].Value.Max));
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
                        var worldAabb = Physics.TransformAabb(worldTransforms[i], new Aabb(localBounds[i].Value.Min, localBounds[i].Value.Max));
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

