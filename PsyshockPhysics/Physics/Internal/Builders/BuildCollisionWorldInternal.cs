using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static unsafe class BuildCollisionWorldInternal
    {
        public struct ColliderAoSData
        {
            public Collider             collider;
            public TransformQvvs        transform;
            public Aabb                 aabb;
            public Entity               entity;
            public CollisionWorldIndex* indexPtr;
        }

        #region Jobs

        // Single
        // Allocate the CollisionLayer NativeLists and temp lists to the correct size using the gathers cached chunks.
        [BurstCompile]
        public struct AllocateCollisionLayerFromFilteredQueryJob : IJob
        {
            public CollisionLayer                                                         layer;
            public NativeList<int>                                                        layerIndices;
            public NativeList<float2>                                                     xMinMaxs;
            public NativeList<ColliderAoSData>                                            colliderAoS;
            [ReadOnly] public NativeArray<BuildCollisionLayerInternal.FilteredChunkCache> filteredChunkCache;

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
            [ReadOnly] public NativeArray<BuildCollisionLayerInternal.FilteredChunkCache>      filteredChunkCache;

            public BuildCollisionWorldTypeHandles typeGroup;

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
                var aabbs           = chunk.GetNativeArray(ref typeGroup.aabb);
                var indices         = chunk.GetComponentDataPtrRW(ref typeGroup.index);
                var enumerator      = new ChunkEntityWithIndexEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count, firstEntityIndex);
                while (enumerator.NextEntityIndex(out var i, out var index))
                {
                    var collider  = chunkColliders[i];
                    var transform = chunkTransforms[i];
                    var entity    = chunkEntities[i];

                    Aabb aabb = aabbs.Length > 0 ? aabbs[i].aabb : Physics.AabbFrom(in collider, transform.worldTransformQvvs);

                    colliderAoS[index] = new ColliderAoSData
                    {
                        collider  = collider,
                        transform = transform.worldTransformQvvs,
                        aabb      = aabb,
                        entity    = entity,
                        indexPtr  = indices == null ? null : indices + i
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

        [BurstCompile]
        public struct Part1bBuildArchetypesFromCacheJob : IJob
        {
            [ReadOnly] public NativeArray<BuildCollisionLayerInternal.FilteredChunkCache> chunks;
            public NativeList<EntityArchetype>                                            archetypes;
            public NativeList<short>                                                      sourceArchetypeIndices;
            public NativeList<short>                                                      archetypeIndicesByBody;
            public NativeList<int2>                                                       archetypeStartsAndCountsByBucket;  // Archetype is inner array
            public NativeList<int>                                                        archetypeBodyIndicesByBucket;  // Relative
            public NativeList<IntervalTreeNode>                                           archetypeIntervalTreesByBucket;
            public int                                                                    bucketCountWithNaN;

            public void Execute()
            {
                int count = 0;
                if (chunks.Length > 0)
                {
                    var last = chunks[chunks.Length - 1];
                    count    = last.firstEntityIndex + last.countInChunk;
                }
                sourceArchetypeIndices.Capacity = count;

                var archetypeMap = new NativeHashMap<EntityArchetype, short>(math.min(short.MaxValue, chunks.Length), Allocator.Temp);
                foreach (var chunk in chunks)
                {
                    var   archetype = chunk.chunk.Archetype;
                    short archetypeIndex;
                    if (!archetypeMap.TryGetValue(archetype, out archetypeIndex))
                    {
                        archetypeIndex = (short)archetypeMap.Count;
                        archetypeMap.Add(archetype, archetypeIndex);
                    }
                    for (int i = 0; i < chunk.countInChunk; i++)
                        sourceArchetypeIndices.AddNoResize(archetypeIndex);
                }

                archetypes.ResizeUninitialized(archetypeMap.Count);
                foreach (var pair in archetypeMap)
                    archetypes[pair.Value] = pair.Key;

                var archetypeBucketCount = archetypeMap.Count * bucketCountWithNaN;
                archetypeStartsAndCountsByBucket.ResizeUninitialized(archetypeBucketCount);
                archetypeIndicesByBody.ResizeUninitialized(count);
                archetypeBodyIndicesByBucket.ResizeUninitialized(count);
                archetypeIntervalTreesByBucket.ResizeUninitialized(count);
            }
        }

        // Borrow jobs 2-4 from CollisionLayer

        // Parallel
        // Copy AoS data to SoA layer and write indices
        [BurstCompile]
        public struct Part5FromAoSJob : IJob, IJobParallelForDefer
        {
            [NoAlias, NativeDisableParallelForRestriction] public CollisionLayer layer;
            [ReadOnly] public NativeArray<ColliderAoSData>                       colliderAoS;

            public int worldIndexPreshifted;

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
                if (aos.indexPtr != null)
                    aos.indexPtr->packed = worldIndexPreshifted + i;
            }
        }

        [BurstCompile]
        public struct Part6BuildBucketArchetypesJob : IJobFor
        {
            [ReadOnly] public CollisionLayer                                           layer;
            [ReadOnly] public NativeArray<short>                                       sourceArchetypeIndices;
            [ReadOnly] public NativeArray<EntityArchetype>                             archetypes;
            [NativeDisableParallelForRestriction] public NativeArray<short>            archetypeIndicesByBody;
            [NativeDisableParallelForRestriction] public NativeArray<int2>             archetypeStartsAndCountsByBucket;
            [NativeDisableParallelForRestriction] public NativeArray<int>              archetypeBodyIndicesByBucket;
            [NativeDisableParallelForRestriction] public NativeArray<IntervalTreeNode> archetypeIntervalTreesByBucket;

            public void Execute(int index)
            {
                var bucketStartAndCount      = layer.bucketStartsAndCounts[index];
                var archetypeStartsAndCounts = archetypeStartsAndCountsByBucket.GetSubArray(index * archetypes.Length, archetypes.Length);
                archetypeStartsAndCounts.AsSpan().Clear();
                if (bucketStartAndCount.y == 0)
                    return;

                var bucket              = layer.GetBucketSlices(index);
                var dstArchetypeIndices = archetypeIndicesByBody.GetSubArray(bucketStartAndCount.x, bucketStartAndCount.y);
                var bodyIndices         = archetypeBodyIndicesByBucket.GetSubArray(bucketStartAndCount.x, bucketStartAndCount.y);
                var intervalTrees       = archetypeIntervalTreesByBucket.GetSubArray(bucketStartAndCount.x, bucketStartAndCount.y);

                for (int i = 0; i < dstArchetypeIndices.Length; i++)
                {
                    var sourceIndex                           = bucket.srcIndices[i];
                    var archetypeIndex                        = sourceArchetypeIndices[sourceIndex];
                    archetypeStartsAndCounts[archetypeIndex] += new int2(0, 1);
                    dstArchetypeIndices[i]                    = archetypeIndex;
                }
                var runningCount = 0;
                for (int i = 0; i < archetypeStartsAndCounts.Length; i++)
                {
                    var asac                     = archetypeStartsAndCounts[i];
                    asac.x                      += runningCount;
                    runningCount                += asac.y;
                    asac.y                       = 0;
                    archetypeStartsAndCounts[i]  = asac;
                }

                for (int i = 0; i < dstArchetypeIndices.Length; i++)
                {
                    var archetypeIndex            = dstArchetypeIndices[i];
                    var asac                      = archetypeStartsAndCounts[archetypeIndex];
                    var indexInArchetype          = asac.x + asac.y;
                    bodyIndices[indexInArchetype] = i;
                    asac.y++;
                    archetypeStartsAndCounts[archetypeIndex] = asac;
                }

                // Don't build the interval trees for the NaN buckets
                if (index == IndexStrategies.NanBucketIndex(layer.cellCount))
                    return;

                for (int i = 0; i < archetypeStartsAndCounts.Length; i++)
                {
                    var asac = archetypeStartsAndCounts[i];
                    if (asac.y == 0)
                        continue;

                    var tree    = intervalTrees.GetSubArray(asac.x, asac.y);
                    var indices = bodyIndices.GetSubArray(asac.x, asac.y);
                    BuildEytzingerIntervalTree(tree, indices, bucket.xmins, bucket.xmaxs);
                }
            }
        }

        // Single
        // All steps
        [BurstCompile]
        public struct BuildFromFilteredChunkCacheSingleJob : IJob
        {
            public CollisionWorld                                                         world;
            [ReadOnly] public NativeArray<BuildCollisionLayerInternal.FilteredChunkCache> filteredChunkCache;
            public BuildCollisionWorldTypeHandles                                         handles;

            public void Execute()
            {
                BuildImmediate(ref world, filteredChunkCache, in handles);
            }
        }

        #endregion

        public static void BuildImmediate(ref CollisionWorld world,
                                          NativeArray<BuildCollisionLayerInternal.FilteredChunkCache> filteredChunkCache,
                                          in BuildCollisionWorldTypeHandles handles)
        {
            int count = 0;
            if (filteredChunkCache.Length != 0)
            {
                var last = filteredChunkCache[filteredChunkCache.Length - 1];
                count    = last.firstEntityIndex + last.countInChunk;
            }
            world.layer.ResizeUninitialized(count);

            var layerIndices = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var xMinMaxs     = new NativeArray<float2>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var colliderAoS  = new NativeArray<ColliderAoSData>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var p1 = new Part1FromFilteredQueryJob
            {
                colliderAoS        = colliderAoS,
                layer              = world.layer,
                layerIndices       = layerIndices,
                xMinMaxs           = xMinMaxs,
                filteredChunkCache = filteredChunkCache,
                typeGroup          = handles
            };
            for (int i = 0; i < filteredChunkCache.Length; i++)
            {
                p1.Execute(i);
            }

            var sourceArchetypeIndices = new NativeList<short>(count, Allocator.Temp);
            new Part1bBuildArchetypesFromCacheJob
            {
                chunks                           = filteredChunkCache,
                archetypeBodyIndicesByBucket     = world.archetypeBodyIndicesByBucket,
                archetypeIndicesByBody           = world.archetypeIndicesByBody,
                archetypeIntervalTreesByBucket   = world.archetypeIntervalTreesByBucket,
                archetypes                       = world.archetypesInLayer,
                archetypeStartsAndCountsByBucket = world.archetypeStartsAndCountsByBucket,
                sourceArchetypeIndices           = sourceArchetypeIndices,
                bucketCountWithNaN               = IndexStrategies.BucketCountWithNaN(world.layer.count)
            }.Execute();

            new BuildCollisionLayerInternal.Part2Job
            {
                layer        = world.layer,
                layerIndices = layerIndices
            }.Execute();

            var p3 = new BuildCollisionLayerInternal.Part3Job
            {
                layerIndices       = layerIndices,
                unsortedSrcIndices = world.layer.srcIndices.AsArray()
            };
            for (int i = 0; i < world.layer.count; i++)
            {
                p3.Execute(i);
            }

            var p4 = new BuildCollisionLayerInternal.Part4Job
            {
                bucketStartAndCounts = world.layer.bucketStartsAndCounts.AsArray(),
                trees                = world.layer.intervalTrees.AsArray(),
                unsortedSrcIndices   = world.layer.srcIndices.AsArray(),
                xMinMaxs             = xMinMaxs
            };
            for (int i = 0; i < world.layer.bucketCount; i++)
            {
                p4.Execute(i);
            }

            var p5 = new Part5FromAoSJob
            {
                colliderAoS = colliderAoS,
                layer       = world.layer,
            };
            for (int i = 0; i < world.layer.count; i++)
            {
                p5.Execute(i);
            }

            var p6 = new Part6BuildBucketArchetypesJob
            {
                archetypeBodyIndicesByBucket     = world.archetypeBodyIndicesByBucket.AsArray(),
                archetypeIndicesByBody           = world.archetypeIndicesByBody.AsArray(),
                archetypeIntervalTreesByBucket   = world.archetypeIntervalTreesByBucket.AsArray(),
                archetypes                       = world.archetypesInLayer.AsArray(),
                archetypeStartsAndCountsByBucket = world.archetypeStartsAndCountsByBucket.AsArray(),
                sourceArchetypeIndices           = sourceArchetypeIndices.AsArray(),
                layer                            = world.layer,
            };
            var bucketCount = IndexStrategies.BucketCountWithNaN(world.layer.count);
            for (int i = 0; i < bucketCount; i++)
            {
                p6.Execute(i);
            }
        }

        #region Eytzinger Interval Tree

        //   Unless otherwise specified, the following functions are C# adaptations of Paul-Virak Khuong and Pat Morin's
        //   Eytzinger Array builder: https://github.com/patmorin/arraylayout/blob/master/src/eytzinger_array.h
        //   This code is licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0)
        private static void BuildEytzingerIntervalTree(NativeArray<IntervalTreeNode> tree, NativeArray<int> bodyIndices, NativeArray<float> xmins, NativeArray<float> xmaxs)
        {
            var builder = new EytzingerIntervalTreeBuilder(tree, bodyIndices, xmins, xmaxs);
            builder.Build();
        }

        private struct EytzingerIntervalTreeBuilder
        {
            private NativeArray<IntervalTreeNode> nodesToPopulate;
            private NativeArray<int>              bodyIndices;
            private NativeArray<float>            xmins;
            private NativeArray<float>            xmaxs;

            public EytzingerIntervalTreeBuilder(NativeArray<IntervalTreeNode> tree, NativeArray<int> bodyIndices, NativeArray<float> xmins, NativeArray<float> xmaxs)
            {
                this.nodesToPopulate = tree;
                this.bodyIndices     = bodyIndices;
                this.xmins           = xmins;
                this.xmaxs           = xmaxs;
            }

            public void Build()
            {
                BuildEytzingerIntervalTreeRecurse(0, 0);

                PatchSubtreeMax();
            }

            private int BuildEytzingerIntervalTreeRecurse(int archetypeRelativeIndex, uint treeIndex)
            {
                // It is for this condition that we need treeIndex to be a uint, which can store 2 * (int.MaxValue - 1) + 2 without overflow.
                // If code reaches beyond this point, it is safe to cast treeIndex to an int.
                if (treeIndex >= nodesToPopulate.Length)
                    return archetypeRelativeIndex;

                archetypeRelativeIndex = BuildEytzingerIntervalTreeRecurse(archetypeRelativeIndex, 2 * treeIndex + 1);

                var bodyIndex                   = bodyIndices[archetypeRelativeIndex];
                var min                         = xmins[bodyIndex];
                var max                         = xmaxs[bodyIndex];
                nodesToPopulate[(int)treeIndex] = new IntervalTreeNode
                {
                    xmin                    = min,
                    xmax                    = max,
                    subtreeXmax             = max,
                    bucketRelativeBodyIndex = bodyIndex
                };
                archetypeRelativeIndex++;

                archetypeRelativeIndex = BuildEytzingerIntervalTreeRecurse(archetypeRelativeIndex, 2 * treeIndex + 2);

                return archetypeRelativeIndex;
            }

            // This function is unique to Latios Framework
            private void PatchSubtreeMax()
            {
                for (int i = nodesToPopulate.Length - 1; i > 0; i--)
                {
                    var node                     = nodesToPopulate[i];
                    var parentIndex              = (i - 1) / 2;
                    var parent                   = nodesToPopulate[parentIndex];
                    parent.subtreeXmax           = math.max(parent.subtreeXmax, node.xmax);
                    nodesToPopulate[parentIndex] = parent;
                }
            }
        }

        #endregion
    }
}

