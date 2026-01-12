using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/Decal Settings")]
    public class DecalSettingsAuthoring : MonoBehaviour
    {
        [Range(0f, 180f)]
        public float angleFadeMin = 60f;
        [Range(0f, 180f)]
        public float angleFadeMax = 80f;
    }

    public class DecalSettingsAuthoringBaker : Baker<DecalSettingsAuthoring>
    {
        public override void Bake(DecalSettingsAuthoring authoring)
        {
#if (HDRP_10_0_0_OR_NEWER && !URP_10_0_0_OR_NEWER) || (!HDRP_10_0_0_OR_NEWER && URP_10_0_0_OR_NEWER)
            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new DecalAngleFade(math.radians(authoring.angleFadeMin), math.radians(authoring.angleFadeMax)));
#else
            UnityEngine.Debug.LogWarning("Either both URP and HDRP are installed in the project, or neither are installed. Cannot bake render pipeline specific DecalAngleFade component.");
#endif
        }
    }
}