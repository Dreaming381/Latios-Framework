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
    internal class GraphicsBufferTrackingPool
    {
        PersistentPool m_skinningTransformsBufferPool;
        PersistentPool m_deformBufferPool;
        PersistentPool m_meshVerticesPool;
        PersistentPool m_meshWeightsPool;
        PersistentPool m_meshBindPosesPool;
        PersistentPool m_meshBlendShapesPool;
        PersistentPool m_boneOffsetsPool;
        PersistentPool m_glyphsPool;

        PerFramePool m_meshVerticesUploadPool;
        PerFramePool m_meshWeightsUploadPool;
        PerFramePool m_meshBindPosesUploadPool;
        PerFramePool m_meshBlendShapesUploadPool;
        PerFramePool m_boneOffsetsUploadPool;
        PerFramePool m_bonesPool;
        PerFramePool m_glyphsUploadPool;
        PerFramePool m_dispatchMetaPool;  // uint4
        PerFramePool m_uploadMetaPool;  // uint3

        FencePool m_fencePool;

        ComputeShader m_copyVerticesShader;
        ComputeShader m_copyTransformUnionsShader;
        ComputeShader m_copyBlendShapesShader;
        ComputeShader m_copyByteAddressShader;

        List<BufferQueuedForDestruction> m_destructionQueue;

        const uint kMinMeshVerticesUploadSize    = 16 * 1024;
        const uint kMinMeshWeightsUploadSize     = 4 * 16 * 1024;
        const uint kMinMeshBindPosesUploadSize   = 256;
        const uint kMinMeshBlendShapesUploadSize = 1024;
        const uint kMinBoneOffsetsUploadSize     = 128;
        const uint kMinBonesSize                 = 128 * 128;
        const uint kMinGlyphsUploadSize          = 128;
        const uint kMinDispatchMetaSize          = 128;
        const uint kMinUploadMetaSize            = 128;

        public GraphicsBufferTrackingPool()
        {
            m_destructionQueue          = new List<BufferQueuedForDestruction>();
            m_copyVerticesShader        = Resources.Load<ComputeShader>("CopyVertices");
            m_copyTransformUnionsShader = Resources.Load<ComputeShader>("CopyTransformUnions");
            m_copyBlendShapesShader     = Resources.Load<ComputeShader>("CopyBlendShapes");
            m_copyByteAddressShader     = Resources.Load<ComputeShader>("CopyBytes");

            // Unity's LBS node uses ByteAddressBuffer
            m_skinningTransformsBufferPool = new PersistentPool(3 * 4 * 1024,
                                                                4,
                                                                GraphicsBuffer.Target.Raw,
                                                                m_copyTransformUnionsShader,
                                                                m_destructionQueue);
            m_deformBufferPool    = new PersistentPool(256 * 1024, 3 * 3 * 4, GraphicsBuffer.Target.Structured, m_copyVerticesShader, m_destructionQueue);
            m_meshVerticesPool    = new PersistentPool(64 * 1024, 3 * 3 * 4, GraphicsBuffer.Target.Structured, m_copyVerticesShader, m_destructionQueue);
            m_meshWeightsPool     = new PersistentPool(2 * 4 * 64 * 1024, 4, GraphicsBuffer.Target.Raw, m_copyByteAddressShader, m_destructionQueue);
            m_meshBindPosesPool   = new PersistentPool(1024, 3 * 4 * 4, GraphicsBuffer.Target.Structured, m_copyTransformUnionsShader, m_destructionQueue);
            m_meshBlendShapesPool = new PersistentPool(16 * 1024, 10 * 4, GraphicsBuffer.Target.Structured, m_copyBlendShapesShader, m_destructionQueue);
            m_boneOffsetsPool     = new PersistentPool(512, 4, GraphicsBuffer.Target.Raw, m_copyByteAddressShader, m_destructionQueue);
            m_glyphsPool          = new PersistentPool(128, 4, GraphicsBuffer.Target.Raw, m_copyByteAddressShader, m_destructionQueue);

            m_meshVerticesUploadPool    = new PerFramePool(3 * 3 * 4, GraphicsBuffer.Target.Structured);
            m_meshWeightsUploadPool     = new PerFramePool(4, GraphicsBuffer.Target.Raw);
            m_meshBindPosesUploadPool   = new PerFramePool(3 * 4 * 4, GraphicsBuffer.Target.Structured);
            m_meshBlendShapesUploadPool = new PerFramePool(10 * 4, GraphicsBuffer.Target.Structured);
            m_boneOffsetsUploadPool     = new PerFramePool(4, GraphicsBuffer.Target.Raw);
            m_bonesPool                 = new PerFramePool(3 * 4 * 4, GraphicsBuffer.Target.Structured);
            m_glyphsUploadPool          = new PerFramePool(4, GraphicsBuffer.Target.Raw);
            m_dispatchMetaPool          = new PerFramePool(4, GraphicsBuffer.Target.Raw);
            m_uploadMetaPool            = new PerFramePool(4, GraphicsBuffer.Target.Raw);

            m_fencePool = new FencePool(true);
        }

        public void Update()
        {
            m_fencePool.Update();
            m_meshVerticesUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_meshWeightsUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_meshBindPosesUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_meshBlendShapesUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_boneOffsetsUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_bonesPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_glyphsUploadPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
            m_dispatchMetaPool.CollectFinishedBuffers(m_fencePool.RecoveredFrameId);
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
            m_skinningTransformsBufferPool.Dispose();
            m_deformBufferPool.Dispose();
            m_meshVerticesPool.Dispose();
            m_meshWeightsPool.Dispose();
            m_meshBlendShapesPool.Dispose();
            m_meshBindPosesPool.Dispose();
            m_glyphsPool.Dispose();
            m_meshVerticesUploadPool.Dispose();
            m_meshWeightsUploadPool.Dispose();
            m_meshBindPosesUploadPool.Dispose();
            m_meshBlendShapesUploadPool.Dispose();
            m_boneOffsetsPool.Dispose();
            m_boneOffsetsUploadPool.Dispose();
            m_bonesPool.Dispose();
            m_dispatchMetaPool.Dispose();
            m_uploadMetaPool.Dispose();
            m_fencePool.Dispose();
        }

        public GraphicsBuffer GetSkinningTransformsBuffer(uint requiredSize)
        {
            return m_skinningTransformsBufferPool.GetBuffer(requiredSize * 3 * 4, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetDeformBuffer(uint requiredSize)
        {
            return m_deformBufferPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshVerticesBuffer(uint requiredSize)
        {
            return m_meshVerticesPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshVerticesBufferRO() => m_meshVerticesPool.GetBuffer(0, m_fencePool.CurrentFrameId);

        public GraphicsBuffer GetMeshWeightsBuffer(uint requiredSize)
        {
            return m_meshWeightsPool.GetBuffer(requiredSize * 2, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshWeightsBufferRO() => m_meshWeightsPool.GetBuffer(0, m_fencePool.CurrentFrameId);

        public GraphicsBuffer GetMeshBindPosesBuffer(uint requiredSize)
        {
            return m_meshBindPosesPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshBindPosesBufferRO() => m_meshBindPosesPool.GetBuffer(0, m_fencePool.CurrentFrameId);

        public GraphicsBuffer GetMeshBlendShapesBuffer(uint requiredSize)
        {
            return m_meshBlendShapesPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshBlendShapesBufferRO() => m_meshBlendShapesPool.GetBuffer(0, m_fencePool.CurrentFrameId);

        public GraphicsBuffer GetBoneOffsetsBuffer(uint requiredSize)
        {
            return m_boneOffsetsPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetBoneOffsetsBufferRO() => m_boneOffsetsPool.GetBuffer(0, m_fencePool.CurrentFrameId);

        public GraphicsBuffer GetGlyphsBuffer(uint requiredSize)
        {
            return m_glyphsPool.GetBuffer(requiredSize * 24, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshVerticesUploadBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshVerticesUploadSize);
            return m_meshVerticesUploadPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshWeightsUploadBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshWeightsUploadSize);
            return m_meshWeightsUploadPool.GetBuffer(requiredSize * 2, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshBindPosesUploadBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshBindPosesUploadSize);
            return m_meshBindPosesUploadPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetMeshBlendShapesUploadBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshBlendShapesUploadSize);
            return m_meshBlendShapesUploadPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetBoneOffsetsUploadBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinBoneOffsetsUploadSize);
            return m_boneOffsetsUploadPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetBonesBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinBonesSize);
            return m_bonesPool.GetBuffer(requiredSize, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetGlyphsUploadBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinGlyphsUploadSize);
            return m_glyphsUploadPool.GetBuffer(requiredSize * 24, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetDispatchMetaBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinDispatchMetaSize);
            return m_dispatchMetaPool.GetBuffer(requiredSize * 4, m_fencePool.CurrentFrameId);
        }

        public GraphicsBuffer GetUploadMetaBuffer(uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinUploadMetaSize);
            return m_uploadMetaPool.GetBuffer(requiredSize * 3, m_fencePool.CurrentFrameId);
        }

        struct BufferQueuedForDestruction
        {
            public GraphicsBuffer buffer;
            public uint           frameId;
        }

        struct PersistentPool : IDisposable
        {
            GraphicsBuffer                   m_currentBuffer;
            ComputeShader                    m_copyShader;
            List<BufferQueuedForDestruction> m_destructionQueue;
            uint                             m_currentSize;
            uint                             m_stride;
            GraphicsBuffer.Target            m_type;

            public PersistentPool(uint initialSize, uint stride, GraphicsBuffer.Target bufferType, ComputeShader copyShader, List<BufferQueuedForDestruction> destructionQueue)
            {
                uint size          = math.ceilpow2(initialSize);
                m_currentBuffer    = new GraphicsBuffer(bufferType, GraphicsBuffer.UsageFlags.None, (int)size, (int)stride);
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

            public GraphicsBuffer GetBuffer(uint requiredSize, uint frameId)
            {
                //UnityEngine.Debug.Log($"Requested Persistent Buffer of size: {requiredSize} while currentSize is: {m_currentSize}");
                if (requiredSize <= m_currentSize)
                    return m_currentBuffer;

                uint size = math.ceilpow2(requiredSize);
                if (requiredSize * m_stride > 1024 * 1024 * 1024)
                    Debug.LogWarning("Attempted to allocate a mesh deformation buffer over 1 GB. Rendering artifacts may occur.");
                if (requiredSize * m_stride < 1024 * 1024 * 1024 && size * m_stride > 1024 * 1024 * 1024)
                    size        = 1024 * 1024 * 1024 / m_stride;
                var prevBuffer  = m_currentBuffer;
                m_currentBuffer = new GraphicsBuffer(m_type, GraphicsBuffer.UsageFlags.None, (int)size, (int)m_stride);
                if (m_copyShader != null)
                {
                    m_copyShader.GetKernelThreadGroupSizes(0, out var threadGroupSize, out _, out _);
                    m_copyShader.SetBuffer(0, "_dst", m_currentBuffer);
                    m_copyShader.SetBuffer(0, "_src", prevBuffer);
                    uint copySize = m_type == GraphicsBuffer.Target.Raw ? m_currentSize / 4 : m_currentSize;
                    for (uint dispatchesRemaining = copySize / threadGroupSize, start = 0; dispatchesRemaining > 0; )
                    {
                        uint dispatchCount = math.min(dispatchesRemaining, 65535);
                        m_copyShader.SetInt("_start", (int)(start * threadGroupSize));
                        m_copyShader.Dispatch(0, (int)dispatchCount, 1, 1);
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
                public GraphicsBuffer buffer;
                public uint           size;
                public uint           frameId;
            }

            uint                  m_stride;
            GraphicsBuffer.Target m_type;
            List<TrackedBuffer>   m_buffersInPool;
            List<TrackedBuffer>   m_buffersInFlight;

            public PerFramePool(uint stride, GraphicsBuffer.Target bufferType)
            {
                m_stride          = stride;
                m_type            = bufferType;
                m_buffersInPool   = new List<TrackedBuffer>();
                m_buffersInFlight = new List<TrackedBuffer>();
            }

            public GraphicsBuffer GetBuffer(uint requiredSize, uint frameId)
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

                uint size       = math.ceilpow2(requiredSize);
                var  newTracked = new TrackedBuffer
                {
                    buffer  = new GraphicsBuffer(m_type, GraphicsBuffer.UsageFlags.LockBufferForWrite, (int)size, (int)m_stride),
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
            uint m_currentFrameId;
            uint m_recoveredFrameId;
            int  m_numberOfFramesToWait;

            public uint CurrentFrameId => m_currentFrameId;
            public uint RecoveredFrameId => m_recoveredFrameId;

            public FencePool(bool dummy)
            {
                m_numberOfFramesToWait = Unity.Rendering.SparseUploader.NumFramesInFlight;
                m_currentFrameId       = (uint)m_numberOfFramesToWait;
                m_recoveredFrameId     = 0;
            }

            public void Update()
            {
                m_recoveredFrameId++;
                m_currentFrameId++;
            }

            public void Dispose()
            {
            }
        }

        static bool IsEqualOrNewer(uint potentiallyNewer, uint requiredVersion)
        {
            return ((int)(potentiallyNewer - requiredVersion)) >= 0;
        }
    }
}

