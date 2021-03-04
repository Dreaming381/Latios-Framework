using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Dragons
{
    public struct AudioSourcePrefab : IComponentData
    {
        public Entity prefab;
    }

    public class SourceSpawner : MonoBehaviour, IDeclareReferencedPrefabs, IConvertGameObjectToEntity
    {
        public GameObject prefab;

        public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
        {
            referencedPrefabs.Add(prefab);
        }

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, new AudioSourcePrefab { prefab = conversionSystem.GetPrimaryEntity(prefab) });
        }
    }
}

