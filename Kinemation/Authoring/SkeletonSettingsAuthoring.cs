using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Specifies additional customizations to be made for baking a skeleton.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/Skeleton Settings (Animated Hierarchy)")]
    public class SkeletonSettingsAuthoring : MonoBehaviour
    {
        /// <summary>
        /// Specifies how entities are gathered into a skeleton
        /// </summary>
        public BindingMode bindingMode = BindingMode.BakeTime;

        //public List<Transform> customIncludeBones;
        //public List<Transform> exportInForcedOptimizedHierarchy;

        public enum BindingMode
        {
            /// <summary>
            /// Skips generation of the skeleton entirely. Useful if the Animator was used to animate something else.
            /// </summary>
            DoNotGenerate,
            /// <summary>
            /// Generates the skeleton based on the GameObject hierarchy and Animator Avatar at bake time.
            /// </summary>
            BakeTime,
            //BakeTimeForceOptimized
            //CustomWhitelistPlusAncestors
            //CustomWhitelistPlusAncestorsForceOptimized
        }
    }

    [Serializable]
    internal struct BoneTransformData
    {
        public float3     localPosition;
        public quaternion localRotation;
        public float3     localScale;
        public Transform  gameObjectTransform;  // Null if not exposed
        public int        parentIndex;  // -1 if root, otherwise must be less than current index
        public bool       ignoreParentScale;
        public string     hierarchyReversePath;  // Example: "foot.l/lower leg.l/upper leg.l/hips/armature/red soldier/"
    }
}

