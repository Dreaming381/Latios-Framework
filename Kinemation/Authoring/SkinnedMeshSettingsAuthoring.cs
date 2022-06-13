using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/Skinned Mesh Settings")]
    public class SkinnedMeshSettingsAuthoring : MonoBehaviour
    {
        public BindingMode bindingMode = BindingMode.ConversionTime;  // Make this Import once supported

        public List<string> customBonePathsReversed;

        // Public for debugging
        public string[] m_importBonePathsReversed;
    }
}

