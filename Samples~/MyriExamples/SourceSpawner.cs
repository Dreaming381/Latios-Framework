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

    public class SourceSpawner : MonoBehaviour
    {
        public GameObject prefab;
    }
    
    public class SourceSpawnerBaker : Baker<SourceSpawner
    {
        public override void Bake(SourceSpawner)
        {
            AddComponent(entity, new AudioSourcePrefab { prefab = GetEntity(prefab) });
        }
    }
}

