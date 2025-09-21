using Unity.Jobs.LowLevel.Unsafe;

namespace Latios.Unsafe
{
    /// <summary>
    /// A non-deterministic typed double-buffer that can be written to in parallel.
    /// The results can then be gathered atomically, and then read until the next swap.
    /// Use this container in a SharedStatic for typed debug diagnostics, such as debug drawing or stats collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public struct AtomicDoubleBuffer<T> where T : unmanaged
    {
        private UnsafeParallelBlockList<T> writeBuffer;
        private UnsafeParallelBlockList<T> readBuffer;

        /// <summary>
        /// Gets a writer for the current thread, atomically locking the thread index.
        /// Multiple calls within the same thread are allowed, as long as the instance
        /// created by the first caller is the last to be disposed (typical call stack
        /// behavior). There are no safety checks for this nor for forgetting to dispose
        /// this instance, and the result can be race conditions and deadlocks. Please take care.
        /// </summary>
        public Writer GetWriter_PleaseRememberUsingExpression()
        {
            return new Writer(ref writeBuffer);
        }

        /// <summary>
        /// Gets a reader for all threads, atomically locking all of them. This must
        /// only be called once, and not called again until the instance is disposed.
        /// There are no safety checks for this nor for forgetting to dispose  this
        /// instance, and the result can be race conditions and deadlocks. Only call
        /// this in the main thread or a single-threaded job. And please take care.
        /// </summary>
        /// <returns></returns>
        public Reader GetReader_PleaseRememberUsingExpression()
        {
            return new Reader(ref readBuffer);
        }

        public void SwapBuffers()
        {
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
            {
                writeBuffer.LockIndexReentrant(i, 3, 0);
                readBuffer.LockIndexReentrant(i, 3, 0);
            }
            // We own all the thread indices now, which means there's no active readers or writers,
            // and therefore no external references to these buffers. So we can simply swap them.
            // Note: This is why readers and writers have their constructors using ref, so that
            // they inherit the swapping once they are unlocked.
            (writeBuffer, readBuffer) = (readBuffer, writeBuffer);
            writeBuffer.Clear();

            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
            {
                writeBuffer.UnlockIndex(i, 3, 0);
                readBuffer.UnlockIndex(i, 3, 0);
            }
        }

        /// <summary>
        /// A disposable writer. Multiple instances can exist on the stack, as long as they are disposed in reverse-order.
        /// </summary>
        public ref struct Writer
        {
            UnsafeParallelBlockList<T> writeBuffer;
            int                        threadIndex;
            bool                       isUnlocker;

            public void Write(in T value)
            {
                writeBuffer.Write(in value, threadIndex);
            }

            public void Dispose()
            {
                if (isUnlocker)
                    writeBuffer.UnlockIndex(threadIndex, 1, 0);
            }

            internal Writer(ref UnsafeParallelBlockList<T> writeable)
            {
                threadIndex = JobsUtility.ThreadIndex;
                isUnlocker  = writeable.LockIndexReentrant(threadIndex, 1, 0);
                writeBuffer = writeable;
            }
        }

        /// <summary>
        /// A disposable reader.
        /// </summary>
        public ref struct Reader
        {
            UnsafeParallelBlockList<T> readBuffer;

            public int Count => readBuffer.Count();
            public UnsafeIndexedBlockList<T>.AllIndicesEnumerator GetEnumerator() => readBuffer.GetEnumerator();
            public void Dispose()
            {
                for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
                {
                    readBuffer.UnlockIndex(i, 2, 0);
                }
            }

            internal Reader(ref UnsafeParallelBlockList<T> readable)
            {
                for (int i = 0; i < JobsUtility.ThreadIndexCount; i++)
                {
                    readable.LockIndexReentrant(i, 2, 0);
                }
                readBuffer = readable;
            }
        }
    }
}

