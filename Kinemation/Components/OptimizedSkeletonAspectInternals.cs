using System;
using System.Collections.Generic;
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
#if !LATIOS_DISABLE_ACL
        internal bool BeginSampleTrueIfAdditive(out NativeArray<AclUnity.Qvvs> targetLocalTransforms)
        {
            targetLocalTransforms          = m_boneTransforms.Reinterpret<AclUnity.Qvvs>().AsNativeArray().GetSubArray(m_currentBaseRootIndexWrite + boneCount, boneCount);
            var previousState              = m_skeletonState.ValueRO.state;
            m_skeletonState.ValueRW.state |= OptimizedSkeletonState.Flags.IsDirty | OptimizedSkeletonState.Flags.NeedsSync | OptimizedSkeletonState.Flags.NextSampleShouldAdd;
            return (previousState & OptimizedSkeletonState.Flags.NextSampleShouldAdd) ==
                   OptimizedSkeletonState.Flags.NextSampleShouldAdd;
        }
#endif

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

        unsafe bool IsFinishedWithInertialBlendInternal(float timesSinceStartOfBlend)
        {
            // We go unsafe here to avoid copying as these are large
            var inertialBlends = (InertialBlendingTransformState*)m_bonesInertialBlendStates.Reinterpret<InertialBlendingTransformState>().GetUnsafePtr();
            var count          = m_bonesInertialBlendStates.Length;
            for (int i = 0; i < count; i++)
            {
                if (inertialBlends[i].NeedsBlend(timesSinceStartOfBlend))
                    return false;
            }
            return true;
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

        struct ParentChild
        {
            public short parent;
            public short child;
        }

        ref struct PropagationQueue
        {
            Span<ParentChild> m_queue;
            int               m_next;
            int               m_count;

            public PropagationQueue(Span<ParentChild> buffer)
            {
                m_queue = buffer;
                m_next  = 0;
                m_count = 0;
            }

            public void EnqueueChildren(int parentIndex, ref BlobArray<BlobArray<short> > childrenIndicesBlob)
            {
                short   parent   = (short)parentIndex;
                ref var children = ref childrenIndicesBlob[parentIndex];
                for (int i = 0; i < children.Length; i++)
                {
                    m_queue[m_next + i] = new ParentChild { parent = parent, child = children[i] };
                }
                m_count += children.Length;
            }

            public bool TryDequeue(out ParentChild result)
            {
                if (m_count == 0)
                {
                    result = default;
                    return false;
                }

                result = m_queue[m_next];
                m_count--;
                m_next++;
                return true;
            }
        }

        void PropagatePositionChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, int currentRootBaseIndex, int changedBoneIndex)
        {
            var queue = new PropagationQueue(stackalloc ParentChild[childrenIndicesBlob.Length - changedBoneIndex]);
            queue.EnqueueChildren(changedBoneIndex, ref childrenIndicesBlob);
            var bones = m_boneTransforms.AsNativeArray().AsSpan();
            while (queue.TryDequeue(out var parentChild))
            {
                ref var childLocal               = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                ref var childRoot                = ref bones[currentRootBaseIndex + parentChild.child];
                ref var parentRoot               = ref bones[currentRootBaseIndex + parentChild.parent];
                childRoot.boneTransform.position = qvvs.TransformPoint(in parentRoot.boneTransform, childLocal.boneTransform.position);
                queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
            }
        }

        void PropagateRotationChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, int currentRootBaseIndex, int changedBoneIndex)
        {
            var queue = new PropagationQueue(stackalloc ParentChild[childrenIndicesBlob.Length - changedBoneIndex]);
            queue.EnqueueChildren(changedBoneIndex, ref childrenIndicesBlob);
            var bones = m_boneTransforms.AsNativeArray().AsSpan();
            while (queue.TryDequeue(out var parentChild))
            {
                ref var childLocal               = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                ref var childRoot                = ref bones[currentRootBaseIndex + parentChild.child];
                ref var parentRoot               = ref bones[currentRootBaseIndex + parentChild.parent];
                childRoot.boneTransform.position = qvvs.TransformPoint(in parentRoot.boneTransform, childLocal.boneTransform.position);
                childRoot.boneTransform.rotation = qvvs.TransformRotation(in parentRoot.boneTransform, childLocal.boneTransform.rotation);
                queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
            }
        }

        void PropagateScaleChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, int currentRootBaseIndex, int changedBoneIndex)
        {
            var queue = new PropagationQueue(stackalloc ParentChild[childrenIndicesBlob.Length - changedBoneIndex]);
            queue.EnqueueChildren(changedBoneIndex, ref childrenIndicesBlob);
            var bones = m_boneTransforms.AsNativeArray().AsSpan();
            while (queue.TryDequeue(out var parentChild))
            {
                ref var childLocal               = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                ref var childRoot                = ref bones[currentRootBaseIndex + parentChild.child];
                ref var parentRoot               = ref bones[currentRootBaseIndex + parentChild.parent];
                childRoot.boneTransform.position = qvvs.TransformPoint(in parentRoot.boneTransform, childLocal.boneTransform.position);
                childRoot.boneTransform.scale    = qvvs.TransformScale(in parentRoot.boneTransform, childLocal.boneTransform.scale);
                queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
            }
        }

        void PropagateStretchChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, int currentRootBaseIndex, int changedBoneIndex)
        {
            // Stretch affects children root positions, so that is all we have to update.
            PropagatePositionChangeToChildren(ref childrenIndicesBlob, currentRootBaseIndex, changedBoneIndex);
        }

        void PropagateTransformChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, int currentRootBaseIndex, int changedBoneIndex)
        {
            var queue = new PropagationQueue(stackalloc ParentChild[childrenIndicesBlob.Length - changedBoneIndex]);
            queue.EnqueueChildren(changedBoneIndex, ref childrenIndicesBlob);
            var bones = m_boneTransforms.AsNativeArray().AsSpan();
            while (queue.TryDequeue(out var parentChild))
            {
                ref var childLocal      = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                ref var childRoot       = ref bones[currentRootBaseIndex + parentChild.child];
                ref var parentRoot      = ref bones[currentRootBaseIndex + parentChild.parent];
                childRoot.boneTransform = qvvs.mul(in parentRoot.boneTransform, childLocal.boneTransform);
                queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
            }
        }

        internal void ApplyRootTransformChanges(Span<TransformQvvs> rootTransformsWithIndices, int currentRootBaseIndex)
        {
            rootTransformsWithIndices.Sort(new TransformWithIndicesComparer());
            var     firstChangedBoneIndex = rootTransformsWithIndices[0].worldIndex;
            ref var childrenIndicesBlob   = ref m_allChildrenIndices;
            ref var parentIndicesBlob     = ref m_skeletonHierarchyBlobRef.ValueRO.blob.Value.parentIndices;
            var     queue                 = new PropagationQueue(stackalloc ParentChild[childrenIndicesBlob.Length - firstChangedBoneIndex]);
            int     transformSpanIndex    = 0;
            var     bones                 = m_boneTransforms.AsNativeArray().AsSpan();
            while (true)
            {
                var hasDirtyTransform = transformSpanIndex < rootTransformsWithIndices.Length;
                var hasNextChild      = queue.TryDequeue(out var parentChild);
                if (!hasDirtyTransform && !hasNextChild)
                    return;

                var nextTransformBoneIndex = hasDirtyTransform ? rootTransformsWithIndices[transformSpanIndex].worldIndex : childrenIndicesBlob.Length;
                var nextChildIndex         = math.select(childrenIndicesBlob.Length, parentChild.child, hasNextChild);
                if (nextChildIndex < nextTransformBoneIndex)
                {
                    // Do normal propagate from parent
                    ref var childLocal      = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                    ref var childRoot       = ref bones[currentRootBaseIndex + parentChild.child];
                    ref var parentRoot      = ref bones[currentRootBaseIndex + parentChild.parent];
                    childRoot.boneTransform = qvvs.mul(in parentRoot.boneTransform, childLocal.boneTransform);
                    queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
                }
                else if (nextChildIndex > nextTransformBoneIndex)
                {
                    // We have encountered one of our changed bones.
                    // If it is the root, we need to not propagate.
                    if (nextTransformBoneIndex == 0)
                    {
                        bones[currentRootBaseIndex + m_boneCount].boneTransform = rootTransformsWithIndices[nextTransformBoneIndex];
                        bones[currentRootBaseIndex].boneTransform               = rootTransformsWithIndices[nextTransformBoneIndex];
                    }
                    else
                    {
                        // Recompute the local transform
                        ref var childLocal       = ref bones[currentRootBaseIndex + m_boneCount + nextTransformBoneIndex];
                        ref var childRoot        = ref bones[currentRootBaseIndex + nextTransformBoneIndex];
                        ref var parentRoot       = ref bones[currentRootBaseIndex + parentIndicesBlob[nextTransformBoneIndex]];
                        childRoot.boneTransform  = rootTransformsWithIndices[nextTransformBoneIndex];
                        childLocal.boneTransform = qvvs.inversemulqvvs(in parentRoot.boneTransform, in childRoot.boneTransform);
                        queue.EnqueueChildren(nextTransformBoneIndex, ref childrenIndicesBlob);
                    }
                    transformSpanIndex++;
                }
                else
                {
                    // The changed bone is the same as the next propagated child
                    ref var childLocal       = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                    ref var childRoot        = ref bones[currentRootBaseIndex + parentChild.child];
                    ref var parentRoot       = ref bones[currentRootBaseIndex + parentChild.parent];
                    childRoot.boneTransform  = rootTransformsWithIndices[nextTransformBoneIndex];
                    childLocal.boneTransform = qvvs.inversemulqvvs(in parentRoot.boneTransform, in childRoot.boneTransform);
                    queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
                    transformSpanIndex++;
                }
            }
        }

        struct TransformWithIndicesComparer : IComparer<TransformQvvs>
        {
            public int Compare(TransformQvvs x, TransformQvvs y) => x.worldIndex.CompareTo(y.worldIndex);
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

