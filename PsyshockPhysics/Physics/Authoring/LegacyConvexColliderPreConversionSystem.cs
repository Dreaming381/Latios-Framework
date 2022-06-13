using System.Collections.Generic;
using Latios.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Psyshock.Authoring.Systems
{
    internal struct PendingConvexColliderBlob
    {
        public MeshCollider                           mesh;
        public SmartBlobberHandle<ConvexColliderBlob> blobHandle;
    }

    internal class ConvexMeshColliderConversionList : IComponentData
    {
        public List<PendingConvexColliderBlob> meshColliders;
    }

    [UpdateInGroup(typeof(GameObjectBeforeConversionGroup))]
    [DisableAutoCreation]
    [ConverterVersion("latios", 4)]
    public class LegacyConvexColliderPreConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var convexUnityList = new List<PendingConvexColliderBlob>();

            Entities.WithNone<DontConvertColliderTag>().ForEach((MeshCollider goMesh) =>
            {
                DeclareDependency(goMesh, goMesh.transform);

                if (goMesh.convex && goMesh.sharedMesh != null)
                {
                    convexUnityList.Add(new PendingConvexColliderBlob
                    {
                        mesh       = goMesh,
                        blobHandle = this.CreateBlob(goMesh.gameObject, new ConvexColliderBakeData { sharedMesh = goMesh.sharedMesh })
                    });
                }
            });

            var e                                                                                    = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(e, new ConvexMeshColliderConversionList { meshColliders = convexUnityList });
        }
    }
}

