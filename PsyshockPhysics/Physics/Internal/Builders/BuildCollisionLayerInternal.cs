using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class BuildCollisionLayerInternal
    {
        public struct ColliderAoSData
        {
            public Collider      collider;
            public TransformQvvs transform;
            public Aabb          aabb;
            public Entity        entity;
        }

        #region Jobs
        //Parallel
        //Calculate RigidTransform, AABB, and target bucket. Write the targetBucket as the layerIndex
        [BurstCompile]
        public struct Part1FromQueryJob : IJobChunk
        {
            [ReadOnly] public CollisionLayer                                                   layer;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<int>             layerIndices;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<ColliderAoSData> colliderAoS;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<float2>          xMinMaxs;
            [ReadOnly] public BuildCollisionLayerTypeHandles                                   typeGroup;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int>                      firstEntityInChunkIndices;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var firstEntityIndex = firstEntityInChunkIndices[unfilteredChunkIndex];

                var chunkEntities   = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders  = chunk.GetNativeArray(ref typeGroup.collider);
                var chunkTransforms = typeGroup.worldTransform.Resolve(chunk);
                var enumerator      = new ChunkEntityWithIndexEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count, firstEntityIndex);
                while (enumerator.NextEntityIndex(out var i, out var index))
                {
                    var collider  = chunkColliders[i];
                    var transform = chunkTransforms[i];
                    var entity    = chunkEntities[i];

                    Aabb aabb = Physics.AabbFrom(in collider, transform.worldTransformQvvs);

                    colliderAoS[index] = new ColliderAoSData
                    {
                        collider  = collider,
                        transform = transform.worldTransformQvvs,
                        aabb      = aabb,
                        entity    = entity
                    };
                    xMinMaxs[index] = new float2(aabb.min.x, aabb.max.x);

                    int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                    int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                    minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                    maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

                    if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
                    {
                        layerIndices[index] = layer.bucketStartsAndCounts.Length - 1;
                    }
                    else if (math.all(minBucket == maxBucket))
                    {
                        layerIndices[index] = (minBucket.x * layer.worldSubdivisionsPerAxis.y + minBucket.y) * layer.worldSubdivisionsPerAxis.z + minBucket.z;
                    }
                    else
                    {
                        layerIndices[index] = layer.bucketStartsAndCounts.Length - 2;
                    }
                }
            }
        }

        //Parallel
        //Calculated Target Bucket and write as layer index
        [BurstCompile]
        public struct Part1FromColliderBodyArrayJob : IJob, IJobParallelFor
        {
            public CollisionLayer                       layer;
            [NoAlias] public NativeArray<int>           layerIndices;
            [ReadOnly] public NativeArray<ColliderBody> colliderBodies;
            [NoAlias] public NativeArray<Aabb>          aabbs;
            [NoAlias] public NativeArray<float2>        xMinMaxs;

            public void Execute()
            {
                for (int i = 0; i < colliderBodies.Length; i++)
                    Execute(i);
            }

            public void Execute(int i)
            {
                var aabb    = Physics.AabbFrom(colliderBodies[i].collider, colliderBodies[i].transform);
                aabbs[i]    = aabb;
                xMinMaxs[i] = new float2(aabb.min.x, aabb.max.x);

                int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

                if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
                {
                    layerIndices[i] = layer.bucketStartsAndCounts.Length - 1;
                }
                else if (math.all(minBucket == maxBucket))
                {
                    layerIndices[i] = (minBucket.x * layer.worldSubdivisionsPerAxis.y + minBucket.y) * layer.worldSubdivisionsPerAxis.z + minBucket.z;
                }
                else
                {
                    layerIndices[i] = layer.bucketStartsAndCounts.Length - 2;
                }
            }
        }

        //Parallel
        //Calculated Target Bucket and write as layer index using the override AABB
        [BurstCompile]
        public struct Part1FromDualArraysJob : IJob, IJobParallelFor
        {
            public CollisionLayer                layer;
            [NoAlias] public NativeArray<int>    layerIndices;
            [ReadOnly] public NativeArray<Aabb>  aabbs;
            [NoAlias] public NativeArray<float2> xMinMaxs;

            public void Execute()
            {
                for (int i = 0; i < aabbs.Length; i++)
                    Execute(i);
            }

            public void Execute(int i)
            {
                var aabb    = aabbs[i];
                xMinMaxs[i] = new float2(aabb.min.x, aabb.max.x);

                int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

                if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
                {
                    layerIndices[i] = layer.bucketStartsAndCounts.Length - 1;
                }
                else if (math.all(minBucket == maxBucket))
                {
                    layerIndices[i] = (minBucket.x * layer.worldSubdivisionsPerAxis.y + minBucket.y) * layer.worldSubdivisionsPerAxis.z + minBucket.z;
                }
                else
                {
                    layerIndices[i] = layer.bucketStartsAndCounts.Length - 2;
                }
            }
        }

        //Single
        //Count total in each bucket and assign global array position to layerIndex
        [BurstCompile]
        public struct Part2Job : IJob
        {
            public CollisionLayer             layer;
            [NoAlias] public NativeArray<int> layerIndices;

            public void Execute()
            {
                NativeArray<int> countsPerBucket = new NativeArray<int>(layer.bucketStartsAndCounts.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int i = 0; i < layerIndices.Length; i++)
                {
                    countsPerBucket[layerIndices[i]]++;
                }

                int totalProcessed = 0;
                for (int i = 0; i < countsPerBucket.Length; i++)
                {
                    layer.bucketStartsAndCounts[i]  = new int2(totalProcessed, countsPerBucket[i]);
                    totalProcessed                 += countsPerBucket[i];
                    countsPerBucket[i]              = 0;
                }

                for (int i = 0; i < layerIndices.Length; i++)
                {
                    int bucketIndex = layerIndices[i];
                    layerIndices[i] = layer.bucketStartsAndCounts[bucketIndex].x + countsPerBucket[bucketIndex];
                    countsPerBucket[bucketIndex]++;
                }
            }
        }

        //Parallel
        //Reverse array of dst indices to array of src indices
        //Todo: Might be faster as an IJob due to potential false sharing
        [BurstCompile]
        public struct Part3Job : IJob, IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int>          layerIndices;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<int> unsortedSrcIndices;

            public void Execute()
            {
                for (int i = 0; i < layerIndices.Length; i++)
                    Execute(i);
            }

            public void Execute(int i)
            {
                int spot                 = layerIndices[i];
                unsortedSrcIndices[spot] = i;
            }
        }

        //Parallel
        //Sort buckets
        [BurstCompile]
        public struct Part4Job : IJob, IJobParallelFor
        {
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<int>              unsortedSrcIndices;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<IntervalTreeNode> trees;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<float2>                    xMinMaxs;
            [ReadOnly] public NativeArray<int2>                                                 bucketStartAndCounts;

            public void Execute()
            {
                for (int i = 0; i < bucketStartAndCounts.Length - 1; i++)
                    Execute(i);
            }

            public void Execute(int i)
            {
                var startAndCount = bucketStartAndCounts[i];

                var intSlice = unsortedSrcIndices.GetSubArray(startAndCount.x, startAndCount.y);
                RadixSortBucket(intSlice, xMinMaxs);
                var tree = trees.GetSubArray(startAndCount.x, startAndCount.y);
                BuildEytzingerIntervalTree(tree, intSlice, xMinMaxs);
            }
        }

        //Parallel
        //Sort buckets using Unity's sort (may be better for smaller counts, needs more analysis)
        /*[BurstCompile]
           public struct Part4UnityJob : IJobFor
           {
            [NativeDisableParallelForRestriction] public NativeArray<int>   unsortedSrcIndices;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<float> xmins;
            [ReadOnly] public NativeArray<int2>                             bucketStartAndCounts;

            public void Execute(int i)
            {
                var startAndCount = bucketStartAndCounts[i];

                var intSlice = unsortedSrcIndices.Slice(startAndCount.x, startAndCount.y);
                UnitySortBucket(intSlice, xmins);
            }
           }*/

        //Parallel
        //Copy AoS data to SoA layer
        [BurstCompile]
        public struct Part5FromQueryJob : IJob, IJobParallelFor
        {
            [NoAlias, NativeDisableParallelForRestriction]
            public CollisionLayer layer;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<ColliderAoSData> colliderAoS;

            public void Execute()
            {
                for (int i = 0; i < layer.srcIndices.Length; i++)
                    Execute(i);
            }

            public void Execute(int i)
            {
                var aos         = colliderAoS[layer.srcIndices[i]];
                layer.bodies[i] = new ColliderBody
                {
                    collider  = aos.collider,
                    transform = aos.transform,
                    entity    = aos.entity
                };
                layer.xmins[i]     = aos.aabb.min.x;
                layer.xmaxs[i]     = aos.aabb.max.x;
                layer.yzminmaxs[i] = new float4(aos.aabb.min.yz, -aos.aabb.max.yz);
            }
        }

        //Parallel
        //Copy array data to layer
        [BurstCompile]
        public struct Part5FromArraysJob : IJob, IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            public CollisionLayer layer;

            [ReadOnly] public NativeArray<Aabb>         aabbs;
            [ReadOnly] public NativeArray<ColliderBody> bodies;

            public void Execute()
            {
                for (int i = 0; i < layer.srcIndices.Length; i++)
                    Execute(i);
            }

            public void Execute(int i)
            {
                int src            = layer.srcIndices[i];
                layer.bodies[i]    = bodies[src];
                layer.xmins[i]     = aabbs[src].min.x;
                layer.xmaxs[i]     = aabbs[src].max.x;
                layer.yzminmaxs[i] = new float4(aabbs[src].min.yz, -aabbs[src].max.yz);
            }
        }

        //Single
        //All five steps for custom arrays
        [BurstCompile]
        public struct BuildFromColliderArraySingleJob : IJob
        {
            public CollisionLayer layer;

            [ReadOnly] public NativeArray<ColliderBody> bodies;

            public void Execute()
            {
                BuildImmediate(ref layer, bodies);
            }
        }

        //Single
        //All five steps for custom arrays
        [BurstCompile]
        public struct BuildFromDualArraysSingleJob : IJob
        {
            public CollisionLayer layer;

            [ReadOnly] public NativeArray<Aabb>         aabbs;
            [ReadOnly] public NativeArray<ColliderBody> bodies;

            public void Execute()
            {
                BuildImmediate(ref layer, bodies, aabbs);
            }
        }

        #endregion

        #region Immediate
        public static void BuildImmediate(ref CollisionLayer layer, NativeArray<ColliderBody> bodies)
        {
            var aabbs        = new NativeArray<Aabb>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var layerIndices = new NativeArray<int>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var xMinMaxs     = new NativeArray<float2>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var p1 = new Part1FromColliderBodyArrayJob
            {
                aabbs          = aabbs,
                colliderBodies = bodies,
                layer          = layer,
                layerIndices   = layerIndices,
                xMinMaxs       = xMinMaxs
            };
            for (int i = 0; i < layer.count; i++)
            {
                p1.Execute(i);
            }

            new Part2Job
            {
                layer        = layer,
                layerIndices = layerIndices
            }.Execute();

            var p3 = new Part3Job
            {
                layerIndices       = layerIndices,
                unsortedSrcIndices = layer.srcIndices
            };
            for (int i = 0; i < layer.count; i++)
            {
                p3.Execute(i);
            }

            var p4 = new Part4Job
            {
                bucketStartAndCounts = layer.bucketStartsAndCounts,
                unsortedSrcIndices   = layer.srcIndices,
                trees                = layer.intervalTrees,
                xMinMaxs             = xMinMaxs
            };
            for (int i = 0; i < layer.bucketCount; i++)
            {
                p4.Execute(i);
            }

            var p5 = new Part5FromArraysJob
            {
                aabbs  = aabbs,
                bodies = bodies,
                layer  = layer,
            };
            for (int i = 0; i < layer.count; i++)
            {
                p5.Execute(i);
            }
        }

        public static void BuildImmediate(ref CollisionLayer layer, NativeArray<ColliderBody> bodies, NativeArray<Aabb> aabbs)
        {
            var layerIndices = new NativeArray<int>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var xMinMaxs     = new NativeArray<float2>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var p1 = new Part1FromDualArraysJob
            {
                aabbs        = aabbs,
                layer        = layer,
                layerIndices = layerIndices,
                xMinMaxs     = xMinMaxs
            };
            for (int i = 0; i < layer.count; i++)
            {
                p1.Execute(i);
            }

            new Part2Job
            {
                layer        = layer,
                layerIndices = layerIndices
            }.Execute();

            var p3 = new Part3Job
            {
                layerIndices       = layerIndices,
                unsortedSrcIndices = layer.srcIndices
            };
            for (int i = 0; i < layer.count; i++)
            {
                p3.Execute(i);
            }

            var p4 = new Part4Job
            {
                bucketStartAndCounts = layer.bucketStartsAndCounts,
                unsortedSrcIndices   = layer.srcIndices,
                xMinMaxs             = xMinMaxs
            };
            for (int i = 0; i < layer.bucketCount; i++)
            {
                p4.Execute(i);
            }

            var p5 = new Part5FromArraysJob
            {
                aabbs  = aabbs,
                bodies = bodies,
                layer  = layer,
            };
            for (int i = 0; i < layer.count; i++)
            {
                p5.Execute(i);
            }
        }
        #endregion

        #region UnitySortBucket
        private struct Ranker : System.IComparable<Ranker>
        {
            public float key;
            public int   index;

            public int CompareTo(Ranker other)
            {
                return key.CompareTo(other.key);
            }
        }

        private static void UnitySortBucket(NativeSlice<int> unsortedSrcIndices, NativeArray<float> xmins)
        {
            var count = unsortedSrcIndices.Length;
            if (count <= 1)
                return;

            var ranks = new NativeArray<Ranker>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < count; i++)
            {
                ranks[i] = new Ranker
                {
                    index = unsortedSrcIndices[i],
                    key   = xmins[unsortedSrcIndices[i]]
                };
            }

            ranks.Sort();

            for (int i = 0; i < count; i++)
            {
                unsortedSrcIndices[i] = ranks[i].index;
            }
        }
        #endregion

        #region RadixSortBucket
        private struct Indexer
        {
            public UintAsBytes key;
            public int         index;
        }

        private struct UintAsBytes
        {
            public byte byte1;
            public byte byte2;
            public byte byte3;
            public byte byte4;
        }

        private static UintAsBytes Keys(float val)
        {
            uint key  = math.asuint(val);
            uint mask = (key & 0x80000000) > 0 ? 0xffffffff : 0x80000000;
            key       = mask ^ key;

            UintAsBytes result;
            result.byte1 = (byte)(key & 0x000000FF);
            key          = key >> 8;
            result.byte2 = (byte)(key & 0x000000FF);
            key          = key >> 8;
            result.byte3 = (byte)(key & 0x000000FF);
            key          = key >> 8;
            result.byte4 = (byte)(key & 0x000000FF);
            return result;
        }

        private static void CalculatePrefixSum(NativeArray<int> counts, NativeArray<int> sums)
        {
            sums[0] = 0;
            for (int i = 0; i < counts.Length - 1; i++)
            {
                sums[i + 1] = sums[i] + counts[i];
            }
        }

        private static void RadixSortBucket(NativeArray<int> unsortedSrcIndices, NativeArray<float2> xMinMaxs)
        {
            var count = unsortedSrcIndices.Length;
            if (count <= 0)
                return;

            NativeArray<int> counts1    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts2    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts3    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts4    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> prefixSum1 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum2 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum3 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum4 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<Indexer> frontArray = new NativeArray<Indexer>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<Indexer> backArray  = new NativeArray<Indexer>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            //Counts
            for (int i = 0; i < count; i++)
            {
                var keys            = Keys(xMinMaxs[unsortedSrcIndices[i]].x);
                counts1[keys.byte1] = counts1[keys.byte1] + 1;
                counts2[keys.byte2] = counts2[keys.byte2] + 1;
                counts3[keys.byte3] = counts3[keys.byte3] + 1;
                counts4[keys.byte4] = counts4[keys.byte4] + 1;
                frontArray[i]       = new Indexer { key = keys, index = unsortedSrcIndices[i] };
            }

            //Sums
            CalculatePrefixSum(counts1, prefixSum1);
            CalculatePrefixSum(counts2, prefixSum2);
            CalculatePrefixSum(counts3, prefixSum3);
            CalculatePrefixSum(counts4, prefixSum4);

            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].key.byte1;
                int  dest       = prefixSum1[key];
                backArray[dest] = frontArray[i];
                prefixSum1[key] = prefixSum1[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = backArray[i].key.byte2;
                int  dest        = prefixSum2[key];
                frontArray[dest] = backArray[i];
                prefixSum2[key]  = prefixSum2[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].key.byte3;
                int  dest       = prefixSum3[key];
                backArray[dest] = frontArray[i];
                prefixSum3[key] = prefixSum3[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key                 = backArray[i].key.byte4;
                int  dest                = prefixSum4[key];
                int  src                 = backArray[i].index;
                unsortedSrcIndices[dest] = src;
                prefixSum4[key]          = prefixSum4[key] + 1;
            }
        }
        #endregion

        #region Eytzinger Interval Tree

        //   Unless otherwise specified, the following functions are C# adaptations of Paul-Virak Khuong and Pat Morin's
        //   Eytzinger Array builder: https://github.com/patmorin/arraylayout/blob/master/src/eytzinger_array.h
        //   This code is licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0)
        private static void BuildEytzingerIntervalTree(NativeArray<IntervalTreeNode> tree, NativeArray<int> sortedSrcIndices, NativeArray<float2> srcXminMaxs)
        {
            var builder = new EytzingerIntervalTreeBuilder(tree, sortedSrcIndices, srcXminMaxs);
            builder.Build();
        }

        private struct EytzingerIntervalTreeBuilder
        {
            private NativeArray<IntervalTreeNode> nodesToPopulate;
            private NativeArray<int>              sortedSrcIndices;
            private NativeArray<float2>           srcXminMaxs;

            public EytzingerIntervalTreeBuilder(NativeArray<IntervalTreeNode> tree, NativeArray<int> sortedSrcIndices, NativeArray<float2> srcXminMaxs)
            {
                this.nodesToPopulate  = tree;
                this.sortedSrcIndices = sortedSrcIndices;
                this.srcXminMaxs      = srcXminMaxs;
            }

            public void Build()
            {
                BuildEytzingerIntervalTreeRecurse(0, 0);

                PatchSubtreeMaxResurse(0);
            }

            private int BuildEytzingerIntervalTreeRecurse(int bucketRelativeIndex, uint treeIndex)
            {
                // It is for this condition that we need treeIndex to be a uint, which can store 2 * (int.MaxValue - 1) + 2 without overflow.
                // If code reaches beyond this point, it is safe to cast treeIndex to an int.
                if (treeIndex >= nodesToPopulate.Length)
                    return bucketRelativeIndex;

                bucketRelativeIndex = BuildEytzingerIntervalTreeRecurse(bucketRelativeIndex, 2 * treeIndex + 1);

                var minmax                      = srcXminMaxs[sortedSrcIndices[bucketRelativeIndex]];
                nodesToPopulate[(int)treeIndex] = new IntervalTreeNode
                {
                    xmin                    = minmax.x,
                    xmax                    = minmax.y,
                    subtreeXmax             = minmax.y,
                    bucketRelativeBodyIndex = bucketRelativeIndex
                };
                bucketRelativeIndex++;

                bucketRelativeIndex = BuildEytzingerIntervalTreeRecurse(bucketRelativeIndex, 2 * treeIndex + 2);

                return bucketRelativeIndex;
            }

            // This function is unique to Latios Framework
            // Todo: There is likely a more cache-friendly way to iterate this tree and do this work
            private float PatchSubtreeMaxResurse(uint treeIndex)
            {
                if (treeIndex >= nodesToPopulate.Length)
                    return 0f;

                float leftTreeMax  = PatchSubtreeMaxResurse(2 * treeIndex + 1);
                float rightTreeMax = PatchSubtreeMaxResurse(2 * treeIndex + 2);

                var node                        = nodesToPopulate[(int)treeIndex];
                node.subtreeXmax                = math.max(math.max(leftTreeMax, rightTreeMax), node.subtreeXmax);
                nodesToPopulate[(int)treeIndex] = node;

                return node.subtreeXmax;
            }
        }

        #endregion
    }
}

