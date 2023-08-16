using System;
using System.Diagnostics;
using Latios.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

//Todo: FilteredCache playback and inflations
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
        void BeginBucket(in FindPairsBucketContext context)
        {
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
        /// An index that is guaranteed to be deterministic and unique between threads for a given FindPairs operation,
        /// and can be used as the sortKey for command buffers
        /// </summary>
        public int jobIndex => m_jobIndex;

        private CollisionLayer m_layerA;
        private CollisionLayer m_layerB;
        private int            m_bucketStartA;
        private int            m_bucketStartB;
        private int            m_bodyAIndex;
        private int            m_bodyBIndex;
        private int            m_jobIndex;
        private bool           m_isThreadSafe;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        /// <summary>
        /// A safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
        /// owning entity of the first collider in the pair. It can also be implicitly casted and used as a normal entity reference.
        /// </summary>
        public SafeEntity entityA => new SafeEntity
        {
            entity = new Entity
            {
                Index   = math.select(-bodyA.entity.Index - 1, bodyA.entity.Index, m_isThreadSafe),
                Version = bodyA.entity.Version
            }
        };
        /// <summary>
        /// A safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
        /// owning entity of the second collider in the pair. It can also be implicitly casted and used as a normal entity reference.
        /// </summary>
        public SafeEntity entityB => new SafeEntity
        {
            entity = new Entity
            {
                Index   = math.select(-bodyB.entity.Index - 1, bodyB.entity.Index, m_isThreadSafe),
                Version = bodyB.entity.Version
            }
        };
#else
        public SafeEntity entityA => new SafeEntity { entity = bodyA.entity };
        public SafeEntity entityB => new SafeEntity { entity = bodyB.entity };
#endif

        /// <summary>
        /// The Aabb of the first collider in the pair
        /// </summary>
        public Aabb aabbA
        {
            get {
                var yzminmax = m_layerA.yzminmaxs[m_bodyAIndex];
                var xmin     = m_layerA.xmins[    m_bodyAIndex];
                var xmax     = m_layerA.xmaxs[    m_bodyAIndex];
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
                var xmin     = m_layerB.xmins[    m_bodyBIndex];
                var xmax     = m_layerB.xmaxs[    m_bodyBIndex];
                return new Aabb(new float3(xmin, yzminmax.xy), new float3(xmax, -yzminmax.zw));
            }
        }

        internal FindPairsResult(in CollisionLayer layerA, in CollisionLayer layerB, in BucketSlices bucketA, in BucketSlices bucketB, int jobIndex, bool isThreadSafe)
        {
            m_layerA       = layerA;
            m_layerB       = layerB;
            m_bucketStartA = bucketA.bucketGlobalStart;
            m_bucketStartB = bucketB.bucketGlobalStart;
            m_jobIndex     = jobIndex;
            m_isThreadSafe = isThreadSafe;
            m_bodyAIndex   = 0;
            m_bodyBIndex   = 0;
        }

        internal static FindPairsResult CreateGlobalResult(in CollisionLayer layerA, in CollisionLayer layerB, int jobIndex, bool isThreadSafe)
        {
            return new FindPairsResult
            {
                m_layerA       = layerA,
                m_layerB       = layerB,
                m_bucketStartA = 0,
                m_bucketStartB = 0,
                m_jobIndex     = jobIndex,
                m_isThreadSafe = isThreadSafe,
                m_bodyAIndex   = 0,
                m_bodyBIndex   = 0,
            };
        }

        internal void SetBucketRelativePairIndices(int aIndex, int bIndex)
        {
            m_bodyAIndex = aIndex + m_bucketStartA;
            m_bodyBIndex = bIndex + m_bucketStartB;
        }
        //Todo: Shorthands for calling narrow phase distance and manifold queries
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
        /// An index that is guaranteed to be deterministic and unique between threads for a given FindPairs operation,
        /// and can be used as the sortKey for command buffers or as an index in a NativeStream.
        /// </summary>
        public int jobIndex => m_jobIndex;

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
                entity = new Entity
                {
                    Index   = math.select(-entity.Index - 1, entity.Index, m_isThreadSafe),
                    Version = entity.Version
                }
            };
#else
            return new SafeEntity {
                entity = layerA.bodies[aIndex].entity
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
                entity = new Entity
                {
                    Index   = math.select(-entity.Index - 1, entity.Index, m_isThreadSafe),
                    Version = entity.Version
                }
            };
#else
            return new SafeEntity {
                entity = layerB.bodies[bIndex].entity
            };
#endif
        }

        private CollisionLayer m_layerA;
        private CollisionLayer m_layerB;
        private int            m_bucketStartA;
        private int            m_bucketStartB;
        private int            m_bucketCountA;
        private int            m_bucketCountB;
        private int            m_jobIndex;
        private bool           m_isThreadSafe;

        internal FindPairsBucketContext(in CollisionLayer layerA, in CollisionLayer layerB, int startA, int countA, int startB, int countB, int jobIndex, bool isThreadSafe)
        {
            m_layerA       = layerA;
            m_layerB       = layerB;
            m_bucketStartA = startA;
            m_bucketCountA = countA;
            m_bucketStartB = startB;
            m_bucketCountB = countB;
            m_jobIndex     = jobIndex;
            m_isThreadSafe = isThreadSafe;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckSafeEntityInRange(int index, int start, int count)
        {
            var clampedIndex = math.clamp(index, start, start + count);
            if (clampedIndex != index)
                throw new ArgumentOutOfRangeException($"Index {index} is outside the bucket range of [{start}, {start + count - 1}].");
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
        public static int FindPairsJobIndexCount(in CollisionLayer layer) => 2 * layer.bucketCount - 1;
        /// <summary>
        /// Returns the number of job indices that would be used on a FindPairs operation with these layers.
        /// This can be used for allocating the correct size NativeStream and using the optional BeginBucket()
        /// and EndBucket() callbacks.
        /// </summary>
        /// <param name="layerA">The first layer that would be used in a FindPairs operation</param>
        /// <param name="layerB">The second layer that would be used in a FindPairs operation</param>
        /// <returns>A value defining the number of job indices that will be used by the FindPairs operation.
        /// Every jobIndex will be less than this value.</returns>
        public static int FindPairsJobIndexCount(in CollisionLayer layerA, in CollisionLayer layerB) => 3 * layerA.bucketCount - 2;

        /// <summary>
        /// Request a FindPairs broadphase operation to report pairs within the layer.
        /// This is the start of a fluent expression.
        /// </summary>
        /// <param name="layer">The layer in which pairs should be detected</param>
        /// <param name="processor">The job-like struct which should process each pair found</param>
        internal static FindPairsLayerSelfConfigUnrolled<T> FindPairsUnrolled<T>(in CollisionLayer layer, in T processor) where T : struct, IFindPairsProcessor
        {
            return new FindPairsLayerSelfConfigUnrolled<T>
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
        internal static FindPairsLayerLayerConfigUnrolled<T> FindPairsUnrolled<T>(in CollisionLayer layerA, in CollisionLayer layerB, in T processor) where T : struct,
        IFindPairsProcessor
        {
            CheckLayersAreCompatible(layerA, layerB);
            return new FindPairsLayerLayerConfigUnrolled<T>
            {
                processor                = processor,
                layerA                   = layerA,
                layerB                   = layerB,
                disableEntityAliasChecks = false
            };
        }

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
        #endregion
    }

    public partial struct FindPairsLayerSelfConfig<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layer;

        internal bool disableEntityAliasChecks;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        public FindPairsLayerSelfConfig<T> WithoutEntityAliasingChecks()
        {
            disableEntityAliasChecks = true;
            return this;
        }

        /// <summary>
        /// Enables usage of a cache for pairs involving the cross bucket.
        /// This increases processing time and memory usage, but may decrease latency.
        /// This may only be used with ScheduleParallel().
        /// </summary>
        /// <param name="cacheAllocator">The type of allocator to use for the cache</param>
        public FindPairsLayerSelfWithCrossCacheConfig<T> WithCrossCache(Allocator cacheAllocator = Allocator.TempJob)
        {
            return new FindPairsLayerSelfWithCrossCacheConfig<T>
            {
                layer                    = layer,
                disableEntityAliasChecks = disableEntityAliasChecks,
                processor                = processor,
                allocator                = cacheAllocator
            };
        }
        #endregion

        #region Schedulers
        /// <summary>
        /// Run the FindPairs operation without using a job. This method can be invoked from inside a job.
        /// </summary>
        public void RunImmediate()
        {
            FindPairsInternal.RunImmediate(layer, ref processor, false);
        }

        /// <summary>
        /// Run the FindPairs operation on the main thread using a Bursted job.
        /// </summary>
        public void Run()
        {
            new FindPairsInternal.LayerSelfSingle
            {
                layer     = layer,
                processor = processor
            }.Run();
        }

        /// <summary>
        /// Run the FindPairs operation on a single worker thread.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps = default)
        {
            return new FindPairsInternal.LayerSelfSingle
            {
                layer     = layer,
                processor = processor
            }.Schedule(inputDeps);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            JobHandle jh = new FindPairsInternal.LayerSelfPart1
            {
                layer     = layer,
                processor = processor
            }.Schedule(layer.bucketCount, 1, inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (disableEntityAliasChecks)
            {
                jh = new FindPairsInternal.LayerSelfPart2
                {
                    layer     = layer,
                    processor = processor
                }.Schedule(jh);
            }
            else
            {
                jh = new FindPairsInternal.LayerSelfPart2_WithSafety
                {
                    layer     = layer,
                    processor = processor
                }.ScheduleParallel(2, 1, jh);
            }
#else
            jh = new FindPairsInternal.LayerSelfPart2
            {
                layer     = layer,
                processor = processor
            }.Schedule(jh);
#endif
            return jh;
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads all at once without entity or body index thread-safety.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelUnsafe(JobHandle inputDeps = default)
        {
            return new FindPairsInternal.LayerSelfParallelUnsafe
            {
                layer     = layer,
                processor = processor
            }.ScheduleParallel(2 * layer.bucketCount - 1, 1, inputDeps);
        }
        #endregion Schedulers
    }

    public partial struct FindPairsLayerSelfWithCrossCacheConfig<T>
    {
        internal T processor;

        internal CollisionLayer layer;

        internal bool disableEntityAliasChecks;

        internal Allocator allocator;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        public FindPairsLayerSelfWithCrossCacheConfig<T> WithoutEntityAliasingChecks()
        {
            disableEntityAliasChecks = true;
            return this;
        }
        #endregion

        #region Schedulers
        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            var       cache = new NativeStream(layer.bucketCount - 1, allocator);
            JobHandle jh    = new FindPairsInternal.LayerSelfPart1
            {
                layer     = layer,
                processor = processor,
                cache     = cache.AsWriter()
            }.Schedule(2 * layer.bucketCount - 1, 1, inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (disableEntityAliasChecks)
            {
                jh = new FindPairsInternal.LayerSelfPart2
                {
                    layer     = layer,
                    processor = processor,
                    cache     = cache.AsReader()
                }.Schedule(jh);
            }
            else
            {
                jh = new FindPairsInternal.LayerSelfPart2_WithSafety
                {
                    layer     = layer,
                    processor = processor,
                    cache     = cache.AsReader()
                }.ScheduleParallel(2, 1, jh);
            }
#else
            jh = new FindPairsInternal.LayerSelfPart2
            {
                layer     = layer,
                processor = processor,
                cache     = cache.AsReader()
            }.Schedule(jh);
#endif
            jh = cache.Dispose(jh);
            return jh;
        }
        #endregion
    }

    public partial struct FindPairsLayerLayerConfig<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layerA;
        internal CollisionLayer layerB;

        internal bool disableEntityAliasChecks;

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
        /// Enables usage of a cache for pairs involving the cross bucket.
        /// This increases processing time and memory usage, but may decrease latency.
        /// This may only be used with ScheduleParallel().
        /// </summary>
        /// <param name="cacheAllocator">The type of allocator to use for the cache</param>
        public FindPairsLayerLayerWithCrossCacheConfig<T> WithCrossCache(Allocator cacheAllocator = Allocator.TempJob)
        {
            return new FindPairsLayerLayerWithCrossCacheConfig<T>
            {
                layerA                   = layerA,
                layerB                   = layerB,
                disableEntityAliasChecks = disableEntityAliasChecks,
                processor                = processor,
                allocator                = cacheAllocator
            };
        }
        #endregion

        #region Schedulers
        /// <summary>
        /// Run the FindPairs operation without using a job. This method can be invoked from inside a job.
        /// </summary>
        public void RunImmediate()
        {
            FindPairsInternal.RunImmediate(in layerA, in layerB, ref processor, false);
        }

        /// <summary>
        /// Run the FindPairs operation on the main thread using a Bursted job.
        /// </summary>
        public void Run()
        {
            new FindPairsInternal.LayerLayerSingle
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.Run();
        }

        /// <summary>
        /// Run the FindPairs operation on a single worker thread.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps = default)
        {
            return new FindPairsInternal.LayerLayerSingle
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.Schedule(inputDeps);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            JobHandle jh = new FindPairsInternal.LayerLayerPart1
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.Schedule(layerB.bucketCount, 1, inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (disableEntityAliasChecks)
            {
                jh = new FindPairsInternal.LayerLayerPart2
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor
                }.Schedule(2, 1, jh);
            }
            else
            {
                jh = new FindPairsInternal.LayerLayerPart2_WithSafety
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor
                }.Schedule(3, 1, jh);
            }
#else
            jh = new FindPairsInternal.LayerLayerPart2
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.Schedule(2, 1, jh);
#endif
            return jh;
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads all at once without entity or body index thread-safety.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelUnsafe(JobHandle inputDeps = default)
        {
            return new FindPairsInternal.LayerLayerParallelUnsafe
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.ScheduleParallel(3 * layerA.bucketCount - 2, 1, inputDeps);
        }
        #endregion Schedulers
    }

    public partial struct FindPairsLayerLayerWithCrossCacheConfig<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layerA;
        internal CollisionLayer layerB;

        internal bool disableEntityAliasChecks;

        internal Allocator allocator;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        public FindPairsLayerLayerWithCrossCacheConfig<T> WithoutEntityAliasingChecks()
        {
            disableEntityAliasChecks = true;
            return this;
        }
        #endregion

        #region Schedulers
        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            var cache = new NativeStream(layerA.bucketCount * 2 - 2, allocator);

            JobHandle jh = new FindPairsInternal.LayerLayerPart1
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor,
                cache     = cache.AsWriter()
            }.Schedule(3 * layerB.bucketCount - 2, 1, inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (disableEntityAliasChecks)
            {
                jh = new FindPairsInternal.LayerLayerPart2
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor,
                    cache     = cache.AsReader()
                }.Schedule(2, 1, jh);
            }
            else
            {
                jh = new FindPairsInternal.LayerLayerPart2_WithSafety
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor,
                    cache     = cache.AsReader()
                }.Schedule(3, 1, jh);
            }
#else
            jh = new FindPairsInternal.LayerLayerPart2
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor,
                cache     = cache.AsReader()
            }.Schedule(2, 1, jh);
#endif
            jh = cache.Dispose(jh);
            return jh;
        }
        #endregion
    }

    internal partial struct FindPairsLayerSelfConfigUnrolled<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layer;

        internal bool disableEntityAliasChecks;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        public FindPairsLayerSelfConfigUnrolled<T> WithoutEntityAliasingChecks()
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
            FindPairsInternalUnrolled.RunImmediate(layer, processor);
        }

        /// <summary>
        /// Run the FindPairs operation on the main thread using a Bursted job.
        /// </summary>
        public void Run()
        {
            new FindPairsInternalUnrolled.LayerSelfSingle
            {
                layer     = layer,
                processor = processor
            }.Run();
        }

        /// <summary>
        /// Run the FindPairs operation on a single worker thread.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps = default)
        {
            return new FindPairsInternalUnrolled.LayerSelfSingle
            {
                layer     = layer,
                processor = processor
            }.Schedule(inputDeps);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            JobHandle jh = new FindPairsInternalUnrolled.LayerSelfPart1
            {
                layer     = layer,
                processor = processor
            }.Schedule(layer.bucketCount, 1, inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (disableEntityAliasChecks)
            {
                jh = new FindPairsInternalUnrolled.LayerSelfPart2
                {
                    layer     = layer,
                    processor = processor
                }.Schedule(jh);
            }
            else
            {
                jh = new FindPairsInternalUnrolled.LayerSelfPart2_WithSafety
                {
                    layer     = layer,
                    processor = processor
                }.ScheduleParallel(2, 1, jh);
            }
#else
            jh = new FindPairsInternalUnrolled.LayerSelfPart2
            {
                layer     = layer,
                processor = processor
            }.Schedule(jh);
#endif
            return jh;
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads all at once without entity or body index thread-safety.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelUnsafe(JobHandle inputDeps = default)
        {
            return new FindPairsInternalUnrolled.LayerSelfParallelUnsafe
            {
                layer     = layer,
                processor = processor
            }.ScheduleParallel(2 * layer.bucketCount - 1, 1, inputDeps);
        }
        #endregion Schedulers
    }

    internal partial struct FindPairsLayerLayerConfigUnrolled<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layerA;
        internal CollisionLayer layerB;

        internal bool disableEntityAliasChecks;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        public FindPairsLayerLayerConfigUnrolled<T> WithoutEntityAliasingChecks()
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
            FindPairsInternalUnrolled.RunImmediate(layerA, layerB, processor);
        }

        /// <summary>
        /// Run the FindPairs operation on the main thread using a Bursted job.
        /// </summary>
        public void Run()
        {
            new FindPairsInternalUnrolled.LayerLayerSingle
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.Run();
        }

        /// <summary>
        /// Run the FindPairs operation on a single worker thread.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps = default)
        {
            return new FindPairsInternalUnrolled.LayerLayerSingle
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.Schedule(inputDeps);
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            JobHandle jh = new FindPairsInternalUnrolled.LayerLayerPart1
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.Schedule(layerB.bucketCount, 1, inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (disableEntityAliasChecks)
            {
                jh = new FindPairsInternalUnrolled.LayerLayerPart2
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor
                }.Schedule(2, 1, jh);
            }
            else
            {
                jh = new FindPairsInternalUnrolled.LayerLayerPart2_WithSafety
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor
                }.Schedule(3, 1, jh);
            }
#else
            jh = new FindPairsInternalUnrolled.LayerLayerPart2
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.Schedule(2, 1, jh);
#endif
            return jh;
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads all at once without entity or body index thread-safety.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelUnsafe(JobHandle inputDeps = default)
        {
            return new FindPairsInternalUnrolled.LayerLayerParallelUnsafe
            {
                layerA    = layerA,
                layerB    = layerB,
                processor = processor
            }.ScheduleParallel(3 * layerA.bucketCount - 2, 1, inputDeps);
        }
        #endregion Schedulers
    }
}

