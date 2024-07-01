using System;
using System.Diagnostics;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    public readonly partial struct OptimizedSkeletonAspect
    {
        internal bool BeginSampleTrueIfAdditive(out NativeArray<AclUnity.Qvvs> targetLocalTransforms)
        {
            targetLocalTransforms          = m_boneTransforms.Reinterpret<AclUnity.Qvvs>().AsNativeArray().GetSubArray(m_currentBaseRootIndexWrite + boneCount, boneCount);
            var previousState              = m_skeletonState.ValueRO.state;
            m_skeletonState.ValueRW.state |= OptimizedSkeletonState.Flags.IsDirty | OptimizedSkeletonState.Flags.NeedsSync | OptimizedSkeletonState.Flags.NextSampleShouldAdd;
            return (previousState & OptimizedSkeletonState.Flags.NextSampleShouldAdd) ==
                   OptimizedSkeletonState.Flags.NextSampleShouldAdd;
        }

        int m_currentBaseRootIndexWrite
        {
            get
            {
                var mask = (byte)(m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.RotationMask);
                return OptimizedSkeletonState.CurrentFromMask[mask] * boneCount * 2;
            }
        }

        int m_previousBaseRootIndex
        {
            get
            {
                var mask = (byte)(m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.RotationMask);
                return OptimizedSkeletonState.PreviousFromMask[mask] * boneCount * 2;
            }
        }

        int m_twoAgoBaseRootIndex
        {
            get
            {
                var mask = (byte)(m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.RotationMask);
                return OptimizedSkeletonState.PreviousFromMask[mask] * boneCount * 2;
            }
        }

        unsafe void StartInertialBlendInternal(float previousDeltaTime, float maxBlendDuration)
        {
            var bufferAsArray  = m_boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray();
            var previousLocals = bufferAsArray.GetSubArray(m_previousBaseRootIndex + boneCount, boneCount);
            var twoAgoLocals   = bufferAsArray.GetSubArray(m_twoAgoBaseRootIndex + boneCount, boneCount);
            var currentLocals  = isDirty ? bufferAsArray.GetSubArray(m_currentBaseRootIndexWrite + boneCount, boneCount) : previousLocals;

            if (m_bonesInertialBlendStates.Length < boneCount)
            {
                for (int i = m_bonesInertialBlendStates.Length; i < boneCount; i++)
                {
                    m_bonesInertialBlendStates.Add(default);
                }
            }

            // We go unsafe here to avoid copying as these are large
            var inertialBlends = (InertialBlendingTransformState*)m_bonesInertialBlendStates.Reinterpret<InertialBlendingTransformState>().GetUnsafePtr();

            float rcpTime = math.rcp(previousDeltaTime);

            for (int i = 0; i < boneCount; i++)
            {
                var currentNormalized = currentLocals[i];
                currentNormalized.NormalizeBone();
                inertialBlends[i].StartNewBlend(in currentNormalized, previousLocals[i], twoAgoLocals[i], rcpTime, maxBlendDuration);
            }
        }

        unsafe void StartInertialBlendInternal(float previousDeltaTime, float maxBlendDuration, ReadOnlySpan<ulong> mask)
        {
            var bufferAsArray  = m_boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray();
            var previousLocals = bufferAsArray.GetSubArray(m_previousBaseRootIndex + boneCount, boneCount);
            var twoAgoLocals   = bufferAsArray.GetSubArray(m_twoAgoBaseRootIndex + boneCount, boneCount);
            var currentLocals  = isDirty ? bufferAsArray.GetSubArray(m_currentBaseRootIndexWrite + boneCount, boneCount) : previousLocals;

            if (m_bonesInertialBlendStates.Length < boneCount)
            {
                for (int i = m_bonesInertialBlendStates.Length; i < boneCount; i++)
                {
                    m_bonesInertialBlendStates.Add(default);
                }
            }

            // We go unsafe here to avoid copying as these are large
            var inertialBlends = (InertialBlendingTransformState*)m_bonesInertialBlendStates.Reinterpret<InertialBlendingTransformState>().GetUnsafePtr();

            float rcpTime = math.rcp(previousDeltaTime);

            for (int i = 0; i < boneCount; i++)
            {
                var batch = mask[i >> 6];
                var bit   = 1ul << (i & 0x3f);
                if ((batch & bit) == 0)
                    continue;

                var currentNormalized = currentLocals[i];
                currentNormalized.NormalizeBone();
                inertialBlends[i].StartNewBlend(in currentNormalized, previousLocals[i], twoAgoLocals[i], rcpTime, maxBlendDuration);
            }
        }

        unsafe void InertialBlendInternal(float timeSinceStartOfBlend)
        {
            var bufferAsArray = m_boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray();
            var currentLocals = bufferAsArray.GetSubArray(m_currentBaseRootIndexWrite + boneCount, boneCount);

            // We go unsafe here to avoid copying as these are large
            var inertialBlends = (InertialBlendingTransformState*)m_bonesInertialBlendStates.Reinterpret<InertialBlendingTransformState>().GetUnsafePtr();

            var blendTimes = new InertialBlendingTimingData(timeSinceStartOfBlend);

            if (needsSync)
            {
                var ptrs = (TransformQvvs*)currentLocals.GetUnsafePtr();
                for (int i = 0; i < currentLocals.Length; i++)
                {
                    ptrs[i].NormalizeBone();
                    inertialBlends[i].Blend(ref ptrs[i], in blendTimes);
                }
            }
            else if (isDirty)
            {
                var ptrs = (TransformQvvs*)currentLocals.GetUnsafePtr();
                for (int i = 0; i < currentLocals.Length; i++)
                {
                    inertialBlends[i].Blend(ref ptrs[i], in blendTimes);
                }
            }
            else
            {
                var previousLocals = bufferAsArray.GetSubArray(m_previousBaseRootIndex + boneCount, boneCount);
                for (int i = 0; i < currentLocals.Length; i++)
                {
                    var transform = previousLocals[i];
                    inertialBlends[i].Blend(ref transform, in blendTimes);
                    currentLocals[i] = transform;
                }
            }
            m_skeletonState.ValueRW.state |= OptimizedSkeletonState.Flags.IsDirty | OptimizedSkeletonState.Flags.NeedsSync;
            m_skeletonState.ValueRW.state &= ~OptimizedSkeletonState.Flags.NextSampleShouldAdd;
        }

        void Sync(bool forceSync0 = false)
        {
            ref var parentIndices   = ref m_skeletonHierarchyBlobRef.ValueRO.blob.Value.parentIndices;
            var     rootBase        = math.select(m_currentBaseRootIndexWrite, 0, forceSync0);
            var     bufferAsArray   = m_boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray();
            var     rootTransforms  = bufferAsArray.GetSubArray(rootBase, boneCount);
            var     localTransforms = bufferAsArray.GetSubArray(rootBase + boneCount, boneCount);
            rootTransforms[0]       = TransformQvvs.identity;
            for (int i = 1; i < boneCount; i++)
            {
                var parent = math.max(0, parentIndices[i]);
                var local  = localTransforms[i];
                local.NormalizeBone();
                rootTransforms[i] = qvvs.mul(rootTransforms[parent], in local);
            }
            {
                var local = localTransforms[0];
                local.NormalizeBone();
                localTransforms[0] = local;
                rootTransforms[0]  = local;
            }
            m_skeletonState.ValueRW.state &= ~(OptimizedSkeletonState.Flags.NeedsSync | OptimizedSkeletonState.Flags.NextSampleShouldAdd);
            m_skeletonState.ValueRW.state |= OptimizedSkeletonState.Flags.IsDirty;
            SyncHistory();
        }

        unsafe void InitWeights(NativeArray<TransformQvvs> bones)
        {
            var ptr = (TransformQvvs*)bones.GetUnsafePtr();
            for (int i = 0; i < bones.Length; i++)
                ptr[i].worldIndex = math.asint(1f);
        }
    }

    public partial struct OptimizedBone
    {
        internal WorldTransformReadOnlyAspect                   m_skeletonWorldTransform;
        internal RefRO<OptimizedSkeletonHierarchyBlobReference> m_skeletonHierarchyBlobRef;
        internal RefRW<OptimizedSkeletonState>                  m_skeletonState;
        internal DynamicBuffer<OptimizedBoneTransform>          m_boneTransforms;
        internal short                                          m_index;
        internal short                                          m_boneCount;

        bool m_isDirty
        {
            get => (m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.IsDirty) == OptimizedSkeletonState.Flags.IsDirty;
        }

        byte m_rotationMask => (byte)(m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.RotationMask);

        int m_currentRootIndexWrite
        {
            get
            {
                CheckSynced();
                var mask = m_rotationMask;
                if (Hint.Unlikely(!m_isDirty))
                {
                    // To avoid only having some bones written to at the current index, we copy the previous bone transforms over.
                    var setSize      = m_boneCount * 2;
                    var currentBase  = OptimizedSkeletonState.CurrentFromMask[mask] * setSize;
                    var previousBase = OptimizedSkeletonState.PreviousFromMask[mask] * setSize;
                    var array        = m_boneTransforms.AsNativeArray();
                    array.GetSubArray(currentBase, setSize).CopyFrom(array.GetSubArray(previousBase, setSize));
                    m_skeletonState.ValueRW.state |= OptimizedSkeletonState.Flags.IsDirty;
                }

                return OptimizedSkeletonState.CurrentFromMask[mask] * m_boneCount * 2 + m_index;
            }
        }
        int m_currentRootIndexRead
        {
            get
            {
                CheckSynced();
                var mask = m_rotationMask;
                return math.select(OptimizedSkeletonState.PreviousFromMask[mask], OptimizedSkeletonState.CurrentFromMask[mask], m_isDirty) * m_boneCount * 2 + m_index;
            }
        }

        int m_previousRootIndex => OptimizedSkeletonState.PreviousFromMask[m_rotationMask] * m_boneCount * 2 + m_index;

        int m_twoAgoRootIndex => OptimizedSkeletonState.TwoAgoFromMask[m_rotationMask] * m_boneCount * 2 + m_index;

        ref BlobArray<BlobArray<short> > m_allChildrenIndices => ref m_skeletonHierarchyBlobRef.ValueRO.blob.Value.childrenIndices;
        TransformQvvs m_skeletonWorldQvvs => m_skeletonWorldTransform.worldTransformQvvs;

        bool TryGetParentRootIndexIgnoreRootBone(int boneRootIndex, out int parentIndex)
        {
            parentIndex  = this.parentIndex;
            bool result  = parentIndex > 0;
            parentIndex += boneRootIndex - m_index;
            return result;
        }

        bool TryGetParentRootTransformIgnoreRootBone(int boneRootIndex, out TransformQvvs parentRootTransform)
        {
            if (Hint.Likely(TryGetParentRootIndexIgnoreRootBone(boneRootIndex, out var parentRootIndex)))
            {
                parentRootTransform = m_boneTransforms[parentRootIndex].boneTransform;
                return true;
            }
            parentRootTransform = default;
            return false;
        }

        void PropagatePositionChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, in TransformQvvs rootTransform, int currentRootBaseIndex, int changedBoneIndex)
        {
            ref var children = ref childrenIndicesBlob[changedBoneIndex];
            for (int i = 0; i < children.Length; i++)
            {
                var     childIndex          = children[i];
                ref var local               = ref m_boneTransforms.ElementAt(currentRootBaseIndex + m_boneCount + childIndex);
                ref var root                = ref m_boneTransforms.ElementAt(currentRootBaseIndex + childIndex);
                root.boneTransform.position = qvvs.TransformPoint(in rootTransform, local.boneTransform.position);
                PropagatePositionChangeToChildren(ref childrenIndicesBlob, in root.boneTransform, currentRootBaseIndex, childIndex);
            }
        }

        void PropagateRotationChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, in TransformQvvs rootTransform, int currentRootBaseIndex, int changedBoneIndex)
        {
            ref var children = ref childrenIndicesBlob[changedBoneIndex];
            for (int i = 0; i < children.Length; i++)
            {
                var     childIndex          = children[i];
                ref var local               = ref m_boneTransforms.ElementAt(currentRootBaseIndex + m_boneCount + childIndex);
                ref var root                = ref m_boneTransforms.ElementAt(currentRootBaseIndex + childIndex);
                root.boneTransform.position = qvvs.TransformPoint(in rootTransform, local.boneTransform.position);
                root.boneTransform.rotation = qvvs.TransformRotation(in rootTransform, local.boneTransform.rotation);
                PropagateRotationChangeToChildren(ref childrenIndicesBlob, in root.boneTransform, currentRootBaseIndex, childIndex);
            }
        }

        void PropagateScaleChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, in TransformQvvs rootTransform, int currentRootBaseIndex, int changedBoneIndex)
        {
            ref var children = ref childrenIndicesBlob[changedBoneIndex];
            for (int i = 0; i < children.Length; i++)
            {
                var     childIndex          = children[i];
                ref var local               = ref m_boneTransforms.ElementAt(currentRootBaseIndex + m_boneCount + childIndex);
                ref var root                = ref m_boneTransforms.ElementAt(currentRootBaseIndex + childIndex);
                root.boneTransform.position = qvvs.TransformPoint(in rootTransform, local.boneTransform.position);
                root.boneTransform.scale    = qvvs.TransformScale(in rootTransform, local.boneTransform.scale);
                PropagateScaleChangeToChildren(ref childrenIndicesBlob, in root.boneTransform, currentRootBaseIndex, childIndex);
            }
        }

        void PropagateStretchChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, in TransformQvvs rootTransform, int currentRootBaseIndex, int changedBoneIndex)
        {
            // Stretch affects children root positions, so that is all we have to update.
            PropagatePositionChangeToChildren(ref childrenIndicesBlob, in rootTransform, currentRootBaseIndex, changedBoneIndex);
        }

        void PropagateTransformChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, in TransformQvvs rootTransform, int currentRootBaseIndex,
                                                int changedBoneIndex)
        {
            ref var children = ref childrenIndicesBlob[changedBoneIndex];
            for (int i = 0; i < children.Length; i++)
            {
                var     childIndex = children[i];
                ref var local      = ref m_boneTransforms.ElementAt(currentRootBaseIndex + m_boneCount + childIndex);
                ref var root       = ref m_boneTransforms.ElementAt(currentRootBaseIndex + childIndex);
                root.boneTransform = qvvs.mul(in rootTransform, local.boneTransform);
                PropagateTransformChangeToChildren(ref childrenIndicesBlob, in root.boneTransform, currentRootBaseIndex, childIndex);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckSynced()
        {
            if ((m_skeletonState.ValueRO.state & OptimizedSkeletonState.Flags.NeedsSync) == OptimizedSkeletonState.Flags.NeedsSync)
            {
                throw new InvalidOperationException(
                    "Attempted to interact with an OptimizedBone while the OptimizedSkeleton has not been synchronized. Call Sync() on the OptimizedSkeletonAspect after sampling animation poses before attempting to read or write to the OptimizedBone.");
            }
        }
    }
}

