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
    public interface IFindObjectsProcessor
    {
        void Execute(in FindObjectsResult result);
    }

    [NativeContainer]
    public struct FindObjectsResult
    {
        public CollisionLayer layer => m_layer;
        public ColliderBody body => m_bucket.bodies[m_bodyIndexRelative];
        public Collider collider => body.collider;
        public RigidTransform transform => body.transform;
        public int bodyIndex => m_bodyIndexRelative + m_bucket.count;
        public int jobIndex => m_jobIndex;

        private CollisionLayer m_layer;
        private BucketSlices   m_bucket;
        private int            m_bodyIndexRelative;
        private int            m_jobIndex;
        private bool           m_isThreadSafe;

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

        internal FindObjectsResult(CollisionLayer layer, BucketSlices bucket, int jobIndex, bool isThreadSafe)
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
        public static FindObjectsConfig<T> FindObjects<T>(Aabb aabb, in CollisionLayer layer, in T processor) where T : struct, IFindObjectsProcessor
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

        public void RunImmediate()
        {
            FindObjectsInternal.RunImmediate(aabb, layer, processor);
        }

        public void Run()
        {
            new FindObjectsInternal.Single
            {
                layer     = layer,
                processor = processor,
                aabb      = aabb
            }.Run();
        }

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

