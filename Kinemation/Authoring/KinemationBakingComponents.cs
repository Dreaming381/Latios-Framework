using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    [BakingType]
    internal struct AutoBindSkinnedMeshToSkeleton : IBufferElementData
    {
        public Entity skinnedMeshEntity;
    }

    [TemporaryBakingType]
    internal struct PendingSkeletonBindingPathsBlob : IComponentData
    {
        public SmartBlobberHandle<SkeletonBindingPathsBlob> blobHandle;
    }

    [TemporaryBakingType]
    internal struct PendingOptimizedSkeletonHierarchyBlob : IComponentData
    {
        public SmartBlobberHandle<OptimizedSkeletonHierarchyBlob> blobHandle;
    }

    [TemporaryBakingType]
    internal struct ShadowHierarchyRequest : IComponentData, IEquatable<ShadowHierarchyRequest>
    {
        public UnityObjectRef<Animator> animatorToBuildShadowFor;

        public bool Equals(ShadowHierarchyRequest other)
        {
            return animatorToBuildShadowFor.GetInstanceID().Equals(other.animatorToBuildShadowFor.GetInstanceID());
        }

        public override int GetHashCode()
        {
            return animatorToBuildShadowFor.GetInstanceID();
        }
    }

    [TemporaryBakingType]
    internal struct ShadowHierarchyReference : IComponentData
    {
        public UnityObjectRef<GameObject> shadowHierarchyRoot;
        public GCHandle                   keepAliveHandle;
    }

    [BakingType]
    internal struct OptimizedSkeletonExportedBone : IBufferElementData
    {
        public Entity boneEntity;
        public int    boneIndex;
    }

    [TemporaryBakingType]
    internal struct ExportedBoneGameObjectRef : IBufferElementData
    {
        public UnityObjectRef<GameObject> authoringGameObjectForBone;
    }

    [TemporaryBakingType]
    internal struct PendingMeshBindingPathsBlob : IComponentData
    {
        public SmartBlobberHandle<MeshBindingPathsBlob> blobHandle;
    }

    [TemporaryBakingType]
    internal struct PendingMeshSkinningBlob : IComponentData
    {
        public SmartBlobberHandle<MeshSkinningBlob> blobHandle;
    }

    internal class SkeletonConversionContext : IComponentData
    {
        public BoneTransformData[]       skeleton;
        public bool                      isOptimized;
        public Animator                  animator;
        public SkeletonSettingsAuthoring authoring;

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
                m_shadowHierarchy.DestroyDuringConversion();
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

