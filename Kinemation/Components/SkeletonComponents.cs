using System;
using System.Runtime.InteropServices;
using Latios.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

#if LATIOS_TRANSFORMS_UNITY
using Unity.Transforms;
#endif

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

    /// <summary>
    /// A mask which specifies the visible splits for this skeleton in the
    /// current shadow-casting light culling pass
    /// Usage: Only write to this if performing custom skeleton LODing logic for shadows.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct ChunkPerCameraSkeletonCullingSplitsMask : IComponentData
    {
        [FieldOffset(0)] public fixed byte  splitMasks[128];
        [FieldOffset(0)] public fixed ulong ulongMasks[16];  // Ensures 8 byte alignment which is helpful (16 would be better)
    }

    /// <summary>
    /// A blob asset which contains bone path "strings" in reverse path order,
    /// that is from leaf bone to root bone, for each bone in the skeleton.
    /// </summary>
    public struct SkeletonBindingPathsBlob
    {
        // Todo: Make this a BlobArray<BlobString> once supported in Burst
        public BlobArray<BlobArray<byte> > pathsInReversedNotation;

        /// <summary>
        /// Returns true if the path in reversed notation begins with the passed in string, case sensitive
        /// </summary>
        /// <param name="pathIndex">The index into pathsInReversedNotation, which is also a boneIndex</param>
        /// <param name="searchString">The string to search</param>
        /// <returns>Returns true if the string matches the start</returns>
        public unsafe bool StartsWith<T>(int pathIndex, in T searchString) where T : unmanaged, INativeList<byte>, IUTF8Bytes
        {
            if (searchString.Length > pathsInReversedNotation[pathIndex].Length)
                return false;

            return UnsafeUtility.MemCmp(searchString.GetUnsafePtr(), pathsInReversedNotation[pathIndex].GetUnsafePtr(), searchString.Length) == 0;
        }

        /// <summary>
        /// Searches through all paths to find one starting with the search string, case sensitive
        /// </summary>
        /// <param name="searchString">The string to search</param>
        /// <param name="foundPathIndex">The first path index that began with the search string. This index corresponds to a bone index.</param>
        /// <returns>Returns true if a match was found</returns>
        public bool TryGetFirstPathIndexThatStartsWith<T>(in T searchString, out int foundPathIndex) where T : unmanaged, INativeList<byte>, IUTF8Bytes
        {
            for (foundPathIndex = 0; foundPathIndex < pathsInReversedNotation.Length; foundPathIndex++)
            {
                if (StartsWith(foundPathIndex, in searchString))
                    return true;
            }
            foundPathIndex = -1;
            return false;
        }
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
    /// Lives on the Skeleton Root. All WorldTransform values will be used as
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
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        public EntityWith<WorldTransform> bone;
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        public EntityWith<LocalToWorld> bone;
#endif
    }

    /// <summary>
    /// An optional flag component which specifies if the bone entities
    /// need to be resynced with the BoneReference buffer
    /// Usage: If a skeleton has this component and its enabled,
    /// it will synchronize its skeleton with all the bones in the buffer,
    /// populating the BoneIndex, removing old bones from culling, and
    /// allowing new bones to report culling.
    /// This happens during SkeletonMeshBindingReactiveSystem and is only
    /// required if you modify the BoneReference buffer after that system
    /// has ran on the skeleton entity once.
    /// </summary>
    public struct BoneReferenceIsDirtyFlag : IComponentData, IEnableableComponent
    {
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
        /// The indices of each bone's children. If the bone does not have children,
        /// The inner blob array at the bone's index is empty.
        /// A maximum of 32767 bones is supported.
        /// </summary>
        public BlobArray<BlobArray<short> > childrenIndices;
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
    /// The bone transforms of an optimized skeleton. Each bone has 3 sets of two transforms,
    /// for a total of 6 transforms. In each set, there is a local space and root space transform.
    /// The three sets provide the current frame, previous frame, and 2 frames prior transforms.
    /// The roles of the three sets are rotated every frame. The sequence of transforms is as follows:
    /// - All setA root transforms
    /// - All setA local transforms
    /// - All setB root transforms
    /// - All setB local transforms
    /// - All setC root transforms
    /// - All setC local transforms
    /// Usage: Prefer to use OptimizedSkeletonAspect instead of this component directly.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct OptimizedBoneTransform : IBufferElementData
    {
        public TransformQvvs boneTransform;
    }

    /// <summary>
    /// The control state for an optimized skeleton. This component keeps track of the transform set rotations,
    /// whether or not anything interacted with the pose during the frame, and animation sampling and blending
    /// statuses.
    /// Usage: Prefer to use OptimizedSkeletonAspect instead of this component directly.
    /// </summary>
    public struct OptimizedSkeletonState : IComponentData
    {
        public enum Flags : byte
        {
            RotationMask = 0x07,
            WasPreviousDirty = 0x04,
            NeedsHistorySync = 0x10,
            IsDirty = 0x20,
            NextSampleShouldAdd = 0x40,
            NeedsSync = 0x80
        }

        public Flags state;

        // mask & 3 == current write set
        // mask & 4 == wasPreviousDirty
        // mask != 3
        internal static readonly int[] CurrentFromMask  = { 0, 1, 2, 0, 0, 1, 2 };
        internal static readonly int[] PreviousFromMask = { 2, 0, 1, 2, 2, 0, 1};
        internal static readonly int[] TwoAgoFromMask   = { 2, 0, 1, 1, 1, 2, 0};
    }

    /// <summary>
    /// Describes an exported bone entity which should inherit the root transform
    /// of an optimized bone as the the entity's LocalTransform.
    /// Usage: Add to an entity to make it track a bone in an optimized skeleton.
    /// The exported bone should be parented to the skeleton entity.
    /// </summary>
    public struct CopyLocalToParentFromBone : IComponentData
    {
        public short boneIndex;
    }
    #endregion
}

