using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Latios.Authoring;
using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    #region Kinemation Smart Blobber Data
    [InternalBufferCapacity(1)]
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
            return animatorToBuildShadowFor.Equals(other.animatorToBuildShadowFor);
        }

        public override int GetHashCode()
        {
            return animatorToBuildShadowFor.GetHashCode();
        }
    }

    [TemporaryBakingType]
    internal struct ShadowHierarchyReference : IComponentData
    {
        public UnityObjectRef<GameObject> shadowHierarchyRoot;
        public GCHandle                   keepAliveHandle;
    }

    [InternalBufferCapacity(0)]
    [BakingType]
    internal struct ImportedSocket : IBufferElementData
    {
        public Entity boneEntity;
        public int    boneIndex;
    }

    [InternalBufferCapacity(0)]
    [TemporaryBakingType]
    internal struct ImportedSocketGameObjectRef : IBufferElementData
    {
        public UnityObjectRef<GameObject> authoringGameObjectForBone;
    }

    [BakingType]
    internal struct AuthoredSocket : IComponentData { }

    [TemporaryBakingType]
    internal struct PendingMeshBindingPathsBlob : IComponentData
    {
        public SmartBlobberHandle<MeshBindingPathsBlob> blobHandle;
    }

    [TemporaryBakingType]
    internal struct PendingMeshDeformDataBlob : IComponentData
    {
        public SmartBlobberHandle<MeshDeformDataBlob> blobHandle;
    }

    [InternalBufferCapacity(0)]
    [TemporaryBakingType]
    internal struct ClipEventToBake : IBufferElementData
    {
        public ClipEvent clipEvent;
    }

    [InternalBufferCapacity(0)]
    [BakingType]
    internal struct OptimizedSkeletonStructureCacheBoneValidation : IBufferElementData
    {
        public TransformQvvs localTransform;
        public int           parentIndex;
        public int           firstPathByteIndex;
        public int           pathByteCount;
    }

    [InternalBufferCapacity(0)]
    [BakingType]
    internal struct OptimizedSkeletonStructureCachePathValidation : IBufferElementData
    {
        public byte b;
    }
    #endregion

    #region Entities Graphics Baking Replacements
    [BakingType]
    [InternalBufferCapacity(1)]
    internal struct BakingMaterialMeshSubmesh : IBufferElementData
    {
        public UnityObjectRef<Material> material;
        public UnityObjectRef<Mesh>     mesh;
        public int                      submesh;
    }

    [BakingType]
    internal struct BakingLightmapIndex : IComponentData
    {
        public int lightmapIndex;
    }
    #endregion
}

