using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// A specialized variant of the EntityCommandBuffer exclusively for destroying entities.
    /// Destroyed entities automatically account for LinkedEntityGroup at the time of playback.
    /// </summary>
    [BurstCompile]
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
        public DestroyCommandBuffer(AllocatorManager.AllocatorHandle allocator)
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
        public void Playback(EntityManager entityManager)
        {
            CheckDidNotPlayback();
            Playbacker.Playback((DestroyCommandBuffer*)UnsafeUtility.AddressOf(ref this), (EntityManager*)UnsafeUtility.AddressOf(ref entityManager));
        }
        /// <summary>
        /// Plays back the DestroyCommandBuffer using a custom batching strategy.
        /// </summary>
        /// <param name="entityManager">The EntityManager with which to play back the DestroyCommandBuffer</param>
        /// <param name="legLookup">A ReadWrite lookup of LinkedEntityGroup dynamic buffers</param>
        /// <param name="esil">A lookup for info about a given entity's chunk and position in it</param>
        public void Playback(EntityManager entityManager, BufferLookup<LinkedEntityGroup> legLookup, EntityStorageInfoLookup esil)
        {
            CheckDidNotPlayback();
            Playbacker.Playback(ref this, ref entityManager, ref legLookup, ref esil);
        }

        /// <summary>
        /// Performs the DestroyCommandBuffer custom batching algorithm on a provided array of entities.
        /// In some cases, this can be faster than calling EntityManager.DestroyEntity() directly.
        /// </summary>
        /// <param name="entityManager">The EntityManager that the entities to be destroyed belong to</param>
        /// <param name="entities">The entities to be destroyed</param>
        /// <param name="legLookup">A ReadWrite lookup of LinkedEntityGroup dynamic buffers</param>
        /// <param name="esil">A lookup for info about a given entity's chunk and position in it</param>
        public static void DestroyEntitiesWithBatching(EntityManager entityManager,
                                                       NativeArray<Entity>             entities,
                                                       BufferLookup<LinkedEntityGroup> legLookup,
                                                       EntityStorageInfoLookup esil)
        {
            Playbacker.PlaybackArray(ref entities, ref entityManager, ref legLookup, ref esil);
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
        [BurstCompile]
        static class Playbacker
        {
            [BurstCompile]
            public static unsafe void Playback(DestroyCommandBuffer* dcb, EntityManager* em)
            {
                var entities = dcb->m_entityOperationCommandBuffer.GetEntities(Allocator.Temp);
                //using (new Unity.Profiling.ProfilerMarker($"Destroying_{entities.Length}_root_entities").Auto())
                {
                    em->DestroyEntity(entities);
                }
                dcb->m_playedBack.Value = true;
            }

            static readonly Unity.Profiling.ProfilerMarker sBatchEntities = new Unity.Profiling.ProfilerMarker("Batch_entities");

            [BurstCompile]
            public static unsafe void Playback(ref DestroyCommandBuffer dcb, ref EntityManager em, ref BufferLookup<LinkedEntityGroup> legLookup, ref EntityStorageInfoLookup esil)
            {
                var entities = dcb.m_entityOperationCommandBuffer.GetEntities(Allocator.TempJob);

                if (entities.Length < 512)
                {
                    PlaybackOnThread(ref entities, ref em, ref legLookup);
                }
                else
                {
                    PlaybackWithJobs(ref entities, ref em, ref legLookup, ref esil);
                }
                entities.Dispose();
            }

            [BurstCompile]
            public static unsafe void PlaybackArray(ref NativeArray<Entity>             entities,
                                                    ref EntityManager em,
                                                    ref BufferLookup<LinkedEntityGroup> legLookup,
                                                    ref EntityStorageInfoLookup esil)
            {
                if (entities.Length < 512)
                    PlaybackOnThread(ref entities, ref em, ref legLookup);
                else
                {
                    // Copy the array, because we don't know what it was allocated with
                    var array = new NativeArray<Entity>(entities, Allocator.TempJob);
                    PlaybackWithJobs(ref array, ref em, ref legLookup, ref esil);
                    array.Dispose();
                }
            }

            static unsafe void PlaybackOnThread(ref NativeArray<Entity> entities, ref EntityManager em, ref BufferLookup<LinkedEntityGroup> legLookup)
            {
                sBatchEntities.Begin();
                // Collect LEGs and count LEG entities
                var legs           = new UnsafeList<DynamicBuffer<LinkedEntityGroup> >(entities.Length, Allocator.Temp);
                int legEntityCount = 0;
                foreach (var entity in entities)
                {
                    if (legLookup.TryGetBuffer(entity, out var leg))
                    {
                        var toAdd = leg.Length - 1;
                        if (toAdd > 0)
                        {
                            legEntityCount += toAdd;
                            legs.AddNoResize(leg);
                        }
                        else if (toAdd == 0)
                        {
                            leg.Clear();
                        }
                    }
                }

                // Find chunks for entities and deduplicate
                var maxChunkCount      = entities.Length + legEntityCount;
                var chunks             = new UnsafeList<ChunkData>(maxChunkCount, Allocator.Temp);
                var chunkMap           = new UnsafeHashMap<int, int>(maxChunkCount, Allocator.Temp);
                var entityWithInfoList = new UnsafeList<EntityWithInfo>(entities.Length + legEntityCount, Allocator.Temp);
                foreach (var entity in entities)
                {
                    if (!em.Exists(entity))
                        continue;
                    var esi = em.GetStorageInfo(entity);
                    if (!chunkMap.TryGetValue(esi.Chunk.GetHashCode(), out var chunkIndex))
                    {
                        chunkIndex                                         = chunks.Length;
                        chunks.AddNoResize(new ChunkData { chunkTotalCount = (byte)esi.Chunk.Count });
                        chunkMap.Add(esi.Chunk.GetHashCode(), chunkIndex);
                    }
                    entityWithInfoList.AddNoResize(new EntityWithInfo { entity = entity, chunkIndex = chunkIndex, indexInChunk = esi.IndexInChunk });

                    ref var chunk = ref chunks.ElementAt(chunkIndex);
                    if (esi.IndexInChunk >= 64)
                        chunk.upper.SetBits(esi.IndexInChunk - 64, true);
                    else
                        chunk.lower.SetBits(esi.IndexInChunk, true);
                }
                foreach (var leg in legs)
                {
                    var legArray = leg.AsNativeArray();
                    for (int i = 1; i < legArray.Length; i++)
                    {
                        var entity = legArray[i].Value;
                        if (!em.Exists(entity))
                            continue;
                        var esi = em.GetStorageInfo(entity);
                        if (!chunkMap.TryGetValue(esi.Chunk.GetHashCode(), out var chunkIndex))
                        {
                            chunkIndex                                         = chunks.Length;
                            chunks.AddNoResize(new ChunkData { chunkTotalCount = (byte)esi.Chunk.Count });
                            chunkMap.Add(esi.Chunk.GetHashCode(), chunkIndex);
                        }
                        entityWithInfoList.AddNoResize(new EntityWithInfo { entity = entity, chunkIndex = chunkIndex, indexInChunk = esi.IndexInChunk });
                        ref var chunk                                                                                              = ref chunks.ElementAt(chunkIndex);
                        if (esi.IndexInChunk >= 64)
                            chunk.upper.SetBits(esi.IndexInChunk - 64, true);
                        else
                            chunk.lower.SetBits(esi.IndexInChunk, true);
                    }
                    leg.Clear();
                }
                // Prefix sum chunks
                int sum = 0;
                for (int i = 0; i < chunks.Length; i++)
                {
                    ref var chunk     = ref chunks.ElementAt(i);
                    chunk.prefixSum   = sum;
                    chunk.lowerCount  = (byte)chunk.lower.CountBits();
                    chunk.count       = (byte)(chunk.lowerCount + chunk.upper.CountBits());
                    sum              += chunk.count;
                }
                // Assign
                var destroyArray = new NativeArray<Entity>(sum, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                foreach (var entity in entityWithInfoList)
                {
                    var chunk = chunks[entity.chunkIndex];
                    if (entity.indexInChunk < 64)
                    {
                        var offset                             = math.countbits(chunk.lower.Value & ((1ul << entity.indexInChunk) - 1));
                        destroyArray[chunk.prefixSum + offset] = entity.entity;
                    }
                    else
                    {
                        var offset                             = chunk.lowerCount + math.countbits(chunk.upper.Value & ((1ul << (entity.indexInChunk - 64)) - 1));
                        destroyArray[chunk.prefixSum + offset] = entity.entity;
                    }
                }
                //foreach (var chunk in chunks)
                //{
                //    var span = destroyArray.AsSpan().Slice(chunk.prefixSum, chunk.count);
                //    ReorderChunk(chunk, span);
                //}
                sBatchEntities.End();
                em.DestroyEntity(destroyArray);
            }

            static unsafe void PlaybackWithJobs(ref NativeArray<Entity>             entities,
                                                ref EntityManager em,
                                                ref BufferLookup<LinkedEntityGroup> legLookup,
                                                ref EntityStorageInfoLookup esil)
            {
                sBatchEntities.Begin();
                var rootsWithInfo = new NativeArray<EntityWithInfo>(entities.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var legPrefixSum  = new NativeArray<int>(entities.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var rootsJh = new FindRootsJob
                {
                    roots         = entities,
                    esil          = esil,
                    legLookup     = legLookup,
                    rootsWithInfo = rootsWithInfo,
                    legCounts     = legPrefixSum,
                }.ScheduleParallel(entities.Length, 16, default);

                var legTotal     = new NativeReference<int>(Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var legsWithInfo = new NativeList<EntityWithInfo>(Allocator.TempJob);
                rootsJh          = new PrefixSumLegsJob
                {
                    legPrefixSums = legPrefixSum,
                    legTotal      = legTotal,
                    legsWithInfo  = legsWithInfo
                }.Schedule(rootsJh);
                var legsJh = new FindLegsJob
                {
                    roots         = entities,
                    esil          = esil,
                    legLookup     = legLookup,
                    legsWithInfo  = legsWithInfo.AsDeferredJobArray(),
                    legPrefixSums = legPrefixSum,
                }.ScheduleParallel(entities.Length, 16, rootsJh);

                rootsJh.Complete();
                var maxChunkCount = entities.Length + legTotal.Value;
                var chunks        = new UnsafeList<ChunkData>(maxChunkCount, Allocator.Temp);
                var chunkMap      = new UnsafeHashMap<int, int>(maxChunkCount, Allocator.Temp);
                for (int i = 0; i < rootsWithInfo.Length; i++)
                {
                    var entity = rootsWithInfo[i];
                    if (entity.entity == Entity.Null)
                        continue;
                    if (!chunkMap.TryGetValue(entity.chunkIndex, out var chunkIndex))
                    {
                        chunkIndex                                         = chunks.Length;
                        chunks.AddNoResize(new ChunkData { chunkTotalCount = (byte)em.GetChunkCountFromChunkHashcode(entity.chunkIndex) });
                        chunkMap.Add(entity.chunkIndex, chunkIndex);
                    }
                    entity.chunkIndex = chunkIndex;
                    rootsWithInfo[i]  = entity;
                    ref var chunk     = ref chunks.ElementAt(chunkIndex);
                    if (entity.indexInChunk >= 64)
                        chunk.upper.SetBits(entity.indexInChunk - 64, true);
                    else
                        chunk.lower.SetBits(entity.indexInChunk, true);
                }

                legsJh.Complete();
                for (int i = 0; i < legsWithInfo.Length; i++)
                {
                    var entity = legsWithInfo[i];
                    if (entity.entity == Entity.Null)
                        continue;
                    if (!chunkMap.TryGetValue(entity.chunkIndex, out var chunkIndex))
                    {
                        chunkIndex                                         = chunks.Length;
                        chunks.AddNoResize(new ChunkData { chunkTotalCount = (byte)em.GetChunkCountFromChunkHashcode(entity.chunkIndex) });
                        chunkMap.Add(entity.chunkIndex, chunkIndex);
                    }
                    entity.chunkIndex = chunkIndex;
                    legsWithInfo[i]   = entity;
                    ref var chunk     = ref chunks.ElementAt(chunkIndex);
                    if (entity.indexInChunk >= 64)
                        chunk.upper.SetBits(entity.indexInChunk - 64, true);
                    else
                        chunk.lower.SetBits(entity.indexInChunk, true);
                }

                int sum = 0;
                for (int i = 0; i < chunks.Length; i++)
                {
                    ref var chunk     = ref chunks.ElementAt(i);
                    chunk.prefixSum   = sum;
                    chunk.lowerCount  = (byte)chunk.lower.CountBits();
                    chunk.count       = (byte)(chunk.lowerCount + chunk.upper.CountBits());
                    sum              += chunk.count;
                }
                // Assign
                var destroyArray = new NativeArray<Entity>(sum, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                foreach (var entity in rootsWithInfo)
                {
                    if (entity.entity == Entity.Null)
                        continue;
                    var chunk = chunks[entity.chunkIndex];
                    if (entity.indexInChunk < 64)
                    {
                        var offset                             = math.countbits(chunk.lower.Value & ((1ul << entity.indexInChunk) - 1));
                        destroyArray[chunk.prefixSum + offset] = entity.entity;
                    }
                    else
                    {
                        var offset                             = chunk.lowerCount + math.countbits(chunk.upper.Value & ((1ul << (entity.indexInChunk - 64)) - 1));
                        destroyArray[chunk.prefixSum + offset] = entity.entity;
                    }
                }
                foreach (var entity in legsWithInfo)
                {
                    if (entity.entity == Entity.Null)
                        continue;
                    var chunk = chunks[entity.chunkIndex];
                    if (entity.indexInChunk < 64)
                    {
                        var offset                             = math.countbits(chunk.lower.Value & ((1ul << entity.indexInChunk) - 1));
                        destroyArray[chunk.prefixSum + offset] = entity.entity;
                    }
                    else
                    {
                        var offset                             = chunk.lowerCount + math.countbits(chunk.upper.Value & ((1ul << (entity.indexInChunk - 64)) - 1));
                        destroyArray[chunk.prefixSum + offset] = entity.entity;
                    }
                }
                //foreach (var chunk in chunks)
                //{
                //    var span = destroyArray.AsSpan().Slice(chunk.prefixSum, chunk.count);
                //    ReorderChunk(chunk, span);
                //}
                rootsWithInfo.Dispose();
                legPrefixSum.Dispose();
                legTotal.Dispose();
                legsWithInfo.Dispose();
                sBatchEntities.End();
                em.DestroyEntity(destroyArray);
            }

            static void ReorderChunk(ChunkData chunk, Span<Entity> toDestroy)
            {
                var last      = FindLastSetIndex(chunk.lower, chunk.upper);
                var moveCount = chunk.chunkTotalCount - (last + 1);
                if (chunk.count <= chunk.chunkTotalCount - moveCount)
                    return;

                var          indexFromStart  = 0;
                var          indicesFilled   = 0;
                int          chunkTotalCount = chunk.chunkTotalCount;
                Span<Entity> reorder         = stackalloc Entity[chunk.count];
                while (indicesFilled < chunk.count)
                {
                    for (int i = 0; i < moveCount; i++)
                    {
                        reorder[indicesFilled] = toDestroy[indexFromStart];
                        indicesFilled++;
                        indexFromStart++;
                    }
                    if (indicesFilled == chunk.count)
                        break;
                    chunkTotalCount -= moveCount;

                    var tailStart         = FindFirstSetIndexInEndRange(last, chunk.lower, chunk.upper);
                    var indicesRequested  = last - tailStart + 1;
                    var indicesRemaining  = chunk.count - indicesFilled;
                    var overshoot         = math.max(0, indicesRequested - indicesRemaining);
                    indicesRequested     += overshoot;
                    tailStart            += overshoot;
                    var tailDestroyIndex  = toDestroy.Length - indicesRequested;

                    var tailSlice = toDestroy[tailDestroyIndex..];
                    for (int i = 0; i < tailSlice.Length; i++)
                    {
                        reorder[indicesFilled] = tailSlice[i];
                        indicesFilled++;
                    }
                    toDestroy = toDestroy[..tailDestroyIndex];
                    ClearMsbBits(tailDestroyIndex, ref chunk.lower, ref chunk.upper);
                    chunkTotalCount -= indicesRequested;
                    last             = FindLastSetIndex(chunk.lower, chunk.upper);
                    moveCount        = chunkTotalCount - (last + 1);
                    moveCount        = math.min(moveCount, chunk.count - indicesFilled);
                }
                reorder.CopyTo(toDestroy);
            }

            static int FindLastSetIndex(BitField64 lower, BitField64 upper)
            {
                var lzcnt = upper.CountLeadingZeros();
                if (lzcnt == 64)
                    lzcnt += lower.CountLeadingZeros();
                return 127 - lzcnt;
            }

            static void ClearMsbBits(int startIndex, ref BitField64 lower, ref BitField64 upper)
            {
                if (startIndex <= 64)
                {
                    upper        = default;
                    lower.Value &= (1ul << startIndex) - 1;
                }
                else
                    upper.Value &= (1ul << (startIndex - 64)) - 1;
            }

            static int FindFirstSetIndexInEndRange(int lastSetIndex, BitField64 lower, BitField64 upper)
            {
                var inverseLower   = lower;
                var inverseUpper   = upper;
                inverseLower.Value = ~inverseLower.Value;
                inverseUpper.Value = ~inverseUpper.Value;
                ClearMsbBits(lastSetIndex, ref inverseLower, ref inverseUpper);
                return FindLastSetIndex(inverseLower, inverseUpper) + 1;
            }

            struct ChunkData
            {
                public BitField64 lower;
                public BitField64 upper;
                public int        prefixSum;
                public byte       count;
                public byte       lowerCount;
                public byte       chunkTotalCount;
            }

            struct EntityWithInfo
            {
                public Entity entity;
                int           packed;
                public int chunkIndex
                {
                    get => Bits.GetBits(packed, 0, 25);
                    set => Bits.SetBits(ref packed, 0, 25, value);
                }
                public int indexInChunk
                {
                    get => Bits.GetBits(packed, 25, 7);
                    set => Bits.SetBits(ref packed, 25, 7, value);
                }
            }

            [BurstCompile]
            struct FindRootsJob : IJobFor
            {
                [ReadOnly] public NativeArray<Entity>             roots;
                [ReadOnly] public EntityStorageInfoLookup         esil;
                [ReadOnly] public BufferLookup<LinkedEntityGroup> legLookup;
                public NativeArray<EntityWithInfo>                rootsWithInfo;
                public NativeArray<int>                           legCounts;

                public void Execute(int i)
                {
                    var entity = roots[i];
                    if (!esil.Exists(entity))
                    {
                        rootsWithInfo[i] = default;
                        legCounts[i]     = default;
                        return;
                    }

                    if (legLookup.TryGetBuffer(entity, out var buffer))
                    {
                        legCounts[i] = math.max(0, buffer.Length - 1);
                        if (buffer.Length == 1)
                        {
                            CheckFirstLegIsRoot(entity, buffer[0].Value);
                        }
                    }
                    else
                        legCounts[i] = 0;

                    var esi          = esil[entity];
                    rootsWithInfo[i] = new EntityWithInfo
                    {
                        entity       = entity,
                        chunkIndex   = esi.Chunk.GetHashCode(),
                        indexInChunk = esi.IndexInChunk,
                    };
                }
            }

            [BurstCompile]
            struct PrefixSumLegsJob : IJob
            {
                public NativeArray<int>           legPrefixSums;
                public NativeReference<int>       legTotal;
                public NativeList<EntityWithInfo> legsWithInfo;

                public void Execute()
                {
                    int sum = 0;
                    for (int i = 0; i < legPrefixSums.Length; i++)
                    {
                        var legCount      = legPrefixSums[i];
                        legPrefixSums[i]  = sum;
                        sum              += legCount;
                    }
                    legTotal.Value = sum;
                    legsWithInfo.ResizeUninitialized(sum);
                }
            }

            [BurstCompile]
            struct FindLegsJob : IJobFor
            {
                [ReadOnly] public NativeArray<Entity>                                        roots;
                [ReadOnly] public EntityStorageInfoLookup                                    esil;
                [ReadOnly] public NativeArray<int>                                           legPrefixSums;
                [NativeDisableParallelForRestriction] public BufferLookup<LinkedEntityGroup> legLookup;
                [NativeDisableParallelForRestriction] public NativeArray<EntityWithInfo>     legsWithInfo;

                public void Execute(int index)
                {
                    if (index < roots.Length - 1)
                    {
                        var count = legPrefixSums[index + 1] - legPrefixSums[index];
                        if (count == 0)
                            return;
                    }
                    else
                    {
                        var count = legsWithInfo.Length - legPrefixSums[index];
                        if (count == 0)
                            return;
                    }

                    var buffer = legLookup[roots[index]];
                    var start  = legPrefixSums[index];
                    var legs   = buffer.AsNativeArray();
                    CheckFirstLegIsRoot(roots[index], legs[0].Value);
                    for (int i = 1; i < legs.Length; i++)
                    {
                        var entity = legs[i].Value;
                        if (!esil.Exists(entity))
                        {
                            legsWithInfo[start + i] = default;
                            continue;
                        }

                        var esi                     = esil[entity];
                        legsWithInfo[start + i - 1] = new EntityWithInfo
                        {
                            entity       = entity,
                            chunkIndex   = esi.Chunk.GetHashCode(),
                            indexInChunk = esi.IndexInChunk,
                        };
                    }
                    buffer.Clear();
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckFirstLegIsRoot(Entity root, Entity leg)
            {
                if (root != leg)
                {
                    throw new InvalidOperationException($"The first LinkedEntityGroup of {root.ToFixedString()} is not the root, and is instead {leg.ToFixedString()}.");
                }
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

