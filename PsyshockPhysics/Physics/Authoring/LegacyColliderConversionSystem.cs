using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock.Authoring.Systems
{
    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    [DisableAutoCreation]
    [ConverterVersion("latios", 4)]
    public class LegacyColliderConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.WithNone<DontConvertColliderTag>().ForEach((UnityEngine.SphereCollider goSphere) =>
            {
                DeclareDependency(goSphere,
                                  goSphere.transform);
                float3 lossyScale = goSphere.transform.lossyScale;
                if (math.cmax(lossyScale) - math.cmin(lossyScale) > 1.0E-5f)
                {
                    UnityEngine.Debug.LogWarning(
                        $"Failed to convert {goSphere}. Only uniform scaling is supported on SphereCollider. Lossy Scale divergence was: {math.cmax(lossyScale) - math.cmin(lossyScale)}");
                    return;
                }

                Entity   entity    = GetPrimaryEntity(goSphere);
                Collider icdSphere = new SphereCollider
                {
                    center = goSphere.center,
                    radius = goSphere.radius * goSphere.transform.localScale.x
                };
                DstEntityManager.AddComponentData(entity, icdSphere);
            });

            Entities.WithNone<DontConvertColliderTag>().ForEach((UnityEngine.CapsuleCollider goCap) =>
            {
                DeclareDependency(goCap,
                                  goCap.transform);
                float3 lossyScale = goCap.transform.lossyScale;
                if (math.cmax(lossyScale) - math.cmin(lossyScale) > 1.0E-5f)
                {
                    UnityEngine.Debug.LogWarning(
                        $"Failed to convert { goCap }. Only uniform scaling is supported on CapsuleCollider. Lossy Scale divergence was: {math.cmax(lossyScale) - math.cmin(lossyScale)}");
                    return;
                }

                Entity entity = GetPrimaryEntity(goCap);
                float3 dir;
                if (goCap.direction == 0)
                {
                    dir = new float3(1f, 0f, 0f);
                }
                else if (goCap.direction == 1)
                {
                    dir = new float3(0f, 1, 0f);
                }
                else
                {
                    dir = new float3(0f, 0f, 1f);
                }
                Collider icdCap = new CapsuleCollider
                {
                    pointB = (float3)goCap.center + ((goCap.height / 2f - goCap.radius) * goCap.transform.lossyScale.x * dir),
                    pointA = (float3)goCap.center - ((goCap.height / 2f - goCap.radius) * goCap.transform.lossyScale.x * dir),
                    radius = goCap.radius * goCap.transform.lossyScale.x
                };
                DstEntityManager.AddComponentData(entity, icdCap);
            });

            Entities.WithNone<DontConvertColliderTag>().ForEach((UnityEngine.BoxCollider goBox) =>
            {
                DeclareDependency(goBox, goBox.transform);
                float3 lossyScale = goBox.transform.lossyScale;

                Entity   entity = GetPrimaryEntity(goBox);
                Collider icdBox = new BoxCollider
                {
                    center   = goBox.center,
                    halfSize = goBox.size * lossyScale / 2f
                };
                DstEntityManager.AddComponentData(entity, icdBox);
            });

            Entities.ForEach((ConvexMeshColliderConversionList list) =>
            {
                foreach (var mc in list.meshColliders)
                {
                    var entity = GetPrimaryEntity(mc.mesh);

                    var blob = mc.blobHandle.Resolve();
                    if (!blob.IsCreated)
                        continue;

                    Collider icdConvex = new ConvexCollider
                    {
                        convexColliderBlob = blob,
                        scale              = mc.mesh.transform.lossyScale
                    };
                    DstEntityManager.AddComponentData(entity, icdConvex);
                }
            });
        }
    }
}

