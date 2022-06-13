using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    internal class SkeletonConversionContext : IComponentData
    {
        public BoneTransformData[] skeleton;
        public bool                isOptimized;
        public Animator            animator;
        public SkeletonAuthoring   authoring;

        public GameObject shadowHierarchy
        {
            get
            {
                if (m_shadowHierarchy == null)
                {
                    m_shadowHierarchy = ShadowHierarchyBuilder.BuildShadowHierarchy(animator.gameObject, isOptimized);
                }
                return m_shadowHierarchy;
            }
        }
        private GameObject m_shadowHierarchy = null;

        public void DestroyShadowHierarchy()
        {
            if (m_shadowHierarchy != null)
            {
                GameObject.Destroy(m_shadowHierarchy);
            }
        }
    }

    internal class SkinnedMeshConversionContext : IComponentData
    {
        public string[]                     bonePathsReversed;
        public SkeletonConversionContext    skeletonContext;
        public SkinnedMeshRenderer          renderer;
        public SkinnedMeshSettingsAuthoring authoring;
    }
}

