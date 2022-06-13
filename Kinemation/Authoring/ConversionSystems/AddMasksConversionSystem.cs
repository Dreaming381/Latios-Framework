using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Authoring.Systems
{
    [UpdateInGroup(typeof(GameObjectConversionGroup), OrderLast = true)]
    [ConverterVersion("Latios", 1)]
    [DisableAutoCreation]
    public class AddMasksConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var query = DstEntityManager.Fluent().WithAll<RenderMesh>(true).Without<ChunkPerFrameCullingMask>(true).IncludePrefabs().IncludeDisabled().Build();
            DstEntityManager.AddComponent(query, new ComponentTypes(ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                    ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                    ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>()));
            query.Dispose();
        }
    }
}

