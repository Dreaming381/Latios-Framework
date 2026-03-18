using System;
using System.Collections.Generic;
using System.Diagnostics;
using Latios.Transforms;
using Latios.Unsafe;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    internal struct OptimizedSkeletonWorldTransform
    {
#if LATIOS_TRANSFORMS_UNITY
        public TransformQvvs worldTransform;
        public bool requiresSocketPropagation => false;
        public OptimizedSkeletonWorldTransform(Unity.Transforms.LocalToWorld localToWorld)
        {
            var ltw = localToWorld.Value;
            worldTransform = new TransformQvvs(ltw.Translation(), ltw.Rotation(), ltw.Scale().x, 1f);
        }
#else
        public TransformAspect transformAspect;
        public TransformQvvs worldTransform => transformAspect.worldTransform;
        public bool requiresSocketPropagation => true;
        public OptimizedSkeletonWorldTransform(TransformAspect ta)
        {
            transformAspect = ta;
        }
#endif
    }

    public partial struct OptimizedSkeletonAspect
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
                var context32     = rootTransforms[i].context32;
                var temp          = qvvs.mul(rootTransforms[parent], in local);
                temp.context32    = context32;
                rootTransforms[i] = temp;
            }
            {
                var local = localTransforms[0];
                local.NormalizeBone();
                localTransforms[0] = local;
                rootTransforms[0]  = local;
            }
#if !LATIOS_TRANSFORMS_UNITY
            // Assuming a 1 MB stack size, then the max struct size for the max bone count we can fit is 32 bytes (actually a little less in practice).
            // Batch commands are bigger than that, so we have to use the ThreadStackAllocator.
            var tsa             = ThreadStackAllocator.GetAllocator();
            var commands        = tsa.AllocateAsSpan<TransformBatchWriteCommand>(m_socketCount);
            int commandsWritten = 0;
            for (int i = 1; i < boneCount; i++)
            {
                if (rootTransforms[i].context32 >= 0)
                {
                    var rootTransform = rootTransforms[i];
                    var socketHandle          = m_worldTransform.transformAspect.entityInHierarchyHandle.GetFromIndexInHierarchy(rootTransform.context32);
                    var socketTransform       = m_worldTransform.transformAspect[socketHandle];
                    rootTransform.context32   = socketTransform.worldTransform.context32;
                    commands[commandsWritten] = TransformBatchWriteCommand.SetLocalTransformQvvs(socketTransform, in rootTransform);
                    commandsWritten++;
                }
            }
            commands = commands.Slice(0, commandsWritten);
            commands.ApplyTransforms();
            tsa.Dispose();
#endif

            m_skeletonState.ValueRW.state &= ~(OptimizedSkeletonState.Flags.NeedsSync | OptimizedSkeletonState.Flags.NextSampleShouldAdd);
            m_skeletonState.ValueRW.state |= OptimizedSkeletonState.Flags.IsDirty;
            SyncHistory();
        }

        unsafe void InitWeights(NativeArray<TransformQvvs> bones)
        {
            var ptr = (TransformQvvs*)bones.GetUnsafePtr();
            for (int i = 0; i < bones.Length; i++)
                ptr[i].context32 = math.asint(1f);
        }
    }

    public partial struct OptimizedBone
    {
        internal OptimizedSkeletonWorldTransform                m_skeletonWorldTransform;
        internal RefRO<OptimizedSkeletonHierarchyBlobReference> m_skeletonHierarchyBlobRef;
        internal RefRW<OptimizedSkeletonState>                  m_skeletonState;
        internal DynamicBuffer<OptimizedBoneTransform>          m_boneTransforms;
        internal short                                          m_index;
        internal short                                          m_boneCount;
        internal short                                          m_socketCount;

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
        TransformQvvs m_skeletonWorldQvvs => m_skeletonWorldTransform.worldTransform;

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
                    m_queue[m_count + m_next + i] = new ParentChild { parent = parent, child = children[i] };
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

        ref struct SocketUpdater
        {
#if !LATIOS_TRANSFORMS_UNITY
            TransformAspect                  referenceTransformAspect;
            ThreadStackAllocator             tsa;
            Span<TransformBatchWriteCommand> commands;
            int                              commandCount;
#endif

            public SocketUpdater(OptimizedSkeletonWorldTransform skeletonWorldTransform, int socketCount)
            {
#if !LATIOS_TRANSFORMS_UNITY
                referenceTransformAspect = skeletonWorldTransform.transformAspect;
                tsa                      = ThreadStackAllocator.GetAllocator();
                commands                 = tsa.AllocateAsSpan<TransformBatchWriteCommand>(socketCount);
                commandCount             = 0;
#endif
            }

            public void ApplyAndDispose()
            {
#if !LATIOS_TRANSFORMS_UNITY
                commands.ApplyTransforms();
                tsa.Dispose();
#endif
            }

            public void SetTransform(int socketTransformHierarchyIndex, TransformQvvs transform)
            {
#if !LATIOS_TRANSFORMS_UNITY
                if (socketTransformHierarchyIndex < 0)
                    return;
                var handle             = referenceTransformAspect.entityInHierarchyHandle.GetFromIndexInHierarchy(socketTransformHierarchyIndex);
                var socket             = referenceTransformAspect[handle];
                transform.context32    = socket.context32;
                commands[commandCount] = TransformBatchWriteCommand.SetLocalTransformQvvs(socket, in transform);
#endif
            }

            public void SetPosition(int socketTransformHierarchyIndex, float3 position)
            {
#if !LATIOS_TRANSFORMS_UNITY
                if (socketTransformHierarchyIndex < 0)
                    return;
                var handle             = referenceTransformAspect.entityInHierarchyHandle.GetFromIndexInHierarchy(socketTransformHierarchyIndex);
                var socket             = referenceTransformAspect[handle];
                commands[commandCount] = TransformBatchWriteCommand.SetLocalPosition(socket, position);
#endif
            }

            public void SetRotation(int socketTransformHierarchyIndex, quaternion rotation)
            {
#if !LATIOS_TRANSFORMS_UNITY
                if (socketTransformHierarchyIndex < 0)
                    return;
                var handle             = referenceTransformAspect.entityInHierarchyHandle.GetFromIndexInHierarchy(socketTransformHierarchyIndex);
                var socket             = referenceTransformAspect[handle];
                commands[commandCount] = TransformBatchWriteCommand.SetLocalRotation(socket, rotation);
#endif
            }

            public void SetPositionRotation(int socketTransformHierarchyIndex, float3 position, quaternion rotation)
            {
#if !LATIOS_TRANSFORMS_UNITY
                if (socketTransformHierarchyIndex < 0)
                    return;
                var handle = referenceTransformAspect.entityInHierarchyHandle.GetFromIndexInHierarchy(socketTransformHierarchyIndex);
                var socket = referenceTransformAspect[handle];
                // Todo: Switch to explicit position-rotation command when that is supported.
                var localScale         = socket.localScale;
                commands[commandCount] = TransformBatchWriteCommand.SetLocalTransform(socket, new TransformQvs(position, rotation, localScale));
#endif
            }

            public void SetScale(int socketTransformHierarchyIndex, float scale)
            {
#if !LATIOS_TRANSFORMS_UNITY
                if (socketTransformHierarchyIndex < 0)
                    return;
                var handle             = referenceTransformAspect.entityInHierarchyHandle.GetFromIndexInHierarchy(socketTransformHierarchyIndex);
                var socket             = referenceTransformAspect[handle];
                commands[commandCount] = TransformBatchWriteCommand.SetLocalScale(socket, scale);
#endif
            }

            public void SetPositionScale(int socketTransformHierarchyIndex, float3 position, float scale)
            {
#if !LATIOS_TRANSFORMS_UNITY
                if (socketTransformHierarchyIndex < 0)
                    return;
                var handle = referenceTransformAspect.entityInHierarchyHandle.GetFromIndexInHierarchy(socketTransformHierarchyIndex);
                var socket = referenceTransformAspect[handle];
                // Todo: Switch to explicit position-scale command when that is supported.
                var localRotation      = socket.localRotation;
                commands[commandCount] = TransformBatchWriteCommand.SetLocalTransform(socket, new TransformQvs(position, localRotation, scale));
#endif
            }

            public void SetStretch(int socketTransformHierarchyIndex, float3 stretch)
            {
#if !LATIOS_TRANSFORMS_UNITY
                if (socketTransformHierarchyIndex < 0)
                    return;
                var handle             = referenceTransformAspect.entityInHierarchyHandle.GetFromIndexInHierarchy(socketTransformHierarchyIndex);
                var socket             = referenceTransformAspect[handle];
                commands[commandCount] = TransformBatchWriteCommand.SetStretch(socket, stretch);
#endif
            }
        }

        void PropagatePositionChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, ref SocketUpdater socketUpdater, int currentRootBaseIndex,
                                               int changedBoneIndex)
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
                socketUpdater.SetPosition(childRoot.boneTransform.context32, childRoot.boneTransform.position);
                queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
            }
        }

        void PropagateRotationChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, ref SocketUpdater socketUpdater, int currentRootBaseIndex,
                                               int changedBoneIndex)
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
                socketUpdater.SetPositionRotation(childRoot.boneTransform.context32, childRoot.boneTransform.position, childRoot.boneTransform.rotation);
                queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
            }
        }

        void PropagateScaleChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, ref SocketUpdater socketUpdater, int currentRootBaseIndex, int changedBoneIndex)
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
                socketUpdater.SetPositionScale(childRoot.boneTransform.context32, childRoot.boneTransform.position, childRoot.boneTransform.scale);
                queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
            }
        }

        void PropagateStretchChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob, ref SocketUpdater socketUpdater, int currentRootBaseIndex, int changedBoneIndex)
        {
            // Stretch affects children root positions, so that is all we have to update.
            PropagatePositionChangeToChildren(ref childrenIndicesBlob, ref socketUpdater, currentRootBaseIndex, changedBoneIndex);
        }

        void PropagateTransformChangeToChildren(ref BlobArray<BlobArray<short> > childrenIndicesBlob,
                                                ref SocketUpdater socketUpdater,
                                                int currentRootBaseIndex,
                                                int changedBoneIndex)
        {
            var queue = new PropagationQueue(stackalloc ParentChild[childrenIndicesBlob.Length - changedBoneIndex]);
            queue.EnqueueChildren(changedBoneIndex, ref childrenIndicesBlob);
            var bones = m_boneTransforms.AsNativeArray().AsSpan();
            while (queue.TryDequeue(out var parentChild))
            {
                ref var childLocal                = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                ref var childRoot                 = ref bones[currentRootBaseIndex + parentChild.child];
                ref var parentRoot                = ref bones[currentRootBaseIndex + parentChild.parent];
                var     context32                 = childRoot.boneTransform.context32;
                childRoot.boneTransform           = qvvs.mul(in parentRoot.boneTransform, childLocal.boneTransform);
                childRoot.boneTransform.context32 = context32;
                socketUpdater.SetTransform(childRoot.boneTransform.context32, childRoot.boneTransform);
                queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
            }
        }

        internal void ApplyRootTransformChanges(Span<TransformQvvs> rootTransformsWithIndices, int currentRootBaseIndex)
        {
            rootTransformsWithIndices.Sort(new TransformWithIndicesComparer());
            var     firstChangedBoneIndex = rootTransformsWithIndices[0].context32;
            ref var childrenIndicesBlob   = ref m_allChildrenIndices;
            ref var parentIndicesBlob     = ref m_skeletonHierarchyBlobRef.ValueRO.blob.Value.parentIndices;
            var     queue                 = new PropagationQueue(stackalloc ParentChild[childrenIndicesBlob.Length - firstChangedBoneIndex]);
            int     transformSpanIndex    = 0;
            var     bones                 = m_boneTransforms.AsNativeArray().AsSpan();
            var     socketUpdater         = new SocketUpdater(m_skeletonWorldTransform, m_socketCount);
            while (true)
            {
                var hasDirtyTransform = transformSpanIndex < rootTransformsWithIndices.Length;
                var hasNextChild      = queue.TryDequeue(out var parentChild);
                if (!hasDirtyTransform && !hasNextChild)
                    break;

                var nextTransformBoneIndex = hasDirtyTransform ? rootTransformsWithIndices[transformSpanIndex].context32 : childrenIndicesBlob.Length;
                var nextChildIndex         = math.select(childrenIndicesBlob.Length, parentChild.child, hasNextChild);
                if (nextChildIndex < nextTransformBoneIndex)
                {
                    // Do normal propagate from parent
                    ref var childLocal                = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                    ref var childRoot                 = ref bones[currentRootBaseIndex + parentChild.child];
                    ref var parentRoot                = ref bones[currentRootBaseIndex + parentChild.parent];
                    var     context32                 = childRoot.boneTransform.context32;
                    childRoot.boneTransform           = qvvs.mul(in parentRoot.boneTransform, childLocal.boneTransform);
                    childRoot.boneTransform.context32 = context32;
                    socketUpdater.SetTransform(childRoot.boneTransform.context32, childRoot.boneTransform);
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
                        ref var childLocal                 = ref bones[currentRootBaseIndex + m_boneCount + nextTransformBoneIndex];
                        ref var childRoot                  = ref bones[currentRootBaseIndex + nextTransformBoneIndex];
                        ref var parentRoot                 = ref bones[currentRootBaseIndex + parentIndicesBlob[nextTransformBoneIndex]];
                        var     context32                  = childRoot.boneTransform.context32;
                        childRoot.boneTransform            = rootTransformsWithIndices[nextTransformBoneIndex];
                        childRoot.boneTransform.context32  = context32;
                        childLocal.boneTransform           = qvvs.inversemulqvvs(in parentRoot.boneTransform, in childRoot.boneTransform);
                        childLocal.boneTransform.context32 = math.asint(1f);
                        socketUpdater.SetTransform(childRoot.boneTransform.context32, childRoot.boneTransform);
                        queue.EnqueueChildren(nextTransformBoneIndex, ref childrenIndicesBlob);
                    }
                    transformSpanIndex++;
                }
                else
                {
                    // The changed bone is the same as the next propagated child
                    ref var childLocal                 = ref bones[currentRootBaseIndex + m_boneCount + parentChild.child];
                    ref var childRoot                  = ref bones[currentRootBaseIndex + parentChild.child];
                    ref var parentRoot                 = ref bones[currentRootBaseIndex + parentChild.parent];
                    var     context32                  = childRoot.boneTransform.context32;
                    childRoot.boneTransform            = rootTransformsWithIndices[nextTransformBoneIndex];
                    childRoot.boneTransform.context32  = context32;
                    childLocal.boneTransform           = qvvs.inversemulqvvs(in parentRoot.boneTransform, in childRoot.boneTransform);
                    childLocal.boneTransform.context32 = math.asint(1f);
                    socketUpdater.SetTransform(childRoot.boneTransform.context32, childRoot.boneTransform);
                    queue.EnqueueChildren(parentChild.child, ref childrenIndicesBlob);
                    transformSpanIndex++;
                }
            }
        }

        struct TransformWithIndicesComparer : IComparer<TransformQvvs>
        {
            public int Compare(TransformQvvs x, TransformQvvs y) => x.context32.CompareTo(y.context32);
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

