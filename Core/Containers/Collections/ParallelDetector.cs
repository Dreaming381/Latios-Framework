using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ParallelDetector : System.IDisposable
    {
        AtomicSafetyHandle m_Safety;
        internal static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<ParallelDetector>();
        int m_Length;
        int m_MinIndex;
        int m_MaxIndex;
        bool relax;

        public bool isParallel
        {
            get
            {
                if (relax)
                    return m_MinIndex > 0 || m_MaxIndex != int.MaxValue;
                else
                    return m_MinIndex != int.MinValue;
            }
        }

        public FixedString128Bytes Print()
        {
            return $"length: {m_Length}, min: {m_MinIndex}, max: {m_MaxIndex}, relax: {relax}";
        }

        /// <summary>
        /// Unity does not provide any differentiation between an IJobFor scheduled single-threaded vs an IJobFor scheduled in parallel with only one thread.
        /// Because of this, safety will be flagged prematurely in some contexts.
        /// </summary>
        public void RelaxForIJobFor()
        {
            relax = true;
        }

        public ParallelDetector(AllocatorManager.AllocatorHandle allocator)
        {
            m_Safety   = CollectionHelper.CreateSafetyHandle(allocator);
            m_Length   = int.MinValue;
            m_MinIndex = int.MinValue;
            m_MaxIndex = int.MinValue;
            relax      = false;
        }

        public void Dispose() => CollectionHelper.DisposeSafetyHandle(ref m_Safety);
    }
#endif
}

