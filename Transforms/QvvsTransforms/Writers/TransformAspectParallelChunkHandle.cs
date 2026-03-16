#if !LATIOS_TRANSFORMS_UNITY
using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public interface IJobChunkParallelTransform
    {
        public ref TransformAspectParallelChunkHandle transformAspectHandleAccess { get; }
    }

    public static class JobChunkParallelTransformExtensions
    {
        /// <summary>
        /// Wraps the IJobChunkParallelTransform job with a TransformsSchedulerJob
        /// </summary>
        public static TransformAspectParallelChunkHandle.TransformsSchedulerJob<T> GetTransformsScheduler<T>(ref this T chunkJob) where T : unmanaged, IJobChunk,
        IJobChunkParallelTransform
        {
            return new TransformAspectParallelChunkHandle.TransformsSchedulerJob<T> { chunkJob = chunkJob };
        }

        /// <summary>
        /// Schedules an IJobParallelForDefer job using the specified TransformAspectParallelChunkHandle to determine the number of iterations.
        /// Each iteration has access to a group of chunks which must be processed together.
        /// </summary>
        /// <param name="job">A job of type IJobParallelForDefer typically used to process entity transforms.</param>
        /// <param name="transformAspectParallelChunkHandle">The handle which should have already had ScheduleChunkGrouping() called</param>
        /// <param name="dependsOn">The JobHandle the new job should depend on (which should include ScheduleChunkGrouping()'s returned JobHandle</param>
        /// <returns>The JobHandle of the scheduled job</returns>
        public static JobHandle ScheduleParallel<T>(this T job, TransformAspectParallelChunkHandle transformAspectParallelChunkHandle, JobHandle dependsOn) where T : unmanaged,
        IJobParallelForDefer
        {
            return job.Schedule(transformAspectParallelChunkHandle.chunkRanges, 1, dependsOn);
        }
    }

    /// <summary>
    /// A structure that can be used to process any transform in parallel by ECS chunks in
    /// IJobEntity, IJobChunk, or a custom IJobParallelForDefer job. This operates by grouping
    /// together chunks which share the same hierarchy, and ensuring chunks in the same group are
    /// operated together on a single thread. For IJobEntity and IJobChunk, the evaluation of the
    /// entities within a hierarchy is based on the order of entities in the EntityQuery, and not
    /// position in the hierarchy. So while global simulation hardware-specific determinism is
    /// preserved, the order that entities update within a hierarchy may be unintuitive and
    /// unstable between frames. A custom IJobParallelForDefer job may reason about all chunks
    /// in a group and reorder the entities it processes to something more intuitive.
    /// Usage of TransformAspectParallelChunkHandle happens in three phases. The first phase
    /// captures chunks matching the EntityQuery, as well as enabled masks and chunk indices.
    /// The second phase groups the chunks together that share hierarchy members. The last phase
    /// is when TransformAspects can be safely iterated and processed.
    /// </summary>
    public unsafe struct TransformAspectParallelChunkHandle
    {
        /* Construct Snippet
           new TransformAspectParallelChunkHandle(SystemAPI.GetComponentLookup<WorldTransform>(false),
                                                  SystemAPI.GetComponentTypeHandle<RootReference>(true),
                                                  SystemAPI.GetBufferLookup<EntityInHierarchy>(true),
                                                  SystemAPI.GetBufferLookup<EntityInHierarchyCleanup>(true),
                                                  SystemAPI.GetEntityStorageInfoLookup(),
                                                  ref state)
         */

        #region Main Thread API
        /// <summary>
        /// Constructs the TransformAspectParallelChunkHandle with a lifecycle of a full frame
        /// </summary>
        /// <param name="worldTransformLookup">A read-write lookup for the WorldTransform component</param>
        /// <param name="rootReferenceHandleRO">A read-only lookup for the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A read-only lookup for the EntityInHierarchy buffer</param>
        /// <param name="entityInHierarchyCleanupRO">A read-only lookup for the EntityInHierarchyCleanup buffer</param>
        /// <param name="entityStorageInfoLookup">A lookup of entity alive statuses</param>
        /// <param name="state">The SystemState of the currently running system</param>
        /// <param name="expectedChunkCount">An optional estimated number of chunks in the processed EntityQuery which can
        /// be used to set the capacity of internal lists for improved performance. Typically you would use EntityQuery.CalculateChunkCountWithoutFiltering() for this.</param>
        public TransformAspectParallelChunkHandle(ComponentLookup<WorldTransform>        worldTransformLookupRW,
                                                  ComponentTypeHandle<RootReference>     rootReferenceHandleRO,
                                                  BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                                  BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupRO,
                                                  EntityStorageInfoLookup entityStorageInfoLookup,
                                                  ref SystemState state,
                                                  int expectedChunkCount = 0)
        {
            allocator           = state.WorldUpdateAllocator;
            transformLookup     = worldTransformLookupRW;
            rootReferenceHandle = rootReferenceHandleRO;
            hierarchyLookup     = entityInHierarchyLookupRO;
            cleanupLookup       = entityInHierarchyCleanupRO;
            esil                = entityStorageInfoLookup;
            chunks              = new NativeList<CapturedChunk>(expectedChunkCount, allocator);
            chunkRanges         = new NativeList<int2>(allocator);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            parallelDetector = new ParallelDetector(allocator);
#endif
            this.expectedChunkCount   = expectedChunkCount;
            currentCapturedChunkIndex = -1;
            didFirstCaptureChunk      = false;
            cache                     = null;
            hierarchyChecker          = default;
            cleanupChecker            = default;
        }

        /// <summary>
        /// Schedule a general-purpose IJobChunk job to capture the chunks in the specified EntityQuery.
        /// This can be useful to optimize scheduling when constructing complex parallel chains of jobs.
        /// It is required when setting up an IJobParallelForDefer processing job.
        /// WARNING: This method cannot be used for making TransformAspect available to an IJobEntity job.
        /// </summary>
        /// <param name="query">The EntityQuery to capture chunks, index, and enabled states</param>
        /// <param name="inputDeps">The JobHandle with read-only access to any IEnableableComponent states
        /// checked by the EntityQuery</param>
        /// <returns>A JobHandle corresponding to the job that captured the chunks</returns>
        public JobHandle ScheduleChunkCaptureForQuery(EntityQuery query, JobHandle inputDeps)
        {
            var job = new CaptureTransformChunksFromQueryJob
            {
                chunks             = chunks,
                chunkRanges        = chunkRanges,
                expectedChunkCount = query.CalculateChunkCountWithoutFiltering(),
            };
            return job.ScheduleByRef(query, inputDeps);
        }

        /// <summary>
        /// Schedule the chunk grouping stage job(s) after collecting the chunks.
        /// </summary>
        /// <param name="inputDeps">The JobHandle corresponding to the job that collected the chunks, as
        /// well as providing read-only access to the RootReference type</param>
        /// <returns>A JobHandle corresponding to the job(s) that grouped the chunks</returns>
        public JobHandle ScheduleChunkGrouping(JobHandle inputDeps)
        {
            var job = new UnionFindJob
            {
                chunks              = chunks,
                chunkRanges         = chunkRanges,
                entityHandle        = esil.AsEntityTypeHandle(),
                rootReferenceHandle = rootReferenceHandle
            };
            return job.ScheduleByRef(inputDeps);
        }

        /// <summary>
        /// A wrapper job around IJobChunk used for dispatching groups of chunks together in a thread-safe manner.
        /// </summary>
        /// <typeparam name="T">The IJobChunk (or IJobEntity) to wrap</typeparam>
        [BurstCompile]
        public struct TransformsSchedulerJob<T> : IJobParallelForDefer where T : unmanaged, IJobChunk, IJobChunkParallelTransform
        {
            /// <summary>
            /// The base IJobChunk job
            /// </summary>
            public T chunkJob;

            /// <summary>
            /// Schedule a parallel job that invokes the chunkJob's Execute() method
            /// </summary>
            /// <param name="inputDeps">The input JobHandle needed by chunkJob</param>
            /// <returns>The JobHandle of the scheduled job</returns>
            public JobHandle ScheduleParallel(JobHandle inputDeps)
            {
                return this.ScheduleByRef(chunkJob.transformAspectHandleAccess.chunkRanges, 1, inputDeps);
            }

            public void Execute(int parallelForIndex)
            {
                ref var handle     = ref chunkJob.transformAspectHandleAccess;
                var     chunkCount = handle.GetChunkCountForIJobParallelForDeferIndex(parallelForIndex);
                for (int i = 0; i < chunkCount; i++)
                {
                    handle.GetChunkInGroupForIJobParallelForDefer(parallelForIndex, i, out var chunk, out var unfilteredChunkIndex, out var useEnabledMask,
                                                                  out var chunkEnabledMask);
                    handle.currentCapturedChunkIndex = handle.chunkRanges[parallelForIndex].x + i;
                    chunkJob.Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
                }
            }
        }
        #endregion

        #region In-Job API
        /// <summary>
        /// A convenience method to return a job member field by ref to fulfill the IJobChunkParallelTransform interface
        /// </summary>
        public ref TransformAspectParallelChunkHandle RefAccess()
        {
            fixed (TransformAspectParallelChunkHandle* ptr = &this)
            return ref *ptr;
        }

        /// <summary>
        /// A method you should call inside IJobEntityChunkBeginEnd.OnChunkBegin() or at the beginning of an IJobChunk job
        /// to capture the chunk or prepare it for processing transforms.
        /// </summary>
        /// <param name="chunk">The chunk passed in to the job method</param>
        /// <param name="unfilteredChunkIndex">The unfilteredChunkIndex passed in to the job method</param>
        /// <param name="useEnabledMask">The useEnabledMask passed in to the job method</param>
        /// <param name="chunkEnabledMask">The chunkEnabledMask passed in to the job method</param>
        /// <returns>True if the job should proceed with processing transforms, false if this is the chunk capturing phase</returns>
        public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!hasGroupedChunks)
            {
                if (!didFirstCaptureChunk)
                {
                    CheckCaptureIsSingleThreaded();
                    chunks.Clear();
                    chunkRanges.Clear();
                    if (expectedChunkCount > chunks.Capacity)
                        chunks.Capacity  = expectedChunkCount;
                    didFirstCaptureChunk = true;
                }
                Role role       = Role.Solo;
                bool hasCleanup = false;
                if (chunk.Has(ref rootReferenceHandle))
                    role = Role.Child;
                else if (hierarchyChecker[chunk])
                {
                    role       = Role.Root;
                    hasCleanup = cleanupChecker[chunk];
                }
                chunks.Add(new CapturedChunk
                {
                    chunk                = chunk,
                    unfilteredChunkIndex = unfilteredChunkIndex,
                    useEnabledMask       = useEnabledMask,
                    enabledMask          = chunkEnabledMask,
                    role                 = role,
                    rootHasCleanup       = hasCleanup,
                    enabledEntityCount   = useEnabledMask ? (math.countbits(chunkEnabledMask.ULong0) + math.countbits(chunkEnabledMask.ULong1)) : chunk.Count
                });
                return false;
            }
            else
            {
                CheckCapturedChunkMatches(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
                SetupChunk(chunks[currentCapturedChunkIndex]);
                return true;
            }
        }

        /// <summary>
        /// Access to the TransformAspect at the specified index of the currently active chunk
        /// </summary>
        public TransformAspect this[int indexInChunk]
        {
            get
            {
                CheckInit();
                CheckIndexInChunkValid(indexInChunk);
                var transform = new RefRW<WorldTransform>(cache->chunkTransforms, indexInChunk);
                switch (cache->role)
                {
                    case Role.Solo:
                        return new TransformAspect
                        {
                            m_worldTransform = transform,
                            m_handle         = default
                        };
                    case Role.Root:
                    {
                        var extra  = cache->entityInHierarchyCleanupAccessor.Length > 0 ? cache->entityInHierarchyCleanupAccessor[indexInChunk].GetUnsafeReadOnlyPtr() : null;
                        var handle = new EntityInHierarchyHandle
                        {
                            m_hierarchy      = cache->entityInHierarchyAccessor[indexInChunk].AsNativeArray(),
                            m_extraHierarchy = (EntityInHierarchy*)extra,
                            m_index          = 0
                        };
                        return new TransformAspect
                        {
                            m_worldTransform = transform,
                            m_handle         = handle,
                            m_esil           = esil,
                            m_accessType     = TransformAspect.AccessType.ComponentLookup,
                            m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                        };
                    }
                    case Role.Child:
                    {
                        var rr     = cache->rootReferences[indexInChunk];
                        var handle = rr.ToHandle(ref hierarchyLookup, ref cleanupLookup);
                        return new TransformAspect
                        {
                            m_worldTransform = transform,
                            m_handle         = handle,
                            m_esil           = esil,
                            m_accessType     = TransformAspect.AccessType.ComponentLookup,
                            m_access         = UnsafeUtility.AddressOf(ref transformLookup)
                        };
                    }
                    default:
                        return default;
                }
            }
        }

        /// <summary>
        /// Gets the TransformsKey associated with hierarchy the entity at the specified index in the chunk belongs to
        /// </summary>
        public TransformsKey GetTransformsKey(int indexInChunk)
        {
            CheckInit();
            CheckIndexInChunkValid(indexInChunk);
            switch (cache->role)
            {
                case Role.Solo:
                    return TransformsKey.CreateFromExclusivelyAccessedRoot(cache->chunk.GetEntityDataPtrRO(esil.AsEntityTypeHandle())[indexInChunk], esil);
                case Role.Root:
                    return TransformsKey.CreateFromExclusivelyAccessedRoot(cache->entityInHierarchyAccessor[indexInChunk][0].entity, esil);
                case Role.Child:
                {
                    var rr     = cache->rootReferences[indexInChunk];
                    var handle = rr.ToHandle(ref hierarchyLookup, ref cleanupLookup);
                    return TransformsKey.CreateFromExclusivelyAccessedRoot(handle.root.entity, esil);
                }
                default:
                    return default;
            }
        }

        /// <summary>
        /// Gets the number of chunks in the group for the current IJobParallelForDefer index.
        /// </summary>
        public int GetChunkCountForIJobParallelForDeferIndex(int parallelForIndex) => chunkRanges[parallelForIndex].y;

        /// <summary>
        /// Gets the specified chunk and parameters for the specified chunk index in the group reserved for the current parallel-for index
        /// </summary>
        /// <param name="parallelForIndex">The index passed into the Execute() method of the IJobParallelForDefer job</param>
        /// <param name="chunkIndexInGroup">The index of the chunk within the group</param>
        public void GetChunkInGroupForIJobParallelForDefer(int parallelForIndex,
                                                           int chunkIndexInGroup,
                                                           out ArchetypeChunk chunk,
                                                           out int unfilteredChunkIndex,
                                                           out bool useEnabledMask,
                                                           out v128 chunkEnabledMask)
        {
            var range = chunkRanges[parallelForIndex];
            CheckIndexInGroupInRange(range.y, chunkIndexInGroup);
            var capture          = chunks[range.x + chunkIndexInGroup];
            chunk                = capture.chunk;
            unfilteredChunkIndex = capture.unfilteredChunkIndex;
            useEnabledMask       = capture.useEnabledMask;
            chunkEnabledMask     = capture.enabledMask;
        }

        /// <summary>
        /// Marks the specified chunk as the active chunk for TransformAspect indexing.
        /// <param name="parallelForIndex">The index passed into the Execute() method of the IJobParallelForDefer job</param>
        /// <param name="chunkIndexInGroup">The index of the chunk within the group</param>
        /// </summary>
        /// <param name="parallelForIndex"></param>
        /// <param name="chunkIndexInGroup"></param>
        public void SetActiveChunkForIJobParallelForDefer(int parallelForIndex, int chunkIndexInGroup)
        {
            var range = chunkRanges[parallelForIndex];
            CheckIndexInGroupInRange(range.y, chunkIndexInGroup);
            currentCapturedChunkIndex = range.x + chunkIndexInGroup;
            SetupChunk(chunks[currentCapturedChunkIndex]);
        }
        #endregion

        #region Impl
        enum Role : byte
        {
            Solo,
            Root,
            Child
        }

        struct CapturedChunk
        {
            public ArchetypeChunk chunk;
            public v128           enabledMask;
            public bool           useEnabledMask;
            public Role           role;
            public bool           rootHasCleanup;
            public int            unfilteredChunkIndex;
            public int            enabledEntityCount;
        }

        struct ThreadCache
        {
            public ComponentTypeHandle<WorldTransform>        transformHandle;
            public NativeArray<WorldTransform>                chunkTransforms;
            public BufferTypeHandle<EntityInHierarchy>        entityInHierarchyHandle;
            public BufferAccessor<EntityInHierarchy>          entityInHierarchyAccessor;
            public BufferTypeHandle<EntityInHierarchyCleanup> entityInHierarchyCleanupHandle;
            public BufferAccessor<EntityInHierarchyCleanup>   entityInHierarchyCleanupAccessor;
            public NativeArray<RootReference>                 rootReferences;
            public ArchetypeChunk                             chunk;
            public Role                                       role;
        }

        [NativeDisableParallelForRestriction] NativeList<CapturedChunk> chunks;
        [NativeDisableParallelForRestriction] internal NativeList<int2> chunkRanges;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        ParallelDetector parallelDetector;
#endif
        bool hasGroupedChunks => !chunkRanges.IsEmpty;
        int                              expectedChunkCount;
        int                              currentCapturedChunkIndex;
        bool                             didFirstCaptureChunk;
        AllocatorManager.AllocatorHandle allocator;

        TransformsComponentLookup<WorldTransform>            transformLookup;
        [ReadOnly] BufferLookup<EntityInHierarchy>           hierarchyLookup;
        [ReadOnly] BufferLookup<EntityInHierarchyCleanup>    cleanupLookup;
        [ReadOnly] public ComponentTypeHandle<RootReference> rootReferenceHandle;
        [ReadOnly] EntityStorageInfoLookup                   esil;
        [NativeDisableUnsafePtrRestriction] ThreadCache*     cache;

        HasChecker<EntityInHierarchy>        hierarchyChecker;
        HasChecker<EntityInHierarchyCleanup> cleanupChecker;

        void SetupChunk(in CapturedChunk chunk)
        {
            if (cache == null)
            {
                cache                                 = AllocatorManager.Allocate<ThreadCache>(Allocator.Temp);
                cache->transformHandle                = transformLookup.lookup.ToHandle(false);
                cache->entityInHierarchyHandle        = hierarchyLookup.ToHandle(true);
                cache->entityInHierarchyCleanupHandle = cleanupLookup.ToHandle(true);
            }
            cache->chunkTransforms = chunk.chunk.GetNativeArray(ref cache->transformHandle);
            if (chunk.role == Role.Solo)
            {
                cache->entityInHierarchyAccessor        = default;
                cache->entityInHierarchyCleanupAccessor = default;
                cache->rootReferences                   = default;
            }
            else if (chunk.role == Role.Root)
            {
                cache->entityInHierarchyAccessor = chunk.chunk.GetBufferAccessorRO(ref cache->entityInHierarchyHandle);
                if (chunk.rootHasCleanup)
                    cache->entityInHierarchyCleanupAccessor = chunk.chunk.GetBufferAccessorRO(ref cache->entityInHierarchyCleanupHandle);
                else
                    cache->entityInHierarchyCleanupAccessor = default;
                cache->rootReferences                       = default;
            }
            else if (chunk.role == Role.Child)
            {
                cache->entityInHierarchyAccessor        = default;
                cache->entityInHierarchyCleanupAccessor = default;
                cache->rootReferences                   = chunk.chunk.GetNativeArray(ref rootReferenceHandle);
            }
            cache->chunk = chunk.chunk;
            cache->role  = chunk.role;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckCaptureIsSingleThreaded()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (parallelDetector.isParallel)
                throw new System.InvalidOperationException("Cannot capture chunks in parallel. Please use ScheduleByRef()");
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckCapturedChunkMatches(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var captured = chunks[currentCapturedChunkIndex];
            if (captured.chunk != chunk)
                throw new System.InvalidOperationException("The chunk does not match the captured chunk. Did you try to change the chunk to be processed?");
            if (captured.unfilteredChunkIndex != unfilteredChunkIndex)
                throw new System.InvalidOperationException("The chunk index does not match the captured chunk index. Did you change the chunk index to be processed?");
            if (captured.useEnabledMask && !useEnabledMask)
                throw new System.ArgumentException(
                    "The passed in useEnabledMask is false, whereas when the chunk was captured, this value was true. This implies more entities may be processed than was declared during capture time. Thus, safety cannot be ensured.");
            if (captured.useEnabledMask && useEnabledMask)
            {
                if ((chunkEnabledMask.ULong0 & ~captured.enabledMask.ULong0) != 0 || (chunkEnabledMask.ULong1 & ~captured.enabledMask.ULong1) != 0)
                    throw new System.ArgumentException(
                        "The passed in chunkEnabledMask has set bits that were not set when the chunk was captured. This implies more entities may be processed than was declared during capture time. Thus, safety cannot be ensured.");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckInit()
        {
            if (cache == null)
                throw new System.InvalidOperationException(
                    "The TransformAccessParallelChunkHandle has not been set up. Use IJobEntityChunkBeginEnd or IJobChunk to pass in the current chunk to OnChunkBegin().");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckIndexInChunkValid(int indexInChunk)
        {
            var capture = chunks[currentCapturedChunkIndex];
            if (!capture.useEnabledMask)
                return;
            BitField64 bits;
            int        bitIndex;
            if (indexInChunk < 64)
            {
                bits     = new BitField64(capture.enabledMask.ULong0);
                bitIndex = indexInChunk;
            }
            else
            {
                bits     = new BitField64(capture.enabledMask.ULong1);
                bitIndex = indexInChunk - 64;
            }
            if (!bits.IsSet(bitIndex))
                throw new System.ArgumentOutOfRangeException($"indexInChunk {indexInChunk} is not an index enabled in the captured chunk.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckIndexInGroupInRange(int groupCount, int indexInGroup)
        {
            if (indexInGroup < 0 || indexInGroup >= groupCount)
                throw new System.ArgumentOutOfRangeException($"indexInGroup {indexInGroup} is out of range of chunks in group count of {groupCount}");
        }

        [BurstCompile]
        struct CaptureTransformChunksFromQueryJob : IJobChunk
        {
            public NativeList<CapturedChunk> chunks;
            public NativeList<int2>          chunkRanges;
            public int                       expectedChunkCount;

            HasChecker<RootReference>            rootReferenceChecker;
            HasChecker<EntityInHierarchy>        hierarchyChecker;
            HasChecker<EntityInHierarchyCleanup> cleanupChecker;
            bool                                 didFirstCaptureChunk;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!didFirstCaptureChunk)
                {
                    chunks.Clear();
                    chunkRanges.Clear();
                    if (expectedChunkCount > chunks.Capacity)
                        chunks.Capacity  = expectedChunkCount;
                    didFirstCaptureChunk = true;
                }
                Role role       = Role.Solo;
                bool hasCleanup = false;
                if (rootReferenceChecker[chunk])
                    role = Role.Child;
                else if (hierarchyChecker[chunk])
                {
                    role       = Role.Root;
                    hasCleanup = cleanupChecker[chunk];
                }
                chunks.Add(new CapturedChunk
                {
                    chunk                = chunk,
                    unfilteredChunkIndex = unfilteredChunkIndex,
                    useEnabledMask       = useEnabledMask,
                    enabledMask          = chunkEnabledMask,
                    role                 = role,
                    rootHasCleanup       = hasCleanup,
                    enabledEntityCount   = useEnabledMask ? (math.countbits(chunkEnabledMask.ULong0) + math.countbits(chunkEnabledMask.ULong1)) : chunk.Count
                });
            }
        }

        [BurstCompile]
        struct UnionFindJob : IJob
        {
            public NativeList<CapturedChunk>                     chunks;
            public NativeList<int2>                              chunkRanges;
            [ReadOnly] public EntityTypeHandle                   entityHandle;
            [ReadOnly] public ComponentTypeHandle<RootReference> rootReferenceHandle;

            public unsafe void Execute()
            {
                if (chunks.IsEmpty)
                    return;

                // Rearrange the chunks so that solo chunks are at the end.
                int hierarchyChunkCount;
                {
                    int currentHierarchyIndex = 0;
                    int currentSoloIndex      = chunks.Length - 1;
                    for (; currentHierarchyIndex < currentSoloIndex; currentHierarchyIndex++)
                    {
                        if (chunks[currentHierarchyIndex].role == Role.Solo)
                        {
                            bool swapped = false;
                            for (; currentSoloIndex > currentHierarchyIndex && !swapped; currentSoloIndex--)
                            {
                                if (chunks[currentSoloIndex].role != Role.Solo)
                                {
                                    (chunks[currentHierarchyIndex], chunks[currentSoloIndex]) = (chunks[currentSoloIndex], chunks[currentHierarchyIndex]);
                                    swapped                                                   = true;
                                }
                            }
                        }
                    }
                    hierarchyChunkCount = currentSoloIndex;
                }

                var hierarchyChunks = chunks.AsArray().GetSubArray(0, hierarchyChunkCount);
                // Count entities so that we can allocate the hashmap accordingly
                int entityCount = 0;
                foreach (var chunk in hierarchyChunks)
                    entityCount += chunk.enabledEntityCount;

                // Perform the Union Find algorithm
                var entityToFirstChunkMap = new UnsafeHashMap<int, int>(entityCount + 1, Allocator.Temp);
                var sets                  = new NativeArray<int>(hierarchyChunkCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < hierarchyChunkCount; i++)
                {
                    sets[i]        = i;
                    var chunk      = hierarchyChunks[i];
                    var enumerator = new ChunkEntityEnumerator(chunk.useEnabledMask, chunk.enabledMask, chunk.chunk.Count);
                    if (chunk.role == Role.Root)
                    {
                        var entities = chunk.chunk.GetEntityDataPtrRO(entityHandle);
                        while (enumerator.NextEntityIndex(out var entityIndex))
                        {
                            Union(entities[entityIndex], i, ref entityToFirstChunkMap, ref sets);
                        }
                    }
                    else
                    {
                        var rootRefs = chunk.chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
                        while (enumerator.NextEntityIndex(out var entityIndex))
                        {
                            Union(rootRefs[entityIndex].rootEntity, i, ref entityToFirstChunkMap, ref sets);
                        }
                    }
                }

                // Collapse the trees (we iterate backwards because we are using the greater-index algorithm
                // Also, get count in each set as we will use a counting sort to reorder the chunks
                var setCounts = new NativeArray<int>(sets.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int i = hierarchyChunkCount - 1; i >= 0; i--)
                {
                    var rootSet = sets[sets[i]];
                    setCounts[rootSet]++;
                    sets[i] = rootSet;
                }

                // Prefix sum and build ranges
                int running          = 0;
                chunkRanges.Capacity = chunks.Length;  // Overestimate, but that's probably faster than computing a minimum capacity
                for (int i = 0; i < setCounts.Length; i++)
                {
                    var count = setCounts[i];
                    if (count > 0)
                    {
                        chunkRanges.AddNoResize(new int2(running, count));
                        setCounts[i]  = running;
                        running      += count;
                    }
                }

                // Assign chunk order using a backup copy
                var backup = new NativeArray<CapturedChunk>(hierarchyChunks, Allocator.Temp);
                for (int i = 0; i < backup.Length; i++)
                {
                    var set = sets[i];
                    var dst = setCounts[set];
                    setCounts[set]++;
                    hierarchyChunks[dst] = backup[i];
                }

                // Assign solo chunks to ranges
                for (int i = hierarchyChunkCount; i < chunks.Length; i++)
                {
                    chunkRanges.AddNoResize(new int2(i, 1));
                }
            }

            static void Union(Entity entity, int chunkIndex, ref UnsafeHashMap<int, int> entityToFirstChunkMap, ref NativeArray<int> sets)
            {
                if (!entityToFirstChunkMap.TryGetValue(entity.Index, out var firstChunkIndex))
                {
                    entityToFirstChunkMap.Add(entity.Index, chunkIndex);
                    return;
                }

                // Rem's algorithm
                var treeNodeA = chunkIndex;
                var treeNodeB = firstChunkIndex;
                while (sets[treeNodeA] != sets[treeNodeB])
                {
                    if (sets[treeNodeA] < sets[treeNodeB])
                    {
                        if (sets[treeNodeA] == treeNodeA)
                        {
                            sets[treeNodeA] = sets[treeNodeB];
                            break;
                        }
                        var temp        = treeNodeA;
                        sets[treeNodeA] = sets[treeNodeB];
                        treeNodeA       = sets[temp];
                    }
                    else
                    {
                        if (sets[treeNodeB] == treeNodeB)
                        {
                            sets[treeNodeB] = sets[treeNodeA];
                            break;
                        }
                        var temp        = treeNodeB;
                        sets[treeNodeB] = sets[treeNodeA];
                        treeNodeB       = sets[temp];
                    }
                }
            }
        }
        #endregion
    }
}
#endif

