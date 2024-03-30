using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for disabling entities.
    /// Disabled entities automatically account for LinkedEntityGroup at the time of playback.
    /// </summary>
    [BurstCompile]
    public unsafe struct DisableCommandBuffer : INativeDisposable
    {
        #region Structure
        private EntityOperationCommandBuffer m_entityOperationCommandBuffer;
        private NativeReference<bool>        m_playedBack;
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an DisableCommandBuffer which can be used to disable entities and play them back later.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public DisableCommandBuffer(AllocatorManager.AllocatorHandle allocator)
        {
            m_entityOperationCommandBuffer = new EntityOperationCommandBuffer(allocator);
            m_playedBack                   = new NativeReference<bool>(allocator);
        }

        /// <summary>
        /// Disposes the DisableCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this DisableCommandBuffer</param>
        /// <returns></returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jh0 = m_entityOperationCommandBuffer.Dispose(inputDeps);
            var jh1 = m_playedBack.Dispose(inputDeps);
            return JobHandle.CombineDependencies(jh0, jh1);
        }

        /// <summary>
        /// Disposes the DisableCommandBuffer
        /// </summary>
        public void Dispose()
        {
            m_entityOperationCommandBuffer.Dispose();
            m_playedBack.Dispose();
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the DisableCommandBuffer which should be disabled
        /// </summary>
        /// <param name="entity">The entity to be disabled, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        public void Add(Entity entity, int sortKey = int.MaxValue)
        {
            CheckDidNotPlayback();
            m_entityOperationCommandBuffer.Add(entity, sortKey);
        }

        /// <summary>
        /// Plays back the DisableCommandBuffer.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the DisableCommandBuffer</param>
        /// <param name="linkedFEReadOnly">A ReadOnly accessor to the entities' LinkedEntityGroup</param>
        public unsafe void Playback(EntityManager entityManager, BufferLookup<LinkedEntityGroup> linkedFEReadOnly)
        {
            CheckDidNotPlayback();
            Playbacker.Playback((DisableCommandBuffer*)UnsafeUtility.AddressOf(ref this), (EntityManager*)UnsafeUtility.AddressOf(ref entityManager),
                                (BufferLookup<LinkedEntityGroup>*)UnsafeUtility.AddressOf(ref linkedFEReadOnly));
        }

        /// <summary>
        /// Get the number of entities stored in this DisableCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this DisableCommandBuffer</returns>
        public int Count() => m_entityOperationCommandBuffer.Count();

        /// <summary>
        /// Gets the ParallelWriter for this DisableCommandBuffer.
        /// </summary>
        /// <returns>The ParallelWriter which shares this DisableCommandBuffer's backing storage.</returns>
        public ParallelWriter AsParallelWriter()
        {
            CheckDidNotPlayback();
            return new ParallelWriter(m_entityOperationCommandBuffer);
        }
        #endregion

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckDidNotPlayback()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_playedBack.Value == true)
                throw new System.InvalidOperationException("The DisableCommandBuffer has already been played back. You cannot write more commands to it or play it back again.");
#endif
        }

        #region PlaybackJobs
        [BurstCompile]
        static class Playbacker
        {
            [BurstCompile]
            public static unsafe void Playback(DisableCommandBuffer* dcb, EntityManager* em, BufferLookup<LinkedEntityGroup>* lookup)
            {
                em->AddComponent<Disabled>(dcb->m_entityOperationCommandBuffer.GetLinkedEntities(*lookup, Allocator.Temp));
                dcb->m_playedBack.Value = true;
            }
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// The parallelWriter implementation of DisableCommandBuffer. Use AsParallelWriter to obtain one from an DisableCommandBuffer
        /// </summary>
        public struct ParallelWriter
        {
            private EntityOperationCommandBuffer.ParallelWriter m_entityOperationCommandBuffer;

            internal ParallelWriter(EntityOperationCommandBuffer eocb)
            {
                m_entityOperationCommandBuffer = eocb.AsParallelWriter();
            }

            /// <summary>
            /// Adds an Entity to the DisableCommandBuffer which should be disabled
            /// </summary>
            /// <param name="entity">The entity to be disabled, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, int sortKey = int.MaxValue)
            {
                m_entityOperationCommandBuffer.Add(entity, sortKey);
            }
        }
        #endregion
    }
}

