using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for instantiating entities.
    /// This variant does not perform any additional initialization after instantiation.
    /// </summary>
    public struct InstantiateCommandBuffer : INativeDisposable
    {
        #region Structure
        private EntityOperationCommandBuffer m_entityOperationCommandBuffer;
        private NativeReference<bool>        m_playedBack;
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an InstantiateCommandBuffer which can be used to instantiate entities and play them back later.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public InstantiateCommandBuffer(Allocator allocator)
        {
            m_entityOperationCommandBuffer = new EntityOperationCommandBuffer(allocator);
            m_playedBack                   = new NativeReference<bool>(allocator);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this InstantiateCommandBuffer</param>
        /// <returns></returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jh0 = m_entityOperationCommandBuffer.Dispose(inputDeps);
            var jh1 = m_playedBack.Dispose(inputDeps);
            return JobHandle.CombineDependencies(jh0, jh1);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer
        /// </summary>
        public void Dispose()
        {
            m_entityOperationCommandBuffer.Dispose();
            m_playedBack.Dispose();
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
        /// </summary>
        /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        public void Add(Entity entity, int sortKey = int.MaxValue)
        {
            CheckDidNotPlayback();
            m_entityOperationCommandBuffer.Add(entity, sortKey);
        }

        /// <summary>
        /// Plays back the InstantiateCommandBuffer.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the InstantiateCommandBuffer</param>
        public void Playback(EntityManager entityManager)
        {
            CheckDidNotPlayback();
            var eet                = entityManager.BeginExclusiveEntityTransaction();
            new PlaybackJob { eocb = m_entityOperationCommandBuffer, eet = eet }.Run();
            eet.EntityManager.EndExclusiveEntityTransaction();
            m_playedBack.Value = true;
        }

        /// <summary>
        /// Get the number of entities stored in this InstantiateCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this InstantiateCommandBuffer</returns>
        public int Count() => m_entityOperationCommandBuffer.Count();

        /// <summary>
        /// Gets the ParallelWriter for this InstantiateCommandBuffer.
        /// </summary>
        /// <returns>The ParallelWriter which shares this InstantiateCommandBuffer's backing storage.</returns>
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
                throw new System.InvalidOperationException("The InstantiateCommandBuffer has already been played back. You cannot write more commands to it or play it back again.");
#endif
        }

        #region PlaybackJobs
        [BurstCompile]
        private struct PlaybackJob : IJob
        {
            [ReadOnly] public EntityOperationCommandBuffer eocb;
            public ExclusiveEntityTransaction              eet;

            public void Execute()
            {
                var prefabs = eocb.GetEntitiesSortedByEntity(Allocator.Temp);
                int i       = 0;
                while (i < prefabs.Length)
                {
                    var prefab = prefabs[i];
                    i++;
                    int count = 1;
                    while (i < prefabs.Length && prefab == prefabs[i])
                    {
                        i++;
                        count++;
                    }
                    eet.EntityManager.Instantiate(prefab, count, Allocator.Temp);
                }
            }
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// The parallelWriter implementation of InstantiateCommandBuffer. Use AsParallelWriter to obtain one from an InstantiateCommandBuffer
        /// </summary>
        public struct ParallelWriter
        {
            private EntityOperationCommandBuffer.ParallelWriter m_entityOperationCommandBuffer;

            internal ParallelWriter(EntityOperationCommandBuffer eocb)
            {
                m_entityOperationCommandBuffer = eocb.AsParallelWriter();
            }

            /// <summary>
            /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
            /// </summary>
            /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, int sortKey)
            {
                m_entityOperationCommandBuffer.Add(entity, sortKey);
            }
        }
        #endregion
    }

    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for instantiating entities.
    /// This variant initializes the specified component type with specified values per instance.
    /// </summary>
    public struct InstantiateCommandBuffer<T0> : INativeDisposable where T0 : unmanaged, IComponentData
    {
        #region Structure
        internal InstantiateCommandBufferUntyped m_instantiateCommandBufferUntyped { get; private set; }
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an InstantiateCommandBuffer which can be used to instantiate entities and play them back later.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public InstantiateCommandBuffer(Allocator allocator)
        {
            FixedList64<ComponentType> types = new FixedList64<ComponentType>();
            types.Add(ComponentType.ReadWrite<T0>());
            m_instantiateCommandBufferUntyped = new InstantiateCommandBufferUntyped(allocator, types);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this InstantiateCommandBuffer</param>
        /// <returns></returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return m_instantiateCommandBufferUntyped.Dispose(inputDeps);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer
        /// </summary>
        public void Dispose()
        {
            m_instantiateCommandBufferUntyped.Dispose();
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
        /// </summary>
        /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="c0">The first component value to initialize for the instantiated entity</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        public void Add(Entity entity, T0 c0, int sortKey = int.MaxValue)
        {
            m_instantiateCommandBufferUntyped.Add(entity, c0, sortKey);
        }

        /// <summary>
        /// Plays back the InstantiateCommandBuffer.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the InstantiateCommandBuffer</param>
        public void Playback(EntityManager entityManager)
        {
            m_instantiateCommandBufferUntyped.Playback(entityManager);
        }

        /// <summary>
        /// Get the number of entities stored in this InstantiateCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this InstantiateCommandBuffer</returns>
        public int Count() => m_instantiateCommandBufferUntyped.Count();

        /// <summary>
        /// Set additional component types to be added to the instantiated entities. These components will be default-initialized.
        /// </summary>
        /// <param name="tags">The types to add to each instantiated entity</param>
        public void SetComponentTags(ComponentTypes tags)
        {
            m_instantiateCommandBufferUntyped.SetTags(tags);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <param name="tag">The type to add to each instantiated entity</param>
        public void AddComponentTag(ComponentType tag)
        {
            m_instantiateCommandBufferUntyped.AddTag(tag);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <typeparam name="T">The type to add to each instantiated entity</typeparam>
        public void AddComponentTag<T>() where T : struct, IComponentData
        {
            AddComponentTag(ComponentType.ReadOnly<T>());
        }

        /// <summary>
        /// Gets the ParallelWriter for this InstantiateCommandBuffer.
        /// </summary>
        /// <returns>The ParallelWriter which shares this InstantiateCommandBuffer's backing storage.</returns>
        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(m_instantiateCommandBufferUntyped);
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// The parallelWriter implementation of InstantiateCommandBuffer. Use AsParallelWriter to obtain one from an InstantiateCommandBuffer
        /// </summary>
        public struct ParallelWriter
        {
            private InstantiateCommandBufferUntyped.ParallelWriter m_instantiateCommandBufferUntyped;

            internal ParallelWriter(InstantiateCommandBufferUntyped eocb)
            {
                m_instantiateCommandBufferUntyped = eocb.AsParallelWriter();
            }

            /// <summary>
            /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
            /// </summary>
            /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="c0">The first component value to initialize for the instantiated entity</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, T0 c0, int sortKey)
            {
                m_instantiateCommandBufferUntyped.Add(entity, c0, sortKey);
            }
        }
        #endregion
    }

    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for instantiating entities.
    /// This variant initializes both the specified component types with specified values per instance.
    /// </summary>
    public struct InstantiateCommandBuffer<T0, T1> : INativeDisposable where T0 : unmanaged, IComponentData where T1 : unmanaged, IComponentData
    {
        #region Structure
        internal InstantiateCommandBufferUntyped m_instantiateCommandBufferUntyped { get; private set; }
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an InstantiateCommandBuffer which can be used to instantiate entities and play them back later.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public InstantiateCommandBuffer(Allocator allocator)
        {
            FixedList64<ComponentType> types = new FixedList64<ComponentType>();
            types.Add(ComponentType.ReadWrite<T0>());
            types.Add(ComponentType.ReadWrite<T1>());
            m_instantiateCommandBufferUntyped = new InstantiateCommandBufferUntyped(allocator, types);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this InstantiateCommandBuffer</param>
        /// <returns></returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return m_instantiateCommandBufferUntyped.Dispose(inputDeps);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer
        /// </summary>
        public void Dispose()
        {
            m_instantiateCommandBufferUntyped.Dispose();
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
        /// </summary>
        /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="c0">The first component value to initialize for the instantiated entity</param>
        /// <param name="c1">The second component value to initialize for the instantiated entity</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        public void Add(Entity entity, T0 c0, T1 c1, int sortKey = int.MaxValue)
        {
            m_instantiateCommandBufferUntyped.Add(entity, c0, c1, sortKey);
        }

        /// <summary>
        /// Plays back the InstantiateCommandBuffer.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the InstantiateCommandBuffer</param>
        public void Playback(EntityManager entityManager)
        {
            m_instantiateCommandBufferUntyped.Playback(entityManager);
        }

        /// <summary>
        /// Get the number of entities stored in this InstantiateCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this InstantiateCommandBuffer</returns>
        public int Count() => m_instantiateCommandBufferUntyped.Count();

        /// <summary>
        /// Set additional component types to be added to the instantiated entities. These components will be default-initialized.
        /// </summary>
        /// <param name="tags">The types to add to each instantiated entity</param>
        public void SetComponentTags(ComponentTypes tags)
        {
            m_instantiateCommandBufferUntyped.SetTags(tags);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <param name="tag">The type to add to each instantiated entity</param>
        public void AddComponentTag(ComponentType tag)
        {
            m_instantiateCommandBufferUntyped.AddTag(tag);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <typeparam name="T">The type to add to each instantiated entity</typeparam>
        public void AddComponentTag<T>() where T : struct, IComponentData
        {
            AddComponentTag(ComponentType.ReadOnly<T>());
        }

        /// <summary>
        /// Gets the ParallelWriter for this InstantiateCommandBuffer.
        /// </summary>
        /// <returns>The ParallelWriter which shares this InstantiateCommandBuffer's backing storage.</returns>
        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(m_instantiateCommandBufferUntyped);
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// The parallelWriter implementation of InstantiateCommandBuffer. Use AsParallelWriter to obtain one from an InstantiateCommandBuffer
        /// </summary>
        public struct ParallelWriter
        {
            private InstantiateCommandBufferUntyped.ParallelWriter m_instantiateCommandBufferUntyped;

            internal ParallelWriter(InstantiateCommandBufferUntyped icb)
            {
                m_instantiateCommandBufferUntyped = icb.AsParallelWriter();
            }

            /// <summary>
            /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
            /// </summary>
            /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="c0">The first component value to initialize for the instantiated entity</param>
            /// <param name="c1">The second component value to initialize for the instantiated entity</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, T0 c0, T1 c1, int sortKey)
            {
                m_instantiateCommandBufferUntyped.Add(entity, c0, c1, sortKey);
            }
        }
        #endregion
    }

    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for instantiating entities.
    /// This variant initializes the three specified component types with specified values per instance.
    /// </summary>
    public struct InstantiateCommandBuffer<T0, T1, T2> : INativeDisposable where T0 : unmanaged, IComponentData where T1 : unmanaged, IComponentData where T2 : unmanaged,
                                                         IComponentData
    {
        #region Structure
        internal InstantiateCommandBufferUntyped m_instantiateCommandBufferUntyped { get; private set; }
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an InstantiateCommandBuffer which can be used to instantiate entities and play them back later.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public InstantiateCommandBuffer(Allocator allocator)
        {
            FixedList64<ComponentType> types = new FixedList64<ComponentType>();
            types.Add(ComponentType.ReadWrite<T0>());
            types.Add(ComponentType.ReadWrite<T1>());
            types.Add(ComponentType.ReadWrite<T2>());
            m_instantiateCommandBufferUntyped = new InstantiateCommandBufferUntyped(allocator, types);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this InstantiateCommandBuffer</param>
        /// <returns></returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return m_instantiateCommandBufferUntyped.Dispose(inputDeps);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer
        /// </summary>
        public void Dispose()
        {
            m_instantiateCommandBufferUntyped.Dispose();
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
        /// </summary>
        /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="c0">The first component value to initialize for the instantiated entity</param>
        /// <param name="c1">The second component value to initialize for the instantiated entity</param>
        /// <param name="c2">The third component value to initialize for the instantiated entity</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        public void Add(Entity entity, T0 c0, T1 c1, T2 c2, int sortKey = int.MaxValue)
        {
            m_instantiateCommandBufferUntyped.Add(entity, c0, c1, c2, sortKey);
        }

        /// <summary>
        /// Plays back the InstantiateCommandBuffer.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the InstantiateCommandBuffer</param>
        public void Playback(EntityManager entityManager)
        {
            m_instantiateCommandBufferUntyped.Playback(entityManager);
        }

        /// <summary>
        /// Get the number of entities stored in this InstantiateCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this InstantiateCommandBuffer</returns>
        public int Count() => m_instantiateCommandBufferUntyped.Count();

        /// <summary>
        /// Set additional component types to be added to the instantiated entities. These components will be default-initialized.
        /// </summary>
        /// <param name="tags">The types to add to each instantiated entity</param>
        public void SetComponentTags(ComponentTypes tags)
        {
            m_instantiateCommandBufferUntyped.SetTags(tags);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <param name="tag">The type to add to each instantiated entity</param>
        public void AddComponentTag(ComponentType tag)
        {
            m_instantiateCommandBufferUntyped.AddTag(tag);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <typeparam name="T">The type to add to each instantiated entity</typeparam>
        public void AddComponentTag<T>() where T : struct, IComponentData
        {
            AddComponentTag(ComponentType.ReadOnly<T>());
        }

        /// <summary>
        /// Gets the ParallelWriter for this InstantiateCommandBuffer.
        /// </summary>
        /// <returns>The ParallelWriter which shares this InstantiateCommandBuffer's backing storage.</returns>
        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(m_instantiateCommandBufferUntyped);
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// The parallelWriter implementation of InstantiateCommandBuffer. Use AsParallelWriter to obtain one from an InstantiateCommandBuffer
        /// </summary>
        public struct ParallelWriter
        {
            private InstantiateCommandBufferUntyped.ParallelWriter m_instantiateCommandBufferUntyped;

            internal ParallelWriter(InstantiateCommandBufferUntyped icb)
            {
                m_instantiateCommandBufferUntyped = icb.AsParallelWriter();
            }

            /// <summary>
            /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
            /// </summary>
            /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="c0">The first component value to initialize for the instantiated entity</param>
            /// <param name="c1">The second component value to initialize for the instantiated entity</param>
            /// <param name="c2">The third component value to initialize for the instantiated entity</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, T0 c0, T1 c1, T2 c2, int sortKey)
            {
                m_instantiateCommandBufferUntyped.Add(entity, c0, c1, c2, sortKey);
            }
        }
        #endregion
    }

    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for instantiating entities.
    /// This variant initializes the four specified component types with specified values per instance.
    /// </summary>
    public struct InstantiateCommandBuffer<T0, T1, T2, T3> : INativeDisposable where T0 : unmanaged, IComponentData where T1 : unmanaged, IComponentData where T2 : unmanaged,
                                                             IComponentData where T3 : unmanaged, IComponentData
    {
        #region Structure
        internal InstantiateCommandBufferUntyped m_instantiateCommandBufferUntyped { get; private set; }
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an InstantiateCommandBuffer which can be used to instantiate entities and play them back later.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public InstantiateCommandBuffer(Allocator allocator)
        {
            FixedList64<ComponentType> types = new FixedList64<ComponentType>();
            types.Add(ComponentType.ReadWrite<T0>());
            types.Add(ComponentType.ReadWrite<T1>());
            types.Add(ComponentType.ReadWrite<T2>());
            types.Add(ComponentType.ReadWrite<T3>());
            m_instantiateCommandBufferUntyped = new InstantiateCommandBufferUntyped(allocator, types);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this InstantiateCommandBuffer</param>
        /// <returns></returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return m_instantiateCommandBufferUntyped.Dispose(inputDeps);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer
        /// </summary>
        public void Dispose()
        {
            m_instantiateCommandBufferUntyped.Dispose();
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
        /// </summary>
        /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="c0">The first component value to initialize for the instantiated entity</param>
        /// <param name="c1">The second component value to initialize for the instantiated entity</param>
        /// <param name="c2">The third component value to initialize for the instantiated entity</param>
        /// <param name="c3">The fourth component value to initialize for the instantiated entity</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        public void Add(Entity entity, T0 c0, T1 c1, T2 c2, T3 c3, int sortKey = int.MaxValue)
        {
            m_instantiateCommandBufferUntyped.Add(entity, c0, c1, c2, c3, sortKey);
        }

        /// <summary>
        /// Plays back the InstantiateCommandBuffer.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the InstantiateCommandBuffer</param>
        public void Playback(EntityManager entityManager)
        {
            m_instantiateCommandBufferUntyped.Playback(entityManager);
        }

        /// <summary>
        /// Get the number of entities stored in this InstantiateCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this InstantiateCommandBuffer</returns>
        public int Count() => m_instantiateCommandBufferUntyped.Count();

        /// <summary>
        /// Set additional component types to be added to the instantiated entities. These components will be default-initialized.
        /// </summary>
        /// <param name="tags">The types to add to each instantiated entity</param>
        public void SetComponentTags(ComponentTypes tags)
        {
            m_instantiateCommandBufferUntyped.SetTags(tags);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <param name="tag">The type to add to each instantiated entity</param>
        public void AddComponentTag(ComponentType tag)
        {
            m_instantiateCommandBufferUntyped.AddTag(tag);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <typeparam name="T">The type to add to each instantiated entity</typeparam>
        public void AddComponentTag<T>() where T : struct, IComponentData
        {
            AddComponentTag(ComponentType.ReadOnly<T>());
        }

        /// <summary>
        /// Gets the ParallelWriter for this InstantiateCommandBuffer.
        /// </summary>
        /// <returns>The ParallelWriter which shares this InstantiateCommandBuffer's backing storage.</returns>
        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(m_instantiateCommandBufferUntyped);
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// The parallelWriter implementation of InstantiateCommandBuffer. Use AsParallelWriter to obtain one from an InstantiateCommandBuffer
        /// </summary>
        public struct ParallelWriter
        {
            private InstantiateCommandBufferUntyped.ParallelWriter m_instantiateCommandBufferUntyped;

            internal ParallelWriter(InstantiateCommandBufferUntyped icb)
            {
                m_instantiateCommandBufferUntyped = icb.AsParallelWriter();
            }

            /// <summary>
            /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
            /// </summary>
            /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="c0">The first component value to initialize for the instantiated entity</param>
            /// <param name="c1">The second component value to initialize for the instantiated entity</param>
            /// <param name="c2">The third component value to initialize for the instantiated entity</param>
            /// <param name="c3">The fourth component value to initialize for the instantiated entity</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, T0 c0, T1 c1, T2 c2, T3 c3, int sortKey)
            {
                m_instantiateCommandBufferUntyped.Add(entity, c0, c1, c2, c3, sortKey);
            }
        }
        #endregion
    }

    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for instantiating entities.
    /// This variant initializes the four specified component types with specified values per instance.
    /// </summary>
    public struct InstantiateCommandBuffer<T0, T1, T2, T3, T4> : INativeDisposable where T0 : unmanaged, IComponentData where T1 : unmanaged, IComponentData where T2 : unmanaged,
                                                                 IComponentData where T3 : unmanaged, IComponentData where T4 : unmanaged, IComponentData
    {
        #region Structure
        internal InstantiateCommandBufferUntyped m_instantiateCommandBufferUntyped { get; private set; }
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an InstantiateCommandBuffer which can be used to instantiate entities and play them back later.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public InstantiateCommandBuffer(Allocator allocator)
        {
            FixedList64<ComponentType> types = new FixedList64<ComponentType>();
            types.Add(ComponentType.ReadWrite<T0>());
            types.Add(ComponentType.ReadWrite<T1>());
            types.Add(ComponentType.ReadWrite<T2>());
            types.Add(ComponentType.ReadWrite<T3>());
            types.Add(ComponentType.ReadWrite<T4>());
            m_instantiateCommandBufferUntyped = new InstantiateCommandBufferUntyped(allocator, types);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this InstantiateCommandBuffer</param>
        /// <returns></returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return m_instantiateCommandBufferUntyped.Dispose(inputDeps);
        }

        /// <summary>
        /// Disposes the InstantiateCommandBuffer
        /// </summary>
        public void Dispose()
        {
            m_instantiateCommandBufferUntyped.Dispose();
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
        /// </summary>
        /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="c0">The first component value to initialize for the instantiated entity</param>
        /// <param name="c1">The second component value to initialize for the instantiated entity</param>
        /// <param name="c2">The third component value to initialize for the instantiated entity</param>
        /// <param name="c3">The fourth component value to initialize for the instantiated entity</param>
        /// <param name="c4">The fifth component value to initialize for the instantiated entity</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        public void Add(Entity entity, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4, int sortKey = int.MaxValue)
        {
            m_instantiateCommandBufferUntyped.Add(entity, c0, c1, c2, c3, c4, sortKey);
        }

        /// <summary>
        /// Plays back the InstantiateCommandBuffer.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the InstantiateCommandBuffer</param>
        public void Playback(EntityManager entityManager)
        {
            m_instantiateCommandBufferUntyped.Playback(entityManager);
        }

        /// <summary>
        /// Get the number of entities stored in this InstantiateCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this InstantiateCommandBuffer</returns>
        public int Count() => m_instantiateCommandBufferUntyped.Count();

        /// <summary>
        /// Set additional component types to be added to the instantiated entities. These components will be default-initialized.
        /// </summary>
        /// <param name="tags">The types to add to each instantiated entity</param>
        public void SetComponentTags(ComponentTypes tags)
        {
            m_instantiateCommandBufferUntyped.SetTags(tags);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <param name="tag">The type to add to each instantiated entity</param>
        public void AddComponentTag(ComponentType tag)
        {
            m_instantiateCommandBufferUntyped.AddTag(tag);
        }

        /// <summary>
        /// Adds an additional component type to the list of types to add to the instantiated entities. This type will be default-initialized.
        /// </summary>
        /// <typeparam name="T">The type to add to each instantiated entity</typeparam>
        public void AddComponentTag<T>() where T : struct, IComponentData
        {
            AddComponentTag(ComponentType.ReadOnly<T>());
        }

        /// <summary>
        /// Gets the ParallelWriter for this InstantiateCommandBuffer.
        /// </summary>
        /// <returns>The ParallelWriter which shares this InstantiateCommandBuffer's backing storage.</returns>
        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(m_instantiateCommandBufferUntyped);
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// The parallelWriter implementation of InstantiateCommandBuffer. Use AsParallelWriter to obtain one from an InstantiateCommandBuffer
        /// </summary>
        public struct ParallelWriter
        {
            private InstantiateCommandBufferUntyped.ParallelWriter m_instantiateCommandBufferUntyped;

            internal ParallelWriter(InstantiateCommandBufferUntyped icb)
            {
                m_instantiateCommandBufferUntyped = icb.AsParallelWriter();
            }

            /// <summary>
            /// Adds an Entity to the InstantiateCommandBuffer which should be instantiated
            /// </summary>
            /// <param name="entity">The entity to be instantiated, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="c0">The first component value to initialize for the instantiated entity</param>
            /// <param name="c1">The second component value to initialize for the instantiated entity</param>
            /// <param name="c2">The third component value to initialize for the instantiated entity</param>
            /// <param name="c3">The fourth component value to initialize for the instantiated entity</param>
            /// <param name="c4">The fifth component value to initialize for the instantiated entity</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4, int sortKey)
            {
                m_instantiateCommandBufferUntyped.Add(entity, c0, c1, c2, c3, c4, sortKey);
            }
        }
        #endregion
    }
}

