using System.Diagnostics;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// This is a special container for writing a list of entities in parallel and retrieving a deterministic list of entities on a single thread.
    /// This container is used internally by several of the specialized ECB variants. However, you can also use it directly to create your own operations.
    /// </summary>
    [NativeContainer]
    public unsafe struct EntityOperationCommandBuffer : INativeDisposable
    {
        #region Structure
        [NativeDisableUnsafePtrRestriction]
        private UnsafeParallelBlockList* m_blockList;

        [NativeDisableUnsafePtrRestriction]
        private State* m_state;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        //Unfortunately this name is hardcoded into Unity. No idea how EntityCommandBuffer gets away with multiple safety handles.
        AtomicSafetyHandle m_Safety;
        static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<EntityOperationCommandBuffer>();
#endif

        private struct State
        {
            public AllocatorManager.AllocatorHandle allocator;
        }

        private struct EntityWithOperation : IRadixSortableInt, IRadixSortableInt3
        {
            public Entity entity;
            public int    sortKey;

            public int GetKey() => sortKey;

            public int3 GetKey3()
            {
                return new int3(entity.Index, entity.Version, sortKey);
            }
        }
        #endregion

        #region CreateDestroy
        /// <summary>
        /// Create an EntityOperationCommandBuffer which can be used to write entities from multiple threads and retrieve them in a deterministic order.
        /// </summary>
        /// <param name="allocator">The type of allocator to use for allocating the buffer</param>
        public EntityOperationCommandBuffer(AllocatorManager.AllocatorHandle allocator) : this(allocator, 1)
        {
        }

        internal EntityOperationCommandBuffer(AllocatorManager.AllocatorHandle allocator, int disposeSentinalStackDepth)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CheckAllocator(allocator);

            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
            CollectionHelper.SetStaticSafetyId<EntityOperationCommandBuffer>(ref m_Safety, ref s_staticSafetyId.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            m_blockList  = AllocatorManager.Allocate<UnsafeParallelBlockList>(allocator, 1);
            m_state      = AllocatorManager.Allocate<State>(allocator, 1);
            *m_blockList = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<EntityWithOperation>(), 256, allocator);
            *m_state     = new State
            {
                allocator = allocator,
            };
        }

        [BurstCompile]
        private struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public State* state;

            [NativeDisableUnsafePtrRestriction]
            public UnsafeParallelBlockList* blockList;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public void Execute()
            {
                Deallocate(state, blockList);
            }
        }

        /// <summary>
        /// Disposes the EntityOperationCommandBuffer after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this EntityOperationCommandBuffer</param>
        /// <returns>The JobHandle for the disposing job scheduled</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jobHandle = new DisposeJob
            {
                blockList = m_blockList,
                state     = m_state,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = m_Safety
#endif
            }.Schedule(inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            m_blockList = null;
            m_state     = null;
            return jobHandle;
        }

        /// <summary>
        /// Disposes the EntityOperationCommandBuffer
        /// </summary>
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif
            Deallocate(m_state, m_blockList);
            m_blockList = null;
            m_state     = null;
        }

        private static void Deallocate(State* state, UnsafeParallelBlockList* blockList)
        {
            var allocator = state->allocator;
            blockList->Dispose();
            AllocatorManager.Free(allocator, blockList, 1);
            AllocatorManager.Free(allocator, state,     1);
        }
        #endregion

        #region PublicAPI
        /// <summary>
        /// Adds an Entity to the EntityOperationCommandBuffer which should be operated on
        /// </summary>
        /// <param name="entity">The entity to be operated on, including its LinkedEntityGroup at the time of playback if it has one</param>
        /// <param name="sortKey">The sort key for deterministic playback if interleaving single and parallel writes</param>
        [WriteAccessRequired]
        public void Add(Entity entity, int sortKey = int.MaxValue)
        {
            CheckWriteAccess();
            m_blockList->Write(new EntityWithOperation { entity = entity, sortKey = sortKey }, 0);
        }

        /// <summary>
        /// Get the number of entities stored in this EntityOperationCommandBuffer. This method performs a summing operation on every invocation.
        /// </summary>
        /// <returns>The number of elements stored in this EntityOperationCommandBuffer</returns>
        public int Count()
        {
            CheckReadAccess();
            return m_blockList->Count();
        }

        /// <summary>
        /// Returns an array of entities stored in the EntityOperationCommandBuffer ordered by SortKey.
        /// </summary>
        /// <param name="allocator">The allocator to use for the returned NativeArray</param>
        /// <returns>The array of entities stored in the EntityOperationCommandBuffer ordered by SortKey</returns>
        public NativeArray<Entity> GetEntities(Allocator allocator)
        {
            CheckReadAccess();
            int count    = m_blockList->Count();
            var entities = new NativeArray<Entity>(count, allocator, NativeArrayOptions.UninitializedMemory);

            GetEntities(entities);

            return entities;
        }

        /// <summary>
        /// Returns an array of entities stored in the EntityOperationCommandBuffer ordered by Entity and then by SortKey.
        /// </summary>
        /// <param name="allocator">The allocator to use for the returned NativeArray</param>
        /// <returns>The array of entities stored in the EntityOperationCommandBuffer ordered by Entity and then by SortKey</returns>
        public NativeArray<Entity> GetEntitiesSortedByEntity(Allocator allocator)
        {
            CheckReadAccess();
            int count    = m_blockList->Count();
            var entities = new NativeArray<Entity>(count, allocator, NativeArrayOptions.UninitializedMemory);

            GetEntitiesSorted(entities);

            return entities;
        }

        /// <summary>
        /// Fills the native list with entities stored in the EntityOperationCommandBuffer sorted by SortKey
        /// </summary>
        /// <param name="entities">The list to fill. The list will automatically be resized to fit the new entities.</param>
        /// <param name="append">If true, entities will be appended. If false, the list will be overwritten.</param>
        public void GetEntities(ref NativeList<Entity> entities, bool append = false)
        {
            CheckReadAccess();
            int count = m_blockList->Count();

            if (append)
            {
                int originalLength = entities.Length;
                entities.ResizeUninitialized(originalLength + count);
                var subArray = entities.AsArray().GetSubArray(originalLength, count);
                GetEntities(subArray);
            }
            else
            {
                entities.ResizeUninitialized(count);
                GetEntities(entities.AsArray());
            }
        }

        /// <summary>
        /// Returns an array of entities stored in the EntityOperationCommandBuffer ordered by SortKey and their LinkedEntityGroup entities if they have them.
        /// </summary>
        /// <param name="linkedLookupReadOnly">A ReadOnly accessor to the Entities' LinkedEntityGroup</param>
        /// <param name="allocator">The allocator to use for the returned NativeArray</param>
        /// <returns>The array of entities stored in the EntityOperationCommandBuffer ordered by SortKey and their LinkedEntityGroup entities if they have them.</returns>
        public NativeArray<Entity> GetLinkedEntities(BufferLookup<LinkedEntityGroup> linkedLookupReadOnly, Allocator allocator)
        {
            GetLinkedEntitiesInternal(linkedLookupReadOnly, out _, Allocator.Temp, out var entities, allocator);
            return entities;
        }

        /// <summary>
        /// Returns an array of entities stored in the EntityOperationCommandBuffer ordered by SortKey and their LinkedEntityGroup entities if they have them.
        /// This override also returns the root entities stored in the EntityOperationCommandBuffer as a separate array.
        /// </summary>
        /// <param name="linkedLookupReadOnly">A ReadOnly accessor to the Entities' LinkedEntityGroup</param>
        /// <param name="allocator">The allocator to use for the returned NativeArray</param>
        /// <param name="roots">An array of entities in the EntityOperationCommandBuffer excluding their LinkedEntityGroup entities</param>
        /// <returns>The array of entities stored in the EntityOperationCommandBuffer ordered by SortKey and their LinkedEntityGroup entities if they have them.</returns>
        public NativeArray<Entity> GetLinkedEntities(BufferLookup<LinkedEntityGroup> linkedLookupReadOnly, Allocator allocator, out NativeArray<Entity> roots)
        {
            CheckReadAccess();
            GetLinkedEntitiesInternal(linkedLookupReadOnly, out roots, allocator, out var entities, allocator);
            return entities;
        }

        /// <summary>
        /// Fills the native list with entities stored in the EntityOperationCommandBuffer sorted by SortKey and their LinkedEntityGroup entities if they have them.
        /// </summary>
        /// <param name="linkedLookupReadOnly">A ReadOnly accessor to the Entities' LinkedEntityGroup</param>
        /// <param name="entities">The list to fill. The list will automatically be resized to fit the new entities.</param>
        /// <param name="append">If true, entities will be appended. If false, the list will be overwritten.</param>
        public void GetLinkedEntities(BufferLookup<LinkedEntityGroup> linkedLookupReadOnly, ref NativeList<Entity> entities, bool append = false)
        {
            CheckReadAccess();
            var roots = GetEntities(Allocator.Temp);
            int count = GetLinkedEntitiesCount(linkedLookupReadOnly, roots);
            if (append)
            {
                int originalLength = entities.Length;
                entities.ResizeUninitialized(originalLength + count);
                var subArray = entities.AsArray().GetSubArray(originalLength, count);
                GetLinkedEntitiesFill(linkedLookupReadOnly, roots, subArray);
            }
            else
            {
                entities.ResizeUninitialized(count);
                GetLinkedEntitiesFill(linkedLookupReadOnly, roots, entities.AsArray());
            }
        }

        public ParallelWriter AsParallelWriter()
        {
            var writer = new ParallelWriter(m_blockList, m_state);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            writer.m_Safety = m_Safety;
            CollectionHelper.SetStaticSafetyId<ParallelWriter>(ref writer.m_Safety, ref ParallelWriter.s_staticSafetyId.Data);
#endif
            return writer;
        }
        #endregion

        #region Implementations
        private void GetEntities(NativeArray<Entity> entities)
        {
            var tempEntitiesWithOperation = new NativeArray<EntityWithOperation>(entities.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var ranks                     = new NativeArray<int>(entities.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            m_blockList->GetElementValues(tempEntitiesWithOperation);

            //Radix sort
            RadixSort.RankSortInt(ranks, tempEntitiesWithOperation);

            //Copy results
            for (int i = 0; i < ranks.Length; i++)
            {
                entities[i] = tempEntitiesWithOperation[ranks[i]].entity;
            }
        }

        private void GetEntitiesSorted(NativeArray<Entity> entities)
        {
            var tempEntitiesWithOperation = new NativeArray<EntityWithOperation>(entities.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var ranks                     = new NativeArray<int>(entities.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            m_blockList->GetElementValues(tempEntitiesWithOperation);

            //Radix sort
            RadixSort.RankSortInt3(ranks, tempEntitiesWithOperation);

            //Copy results
            for (int i = 0; i < ranks.Length; i++)
            {
                entities[i] = tempEntitiesWithOperation[ranks[i]].entity;
            }
        }

        private int GetLinkedEntitiesCount(BufferLookup<LinkedEntityGroup> linkedLookup, NativeArray<Entity> roots)
        {
            int count = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                if (linkedLookup.HasBuffer(roots[i]))
                {
                    count += linkedLookup[roots[i]].Length;
                }
                else
                {
                    count++;
                }
            }
            return count;
        }

        private void GetLinkedEntitiesFill(BufferLookup<LinkedEntityGroup> linkedLookup, NativeArray<Entity> roots, NativeArray<Entity> entities)
        {
            int count = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                if (linkedLookup.HasBuffer(roots[i]))
                {
                    var currentGroup = linkedLookup[roots[i]];
                    NativeArray<Entity>.Copy(currentGroup.AsNativeArray().Reinterpret<Entity>(), 0, entities, count, currentGroup.Length);
                    count += currentGroup.Length;
                }
                else
                {
                    entities[count] = roots[i];
                    count++;
                }
            }
        }

        private void GetLinkedEntitiesInternal(BufferLookup<LinkedEntityGroup> linkedLookup,
                                               out NativeArray<Entity>         roots,
                                               Allocator rootsAllocator,
                                               out NativeArray<Entity>         linkedEntities,
                                               Allocator linkedAllocator)
        {
            CheckReadAccess();
            roots          = GetEntities(rootsAllocator);
            int count      = GetLinkedEntitiesCount(linkedLookup, roots);
            linkedEntities = new NativeArray<Entity>(count, linkedAllocator, NativeArrayOptions.UninitializedMemory);
            GetLinkedEntitiesFill(linkedLookup, roots, linkedEntities);
        }
        #endregion

        #region Checks
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckWriteAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckReadAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckAllocator(AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (allocator.ToAllocator <= Allocator.None)
                throw new System.InvalidOperationException("Allocator cannot be Invalid or None");
#endif
        }
        #endregion

        #region ParallelWriter
        /// <summary>
        /// Implements ParallelWriter of the EntityOperationCommandBuffer. Use AsParallelWriter to obtain one from the EntityOperationCommandBuffer.
        /// </summary>
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction]
            private UnsafeParallelBlockList* m_blockList;

            [NativeDisableUnsafePtrRestriction]
            private State* m_state;

            [NativeSetThreadIndex]
            private int m_ThreadIndex;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            //More ugly Unity naming
            internal AtomicSafetyHandle m_Safety;
            internal static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<ParallelWriter>();
#endif

            internal ParallelWriter(UnsafeParallelBlockList* blockList, void* state)
            {
                m_blockList   = blockList;
                m_state       = (State*)state;
                m_ThreadIndex = 0;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = default;
#endif
            }

            /// <summary>
            /// Adds an Entity to the EntityOperationCommandBuffer which should be operated on
            /// </summary>
            /// <param name="entity">The entity to be operated on, including its LinkedEntityGroup at the time of playback if it has one</param>
            /// <param name="sortKey">The sort key for deterministic playback</param>
            public void Add(Entity entity, int sortKey)
            {
                CheckWriteAccess();
                m_blockList->Write(new EntityWithOperation { entity = entity, sortKey = sortKey }, m_ThreadIndex);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckWriteAccess()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }
        }
        #endregion
    }
}

