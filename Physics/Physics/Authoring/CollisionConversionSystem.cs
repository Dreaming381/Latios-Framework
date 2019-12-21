using Unity.Entities;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    [ConverterVersion("latios", 1)]
    public class CollisionConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((UnityEngine.SphereCollider goSphere) =>
            {
                if (goSphere.transform.lossyScale.x != goSphere.transform.lossyScale.y || goSphere.transform.lossyScale.x != goSphere.transform.lossyScale.z)
                {
                    UnityEngine.Debug.LogWarning("Failed to convert " + goSphere + ". Only uniform scaling is supported on SphereCollider.");
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

            Entities.ForEach((UnityEngine.CapsuleCollider goCap) =>
            {
                if (goCap.transform.lossyScale.x != goCap.transform.lossyScale.y || goCap.transform.lossyScale.x != goCap.transform.lossyScale.z)
                {
                    UnityEngine.Debug.LogWarning("Failed to convert " + goCap + ". Only uniform scaling is supported on CapsuleCollider.");
                    return;
                }

                Entity entity = GetPrimaryEntity(goCap);
                float3 dir;
                if (goCap.direction == 0)
                {
                    dir = new float3(0.5f, 0f, 0f);
                }
                else if (goCap.direction == 1)
                {
                    dir = new float3(0f, 0.5f, 0f);
                }
                else
                {
                    dir = new float3(0f, 0f, 0.5f);
                }
                Collider icdCap = new CapsuleCollider
                {
                    pointB = (float3)goCap.center + ((goCap.height - goCap.radius) * goCap.transform.lossyScale.x * dir),
                    pointA = (float3)goCap.center - ((goCap.height - goCap.radius) * goCap.transform.lossyScale.x * dir),
                    radius = goCap.radius * goCap.transform.lossyScale.x
                };
                DstEntityManager.AddComponentData(entity, icdCap);
            });
        }
    }
}

