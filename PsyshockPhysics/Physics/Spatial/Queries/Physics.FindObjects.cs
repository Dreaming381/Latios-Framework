using System;
using System.Diagnostics;
using Latios.Transforms;
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
    public interface IFindObjectsProcessor
    {
        void Execute(in FindObjectsResult result);
    }

    /// <summary>
    /// A result struct passed into the Execute method of a FindObjectsProcessor providing info about a collider
    /// whose Aabb overlapped the query Aabb.
    /// </summary>
    [NativeContainer]
    public struct FindObjectsResult
    {
        /// <summary>
        /// The CollisionLayer queried. Can be used for additional immediate queries directly inside the processor.
        /// </summary>
        public CollisionLayer layer => m_layer;
        /// <summary>
        /// The full ColliderBody of the collider
        /// </summary>
        public ColliderBody body => m_bucket.bodies[m_bodyIndexRelative];
        /// <summary>
        /// The collider
        /// </summary>
        public Collider collider => body.collider;
        /// <summary>
        /// The transform of the collider
        /// </summary>
        public TransformQvvs transform => body.transform;
        /// <summary>
        /// The index of the collider in the CollisionLayer
        /// </summary>
        public int bodyIndex => m_bodyIndexRelative + m_bucket.count;
        /// <summary>
        /// The index of the collider relative to the original EntityQuery or NativeArrays used to create the CollisionLayer
        /// </summary>
        public int sourceIndex => m_bucket.srcIndices[m_bodyIndexRelative];
        /// <summary>
        /// An index that is guaranteed to be deterministic and unique between threads for a given FindObjects operation,
        /// and can be used as the sortKey for command buffers
        /// </summary>
        public int jobIndex => m_jobIndex;

        private CollisionLayer m_layer;
        private BucketSlices   m_bucket;
        private int            m_bodyIndexRelative;
        private int            m_jobIndex;
        private bool           m_isThreadSafe;

        /// <summary>
        /// A safe entity handle that can be used inside of PhysicsComponentLookup or PhysicsBufferLookup and corresponds to the
        /// owning entity of the collider. It can also be implicitly casted and used as a normal entity reference.
        /// </summary>
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public SafeEntity entity => new SafeEntity
        {
            entity = new Entity
            {
                Index   = math.select(-body.entity.Index - 1, body.entity.Index, m_isThreadSafe),
                Version = body.entity.Version
            }
        };
#else
        public SafeEntity entity => new SafeEntity { entity = body.entity };
#endif

        /// <summary>
        /// The Aabb of the collider
        /// </summary>
        public Aabb aabb
        {
            get
            {
                var yzminmax = m_bucket.yzminmaxs[m_bodyIndexRelative];
                var xmin     = m_bucket.xmins[m_bodyIndexRelative];
                var xmax     = m_bucket.xmaxs[m_bodyIndexRelative];
                return new Aabb(new float3(xmin, yzminmax.xy), new float3(xmax, -yzminmax.zw));
            }
        }

        internal FindObjectsResult(in CollisionLayer layer, in BucketSlices bucket, int jobIndex, bool isThreadSafe)
        {
            m_layer             = layer;
            m_bucket            = bucket;
            m_jobIndex          = jobIndex;
            m_isThreadSafe      = isThreadSafe;
            m_bodyIndexRelative = 0;
        }

        internal void SetBucketRelativeIndex(int index)
        {
            m_bodyIndexRelative = index;
        }
        //Todo: Shorthands for calling narrow phase distance and manifold queries
    }

    public static partial class Physics
    {
        /// <summary>
        /// Searches the CollisionLayer for all colliders whose Aabbs overlap with the queried Aabb and runs processor.Execute()
        /// on each found result. This is the start of a fluent chain.
        /// </summary>
        /// <typeparam name="T">An IFindObjectsProcessor which may contain containers, data, and logic used to process each found result.</typeparam>
        /// <param name="aabb">The queried Aabb</param>
        /// <param name="layer">The CollisionLayer to find colliders whose Aabbs overlap the queried Aabb</param>
        /// <param name="processor">The processor with initialized containers and values to process each found result</param>
        /// <returns>A config object as part of a fluent chain</returns>
        public static FindObjectsConfig<T> FindObjects<T>(in Aabb aabb, in CollisionLayer layer, in T processor) where T : struct, IFindObjectsProcessor
        {
            return new FindObjectsConfig<T>
            {
                processor                = processor,
                layer                    = layer,
                aabb                     = aabb,
                disableEntityAliasChecks = false
            };
        }
    }

    /// <summary>
    /// A config object for a FindObjects operation which is a fluent API
    /// </summary>
    public partial struct FindObjectsConfig<T> where T : struct, IFindObjectsProcessor
    {
        internal T processor;

        internal CollisionLayer layer;

        internal Aabb aabb;

        internal bool disableEntityAliasChecks;

        #region Settings
        /// <summary>
        /// Disables entity aliasing checks on parallel jobs when safety checks are enabled. Use this only when entities can be aliased but body indices must be thread-safe.
        /// </summary>
        internal FindObjectsConfig<T> WithoutEntityAliasingChecks()  // Internal until a use case for parallel jobs is created
        {
            disableEntityAliasChecks = true;
            return this;
        }
        #endregion

        #region Schedulers
        /// <summary>
        /// Runs the operation immediately without creating a job struct. Use this inside a job or another FindObjects or FindPairs processor.
        /// </summary>
        public T RunImmediate()
        {
            return FindObjectsInternal.RunImmediate(in aabb, in layer, processor);
        }

        /// <summary>
        /// Runs the operation as a job on the main thread. Call this in a managed context to execute the search using Burst.
        /// </summary>
        public void Run()
        {
            new FindObjectsInternal.Single
            {
                layer     = layer,
                processor = processor,
                aabb      = aabb
            }.Run();
        }

        /// <summary>
        /// Schedules the search as a single-threaded job using Burst.
        /// </summary>
        /// <param name="inputDeps">A JobHandle to depend on</param>
        /// <returns>The JobHandle of the scheduled job</returns>
        public JobHandle ScheduleSingle(JobHandle inputDeps = default)
        {
            return new FindObjectsInternal.Single
            {
                layer     = layer,
                processor = processor,
                aabb      = aabb
            }.Schedule(inputDeps);
        }
        #endregion Schedulers
    }
}

