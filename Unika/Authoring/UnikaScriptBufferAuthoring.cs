using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Unika.Authoring
{
    /// <summary>
    /// This should be added to any GameObject that will be baked into an entity and have scripts assigned to it.
    /// </summary>
    [AddComponentMenu("Latios/Unika/Script Buffer (Unika)")]
    [DisallowMultipleComponent]
    public class UnikaScriptBufferAuthoring : MonoBehaviour
    {
    }

    public class UnikaScriptBufferAuthoringBaker : Baker<UnikaScriptBufferAuthoring>
    {
        public override void Bake(UnikaScriptBufferAuthoring authoring)
        {
            var                              entity = GetEntity(TransformUsageFlags.None);
            FixedList128Bytes<ComponentType> types  = default;
            types.Add(ComponentType.ReadWrite<UnikaScripts>());
            types.Add(ComponentType.ReadWrite<UnikaSerializedEntityReference>());
            types.Add(ComponentType.ReadWrite<UnikaSerializedBlobReference>());
            types.Add(ComponentType.ReadWrite<UnikaSerializedAssetReference>());
            types.Add(ComponentType.ReadWrite<UnikaSerializedObjectReference>());
            types.Add(ComponentType.ReadWrite<UnikaSerializedTypeIds>());
            AddComponent(entity, new ComponentTypeSet(in types));
        }
    }

    [DisableAutoCreation]
    public class UnikaScriptBufferEntitySerializationBaker : Baker<UnikaScriptBufferAuthoring>
    {
        public override void Bake(UnikaScriptBufferAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent<UnikaEntitySerializationController>(entity);
            SetComponentEnabled<UnikaEntitySerializationController>(entity, true);
        }
    }
}

