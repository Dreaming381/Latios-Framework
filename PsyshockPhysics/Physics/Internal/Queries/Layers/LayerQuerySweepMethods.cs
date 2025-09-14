using Latios.Calci;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    [BurstCompile]
    internal static class LayerQuerySweepMethods
    {
        public static void AabbSweep<T>(in Aabb aabb, in CollisionLayer layer, ref T processor) where T : struct, IFindObjectsProcessor
        {
            if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
                return;

            int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
            int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
            minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
            maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

            for (int i = minBucket.x; i <= maxBucket.x; i++)
            {
                for (int j = minBucket.y; j <= maxBucket.y; j++)
                {
                    for (int k = minBucket.z; k <= maxBucket.z; k++)
                    {
                        var bucketIndex = IndexStrategies.CellIndexFromSubdivisionIndices(new int3(i, j, k), layer.worldSubdivisionsPerAxis);
                        AabbSweepBucket(in aabb, in layer, layer.GetBucketSlices(bucketIndex), ref processor);
                    }
                }
            }

            var crossBucketIndex = IndexStrategies.CrossBucketIndex(layer.cellCount);
            AabbSweepBucket(in aabb, in layer, layer.GetBucketSlices(crossBucketIndex), ref processor);
        }

        private static void AabbSweepBucket<T>(in Aabb aabb, in CollisionLayer layer, in BucketSlices bucket, ref T processor) where T : struct, IFindObjectsProcessor
        {
            if (bucket.count == 0)
                return;

            var context = new AabbSweepRecursiveContext(new FindObjectsResult(layer, bucket, 0, false, 0),
                                                        bucket,
                                                        new float4(aabb.max.yz, -aabb.min.yz),
                                                        aabb.min.x
                                                        );

            var qxmax = aabb.max.x;

            var linearSweepStartIndex = BinarySearch.FirstGreaterOrEqual(in bucket.xmins, context.qxmin);

            for (int indexInBucket = linearSweepStartIndex; indexInBucket < bucket.count && bucket.xmins[indexInBucket] <= qxmax; indexInBucket++)
            {
                if (Hint.Unlikely(math.bitmask(context.qyzMinMax < bucket.yzminmaxs[indexInBucket]) == 0))
                {
                    context.result.SetBucketRelativeIndex(indexInBucket);
                    processor.Execute(in context.result);
                }
            }

            SearchTreeLooped(ref context, ref processor);
        }

        private struct AabbSweepRecursiveContext
        {
            public FindObjectsResult     result;
            public readonly BucketSlices bucket;
            public float4                qyzMinMax;
            public float                 qxmin;

            public AabbSweepRecursiveContext(in FindObjectsResult result, in BucketSlices bucket, float4 qyzMinMax, float qxmin)
            {
                this.result    = result;
                this.bucket    = bucket;
                this.qyzMinMax = qyzMinMax;
                this.qxmin     = qxmin;
            }
        }

        private static void SearchTree<T>(ref AabbSweepRecursiveContext context, ref T processor) where T : struct, IFindObjectsProcessor => SearchTree(0,
                                                                                                                                                        ref context,
                                                                                                                                                        ref processor);
        private static void SearchTree<T>(uint currentIndex, ref AabbSweepRecursiveContext context, ref T processor) where T : struct, IFindObjectsProcessor
        {
            if (currentIndex >= context.bucket.count)
                return;

            var node = context.bucket.intervalTree[(int)currentIndex];
            if (context.qxmin >= node.subtreeXmax)
                return;

            SearchTree(GetLeftChildIndex(currentIndex), ref context, ref processor);

            if (context.qxmin < node.xmin)
                return;

            if (context.qxmin > node.xmin && context.qxmin <= node.xmax)
            {
                if (Hint.Unlikely(math.bitmask(context.qyzMinMax < context.bucket.yzminmaxs[node.bucketRelativeBodyIndex]) == 0))
                {
                    context.result.SetBucketRelativeIndex(node.bucketRelativeBodyIndex);
                    processor.Execute(in context.result);
                }
            }

            SearchTree(GetRightChildIndex(currentIndex), ref context, ref processor);
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

                    var node = context.bucket.intervalTree[(int)currentFrame.currentIndex];
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
                    var node = context.bucket.intervalTree[(int)currentFrame.currentIndex];
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

    public unsafe partial struct FindObjectsEnumerator
    {
        FindObjectsResult m_result;

        int m_layerIndex;

        int3         m_bucketIjk;
        int3         m_minBucket;
        int3         m_maxBucket;
        BucketSlices m_bucket;

        float4 m_qyzMinMax;
        float  m_qxmin;
        float  m_qxmax;

        int   m_indexInBucket;
        uint  m_currentFrameIndex;
        Stack m_stack;

        struct Stack
        {
            fixed uint m_stackData[64];

            public ref LayerQuerySweepMethods.StackFrame this[uint i] => ref UnsafeUtility.As<uint, LayerQuerySweepMethods.StackFrame>(ref m_stackData[2 * i]);
        }

        public FindObjectsEnumerator(in Aabb aabb, in CollisionLayer layer, int layerIndex = 0)
        {
            if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
            {
                m_maxBucket         = 0;
                m_minBucket         = 0;
                m_bucketIjk         = 1;
                m_result            = default;
                m_bucket            = layer.GetBucketSlices(0);
                m_indexInBucket     = m_bucket.count;
                m_qyzMinMax         = default;
                m_qxmin             = default;
                m_qxmax             = default;
                m_currentFrameIndex = 33;
                m_layerIndex        = layerIndex;
            }
            else
            {
                m_minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                m_maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                m_minBucket = math.clamp(m_minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                m_maxBucket = math.clamp(m_maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                m_bucketIjk = m_minBucket;
                m_bucket    = layer.GetBucketSlices(IndexStrategies.CellIndexFromSubdivisionIndices(m_bucketIjk, layer.worldSubdivisionsPerAxis));
                m_result    = new FindObjectsResult(in layer, in m_bucket, 0, false, layerIndex);

                m_layerIndex = layerIndex;

                m_qxmin     = aabb.min.x;
                m_qxmax     = aabb.max.x;
                m_qyzMinMax = new float4(aabb.max.yz, -aabb.min.yz);

                m_indexInBucket     = BinarySearch.FirstGreaterOrEqual(in m_bucket.xmins, m_qxmin);
                m_currentFrameIndex = 0;
                m_stack[0]          = new LayerQuerySweepMethods.StackFrame { currentIndex = 0, checkpoint = 0 };
            }
        }

        public bool MoveNext()
        {
            while (math.all(m_bucketIjk <= m_maxBucket))
            {
                if (StepBucket())
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
                            m_bucketIjk = m_result.layer.worldSubdivisionsPerAxis - 1;
                            m_bucketIjk.z++;
                        }
                    }
                }

                var bucketIndex = IndexStrategies.CellIndexFromSubdivisionIndices(m_bucketIjk, m_result.layer.worldSubdivisionsPerAxis);
                m_bucket        = m_result.layer.GetBucketSlices(bucketIndex);
                m_result        = new FindObjectsResult(in m_result.layer, in m_bucket, bucketIndex, false, m_layerIndex);

                m_indexInBucket     = BinarySearch.FirstGreaterOrEqual(in m_bucket.xmins, m_qxmin);
                m_currentFrameIndex = 0;
                m_stack[0]          = new LayerQuerySweepMethods.StackFrame { currentIndex = 0, checkpoint = 0 };
            }

            return StepBucket();
        }

        bool StepBucket()
        {
            while (m_indexInBucket < m_bucket.count && m_bucket.xmins[m_indexInBucket] <= m_qxmax)
            {
                if (Hint.Unlikely(math.bitmask(m_qyzMinMax < m_bucket.yzminmaxs[m_indexInBucket]) == 0))
                {
                    m_result.SetBucketRelativeIndex(m_indexInBucket);
                    m_indexInBucket++;
                    return true;
                }
                m_indexInBucket++;
            }

            while (m_currentFrameIndex < 32)
            {
                var currentFrame = m_stack[m_currentFrameIndex];
                if (currentFrame.checkpoint == 0)
                {
                    if (currentFrame.currentIndex >= m_bucket.count)
                    {
                        m_currentFrameIndex--;
                        continue;
                    }

                    var node = m_bucket.intervalTree[(int)currentFrame.currentIndex];
                    if (m_qxmin >= node.subtreeXmax)
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
                    var node = m_bucket.intervalTree[(int)currentFrame.currentIndex];
                    if (m_qxmin < node.xmin)
                    {
                        m_currentFrameIndex--;
                        continue;
                    }

                    currentFrame.checkpoint      = 2;
                    m_stack[m_currentFrameIndex] = currentFrame;
                    m_currentFrameIndex++;
                    m_stack[m_currentFrameIndex].currentIndex = LayerQuerySweepMethods.GetRightChildIndex(currentFrame.currentIndex);
                    m_stack[m_currentFrameIndex].checkpoint   = 0;

                    if (m_qxmin > node.xmin && m_qxmin <= node.xmax)
                    {
                        if (Hint.Unlikely(math.bitmask(m_qyzMinMax < m_bucket.yzminmaxs[node.bucketRelativeBodyIndex]) == 0))
                        {
                            m_result.SetBucketRelativeIndex(node.bucketRelativeBodyIndex);
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

            return false;
        }
    }
}

