using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Specifies an additional LOD1 to be added to a MeshRenderer.
    /// Unlike with LOD Group, this LOD1 is combined with the entity or pair of entities.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/LOD Pack (Kinemation)")]
    public class LodAppendAuthoring : MonoBehaviour
    {
        [Tooltip("The screen percentage above which LOD0 (the MeshFilter mesh) is exclusively shown")]
        [Range(0.0f, 100f)]
        [FormerlySerializedAs("lodTransitionMaxPercentage")]
        public float lod01TransitionMaxPercentage = 1f;
        [Tooltip("The screen percentage below which LOD1 (the lod1Mesh) is exclusively shown")]
        [Range(0.0f, 100f)]
        [FormerlySerializedAs("lodTransitionMinPercentage")]
        public float lod01TransitionMinPercentage = 0.5f;

        [Tooltip("The screen percentage above which LOD1 (the lod1 mesh) is exclusively shown")]
        [Range(0.0f, 100f)]
        public float lod12TransitionMaxPercentage = 0.1f;
        [Tooltip("The screen percentage below which LOD2 (the lod2 mesh) is exclusively shown")]
        [Range(0.0f, 100f)]
        public float lod12TransitionMinPercentage = 0.01f;

        [FormerlySerializedAs("loResMesh")]
        public Mesh lod1Mesh;
        [FormerlySerializedAs("useOverrideMaterials")]
        public bool useOverrideMaterialsForLod1;
        [FormerlySerializedAs("overrideMaterials")]
        public List<Material> overrideMaterialsForLod1;

        public bool           enableLod2;
        public Mesh           lod2Mesh;
        public bool           useOverrideMaterialsForLod2;
        public List<Material> overrideMaterialsForLod2;
    }
}

