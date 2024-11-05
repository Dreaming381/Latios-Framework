using System;
using System.Collections.Generic;
using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    [Serializable]
    public struct OptimizedBoneDescription
    {
        public TransformQvvs rootTransform;
        public TransformQvs  localTransform;
        public int           parentIndex;
        public string        shortName;
        public string        reversePath;
        public Transform     importedSocket;  // Warning: Not validated during baking
    }

    [AddComponentMenu("Latios/Kinemation/Optimized Skeleton Cache")]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class OptimizedSkeletonStructureCache : MonoBehaviour
    {
        // Todo: Make this read-only in the inspector
        [SerializeField] internal List<OptimizedBoneDescription> m_bones;
        [SerializeField] internal bool                           m_validateDuringBaking = true;

        // Force this to be copied on instantiation.
        [SerializeField, HideInInspector] bool m_isClone = false;

        public int length => m_bones.Count;

        public OptimizedBoneDescription this[int index] => m_bones[index];

        private void Awake()
        {
            Generate();
        }

        [ContextMenu("Regenerate")]
        private void Regenerate()
        {
            m_bones?.Clear();
            Generate();
        }

        private void Generate()
        {
            if (m_isClone)
                return;

            if (m_bones == null)
                m_bones = new List<OptimizedBoneDescription>();

            if (m_bones.Count != 0)
                return;

            if (!TryGetComponent<Animator>(out var animator))
                return;

            m_isClone        = true;
            var shadow       = new RuntimeBlobBuilders.SkeletonClipSetSampler(animator);
            m_isClone        = false;
            var boneCount    = shadow.boneCount;
            m_bones.Capacity = boneCount;
            for (int i = 0; i < boneCount; i++)
            {
                var bone = shadow.GetShadowTransformForBone(i);
                Transforms.Authoring.TransformBakeUtils.GetScaleAndStretch(bone.localScale, out var scale, out var stretch);
                Transform exportTransform = null;
                {
                    if (bone.TryGetComponent<HideThis.ShadowCloneTracker>(out var export))
                        exportTransform = export.transform;
                }
                m_bones.Add(new OptimizedBoneDescription
                {
                    rootTransform  = new TransformQvvs(default, default, default, stretch),
                    localTransform = new TransformQvs(bone.localPosition, bone.localRotation, scale),
                    parentIndex    = shadow.GetBoneParent(i),
                    shortName      = shadow.GetNameOfBone(i),
                    importedSocket = exportTransform
                });
            }
            var first                    = m_bones[0];
            first.rootTransform.rotation = first.localTransform.rotation;
            first.rootTransform.position = first.localTransform.position;
            first.rootTransform.scale    = first.localTransform.scale;
            first.reversePath            = $"{first.shortName}/";
            m_bones[0]                   = first;
            for (int i = 1; i < boneCount; i++)
            {
                var bone   = m_bones[i];
                var parent = m_bones[bone.parentIndex];
                qvvs.mul(ref bone.rootTransform, parent.rootTransform, bone.localTransform);
                bone.reversePath = $"{bone.shortName}/{parent.reversePath}";
                m_bones[i]       = bone;
            }

            shadow.Dispose();
        }
    }

    public class OptimizedSkeletonStructureCacheBaker : Baker<OptimizedSkeletonStructureCache>
    {
        public override void Bake(OptimizedSkeletonStructureCache authoring)
        {
            if (authoring.m_validateDuringBaking && authoring.m_bones != null && authoring.m_bones.Count > 0)
            {
                var entity     = GetEntity(TransformUsageFlags.None);
                var boneBuffer = AddBuffer<OptimizedSkeletonStructureCacheBoneValidation>(entity);
                var pathBuffer = AddBuffer<OptimizedSkeletonStructureCachePathValidation>(entity).Reinterpret<byte>();
                for (int i = 0; i < authoring.m_bones.Count; i++)
                {
                    var                  bone         = authoring.m_bones[i];
                    FixedString4096Bytes stringBuffer = bone.reversePath;
                    boneBuffer.Add(new OptimizedSkeletonStructureCacheBoneValidation
                    {
                        localTransform     = new TransformQvvs(bone.localTransform.position, bone.localTransform.rotation, bone.localTransform.scale, bone.rootTransform.stretch),
                        parentIndex        = bone.parentIndex,
                        firstPathByteIndex = pathBuffer.Length,
                        pathByteCount      = stringBuffer.Length
                    });
                    for (int j = 0; j < stringBuffer.Length; j++)
                        pathBuffer.Add(stringBuffer[j]);
                }
            }
        }
    }
}

