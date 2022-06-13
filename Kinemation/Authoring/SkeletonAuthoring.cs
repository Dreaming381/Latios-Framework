using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/Skeleton (Animated Hierarchy)")]
    public class SkeletonAuthoring : MonoBehaviour
    {
        public BindingMode             bindingMode = BindingMode.ConversionTime;  // Make this Import once supported
        public List<BoneTransformData> customSkeleton;

        // Public for debugging
        public BoneTransformData[] m_importSkeleton;
        public ImportStatus        m_importStatus;
    }

    public enum BindingMode
    {
        Import,
        ConversionTime,
        Custom
    }

    public enum ImportStatus
    {
        Uninitialized,
        Success,
        UnknownError,
        AmbiguityError
    }

    [Serializable]
    public struct BoneTransformData
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

