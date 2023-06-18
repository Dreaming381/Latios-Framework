#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using System;
using System.Diagnostics;
using Latios.Transforms;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    public readonly partial struct OptimizedSkeletonAspect : IAspect
    {
        readonly RefRO<WorldTransform>                          m_worldTransform;
        readonly RefRO<OptimizedSkeletonHierarchyBlobReference> m_skeletonHierarchyBlobRef;
        readonly RefRW<OptimizedSkeletonState>                  m_skeletonState;
        readonly DynamicBuffer<OptimizedBoneTransform>          m_boneTransforms;
        readonly DynamicBuffer<OptimizedBoneInertialBlendState> m_bonesInertialBlendStates;

        #region ReadOnly Properties
        /// <summary>
        /// The skeleton's world transform. An optimized skeleton stores its bones in local and root space,
        /// so to get the world-space transforms of the bones, this component's value is used.
        /// </summary>
        public ref readonly TransformQvvs skeletonWorldTransform => ref m_worldTransform.ValueRO.worldTransform;
        /// <summary>
        /// The blob value of the Optimized skeleton hierarchy
        /// </summary>
        public ref OptimizedSkeletonHierarchyBlob skeletonHierarchyBlob => ref m_skeletonHierarchyBlobRef.ValueRO.blob.Value;
        /// <summary>
        /// The number of bones in the skeleton
        /// </summary>
        public int boneCount => m_boneTransforms.Length / 6;
        /// <summary>
        /// Access to the array of bone handles for bones in the skeleton.
        /// Transform data for the bone at index 0 contains root-motion sampled data.
        /// Its children bones ignore its transform state.
        /// </summary>
        public OptimizedBoneInSkeletonArray bones => new OptimizedBoneInSkeletonArray
        {
            m_skeletonWorldTransform   = m_worldTransform,
            m_skeletonHierarchyBlobRef = m_skeletonHierarchyBlobRef,
            m_skeletonState            = m_skeletonState,
            m_boneTransforms           = m_boneTransforms,
            m_boneCount                = (short)boneCount
        };

        /// <summary>
        /// True if any of the bones have been modified this tick
        /// </summary>
        public bool isDirty => (m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.IsDirty) == OptimizedSkeletonState.Flags.IsDirty;
        /// <summary>
        /// True if any of the bones had been modified the previous tick
        /// </summary>
        public bool wasDirtyPreviousTick => (m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.WasPreviousDirty) == OptimizedSkeletonState.Flags.WasPreviousDirty;
        /// <summary>
        /// True if at least one pose has been sampled since the last sync
        /// </summary>
        public bool needsSync => (m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.NeedsSync) == OptimizedSkeletonState.Flags.NeedsSync;
        /// <summary>
        /// True if the motion history needs to be synced. This may happen if the entity was
        /// just instantiated and had no initial pose in the prefab or subscene.
        /// To clear this, call EndSamplingAndSync or SyncHistory.
        /// </summary>
        public bool needsHistorySync => (m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.NeedsHistorySync) == OptimizedSkeletonState.Flags.NeedsHistorySync;

        /// <summary>
        /// Retrieves a read-only NativeArray containing the current frame's root-space transforms.
        /// If isDirty is false, the contents of this array are undefined.
        /// To ensure the contents are valid, call CopyFromPrevious() if isDirty is false.
        /// </summary>
        public NativeArray<TransformQvvs>.ReadOnly rawRootTransforms => m_boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray().GetSubArray(m_currentBaseRootIndexWrite,
                                                                                                                                                  boneCount).AsReadOnly();
        /// <summary>
        /// Retrieves a read-only NativeArray containing the current frame's local-space transforms.
        /// If isDirty is false, the contents of this array are undefined.
        /// To ensure the contents are valid, call CopyFromPrevious() if isDirty is false.
        /// </summary>
        public NativeArray<TransformQvvs>.ReadOnly rawLocalTransformsRO => m_boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray().GetSubArray(
            m_currentBaseRootIndexWrite + boneCount,
            boneCount).AsReadOnly();
        #endregion

        #region Read/Write Properties
        /// <summary>
        /// Retrieves write access to the NativeArray containing the current frame's local-space transforms.
        /// If isDirty is false prior to calling this method, the contents of this array are undefined.
        /// To ensure the contents are valid, call CopyFromPrevious() if isDirty was false.
        /// This method sets isDirty and needsSync to true. Root-space transforms are not automatically synced
        /// when you make changes to this array. You must call EndSamplingAndSync() when you are done making changes.
        /// If you want the hierarchy to remain in sync at all times, iterate through the bones property instead.
        /// </summary>
        public NativeArray<TransformQvvs> rawLocalTransformsRW
        {
            get
            {
                m_skeletonState.ValueRW.state |= OptimizedSkeletonState.Flags.IsDirty | OptimizedSkeletonState.Flags.NeedsSync;
                return m_boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray().GetSubArray(m_currentBaseRootIndexWrite + boneCount, boneCount);
            }
        }
        #endregion

        #region Modification Methods
        /// <summary>
        /// Begins a new inertial blend for the entire skeleton. You may call this prior to syncing if you wish.
        /// However, needsHistorySync must be <see langword="false"/> or else results will be unusable.
        /// </summary>
        /// <param name="previousDeltaTime">The previous delta time used for the previous animation sample.</param>
        /// <param name="maxBlendDurationStartingFromTimeOfPrevious">The maximum blend time, measured from when the previous sample was recorded.</param>
        public void StartNewInertialBlend(float previousDeltaTime, float maxBlendDurationStartingFromTimeOfPrevious)
        {
            StartInertialBlendInternal(previousDeltaTime, maxBlendDurationStartingFromTimeOfPrevious);
        }

        /// <summary>
        /// Performs inertial blending for the entire skeleton. You may call this prior to syncing if you wish.
        /// </summary>
        /// <param name="timeSinceStartOfBlend">The time since the start of the blend, which was the sample before
        /// StartNewInertialBlend was called.</param>
        public void InertialBlend(float timeSinceStartOfBlend)
        {
            InertialBlendInternal(timeSinceStartOfBlend);
        }

        /// <summary>
        /// After sampling poses via 1 or more calls to SkeletonClip.SamplePose(),
        /// call this method to make transforms valid and accessible via OptimizedBone handles.
        /// This implicitly calls SyncHistory() as well.
        /// </summary>
        public void EndSamplingAndSync()
        {
            if (needsSync)
                Sync();
        }

        /// <summary>
        /// On the first frame, if the initial pose was erased during baking,
        /// you may need to call this method for motion history to be correct.
        /// This should be called after setting the initial pose.
        /// </summary>
        public void SyncHistory()
        {
            if (needsHistorySync)
            {
                m_skeletonState.ValueRW.state &= ~OptimizedSkeletonState.Flags.NeedsHistorySync;
                if (m_currentBaseRootIndexWrite != 0)
                {
                    // The buffers have already been rotated once. Nothing to sync.
                }
                else
                {
                    var requiredBones = boneCount;
                    var array         = m_boneTransforms.AsNativeArray();
                    array.GetSubArray(requiredBones * 2, requiredBones * 2).CopyFrom(array.GetSubArray(0, requiredBones * 2));
                    array.GetSubArray(requiredBones * 4, requiredBones * 2).CopyFrom(array.GetSubArray(0, requiredBones * 2));
                }
            }
        }

        /// <summary>
        /// Copies the tick's initial transforms into the current pose buffer.
        /// </summary>
        public void CopyFromPrevious()
        {
            var array = m_boneTransforms.AsNativeArray();
            array.GetSubArray(m_currentBaseRootIndexWrite, boneCount * 2).CopyFrom(array.GetSubArray(m_previousBaseRootIndex, boneCount * 2));
            ref var state  = ref m_skeletonState.ValueRW.state;
            state         |= OptimizedSkeletonState.Flags.IsDirty;
            state         &= ~(OptimizedSkeletonState.Flags.NeedsSync | OptimizedSkeletonState.Flags.NextSampleShouldAdd);
        }

        /// <summary>
        /// Forces the skeleton to initialize into a valid expanded state.
        /// Prefabs are stored in a compressed state to reduce serialization and instantiation costs.
        /// Normally, skeletons are automatically expanded during MotionHistoryUpdateSuperSystem.
        /// However, if you created a skeleton after that point, you may need to perform the expansion
        /// using this method.
        /// </summary>
        public void ForceInitialize()
        {
            var requiredBones = m_skeletonHierarchyBlobRef.ValueRO.blob.Value.parentIndices.Length;
            if (requiredBones == boneCount)
                return;

            if (boneCount == requiredBones)
            {
                m_boneTransforms.Resize(requiredBones * 6, NativeArrayOptions.UninitializedMemory);
                var array = m_boneTransforms.AsNativeArray();
                array.GetSubArray(requiredBones, requiredBones).CopyFrom(array.GetSubArray(0, requiredBones));
                Sync(true);
                array.GetSubArray(requiredBones * 2, requiredBones * 2).CopyFrom(array.GetSubArray(0, requiredBones * 2));
                array.GetSubArray(requiredBones * 4, requiredBones * 2).CopyFrom(array.GetSubArray(0, requiredBones * 2));
            }
            else if (boneCount < requiredBones)
            {
                m_boneTransforms.Resize(requiredBones * 6, NativeArrayOptions.ClearMemory);
                m_skeletonState.ValueRW.state |= OptimizedSkeletonState.Flags.NeedsHistorySync;
            }
        }

        #endregion
    }

    /// <summary>
    /// An Optimized Bone handle that can be acquired from an OptimizedSkeletonAspect.
    /// It provides many useful utilities for interacting with the individual bones.
    /// </summary>
    public partial struct OptimizedBone
    {
        #region ReadOnly Properties
        /// <summary>
        /// The index of the bone in the skeleton
        /// </summary>
        public short index => m_index;
        /// <summary>
        /// The parent index of the bone in the skeleton. If the bone does not have a parent, -1 is returned.
        /// </summary>
        public short parentIndex => m_skeletonHierarchyBlobRef.ValueRO.blob.Value.parentIndices[m_index];
        /// <summary>
        /// The parent bone,
        /// </summary>
        public OptimizedBone parent
        {
            get
            {
                if (Hint.Unlikely(parentIndex < 0))
                    return default;

                var result     = this;
                result.m_index = parentIndex;
                return result;
            }
        }
        /// <summary>
        /// The number of children this bone has
        /// </summary>
        public int childCount => m_allChildrenIndices[m_index].Length;

        /// <summary>
        /// Access to the array of OptimizedBones which are immediate children of this bone
        /// </summary>
        public OptimizedBoneChildrenArray children => new OptimizedBoneChildrenArray
        {
            m_skeletonWorldTransform   = m_skeletonWorldTransform,
            m_skeletonHierarchyBlobRef = m_skeletonHierarchyBlobRef,
            m_skeletonState            = m_skeletonState,
            m_boneTransforms           = m_boneTransforms,
            m_index                    = m_index,
            m_boneCount                = m_boneCount,
        };

        /// <summary>
        /// An array of skeleton bone indices of all the immediate children of this bone
        /// </summary>
        public ref BlobArray<short> childrenIndices => ref m_allChildrenIndices[m_index];

        // Todo: More ReadOnly Properties

        #endregion

        #region Read/Write Properties
        /// <summary>
        /// The world-space position of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions are updated.
        /// </summary>
        public float3 worldPosition
        {
            get => qvvs.TransformPoint(in m_skeletonWorldQvvs, rootPosition);
            set => rootPosition = qvvs.InverseTransformPoint(in m_skeletonWorldQvvs, value);
        }

        /// <summary>
        /// The world-space rotation of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions and rotations are updated.
        /// </summary>
        public quaternion worldRotation
        {
            get => qvvs.TransformRotation(in m_skeletonWorldQvvs, rootRotation);
            set => rootRotation = qvvs.InverseTransformRotation(in m_skeletonWorldQvvs, value);
        }

        /// <summary>
        /// The world-space scale of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions and scales are updated.
        /// </summary>
        public float worldScale
        {
            get => qvvs.TransformScale(in m_skeletonWorldQvvs, rootScale);
            set => rootScale = qvvs.InverseTransformScale(in m_skeletonWorldQvvs, value);
        }

        /// <summary>
        /// The root-space position of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions are updated.
        /// </summary>
        public float3 rootPosition
        {
            get => m_boneTransforms[m_currentRootIndexRead].boneTransform.position;
            set
            {
                var     currentRootIndex    = m_currentRootIndexWrite;
                ref var root                = ref m_boneTransforms.ElementAt(currentRootIndex);
                root.boneTransform.position = value;
                ref var local               = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                if (Hint.Likely(TryGetParentRootTransformIgnoreRootBone(currentRootIndex, out var parentRootTransform)))
                    local.boneTransform.position = qvvs.InverseTransformPoint(in parentRootTransform, value);
                else
                    local.boneTransform.position = value;
                PropagatePositionChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }

        /// <summary>
        /// The root-space rotation of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions and rotations are updated.
        /// </summary>
        public quaternion rootRotation
        {
            get => m_boneTransforms[m_currentRootIndexRead].boneTransform.rotation;
            set
            {
                var     currentRootIndex    = m_currentRootIndexWrite;
                ref var root                = ref m_boneTransforms.ElementAt(currentRootIndex);
                root.boneTransform.rotation = value;
                ref var local               = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                if (Hint.Likely(TryGetParentRootTransformIgnoreRootBone(currentRootIndex, out var parentRootTransform)))
                    local.boneTransform.rotation = qvvs.InverseTransformRotation(in parentRootTransform, value);
                else
                    local.boneTransform.rotation = value;
                PropagatePositionChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }

        /// <summary>
        /// The root-space scale of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions and scales are updated.
        /// </summary>
        public float rootScale
        {
            get => m_boneTransforms[m_currentRootIndexRead].boneTransform.scale;
            set
            {
                var     currentRootIndex = m_currentRootIndexWrite;
                ref var root             = ref m_boneTransforms.ElementAt(currentRootIndex);
                root.boneTransform.scale = value;
                ref var local            = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                if (Hint.Likely(TryGetParentRootTransformIgnoreRootBone(currentRootIndex, out var parentRootTransform)))
                    local.boneTransform.scale = qvvs.InverseTransformScale(in parentRootTransform, value);
                else
                    local.boneTransform.scale = value;
                PropagatePositionChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }

        /// <summary>
        /// The local-space position of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions are updated.
        /// </summary>
        public float3 localPosition
        {
            get => m_boneTransforms[m_currentRootIndexRead + m_boneCount].boneTransform.position;
            set
            {
                var     currentRootIndex     = m_currentRootIndexWrite;
                ref var local                = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                local.boneTransform.position = value;
                ref var root                 = ref m_boneTransforms.ElementAt(currentRootIndex);
                if (Hint.Likely(TryGetParentRootTransformIgnoreRootBone(currentRootIndex, out var parentRootTransform)))
                    root.boneTransform.position = qvvs.TransformPoint(in parentRootTransform, value);
                else
                    root.boneTransform.position = value;
                PropagatePositionChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }

        /// <summary>
        /// The local-space rotation of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions and rotations are updated.
        /// </summary>
        public quaternion localRotation
        {
            get => m_boneTransforms[m_currentRootIndexRead + m_boneCount].boneTransform.rotation;
            set
            {
                var     currentRootIndex     = m_currentRootIndexWrite;
                ref var local                = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                local.boneTransform.rotation = value;
                ref var root                 = ref m_boneTransforms.ElementAt(currentRootIndex);
                if (Hint.Likely(TryGetParentRootTransformIgnoreRootBone(currentRootIndex, out var parentRootTransform)))
                    root.boneTransform.rotation = qvvs.TransformRotation(in parentRootTransform, value);
                else
                    root.boneTransform.rotation = value;
                PropagatePositionChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }

        /// <summary>
        /// The local-space scale of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions and scales are updated.
        /// </summary>
        public float localScale
        {
            get => m_boneTransforms[m_currentRootIndexRead + m_boneCount].boneTransform.scale;
            set
            {
                var     currentRootIndex  = m_currentRootIndexWrite;
                ref var local             = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                local.boneTransform.scale = value;
                ref var root              = ref m_boneTransforms.ElementAt(currentRootIndex);
                if (Hint.Likely(TryGetParentRootTransformIgnoreRootBone(currentRootIndex, out var parentRootTransform)))
                    root.boneTransform.scale = qvvs.TransformScale(in parentRootTransform, value);
                else
                    root.boneTransform.scale = value;
                PropagateScaleChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }

        /// <summary>
        /// The stretch of the bone in the bone's local space that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space positions are updated.
        /// </summary>
        public float3 stretch
        {
            get => m_boneTransforms[m_currentRootIndexRead].boneTransform.stretch;
            set
            {
                var     currentRootIndex    = m_currentRootIndexWrite;
                ref var local               = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                local.boneTransform.stretch = value;
                ref var root                = ref m_boneTransforms.ElementAt(currentRootIndex);
                root.boneTransform.stretch  = value;
                PropagateStretchChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }

        /// <summary>
        /// The world-space transform of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space transforms are updated.
        /// </summary>
        public TransformQvvs worldTransform
        {
            get => qvvs.mul(in m_skeletonWorldQvvs, rootTransform);
            set => rootTransform = qvvs.inversemulqvvs(in m_skeletonWorldQvvs, value);
        }

        /// <summary>
        /// The root-space transform of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space transforms are updated.
        /// </summary>
        public TransformQvvs rootTransform
        {
            get => m_boneTransforms[m_currentRootIndexRead].boneTransform;
            set
            {
                var     currentRootIndex = m_currentRootIndexWrite;
                ref var root             = ref m_boneTransforms.ElementAt(currentRootIndex);
                root.boneTransform       = value;
                ref var local            = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                if (Hint.Likely(TryGetParentRootTransformIgnoreRootBone(currentRootIndex, out var parentRootTransform)))
                    local.boneTransform = qvvs.inversemulqvvs(in parentRootTransform, value);
                else
                    local.boneTransform = value;
                PropagateTransformChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }

        /// <summary>
        /// The local-space transform of the bone that can be read or modified.
        /// If the bone is modified and has children, all descendent root-space transforms are updated.
        /// </summary>
        public TransformQvvs localTransform
        {
            get => m_boneTransforms[m_currentRootIndexRead + m_boneCount].boneTransform;
            set
            {
                var     currentRootIndex = m_currentRootIndexWrite;
                ref var local            = ref m_boneTransforms.ElementAt(currentRootIndex + m_boneCount);
                local.boneTransform      = value;
                ref var root             = ref m_boneTransforms.ElementAt(currentRootIndex);
                if (Hint.Likely(TryGetParentRootTransformIgnoreRootBone(currentRootIndex, out var parentRootTransform)))
                    root.boneTransform = qvvs.mul(in parentRootTransform, value);
                else
                    root.boneTransform = value;
                PropagateTransformChangeToChildren(ref m_allChildrenIndices, in root.boneTransform, currentRootIndex - m_index, m_index);
            }
        }
        #endregion

        // Todo: Modification Methods and ReadOnly Transformation Methods

        #region ReadOnly Properties
        /// <summary>
        /// The root-space transform of the bone from the previous frame
        /// </summary>
        public TransformQvvs previousRootTransform => m_boneTransforms[m_previousRootIndex].boneTransform;
        /// <summary>
        /// The local-space transform of the bone from the previous frame
        /// </summary>
        public TransformQvvs previousLocalTransform => m_boneTransforms[m_previousRootIndex + m_boneCount].boneTransform;
        /// <summary>
        /// The root-space transform of the bone from two frames ago
        /// </summary>
        public TransformQvvs twoAgoRootTransform => m_boneTransforms[m_previousRootIndex].boneTransform;
        /// <summary>
        /// The local-space transform of the bone from two frames ago
        /// </summary>
        public TransformQvvs twoAgoLocalTransform => m_boneTransforms[m_previousRootIndex + m_boneCount].boneTransform;
        #endregion
    }

    /// <summary>
    /// A handle to the array of children of a bone.
    /// </summary>
    public struct OptimizedBoneChildrenArray
    {
        internal RefRO<WorldTransform>                          m_skeletonWorldTransform;
        internal RefRO<OptimizedSkeletonHierarchyBlobReference> m_skeletonHierarchyBlobRef;
        internal RefRW<OptimizedSkeletonState>                  m_skeletonState;
        internal DynamicBuffer<OptimizedBoneTransform>          m_boneTransforms;
        internal short                                          m_index;
        internal short                                          m_boneCount;

        /// <summary>
        /// The number of children this bone has
        /// </summary>
        public int childCount => m_skeletonHierarchyBlobRef.ValueRO.blob.Value.childrenIndices[m_index].Length;

        /// <summary>
        /// Gets the bone handle for the childIndex'th child in the bone's child array.
        /// The number of children in the array can be queried via childCount.
        /// Note that the childIndex is usually not the same as the child bone's index in the skeleton.
        /// However, the child bone's index in the skeleton can be obtained from the returned handle.
        /// </summary>
        /// <param name="childIndex">The index in the child array of the bone that should be retrived</param>
        public OptimizedBone this[int childIndex]
        {
            get
            {
                return new OptimizedBone
                {
                    m_skeletonWorldTransform   = m_skeletonWorldTransform,
                    m_skeletonHierarchyBlobRef = m_skeletonHierarchyBlobRef,
                    m_skeletonState            = m_skeletonState,
                    m_boneTransforms           = m_boneTransforms,
                    m_boneCount                = m_boneCount,
                    m_index                    = m_skeletonHierarchyBlobRef.ValueRO.blob.Value.childrenIndices[m_index][childIndex]
                };
            }
        }
    }

    /// <summary>
    /// A handle to the array of bones in the skeleton.
    /// </summary>
    public struct OptimizedBoneInSkeletonArray
    {
        internal RefRO<WorldTransform>                          m_skeletonWorldTransform;
        internal RefRO<OptimizedSkeletonHierarchyBlobReference> m_skeletonHierarchyBlobRef;
        internal RefRW<OptimizedSkeletonState>                  m_skeletonState;
        internal DynamicBuffer<OptimizedBoneTransform>          m_boneTransforms;
        internal short                                          m_boneCount;

        /// <summary>
        /// Gets the bone handle for the bone at boneIndex in the skeleton.
        /// </summary>
        public OptimizedBone this[int boneIndex]
        {
            get
            {
                return new OptimizedBone
                {
                    m_skeletonWorldTransform   = m_skeletonWorldTransform,
                    m_skeletonHierarchyBlobRef = m_skeletonHierarchyBlobRef,
                    m_skeletonState            = m_skeletonState,
                    m_boneTransforms           = m_boneTransforms,
                    m_boneCount                = m_boneCount,
                    m_index                    = (short)boneIndex
                };
            }
        }
    }
}
#endif

