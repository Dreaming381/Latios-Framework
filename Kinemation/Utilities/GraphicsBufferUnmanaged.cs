using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Latios.Kinemation
{
    /// <summary>
    /// A Burst-compatible wrapper around a GraphicsBuffer that can be used in Burst contexts.
    /// Constructing one will still allocate a managed object tracked by GC.
    /// </summary>
    public struct GraphicsBufferUnmanaged : IDisposable
    {
        internal int index;
        internal int version;

        /// <summary>
        /// A Null instance of a GraphicsBufferUnmanaged
        /// </summary>
        public static GraphicsBufferUnmanaged Null => default;

        /// <summary>
        /// Construct a new GraphicsBufferUnmanaged backed by a managed GraphicsBuffer.
        /// </summary>
        /// <param name="target">Specify how this buffer can be used within the graphics pipeline.</param>
        /// <param name="usageFlags">Select what kind of update mode the buffer will have.</param>
        /// <param name="count">Number of elements in the buffer.</param>
        /// <param name="stride">Size of one element in the buffer. For index buffers, this must be either 2 or 4 bytes.</param>
        public GraphicsBufferUnmanaged(GraphicsBuffer.Target target, GraphicsBuffer.UsageFlags usageFlags, int count, int stride)
        {
            this = GraphicsUnmanaged.CreateGraphicsBuffer(target, usageFlags, count, stride);
        }

        /// <summary>
        /// Determines if this instance still wraps a valid GraphicsBuffer.
        /// It is possible for an instance to be invalid if a copy of the instance was disposed.
        /// </summary>
        /// <returns>True if the underlying GraphicsBuffer object is valid</returns>
        public bool IsValid() => GraphicsUnmanaged.IsValid(this);

        /// <summary>
        /// Retrieves the managed GraphicsBuffer instance if valid, null otherwise.
        /// </summary>
        /// <returns>The managed UnityEngine.GraphicsBuffer instance, or null</returns>
        public GraphicsBuffer ToManaged() => IsValid() ? GraphicsUnmanaged.GetAtIndex(index) : null;

        /// <summary>
        /// Disposes this instance and the underlying GraphicsBuffer, if valid
        /// </summary>
        public void Dispose()
        {
            if (IsValid())
                GraphicsUnmanaged.DisposeGraphicsBuffer(this);
            this = Null;
        }

        /// <summary>
        /// Begins a write operation to the buffer
        /// </summary>
        /// <typeparam name="T">The type of element to write</typeparam>
        /// <param name="bufferStartIndex">The index of an element where the write operation begins.</param>
        /// <param name="count">Maximum number of elements which will be written</param>
        /// <returns>A NativeArray of size count</returns>
        public NativeArray<T> LockBufferForWrite<T>(int bufferStartIndex, int count) where T : unmanaged
        {
            CheckValid();
            var size   = UnsafeUtility.SizeOf<T>();
            var result = GraphicsUnmanaged.GraphicsBufferLockForWrite(this, bufferStartIndex * size, count * size);
            return result.Reinterpret<T>(1);
        }

        /// <summary>
        /// Ends a write operation to the buffer
        /// </summary>
        /// <typeparam name="T">The type of elements written</typeparam>
        /// <param name="countWritten">Number of elements written to the buffer. Counted from the first element.</param>
        public void UnlockBufferAfterWrite<T>(int countWritten) where T : unmanaged
        {
            CheckValid();
            var size = UnsafeUtility.SizeOf<T>();
            GraphicsUnmanaged.GraphicsBufferUnlockAfterWrite(this, countWritten * size);
        }

        /// <summary>
        /// Number of elements in the buffer (Read Only).
        /// </summary>
        public int count
        {
            get
            {
                CheckValid();
                return GraphicsUnmanaged.GetGraphicsBufferCount(this);
            }
        }

        /// <summary>
        /// Size of one element in the buffer. For index buffers, this must be either 2 or 4 bytes (Read Only).
        /// </summary>
        public int stride
        {
            get
            {
                CheckValid();
                return GraphicsUnmanaged.GetGraphicsBufferStride(this);
            }
        }

        /// <summary>
        /// The debug label for the GraphicsBuffer (setter only).
        /// </summary>
        public FixedString128Bytes name
        {
            set
            {
                CheckValid();
                GraphicsUnmanaged.SetGraphicsBufferName(this, value);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void CheckValid()
        {
            if (!IsValid())
                throw new NullReferenceException("The GraphicsBufferUnmanaged is not valid.");
        }
    }
}

