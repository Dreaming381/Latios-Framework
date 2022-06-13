using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public class AddMissingMasksSystem : SubSystem
    {
        EntityQuery m_computeQuery;
        EntityQuery m_linearBlendQuery;
        EntityQuery m_copyQuery;
        EntityQuery m_baseQuery;

        protected override void OnCreate()
        {
            m_computeQuery = Fluent.WithAll<RenderMesh>(true).WithAll<ComputeDeformShaderIndex>().Without<ShareSkinFromEntity>().Without<ChunkComputeDeformMemoryMetadata>(true)
                             .IncludePrefabs().IncludeDisabled().Build();
            m_linearBlendQuery =
                Fluent.WithAll<RenderMesh>(true).WithAll<LinearBlendSkinningShaderIndex>().Without<ShareSkinFromEntity>().Without<ChunkLinearBlendSkinningMemoryMetadata>(true)
                .IncludePrefabs().IncludeDisabled().Build();
            m_copyQuery = Fluent.WithAll<ShareSkinFromEntity>(true).Without<ChunkCopySkinShaderData>(true).IncludePrefabs().IncludeDisabled().Build();
            m_baseQuery = Fluent.WithAll<RenderMesh>(true).Without<ChunkPerFrameCullingMask>(true).IncludePrefabs().IncludeDisabled().Build();
        }

        protected override void OnUpdate()
        {
            EntityManager.AddComponent(m_computeQuery, new ComponentTypes(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                          ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                          ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>(),
                                                                          ComponentType.ChunkComponent<ChunkComputeDeformMemoryMetadata>()));
            EntityManager.AddComponent(m_computeQuery, new ComponentTypes(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                          ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                          ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>(),
                                                                          ComponentType.ChunkComponent<ChunkLinearBlendSkinningMemoryMetadata>()));
            EntityManager.AddComponent(m_copyQuery, new ComponentTypes(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                       ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                       ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>(),
                                                                       ComponentType.ChunkComponent<ChunkCopySkinShaderData>()));
            EntityManager.AddComponent(m_baseQuery, new ComponentTypes(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                       ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                       ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>()));
        }
    }
}

