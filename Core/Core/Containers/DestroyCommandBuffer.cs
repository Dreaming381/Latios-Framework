using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for destroying entities.
    /// Destroyed entities automatically account for LinkedEntityGroup at the time of playback.
    /// </summary>
    public unsafe struct DestroyCommandBuffer : INativeDisposable
    {
        #region Structure
        private EntityOperationCommandBuffer m_entityOperationCommandBuffer;
        private NativeReference<bool>        m_playedBack;
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an DestroyCommandBuffer which can be used to destroy entities and play them back later.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public DestroyCommandBuffer(Allocator allocator)
        {
            m_entityOperationCommandBuffer = new EntityOperationCommandBuffer(allocator);
            m_playedBack                   = new NativeReference<bool>(allocator);
        }

        /// <summary>
        /// Disposes the DestroyCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this DestroyCommandBuffer</param>
        /// <returns></returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jh0 = m_entityOperationCommandBuffer.Dispose(inputDeps);
            var jh1 = m_playedBack.Dispose(inputDeps);
            return JobHandle.CombineDependencies(jh0, jh1);
        }

        /// <summary>
        /// Disposes the DestroyCommandBuffer
        /// </summary>
        public void Dispose()
        {
            m_entityOperationCommandBuffer.Dispose();
            m_playedBack.Dispose();
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the DestroyCommandBuffer which should be destroyed
        /// </summary>
        /// <param name="entity">The entity to be destroyed, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        public void Add(Entity entity, int sortKey = int.MaxValue)
        {
            CheckDidNotPlayback();
            m_entityOperationCommandBuffer.Add(entity, sortKey);
        }

        /// <summary>
        /// Plays back the DestroyCommandBuffer.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the DestroyCommandBuffer</param>
        /// <param name="linkedFEReadOnly">A ReadOnly accessor to the entities' LinkedEntityGroup</param>
        public void Playback(EntityManager entityManager)
        {
            CheckDidNotPlayback();
            bool ran      = false;
            var  entities = RunPrepInJob(ref ran);
            if (ran)
            {
                entityManager.DestroyEntity(entities);
                entities.Dispose();
            }
            else
            {
                entityManager.DestroyEntity(m_entityOperationCommandBuffer.GetEntities(Allocator.Temp));
            }
            m_playedBack.Value = true;
        }

        /// <summary>
        /// Get the number of entities stored in this DestroyCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this DestroyCommandBuffer</returns>
        public int Count() => m_entityOperationCommandBuffer.Count();

        /// <summary>
        /// Gets the ParallelWriter for this DestroyCommandBuffer.
        /// </summary>
        /// <returns>The ParallelWriter which shares this DestroyCommandBuffer's backing storage.</returns>
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
                throw new System.InvalidOperationException("The DestroyCommandBuffer has already been played back. You cannot write more commands to it or play it back again.");
#endif
        }

        #region PlaybackJobs
        [BurstDiscard]
        private NativeList<Entity> RunPrepInJob(ref bool ran)
        {
            ran                = true;
            var entities       = new NativeList<Entity>(0, Allocator.TempJob);
            new PrepJob { eocb = m_entityOperationCommandBuffer, entities = entities }.Run();
            return entities;
        }

        [BurstCompile]
        private struct PrepJob : IJob
        {
            [ReadOnly] public EntityOperationCommandBuffer eocb;
            public NativeList<Entity>                      entities;

            public void Execute()
            {
                eocb.GetEntities(ref entities);
            }
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// The parallelWriter implementation of DestroyCommandBuffer. Use AsParallelWriter to obtain one from an DestroyCommandBuffer
        /// </summary>
        public struct ParallelWriter
        {
            private EntityOperationCommandBuffer.ParallelWriter m_entityOperationCommandBuffer;

            internal ParallelWriter(EntityOperationCommandBuffer eocb)
            {
                m_entityOperationCommandBuffer = eocb.AsParallelWriter();
            }

            /// <summary>
            /// Adds an Entity to the DestroyCommandBuffer which should be destroyed
            /// </summary>
            /// <param name="entity">The entity to be destroyed, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, int sortKey)
            {
                m_entityOperationCommandBuffer.Add(entity, sortKey);
            }
        }
        #endregion
    }
}

