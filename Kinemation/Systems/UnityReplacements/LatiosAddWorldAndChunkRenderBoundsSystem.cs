using Latios;
using Latios.Transforms.Abstract;
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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_MissingWorldRenderBounds =
                state.Fluent().WithAll<RenderBounds>(true).WithWorldTransformReadOnlyWeak().Without<WorldRenderBounds>().IncludePrefabs().IncludeDisabledEntities().Build();

            m_MissingWorldChunkRenderBounds =
                state.Fluent().WithAll<RenderBounds>(true).WithWorldTransformReadOnlyWeak().Without<ChunkWorldRenderBounds>(true).IncludePrefabs().IncludeDisabledEntities().
                Build();
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

