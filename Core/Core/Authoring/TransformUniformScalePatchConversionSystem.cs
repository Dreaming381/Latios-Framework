using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Latios.Authoring.Systems
{
    [ConverterVersion("latios", 1)]
    [UpdateInGroup(typeof(GameObjectConversionGroup))]
    public class TransformUniformScalePatchConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((Transform transform) =>
            {
                var entity = GetPrimaryEntity(transform);
                if (DstEntityManager.HasComponent<NonUniformScale>(entity))
                {
                    var nus = DstEntityManager.GetComponentData<NonUniformScale>(entity);
                    if (math.cmin(nus.Value) == math.cmax(nus.Value))
                    {
                        //Why? Unity, why?
                        DstEntityManager.RemoveComponent<NonUniformScale>(entity);
                        DstEntityManager.AddComponentData(entity, new Scale { Value = nus.Value.x });
                    }
                }
            });
        }
    }
}

