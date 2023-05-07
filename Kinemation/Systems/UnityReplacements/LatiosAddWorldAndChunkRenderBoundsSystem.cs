#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Kinemation
{
    [UpdateInGroup(typeof(StructuralChangePresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.EntitySceneOptimizations | WorldSystemFilterFlags.Editor)]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LatiosAddWorldAndChunkRenderBoundsSystem : ISystem
    {
        EntityQuery m_MissingWorldRenderBounds;
        EntityQuery m_MissingWorldChunkRenderBounds;

        public void OnCreate(ref SystemState state)
        {
            m_MissingWorldRenderBounds = state.GetEntityQuery
                                         (
                new EntityQueryDesc
            {
                All     = new[] { ComponentType.ReadOnly<RenderBounds>(), ComponentType.ReadOnly<WorldTransform>() },
                None    = new[] { ComponentType.ReadOnly<WorldRenderBounds>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            }
                                         );

            m_MissingWorldChunkRenderBounds = state.GetEntityQuery
                                              (
                new EntityQueryDesc
            {
                All     = new[] { ComponentType.ReadOnly<RenderBounds>(), ComponentType.ReadOnly<WorldTransform>() },
                None    = new[] { ComponentType.ChunkComponentReadOnly<ChunkWorldRenderBounds>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            }
                                              );
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.AddComponent(m_MissingWorldRenderBounds,      ComponentType.ReadWrite<WorldRenderBounds>());
            state.EntityManager.AddComponent(m_MissingWorldChunkRenderBounds, ComponentType.ChunkComponent<ChunkWorldRenderBounds>());
        }
    }
}
#endif

