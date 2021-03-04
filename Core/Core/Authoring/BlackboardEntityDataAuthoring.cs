using Unity.Entities;
using UnityEngine;

//Based on the code generated from [GenerateAuthoringComponent]
namespace Latios.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Core/Blackboard Entity Data")]
    [ConverterVersion("latios", 2)]
    public class BlackboardEntityDataAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public BlackboardScope blackboardScope;

        public MergeMethod mergeMethod;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            BlackboardEntityData componentData = default;
            componentData.blackboardScope      = blackboardScope;
            componentData.mergeMethod          = mergeMethod;
            dstManager.AddComponentData(entity, componentData);
        }
    }
}

