using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation
{
    /// <summary>
    /// A Burst-compatible API for various graphics operations
    /// </summary>
    public static unsafe class GraphicsUnmanaged
    {
        #region Public API
        /// <summary>
        /// Initialize the GraphicsUnmanaged APIs. Call this if you intend to use these APIs before Kinemation's systems are installed.
        /// </summary>
        public static void Initialize()
        {
            if (initialized)
                return;

            buffers               = new List<GraphicsBuffer>();
            handles.Data.freeList = new UnsafeList<int>(128, Allocator.Persistent);
            handles.Data.versions = new UnsafeList<int>(128, Allocator.Persistent);

            managedDelegate                 = ManagedExecute;
            handles.Data.managedFunctionPtr = new FunctionPointer<ManagedDelegate>(Marshal.GetFunctionPointerForDelegate<ManagedDelegate>(ManagedExecute));

            initialized = true;

            // important: this will always be called from a special unload thread (main thread will be blocking on this)
            AppDomain.CurrentDomain.DomainUnload += (_, __) => { Shutdown(); };

            // There is no domain unload in player builds, so we must be sure to shutdown when the process exits.
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { Shutdown(); };
        }

        /// <summary>
        /// Sets a global buffer property for all shaders. Equivalent to Shader.SetGlobalBuffer()
        /// </summary>
        /// <param name="propertyId">The name ID of the poperty retrived by Shader.PropertyToID</param>
        /// <param name="graphicsBuffer">The buffer to set.</param>
        public static void SetGlobalBuffer(int propertyId, GraphicsBufferUnmanaged graphicsBuffer)
        {
            graphicsBuffer.CheckValid();
            var context = new SetGlobalBufferContext
            {
                propertyId = propertyId,
                listIndex  = graphicsBuffer.index,
                success    = false
            };
            DoManagedExecute((IntPtr)(&context), 8);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Setting the Graphics Buffer globally failed.");
#endif
        }
        #endregion

        #region Extensions
        /// <summary>
        /// Sets an input or output compute buffer.
        /// </summary>
        /// <param name="kernelIndex">For which kernel the buffer is being set. See ComputeShader.FindKernel().</param>
        /// <param name="propertyId">Property name ID, use Shader.PropertyToID() to get it.</param>
        /// <param name="graphicsBuffer">Buffer to set.</param>
        public static void SetBuffer(this UnityObjectRef<ComputeShader> computeShader, int kernelIndex, int propertyId, GraphicsBufferUnmanaged graphicsBuffer)
        {
            graphicsBuffer.CheckValid();
            var context = new ComputeShaderSetBufferContext
            {
                computeShader = computeShader,
                kernelIndex   = kernelIndex,
                propertyId    = propertyId,
                listIndex     = graphicsBuffer.index,
                success       = false
            };
            DoManagedExecute((IntPtr)(&context), 5);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Setting the Graphics Buffer for the Compute Shader failed.");
#endif
        }

        /// <summary>
        /// Set an integer parameter
        /// </summary>
        /// <param name="propertyId">Property name ID, use Shader.PropertyToID() to get it.</param>
        /// <param name="integer">Value to set.</param>
        public static void SetInt(this UnityObjectRef<ComputeShader> computeShader, int propertyId, int integer)
        {
            var context = new ComputeShaderSetIntContext
            {
                computeShader = computeShader,
                propertyId    = propertyId,
                integer       = integer,
                success       = false
            };
            DoManagedExecute((IntPtr)(&context), 6);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Setting the Int for the Compute Shader failed.");
#endif
        }

        /// <summary>
        /// Execute a compute shader.
        /// </summary>
        /// <param name="kernelIndex">Which kernel to exeucte. A single compute shader asset can have multiple kernel entry points.</param>
        /// <param name="threadGroupsX">Number of work groups in the X dimension.</param>
        /// <param name="threadGroupsY">Number of work groups in the Y dimension.</param>
        /// <param name="threadGroupsZ">Number of work groups in the Z dimension.</param>
        public static void Dispatch(this UnityObjectRef<ComputeShader> computeShader, int kernelIndex, int threadGroupsX, int threadGroupsY, int threadGroupsZ)
        {
            var context = new ComputeShaderDispatchContext
            {
                computeShader = computeShader,
                kernelIndex   = kernelIndex,
                threadGroupsX = threadGroupsX,
                threadGroupsY = threadGroupsY,
                threadGroupsZ = threadGroupsZ,
                success       = false
            };
            DoManagedExecute((IntPtr)(&context), 7);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Dispatching the Compute Shader failed.");
#endif
        }

        /// <summary>
        /// Get kernel thread group sizes.
        /// </summary>
        /// <param name="kernelIndex">Which kernel to exeucte. A single compute shader asset can have multiple kernel entry points.</param>
        /// <param name="x">Thread group size in the X dimension.</param>
        /// <param name="y">Thread group size in the Y dimension.</param>
        /// <param name="z">Thread group size in the Z dimension.</param>
        public static void GetKernelThreadGroupSizes(this UnityObjectRef<ComputeShader> computeShader, int kernelIndex, out uint x, out uint y, out uint z)
        {
            var context = new GetComputShaderThreadGroupSizesContext
            {
                computeShader = computeShader,
                kernelIndex   = kernelIndex,
                success       = false
            };
            DoManagedExecute((IntPtr)(&context), 9);
            x = context.x;
            y = context.y;
            z = context.z;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Dispatching the Compute Shader failed.");
#endif
        }
        #endregion

        #region Internal
        internal static GraphicsBufferUnmanaged CreateGraphicsBuffer(GraphicsBuffer.Target target, GraphicsBuffer.UsageFlags usageFlags, int count, int stride)
        {
            int  listIndex    = -1;
            bool appendToList = false;
            if (handles.Data.freeList.IsEmpty)
            {
                listIndex = handles.Data.versions.Length;
                handles.Data.versions.Add(1);
                appendToList = true;
            }
            else
            {
                listIndex = handles.Data.freeList[handles.Data.freeList.Length - 1];
                handles.Data.freeList.Length--;
                handles.Data.versions[listIndex]++;
                appendToList = false;
            }
            var context = new GraphicsBufferCreateContext
            {
                target       = target,
                usageFlags   = usageFlags,
                count        = count,
                stride       = stride,
                listIndex    = listIndex,
                appendToList = appendToList,
                success      = false
            };
            DoManagedExecute((IntPtr)(&context), 1);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Creating the GraphicsBufferUnmanaged failed.");
#endif
            return new GraphicsBufferUnmanaged { index = listIndex, version = handles.Data.versions[listIndex] };
        }

        internal static void DisposeGraphicsBuffer(GraphicsBufferUnmanaged unmanaged)
        {
            handles.Data.versions[unmanaged.index]++;
            handles.Data.freeList.Add(unmanaged.index);
            var context = new GraphicsBufferDisposeContext { listIndex = unmanaged.index };
            DoManagedExecute((IntPtr)(&context), 2);
        }

        internal static bool IsValid(GraphicsBufferUnmanaged unmanaged)
        {
            return unmanaged.version == handles.Data.versions[unmanaged.index];
        }

        internal static GraphicsBuffer GetAtIndex(int index) => buffers[index];

        internal static NativeArray<byte> GraphicsBufferLockForWrite(GraphicsBufferUnmanaged unmanaged, int byteOffset, int byteCount)
        {
            var context = new GraphicsBufferLockForWriteContext
            {
                bytes      = default,
                listIndex  = unmanaged.index,
                byteOffset = byteOffset,
                byteCount  = byteCount,
                success    = false
            };
            DoManagedExecute((IntPtr)(&context), 3);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Locking the GraphicsBufferUnmanaged for write failed.");
#endif
            return context.bytes;
        }

        internal static void GraphicsBufferUnlockAfterWrite(GraphicsBufferUnmanaged unmanaged, int byteCount)
        {
            var context = new GraphicsBufferUnlockAfterWriteContext
            {
                listIndex = unmanaged.index,
                byteCount = byteCount,
                success   = false
            };
            DoManagedExecute((IntPtr)(&context), 4);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Unlocking the GraphicsBufferUnmanaged after write failed.");
#endif
        }

        internal static int GetGraphicsBufferCount(GraphicsBufferUnmanaged unmanaged)
        {
            var context = new GetGraphicsBufferCountContext
            {
                listIndex = unmanaged.index,
                count     = 0,
                success   = false
            };
            DoManagedExecute((IntPtr)(&context), 10);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Failed to obtain the count from the GraphicsBufferUnmanaged.");
#endif
            return context.count;
        }

        internal static int GetGraphicsBufferStride(GraphicsBufferUnmanaged unmanaged)
        {
            var context = new GetGraphicsBufferStrideContext
            {
                listIndex = unmanaged.index,
                stride    = 0,
                success   = false
            };
            DoManagedExecute((IntPtr)(&context), 11);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Failed to obtain the stride from the GraphicsBufferUnmanaged.");
#endif
            return context.stride;
        }

        internal static void SetGraphicsBufferName(GraphicsBufferUnmanaged unmanaged, in FixedString128Bytes name)
        {
            var context = new SetGraphicsBufferNameContext
            {
                listIndex = unmanaged.index,
                name      = name,
                success   = false
            };
            DoManagedExecute((IntPtr)(&context), 12);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!context.success)
                throw new System.InvalidOperationException("Failed to set the name for the GraphicsBufferUnmanaged.");
#endif
        }
        #endregion

        #region State
        static List<GraphicsBuffer>              buffers;
        static readonly SharedStatic<HandleData> handles     = SharedStatic<HandleData>.GetOrCreate<HandleData>();
        static bool                              initialized = false;

        private delegate void ManagedDelegate(IntPtr context, int operation);
        static ManagedDelegate managedDelegate;

        struct HandleData
        {
            public UnsafeList<int>                  versions;
            public UnsafeList<int>                  freeList;
            public FunctionPointer<ManagedDelegate> managedFunctionPtr;
        }

        static void Shutdown()
        {
            if (!initialized)
                return;
            foreach (var buffer in buffers)
                buffer?.Dispose();
            buffers = null;
            handles.Data.freeList.Dispose();
            handles.Data.versions.Dispose();
            managedDelegate = null;
            initialized     = false;
        }
        #endregion

        #region Contexts
        // Code 1
        struct GraphicsBufferCreateContext
        {
            public GraphicsBuffer.Target     target;
            public GraphicsBuffer.UsageFlags usageFlags;
            public int                       count;
            public int                       stride;
            public int                       listIndex;
            public bool                      appendToList;
            public bool                      success;
        }

        // Code 2
        struct GraphicsBufferDisposeContext
        {
            public int listIndex;
        }

        // Code 3
        struct GraphicsBufferLockForWriteContext
        {
            public NativeArray<byte> bytes;
            public int               listIndex;
            public int               byteOffset;
            public int               byteCount;
            public bool              success;
        }

        // Code 4
        struct GraphicsBufferUnlockAfterWriteContext
        {
            public int  listIndex;
            public int  byteCount;
            public bool success;
        }

        // Code 5
        struct ComputeShaderSetBufferContext
        {
            public UnityObjectRef<ComputeShader> computeShader;
            public int                           kernelIndex;
            public int                           propertyId;
            public int                           listIndex;
            public bool                          success;
        }

        // Code 6
        struct ComputeShaderSetIntContext
        {
            public UnityObjectRef<ComputeShader> computeShader;
            public int                           propertyId;
            public int                           integer;
            public bool                          success;
        }

        // Code 7
        struct ComputeShaderDispatchContext
        {
            public UnityObjectRef<ComputeShader> computeShader;
            public int                           kernelIndex;
            public int                           threadGroupsX;
            public int                           threadGroupsY;
            public int                           threadGroupsZ;
            public bool                          success;
        }

        // Code 8
        struct SetGlobalBufferContext
        {
            public int  propertyId;
            public int  listIndex;
            public bool success;
        }

        // Code 9
        struct GetComputShaderThreadGroupSizesContext
        {
            public UnityObjectRef<ComputeShader> computeShader;
            public int                           kernelIndex;
            public uint                          x;
            public uint                          y;
            public uint                          z;
            public bool                          success;
        }

        // Code 10
        struct GetGraphicsBufferCountContext
        {
            public int  listIndex;
            public int  count;
            public bool success;
        }

        // Code 11
        struct GetGraphicsBufferStrideContext
        {
            public int  listIndex;
            public int  stride;
            public bool success;
        }

        // Code 12
        struct SetGraphicsBufferNameContext
        {
            public int                 listIndex;
            public FixedString128Bytes name;
            public bool                success;
        }
        #endregion

        static void DoManagedExecute(IntPtr context, int operation)
        {
            bool didIt = false;
            ManagedExecuteFromManaged(context, operation, ref didIt);

            if (!didIt)
                handles.Data.managedFunctionPtr.Invoke(context, operation);
        }

        [BurstDiscard]
        static void ManagedExecuteFromManaged(IntPtr context, int operation, ref bool didIt)
        {
            didIt = true;
            ManagedExecute(context, operation);
        }

        [MonoPInvokeCallback(typeof(ManagedDelegate))]
        static void ManagedExecute(IntPtr context, int operation)
        {
            try
            {
                switch (operation)
                {
                    case 1:
                    {
                        ref var ctx = ref *(GraphicsBufferCreateContext*)context;
                        if (ctx.appendToList)
                            buffers.Add(default); // Gaurd against desync if constructor throws

                        var buffer             = new GraphicsBuffer(ctx.target, ctx.usageFlags, ctx.count, ctx.stride);
                        buffers[ctx.listIndex] = buffer;
                        ctx.success            = true;
                        break;
                    }
                    case 2:
                    {
                        var index = ((GraphicsBufferDisposeContext*)context)->listIndex;
                        buffers[index].Dispose();
                        buffers[index] = null;
                        break;
                    }
                    case 3:
                    {
                        ref var ctx    = ref *(GraphicsBufferLockForWriteContext*)context;
                        var     buffer = buffers[ctx.listIndex];
                        ctx.bytes      = buffer.LockBufferForWrite<byte>(ctx.byteOffset, ctx.byteCount);
                        ctx.success    = true;
                        break;
                    }
                    case 4:
                    {
                        ref var ctx    = ref *(GraphicsBufferUnlockAfterWriteContext*)context;
                        var     buffer = buffers[ctx.listIndex];
                        buffer.UnlockBufferAfterWrite<byte>(ctx.byteCount);
                        ctx.success = true;
                        break;
                    }
                    case 5:
                    {
                        ref var       ctx    = ref *(ComputeShaderSetBufferContext*)context;
                        var           buffer = buffers[ctx.listIndex];
                        ComputeShader shader = ctx.computeShader;
                        shader.SetBuffer(ctx.kernelIndex, ctx.propertyId, buffer);
                        ctx.success = true;
                        break;
                    }
                    case 6:
                    {
                        ref var       ctx    = ref *(ComputeShaderSetIntContext*)context;
                        ComputeShader shader = ctx.computeShader;
                        shader.SetInt(ctx.propertyId, ctx.integer);
                        ctx.success = true;
                        break;
                    }
                    case 7:
                    {
                        ref var       ctx    = ref *(ComputeShaderDispatchContext*)context;
                        ComputeShader shader = ctx.computeShader;
                        shader.Dispatch(ctx.kernelIndex, ctx.threadGroupsX, ctx.threadGroupsY, ctx.threadGroupsZ);
                        ctx.success = true;
                        break;
                    }
                    case 8:
                    {
                        ref var ctx    = ref *(SetGlobalBufferContext*)context;
                        var     buffer = buffers[ctx.listIndex];
                        Shader.SetGlobalBuffer(ctx.propertyId, buffer);
                        ctx.success = true;
                        break;
                    }
                    case 9:
                    {
                        ref var       ctx    = ref *(GetComputShaderThreadGroupSizesContext*)context;
                        ComputeShader shader = ctx.computeShader;
                        shader.GetKernelThreadGroupSizes(ctx.kernelIndex, out ctx.x, out ctx.y, out ctx.z);
                        ctx.success = true;
                        break;
                    }
                    case 10:
                    {
                        ref var ctx    = ref *(GetGraphicsBufferCountContext*)context;
                        var     buffer = buffers[ctx.listIndex];
                        ctx.count      = buffer.count;
                        ctx.success    = true;
                        break;
                    }
                    case 11:
                    {
                        ref var ctx    = ref *(GetGraphicsBufferStrideContext*)context;
                        var     buffer = buffers[ctx.listIndex];
                        ctx.stride     = buffer.stride;
                        ctx.success    = true;
                        break;
                    }
                    case 12:
                    {
                        ref var ctx    = ref *(SetGraphicsBufferNameContext*)context;
                        var     buffer = buffers[ctx.listIndex];
                        buffer.name    = ctx.name.ToString();
                        ctx.success    = true;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }
    }
}

