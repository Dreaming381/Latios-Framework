using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Specifies an additional LOD1 to be added to a MeshRenderer.
    /// Unlike with LOD Group, this LOD1 is combined with the entity or pair of entities.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/LOD1 Append")]
    public class LodAppendAuthoring : MonoBehaviour
    {
        [Tooltip("The screen percentage below which LOD1 (the loResMesh) is exclusively shown")]
        [Range(0.0f, 100f)]
        public float lodTransitionMinPercentage = 0.5f;
        [Tooltip("The screen percentage above which LOD0 (the MeshFilter mesh) is exclusively shown")]
        [Range(0.0f, 100f)]
        public float lodTransitionMaxPercentage = 1f;

        public Mesh           loResMesh;
        public bool           useOverrideMaterials;
        public List<Material> overrideMaterials;
    }
}

