#if false
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Authoring
{
    [AddComponentMenu("Latios/Ticking/Ticked Entity (Latios Core)")]
    public class TickedEntityAuthoring : MonoBehaviour
    {
    }

    public class TickedEntityAuthoringBaker : Baker<TickedEntityAuthoring>
    {
        public override void Bake(TickedEntityAuthoring authoring)
        {
            var entity = GetEntityWithoutDependency();
            AddComponent<TickedEntityTag>(entity);
        }
    }
}
#endif

