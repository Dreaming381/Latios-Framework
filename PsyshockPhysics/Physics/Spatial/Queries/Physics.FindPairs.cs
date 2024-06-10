using System;
using System.Diagnostics;
using Latios.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// An interface whose Execute method is invoked for each pair found in a FindPairs operation.
    /// </summary>
    public interface IFindPairsProcessor
    {
        /// <summary>
        /// The main pair processing callback.
        /// </summary>
        void Execute(in FindPairsResult result);
        /// <summary>
        /// An optional callback prior to processing a bucket (single layer) or bucket combination (two layers).
        /// Each invocation of this callback will have a different jobIndex.
        /// </summary>
        /// <returns>Returns true if the Execute() and EndBucket() methods should be called. Otherwise, further
        /// processing of the bucket is skipped.</returns>
        bool BeginBucket(in FindPairsBucketContext context)
        {
            return true;
        }
        /// <summary>
        /// An optional callback following processing a bucket (single layer) or bucket combination (two layers).
        /// Each invocation of this callback will have a different jobIndex.
        /// </summary>
        void EndBucket(in FindPairsBucketContext context)
        {
        }
    }

    /// <summary>
    /// A result struct passed into the Execute method of an IFindPairsProcessor providing info
    /// about the pair of colliders whose Aabbs overlapped.
    /// </summary>
    [NativeContainer]
    public struct FindPairsResult
    {
        /// <summary>
        /// The CollisionLayer the first collider in pair belongs to. Can be used for additional
        /// immediate queries inside the processor.
        /// </summary>
        public CollisionLayer layerA => m_layerA;
        /// <summary>
        /// The CollisionLayer the second collider in the pair belongs to. Can be used for additional
        /// immediate queries inside the processor. If the FindPairs operation only uses a single
        /// CollisionLayer, this value is the same as layerA.
        /// </summary>
        public CollisionLayer layerB => m_layerB;
        /// <summary>
        /// The full ColliderBody of the first collider in the pair
        /// </summary>
        public ColliderBody bodyA => m_layerA.bodies[bodyIndexA];
        /// <summary>
        /// The full ColliderBody of the second collider in the pair
        /// </summary>
        public ColliderBody bodyB => m_layerB.bodies[bodyIndexB];
        /// <summary>
        /// The first collider in the pair
        /// </summary>
        public Collider colliderA => bodyA.collider;
        /// <summary>
        /// The second collider in the pair
        /// </summary>
        public Collider colliderB => bodyB.collider;
        /// <summary>
        /// The transform of the first collider in the pair
        /// </summary>
        public TransformQvvs transformA => bodyA.transform;
        /// <summary>
        /// The transform of the second collider in the pair
        /// </summary>
        public TransformQvvs transformB => bodyB.transform;
        /// <summary>
        /// The index of the first collider in the pair within its CollisionLayer
        /// </summary>
        public int bodyIndexA => m_bodyAIndex;
        /// <summary>
        /// The index of the second collider in the pair within its CollisionLayer
        /// </summary>
        public int bodyIndexB => m_bodyBIndex;
        /// <summary>
        /// The index of the first collider in the pair relative to the original EntityQuery or NativeArrays used to
        /// create the CollisionLayer.
        /// </summary>
        public int sourceIndexA => m_layerA.srcIndices[bodyIndexA];
        /// <summary>
        /// The index of the second collider in the pair relative to the original EntityQuery or NativeArrays used to
        /// create the CollisionLayer.
        /// </summary>
        public int sourceIndexB => m_layerB.srcIndices[bodyIndexB];
        /// <summary>
        /// The bucket index of the first collider in the pair, used for populating a PairStream in an immediate context
        /// </summary>
        public int bucketIndexA => m_bucketIndexA;
        /// <summary>
        /// The bucket index of the second collider in the pair, used for populating a PairStream in an immediate context
        /// </summary>
        public int bucketIndexB => m_bucketIndexB;
        /// <summary>
        /// An index that is guaranteed to be deterministic and unique between threads for a given FindPairs operation,
        /// and can be used as the sortKey for command buffers
        /// </summary>
        public int jobIndex => m_jobIndex;

        /// <summary>
        /// The Aabb of the first collider in the pair
        /// </summary>
        public Aabb aabbA
        {
            get
            {
                var yzminmax = m_layerA.yzminmaxs[m_bodyAIndex];
                var xmin     = m_layerA.xmins[m_bodyAIndex];
                var xmax     = m_layerA.xmaxs[m_bodyAIndex];
                return new Aabb(new float3(xmin, yzminmax.xy), new float3(xmax, -yzminmax.zw));
            }
        }

        /// <summary>
        /// The Aabb of the second collider in the pair
        /// </summary>
        public Aabb aabbB
        {
            get
            {
                var yzminmax = m_layerB.yzminmaxs[m_bodyBIndex];
                var xmin     = m_layerB.xmins[m_bodyBIndex];
                var xmax     = m_layerB.xmaxs[m_bodyBIndex];
                return new Aabb(new float3(xmin, yzminmax.xy), new float3(xmax, -yzminmax.zw));
            }
        }

        /// <summary>
        /// A key that can be used in a PairStream.ParallelWriter
        /// </summary>
        public PairStream.ParallelWriteKey pairStreamKey
        {
            get
            {
                CheckCanGenerateParallelPairKey();
                return new PairStream.ParallelWriteKey
                {
                    entityA             = bodyA.entity,
                    entityB             = bodyB.entity,
                    streamIndexA        = IndexStrategies.BucketStreamIndexFromFindPairsJobIndex(m_bucketIndexA, m_jobIndex, layerA.cellCount),
                    streamIndexB        = IndexStrategies.BucketStreamIndexFromFindPairsJobIndex(m_bucketIndexB, m_jobIndex, layerA.cellCount),
                    streamIndexCombined = IndexStrategies.MixedStreamIndexFromFindPairsJobIndex(jobIndex, layerA.cellCount),
                    cellCount           = layerA.cellCount
                };
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        /// <summary>
        /// A safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
        /// owning entity of the first collider in the pair. It can also be implicitly casted and used as a normal entity reference.
        /// </summary>
        public SafeEntity entityA => new SafeEntity
        {
            m_entity = new Entity
            {
                Index   = math.select(-bodyA.entity.Index - 1, bodyA.entity.Index, m_isAThreadSafe),
                Version = bodyA.entity.Version
            }
        };
        /// <summary>
        /// A safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
        /// owning entity of the second collider in the pair. It can also be implicitly casted and used as a normal entity reference.
        /// </summary>
        public SafeEntity entityB => new SafeEntity
        {
            m_entity = new Entity
            {
                Index   = math.select(-bodyB.entity.Index - 1, bodyB.entity.Index, m_isBThreadSafe),
                Version = bodyB.entity.Version
            }
        };
#else
        public SafeEntity entityA => new SafeEntity { m_entity = bodyA.entity };
        public SafeEntity entityB => new SafeEntity { m_entity = bodyB.entity };
#endif

        private CollisionLayer m_layerA;
        private CollisionLayer m_layerB;
        private int            m_bucketStartA;
        private int            m_bucketStartB;
        private int            m_bodyAIndex;
        private int            m_bodyBIndex;
        private int            m_bucketIndexA;
        private int            m_bucketIndexB;
        private int            m_jobIndex;
        private bool           m_isAThreadSafe;
        private bool           m_isBThreadSafe;
        private bool           m_isImmediateContext;

        internal FindPairsResult(in CollisionLayer layerA,
                                 in CollisionLayer layerB,
                                 in BucketSlices bucketA,
                                 in BucketSlices bucketB,
                                 int jobIndex,
                                 bool isAThreadSafe,
                                 bool isBThreadSafe,
                                 bool isImmediateContext = false)
        {
            m_layerA             = layerA;
            m_layerB             = layerB;
            m_bucketStartA       = bucketA.bucketGlobalStart;
            m_bucketStartB       = bucketB.bucketGlobalStart;
            m_bucketIndexA       = bucketA.bucketIndex;
            m_bucketIndexB       = bucketB.bucketIndex;
            m_jobIndex           = jobIndex;
            m_isAThreadSafe      = isAThreadSafe;
            m_isBThreadSafe      = isBThreadSafe;
            m_isImmediateContext = isImmediateContext;
            m_bodyAIndex         = 0;
            m_bodyBIndex         = 0;
        }

        internal static FindPairsResult CreateGlobalResult(in CollisionLayer layerA,
                                                           in CollisionLayer layerB,
                                                           int bucketIndexA,
                                                           int bucketIndexB,
                                                           int jobIndex,
                                                           bool isAThreadSafe,
                                                           bool isBThreadSafe,
                                                           bool isImmediateContext = false)
        {
            return new FindPairsResult
            {
                m_layerA             = layerA,
                m_layerB             = layerB,
                m_bucketStartA       = 0,
                m_bucketStartB       = 0,
                m_bucketIndexA       = bucketIndexA,
                m_bucketIndexB       = bucketIndexB,
                m_jobIndex           = jobIndex,
                m_isAThreadSafe      = isAThreadSafe,
                m_isBThreadSafe      = isBThreadSafe,
                m_isImmediateContext = isImmediateContext,
                m_bodyAIndex         = 0,
                m_bodyBIndex         = 0,
            };
        }

        internal void SetBucketRelativePairIndices(int aIndex, int bIndex)
        {
            m_bodyAIndex = aIndex + m_bucketStartA;
            m_bodyBIndex = bIndex + m_bucketStartB;
        }
        //Todo: Shorthands for calling narrow phase distance and manifold queries

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckCanGenerateParallelPairKey()
        {
            if (m_isImmediateContext)
                throw new InvalidOperationException($"Cannot generate a ParallelWriteKey in a FindPairs.RunImmediate() context.");
        }
    }

    /// <summary>
    /// A context struct passed into BeginBucket and EndBucket of an IFindPairsProcessor which provides
    /// additional information about the buckets being processed.
    /// </summary>
    [NativeContainer]
    public struct FindPairsBucketContext
    {
        /// <summary>
        /// The CollisionLayer the first collider in any pair belongs to. Can be used for additional
        /// immediate queries inside the processor.
        /// </summary>
        public CollisionLayer layerA => m_layerA;
        /// <summary>
        /// The CollisionLayer the second collider in any pair belongs to. Can be used for additional
        /// immediate queries inside the processor. If the FindPairs operation only uses a single
        /// CollisionLayer, this value is the same as layerA.
        /// </summary>
        public CollisionLayer layerB => m_layerB;
        /// <summary>
        /// The first collider index in layerA that is processed with this jobIndex.
        /// </summary>
        public int bucketStartA => m_bucketStartA;
        /// <summary>
        /// The first collider index in layerB that is processed with this jobIndex.
        /// </summary>
        public int bucketStartB => m_bucketStartB;
        /// <summary>
        /// The number of colliders in layerA that is processed with this jobIndex.
        /// </summary>
        public int bucketCountA => m_bucketCountA;
        /// <summary>
        /// The number of colliders in layerB that is processed with this jobIndex.
        /// </summary>
        public int bucketCountB => m_bucketCountB;
        /// <summary>
        /// The bucket index of the colliders in layerA, used for populating a PairStream in an immediate context.
        /// </summary>
        public int bucketIndexA => m_bucketIndexA;
        /// <summary>
        /// The bucket index of the colliders in layerB, used for populating a PairStream in an immediate context.
        /// </summary>
        public int bucketIndexB => m_bucketIndexB;
        /// <summary>
        /// An index that is guaranteed to be deterministic and unique between threads for a given FindPairs operation,
        /// and can be used as the sortKey for command buffers or as an index in a NativeStream.
        /// </summary>
        public int jobIndex => m_jobIndex;

        /// <summary>
        /// A key that can be used in a PairStream.ParallelWriter
        /// </summary>
        public PairStream.ParallelWriteKey CreatePairStreamKey(int aIndex, int bIndex)
        {
            CheckSafeEntityInRange(aIndex, bucketStartA, bucketCountA);
            CheckSafeEntityInRange(bIndex, bucketStartB, bucketCountB);
            CheckCanGenerateParallelPairKey();

            return new PairStream.ParallelWriteKey
            {
                entityA             = layerA.bodies[aIndex].entity,
                entityB             = layerB.bodies[bIndex].entity,
                streamIndexA        = IndexStrategies.BucketStreamIndexFromFindPairsJobIndex(m_bucketIndexA, m_jobIndex, layerA.cellCount),
                streamIndexB        = IndexStrategies.BucketStreamIndexFromFindPairsJobIndex(m_bucketIndexB, m_jobIndex, layerA.cellCount),
                streamIndexCombined = IndexStrategies.MixedStreamIndexFromFindPairsJobIndex(jobIndex, layerA.cellCount),
                cellCount           = layerA.cellCount
            };
        }

        /// <summary>
        /// Obtains a safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
        /// owning entity of the first collider in any pair in this bucket for layerA. It can also be implicitly casted and used as a normal
        /// entity reference.
        /// </summary>
        public SafeEntity GetSafeEntityInA(int aIndex)
        {
            CheckSafeEntityInRange(aIndex, bucketStartA, bucketCountA);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var entity = layerA.bodies[aIndex].entity;
            return new SafeEntity
            {
                m_entity = new Entity
                {
                    Index   = math.select(-entity.Index - 1, entity.Index, m_isAThreadSafe),
                    Version = entity.Version
                }
            };
#else
            return new SafeEntity
            {
                m_entity = layerA.bodies[aIndex].entity
            };
#endif
        }

        /// <summary>
        /// Obtains a safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
        /// owning entity of the first collider in any pair in this bucket for layerB. It can also be implicitly casted and used as a normal
        /// entity reference.
        /// </summary>
        public SafeEntity GetSafeEntityInB(int bIndex)
        {
            CheckSafeEntityInRange(bIndex, bucketStartB, bucketCountB);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var entity = layerB.bodies[bIndex].entity;
            return new SafeEntity
            {
                m_entity = new Entity
                {
                    Index   = math.select(-entity.Index - 1, entity.Index, m_isBThreadSafe),
                    Version = entity.Version
                }
            };
#else
            return new SafeEntity
            {
                m_entity = layerB.bodies[bIndex].entity
            };
#endif
        }

        private CollisionLayer m_layerA;
        private CollisionLayer m_layerB;
        private int            m_bucketStartA;
        private int            m_bucketStartB;
        private int            m_bucketCountA;
        private int            m_bucketCountB;
        private int            m_bucketIndexA;
        private int            m_bucketIndexB;
        private int            m_jobIndex;
        private bool           m_isAThreadSafe;
        private bool           m_isBThreadSafe;
        private bool           m_isImmediateContext;

        internal FindPairsBucketContext(in CollisionLayer layerA,
                                        in CollisionLayer layerB,
                                        in BucketSlices bucketA,
                                        in BucketSlices bucketB,
                                        int jobIndex,
                                        bool isAThreadSafe,
                                        bool isBThreadSafe,
                                        bool isImmediateContext = false)
        {
            m_layerA             = layerA;
            m_layerB             = layerB;
            m_bucketStartA       = bucketA.bucketGlobalStart;
            m_bucketCountA       = bucketA.count;
            m_bucketIndexA       = bucketA.bucketIndex;
            m_bucketStartB       = bucketB.bucketGlobalStart;
            m_bucketCountB       = bucketB.count;
            m_bucketIndexB       = bucketB.bucketIndex;
            m_jobIndex           = jobIndex;
            m_isAThreadSafe      = isAThreadSafe;
            m_isBThreadSafe      = isBThreadSafe;
            m_isImmediateContext = isImmediateContext;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckSafeEntityInRange(int index, int start, int count)
        {
            var clampedIndex = math.clamp(index, start, start + count);
            if (clampedIndex != index)
                throw new ArgumentOutOfRangeException($"Index {index} is outside the bucket range of [{start}, {start + count - 1}].");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckCanGenerateParallelPairKey()
        {
            if (m_isImmediateContext)
                throw new InvalidOperationException($"Cannot generate a ParallelWriteKey in a FindPairs.RunImmediate() context.");
        }
    }

    public static partial class Physics
    {
        /// <summary>
        /// Request a FindPairs broadphase operation to report pairs within the layer.
        /// This is the start of a fluent expression.
        /// </summary>
        /// <param name="layer">The layer in which pairs should be detected</param>
        /// <param name="processor">The job-like struct which should process each pair found</param>
        public static FindPairsLayerSelfConfig<T> FindPairs<T>(in CollisionLayer layer, in T processor) where T : struct, IFindPairsProcessor
        {
            return new FindPairsLayerSelfConfig<T>
            {
                processor                = processor,
                layer                    = layer,
                disableEntityAliasChecks = false
            };
        }

        /// <summary>
        /// Request a FindPairs broadphase operation to report pairs between the two layers.
        /// Only pairs containing one element from layerA and one element from layerB will be reported.
        /// This is the start of a fluent expression.
        /// </summary>
        /// <param name="layerA">The first layer in which pairs should be detected</param>
        /// <param name="layerB">The second layer in which pairs should be detected</param>
        /// <param name="processor">The job-like struct which should process each pair found</param>
        public static FindPairsLayerLayerConfig<T> FindPairs<T>(in CollisionLayer layerA, in CollisionLayer layerB, in T processor) where T : struct, IFindPairsProcessor
        {
            CheckLayersAreCompatible(layerA, layerB);
            return new FindPairsLayerLayerConfig<T>
            {
                processor                = processor,
                layerA                   = layerA,
                layerB                   = layerB,
                disableEntityAliasChecks = false
            };
        }

        /// <summary>
        /// Returns the number of job indices that would be used on a FindPairs operation with this layer.
        /// This can be used for allocating the correct size NativeStream and using the optional BeginBucket()
        /// and EndBucket() callbacks.
        /// </summary>
        /// <param name="layer">The layer that would be used in a FindPairs operation</param>
        /// <returns>A value defining the number of job indices that will be used by the FindPairs operation.
        /// Every jobIndex will be less than this value.</returns>
        public static int FindPairsJobIndexCount(in CollisionLayer layer) => IndexStrategies.JobIndicesFromSingleLayerFindPairs(layer.cellCount);
        /// <summary>
        /// Returns the number of job indices that would be used on a FindPairs operation with these layers.
        /// This can be used for allocating the correct size NativeStream and using the optional BeginBucket()
        /// and EndBucket() callbacks.
        /// </summary>
        /// <param name="layerA">The first layer that would be used in a FindPairs operation</param>
        /// <param name="layerB">The second layer that would be used in a FindPairs operation</param>
        /// <returns>A value defining the number of job indices that will be used by the FindPairs operation.
        /// Every jobIndex will be less than this value.</returns>
        public static int FindPairsJobIndexCount(in CollisionLayer layerA, in CollisionLayer layerB)
        {
            CheckLayersAreCompatible(in layerA, in layerB);
            return IndexStrategies.JobIndicesFromDualLayerFindPairs(layerA.cellCount);
        }

        internal static readonly Unity.Profiling.ProfilerMarker kCellMarker  = new Unity.Profiling.ProfilerMarker("Cell");
        internal static readonly Unity.Profiling.ProfilerMarker kCrossMarker = new Unity.Profiling.ProfilerMarker("Cross");

        #region SafetyChecks
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckLayersAreCompatible(in CollisionLayer layerA, in CollisionLayer layerB)
        {
            if (math.any(layerA.worldMin != layerB.worldMin | layerA.worldAxisStride != layerB.worldAxisStride | layerA.worldSubdivisionsPerAxis !=
                         layerB.worldSubdivisionsPerAxis))
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                throw new InvalidOperationException(
                    "The two layers used in the FindPairs operation are not compatible. Please ensure the layers were constructed with identical settings.");
#endif
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void WarnEntityAliasingUnchecked()
        {
            UnityEngine.Debug.LogWarning("IgnoreEntityAliasing is unchecked for this schedule mode.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void WarnCrossCacheUnused()
        {
            UnityEngine.Debug.LogWarning("Cross-caching is unsupported for this schedule mode at this time. The setting is being ignored.");
        }
        #endregion
    }

    public partial struct FindPairsLayerSelfConfig<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layer;

        internal bool disableEntityAliasChecks;
        internal bool useCrossCache;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        public FindPairsLayerSelfConfig<T> WithoutEntityAliasingChecks()
        {
            disableEntityAliasChecks = true;
            return this;
        }
        #endregion

        #region Schedulers
        /// <summary>
        /// Run the FindPairs operation without using a job. This method can be invoked from inside a job.
        /// </summary>
        public void RunImmediate()
        {
            if (disableEntityAliasChecks)
            {
                Physics.WarnEntityAliasingUnchecked();
            }
            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            FindPairsInternal.RunImmediate(layer, ref processor, false);
        }

        /// <summary>
        /// Run the FindPairs operation on the main thread using a Bursted job.
        /// </summary>
        public void Run()
        {
            if (disableEntityAliasChecks)
            {
                Physics.WarnEntityAliasingUnchecked();
            }
            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            new FindPairsInternal.LayerSelfJob(in layer, in processor).Run();
        }

        /// <summary>
        /// Run the FindPairs operation on a single worker thread.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps = default)
        {
            if (disableEntityAliasChecks)
            {
                Physics.WarnEntityAliasingUnchecked();
            }
            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            return new FindPairsInternal.LayerSelfJob(in layer, in processor).ScheduleSingle(inputDeps);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// If the CollisionLayer only contains a single cell (all subdivisions == 1), this falls back to ScheduleSingle().
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(layer.cellCount))
                return ScheduleSingle(inputDeps);

            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            var scheduleMode = disableEntityAliasChecks ? ScheduleMode.ParallelPart1AllowEntityAliasing : ScheduleMode.ParallelPart1;
            var jh           = new FindPairsInternal.LayerSelfJob(in layer, in processor).ScheduleParallel(inputDeps, scheduleMode);
            scheduleMode     = disableEntityAliasChecks ? ScheduleMode.ParallelPart2AllowEntityAliasing : ScheduleMode.ParallelPart2;
            return new FindPairsInternal.LayerSelfJob(in layer, in processor).ScheduleParallel(jh, scheduleMode);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads all at once without entity or body index thread-safety.
        /// If the CollisionLayer only contains a single cell (all subdivisions == 1), this falls back to ScheduleSingle().
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelUnsafe(JobHandle inputDeps = default)
        {
            if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(layer.cellCount))
                return ScheduleSingle(inputDeps);

            if (disableEntityAliasChecks)
            {
                Physics.WarnEntityAliasingUnchecked();
            }
            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            return new FindPairsInternal.LayerSelfJob(in layer, in processor).ScheduleParallel(inputDeps, ScheduleMode.ParallelUnsafe);
        }
        #endregion Schedulers
    }

    public partial struct FindPairsLayerLayerConfig<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layerA;
        internal CollisionLayer layerB;

        internal bool disableEntityAliasChecks;
        internal bool useCrossCache;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        public FindPairsLayerLayerConfig<T> WithoutEntityAliasingChecks()
        {
            disableEntityAliasChecks = true;
            return this;
        }

        /// <summary>
        /// Enables a cross-cache to increase parallelism and reduce latency at the cost of some extra overhead for allocations and cached writing and reading.
        /// Currently, this is only supported when using ScheduleParallelByA().
        /// </summary>
        public FindPairsLayerLayerConfig<T> WithCrossCache()
        {
            useCrossCache = true;
            return this;
        }
        #endregion

        #region Schedulers
        /// <summary>
        /// Run the FindPairs operation without using a job. This method can be invoked from inside a job.
        /// </summary>
        public void RunImmediate()
        {
            if (disableEntityAliasChecks)
            {
                Physics.WarnEntityAliasingUnchecked();
            }
            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            FindPairsInternal.RunImmediate(in layerA, in layerB, ref processor, false);
        }

        /// <summary>
        /// Run the FindPairs operation on the main thread using a Bursted job.
        /// </summary>
        public void Run()
        {
            if (disableEntityAliasChecks)
            {
                Physics.WarnEntityAliasingUnchecked();
            }
            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            new FindPairsInternal.LayerLayerJob(in layerA, in layerB, in processor).Run();
        }

        /// <summary>
        /// Run the FindPairs operation on a single worker thread.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps = default)
        {
            if (disableEntityAliasChecks)
            {
                Physics.WarnEntityAliasingUnchecked();
            }
            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            return new FindPairsInternal.LayerLayerJob(in layerA, in layerB, in processor).ScheduleSingle(inputDeps);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// If the CollisionLayers only contains a single cell each (all subdivisions == 1), this falls back to ScheduleSingle().
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(layerA.cellCount))
                return ScheduleSingle(inputDeps);

            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            var scheduleMode = ScheduleMode.ParallelPart1;
            if (disableEntityAliasChecks)
                scheduleMode |= ScheduleMode.AllowEntityAliasing;
            var jh            = new FindPairsInternal.LayerLayerJob(in layerA, in layerB, in processor).ScheduleParallel(inputDeps, scheduleMode);
            scheduleMode      = ScheduleMode.ParallelPart2;
            if (disableEntityAliasChecks)
                scheduleMode |= ScheduleMode.AllowEntityAliasing;
            return new FindPairsInternal.LayerLayerJob(in layerA, in layerB, in processor).ScheduleParallel(jh, scheduleMode);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in a single phase, without safe write access to the second layer.
        /// If the CollisionLayers only contains a single cell each (all subdivisions == 1), this falls back to ScheduleSingle().
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelByA(JobHandle inputDeps = default)
        {
            if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(layerA.cellCount))
                return ScheduleSingle(inputDeps);

            var scheduleMode = ScheduleMode.ParallelByA;
            if (disableEntityAliasChecks)
                scheduleMode |= ScheduleMode.AllowEntityAliasing;
            if (useCrossCache)
                scheduleMode |= ScheduleMode.UseCrossCache;
            return new FindPairsInternal.LayerLayerJob(in layerA, in layerB, in processor).ScheduleParallel(inputDeps, scheduleMode);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads all at once without entity or body index thread-safety.
        /// If the CollisionLayers only contains a single cell each (all subdivisions == 1), this falls back to ScheduleSingle().
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelUnsafe(JobHandle inputDeps = default)
        {
            if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(layerA.cellCount))
                return ScheduleSingle(inputDeps);

            if (disableEntityAliasChecks)
            {
                Physics.WarnEntityAliasingUnchecked();
            }
            if (useCrossCache)
            {
                Physics.WarnCrossCacheUnused();
            }
            return new FindPairsInternal.LayerLayerJob(in layerA, in layerB, in processor).ScheduleParallel(inputDeps, ScheduleMode.ParallelUnsafe);
        }
        #endregion Schedulers
    }
}

