using System.Collections;
using System.Collections.Generic;
using Latios;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Dragons
{
    public class ButtonGUI : MonoBehaviour
    {
        public float maxDistance = 25f;

        void OnGUI()
        {
            if (GUI.Button(new Rect(10, 10, 150, 100), "Play Clip!"))
            {
                var latiosWorld = World.DefaultGameObjectInjectionWorld as LatiosWorld;
                if (latiosWorld == null)
                {
                    Debug.LogError("The default world is not a LatiosWorld. Did you forget to add a bootstrap?");
                    return;
                }
                var entity = latiosWorld.EntityManager.Instantiate(
                    latiosWorld.worldBlackboardEntity.GetComponentData<AudioSourcePrefab>().prefab);
                var pos                                                                    = Random.insideUnitSphere * maxDistance;
                latiosWorld.EntityManager.SetComponentData(entity, new Translation { Value = pos });
            }
        }
    }
}

