using Unity.Entities;
using UnityEngine;

//Based on the code generated from [GenerateAuthoringComponent]
namespace Latios.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Core/Blackboard Entity Data")]
    public class BlackboardEntityDataAuthoring : MonoBehaviour
    {
        public BlackboardScope blackboardScope;

        public MergeMethod mergeMethod;
    }

    public class BlackboardEntityDataBaker : Baker<BlackboardEntityDataAuthoring>
    {
        public override void Bake(BlackboardEntityDataAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new BlackboardEntityData
            {
                blackboardScope = authoring.blackboardScope,
                mergeMethod     = authoring.mergeMethod
            });
        }
    }
}

