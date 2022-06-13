using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// Todo: A lot of this code is written naively. Is it's impact worth optimizing?
namespace Latios.Kinemation
{
    internal class ComputeBufferTrackingPool
    {
        PersistentPool m_lbsMatsBufferPool;
        PersistentPool m_deformBufferPool;
        PersistentPool m_meshVerticesPool;
        PersistentPool m_meshWeightsPool;
        PersistentPool m_meshBindPosesPool;
        PersistentPool m_boneOffsetsPool;

        PerFramePool m_meshVerticesUploadPool;
        PerFramePool m_meshWeightsUploadPool;
        PerFramePool m_meshBindPosesUploadPool;
        PerFramePool m_boneOffsetsUploadPool;
        PerFramePool m_bonesPool;
        PerFramePool m_skinningMetaPool;
        PerFramePool m_uploadMetaPool;

        FencePool m_fencePool;

        ComputeShader m_copyVerticesShader;
        ComputeShader m_copyMatricesShader;
        //ComputeShader m_copyShortIndicesShader;
        ComputeShader m_copyByteAddressShader;

        List<BufferQueuedForDestruction> m_destructionQueue;

        const int kMinMeshVerticesUploadSize  = 16 * 1024;
        const int kMinMeshWeightsUploadSize   = 4 * 16 * 1024;
        const int kMinMeshBindPosesUploadSize = 256;
        const int kMinBoneOffsetsUploadSize   = 128;
        const int kMinBonesSize               = 128 * 128;
        const int kMinSkinningMetaSize        = 128;
        const int kMinUploadMetaSize          = 128;

        public ComputeBufferTrackingPool()
        {
            m_destructionQueue      = new List<BufferQueuedForDestruction>();
            m_copyVerticesShader    = Resources.Load<ComputeShader>("CopyVertices");
            m_copyMatricesShader    = Resources.Load<ComputeShader>("CopyMatrices");
            m_copyByteAddressShader = Resources.Load<ComputeShader>("CopyBytes");

            m_lbsMatsBufferPool = new PersistentPool(1024, 3 * 4 * 4, ComputeBufferType.Structured, m_copyMatricesShader, m_destructionQueue);
            m_deformBufferPool  = new PersistentPool(256 * 1024, 3 * 3 * 4, ComputeBufferType.Structured, m_copyVerticesShader, m_destructionQueue);
            m_meshVerticesPool  = new PersistentPool(64 * 1024, 3 * 3 * 4, ComputeBufferType.Structured, m_copyVerticesShader, m_destructionQueue);
            m_meshWeightsPool   = new PersistentPool(2 * 4 * 64 * 1024, 4, ComputeBufferType.Raw, m_copyByteAddressShader, m_destructionQueue);
            m_meshBindPosesPool = new PersistentPool(1024, 3 * 4 * 4, ComputeBufferType.Structured, m_copyMatricesShader, m_destructionQueue);
            m_boneOffsetsPool   = new PersistentPool(512, 4, ComputeBufferType.Raw, m_copyByteAddressShader, m_destructionQueue);

            m_meshVerticesUploadPool  = new PerFramePool(3 * 3 * 4, ComputeBufferType.Structured);
            m_meshWeightsUploadPool   = new PerFramePool(4, ComputeBufferType.Raw);
            m_meshBindPosesUploadPool = new PerFramePool(3 * 4 * 4, ComputeBufferType.Structured);
            m_boneOffsetsUploadPool   = new PerFramePool(4, ComputeBufferType.Raw);
            m_bonesPool               = new PerFramePool(3 * 4 * 4, ComputeBufferType.Structured);
            m_skinningMetaPool        = new PerFramePool(4, ComputeBufferType.Raw);
            m_uploadMetaPool          = new PerFramePool(4, ComputeBufferType.Raw);

            m_fencePool = new FencePool(true);
        }

        public void Update()
        {
            m_fencePool.Update();
            m_meshVerticesUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_meshWeightsUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_meshBindPosesUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_boneOffsetsUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_bonesPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_skinningMetaPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_uploadMetaPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);

            for (int i = 0; i < m_destructionQueue.Count; i++)
            {
                if (IsEqualOrNewer(m_fencePool.RecoveredFrameId, m_destructionQueue[i].frameId))
                {
                    m_destructionQueue[i].buffer.Dispose();
                    m_destructionQueue.RemoveAtSwapBack(i);
                    i--;
                }
            }
        }

        public void Dispose()
        {
            foreach (var b in m_destructionQueue)
                b.buffer.Dispose();
            m_lbsMatsBufferPool.Dispose();
            m_deformBufferPool.Dispose();
            m_meshVerticesPool.Dispose();
            m_meshWeightsPool.Dispose();
            m_meshBindPosesPool.Dispose();
            m_meshVerticesUploadPool.Dispose();
            m_meshWeightsUploadPool.Dispose();
            m_meshBindPosesUploadPool.Dispose();
            m_boneOffsetsPool.Dispose();
            m_boneOffsetsUploadPool.Dispose();
            m_bonesPool.Dispose();
            m_skinningMetaPool.Dispose();
            m_uploadMetaPool.Dispose();
            m_fencePool.Dispose();
        }

        public ComputeBuffer GetLbsMatsBuffer(int requiredSize)
        {
            return m_lbsMatsBufferPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetDeformBuffer(int requiredSize)
        {
            return m_deformBufferPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetMeshVerticesBuffer(int requiredSize)
        {
            return m_meshVerticesPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetMeshWeightsBuffer(int requiredSize)
        {
            return m_meshWeightsPool.GetBuffer(requiredSize * 2, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetMeshBindPosesBuffer(int requiredSize)
        {
            return m_meshBindPosesPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetBoneOffsetsBuffer(int requiredSize)
        {
            return m_boneOffsetsPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetMeshVerticesUploadBuffer(int requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshVerticesUploadSize);
            return m_meshVerticesUploadPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetMeshWeightsUploadBuffer(int requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshWeightsUploadSize);
            return m_meshWeightsUploadPool.GetBuffer(requiredSize * 2, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetMeshBindPosesUploadBuffer(int requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshBindPosesUploadSize);
            return m_meshBindPosesUploadPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetBoneOffsetsUploadBuffer(int requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinBoneOffsetsUploadSize);
            return m_boneOffsetsUploadPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetBonesBuffer(int requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinBonesSize);
            return m_bonesPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetSkinningMetaBuffer(int requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinSkinningMetaSize);
            return m_skinningMetaPool.GetBuffer(requiredSize * 4, m_fencePool.CurrentFrameId);
        }

        public ComputeBuffer GetUploadMetaBuffer(int requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinUploadMetaSize);
            return m_uploadMetaPool.GetBuffer(requiredSize * 3, m_fencePool.CurrentFrameId);
        }

        struct BufferQueuedForDestruction
        {
            public ComputeBuffer buffer;
            public uint          frameId;
        }

        struct PersistentPool : IDisposable
        {
            ComputeBuffer                    m_currentBuffer;
            ComputeShader                    m_copyShader;
            List<BufferQueuedForDestruction> m_destructionQueue;
            int                              m_currentSize;
            int                              m_stride;
            ComputeBufferType                m_type;

            public PersistentPool(int initialSize, int stride, ComputeBufferType bufferType, ComputeShader copyShader, List<BufferQueuedForDestruction> destructionQueue)
            {
                int size           = math.ceilpow2(initialSize);
                m_currentBuffer    = new ComputeBuffer(size, stride, bufferType, ComputeBufferMode.Immutable);
                m_copyShader       = copyShader;
                m_destructionQueue = destructionQueue;
                m_currentSize      = size;
                m_stride           = stride;
                m_type             = bufferType;
            }

            public void Dispose()
            {
                m_currentBuffer.Dispose();
            }

            public ComputeBuffer GetBuffer(int requiredSize, uint frameId)
            {
                //UnityEngine.Debug.Log($"Requested Persistent Buffer of size: {requiredSize} while currentSize is: {m_currentSize}");
                if (requiredSize <= m_currentSize)
                    return m_currentBuffer;

                int size = math.ceilpow2(requiredSize);
                if (requiredSize * m_stride > 1024 * 1024 * 1024)
                    Debug.LogWarning("Attempted to allocate a mesh deformation buffer over 1 GB. Rendering artifacts may occur.");
                if (requiredSize * m_stride < 1024 * 1024 * 1024 && size * m_stride > 1024 * 1024 * 1024)
                    size        = 1024 * 1024 * 1024 / m_stride;
                var prevBuffer  = m_currentBuffer;
                m_currentBuffer = new ComputeBuffer(size, m_stride, m_type, ComputeBufferMode.Immutable);
                if (m_copyShader != null)
                {
                    m_copyShader.GetKernelThreadGroupSizes(0, out var threadGroupSize, out _, out _);
                    m_copyShader.SetBuffer(0, "_dst", m_currentBuffer);
                    m_copyShader.SetBuffer(0, "_src", prevBuffer);
                    int copySize = m_type == ComputeBufferType.Raw ? m_currentSize / 4 : m_currentSize;
                    for (int dispatchesRemaining = copySize / (int)threadGroupSize, start = 0; dispatchesRemaining > 0; )
                    {
                        int dispatchCount = math.min(dispatchesRemaining, 65535);
                        m_copyShader.SetInt("_start", start * (int)threadGroupSize);
                        m_copyShader.Dispatch(0, dispatchCount, 1, 1);
                        dispatchesRemaining -= dispatchCount;
                        start               += dispatchCount;
                        //UnityEngine.Debug.Log($"Dispatched buffer type: {m_type} with dispatchCount: {dispatchCount}");
                    }
                }
                m_currentSize                                                  = size;
                m_destructionQueue.Add(new BufferQueuedForDestruction { buffer = prevBuffer, frameId = frameId });
                return m_currentBuffer;
            }
        }

        struct PerFramePool : IDisposable
        {
            struct TrackedBuffer
            {
                public ComputeBuffer buffer;
                public int           size;
                public uint          frameId;
            }

            int                 m_stride;
            ComputeBufferType   m_type;
            List<TrackedBuffer> m_buffersInPool;
            List<TrackedBuffer> m_buffersInFlight;

            public PerFramePool(int stride, ComputeBufferType bufferType)
            {
                m_stride          = stride;
                m_type            = bufferType;
                m_buffersInPool   = new List<TrackedBuffer>();
                m_buffersInFlight = new List<TrackedBuffer>();
            }

            public ComputeBuffer GetBuffer(int requiredSize, uint frameId)
            {
                for (int i = 0; i < m_buffersInPool.Count; i++)
                {
                    if (m_buffersInPool[i].size >= requiredSize)
                    {
                        var tracked     = m_buffersInPool[i];
                        tracked.frameId = frameId;
                        m_buffersInFlight.Add(tracked);
                        m_buffersInPool.RemoveAtSwapBack(i);
                        return tracked.buffer;
                    }
                }

                if (m_buffersInPool.Count > 0)
                {
                    m_buffersInPool[0].buffer.Dispose();
                    m_buffersInPool.RemoveAtSwapBack(0);
                }

                int size       = math.ceilpow2(requiredSize);
                var newTracked = new TrackedBuffer
                {
                    buffer  = new ComputeBuffer(size, m_stride, m_type, ComputeBufferMode.SubUpdates),
                    size    = size,
                    frameId = frameId
                };
                m_buffersInFlight.Add(newTracked);
                return newTracked.buffer;
            }

            public void CollectFinishedBuffers(uint finishedFrameId)
            {
                for (int i = 0; i < m_buffersInFlight.Count; i++)
                {
                    var tracked = m_buffersInFlight[i];
                    if (IsEqualOrNewer(finishedFrameId, tracked.frameId))
                    {
                        m_buffersInPool.Add(tracked);
                        m_buffersInFlight.RemoveAtSwapBack(i);
                        i--;
                    }
                }
            }

            public void Dispose()
            {
                foreach (var buffer in m_buffersInPool)
                    buffer.buffer.Dispose();
                foreach (var buffer in m_buffersInFlight)
                    buffer.buffer.Dispose();
            }
        }

        struct FencePool : IDisposable
        {
            struct TrackedFence
            {
                public ComputeBuffer           buffer;
                public uint                    frameId;
                public AsyncGPUReadbackRequest request;
            }

            uint m_currentFrameId;
            uint m_recoveredFrameId;

            List<TrackedFence> m_fencesInFlight;
            List<TrackedFence> m_fencesInPool;

            public uint CurrentFrameId => m_currentFrameId;
            public uint RecoveredFrameId => m_recoveredFrameId;

            public FencePool(bool dummy)
            {
                m_fencesInFlight   = new List<TrackedFence>();
                m_fencesInPool     = new List<TrackedFence>();
                m_currentFrameId   = 1;
                m_recoveredFrameId = 0;
            }

            public void Update()
            {
                for (int i = 0; i < m_fencesInFlight.Count; i++)
                {
                    var fence = m_fencesInFlight[i];
                    if (fence.request.done)
                    {
                        if (IsEqualOrNewer(fence.frameId, m_recoveredFrameId))
                            m_recoveredFrameId = fence.frameId;
                        m_fencesInPool.Add(fence);
                        m_fencesInFlight.RemoveAtSwapBack(i);
                        i--;
                    }
                }

                TrackedFence newFence;
                if (m_fencesInPool.Count > 0)
                {
                    newFence = m_fencesInPool[0];
                    m_fencesInPool.RemoveAtSwapBack(0);
                }
                else
                {
                    newFence = new TrackedFence
                    {
                        buffer = new ComputeBuffer(1, 4, ComputeBufferType.Default, ComputeBufferMode.Immutable),
                    };
                }

                newFence.frameId = m_currentFrameId;
                newFence.request = AsyncGPUReadback.Request(newFence.buffer);
                m_fencesInFlight.Add(newFence);

                m_currentFrameId++;
            }

            public void Dispose()
            {
                foreach (var buffer in m_fencesInPool)
                    buffer.buffer.Dispose();
                foreach (var buffer in m_fencesInFlight)
                    buffer.buffer.Dispose();
            }
        }

        static bool IsEqualOrNewer(uint potentiallyNewer, uint requiredVersion)
        {
            return ((int)(potentiallyNewer - requiredVersion)) >= 0;
        }
    }
}

