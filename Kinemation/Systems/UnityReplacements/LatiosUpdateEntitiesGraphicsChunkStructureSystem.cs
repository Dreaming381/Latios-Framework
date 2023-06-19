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
    // Todo: What is this below TODO talking about? Regardless, this always needs to update to check the RecreateAllBatchesFlag.
    //@TODO: Updating always necessary due to empty component group. When Component group and archetype chunks are unified, [RequireMatchingQueriesForUpdate] can be added.
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    //[UpdateInGroup(typeof(StructuralChangePresentationSystemGroup))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LatiosUpdateEntitiesGraphicsChunkStructureSystem : ISystem
    {
        private EntityQuery m_MissingHybridChunkInfo;
        private EntityQuery m_DisabledRenderingQuery;
#if UNITY_EDITOR
        private EntityQuery m_HasHybridChunkInfo;
#endif
        public void OnCreate(ref SystemState state)
        {
            m_MissingHybridChunkInfo = state.Fluent().WithAll<ChunkWorldRenderBounds>(true, true).WithAll<WorldRenderBounds>(true).WithWorldTransformReadOnlyWeak()
                                       .WithAll<MaterialMeshInfo>(true).Without<EntitiesGraphicsChunkInfo>(true).Without<DisableRendering>().IncludePrefabs().
                                       IncludeDisabledEntities().Build();

            m_DisabledRenderingQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<DisableRendering>(),
                },
            });

#if UNITY_EDITOR
            m_HasHybridChunkInfo = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ChunkComponentReadOnly<EntitiesGraphicsChunkInfo>(),
                },
            });
#endif
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        //[BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
#if UNITY_EDITOR
            if (EntitiesGraphicsEditorTools.DebugSettings.RecreateAllBatches)
            {
                UnityEngine.Debug.Log("Recreating all batches");
                state.EntityManager.RemoveChunkComponentData<EntitiesGraphicsChunkInfo>(m_HasHybridChunkInfo);
            }
#endif

            state.EntityManager.AddComponent(m_MissingHybridChunkInfo, ComponentType.ChunkComponent<EntitiesGraphicsChunkInfo>());
            state.EntityManager.RemoveChunkComponentData<EntitiesGraphicsChunkInfo>(m_DisabledRenderingQuery);
        }
    }
}

