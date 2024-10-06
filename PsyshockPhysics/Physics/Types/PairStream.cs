using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A span of memory which can be stored as a field inside of any object stored within a PairStream.
    /// These can be nested.
    /// </summary>
    /// <typeparam name="T">The type of element stored within the span</typeparam>
    public unsafe struct StreamSpan<T> where T : unmanaged
    {
        /// <summary>
        /// The number of elements in the span
        /// </summary>
        public int length => m_length;
        /// <summary>
        /// Gets the pointer to the raw memory of the span
        /// </summary>
        /// <returns></returns>
        public T* GetUnsafePtr() => m_ptr;
        /// <summary>
        /// Gets an element of the span by ref
        /// </summary>
        /// <param name="index">The index of the element to fetch</param>
        /// <returns>The element at the specified index</returns>
        public ref T this[int index] => ref AsSpan()[index];
        /// <summary>
        /// Gets an enumerator over the span
        /// </summary>
        /// <returns></returns>
        public Span<T>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();
        /// <summary>
        /// Returns the StreamSpan as a .NET Span
        /// </summary>
        /// <returns></returns>
        public Span<T> AsSpan()
        {
            PairStream.CheckNotNull(m_ptr);
            return new Span<T>(m_ptr, length);
        }

        /// <summary>
        /// Implicitly converts this StreamSpan into a DynamicStreamSpan.
        /// </summary>
        public static implicit operator DynamicStreamSpan(StreamSpan<T> streamSpan)
        {
            return new DynamicStreamSpan
            {
                m_ptr      = streamSpan.m_ptr,
                m_length   = streamSpan.m_length,
                m_typeHash = BurstRuntime.GetHashCode32<T>()
            };
        }

        internal T*  m_ptr;
        internal int m_length;
    }

    /// <summary>
    /// A type-punned version of StreamSpan whic stores the type hash for safety referencing.
    /// </summary>
    public unsafe struct DynamicStreamSpan
    {
        /// <summary>
        /// Try to retrieve the StreamSpan of the specified type. Throws if the type hash doesn't match.
        /// </summary>
        public StreamSpan<T> GetSpan<T>() where T : unmanaged
        {
            CheckTypeHash<T>();
            PairStream.CheckNotNull(m_ptr);
            return new StreamSpan<T> { m_ptr = (T*)m_ptr, m_length = m_length };
        }

        internal void* m_ptr;
        internal int   m_length;
        internal int   m_typeHash;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckTypeHash<T>() where T : unmanaged
        {
            if (m_typeHash != BurstRuntime.GetHashCode32<T>())
                throw new InvalidOperationException($"Attempted to access a StreamSpan from a DynamicStreamSpan using the wrong type.");
        }
    }

    /// <summary>
    /// A NativeContainer which stores multiple "streams" of pairs and per-pair user-allocated data
    /// grouped by the multi-box mechanism of FindPairs. Instances can be concatenated to agregate
    /// the products of multiple FindPairs operations.
    /// </summary>
    /// <remarks>
    /// The streams are allocated to allow for full addition of pairs from FindPairs operations
    /// (including ParallelUnsafe variants) deterministically. The streams can the be combined
    /// using the same allocator, and then be iterated over in parallel using Physics.ForEachPair().
    /// While iterating, pair data can be modified, and new allocations can be performed safely.
    /// Pairs that are composed of entities from different buckets in the multi-box can be further
    /// parallelized via an islanding algorithm.
    /// </remarks>
    [NativeContainer]
    public unsafe struct PairStream : INativeDisposable
    {
        #region Create and Destroy
        /// <summary>
        /// Creates a PairStream using a multi-box with the specified number of cells per axis
        /// </summary>
        /// <param name="worldSubdivisionsPerAxis">The number of cells per axis</param>
        /// <param name="allocator">The allocator to use for the PairStream</param>
        public PairStream(int3 worldSubdivisionsPerAxis,
                          AllocatorManager.AllocatorHandle allocator) : this(IndexStrategies.CellCountFromSubdivisionsPerAxis(worldSubdivisionsPerAxis) + 1, allocator)
        {
        }
        /// <summary>
        /// Creates a PairStream using the multi-box from the CollisionLayerSettings
        /// </summary>
        /// <param name="settings">The settings that specify the multi-box pairs will conform to</param>
        /// <param name="allocator">The allocator to use for the PairStream</param>
        public PairStream(in CollisionLayerSettings settings, AllocatorManager.AllocatorHandle allocator) : this(settings.worldSubdivisionsPerAxis, allocator)
        {
        }
        /// <summary>
        /// Creates a PairStream using the multi-box from the CollisionLayer.
        /// It is safe to pass in a CollisionLayer currently being used in a job.
        /// </summary>
        /// <param name="layerWithSettings">A CollisionLayer with the desired multi-box configuration</param>
        /// <param name="allocator">The allocator to use for the PairStream</param>
        public PairStream(in CollisionLayer layerWithSettings, AllocatorManager.AllocatorHandle allocator) : this(layerWithSettings.bucketCount, allocator)
        {
        }
        /// <summary>
        /// Creates a PairStream using a multi-box that has the specified number of buckets.
        /// The cross-bucket is included in this count, but the NaN bucket is excluded.
        /// </summary>
        /// <param name="bucketCountExcludingNan">The number of buckets to use.
        /// PairStreams will allocate 5n - 1 streams that may be iterated by the enumerator.</param>
        /// <param name="allocator">The allocator to use for the PairStream</param>
        public PairStream(int bucketCountExcludingNan, AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CheckAllocator(allocator);

            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
            CollectionHelper.SetStaticSafetyId<PairStream>(ref m_Safety, ref s_staticSafetyId.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            int cellCount    = IndexStrategies.CellCountFromBucketCountWithoutNaN(bucketCountExcludingNan);
            int totalStreams = IndexStrategies.PairStreamIndexCount(cellCount);
            data             = new SharedContainerData
            {
                pairHeaders      = new UnsafeIndexedBlockList(UnsafeUtility.SizeOf<PairHeader>(), 4096 / UnsafeUtility.SizeOf<PairHeader>(), totalStreams, allocator),
                blockStreamArray = AllocatorManager.Allocate<BlockStream>(allocator, totalStreams),
                state            = AllocatorManager.Allocate<State>(allocator),
                cellCount        = cellCount,
                allocator        = allocator
            };

            *data.state = default;

            for (int i = 0; i < data.pairHeaders.indexCount; i++)
                data.blockStreamArray[i] = default;
        }

        /// <summary>
        /// Disposes the PairStream after the jobs which use it have finished.
        /// </summary>
        /// <param name="inputDeps">The JobHandle for any jobs previously using this PairStream</param>
        /// <returns>The JobHandle for the disposing job scheduled, or inputDeps if no job was scheduled</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jobHandle = new DisposeJob
            {
                blockList    = data.pairHeaders,
                state        = data.state,
                blockStreams = data.blockStreamArray,
                allocator    = data.allocator,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = m_Safety
#endif
            }.Schedule(inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            this = default;
            return jobHandle;
        }

        /// <summary>
        /// Disposes the PairStream
        /// </summary>
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif
            Deallocate(data.state, data.pairHeaders, data.blockStreamArray, data.allocator);
            this = default;
        }
        #endregion

        #region Public API
        /// <summary>
        /// The number of buckets in the multibox excluding NaN. This value is the same as CollisionLayer.bucketCount.
        /// </summary>
        public int bucketCount => IndexStrategies.BucketCountWithoutNaN(data.cellCount);

        /// <summary>
        /// Adds a Pair and allocates memory in the stream for a single instance of type T. Returns a ref to T.
        /// The pair will save the reference to T for later lookup.
        /// </summary>
        /// <typeparam name="T">Any unmanaged type that contains data that should be associated with the pair.
        /// This type may contain StreamSpan instances.</typeparam>
        /// <param name="entityA">The first entity in the pair</param>
        /// <param name="bucketA">The bucket index from the multi-box the first entity belongs to.
        /// If aIsRW is false, this can be any value [0, bucketCount - 1]</param>
        /// <param name="aIsRW">If true, the first entity is given read-write access in a parallel ForEachPair operation</param>
        /// <param name="entityB">The second entity in the pair</param>
        /// <param name="bucketB">The bucket index from the multi-box the second entity belongs to.
        /// If bIsRW is false, this can be any value [0, bucketCount - 1]</param>
        /// <param name="bIsRW">If true, the second entity is given read-write access in a parallel ForEachPair operation</param>
        /// <param name="pair">The pair instance, which can store additional settings and perform additional allocations</param>
        /// <returns>The reference to the allocated instance of type T</returns>
        public ref T AddPairAndGetRef<T>(Entity entityA, int bucketA, bool aIsRW, Entity entityB, int bucketB, bool bIsRW, out Pair pair) where T : unmanaged
        {
            var root = AddPairImpl(entityA,
                                   bucketA,
                                   aIsRW,
                                   entityB,
                                   bucketB,
                                   bIsRW,
                                   UnsafeUtility.SizeOf<T>(),
                                   UnsafeUtility.AlignOf<T>(),
                                   BurstRuntime.GetHashCode32<T>(),
                                   false,
                                   out pair);
            pair.header->rootPtr = root;
            ref var result       = ref UnsafeUtility.AsRef<T>(root);
            result               = default;
            return ref result;
        }

        /// <summary>
        /// Adds a Pair and allocates raw memory in the stream. Returns the pointer to the raw memory.
        /// The pair will save the pointer for later lookup.
        /// </summary>
        /// <param name="entityA">The first entity in the pair</param>
        /// <param name="bucketA">The bucket index from the multi-box the first entity belongs to.
        /// If aIsRW is false, this can be any value [0, bucketCount - 1]</param>
        /// <param name="aIsRW">If true, the first entity is given read-write access in a parallel ForEachPair operation</param>
        /// <param name="entityB">The second entity in the pair</param>
        /// <param name="bucketB">The bucket index from the multi-box the second entity belongs to.
        /// If bIsRW is false, this can be any value [0, bucketCount - 1]</param>
        /// <param name="bIsRW">If true, the second entity is given read-write access in a parallel ForEachPair operation</param>
        /// <param name="sizeInBytes">Specifies the size in bytes to allocate</param>
        /// <param name="alignInBytes">Specifies the required alignment of the allocation</param>
        /// <param name="pair">The pair instance, which can store additional settings and perform additional allocations</param>
        /// <returns>The pointer to the allocated data</returns>
        public void* AddPairRaw(Entity entityA, int bucketA, bool aIsRW, Entity entityB, int bucketB, bool bIsRW, int sizeInBytes, int alignInBytes, out Pair pair)
        {
            return AddPairImpl(entityA, bucketA, aIsRW, entityB, bucketB, bIsRW, sizeInBytes, alignInBytes, 0, true, out pair);
        }

        /// <summary>
        /// Clone's a pair's entities, buckets, and read-write statuses from another PairStream.
        /// All other pair data is reset for the new Pair instance.
        /// A new object of type T is allocated for this clone and returned.
        /// </summary>
        /// <typeparam name="T">Any unmanaged type that contains data that should be associated with the pair</typeparam>
        /// <param name="pairFromOtherStream">A pair instance from another stream</param>
        /// <param name="pairInThisStream">The newly cloned pair that belongs to this PairStream</param>
        /// <returns>The reference to the allocated instance of type T</returns>
        public ref T AddPairFromOtherStreamAndGetRef<T>(in Pair pairFromOtherStream, out Pair pairInThisStream) where T : unmanaged
        {
            return ref AddPairAndGetRef<T>(pairFromOtherStream.entityA,
                                           pairFromOtherStream.index,
                                           pairFromOtherStream.aIsRW,
                                           pairFromOtherStream.entityB,
                                           pairFromOtherStream.index,
                                           pairFromOtherStream.bIsRW,
                                           out pairInThisStream);
        }

        /// <summary>
        /// Clone's a pair's entities, buckets, and read-write statuses from another PairStream.
        /// All other pair data is reset for the new Pair instance.
        /// New raw memory is allocated for this clone and returned.
        /// </summary>
        /// <param name="pairFromOtherStream">A pair instance from another stream</param>
        /// <param name="pairInThisStream">The newly cloned pair that belongs to this PairStream</param>
        /// <returns>The pointer to the allocated data</returns>
        public void* AddPairFromOtherStreamRaw(in Pair pairFromOtherStream, int sizeInBytes, int alignInBytes, out Pair pairInThisStream)
        {
            return AddPairRaw(pairFromOtherStream.entityA,
                              pairFromOtherStream.index,
                              pairFromOtherStream.aIsRW,
                              pairFromOtherStream.entityB,
                              pairFromOtherStream.index,
                              pairFromOtherStream.bIsRW,
                              sizeInBytes,
                              alignInBytes,
                              out pairInThisStream);
        }

        /// <summary>
        /// Concatenates another PairStream to this PairStream, stealing all allocated memory.
        /// Pointers to allocated data associated with pairs (to any level of nesting) are preserved
        /// within this PairStream, but will no longer be associated with the old PairStream.
        /// This method only works if both this PairStream and the other PairStream have been allocated
        /// with the same allocator and use the same multi-box layout.
        /// </summary>
        /// <param name="pairStreamToStealFrom">Another PairStream with the same allocator and multi-box configuration,
        /// whose pairs and memory should be transfered. After the transfer, the old PairStream is empty of elements
        /// but is otherwise in a valid state to collect new pairs using the same multi-box configuration.</param>
        public void ConcatenateFrom(ref PairStream pairStreamToStealFrom)
        {
            CheckWriteAccess();
            pairStreamToStealFrom.CheckWriteAccess();
            CheckStreamsMatch(ref pairStreamToStealFrom);

            data.state->enumeratorVersion++;
            data.state->pairPtrVersion++;
            pairStreamToStealFrom.data.state->enumeratorVersion++;
            pairStreamToStealFrom.data.state->pairPtrVersion++;

            data.pairHeaders.ConcatenateAndStealFromUnordered(ref pairStreamToStealFrom.data.pairHeaders);
            for (int i = 0; i < data.pairHeaders.indexCount; i++)
            {
                ref var stream      = ref data.blockStreamArray[i];
                ref var otherStream = ref pairStreamToStealFrom.data.blockStreamArray[i];
                if (!stream.blocks.IsCreated)
                {
                    stream      = otherStream;
                    otherStream = default;
                }
                else if (otherStream.blocks.IsCreated)
                {
                    stream.blocks.AddRange(otherStream.blocks);
                    stream.bytesRemainingInBlock = otherStream.bytesRemainingInBlock;
                    stream.nextFreeAddress       = otherStream.nextFreeAddress;
                    otherStream.blocks.Clear();
                    otherStream.bytesRemainingInBlock = 0;
                }
            }
        }

        /// <summary>
        /// Gets a ParallelWriter of this PairStream, which can be used inside FindPairs and ForEachPair operations
        /// </summary>
        public ParallelWriter AsParallelWriter()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = m_Safety;
            CollectionHelper.SetStaticSafetyId<ParallelWriter>(ref safety, ref ParallelWriter.s_staticSafetyId.Data);
#endif
            return new ParallelWriter
            {
                data        = data,
                threadIndex = -1,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = safety,
#endif
            };
        }

        /// <summary>
        /// Gets an enumerator to enumerate all pairs in the PairStream. Disabled pairs are included.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            CheckAllocatedAccess();
            return new Enumerator
            {
                pair = new Pair
                {
                    areEntitiesSafeInContext = false,
                    data                     = data,
                    header                   = null,
                    index                    = 0,
                    isParallelKeySafe        = false,
                    version                  = data.state->pairPtrVersion,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = m_Safety,
#endif
                },
                currentHeader          = null,
                enumerator             = data.pairHeaders.GetEnumerator(0),
                enumeratorVersion      = data.state->enumeratorVersion,
                onePastLastStreamIndex = data.pairHeaders.indexCount,
            };
        }
        #endregion

        #region Public Types
        /// <summary>
        /// A pair which contains pair metadata, user-assignable metadata, and a pointer
        /// to allocated data associated with the pair and owned by the PairStream.
        /// </summary>
        [NativeContainer]
        public partial struct Pair
        {
            /// <summary>
            /// Allocates multiple contiguous elements of T that will be owned by the PairStream.
            /// </summary>
            /// <typeparam name="T">Any unmanaged type that should be associated with the pair</typeparam>
            /// <param name="count">The number of elements to allocate</param>
            /// <returns>The span of elements allocated</returns>
            public StreamSpan<T> Allocate<T>(int count, NativeArrayOptions options = NativeArrayOptions.ClearMemory) where T : unmanaged
            {
                CheckWriteAccess();
                CheckPairPtrVersionMatches(data.state, version);
                ref var blocks = ref data.blockStreamArray[index];
                var     ptr    = blocks.Allocate<T>(count, data.allocator);
                var     result = new StreamSpan<T> { m_ptr = ptr, m_length = count };
                if (options == NativeArrayOptions.ClearMemory)
                    result.AsSpan().Clear();
                return result;
            }
            /// <summary>
            /// Allocates raw memory that will be owned by the PairStream
            /// </summary>
            /// <param name="sizeInBytes">The number of bytes to allocate</param>
            /// <param name="alignInBytes">The alignment of the allocation</param>
            /// <returns>A pointer to the raw allocated memory</returns>
            public void* AllocateRaw(int sizeInBytes, int alignInBytes)
            {
                CheckWriteAccess();
                CheckPairPtrVersionMatches(data.state, version);
                if (sizeInBytes == 0)
                    return null;
                ref var blocks = ref data.blockStreamArray[index];
                return blocks.Allocate(sizeInBytes, alignInBytes, data.allocator);
            }
            /// <summary>
            /// Replaces the top-level ref associated with the pair with a new allocation of type T.
            /// The old data is still retained but no longer directly referenced by the pair itself.
            /// </summary>
            /// <typeparam name="T">Any unmanaged type that should be associated with the pair.</typeparam>
            /// <returns>A reference to the allocated instance of type T</returns>
            public ref T ReplaceRef<T>() where T : unmanaged
            {
                WriteHeader().flags  &= (~PairHeader.kRootPtrIsRaw) & 0xff;
                header->rootPtr       = AllocateRaw(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>());
                header->rootTypeHash  = BurstRuntime.GetHashCode32<T>();
                ref var result        = ref UnsafeUtility.AsRef<T>(header->rootPtr);
                result                = default;
                return ref result;
            }
            /// <summary>
            /// Replaces the top-level pointer associated with the pair with a new raw allocation.
            /// The old data is still retained but no longer directly referenced by the pair itself.
            /// </summary>
            /// <param name="sizeInBytes">The number of bytes to allocate</param>
            /// <param name="alignInBytes">The alignment of the allocation</param>
            /// <returns>A pointer to the new allocation</returns>
            public void* ReplaceRaw(int sizeInBytes, int alignInBytes)
            {
                WriteHeader().flags  |= PairHeader.kRootPtrIsRaw;
                header->rootPtr       = AllocateRaw(sizeInBytes, alignInBytes);
                header->rootTypeHash  = 0;
                return header->rootPtr;
            }
            /// <summary>
            /// Gets a reference to the top-level object associated with the pair.
            /// When safety checks are enabled, the type is checked with the type allocated for the pair.
            /// </summary>
            /// <typeparam name="T">The unmanaged type that was allocated for the pair</typeparam>
            /// <returns>A reference to the data that was allocated for this pair</returns>
            public ref T GetRef<T>() where T : unmanaged
            {
                var root = GetRaw();
                CheckTypeHash<T>();
                CheckNotNull(root);
                return ref UnsafeUtility.AsRef<T>(root);
            }
            /// <summary>
            /// Gets the raw top-level pointer associated with the pair. If the top-level object
            /// was allocated with a specific type, this gets the raw pointer to that object.
            /// </summary>
            /// <returns>The raw pointer of the object associated with the pair</returns>
            public void* GetRaw() => WriteHeader().rootPtr;

            /// <summary>
            /// A ushort value stored with the pair that may serve any purpose of the user.
            /// </summary>
            public ushort userUShort
            {
                get => ReadHeaderParallel().userUshort;
                set => WriteHeader().userUshort = value;
            }
            /// <summary>
            /// A byte value stored with the pair that may serve any purpose of the user.
            /// A common use for this is to encode an enum specifying the type of object
            /// associated with the pair.
            /// </summary>
            public byte userByte
            {
                get => ReadHeaderParallel().userByte;
                set => WriteHeader().userByte = value;
            }
            /// <summary>
            /// Whether or not the pair is enabled. Disabled pairs may be skipped in a ForEachPair operation.
            /// </summary>
            public bool enabled
            {
                get => (ReadHeaderParallel().flags & PairHeader.kEnabled) == PairHeader.kEnabled;
                set => WriteHeader().flags |= PairHeader.kEnabled;
            }

            /// <summary>
            /// If true, the pair's associated object was allocated as a raw pointer.
            /// </summary>
            public bool isRaw => (ReadHeaderParallel().flags & PairHeader.kRootPtrIsRaw) == PairHeader.kRootPtrIsRaw;
            /// <summary>
            /// If true, the first entity in the pair was granted read-write access upon creation.
            /// However, read-write access may still not be permitted depending on the context
            /// (it is disallowed for immediate contexts).
            /// </summary>
            public bool aIsRW => (ReadHeaderParallel().flags & PairHeader.kWritableA) == PairHeader.kWritableA;
            /// <summary>
            /// If true, the second entity in the pair was granted read-write access upon creation.
            /// However, read-write access may still not be permitted depending on the context
            /// (it is disallowed for immediate contexts).
            /// </summary>
            public bool bIsRW => (ReadHeaderParallel().flags & PairHeader.kWritableB) == PairHeader.kWritableB;
            /// <summary>
            /// The index of the stream this pair resides in
            /// </summary>
            public int streamIndex
            {
                get
                {
                    ReadHeaderParallel();
                    return index;
                }
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            /// <summary>
            /// A safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
            /// owning entity of the first entity in the pair. It can also be implicitly casted and used as a normal entity reference.
            /// </summary>
            public SafeEntity entityA => new SafeEntity
            {
                m_entity = new Entity
                {
                    Index   = math.select(-header->entityA.Index - 1, header->entityA.Index, aIsRW && areEntitiesSafeInContext),
                    Version = header->entityA.Version
                }
            };
            /// <summary>
            /// A safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
            /// owning entity of the second entity in the pair. It can also be implicitly casted and used as a normal entity reference.
            /// </summary>
            public SafeEntity entityB => new SafeEntity
            {
                m_entity = new Entity
                {
                    Index   = math.select(-header->entityB.Index - 1, header->entityB.Index, bIsRW && areEntitiesSafeInContext),
                    Version = header->entityB.Version
                }
            };
#else
            public SafeEntity entityA => new SafeEntity { m_entity = header->entityA };
            public SafeEntity entityB => new SafeEntity { m_entity = header->entityB };
#endif
        }

        /// <summary>
        /// A key generated via FindPairs that can be used to populate a pair's base data for a ParallelWriter
        /// </summary>
        [NativeContainer]  // Similar to FindPairsResult, keep this from escaping the local context
        public struct ParallelWriteKey
        {
            internal Entity entityA;
            internal Entity entityB;
            internal int    streamIndexA;
            internal int    streamIndexB;
            internal int    streamIndexCombined;
            internal int    cellCount;
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public partial struct ParallelWriter
        {
            /// <summary>
            /// Adds a Pair from within a FindPairs operation and allocate memory in the stream for a single instance of type T.
            /// Returns a ref to T. The pair will save the reference to T for later lookup.
            /// </summary>
            /// <typeparam name="T">Any unmanaged type that contains data that should be associated with the pair.
            /// This type may contain StreamSpan instances.</typeparam>
            /// <param name="key">A key obtained from a FindPairs operation</param>
            /// <param name="aIsRW">If true, the first entity is given read-write access in a parallel ForEachPair operation</param>
            /// <param name="bIsRW">If true, the second entity is given read-write access in a parallel ForEachPair operation</param>
            /// <param name="pair">The pair instance, which can store additional settings and perform additional allocations</param>
            /// <returns>The reference to the allocated instance of type T</returns>
            public ref T AddPairAndGetRef<T>(ParallelWriteKey key, bool aIsRW, bool bIsRW, out Pair pair) where T : unmanaged
            {  // Todo: Passing the key as an in parameter confuses the compiler.
                var root = AddPairImpl(in key,
                                       aIsRW,
                                       bIsRW,
                                       UnsafeUtility.SizeOf<T>(),
                                       UnsafeUtility.AlignOf<T>(),
                                       BurstRuntime.GetHashCode32<T>(),
                                       false,
                                       out pair);
                pair.header->rootPtr = root;
                ref var result       = ref UnsafeUtility.AsRef<T>(root);
                result               = default;
                return ref result;
            }

            /// <summary>
            /// Adds a Pair from within a FindPairs operation and allocate raw memory in the stream.
            /// Returns the pointer to the raw memory. The pair will save the pointer for later lookup.
            /// </summary>
            /// <param name="key">A key obtained from a FindPairs operation</param>
            /// <param name="aIsRW">If true, the first entity is given read-write access in a parallel ForEachPair operation</param>
            /// <param name="bIsRW">If true, the second entity is given read-write access in a parallel ForEachPair operation</param>
            /// <param name="sizeInBytes">Specifies the size in bytes to allocate</param>
            /// <param name="alignInBytes">Specifies the required alignment of the allocation</param>
            /// <param name="pair">The pair instance, which can store additional settings and perform additional allocations</param>
            /// <returns>The pointer to the allocated data</returns>
            public void* AddPairRaw(in ParallelWriteKey key, bool aIsRW, bool bIsRW, int sizeInBytes, int alignInBytes, out Pair pair)
            {
                return AddPairImpl(in key, aIsRW, bIsRW, sizeInBytes, alignInBytes, 0, true, out pair);
            }

            /// <summary>
            /// Clone's a pair's entities, buckets, and read-write statuses from within a ForEachPair operation on another PairStream.
            /// All other pair data is reset for the new Pair instance.
            /// A new object of type T is allocated for this clone and returned.
            /// </summary>
            /// <typeparam name="T">Any unmanaged type that contains data that should be associated with the pair</typeparam>
            /// <param name="pairFromOtherStream">A pair instance from another stream</param>
            /// <param name="pairInThisStream">The newly cloned pair that belongs to this PairStream</param>
            /// <returns>The reference to the allocated instance of type T</returns>
            public ref T AddPairFromOtherStreamAndGetRef<T>(in Pair pairFromOtherStream, out Pair pairInThisStream) where T : unmanaged
            {
                CheckPairCanBeAddedInParallel(in pairFromOtherStream);
                var key = new ParallelWriteKey
                {
                    entityA             = pairFromOtherStream.entityA,
                    entityB             = pairFromOtherStream.entityB,
                    streamIndexA        = pairFromOtherStream.index,
                    streamIndexB        = pairFromOtherStream.index,
                    streamIndexCombined = pairFromOtherStream.index,
                    cellCount           = pairFromOtherStream.data.cellCount
                };
                return ref AddPairAndGetRef<T>(key, pairFromOtherStream.aIsRW, pairFromOtherStream.bIsRW, out pairInThisStream);
            }
            /// <summary>
            /// Clone's a pair's entities, buckets, and read-write statuses from within a ForEachPair operation on another PairStream.
            /// All other pair data is reset for the new Pair instance.
            /// New raw memory is allocated for this clone and returned.
            /// </summary>
            /// <param name="pairFromOtherStream">A pair instance from another stream</param>
            /// <param name="pairInThisStream">The newly cloned pair that belongs to this PairStream</param>
            /// <returns>The pointer to the allocated data</returns>
            public void* AddPairFromOtherStreamRaw(in Pair pairFromOtherStream, int sizeInBytes, int alignInBytes, out Pair pairInThisStream)
            {
                CheckPairCanBeAddedInParallel(in pairFromOtherStream);
                var key = new ParallelWriteKey
                {
                    entityA             = pairFromOtherStream.entityA,
                    entityB             = pairFromOtherStream.entityB,
                    streamIndexA        = pairFromOtherStream.index,
                    streamIndexB        = pairFromOtherStream.index,
                    streamIndexCombined = pairFromOtherStream.index,
                    cellCount           = pairFromOtherStream.data.cellCount
                };
                return AddPairRaw(in key, pairFromOtherStream.aIsRW, pairFromOtherStream.bIsRW, sizeInBytes, alignInBytes, out pairInThisStream);
            }
        }

        /// <summary>
        /// An enumerator over all pairs in the PairStream, or a batch if obtained from a ForEachPair operation.
        /// This includes disabled pairs.
        /// </summary>
        public partial struct Enumerator
        {
            /// <summary>
            /// The current Pair
            /// </summary>
            public Pair Current
            {
                get
                {
                    pair.CheckReadAccess();
                    CheckSafeToEnumerate();
                    CheckValid();
                    return pair;
                }
            }

            /// <summary>
            /// Advance to the next Pair
            /// </summary>
            /// <returns>false if no more pairs are left</returns>
            public bool MoveNext()
            {
                pair.CheckReadAccess();
                CheckSafeToEnumerate();
                while (pair.index < onePastLastStreamIndex)
                {
                    if (enumerator.MoveNext())
                    {
                        currentHeader = (PairHeader*)UnsafeUtility.AddressOf(ref enumerator.GetCurrentAsRef<PairHeader>());
                        pair.header   = currentHeader;
                        return true;
                    }
                    pair.index++;
                    enumerator = pair.data.pairHeaders.GetEnumerator(pair.index);
                }
                return false;
            }
        }
        #endregion

        #region Public Types Internal Members
        partial struct Pair
        {
            internal SharedContainerData data;
            internal int                 index;
            internal int                 version;
            internal PairHeader*         header;
            internal bool                isParallelKeySafe;
            internal bool                areEntitiesSafeInContext;

            ref PairHeader ReadHeader()
            {
                CheckReadAccess();
                CheckPairPtrVersionMatches(data.state, version);
                return ref *header;
            }

            ref PairHeader WriteHeader()
            {
                CheckWriteAccess();
                CheckPairPtrVersionMatches(data.state, version);
                return ref *header;
            }

            ref PairHeader ReadHeaderParallel()
            {
                // Bypass AtomicWriteOnly safety access.
                if (isParallelKeySafe)
                    return ref WriteHeader();
                else
                    return ref ReadHeader();
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            //Unfortunately this name is hardcoded into Unity. No idea how EntityCommandBuffer gets away with multiple safety handles.
            internal AtomicSafetyHandle m_Safety;
#endif

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckWriteAccess()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            internal void CheckReadAccess()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckTypeHash<T>() where T : unmanaged
            {
                if ((header->flags & PairHeader.kRootPtrIsRaw) == PairHeader.kRootPtrIsRaw)
                    throw new InvalidOperationException(
                        $"Attempted to access a raw allocation using an explicit type. If this is intended, use GetRaw in combination with UnsafeUtility.AsRef.");
                if (header->rootTypeHash != BurstRuntime.GetHashCode32<T>())
                    throw new InvalidOperationException($"Attempted to access an allocation of a pair using the wrong type.");
            }
        }

        partial struct ParallelWriter
        {
            internal SharedContainerData data;

            [NativeSetThreadIndex]
            internal int threadIndex;

            bool needsAliasingChecks;
            bool needsIslanding;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            //Unfortunately this name is hardcoded into Unity. No idea how EntityCommandBuffer gets away with multiple safety handles.
            internal AtomicSafetyHandle m_Safety;

            internal static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<ParallelWriter>();
#endif

            void* AddPairImpl(in ParallelWriteKey key, bool aIsRW, bool bIsRW, int sizeInBytes, int alignInBytes, int typeHash, bool isRaw, out Pair pair)
            {
                CheckWriteAccess();
                CheckKeyCompatible(in key);

                // If for some reason the ParallelWriter was created and used in the same thread as an Enumerator,
                // We need to bump the version. But we don't want to do this if we are actually running in parallel.
                if (threadIndex == -1)
                    data.state->enumeratorVersion++;

                int targetStream;
                if (key.streamIndexA == key.streamIndexB)
                    targetStream = key.streamIndexA;
                else if (!bIsRW)
                    targetStream = key.streamIndexA;
                else if (!aIsRW)
                    targetStream = key.streamIndexB;
                else
                    targetStream = key.streamIndexCombined;

                // We can safely rely on eventual consistency here as this is a forced value write.
                // We only write the first time to avoid dirtying the cache line.
                if (!needsIslanding && targetStream == key.streamIndexCombined)
                {
                    needsIslanding             = true;
                    data.state->needsIslanding = true;
                }
                else if (!needsAliasingChecks && targetStream != key.streamIndexCombined)
                {
                    needsAliasingChecks          = true;
                    data.state->needsAliasChecks = true;
                }

                var headerPtr = (PairHeader*)data.pairHeaders.Allocate(targetStream);
                *   headerPtr = new PairHeader
                {
                    entityA      = key.entityA,
                    entityB      = key.entityB,
                    rootTypeHash = typeHash,
                    flags        =
                        (byte)((aIsRW ? PairHeader.kWritableA : default) + (bIsRW ? PairHeader.kWritableB : default) + PairHeader.kEnabled +
                               (isRaw ? PairHeader.kRootPtrIsRaw : default))
                };

                pair = new Pair
                {
                    data                     = data,
                    header                   = headerPtr,
                    index                    = targetStream,
                    version                  = data.state->pairPtrVersion,
                    isParallelKeySafe        = true,
                    areEntitiesSafeInContext = false,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = m_Safety,
#endif
                };

                var root           = pair.AllocateRaw(sizeInBytes, alignInBytes);
                headerPtr->rootPtr = root;
                return root;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckWriteAccess()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckKeyCompatible(in ParallelWriteKey key)
            {
                if (key.cellCount != data.cellCount)
                    throw new InvalidOperationException(
                        $"The key is generated from a different base bucket count {IndexStrategies.BucketCountWithoutNaN(key.cellCount)} from what the PairStream was constructed with {IndexStrategies.BucketCountWithoutNaN(data.cellCount)}");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckPairCanBeAddedInParallel(in Pair pairFromOtherStream)
            {
                if (!pairFromOtherStream.isParallelKeySafe)
                    throw new InvalidOperationException(
                        $"The pair cannot be safely added to the ParallelWriter because the pair was created from an immediate operation. Add directly to the PairStream instead of the ParallelWriter.");
            }
        }

        partial struct Enumerator
        {
            internal Pair                              pair;
            internal UnsafeIndexedBlockList.Enumerator enumerator;
            internal PairHeader*                       currentHeader;
            internal int                               onePastLastStreamIndex;
            internal int                               enumeratorVersion;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckSafeToEnumerate()
            {
                if (pair.data.state->enumeratorVersion != enumeratorVersion)
                    throw new InvalidOperationException($"The PairStream Enumerator has been invalidated.");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckValid()
            {
                if (pair.header == null)
                    throw new InvalidOperationException("Attempted to read the Current value when there is none, either because MoveNext() has not been called or returned false.");
                if (pair.header != currentHeader)
                    throw new InvalidOperationException(
                        "The Pair value in the enumerator was overwritten. Do not directly assign a different Pair instance to the ref passed into the processor.");
            }
        }
        #endregion

        #region Internal Types
        [StructLayout(LayoutKind.Sequential, Size = 32)]  // Force to 8-byte alignment
        internal struct PairHeader
        {
            public Entity entityA;
            public Entity entityB;
            public void*  rootPtr;
            public int    rootTypeHash;
            public ushort userUshort;
            public byte   userByte;
            public byte   flags;

            public const byte kWritableA    = 0x1;
            public const byte kWritableB    = 0x2;
            public const byte kEnabled      = 0x4;
            public const byte kRootPtrIsRaw = 0x8;
        }

        internal struct BlockPtr
        {
            public byte* ptr;
            public int   byteCount;
        }

        [StructLayout(LayoutKind.Sequential, Size = JobsUtility.CacheLineSize)]
        internal struct BlockStream
        {
            public UnsafeList<BlockPtr> blocks;
            public byte*                nextFreeAddress;
            public int                  bytesRemainingInBlock;

            public T* Allocate<T>(int count, AllocatorManager.AllocatorHandle allocator) where T : unmanaged
            {
                var neededBytes = UnsafeUtility.SizeOf<T>() * count;
                return (T*)Allocate(neededBytes, UnsafeUtility.AlignOf<T>(), allocator);
            }

            public void* Allocate(int sizeInBytes, int alignInBytes, AllocatorManager.AllocatorHandle allocator)
            {
                var neededBytes = sizeInBytes;
                if (Hint.Unlikely(!CollectionHelper.IsAligned(nextFreeAddress, alignInBytes)))
                {
                    var newAddress         = (byte*)CollectionHelper.Align((ulong)nextFreeAddress, (ulong)alignInBytes);
                    var diff               = newAddress - nextFreeAddress;
                    bytesRemainingInBlock -= (int)diff;
                    nextFreeAddress        = newAddress;
                }

                if (Hint.Unlikely(neededBytes > bytesRemainingInBlock))
                {
                    if (Hint.Unlikely(!blocks.IsCreated))
                    {
                        blocks = new UnsafeList<BlockPtr>(8, allocator);
                    }
                    var blockSize = math.max(neededBytes, 16 * 1024);
                    var newBlock  = new BlockPtr
                    {
                        byteCount = blockSize,
                        ptr       = AllocatorManager.Allocate<byte>(allocator, blockSize)
                    };
                    UnityEngine.Debug.Assert(CollectionHelper.IsAligned(newBlock.ptr, alignInBytes));
                    blocks.Add(newBlock);
                    nextFreeAddress       = newBlock.ptr;
                    bytesRemainingInBlock = blockSize;
                }

                var result             = nextFreeAddress;
                bytesRemainingInBlock -= neededBytes;
                nextFreeAddress       += neededBytes;
                return result;
            }
        }

        internal struct State
        {
            public int  enumeratorVersion;
            public int  pairPtrVersion;
            public bool needsAliasChecks;
            public bool needsIslanding;
        }

        internal struct SharedContainerData
        {
            public UnsafeIndexedBlockList pairHeaders;

            [NativeDisableUnsafePtrRestriction]
            public BlockStream* blockStreamArray;

            [NativeDisableUnsafePtrRestriction]
            public State* state;

            public int                              cellCount;
            public AllocatorManager.AllocatorHandle allocator;
        }
        #endregion

        #region Internal Structure
        internal SharedContainerData data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        //Unfortunately this name is hardcoded into Unity. No idea how EntityCommandBuffer gets away with multiple safety handles.
        internal AtomicSafetyHandle m_Safety;

        static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<PairStream>();
#endif
        #endregion

        #region Internal Helpers
        //internal int firstMixedBucketStream => 3 * data.expectedBucketCount;
        //internal int nanBucketStream => 5 * data.expectedBucketCount - 2;
        //internal int mixedIslandAggregateStream => 5 * data.expectedBucketCount - 1;

        void* AddPairImpl(Entity entityA,
                          int bucketA,
                          bool aIsRW,
                          Entity entityB,
                          int bucketB,
                          bool bIsRW,
                          int sizeInBytes,
                          int alignInBytes,
                          int typeHash,
                          bool isRaw,
                          out Pair pair)
        {
            CheckWriteAccess();
            CheckTargetBucketIsValid(bucketA);
            CheckTargetBucketIsValid(bucketB);

            data.state->enumeratorVersion++;
            int targetStream;
            if (bucketA == bucketB)
                targetStream = IndexStrategies.FirstStreamIndexFromBucketIndex(bucketA, data.cellCount);
            else if (!bIsRW)
                targetStream = IndexStrategies.FirstStreamIndexFromBucketIndex(bucketA, data.cellCount);
            else if (!aIsRW)
                targetStream = IndexStrategies.FirstStreamIndexFromBucketIndex(bucketB, data.cellCount);
            else
                targetStream = IndexStrategies.FirstMixedStreamIndex(data.cellCount);

            if (targetStream == IndexStrategies.FirstMixedStreamIndex(data.cellCount))
                data.state->needsIslanding = true;
            else
                data.state->needsAliasChecks = true;

            var headerPtr = (PairHeader*)data.pairHeaders.Allocate(targetStream);
            *headerPtr    = new PairHeader
            {
                entityA      = entityA,
                entityB      = entityB,
                rootTypeHash = typeHash,
                flags        =
                    (byte)((aIsRW ? PairHeader.kWritableA : default) + (bIsRW ? PairHeader.kWritableB : default) + PairHeader.kEnabled +
                           (isRaw ? PairHeader.kRootPtrIsRaw : default))
            };

            pair = new Pair
            {
                data                     = data,
                header                   = headerPtr,
                index                    = targetStream,
                version                  = data.state->pairPtrVersion,
                isParallelKeySafe        = false,
                areEntitiesSafeInContext = false,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = m_Safety,
#endif
            };

            var root           = pair.AllocateRaw(sizeInBytes, alignInBytes);
            headerPtr->rootPtr = root;
            return root;
        }

        private static void Deallocate(State* state, UnsafeIndexedBlockList blockList, BlockStream* blockStreams, AllocatorManager.AllocatorHandle allocator)
        {
            for (int i = 0; i < blockList.indexCount; i++)
            {
                var blockStream = blockStreams[i];
                if (blockStream.blocks.IsCreated)
                {
                    foreach (var block in blockStream.blocks)
                        AllocatorManager.Free(allocator, block.ptr, block.byteCount);
                    blockStream.blocks.Dispose();
                }
            }

            AllocatorManager.Free(allocator, blockStreams, blockList.indexCount);
            AllocatorManager.Free(allocator, state,        1);
            blockList.Dispose();
        }

        [BurstCompile]
        private struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public State* state;

            public UnsafeIndexedBlockList blockList;

            [NativeDisableUnsafePtrRestriction]
            public BlockStream* blockStreams;

            public AllocatorManager.AllocatorHandle allocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public void Execute()
            {
                Deallocate(state, blockList, blockStreams, allocator);
            }
        }
        #endregion

        #region Safety Checks
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void CheckWriteAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckAllocatedAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
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

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckTargetBucketIsValid(int bucket)
        {
            var bucketCount = IndexStrategies.BucketCountWithoutNaN(data.cellCount);
            if (bucket < 0 || bucket > bucketCount) // greater than because add 1 for NaN bucket
                throw new ArgumentOutOfRangeException($"The target bucket {bucket} is out of range of max buckets {bucketCount}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckStreamsMatch(ref PairStream other)
        {
            if (data.cellCount != other.data.cellCount)
                throw new InvalidOperationException(
                    $"The streams do not have matching bucket counts: {IndexStrategies.BucketCountWithoutNaN(data.cellCount)} vs {IndexStrategies.BucketCountWithoutNaN(other.data.cellCount)}.");
            if (data.allocator != other.data.allocator)
                throw new InvalidOperationException($"The allocators are not the same. Memory stealing cannot be safely performed.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckPairPtrVersionMatches(State* state, int version)
        {
            if (state->pairPtrVersion != version)
                throw new InvalidOperationException($"The pair allocator has been invalidated by a concatenate operation.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckEnumerationVersionMatches(State* state, int version)
        {
            if (state->pairPtrVersion != version)
                throw new InvalidOperationException($"The enumerator has been invalidated by an addition or concatenate operation.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckNotNull(void* rawPtr)
        {
            if (rawPtr == null)
                throw new InvalidOperationException("Attempted to access a typed allocation from a null object.");
        }

        #endregion
    }
}

