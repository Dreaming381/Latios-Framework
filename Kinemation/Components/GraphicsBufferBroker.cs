using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation
{
    /// <summary>
    /// Provides access to the GraphicsBufferBroker.
    /// Usage of the broker prior to UpdateGraphicsBufferBrokerSystem is undefined.
    /// UpdateGraphicsBufferBrokerSystem updates in PresentationSystemGroup with OrderFirst = true.
    /// It is up to you to manage job dependencies for jobs that write to locked graphics buffers.
    /// </summary>
    public partial struct GraphicsBufferBrokerReference : IManagedStructComponent
    {
        public GraphicsBufferBroker graphicsBufferBroker { get; internal set; }

        public void Dispose()
        {
            if (graphicsBufferBroker != null)
                graphicsBufferBroker.Dispose();
        }
    }

    public static class DeformationGraphicsBufferBrokerExtensions
    {
        #region Public Access
        /// <summary>
        /// Acquires the graphics buffer which contains the skinning transforms for Latios Vertex Skinning.
        /// The buffer may change with each culling pass, but contains the contents of previous culling passes within the frame.
        /// It is valid during CullingRoundRobinLateExtensionsSuperSystem during the Dispatch phase.
        /// </summary>
        public static GraphicsBuffer GetSkinningTransformsBuffer(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(s_skinningTransformsID);
        /// <summary>
        /// Acquires the graphics buffer which contains the deformed vertices for Latios Deform.
        /// The buffer may change with each culling pass, but contains the contents of previous culling passes within the frame.
        /// It is valid during CullingRoundRobinLateExtensionsSuperSystem during the Dispatch phase.
        /// </summary>
        public static GraphicsBuffer GetDeformedVerticesBuffer(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(s_deformedVerticesID);
        /// <summary>
        /// Acquires the graphics buffer which contains the undeformed shared mesh vertices.
        /// The buffer is constant for a given frame, and is valid after KinemationPostRenderSuperSystem.
        /// </summary>
        public static GraphicsBuffer GetMeshVerticesBuffer(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(s_meshVerticesID);

        /// <summary>
        /// Acquires a pooled ByteAddressBuffer designed for locked CPU writes of uint3 values.
        /// These are often used to specify a list of copy operations to a compute shader.
        /// </summary>
        /// <param name="requiredNumUint3s">The number of uint3 values the buffer should have</param>
        /// <returns>A buffer at least the size required to store the required number of uint3 values</returns>
        public static GraphicsBuffer GetMetaUint3UploadBuffer(this GraphicsBufferBroker broker, uint requiredNumUint3s)
        {
            requiredNumUint3s = math.max(requiredNumUint3s, kMinUploadMetaSize);
            return broker.GetUploadBuffer(s_metaUint3UploadID, requiredNumUint3s * 3);
        }
        /// <summary>
        /// Acquires a pooled ByteAddressBuffer designed for locked CPU writes of uint4 values.
        /// These are often used to specify a list of custom operations to a compute shader.
        /// </summary>
        /// <param name="requiredNumUint4s">The number of uint4 values the buffer should have</param>
        /// <returns>A buffer at least the size required to store the required number of uint4 values</returns>
        public static GraphicsBuffer GetMetaUint4UploadBuffer(this GraphicsBufferBroker broker, uint requiredNumUint4s)
        {
            requiredNumUint4s = math.max(requiredNumUint4s, kMinUploadMetaSize);
            return broker.GetUploadBuffer(s_metaUint3UploadID, requiredNumUint4s * 4);
        }
        #endregion

        #region Reservations
        internal static GraphicsBufferBroker.StaticID s_skinningTransformsID = GraphicsBufferBroker.ReservePersistentBuffer();
        internal static GraphicsBufferBroker.StaticID s_deformedVerticesID   = GraphicsBufferBroker.ReservePersistentBuffer();
        internal static GraphicsBufferBroker.StaticID s_meshVerticesID       = GraphicsBufferBroker.ReservePersistentBuffer();
        internal static GraphicsBufferBroker.StaticID s_meshWeightsID        = GraphicsBufferBroker.ReservePersistentBuffer();
        internal static GraphicsBufferBroker.StaticID s_meshBindPosesID      = GraphicsBufferBroker.ReservePersistentBuffer();
        internal static GraphicsBufferBroker.StaticID s_meshBlendShapesID    = GraphicsBufferBroker.ReservePersistentBuffer();
        internal static GraphicsBufferBroker.StaticID s_boneOffsetsID        = GraphicsBufferBroker.ReservePersistentBuffer();

        internal static GraphicsBufferBroker.StaticID s_metaUint3UploadID = GraphicsBufferBroker.ReserveUploadPool();
        internal static GraphicsBufferBroker.StaticID s_metaUint4UploadID = GraphicsBufferBroker.ReserveUploadPool();

        internal static GraphicsBufferBroker.StaticID s_meshVerticesUploadID    = GraphicsBufferBroker.ReserveUploadPool();
        internal static GraphicsBufferBroker.StaticID s_meshWeightsUploadID     = GraphicsBufferBroker.ReserveUploadPool();
        internal static GraphicsBufferBroker.StaticID s_meshBindPosesUploadID   = GraphicsBufferBroker.ReserveUploadPool();
        internal static GraphicsBufferBroker.StaticID s_meshBlendShapesUploadID = GraphicsBufferBroker.ReserveUploadPool();
        internal static GraphicsBufferBroker.StaticID s_boneOffsetsUploadID     = GraphicsBufferBroker.ReserveUploadPool();
        internal static GraphicsBufferBroker.StaticID s_bonesUploadID           = GraphicsBufferBroker.ReserveUploadPool();

        const uint kMinUploadMetaSize = 128;
        #endregion
    }

    /// <summary>
    /// A special object which manages the lifecycle of Graphics Buffers, automatically resizing them and pooling them.
    /// An API is provided for users to obtain buffers that meet their use case.
    /// </summary>
    public class GraphicsBufferBroker : IDisposable
    {
        #region API
        /// <summary>
        /// A runtime static handle to a particular persistent growable buffer or upload buffer pool.
        /// </summary>
        public struct StaticID
        {
            internal int index;
        }

        /// <summary>
        /// Statically acquires a handle for a persistent GPU-resident graphics buffer that is allowed to grow.
        /// </summary>
        public static StaticID ReservePersistentBuffer() => new StaticID
        {
            index = s_reservedPersistentBuffersCount++
        };
        /// <summary>
        /// Statically acquires a handle for a graphics buffer upload pool for buffers that are written to by the CPU using LockBufferForWrite.
        /// Pooled buffers are automatically recycled every few frames safely for LockBufferForWrite usage.
        /// </summary>
        public static StaticID ReserveUploadPool() => new StaticID
        {
            index = s_reservedUploadPoolsCount++
        };

        /// <summary>
        /// Initialize a persistent GPU-resident graphics buffer. This should be called in OnCreate() of a system.
        /// </summary>
        /// <param name="staticID">The reserved ID for the buffer</param>
        /// <param name="initialNumElements">The initial number of elements stored in the buffer. The value is rounded up to a power of two.</param>
        /// <param name="strideOfElement">The number of bytes of each element. Must be 4 for Raw binding targets.</param>
        /// <param name="bindingTarget">Specifies the binding target type, typically Structured or Raw.</param>
        /// <param name="copyShader">A compute shader used to grow the buffer while preserving contents.
        /// If left null, the new buffer's contents will be undefined on resize, which may be suitable for buffers
        /// whose contents are overwritten once every frame. Otherwise, the shader must define the properties _src as a buffer, _dst as a buffer,
        /// and _start as an integer which is used as an offset in both the src and dst buffers if the copy operation requires multiple dispatches.
        /// Kernel index 0 is always used. Each thread is responsible for copying one element.
        /// For ByteAddressBuffers, you can use the built-in CopyBytes shader loaded from Resources.</param>
        public void InitializePersistentBuffer(StaticID staticID, uint initialNumElements, uint strideOfElement, GraphicsBuffer.Target bindingTarget, ComputeShader copyShader)
        {
            while (m_persistentBuffers.Count <= staticID.index)
                m_persistentBuffers.Add(default);
            m_persistentBuffers[staticID.index] = new PersistentBuffer(initialNumElements, strideOfElement, bindingTarget, copyShader, m_buffersToDelete);
        }

        /// <summary>
        /// Requests access to the persistent GPU-resident graphics buffer, possibly increasing the size of the buffer if necessary.
        /// The buffer is valid until the next call to this method with the same StaticID.
        /// </summary>
        /// <param name="staticID">The reserved ID for the buffer</param>
        /// <param name="requiredNumElements">The number of elements the buffer must be able to hold. The value is rounded up to a power of two.</param>
        /// <returns>The persistent graphics buffer sized at least to the requested size</returns>
        public GraphicsBuffer GetPersistentBuffer(StaticID staticID, uint requiredNumElements)
        {
            var persistent                      = m_persistentBuffers[staticID.index];
            var result                          = persistent.GetBuffer(requiredNumElements, m_frameFenceTracker.CurrentFrameId);
            m_persistentBuffers[staticID.index] = persistent;
            return result;
        }

        /// <summary>
        /// Requests access to the persistent GPU-resident graphics buffer, preserving the buffer's existing size.
        /// The buffer is valid until the next call to the GetPersistentBuffer() method with the same StaticID.
        /// This method is typically used for when you want to read from the buffer in a shader.
        /// </summary>
        /// <param name="staticID">The reserved ID for the buffer</param>
        /// <returns>The persistent graphics buffer at whatever size is was last resized to</returns>
        public GraphicsBuffer GetPersistentBufferNoResize(StaticID staticID) => m_persistentBuffers[staticID.index].GetBufferNoResize();

        /// <summary>
        /// Initializes a graphics buffer upload pool for buffers using LockBufferForWrite protocol.
        /// </summary>
        /// <param name="staticID">The ID of the pool</param>
        /// <param name="strideOfElement">The number of bytes of each element. Must be 4 for Raw binding targets.</param>
        /// <param name="bindingTarget">Specifies the binding target type, typically Structured or Raw.</param>
        public void InitializeUploadPool(StaticID staticID, uint strideOfElement, GraphicsBuffer.Target bindingTarget)
        {
            while (m_uploadPools.Count <= staticID.index)
                m_uploadPools.Add(default);
            m_uploadPools[staticID.index] = new UploadPool(strideOfElement, bindingTarget);
        }

        /// <summary>
        /// Retrieves a graphics buffer from the upload pool that is guaranteed to be the specified size of larger.
        /// Each successive call within a frame provides a different GraphicsBuffer instance. The instance is only valid
        /// for the same frame it is retrieved.
        /// </summary>
        /// <param name="staticID">The ID of the pool</param>
        /// <param name="requiredNumElements">The number of elements the graphics buffer should be able to hold</param>
        /// <returns></returns>
        public GraphicsBuffer GetUploadBuffer(StaticID staticID, uint requiredNumElements)
        {
            var upload                    = m_uploadPools[staticID.index];
            var result                    = upload.GetBuffer(requiredNumElements, m_frameFenceTracker.CurrentFrameId);
            m_uploadPools[staticID.index] = upload;
            return result;
        }
        #endregion

        #region Members
        static int s_reservedPersistentBuffersCount = 0;
        static int s_reservedUploadPoolsCount       = 0;

        List<PersistentBuffer>           m_persistentBuffers = new List<PersistentBuffer>();
        List<UploadPool>                 m_uploadPools       = new List<UploadPool>();
        List<BufferQueuedForDestruction> m_buffersToDelete   = new List<BufferQueuedForDestruction>();
        FrameFenceTracker                m_frameFenceTracker = new FrameFenceTracker(true);
        #endregion

        #region Buffer Types
        struct PersistentBuffer : IDisposable
        {
            GraphicsBuffer                   m_currentBuffer;
            ComputeShader                    m_copyShader;
            List<BufferQueuedForDestruction> m_destructionQueue;
            uint                             m_currentSize;
            uint                             m_stride;
            GraphicsBuffer.Target            m_bindingTarget;

            public PersistentBuffer(uint initialSize, uint stride, GraphicsBuffer.Target bufferType, ComputeShader copyShader, List<BufferQueuedForDestruction> destructionQueue)
            {
                uint size          = math.ceilpow2(initialSize);
                m_currentBuffer    = new GraphicsBuffer(bufferType, GraphicsBuffer.UsageFlags.None, (int)size, (int)stride);
                m_copyShader       = copyShader;
                m_destructionQueue = destructionQueue;
                m_currentSize      = size;
                m_stride           = stride;
                m_bindingTarget    = bufferType;
            }

            public void Dispose()
            {
                m_currentBuffer.Dispose();
            }

            public bool valid => m_currentBuffer != null;

            public GraphicsBuffer GetBufferNoResize() => m_currentBuffer;

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
                m_currentBuffer = new GraphicsBuffer(m_bindingTarget, GraphicsBuffer.UsageFlags.None, (int)size, (int)m_stride);
                if (m_copyShader != null)
                {
                    m_copyShader.GetKernelThreadGroupSizes(0, out var threadGroupSize, out _, out _);
                    m_copyShader.SetBuffer(0, "_dst", m_currentBuffer);
                    m_copyShader.SetBuffer(0, "_src", prevBuffer);
                    uint copySize = m_currentSize;
                    for (uint dispatchesRemaining = copySize / threadGroupSize, start = 0; dispatchesRemaining > 0;)
                    {
                        uint dispatchCount = math.min(dispatchesRemaining, 65535);
                        m_copyShader.SetInt("_start", (int)(start * threadGroupSize));
                        m_copyShader.Dispatch(0, (int)dispatchCount, 1, 1);
                        dispatchesRemaining -= dispatchCount;
                        start               += dispatchCount;
                        //UnityEngine.Debug.Log($"Dispatched buffer type: {m_bindingTarget} with dispatchCount: {dispatchCount}");
                    }
                }
                m_currentSize                                                  = size;
                m_destructionQueue.Add(new BufferQueuedForDestruction { buffer = prevBuffer, frameId = frameId });
                return m_currentBuffer;
            }
        }

        struct BufferQueuedForDestruction
        {
            public GraphicsBuffer buffer;
            public uint           frameId;
        }

        struct UploadPool : IDisposable
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

            public UploadPool(uint stride, GraphicsBuffer.Target bufferType)
            {
                m_stride          = stride;
                m_type            = bufferType;
                m_buffersInPool   = new List<TrackedBuffer>();
                m_buffersInFlight = new List<TrackedBuffer>();
            }

            public bool valid => m_buffersInPool != null;

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

        struct FrameFenceTracker
        {
            uint m_currentFrameId;
            uint m_recoveredFrameId;
            int  m_numberOfFramesToWait;

            public uint CurrentFrameId => m_currentFrameId;
            public uint RecoveredFrameId => m_recoveredFrameId;

            public FrameFenceTracker(bool dummy)
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
        }
        #endregion

        #region Internal Management Methods
        static bool IsEqualOrNewer(uint potentiallyNewer, uint requiredVersion)
        {
            return ((int)(potentiallyNewer - requiredVersion)) >= 0;
        }

        internal void Update()
        {
            m_frameFenceTracker.Update();
            for (int i = 0; i < m_uploadPools.Count; i++)
            {
                var pool = m_uploadPools[i];
                if (pool.valid)
                {
                    pool.CollectFinishedBuffers(m_frameFenceTracker.RecoveredFrameId);
                    m_uploadPools[i] = pool;
                }
            }

            for (int i = 0; i < m_buffersToDelete.Count; i++)
            {
                if (IsEqualOrNewer(m_frameFenceTracker.RecoveredFrameId, m_buffersToDelete[i].frameId))
                {
                    m_buffersToDelete[i].buffer.Dispose();
                    m_buffersToDelete.RemoveAtSwapBack(i);
                    i--;
                }
            }
        }

        public void Dispose()
        {
            foreach (var b in m_buffersToDelete)
                b.buffer.Dispose();
            foreach (var b in m_uploadPools)
            {
                if (b.valid)
                    b.Dispose();
            }
            foreach (var b in m_persistentBuffers)
            {
                if (b.valid)
                    b.Dispose();
            }
        }
        #endregion
    }
}

