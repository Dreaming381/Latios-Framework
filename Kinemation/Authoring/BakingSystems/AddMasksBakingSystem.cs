using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateAfter(typeof(RenderMeshPostProcessSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AddMasksBakingSystem : ISystem
    {
        EntityQuery m_addQuery;
        EntityQuery m_removeQuery;

        public void OnCreate(ref SystemState state)
        {
            m_addQuery    = state.Fluent().With<MaterialMeshInfo>(true).Without<ChunkPerFrameCullingMask>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_removeQuery = state.Fluent().Without<MaterialMeshInfo>(true).With<ChunkPerFrameCullingMask>(true, true).IncludePrefabs().IncludeDisabledEntities().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var typeset = new ComponentTypeSet(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                               ComponentType.ChunkComponent<ChunkPerCameraCullingSplitsMask>(),
                                               ComponentType.ChunkComponent<ChunkPerDispatchCullingMask>(),
                                               ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                               ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>());
            state.EntityManager.AddComponent(m_addQuery, typeset);
            state.EntityManager.RemoveComponent(m_removeQuery, typeset);
        }
    }

    //[RequireMatchingQueriesForUpdate]
    //[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    //[UpdateAfter(typeof(RenderMeshPostProcessSystem))]
    //[DisableAutoCreation]
    //public partial class ValidateRenderMeshArraySystem : SystemBase
    //{
    //    protected override void OnUpdate()
    //    {
    //        foreach ((var renderMeshArray, var renderMesh, var entity) in Query<RenderMeshArray>().WithEntityAccess())
    //        {
    //            UnityEngine.Debug.Log($"RenderMeshArray was null: {entity}, {renderMesh.mesh.name}, {renderMesh.material.name}");
    //        }
    //
    //        //Entities.ForEach((Entity entity, in RenderMeshArray renderMeshArray, in RenderMesh renderMesh) =>
    //        //{
    //        //    if (renderMeshArray.Meshes == null)
    //        //    {
    //        //        UnityEngine.Debug.Log($"RenderMeshArray was null: {entity}, {renderMesh.mesh.name}, {renderMesh.material.name}");
    //        //    }
    //        //}).WithoutBurst().Run();
    //    }
    //}
}

