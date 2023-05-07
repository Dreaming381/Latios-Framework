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
            m_MissingHybridChunkInfo = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ChunkComponentReadOnly<ChunkWorldRenderBounds>(),
                    ComponentType.ReadOnly<WorldRenderBounds>(),
                    ComponentType.ReadOnly<WorldTransform>(),
                    ComponentType.ReadOnly<MaterialMeshInfo>(),
                },
                None = new[]
                {
                    ComponentType.ChunkComponentReadOnly<EntitiesGraphicsChunkInfo>(),
                    ComponentType.ReadOnly<DisableRendering>(),
                },

                // TODO: Add chunk component to disabled entities and prefab entities to work around
                // the fragmentation issue where entities are not added to existing chunks with chunk
                // components. Remove this once chunk components don't affect archetype matching
                // on entity creation.
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab,
            });

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
#endif

