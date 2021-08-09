using System;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

//Todo: Stream types, caches, scratchlists, and inflations
namespace Latios.Psyshock
{
    /// <summary>
    /// An interface whose Execute method is invoked for each pair found in a FindPairs operations.
    /// </summary>
    public interface IFindPairsProcessor
    {
        void Execute(FindPairsResult result);
    }

    [NativeContainer]
    public struct FindPairsResult
    {
        public ColliderBody bodyA;
        public ColliderBody bodyB;
        public int          bodyAIndex;
        public int          bodyBIndex;
        public int          jobIndex;

        internal bool isThreadSafe;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public SafeEntity entityA => new SafeEntity
        {
            entity = new Entity
            {
                Index   = math.select(-bodyA.entity.Index, bodyA.entity.Index, isThreadSafe),
                Version = bodyA.entity.Version
            }
        };
        public SafeEntity entityB => new SafeEntity
        {
            entity = new Entity
            {
                Index   = math.select(-bodyB.entity.Index, bodyB.entity.Index, isThreadSafe),
                Version = bodyB.entity.Version
            }
        };
#else
        public SafeEntity entityA => new SafeEntity { entity = bodyA.entity };
        public SafeEntity entityB => new SafeEntity { entity = bodyB.entity };
#endif
        //Todo: Shorthands for calling narrow phase distance and manifold queries
    }

    public static partial class Physics
    {
        /// <summary>
        /// Request a FindPairs broadphase operation to report pairs within the layer. This is the start of a fluent expression.
        /// </summary>
        /// <param name="layer">The layer in which pairs should be detected</param>
        /// <param name="processor">The job-like struct which should process each pair found</param>
        public static FindPairsConfig<T> FindPairs<T>(CollisionLayer layer, T processor) where T : struct, IFindPairsProcessor
        {
            return new FindPairsConfig<T>
            {
                processor                = processor,
                layerA                   = layer,
                isLayerLayer             = false,
                disableEntityAliasChecks = false
            };
        }

        /// <summary>
        /// Request a FindPairs broadphase operation to report pairs between the two layers. Only pairs containing one element from layerA and one element from layerB will be reported. This is the start of a fluent expression.
        /// </summary>
        /// <param name="layerA">The first layer in which pairs should be detected</param>
        /// <param name="layerB">The second layer in which pairs should be detected</param>
        /// <param name="processor">The job-like struct which should process each pair found</param>
        public static FindPairsConfig<T> FindPairs<T>(CollisionLayer layerA, CollisionLayer layerB, T processor) where T : struct, IFindPairsProcessor
        {
            CheckLayersAreCompatible(layerA, layerB);
            return new FindPairsConfig<T>
            {
                processor                = processor,
                layerA                   = layerA,
                layerB                   = layerB,
                isLayerLayer             = true,
                disableEntityAliasChecks = false
            };
        }

        #region SafetyChecks
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckLayersAreCompatible(CollisionLayer layerA, CollisionLayer layerB)
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

    public partial struct FindPairsConfig<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layerA;
        internal CollisionLayer layerB;

        internal bool isLayerLayer;
        internal bool disableEntityAliasChecks;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        public FindPairsConfig<T> WithoutEntityAliasingChecks()
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
            if (isLayerLayer)
            {
                FindPairsInternal.RunImmediate(layerA, layerB, processor);
            }
            else
            {
                FindPairsInternal.RunImmediate(layerA, processor);
            }
        }

        /// <summary>
        /// Run the FindPairs operation on the main thread using a Bursted job.
        /// </summary>
        public void Run()
        {
            if (isLayerLayer)
            {
                new FindPairsInternal.LayerLayerSingle
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor
                }.Run();
            }
            else
            {
                new FindPairsInternal.LayerSelfSingle
                {
                    layer     = layerA,
                    processor = processor
                }.Run();
            }
        }

        /// <summary>
        /// Run the FindPairs operation on a single worker thread.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps = default)
        {
            if (isLayerLayer)
            {
                return new FindPairsInternal.LayerLayerSingle
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor
                }.Schedule(inputDeps);
            }
            else
            {
                return new FindPairsInternal.LayerSelfSingle
                {
                    layer     = layerA,
                    processor = processor
                }.Schedule(inputDeps);
            }
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads in multiple phases.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>The final JobHandle for the scheduled jobs</returns>
        public JobHandle ScheduleParallel(JobHandle inputDeps = default)
        {
            if (isLayerLayer)
            {
                JobHandle jh = new FindPairsInternal.LayerLayerPart1
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor
                }.Schedule(layerB.BucketCount, 1, inputDeps);
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
            else
            {
                JobHandle jh = new FindPairsInternal.LayerSelfPart1
                {
                    layer     = layerA,
                    processor = processor
                }.Schedule(layerA.BucketCount, 1, inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (disableEntityAliasChecks)
                {
                    jh = new FindPairsInternal.LayerSelfPart2
                    {
                        layer     = layerA,
                        processor = processor
                    }.Schedule(jh);
                }
                else
                {
                    jh = new FindPairsInternal.LayerSelfPart2_WithSafety
                    {
                        layer     = layerA,
                        processor = processor
                    }.ScheduleParallel(2, 1, jh);
                }
#else
                jh = new FindPairsInternal.LayerSelfPart2
                {
                    layer     = layerA,
                    processor = processor
                }.Schedule(jh);
#endif
                return jh;
            }
        }

        /// <summary>
        /// Run the FindPairs operation using multiple worker threads all at once without entity or body index thread-safety.
        /// </summary>
        /// <param name="inputDeps">The input dependencies for any layers or processors used in the FindPairs operation</param>
        /// <returns>A JobHandle for the scheduled job</returns>
        public JobHandle ScheduleParallelUnsafe(JobHandle inputDeps = default)
        {
            if (isLayerLayer)
            {
                return new FindPairsInternal.LayerLayerParallelUnsafe
                {
                    layerA    = layerA,
                    layerB    = layerB,
                    processor = processor
                }.ScheduleParallel(3 * layerA.BucketCount - 2, 1, inputDeps);
            }
            else
            {
                return new FindPairsInternal.LayerSelfParallelUnsafe
                {
                    layer     = layerA,
                    processor = processor
                }.ScheduleParallel(2 * layerA.BucketCount - 1, 1, inputDeps);
            }
        }
        #endregion Schedulers
    }
}

