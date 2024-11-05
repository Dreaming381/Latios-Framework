using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Unika
{
    public class UnikaScriptBufferAuthoring : MonoBehaviour
    {
    }

    public class UnikaScriptBufferAuthoringBaker : Baker<UnikaScriptBufferAuthoring>
    {
        public override void Bake(UnikaScriptBufferAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddBuffer<UnikaScripts>(                  entity);
            AddBuffer<UnikaSerializedEntityReference>(entity);
            AddBuffer<UnikaSerializedBlobReference>(  entity);
            AddBuffer<UnikaSerializedAssetReference>( entity);
            AddBuffer<UnikaSerializedObjectReference>(entity);
            AddComponent<UnikaSerializedTypeIds>(entity);
        }
    }
}

