using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary></summary>
    /// An optional flag which specifies when a skinned mesh needs to be rebound
    /// Usage: Add/Write this component to true whenever binding needs to occur.
    /// This will become an Enabled component tag in 1.0.
    /// Binding must occur whenever the presence or value of any of the following changes:
    /// - BindSkeletonRoot
    /// - MeshSkinningBlobReference
    /// - MeshBindingPathsBlobReference
    /// - OverrideSkinningBoneIndex
    /// - ShaderEffectRadialBounds
    /// - LockToSkeletonRootTag
    /// An initial attempt at binding will be made when the SkeletonMeshBindingReactiveSystem
    /// first processes a mesh entity, even without this flag component.
    /// However, if the flag component is present and set to false at this time, no binding
    /// attempt will be made.
    /// </summary>
    public struct NeedsBindingFlag : IComponentData
    {
        public bool needsBinding;
    }

    /// <summary>
    /// The skeleton entity this skinned mesh should be bound to
    /// Usage: Add/Write to attach a skinned mesh to a skeleton.
    /// </summary>
    [MaximumChunkCapacity(128)]
    public struct BindSkeletonRoot : IComponentData
    {
        public EntityWith<SkeletonRootTag> root;
    }

    /// <summary>
    /// The skinned mesh skinning data to be used for skinning on the GPU
    /// Usage: Typically Read Only (Add/Write for procedural meshes)
    /// After binding, this component can be safely removed.
    /// </summary>
    public struct MeshSkinningBlobReference : IComponentData
    {
        public BlobAssetReference<MeshSkinningBlob> blob;
    }

    /// <summary>
    /// Authored binding paths used to help a skinned mesh find the correct bones
    /// in the skeleton to bind to
    /// Usage: Typically Read Only (Add/Write for procedural meshes)
    /// After binding, this component can be safely removed.
    /// </summary>
    public struct MeshBindingPathsBlobReference : IComponentData
    {
        public BlobAssetReference<MeshBindingPathsBlob> blob;
    }

    /// <summary>
    /// This buffer allows for manual binding of bone indices with the skeleton
    /// Usage: Add/Write to create a custom mapping from mesh bone indices to
    /// skeleton bone indices. The length of this buffer must match the length
    /// of MeshSkinningBlob.bindPoses.
    /// After binding, this buffer can be safely removed
    /// </summary>
    public struct OverrideSkinningBoneIndex : IBufferElementData
    {
        public short boneOffset;
    }

    /// <summary>
    /// An additional offset to the bounds to account for vertex shader effects
    /// Usage: Add/Write to apply additional bounds inflation for a mesh
    /// that modifies vertex positions in its shader (aside from skinning)
    /// After binding, this buffer can be safely removed
    /// </summary>
    public struct ShaderEffectRadialBounds : IComponentData
    {
        public float radialBounds;
    }

    /// <summary>
    /// Instructs this entity to reuse skinning data from another entity with the
    /// same mesh rather than perform its own skinning
    /// Usage: Add to share a skin from another entity.
    /// This is used when an authored Skinned Mesh is decomposed into multiple
    /// RenderMesh instances with different submesh indices
    /// </summary>
    public struct ShareSkinFromEntity : IComponentData
    {
        public EntityWith<MeshSkinningBlobReference> sourceSkinnedEntity;
    }

    /// <summary>
    /// An entity that becomes the parent target for all skinned mesh entities
    /// which failed to bind to their intended skeleton target
    /// Usage: Query to get the entity where all children are failed bindings.
    /// Currently the transform system (including framework variants) will
    /// crash if a Parent has a Null entity value.
    /// For performance reasons, all bound entities have Parent and LocalToParent
    /// components added even if the bindings failed. So entities with failed
    /// bindings are parented to a singleton entity with this tag instead.
    /// </summary>
    public struct FailedBindingsRootTag : IComponentData { }

    #region BlobData
    public struct BoneWeightLinkedList
    {
        public float weight;
        public uint  next10Lds7Bone15;
    }

    public struct VertexToSkin
    {
        public float3 position;
        public float3 normal;
        public float3 tangent;
    }

    public struct MeshSkinningBlob
    {
        public BlobArray<float4x4>             bindPoses;
        public BlobArray<float>                maxRadialOffsetsInBoneSpaceByBone;
        public BlobArray<VertexToSkin>         verticesToSkin;
        public BlobArray<BoneWeightLinkedList> boneWeights;
        public BlobArray<uint>                 boneWeightBatchStarts;
        public FixedString128Bytes             name;
    }

    public struct MeshBindingPathsBlob
    {
        // Todo: Make this a BlobArray<BlobString> once supported in Burst
        public BlobArray<BlobArray<byte> > pathsInReversedNotation;
    }
    #endregion
}

