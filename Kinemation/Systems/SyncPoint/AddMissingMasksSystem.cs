using Unity.Burst;
using Unity.Entities;
using Unity.Rendering;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AddMissingMasksSystem : ISystem
    {
        EntityQuery m_computeQuery;
        EntityQuery m_linearBlendQuery;
        EntityQuery m_copyQuery;
        EntityQuery m_baseQuery;

        public void OnCreate(ref SystemState state)
        {
            m_computeQuery = state.Fluent().WithAll<RenderMesh>(true).WithAll<ComputeDeformShaderIndex>().Without<ShareSkinFromEntity>()
                             .Without<ChunkComputeDeformMemoryMetadata>(      true).IncludePrefabs().IncludeDisabled().Build();

            m_linearBlendQuery = state.Fluent().WithAll<RenderMesh>(true).WithAll<LinearBlendSkinningShaderIndex>().Without<ShareSkinFromEntity>()
                                 .Without<ChunkLinearBlendSkinningMemoryMetadata>(true).IncludePrefabs().IncludeDisabled().Build();

            m_copyQuery = state.Fluent().WithAll<ShareSkinFromEntity>(true).Without<ChunkCopySkinShaderData>(true).IncludePrefabs().IncludeDisabled().Build();

            m_baseQuery = state.Fluent().WithAll<RenderMesh>(true).Without<ChunkPerFrameCullingMask>(true).IncludePrefabs().IncludeDisabled().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.AddComponent(m_computeQuery, new ComponentTypeSet(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                                  ComponentType.ChunkComponent<ChunkPerCameraCullingSplitsMask>(),
                                                                                  ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                                  ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>(),
                                                                                  ComponentType.ChunkComponent<ChunkComputeDeformMemoryMetadata>()));
            state.EntityManager.AddComponent(m_linearBlendQuery, new ComponentTypeSet(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                                      ComponentType.ChunkComponent<ChunkPerCameraCullingSplitsMask>(),
                                                                                      ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                                      ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>(),
                                                                                      ComponentType.ChunkComponent<ChunkLinearBlendSkinningMemoryMetadata>()));
            state.EntityManager.AddComponent(m_copyQuery, new ComponentTypeSet(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                               ComponentType.ChunkComponent<ChunkPerCameraCullingSplitsMask>(),
                                                                               ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                               ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>(),
                                                                               ComponentType.ChunkComponent<ChunkCopySkinShaderData>()));
            state.EntityManager.AddComponent(m_baseQuery, new ComponentTypeSet(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                               ComponentType.ChunkComponent<ChunkPerCameraCullingSplitsMask>(),
                                                                               ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                               ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>()));
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}

