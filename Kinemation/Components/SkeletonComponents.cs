using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation
{
    #region All Skeletons

    /// <summary>
    /// A tag attached to the skeleton entity
    /// Usage: Required for a skeleton to be valid.
    /// Add/Write for procedural skeletons.
    /// </summary>
    public struct SkeletonRootTag : IComponentData { }

    /// <summary>
    /// A reference to the skeleton entity used by an exposed or exported bone
    /// Usage: Typically Read Only (Add/Write for procedural skeletons)
    /// This component is used internally to point mesh bindings to the skeleton
    /// root and for when exported bones look up copy their transforms.
    /// After conversion, it is not maintained internally.
    /// If you make procedural changes to the skeleton,
    /// you are responsible for maintaining this component.
    /// </summary>
    public struct BoneOwningSkeletonReference : IComponentData
    {
        public EntityWith<SkeletonRootTag> skeletonRoot;
    }

    /// <summary>
    /// A mask which specifies if this skeleton is visible for the current
    /// camera culling pass
    /// Usage: Typically Read Only
    /// </summary>
    public struct ChunkPerCameraSkeletonCullingMask : IComponentData
    {
        public BitField64 lower;
        public BitField64 upper;
    }

    public struct SkeletonBindingPathsBlob
    {
        // Todo: Make this a BlobArray<BlobString> once supported in Burst
        public BlobArray<BlobArray<byte> > pathsInReversedNotation;
    }

    /// <summary>
    /// Authored binding paths used to help a skinned mesh find the correct bones
    /// in the skeleton to bind to
    /// Usage: Typically Read Only (Add/Write for procedural meshes)
    /// After all bindings are complete, this component can be safely removed.
    /// </summary>
    public struct SkeletonBindingPathsBlobReference : IComponentData
    {
        public BlobAssetReference<SkeletonBindingPathsBlob> blob;
    }

    #endregion
    #region Exposed skeleton

    /// <summary>
    /// The bone index in the skeleton this bone corresponds to
    /// Usage: Typically Read Only (Add/Write for procedural skeletons)
    /// This component is added during conversion for user convenience
    /// and is written to by SkeletonMeshBindingReactiveSystem but never
    /// read internally. It can be used for sampling skeleton clips.
    /// </summary>
    public struct BoneIndex : IComponentData
    {
        public short index;
    }

    /// <summary>
    /// A buffer containing the bone entities in the exposed skeleton
    /// Usage: Typically Read Only (Add/Write for procedural skeletons)
    /// Lives on the Skeleton Root. All LocalToWorld values will be used as
    /// bone matrices for skinning purposes. The first bone is the reference
    /// space for deformations and should be the skeleton root entity.
    /// If creating bones from scratch, you also should call
    /// CullingUtilities.GetBoneCullingComponentTypes() and add to each bone
    /// in this buffer. After the components have been added, you must set the
    /// BoneReferenceIsDirtyFlag to true (you may need to add that component).
    /// The bones will be synchronized with the skeleton during
    /// SkeletonMeshBindingReactiveSystem. You do not need to set the flag
    /// if the system has not processed the skeleton at least once yet.
    ///
    /// WARNING: If a bone with a BoneIndex or the culling components is added
    /// to multiple BoneReference buffers, there will be a data race!
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BoneReference : IBufferElementData
    {
        public EntityWith<LocalToWorld> bone;
    }

    /// <summary>
    /// An optional flag component which specifies if the bone entities
    /// need to be resynced with the BoneReference buffer
    /// Usage: If a skeleton has this component and a value of true,
    /// it will synchronize its skeleton with all the bones in the buffer,
    /// populating the BoneIndex, removing old bones from culling, and
    /// allowing new bones to report culling.
    /// This happens during SkeletonMeshBindingReactiveSystem and is only
    /// required if you modify the BoneReference buffer after that system
    /// has ran on the skeleton entity once.
    /// </summary>
    public struct BoneReferenceIsDirtyFlag : IComponentData
    {
        public bool isDirty;
    }

    #endregion
    #region Optimized skeleton
    /// <summary>
    /// Blob asset containing authored hierarchical information about a skeleton
    /// </summary>
    public struct OptimizedSkeletonHierarchyBlob
    {
        /// <summary>
        /// The index to each bone's parent, or -1 if the bone does not have a parent.
        /// A parent is guaranteed to have a smaller index than its child bone.
        /// A maximum of 32767 bones is supported.
        /// </summary>
        public BlobArray<short> parentIndices;
        /// <summary>
        /// A bit array specifying if a bone expects ParentScaleInverse behavior to be applied.
        /// This allows animators to achieve extreme and often cartoony expressions.
        /// </summary>
        public BlobArray<BitField64> hasParentScaleInverseBitmask;
        /// <summary>
        /// A bit array specifying if a bone needs to calculate an inverse scale because a
        /// child requires it for ParentScaleInverse behavior.
        /// </summary>
        public BlobArray<BitField64> hasChildWithParentScaleInverseBitmask;
        /// <summary>
        /// If true, at least one bone expects ParentScaleInverse behavior to be applied.
        /// Some fast-paths may be enabled when this value is false.
        /// </summary>
        public bool hasAnyParentScaleInverseBone;
    }

    /// <summary>
    /// The blob asset reference for an optimized skeleton describing its hierarchical structure
    /// Usage: Typically Read Only (Add/Write for procedural skeletons)
    /// </summary>
    public struct OptimizedSkeletonHierarchyBlobReference : IComponentData
    {
        public BlobAssetReference<OptimizedSkeletonHierarchyBlob> blob;
    }

    /// <summary>
    /// The bone matrices of an optimized hierarchy which get copied to exported bones
    /// and uploaded to the GPU for skinning
    /// Usage: Read or Write for Animations
    /// When animating an optimized hierarchy, you must write to this buffer.
    /// The matrices are the bone transform relative to the root. Use the
    /// OptimizedSkeletonHierarchyBlobReference to compute hierarchical transforms.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct OptimizedBoneToRoot : IBufferElementData
    {
        public float4x4 boneToRoot;
    }

    /// <summary>
    /// Describes an exported bone entity which should inherit the transform of an
    /// optimized bone.
    /// Usage: Add to an entity to make it track a bone in an optimized skeleton.
    /// The exported bone should be parented to the skeleton entity.
    /// </summary>
    [WriteGroup(typeof(LocalToParent))]
    public struct CopyLocalToParentFromBone : IComponentData
    {
        public short boneIndex;
    }
    #endregion
}

