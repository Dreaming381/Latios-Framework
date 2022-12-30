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
            m_computeQuery = state.Fluent().WithAll<MaterialMeshInfo>(true).WithAll<ComputeDeformShaderIndex>().Without<ShareSkinFromEntity>()
                             .Without<ChunkComputeDeformMemoryMetadata>(      true).IncludePrefabs().IncludeDisabledEntities().Build();

            m_linearBlendQuery = state.Fluent().WithAll<MaterialMeshInfo>(true).WithAll<LinearBlendSkinningShaderIndex>().Without<ShareSkinFromEntity>()
                                 .Without<ChunkLinearBlendSkinningMemoryMetadata>(true).IncludePrefabs().IncludeDisabledEntities().Build();

            m_copyQuery = state.Fluent().WithAll<MaterialMeshInfo>(true).WithAll<ShareSkinFromEntity>(true).Without<ChunkCopySkinShaderData>(true)
                          .IncludePrefabs().IncludeDisabledEntities().Build();

            m_baseQuery = state.Fluent().WithAll<MaterialMeshInfo>(true).Without<ChunkPerFrameCullingMask>(true).IncludePrefabs().IncludeDisabledEntities().Build();
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

