using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Latios.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Latios.Kinemation.SparseUpload
{
    internal enum OperationType : int
    {
        Upload = 0,
        Matrix_4x4 = 1,
        Matrix_Inverse_4x4 = 2,
        Matrix_3x4 = 3,
        Matrix_Inverse_3x4 = 4,
        StridedUpload = 5,
        Qvvs_Matrix_3x4 = 6,
        Qvvs_Matrix_3x4_Inverse = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Operation
    {
        public uint type;
        public uint srcOffset;
        public uint srcStride;
        public uint dstOffset;
        public uint dstOffsetExtra;
        public int  dstStride;
        public uint size;
        public uint count;
    }

    internal unsafe struct MappedBuffer
    {
        public byte* m_Data;
        public long  m_Marker;
        public int   m_BufferID;

        public static long PackMarker(long operationOffset, long dataOffset)
        {
            return (dataOffset << 32) | (operationOffset & 0xFFFFFFFF);
        }

        public static void UnpackMarker(long marker, out long operationOffset, out long dataOffset)
        {
            operationOffset = marker & 0xFFFFFFFF;
            dataOffset      = (marker >> 32) & 0xFFFFFFFF;
        }

        public bool TryAlloc(int operationSize, int dataSize, out byte* ptr, out int operationOffset, out int dataOffset)
        {
            long originalMarker;
            long newMarker;
            long currOperationOffset;
            long currDataOffset;
            do
            {
                // Read the marker as is right now
                originalMarker = Interlocked.Read(ref m_Marker);
                UnpackMarker(originalMarker, out currOperationOffset, out currDataOffset);

                // Calculate the new offsets for operation and data
                // Operations are stored in the beginning of the buffer
                // Data is stored at the end of the buffer
                var newOperationOffset = currOperationOffset + operationSize;
                var newDataOffset      = currDataOffset - dataSize;

                // Check if there was enough space in the buffer for this allocation
                if (newDataOffset < newOperationOffset)
                {
                    // Not enough space, return false
                    ptr             = null;
                    operationOffset = 0;
                    dataOffset      = 0;
                    return false;
                }

                newMarker = PackMarker(newOperationOffset, newDataOffset);

                // Finally we try to CAS the new marker in.
                // If anyone has allocated from the buffer in the meantime this will fail and the loop will rerun
            }
            while (Interlocked.CompareExchange(ref m_Marker, newMarker, originalMarker) != originalMarker);

            // Now we have succeeded in getting a data slot out and can return true.
            ptr             = m_Data;
            operationOffset = (int)currOperationOffset;
            dataOffset      = (int)(currDataOffset - dataSize);
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ThreadedSparseUploaderData
    {
        [NativeDisableUnsafePtrRestriction] public MappedBuffer* m_Buffers;
        public int                                               m_NumBuffers;
        public int                                               m_CurrBuffer;
    }

    /// <summary>
    /// An unmanaged and Burst-compatible interface for SparseUploader.
    /// </summary>
    /// <remarks>
    /// This should be created each frame by a call to SparseUploader.Begin and is later returned by a call to SparseUploader.EndAndCommit.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct LatiosThreadedSparseUploader
    {
        // TODO: safety handle?
        [NativeDisableUnsafePtrRestriction] internal ThreadedSparseUploaderData* m_Data;

        /// <summary>
        /// Indicates whether the SparseUploader is valid and can be used.
        /// </summary>
        public bool IsValid => m_Data != null;

        private bool TryAlloc(int operationSize, int dataSize, out byte* ptr, out int operationOffset, out int dataOffset)
        {
            // Fetch current buffer and ensure we are not already out of GPU buffers to allocate from;
            var numBuffers = m_Data->m_NumBuffers;
            var buffer     = m_Data->m_CurrBuffer;
            if (buffer < numBuffers)
            {
                do
                {
                    // Try to allocate from the current buffer
                    if (m_Data->m_Buffers[buffer].TryAlloc(operationSize, dataSize, out var p, out var op, out var d))
                    {
                        // Success, we can return true at onnce
                        ptr             = p;
                        operationOffset = op;
                        dataOffset      = d;
                        return true;
                    }

                    // Try to increment the buffer.
                    // If someone else has done this while we where trying to alloc we will use their
                    // value and du another iteration. Otherwise we will use our new value
                    buffer = Interlocked.CompareExchange(ref m_Data->m_CurrBuffer, buffer + 1, buffer);
                }
                while (buffer < m_Data->m_NumBuffers);
            }

            // We have run out of buffers, return false
            ptr             = null;
            operationOffset = 0;
            dataOffset      = 0;
            return false;
        }

        /// <summary>
        /// Adds a new pending upload operation to execute when you call SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// When this operation executes, the SparseUploader copies data from the source pointer.
        /// </remarks>
        /// <param name="src">The source pointer of data to upload.</param>
        /// <param name="size">The amount of data, in bytes, to read from the source pointer.</param>
        /// <param name="offsetInBytes">The destination offset of the data in the GPU buffer.</param>
        /// <param name="repeatCount">The number of times to repeat the source data in the destination buffer when uploading.</param>
        public void AddUpload(void* src, int size, int offsetInBytes, int repeatCount = 1)
        {
            var opsize         = UnsafeUtility.SizeOf<Operation>();
            var allocSucceeded = TryAlloc(opsize, size, out var dst, out var operationOffset, out var dataOffset);

            if (!allocSucceeded)
            {
                Debug.Log("SparseUploader failed to allocate upload memory for AddUpload operation");
                return;
            }

            if (repeatCount <= 0)
                repeatCount = 1;

            // TODO: Vectorized memcpy
            UnsafeUtility.MemCpy(dst + dataOffset, src, size);
            var op = new Operation
            {
                type           = (uint)OperationType.Upload,
                srcOffset      = (uint)dataOffset,
                dstOffset      = (uint)offsetInBytes,
                dstOffsetExtra = 0,
                size           = (uint)size,
                count          = (uint)repeatCount
            };
            UnsafeUtility.MemCpy(dst + operationOffset, &op, opsize);
        }

        /// <summary>
        /// Adds a new pending upload operation to execute when you call SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// When this operation executes, the SparseUploader copies data from the source value.
        /// </remarks>
        /// <param name="val">The source data to upload.</param>
        /// <param name="offsetInBytes">The destination offset of the data in the GPU buffer.</param>
        /// <param name="repeatCount">The number of times to repeat the source data in the destination buffer when uploading.</param>
        /// <typeparam name="T">Any unmanaged simple type.</typeparam>
        public void AddUpload<T>(T val, int offsetInBytes, int repeatCount = 1) where T : unmanaged
        {
            var size = UnsafeUtility.SizeOf<T>();
            AddUpload(&val, size, offsetInBytes, repeatCount);
        }

        /// <summary>
        /// Adds a new pending upload operation to execute when you call SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// When this operation executes, the SparseUploader copies data from the source array.
        /// </remarks>
        /// <param name="array">The source array of data to upload.</param>
        /// <param name="offsetInBytes">The destination offset of the data in the GPU buffer.</param>
        /// <param name="repeatCount">The number of times to repeat the source data in the destination buffer when uploading.</param>
        /// <typeparam name="T">Any unmanaged simple type.</typeparam>
        public void AddUpload<T>(NativeArray<T> array, int offsetInBytes, int repeatCount = 1) where T : unmanaged
        {
            var size = UnsafeUtility.SizeOf<T>() * array.Length;
            AddUpload(array.GetUnsafeReadOnlyPtr(), size, offsetInBytes, repeatCount);
        }

        /// <summary>
        /// Options for the type of matrix to use in matrix uploads.
        /// </summary>
        public enum MatrixType
        {
            /// <summary>
            /// A float4x4 matrix.
            /// </summary>
            MatrixType4x4,
            /// <summary>
            /// A float3x4 matrix.
            /// </summary>
            MatrixType3x4,
        }

        private void MatrixUploadHelper(void* src, int numMatrices, int offset, int offsetInverse, MatrixType srcType, MatrixType dstType)
        {
            var size   = numMatrices * sizeof(float3x4);
            var opsize = UnsafeUtility.SizeOf<Operation>();

            var allocSucceeded = TryAlloc(opsize, size, out var dst, out var operationOffset, out var dataOffset);

            if (!allocSucceeded)
            {
                Debug.Log("SparseUploader failed to allocate upload memory for AddMatrixUpload operation");
                return;
            }

            if (srcType == MatrixType.MatrixType4x4)
            {
                var srcLocal = (byte*)src;
                var dstLocal = dst + dataOffset;
                for (int i = 0; i < numMatrices; ++i)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        UnsafeUtility.MemCpy(dstLocal, srcLocal, 12);
                        dstLocal += 12;
                        srcLocal += 16;
                    }
                }
            }
            else
            {
                UnsafeUtility.MemCpy(dst + dataOffset, src, size);
            }

            var uploadType  = (offsetInverse == -1) ? (uint)OperationType.Matrix_4x4 : (uint)OperationType.Matrix_Inverse_4x4;
            uploadType     += (dstType == MatrixType.MatrixType3x4) ? 2u : 0u;

            var op = new Operation
            {
                type           = uploadType,
                srcOffset      = (uint)dataOffset,
                dstOffset      = (uint)offset,
                dstOffsetExtra = (uint)offsetInverse,
                size           = (uint)size,
                count          = 1,
            };
            UnsafeUtility.MemCpy(dst + operationOffset, &op, opsize);
        }

        /// <summary>
        /// Adds a new pending matrix upload operation to execute when you call SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// When this operation executes, the SparseUploader copies data from the source pointer.
        /// </remarks>
        /// <param name="src">A pointer to a memory area that contains matrices of the type specified by srcType.</param>
        /// <param name="numMatrices">The number of matrices to upload.</param>
        /// <param name="offset">The destination offset of the copy part of the upload operation.</param>
        /// <param name="srcType">The source matrix format.</param>
        /// <param name="dstType">The destination matrix format.</param>
        public void AddMatrixUpload(void* src, int numMatrices, int offset, MatrixType srcType, MatrixType dstType)
        {
            MatrixUploadHelper(src, numMatrices, offset, -1, srcType, dstType);
        }

        /// <summary>
        /// Adds a new pending matrix upload operation to execute when you call SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// When this operation executes, the SparseUploader copies data from the source pointer.
        ///
        /// The upload operation automatically inverts matrices during the upload operation and it then stores the inverted matrices in a
        /// separate offset in the GPU buffer.
        /// </remarks>
        /// <param name="src">A pointer to a memory area that contains matrices of the type specified by srcType.</param>
        /// <param name="numMatrices">The number of matrices to upload.</param>
        /// <param name="offset">The destination offset of the copy part of the upload operation.</param>
        /// <param name="offsetInverse">The destination offset of the inverse part of the upload operation.</param>
        /// <param name="srcType">The source matrix format.</param>
        /// <param name="dstType">The destination matrix format.</param>
        public void AddMatrixUploadAndInverse(void* src, int numMatrices, int offset, int offsetInverse, MatrixType srcType, MatrixType dstType)
        {
            MatrixUploadHelper(src, numMatrices, offset, offsetInverse, srcType, dstType);
        }

        private void QvvsUploadHelper(void* src, int numQvvs, int offset, int offsetInverse, void* postMatrixSrc = null)
        {
            var size   = numQvvs * sizeof(TransformQvvs);
            var opsize = UnsafeUtility.SizeOf<Operation>();

            var allocSucceeded = TryAlloc(opsize, size, out var dst, out var operationOffset, out var dataOffset);

            if (!allocSucceeded)
            {
                Debug.Log("SparseUploader failed to allocate upload memory for AddMatrixUpload operation");
                return;
            }

            if (postMatrixSrc != null)
            {
                var qvvs     = (TransformQvvs*)src;
                var matrices = (float3x4*)postMatrixSrc;
                var dstMat   = (float3x4*)(dst + dataOffset);
                for (int i = 0; i < numQvvs; i++)
                {
                    var bigPost = new float4x4(new float4(matrices[i].c0, 0f),
                                               new float4(matrices[i].c1, 0f),
                                               new float4(matrices[i].c2, 0f),
                                               new float4(matrices[i].c3, 1f));
                    var bigRes = math.mul(bigPost, qvvs[i].ToMatrix4x4());
                    dstMat[i]  = new float3x4(bigRes.c0.xyz, bigRes.c1.xyz, bigRes.c2.xyz, bigRes.c3.xyz);
                }
            }
            else
                UnsafeUtility.MemCpy(dst + dataOffset, src, size);

            var uploadType = (offsetInverse == -1) ? (uint)OperationType.Qvvs_Matrix_3x4 : (uint)OperationType.Qvvs_Matrix_3x4_Inverse;

            var op = new Operation
            {
                type           = uploadType,
                srcOffset      = (uint)dataOffset,
                dstOffset      = (uint)offset,
                dstOffsetExtra = (uint)offsetInverse,
                size           = (uint)size,
                count          = 1,
            };
            UnsafeUtility.MemCpy(dst + operationOffset, &op, opsize);
        }

        /// <summary>
        /// Adds a new pending QVVS upload operation to execute when you call SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// When this operation executes, the SparseUploader copies data from the source pointer.
        ///
        /// The upload operation automatically converts QVVS transforms into float3x4 matrices on the GPU.
        /// </remarks>
        /// <param name="src">A pointer to a memory area that contains qvvs.</param>
        /// <param name="numQvvs">The number of QVVS transforms to upload.</param>
        /// <param name="offset">The destination offset of the copy part of the upload operation.</param>
        /// <param name="postMatrixSrc">A pointer to a memory area that contains float3x4 matrices to multiply with the qvvs.</param>
        public void AddQvvsUpload(void* src, int numQvvs, int offset, void* postMatrixSrc = null)
        {
            QvvsUploadHelper(src, numQvvs, offset, -1, postMatrixSrc);
        }

        /// <summary>
        /// Adds a new pending matrix upload operation to execute when you call SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// When this operation executes, the SparseUploader copies data from the source pointer.
        ///
        /// The upload operation automatically converts QVVS transforms into float3x4 matrices on the GPU.
        /// Afterwards, it inverts matrices during the upload operation and it then stores the inverted matrices in a
        /// separate offset in the GPU buffer.
        /// </remarks>
        /// <param name="src">A pointer to a memory area that contains qvvs.</param>
        /// <param name="numQvvs">The number of QVVS transforms to upload.</param>
        /// <param name="offset">The destination offset of the copy part of the upload operation.</param>
        /// <param name="offsetInverse">The destination offset of the inverse part of the upload operation.</param>
        /// <param name="postMatrixSrc">A pointer to a memory area that contains float3x4 matrices to multiply with the qvvs.</param>
        public void AddQvvsUploadAndInverse(void* src, int numQvvs, int offset, int offsetInverse, void* postMatrixSrc = null)
        {
            QvvsUploadHelper(src, numQvvs, offset, offsetInverse, postMatrixSrc);
        }

        /// <summary>
        /// Adds a new pending upload operation to execute when you call SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// When this operation executes, the SparseUploader copies data from the source pointer.
        ///
        /// The upload operations reads data with the specified source stride from the source pointer and then stores the data with the specified destination stride.
        /// </remarks>
        /// <param name="src">The source data pointer.</param>
        /// <param name="elemSize">The size of each data element to upload.</param>
        /// <param name="srcStride">The stride of each data element as stored in the source pointer.</param>
        /// <param name="count">The number of data elements to upload.</param>
        /// <param name="dstOffset">The destination offset</param>
        /// <param name="dstStride">The destination stride</param>
        public void AddStridedUpload(void* src, uint elemSize, uint srcStride, uint count, uint dstOffset, int dstStride)
        {
            int  opSize   = UnsafeUtility.SizeOf<Operation>();
            uint dataSize = count * srcStride;

            var allocSucceeded = TryAlloc(opSize, (int)dataSize, out var dst, out var operationOffset, out var dataOffset);

            if (!allocSucceeded)
            {
                Debug.Log("SparseUploader failed to allocate upload memory for AddStridedUpload operation");
                return;
            }

            UnsafeUtility.MemCpy(dst + dataOffset, src, dataSize);
            var op = new Operation
            {
                type           = (uint)OperationType.StridedUpload,
                srcOffset      = (uint)dataOffset,
                srcStride      = srcStride,
                dstOffset      = (uint)dstOffset,
                dstOffsetExtra = 0,
                dstStride      = dstStride,
                size           = elemSize,
                count          = count,
            };
            UnsafeUtility.MemCpy(dst + operationOffset, &op, opSize);
        }
    }

    internal struct NativeStack<T> : IDisposable where T : unmanaged
    {
        NativeList<T> m_buffer;

        public NativeStack(AllocatorManager.AllocatorHandle allocator)
        {
            m_buffer = new NativeList<T>(allocator);
        }

        public void Dispose() => m_buffer.Dispose();

        public bool IsEmpty => m_buffer.IsEmpty;
        public void Push(in T item) => m_buffer.Add(in item);
        public T Pop()
        {
            var result = m_buffer[m_buffer.Length - 1];
            m_buffer.RemoveAt(m_buffer.Length - 1);
            return result;
        }
    }

    internal struct BufferPool : IDisposable
    {
        private NativeList<GraphicsBufferUnmanaged> m_Buffers;
        private NativeStack<int>                    m_FreeBufferIds;
        private NativeStack<int>                    m_BuffersReleased;

        private int                       m_Count;
        private int                       m_Stride;
        private GraphicsBuffer.Target     m_Target;
        private GraphicsBuffer.UsageFlags m_UsageFlags;

        public BufferPool(int count, int stride, GraphicsBuffer.Target target, GraphicsBuffer.UsageFlags usageFlags)
        {
            m_Buffers         = new NativeList<GraphicsBufferUnmanaged>(Allocator.Persistent);
            m_FreeBufferIds   = new NativeStack<int>(Allocator.Persistent);
            m_BuffersReleased = new NativeStack<int>(Allocator.Persistent);

            m_Count      = count;
            m_Stride     = stride;
            m_Target     = target;
            m_UsageFlags = usageFlags;
        }

        public void Dispose()
        {
            for (int i = 0; i < m_Buffers.Length; ++i)
            {
                if (m_Buffers[i].IsValid())
                    m_Buffers[i].Dispose();
            }
            m_FreeBufferIds.Dispose();
            m_BuffersReleased.Dispose();
            m_Buffers.Dispose();
        }

        private int AllocateBuffer()
        {
            var cb  = new GraphicsBufferUnmanaged(m_Target, m_UsageFlags, m_Count, m_Stride);
            cb.name = "LatiosSparseUploaderBuffer";
            if (!m_BuffersReleased.IsEmpty)
            {
                var id        = m_BuffersReleased.Pop();
                m_Buffers[id] = cb;
                return id;
            }
            else
            {
                var id = m_Buffers.Length;
                m_Buffers.Add(cb);
                return id;
            }
        }

        public int GetBufferId()
        {
            if (m_FreeBufferIds.IsEmpty)
                return AllocateBuffer();

            return m_FreeBufferIds.Pop();
        }

        public GraphicsBufferUnmanaged GetBufferFromId(int id)
        {
            return m_Buffers[id];
        }

        public void PutBufferId(int id)
        {
            m_FreeBufferIds.Push(id);
        }

        /*
         * Prune free buffers to allow up to maxMemoryToRetainInBytes to remain.
         * Note that this will only release buffers that are marked free, so the actual memory retained might be higher than requested
         */
        public void PruneFreeBuffers(int maxMemoryToRetainInBytes)
        {
            int memoryToFree = TotalBufferSize - maxMemoryToRetainInBytes;
            if (memoryToFree <= 0)
                return;

            while (memoryToFree > 0 && !m_FreeBufferIds.IsEmpty)
            {
                var id     = m_FreeBufferIds.Pop();
                var buffer = GetBufferFromId(id);
                buffer.Dispose();
                m_BuffersReleased.Push(id);
                memoryToFree -= m_Count * m_Stride;
            }
        }

        public int TotalBufferCount => m_Buffers.Length;
        public int TotalBufferSize => TotalBufferCount * m_Count * m_Stride;
    }

    /// <summary>
    /// Represents SparseUploader statistics.
    /// </summary>
    public struct LatiosSparseUploaderStats
    {
        /// <summary>
        /// The amount of GPU memory the SparseUploader uses internally.
        /// </summary>
        /// <remarks>
        /// This value doesn't include memory in the managed GPU buffer that you pass into the SparseUploader on construction,
        /// or when you use SparseUploader.ReplaceBuffer.
        /// </remarks>
        public long BytesGPUMemoryUsed;

        /// <summary>
        /// The amount of memory the SparseUploader used to upload during the current frame.
        /// </summary>
        public long BytesGPUMemoryUploadedCurr;

        /// <summary>
        /// The highest amount of memory the SparseUploader used for upload during a previous frame.
        /// </summary>
        public long BytesGPUMemoryUploadedMax;
    }

    /// <summary>
    /// Provides utility methods that you can use to upload data into GPU memory.
    /// </summary>
    /// <remarks>
    /// To add uploads from jobs, use a ThreadedSparseUploader which you can create using SparseUploader.Begin.
    /// If you add uploads from jobs, the ThreadedSparseUploader submits them to the GPU in a series of compute shader dispatches when you call SparseUploader.EndAndCommit.
    /// </remarks>
    public unsafe struct LatiosSparseUploader : IDisposable
    {
        const int k_MaxThreadGroupsPerDispatch = 65535;

        int m_BufferChunkSize;

        GraphicsBufferUnmanaged m_DestinationBuffer;

        BufferPool m_UploadBufferPool;

        NativeArray<MappedBuffer> m_MappedBuffers;

        private long m_CurrentFrameUploadSize;
        private long m_MaxUploadSize;

        struct FrameData : IDisposable
        {
            public NativeStack<int> m_Buffers;

            public FrameData(AllocatorManager.AllocatorHandle allocator)
            {
                m_Buffers = new NativeStack<int>(allocator);
            }

            public void Dispose() => m_Buffers.Dispose();
        }

        NativeStack<FrameData> m_FreeFrameData;
        NativeList<FrameData>  m_FrameData;

        ThreadedSparseUploaderData* m_ThreadData;

        UnityObjectRef<ComputeShader> m_SparseUploaderShader;
        int                           m_CopyKernelIndex;
        int                           m_ReplaceKernelIndex;

        int m_SrcBufferID;
        int m_DstBufferID;
        int m_OperationsBaseID;
        int m_ReplaceOperationSize;

        int  m_RequestedUploadBufferPoolMaxSizeBytes;
        bool m_PruneUploadBufferPool;

        uint m_frameIndex;
        bool m_firstUpdate;
        bool m_firstUpdateThisFrame;

        /// <summary>
        /// Constructs a new sparse uploader with the specified buffer as the target.
        /// </summary>
        /// <param name="latiosWorld">The LatiosWorld (needed for GC preservation bug workaround)</param>
        /// <param name="destinationBuffer">The target buffer to write uploads into.</param>
        /// <param name="bufferChunkSize">The upload buffer chunk size.</param>
        public LatiosSparseUploader(LatiosWorld latiosWorld, GraphicsBufferUnmanaged destinationBuffer, int bufferChunkSize = 16 * 1024 * 1024)
        {
            m_BufferChunkSize = bufferChunkSize;

            m_DestinationBuffer = destinationBuffer;

            m_UploadBufferPool = new BufferPool(m_BufferChunkSize / 4, 4, GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite);
            m_MappedBuffers    = new NativeArray<MappedBuffer>();
            m_FreeFrameData    = new NativeStack<FrameData>(Allocator.Persistent);
            m_FrameData        = new NativeList<FrameData>(Allocator.Persistent);

            m_ThreadData = (ThreadedSparseUploaderData*)UnsafeUtility.MallocTracked(sizeof(ThreadedSparseUploaderData),
                                                                                    UnsafeUtility.AlignOf<ThreadedSparseUploaderData>(), Allocator.Persistent, 0);
            m_ThreadData->m_Buffers    = null;
            m_ThreadData->m_NumBuffers = 0;
            m_ThreadData->m_CurrBuffer = 0;

            m_SparseUploaderShader = latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("LatiosSparseUploader");
            m_CopyKernelIndex      = m_SparseUploaderShader.Value.FindKernel("CopyKernel");
            m_ReplaceKernelIndex   = m_SparseUploaderShader.Value.FindKernel("ReplaceKernel");

            m_SrcBufferID          = Shader.PropertyToID("srcBuffer");
            m_DstBufferID          = Shader.PropertyToID("dstBuffer");
            m_OperationsBaseID     = Shader.PropertyToID("operationsBase");
            m_ReplaceOperationSize = Shader.PropertyToID("replaceOperationSize");

            m_CurrentFrameUploadSize = 0;
            m_MaxUploadSize          = 0;

            m_RequestedUploadBufferPoolMaxSizeBytes = 0;
            m_PruneUploadBufferPool                 = false;

            m_frameIndex           = 0;
            m_firstUpdate          = true;
            m_firstUpdateThisFrame = false;
        }

        /// <summary>
        /// Disposes of the SparseUploader.
        /// </summary>
        public void Dispose()
        {
            m_UploadBufferPool.Dispose();
            foreach (var f in m_FrameData)
                f.Dispose();
            while (!m_FreeFrameData.IsEmpty)
                m_FreeFrameData.Pop().Dispose();
            m_FreeFrameData.Dispose();
            m_FrameData.Dispose();
            UnsafeUtility.FreeTracked(m_ThreadData, Allocator.Persistent);
        }

        /// <summary>
        /// Replaces the destination GPU buffer with a new one.
        /// </summary>
        /// <remarks>
        /// If the new buffer is non-null and copyFromPrevious is true, this method
        /// dispatches a copy operation that copies data from the previous buffer to the new one.
        ///
        /// This is useful when the persistent storage buffer needs to grow.
        /// </remarks>
        /// <param name="buffer">The new buffer to replace the old one with.</param>
        /// <param name="copyFromPrevious">Indicates whether to copy the contents of the old buffer to the new buffer.</param>
        public void ReplaceBuffer(GraphicsBufferUnmanaged buffer, bool copyFromPrevious = false)
        {
            if (copyFromPrevious && m_DestinationBuffer.IsValid())
            {
                // Since we have no code such as Graphics.CopyBuffer(dst, src) currently
                // we have to do this ourselves in a compute shader
                var srcSize = m_DestinationBuffer.count * m_DestinationBuffer.stride;
                m_SparseUploaderShader.SetBuffer(m_ReplaceKernelIndex, m_SrcBufferID, m_DestinationBuffer);
                m_SparseUploaderShader.SetBuffer(m_ReplaceKernelIndex, m_DstBufferID, buffer);
                m_SparseUploaderShader.SetInt(m_ReplaceOperationSize, srcSize);

                m_SparseUploaderShader.Dispatch(m_ReplaceKernelIndex, 1, 1, 1);
            }

            m_DestinationBuffer = buffer;
        }

        internal static int NumFramesInFlight
        {
            get
            {
                // The number of frames in flight at the same time
                // depends on the Graphics device that we are using.
                // This number tells how long we need to keep the buffers
                // for a given frame alive. For example, if this is 4,
                // we can reclaim the buffers for a frame after 4 frames have passed.
                int numFrames = 0;

                switch (SystemInfo.graphicsDeviceType)
                {
                    case GraphicsDeviceType.Vulkan:
                    case GraphicsDeviceType.Direct3D11:
                    case GraphicsDeviceType.Direct3D12:
                    case GraphicsDeviceType.PlayStation4:
                    case GraphicsDeviceType.PlayStation5:
                    case GraphicsDeviceType.XboxOne:
                    case GraphicsDeviceType.GameCoreXboxOne:
                    case GraphicsDeviceType.GameCoreXboxSeries:
                    case GraphicsDeviceType.OpenGLCore:
                        // OpenGL ES 2.0 is no longer supported in Unity 2023.1 and later
#if !UNITY_2023_1_OR_NEWER
                    case GraphicsDeviceType.OpenGLES2:
#endif
                    case GraphicsDeviceType.OpenGLES3:
                    case GraphicsDeviceType.PlayStation5NGGC:
                        numFrames = 3;
                        break;
                    case GraphicsDeviceType.Switch:
                    case GraphicsDeviceType.Metal:
                    default:
                        numFrames = 4;
                        break;
                }

                // Use at least as many frames as the quality settings have, but use a platform
                // specific lower limit in any case.
                numFrames = math.max(numFrames, QualitySettings.maxQueuedFrames);

                return numFrames;
            }
        }

        private void RecoverBuffers()
        {
            int numFree = 0;

            // Count frames instead of using async readback to determine completion, because
            // using async readback prevents Unity from letting the device idle, which is really
            // bad for power usage.
            // Add 1 to the device frame count to account for two frames overlapping on
            // CPU side before reaching the GPU.
            int maxBufferedFrames = NumFramesInFlight + 1;

            // If we have more buffered frames than the maximum, free all the excess
            if (m_FrameData.Length > maxBufferedFrames)
                numFree = m_FrameData.Length - maxBufferedFrames;

            for (int i = 0; i < numFree; ++i)
            {
                while (!m_FrameData[i].m_Buffers.IsEmpty)
                {
                    var buffer = m_FrameData[i].m_Buffers.Pop();
                    m_UploadBufferPool.PutBufferId(buffer);
                }
                m_FreeFrameData.Push(m_FrameData[i]);
            }

            if (numFree > 0)
            {
                m_FrameData.RemoveRange(0, numFree);
            }
        }

        /// <summary>
        /// Begins a new upload frame and returns a new ThreadedSparseUploader that is valid until the next call to
        /// SparseUploader.EndAndCommit.
        /// </summary>
        /// <remarks>
        /// You must follow this method with a call to SparseUploader.EndAndCommit later in the frame. You must also pass
        /// the returned value from a Begin method to the next SparseUploader.EndAndCommit.
        /// </remarks>
        /// <param name="maxDataSizeInBytes">An upper bound of total data size that you want to upload this frame.</param>
        /// <param name="biggestDataUpload">The size of the largest upload operation that will occur.</param>
        /// <param name="maxOperationCount">An upper bound of the total number of upload operations that will occur this frame.</param>
        /// <param name="frameID">An ID that should be unique each frame but identical for uploads which occur during the same frame.</param>
        /// <returns>Returns a new ThreadedSparseUploader that must be passed to SparseUploader.EndAndCommit later.</returns>
        public LatiosThreadedSparseUploader Begin(int maxDataSizeInBytes, int biggestDataUpload, int maxOperationCount, uint frameID)
        {
            // If we forgot to finish the previous update, finish it now.
            if (m_ThreadData->m_Buffers != null)
                FrameCleanup();

            // First: recover all buffers from the previous frames (if any)
            if (m_firstUpdate || frameID != m_frameIndex)
            {
                RecoverBuffers();
                m_firstUpdateThisFrame = true;
            }
            m_firstUpdate = false;
            m_frameIndex  = frameID;

            // Second: calculate total size needed this frame, allocate buffers and map what is needed
            var operationSize                   = UnsafeUtility.SizeOf<Operation>();
            var maxOperationSizeInBytes         = maxOperationCount * operationSize;
            var sizeNeeded                      = maxOperationSizeInBytes + maxDataSizeInBytes;
            var bufferSizeWithMaxPaddingRemoved = m_BufferChunkSize - operationSize - biggestDataUpload;
            var numBuffersNeeded                = (sizeNeeded + bufferSizeWithMaxPaddingRemoved - 1) / bufferSizeWithMaxPaddingRemoved;

            if (numBuffersNeeded < 0)
                numBuffersNeeded = 0;

            m_CurrentFrameUploadSize = sizeNeeded;
            if (m_CurrentFrameUploadSize > m_MaxUploadSize)
                m_MaxUploadSize = m_CurrentFrameUploadSize;

            m_MappedBuffers = new NativeArray<MappedBuffer>(numBuffersNeeded, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < numBuffersNeeded; ++i)
            {
                var id             = m_UploadBufferPool.GetBufferId();
                var cb             = m_UploadBufferPool.GetBufferFromId(id);
                var data           = cb.LockBufferForWrite<byte>(0, m_BufferChunkSize);
                var marker         = MappedBuffer.PackMarker(0, m_BufferChunkSize);
                m_MappedBuffers[i] = new MappedBuffer
                {
                    m_Data     = (byte*)data.GetUnsafePtr(),
                    m_Marker   = marker,
                    m_BufferID = id,
                };
            }

            m_ThreadData->m_Buffers    = (MappedBuffer*)m_MappedBuffers.GetUnsafePtr();
            m_ThreadData->m_NumBuffers = numBuffersNeeded;

            // TODO: set safety handle on thread data
            return new LatiosThreadedSparseUploader
            {
                m_Data = m_ThreadData
            };
        }

        private void DispatchUploads(int numOps, GraphicsBufferUnmanaged graphicsBuffer)
        {
            for (int iOp = 0; iOp < numOps; iOp += k_MaxThreadGroupsPerDispatch)
            {
                int opsBegin        = iOp;
                int opsEnd          = math.min(opsBegin + k_MaxThreadGroupsPerDispatch, numOps);
                int numThreadGroups = opsEnd - opsBegin;

                m_SparseUploaderShader.SetBuffer(m_CopyKernelIndex, m_SrcBufferID, graphicsBuffer);
                m_SparseUploaderShader.SetBuffer(m_CopyKernelIndex, m_DstBufferID, m_DestinationBuffer);
                m_SparseUploaderShader.SetInt(m_OperationsBaseID, opsBegin);

                m_SparseUploaderShader.Dispatch(m_CopyKernelIndex, numThreadGroups, 1, 1);
            }
        }

        private void StepFrame()
        {
            // TODO: release safety handle of thread data
            m_ThreadData->m_Buffers    = null;
            m_ThreadData->m_NumBuffers = 0;
            m_ThreadData->m_CurrBuffer = 0;
        }

        /// <summary>
        /// Ends an upload frame and dispatches any upload operations added to the passed in ThreadedSparseUploader.
        /// </summary>
        /// <param name="tsu">The ThreadedSparseUploader to consume and process upload dispatches for. You must have created this with a call to SparseUploader.Begin.</param>
        public void EndAndCommit(LatiosThreadedSparseUploader tsu)
        {
            // Enforce that EndAndCommit is only called with a valid ThreadedSparseUploader
            if (!tsu.IsValid)
            {
                Debug.LogError("Invalid LatiosThreadedSparseUploader passed to EndAndCommit");
                return;
            }

            int numBuffers = m_ThreadData->m_NumBuffers;

            // If there is no work for us to do, early out so we don't add empty entries into m_FrameData
            if (numBuffers == 0 && !m_MappedBuffers.IsCreated)
                return;

            FrameData frameData = default;
            if (m_firstUpdateThisFrame)
                frameData = (!m_FreeFrameData.IsEmpty) ? m_FreeFrameData.Pop() : new FrameData(Allocator.Persistent);
            else
                frameData = m_FrameData[m_FrameData.Length - 1];
            for (int iBuf = 0; iBuf < numBuffers; ++iBuf)
            {
                var mappedBuffer = m_MappedBuffers[iBuf];
                MappedBuffer.UnpackMarker(mappedBuffer.m_Marker, out var operationOffset, out var dataOffset);
                var numOps           = (int)(operationOffset / UnsafeUtility.SizeOf<Operation>());
                var graphicsBufferID = mappedBuffer.m_BufferID;
                var graphicsBuffer   = m_UploadBufferPool.GetBufferFromId(graphicsBufferID);

                if (numOps > 0)
                {
                    graphicsBuffer.UnlockBufferAfterWrite<byte>(m_BufferChunkSize);

                    DispatchUploads(numOps, graphicsBuffer);

                    frameData.m_Buffers.Push(graphicsBufferID);
                }
                else
                {
                    graphicsBuffer.UnlockBufferAfterWrite<byte>(0);
                    m_UploadBufferPool.PutBufferId(graphicsBufferID);
                }
            }

            if (m_firstUpdateThisFrame)
                m_FrameData.Add(frameData);

            if (m_MappedBuffers.IsCreated)
                m_MappedBuffers.Dispose();

            StepFrame();
        }

        /// <summary>
        /// Requests pruning of upload buffers. The actual release will happen in FrameCleanup.
        /// </summary>
        /// <param name="requestedMaxSizeRetainedInBytes">Maximum memory target to keep alive in upload buffer pool. Only buffers marked as free will be pruned, so the memory retained might be more than requested.</param>
        public void PruneUploadBufferPoolOnFrameCleanup(int requestedMaxSizeRetainedInBytes)
        {
            m_RequestedUploadBufferPoolMaxSizeBytes = requestedMaxSizeRetainedInBytes;
            m_PruneUploadBufferPool                 = true;
        }

        /// <summary>
        /// Cleans up internal data and recovers buffers into the free buffer pool.
        /// </summary>
        /// <remarks>
        /// You should call this once per frame.
        /// </remarks>
        public void FrameCleanup()
        {
            var numBuffers = m_ThreadData->m_NumBuffers;

            if (numBuffers > 0)
            {
                // These buffers were never used, so they gets returned to the pool at once
                for (int iBuf = 0; iBuf < numBuffers; ++iBuf)
                {
                    var mappedBuffer = m_MappedBuffers[iBuf];
                    MappedBuffer.UnpackMarker(mappedBuffer.m_Marker, out var operationOffset, out var dataOffset);
                    var graphicsBufferID = mappedBuffer.m_BufferID;
                    var graphicsBuffer   = m_UploadBufferPool.GetBufferFromId(graphicsBufferID);

                    graphicsBuffer.UnlockBufferAfterWrite<byte>(0);
                    m_UploadBufferPool.PutBufferId(graphicsBufferID);
                }

                m_MappedBuffers.Dispose();
            }
            if (m_PruneUploadBufferPool)
            {
                m_UploadBufferPool.PruneFreeBuffers(m_RequestedUploadBufferPoolMaxSizeBytes);
                m_PruneUploadBufferPool = false;
            }

            StepFrame();
        }

        /// <summary>
        /// Calculates statistics about the current and previous frame uploads.
        /// </summary>
        /// <returns>Returns a new statistics struct that contains information about the frame uploads.</returns>
        public LatiosSparseUploaderStats ComputeStats()
        {
            var stats = default(LatiosSparseUploaderStats);

            var totalUploadMemory            = m_UploadBufferPool.TotalBufferSize;
            stats.BytesGPUMemoryUsed         = totalUploadMemory;
            stats.BytesGPUMemoryUploadedCurr = m_CurrentFrameUploadSize;
            stats.BytesGPUMemoryUploadedMax  = m_MaxUploadSize;

            return stats;
        }
    }
}

