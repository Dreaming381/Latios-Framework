using System;
using System.Diagnostics;
using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
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

        public struct FilteredChunkCache
        {
            public ArchetypeChunk chunk;
            public v128           mask;
            public int            firstEntityIndex;
            public int            countInChunk;
            public bool           usesMask;
        }

        #region Jobs
        // Parallel
        // Calculate Aabb and target bucket. Write the targetBucket as the layerIndex
        [BurstCompile]
        public struct Part1FromUnfilteredQueryJob : IJobChunk
        {
            [ReadOnly] public CollisionLayer                                                   layer;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<int>             layerIndices;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<ColliderAoSData> colliderAoS;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<float2>          xMinMaxs;
            [ReadOnly] public BuildCollisionLayerTypeHandles                                   typeGroup;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int>                      firstEntityInChunkIndices;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunkEntities   = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders  = chunk.GetNativeArray(ref typeGroup.collider);
                var chunkTransforms = typeGroup.worldTransform.Resolve(chunk);
                for (int src = 0, dst = firstEntityInChunkIndices[unfilteredChunkIndex]; src < chunk.Count; src++, dst++)
                {
                    var collider  = chunkColliders[src];
                    var transform = chunkTransforms[src];
                    var entity    = chunkEntities[src];

                    Aabb aabb = Physics.AabbFrom(in collider, transform.worldTransformQvvs);

                    colliderAoS[dst] = new ColliderAoSData
                    {
                        collider  = collider,
                        transform = transform.worldTransformQvvs,
                        aabb      = aabb,
                        entity    = entity
                    };
                    xMinMaxs[dst] = new float2(aabb.min.x, aabb.max.x);

                    int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                    int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                    minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                    maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

                    if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
                    {
                        layerIndices[dst] = IndexStrategies.NanBucketIndex(layer.cellCount);
                    }
                    else if (math.all(minBucket == maxBucket))
                    {
                        layerIndices[dst] = IndexStrategies.CellIndexFromSubdivisionIndices(minBucket, layer.worldSubdivisionsPerAxis);
                    }
                    else
                    {
                        layerIndices[dst] = IndexStrategies.CrossBucketIndex(layer.cellCount);
                    }
                }
            }
        }

        // Single
        // Create a cache of chunks, their masks, and their counts to preallocate the arrays without having to process filters twice.
        [BurstCompile]
        public struct Part0PrefilterQueryJob : IJobChunk
        {
            public NativeList<FilteredChunkCache> filteredChunkCache;
            int                                   countAccumulated;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                int filteredCount = math.select(chunk.Count, math.countbits(chunkEnabledMask.ULong0) + math.countbits(chunkEnabledMask.ULong1), useEnabledMask);
                filteredChunkCache.AddNoResize(new FilteredChunkCache
                {
                    chunk            = chunk,
                    mask             = chunkEnabledMask,
                    firstEntityIndex = countAccumulated,
                    countInChunk     = filteredCount,
                    usesMask         = useEnabledMask
                });
                countAccumulated += filteredCount;
            }
        }

        // Single
        // Allocate the CollisionLayer NativeLists and temp lists to the correct size using the gathers cached chunks.
        [BurstCompile]
        public struct AllocateCollisionLayerFromFilteredQueryJob : IJob
        {
            public CollisionLayer                             layer;
            public NativeList<int>                            layerIndices;
            public NativeList<float2>                         xMinMaxs;
            public NativeList<ColliderAoSData>                colliderAoS;
            [ReadOnly] public NativeArray<FilteredChunkCache> filteredChunkCache;

            public void Execute()
            {
                if (filteredChunkCache.Length == 0)
                    return;

                var last  = filteredChunkCache[filteredChunkCache.Length - 1];
                var count = last.firstEntityIndex + last.countInChunk;
                layer.ResizeUninitialized(count);
                layerIndices.ResizeUninitialized(count);
                xMinMaxs.ResizeUninitialized(count);
                colliderAoS.ResizeUninitialized(count);
            }
        }

        // Parallel
        // Calculate Aabb and target bucket. Write the targetBucket as the layerIndex
        [BurstCompile]
        public struct Part1FromFilteredQueryJob : IJobParallelForDefer
        {
            [ReadOnly] public CollisionLayer                                                   layer;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<int>             layerIndices;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<ColliderAoSData> colliderAoS;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<float2>          xMinMaxs;
            [ReadOnly] public BuildCollisionLayerTypeHandles                                   typeGroup;
            [ReadOnly] public NativeArray<FilteredChunkCache>                                  filteredChunkCache;

            public void Execute(int chunkIndex)
            {
                var filteredChunk    = filteredChunkCache[chunkIndex];
                var chunk            = filteredChunk.chunk;
                var firstEntityIndex = filteredChunk.firstEntityIndex;
                var useEnabledMask   = filteredChunk.usesMask;
                var chunkEnabledMask = filteredChunk.mask;

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
                        layerIndices[index] = IndexStrategies.NanBucketIndex(layer.cellCount);
                    }
                    else if (math.all(minBucket == maxBucket))
                    {
                        layerIndices[index] = IndexStrategies.CellIndexFromSubdivisionIndices(minBucket, layer.worldSubdivisionsPerAxis);
                    }
                    else
                    {
                        layerIndices[index] = IndexStrategies.CrossBucketIndex(layer.cellCount);
                    }
                }
            }
        }

        // Single
        // Allocate the CollisionLayer NativeLists and temp lists to the correct size using the bodies list
        [BurstCompile]
        public struct AllocateCollisionLayerFromBodiesListJob : IJob
        {
            public CollisionLayer                       layer;
            public NativeList<int>                      layerIndices;
            public NativeList<float2>                   xMinMaxs;
            public NativeList<Aabb>                     aabbs;
            [ReadOnly] public NativeArray<ColliderBody> bodies;

            public bool aabbsAreProvided;

            public void Execute()
            {
                layer.ResizeUninitialized(bodies.Length);
                layerIndices.ResizeUninitialized(bodies.Length);
                xMinMaxs.ResizeUninitialized(bodies.Length);
                if (aabbsAreProvided)
                {
                    ValidateOverrideAabbsAreRightLength(aabbs.AsArray(), bodies.Length);
                }
                else
                {
                    aabbs.ResizeUninitialized(bodies.Length);
                }
            }
        }

        // Parallel
        // Calculate target bucket and write as layer index
        [BurstCompile]
        public struct Part1FromColliderBodyArrayJob : IJob, IJobParallelForDefer
        {
            [ReadOnly] public CollisionLayer            layer;
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
                    layerIndices[i] = IndexStrategies.NanBucketIndex(layer.cellCount);
                }
                else if (math.all(minBucket == maxBucket))
                {
                    layerIndices[i] = IndexStrategies.CellIndexFromSubdivisionIndices(minBucket, layer.worldSubdivisionsPerAxis);
                }
                else
                {
                    layerIndices[i] = IndexStrategies.CrossBucketIndex(layer.cellCount);
                }
            }
        }

        // Parallel
        // Calculate target bucket and write as layer index using the override Aabb
        [BurstCompile]
        public struct Part1FromDualArraysJob : IJob, IJobParallelForDefer
        {
            [ReadOnly] public CollisionLayer     layer;
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
                ValidateOverrideAabbsAreRightLength(aabbs, layerIndices.Length);

                var aabb    = aabbs[i];
                xMinMaxs[i] = new float2(aabb.min.x, aabb.max.x);

                int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

                if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
                {
                    layerIndices[i] = IndexStrategies.NanBucketIndex(layer.cellCount);
                }
                else if (math.all(minBucket == maxBucket))
                {
                    layerIndices[i] = IndexStrategies.CellIndexFromSubdivisionIndices(minBucket, layer.worldSubdivisionsPerAxis);
                }
                else
                {
                    layerIndices[i] = IndexStrategies.CrossBucketIndex(layer.cellCount);
                }
            }
        }

        // Single
        // Count total in each bucket and assign global array position to layerIndex
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

        // Parallel
        // Reverse array of dst indices to array of src indices
        // Todo: Might be faster as an IJob due to potential false sharing
        [BurstCompile]
        public struct Part3Job : IJob, IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int>                                     layerIndices;
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

        // Parallel
        // Sort buckets and build interval trees
        [BurstCompile]
        public struct Part4Job : IJob, IJobParallelFor
        {
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<int>              unsortedSrcIndices;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<IntervalTreeNode> trees;
            [ReadOnly] public NativeArray<float2>                                               xMinMaxs;
            [ReadOnly] public NativeArray<int2>                                                 bucketStartAndCounts;

            public void Execute()
            {
                for (int i = 0; i < IndexStrategies.BucketCountWithoutNaNFromBucketCountWithNaN(bucketStartAndCounts.Length); i++)
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

        // Parallel
        // Copy AoS data to SoA layer
        [BurstCompile]
        public struct Part5FromAoSJob : IJob, IJobParallelForDefer
        {
            [NoAlias, NativeDisableParallelForRestriction]
            public CollisionLayer layer;

            [ReadOnly]
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

        // Parallel
        // Copy array data to layer
        [BurstCompile]
        public struct Part5FromSplitArraysJob : IJob, IJobParallelForDefer
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

        // Single
        // All five steps plus allocation for chunk caches
        [BurstCompile]
        public struct BuildFromFilteredChunkCacheSingleJob : IJob
        {
            public CollisionLayer                             layer;
            [ReadOnly] public NativeArray<FilteredChunkCache> filteredChunkCache;
            public BuildCollisionLayerTypeHandles             handles;

            public void Execute()
            {
                BuildImmediate(ref layer, filteredChunkCache, in handles);
            }
        }

        // Single
        // All five steps for custom bodies array
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

        // Single
        // All five steps for custom arrays
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
        public static void BuildImmediate(ref CollisionLayer layer, NativeArray<FilteredChunkCache> filteredChunkCache, in BuildCollisionLayerTypeHandles handles)
        {
            int count = 0;
            if (filteredChunkCache.Length != 0)
            {
                var last = filteredChunkCache[filteredChunkCache.Length - 1];
                count    = last.firstEntityIndex + last.countInChunk;
            }
            layer.ResizeUninitialized(count);

            var layerIndices = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var xMinMaxs     = new NativeArray<float2>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var colliderAoS  = new NativeArray<ColliderAoSData>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var p1 = new Part1FromFilteredQueryJob
            {
                colliderAoS        = colliderAoS,
                layer              = layer,
                layerIndices       = layerIndices,
                xMinMaxs           = xMinMaxs,
                filteredChunkCache = filteredChunkCache,
                typeGroup          = handles
            };
            for (int i = 0; i < filteredChunkCache.Length; i++)
            {
                p1.Execute(i);
            }

            BuildImmediateAoS2To5(ref layer, colliderAoS, layerIndices, xMinMaxs);
        }

        public static void BuildImmediate(ref CollisionLayer layer, NativeArray<ColliderBody> bodies)
        {
            var aabbs        = new NativeArray<Aabb>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var layerIndices = new NativeArray<int>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var xMinMaxs     = new NativeArray<float2>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            layer.ResizeUninitialized(bodies.Length);

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

            BuildImmediateSplit2To5(ref layer, bodies, aabbs, layerIndices, xMinMaxs);
        }

        public static void BuildImmediate(ref CollisionLayer layer, NativeArray<ColliderBody> bodies, NativeArray<Aabb> aabbs)
        {
            ValidateOverrideAabbsAreRightLength(aabbs, bodies.Length);

            var layerIndices = new NativeArray<int>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var xMinMaxs     = new NativeArray<float2>(bodies.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            layer.ResizeUninitialized(bodies.Length);

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

            BuildImmediateSplit2To5(ref layer, bodies, aabbs, layerIndices, xMinMaxs);
        }

        static void BuildImmediateAoS2To5(ref CollisionLayer layer,
                                          NativeArray<ColliderAoSData> colliderAoS,
                                          NativeArray<int>             layerIndices,
                                          NativeArray<float2>          xMinMaxs)
        {
            new Part2Job
            {
                layer        = layer,
                layerIndices = layerIndices
            }.Execute();

            var p3 = new Part3Job
            {
                layerIndices       = layerIndices,
                unsortedSrcIndices = layer.srcIndices.AsArray()
            };
            for (int i = 0; i < layer.count; i++)
            {
                p3.Execute(i);
            }

            var p4 = new Part4Job
            {
                bucketStartAndCounts = layer.bucketStartsAndCounts.AsArray(),
                trees                = layer.intervalTrees.AsArray(),
                unsortedSrcIndices   = layer.srcIndices.AsArray(),
                xMinMaxs             = xMinMaxs
            };
            for (int i = 0; i < layer.bucketCount; i++)
            {
                p4.Execute(i);
            }

            var p5 = new Part5FromAoSJob
            {
                colliderAoS = colliderAoS,
                layer       = layer,
            };
            for (int i = 0; i < layer.count; i++)
            {
                p5.Execute(i);
            }
        }

        static void BuildImmediateSplit2To5(ref CollisionLayer layer,
                                            NativeArray<ColliderBody> bodies,
                                            NativeArray<Aabb>         aabbs,
                                            NativeArray<int>          layerIndices,
                                            NativeArray<float2>       xMinMaxs)
        {
            new Part2Job
            {
                layer        = layer,
                layerIndices = layerIndices
            }.Execute();

            var p3 = new Part3Job
            {
                layerIndices       = layerIndices,
                unsortedSrcIndices = layer.srcIndices.AsArray()
            };
            for (int i = 0; i < layer.count; i++)
            {
                p3.Execute(i);
            }

            var p4 = new Part4Job
            {
                bucketStartAndCounts = layer.bucketStartsAndCounts.AsArray(),
                trees                = layer.intervalTrees.AsArray(),
                unsortedSrcIndices   = layer.srcIndices.AsArray(),
                xMinMaxs             = xMinMaxs
            };
            for (int i = 0; i < layer.bucketCount; i++)
            {
                p4.Execute(i);
            }

            var p5 = new Part5FromSplitArraysJob
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

        #region Safety
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateOverrideAabbsAreRightLength(NativeArray<Aabb> aabbs, int count)
        {
            if (aabbs.Length != count)
                throw new InvalidOperationException(
                    $"The number of elements in overrideAbbs does not match the number of bodies in the bodies array");
        }
        #endregion
    }
}

