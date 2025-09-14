using Latios.Calci;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    [BurstCompile]
    internal static class WorldQuerySweepMethods
    {
        public static void AabbSweep<T>(in Aabb aabb, in CollisionWorld world, in CollisionWorld.Mask archetypeIndices, ref T processor) where T : struct, IFindObjectsProcessor
        {
            if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
                return;

            int3 minBucket = math.int3(math.floor((aabb.min - world.layer.worldMin) / world.layer.worldAxisStride));
            int3 maxBucket = math.int3(math.floor((aabb.max - world.layer.worldMin) / world.layer.worldAxisStride));
            minBucket      = math.clamp(minBucket, 0, world.layer.worldSubdivisionsPerAxis - 1);
            maxBucket      = math.clamp(maxBucket, 0, world.layer.worldSubdivisionsPerAxis - 1);

            for (int i = minBucket.x; i <= maxBucket.x; i++)
            {
                for (int j = minBucket.y; j <= maxBucket.y; j++)
                {
                    for (int k = minBucket.z; k <= maxBucket.z; k++)
                    {
                        var bucketIndex = IndexStrategies.CellIndexFromSubdivisionIndices(new int3(i, j, k), world.layer.worldSubdivisionsPerAxis);
                        AabbSweepBucket(in aabb, in world, world.GetBucket(bucketIndex), in archetypeIndices, ref processor);
                    }
                }
            }

            var crossBucketIndex = IndexStrategies.CrossBucketIndex(world.layer.cellCount);
            AabbSweepBucket(in aabb, in world, world.GetBucket(crossBucketIndex), archetypeIndices, ref processor);
        }

        private static void AabbSweepBucket<T>(in Aabb aabb, in CollisionWorld world, in WorldBucket bucket, in CollisionWorld.Mask archetypeIndices,
                                               ref T processor) where T : struct, IFindObjectsProcessor
        {
            if (bucket.slices.count == 0)
                return;

            var context = new AabbSweepRecursiveContext(new FindObjectsResult(world.layer, bucket.slices, 0, false, 0),
                                                        bucket.slices,
                                                        new float4(aabb.max.yz, -aabb.min.yz),
                                                        aabb.min.x
                                                        );

            var qxmax = aabb.max.x;

            var linearSweepStartIndex = BinarySearch.FirstGreaterOrEqual(in bucket.slices.xmins, context.qxmin);
            foreach (var archetypeIndex in archetypeIndices)
            {
                var asac = bucket.archetypeStartsAndCounts[archetypeIndex];
                if (asac.y == 0)
                    continue;

                var bodyIndices      = bucket.archetypeBodyIndices.GetSubArray(asac.x, asac.y);
                var sparseStartIndex = BinarySearch.FirstGreaterOrEqual(in bodyIndices, linearSweepStartIndex);

                for (int indexInSparse = sparseStartIndex; indexInSparse < bodyIndices.Length && bucket.slices.xmins[bodyIndices[indexInSparse]] <= qxmax; indexInSparse++)
                {
                    if (Hint.Unlikely(math.bitmask(context.qyzMinMax < bucket.slices.yzminmaxs[bodyIndices[indexInSparse]]) == 0))
                    {
                        context.result.SetBucketRelativeIndex(bodyIndices[indexInSparse]);
                        processor.Execute(in context.result);
                    }
                }

                context.tree = bucket.archetypeIntervalTrees.GetSubArray(asac.x, asac.y);
                SearchTreeLooped(ref context, ref processor);
            }
        }

        [BurstDiscard]
        static void SkipWithoutBurst(ref bool isBurst) => isBurst = false;

        private struct AabbSweepRecursiveContext
        {
            public FindObjectsResult             result;
            public readonly BucketSlices         bucket;
            public NativeArray<IntervalTreeNode> tree;
            public float4                        qyzMinMax;
            public float                         qxmin;

            public AabbSweepRecursiveContext(in FindObjectsResult result, in BucketSlices bucket, float4 qyzMinMax, float qxmin)
            {
                this.result    = result;
                this.bucket    = bucket;
                this.qyzMinMax = qyzMinMax;
                this.qxmin     = qxmin;
                tree           = default;
            }
        }

        internal static uint GetLeftChildIndex(uint currentIndex) => 2 * currentIndex + 1;
        internal static uint GetRightChildIndex(uint currentIndex) => 2 * currentIndex + 2;
        internal static uint GetParentIndex(uint currentIndex) => (currentIndex - 1) / 2;

        internal struct StackFrame
        {
            public uint currentIndex;
            public uint checkpoint;
        }

        [SkipLocalsInit]
        private static unsafe void SearchTreeLooped<T>(ref AabbSweepRecursiveContext context, ref T processor) where T : struct, IFindObjectsProcessor
        {
            uint        currentFrameIndex = 0;
            StackFrame* stack             = stackalloc StackFrame[32];
            stack[0]                      = new StackFrame { currentIndex = 0, checkpoint = 0 };

            while (currentFrameIndex < 32)
            {
                var currentFrame = stack[currentFrameIndex];
                if (currentFrame.checkpoint == 0)
                {
                    if (currentFrame.currentIndex >= context.bucket.count)
                    {
                        currentFrameIndex--;
                        continue;
                    }

                    var node = context.tree[(int)currentFrame.currentIndex];
                    if (context.qxmin >= node.subtreeXmax)
                    {
                        currentFrameIndex--;
                        continue;
                    }

                    currentFrame.checkpoint  = 1;
                    stack[currentFrameIndex] = currentFrame;
                    currentFrameIndex++;
                    stack[currentFrameIndex].currentIndex = GetLeftChildIndex(currentFrame.currentIndex);
                    stack[currentFrameIndex].checkpoint   = 0;
                    continue;
                }
                else if (currentFrame.checkpoint == 1)
                {
                    var node = context.tree[(int)currentFrame.currentIndex];
                    if (context.qxmin < node.xmin)
                    {
                        currentFrameIndex--;
                        continue;
                    }

                    if (context.qxmin > node.xmin && context.qxmin <= node.xmax)
                    {
                        if (Hint.Unlikely(math.bitmask(context.qyzMinMax < context.bucket.yzminmaxs[node.bucketRelativeBodyIndex]) == 0))
                        {
                            context.result.SetBucketRelativeIndex(node.bucketRelativeBodyIndex);
                            processor.Execute(in context.result);
                        }
                    }

                    currentFrame.checkpoint  = 2;
                    stack[currentFrameIndex] = currentFrame;
                    currentFrameIndex++;
                    stack[currentFrameIndex].currentIndex = GetRightChildIndex(currentFrame.currentIndex);
                    stack[currentFrameIndex].checkpoint   = 0;
                    continue;
                }
                else
                {
                    currentFrameIndex--;
                }
            }
        }
    }

    public unsafe partial struct FindObjectsWorldEnumerator
    {
        FindObjectsResult m_result;

        CollisionWorld      m_world;
        CollisionWorld.Mask m_mask;

        int m_layerIndex;

        int3 m_bucketIjk;
        int3 m_minBucket;
        int3 m_maxBucket;

        float4 m_qyzMinMax;
        float  m_qxmin;
        float  m_qxmax;

        BucketEnumerator m_bucketEnumerator;

        public FindObjectsWorldEnumerator(in Aabb aabb, in CollisionWorld collisionWorld, in CollisionWorld.Mask mask, int layerIndex = 0)
        {
            if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
            {
                this        = default;
                m_bucketIjk = 1;
            }
            else
            {
                m_world      = collisionWorld;
                m_mask       = mask;
                m_layerIndex = layerIndex;

                m_minBucket = math.int3(math.floor((aabb.min - m_world.layer.worldMin) / m_world.layer.worldAxisStride));
                m_maxBucket = math.int3(math.floor((aabb.max - m_world.layer.worldMin) / m_world.layer.worldAxisStride));
                m_minBucket = math.clamp(m_minBucket, 0, m_world.layer.worldSubdivisionsPerAxis - 1);
                m_maxBucket = math.clamp(m_maxBucket, 0, m_world.layer.worldSubdivisionsPerAxis - 1);
                m_bucketIjk = m_minBucket;

                m_qxmin     = aabb.min.x;
                m_qxmax     = aabb.max.x;
                m_qyzMinMax = new float4(aabb.max.yz, -aabb.min.yz);

                m_bucketEnumerator                         = default;
                var bucketIndex                            = IndexStrategies.CellIndexFromSubdivisionIndices(m_bucketIjk, m_world.layer.worldSubdivisionsPerAxis);
                m_bucketEnumerator.m_bucket                = m_world.GetBucket(bucketIndex);
                m_result                                   = new FindObjectsResult(in m_world.layer, in m_bucketEnumerator.m_bucket.slices, bucketIndex, false, m_layerIndex);
                m_bucketEnumerator.m_archetypeEnumerator   = m_mask;
                m_bucketEnumerator.m_linearSweepStartIndex = BinarySearch.FirstGreaterOrEqual(in m_bucketEnumerator.m_bucket.slices.xmins, m_qxmin);
                m_bucketEnumerator.isInside                = false;
            }
        }

        public bool MoveNext()
        {
            while (math.all(m_bucketIjk <= m_maxBucket))
            {
                if (m_bucketEnumerator.MoveNext(ref this))
                    return true;

                m_bucketIjk.z++;
                if (m_bucketIjk.z > m_maxBucket.z)
                {
                    m_bucketIjk.y++;
                    m_bucketIjk.z = m_minBucket.z;
                    if (m_bucketIjk.y > m_maxBucket.y)
                    {
                        m_bucketIjk.x++;
                        m_bucketIjk.y = m_minBucket.y;
                        if (m_bucketIjk.x > m_maxBucket.x)
                        {
                            // Set the target bucket to the cross bucket by adding one to the max bucket
                            m_bucketIjk = m_world.layer.worldSubdivisionsPerAxis - 1;
                            m_bucketIjk.z++;
                        }
                    }
                }

                var bucketIndex                            = IndexStrategies.CellIndexFromSubdivisionIndices(m_bucketIjk, m_world.layer.worldSubdivisionsPerAxis);
                m_bucketEnumerator.m_bucket                = m_world.GetBucket(bucketIndex);
                m_result                                   = new FindObjectsResult(in m_world.layer, in m_bucketEnumerator.m_bucket.slices, bucketIndex, false, m_layerIndex);
                m_bucketEnumerator.m_archetypeEnumerator   = m_mask;
                m_bucketEnumerator.m_linearSweepStartIndex = BinarySearch.FirstGreaterOrEqual(in m_bucketEnumerator.m_bucket.slices.xmins, m_qxmin);
                m_bucketEnumerator.isInside                = false;
            }

            return m_bucketEnumerator.MoveNext(ref this);
        }

        struct BucketEnumerator
        {
            public CollisionWorld.Mask m_archetypeEnumerator;
            public WorldBucket         m_bucket;
            public int                 m_linearSweepStartIndex;
            public bool                isInside;

            NativeArray<int> m_bodyIndices;
            int              m_indexInSparse;

            NativeArray<IntervalTreeNode> m_tree;
            uint                          m_currentFrameIndex;
            Stack                         m_stack;

            struct Stack
            {
                fixed uint m_stackData[64];

                public ref LayerQuerySweepMethods.StackFrame this[uint i] => ref UnsafeUtility.As<uint, LayerQuerySweepMethods.StackFrame>(ref m_stackData[2 * i]);
            }

            public bool MoveNext(ref FindObjectsWorldEnumerator outer)
            {
                while (true)
                {
                    if (!isInside)
                    {
                        if (m_archetypeEnumerator.MoveNext())
                        {
                            var asac = m_bucket.archetypeStartsAndCounts[m_archetypeEnumerator.Current];
                            if (asac.y == 0)
                                continue;
                            m_bodyIndices       = m_bucket.archetypeBodyIndices.GetSubArray(asac.x, asac.y);
                            m_tree              = m_bucket.archetypeIntervalTrees.GetSubArray(asac.x, asac.y);
                            m_indexInSparse     = BinarySearch.FirstGreaterOrEqual(in m_bodyIndices, m_linearSweepStartIndex);
                            m_currentFrameIndex = 0;
                            m_stack[0]          = new LayerQuerySweepMethods.StackFrame { currentIndex = 0, checkpoint = 0 };
                            isInside            = true;
                        }
                        else
                            return false;
                    }
                    else
                    {
                        while (m_indexInSparse < m_bodyIndices.Length && m_bucket.slices.xmins[m_bodyIndices[m_indexInSparse]] <= outer.m_qxmax)
                        {
                            outer.m_result.SetBucketRelativeIndex(m_bodyIndices[m_indexInSparse]);
                            bool found = math.bitmask(outer.m_qyzMinMax < m_bucket.slices.yzminmaxs[m_bodyIndices[m_indexInSparse]]) == 0;
                            m_indexInSparse++;
                            if (Hint.Unlikely(found))
                                return true;
                        }

                        while (m_currentFrameIndex < 32)
                        {
                            var currentFrame = m_stack[m_currentFrameIndex];
                            if (currentFrame.checkpoint == 0)
                            {
                                if (currentFrame.currentIndex >= m_bucket.slices.count)
                                {
                                    m_currentFrameIndex--;
                                    continue;
                                }

                                var node = m_tree[(int)currentFrame.currentIndex];
                                if (outer.m_qxmin >= node.subtreeXmax)
                                {
                                    m_currentFrameIndex--;
                                    continue;
                                }

                                currentFrame.checkpoint      = 1;
                                m_stack[m_currentFrameIndex] = currentFrame;
                                m_currentFrameIndex++;
                                m_stack[m_currentFrameIndex].currentIndex = LayerQuerySweepMethods.GetLeftChildIndex(currentFrame.currentIndex);
                                m_stack[m_currentFrameIndex].checkpoint   = 0;
                                continue;
                            }
                            else if (currentFrame.checkpoint == 1)
                            {
                                var node = m_tree[(int)currentFrame.currentIndex];
                                if (outer.m_qxmin < node.xmin)
                                {
                                    m_currentFrameIndex--;
                                    continue;
                                }

                                currentFrame.checkpoint      = 2;
                                m_stack[m_currentFrameIndex] = currentFrame;
                                m_currentFrameIndex++;
                                m_stack[m_currentFrameIndex].currentIndex = LayerQuerySweepMethods.GetRightChildIndex(currentFrame.currentIndex);
                                m_stack[m_currentFrameIndex].checkpoint   = 0;

                                if (outer.m_qxmin > node.xmin && outer.m_qxmin <= node.xmax)
                                {
                                    if (Hint.Unlikely(math.bitmask(outer.m_qyzMinMax < m_bucket.slices.yzminmaxs[node.bucketRelativeBodyIndex]) == 0))
                                    {
                                        outer.m_result.SetBucketRelativeIndex(node.bucketRelativeBodyIndex);
                                        return true;
                                    }
                                }
                                continue;
                            }
                            else
                            {
                                m_currentFrameIndex--;
                            }
                        }

                        isInside = false;
                    }
                }
            }
        }
    }
}

