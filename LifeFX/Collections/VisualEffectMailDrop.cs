using System.Diagnostics;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    [NativeContainer]
    public struct VisualEffectMailDrop<T> where T : unmanaged
    {
        public void Add(in T message)
        {
            CheckSafeToAccess();
            m_blockList.Write(message, m_nativeThreadIndex);
        }

        UnsafeParallelBlockList m_blockList;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle m_Safety;
        static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<VisualEffectMailDrop<T> >();
#endif

        [NativeSetThreadIndex] int m_nativeThreadIndex;

        internal VisualEffectMailDrop(UnsafeParallelBlockList blockList)
        {
            m_blockList         = blockList;
            m_nativeThreadIndex = JobsUtility.ThreadIndex;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.GetTempMemoryHandle();
            CollectionHelper.SetStaticSafetyId<VisualEffectMailDrop<T> >(ref m_Safety, ref s_staticSafetyId.Data);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckSafeToAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }
    }
}

