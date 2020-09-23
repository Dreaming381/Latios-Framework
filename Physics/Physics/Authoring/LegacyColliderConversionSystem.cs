using Unity.Entities;
using Unity.Mathematics;

namespace Latios.PhysicsEngine.Authoring.Systems
{
    [ConverterVersion("latios", 1)]
    public class LegacyColliderConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.WithNone<DontConvertColliderTag>().ForEach((UnityEngine.SphereCollider goSphere) =>
            {
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
                float3 lossyScale = goCap.transform.lossyScale;
                if (math.cmax(lossyScale) - math.cmin(lossyScale) > 1.0E-5f)
                {
                    UnityEngine.Debug.LogWarning("Failed to convert " + goCap + ". Only uniform scaling is supported on CapsuleCollider.");
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
        }
    }
}

