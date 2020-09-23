using Latios.PhysicsEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace OptimizationAdventures
{
    internal static class BuildCollisionLayerP4
    {
        public struct LayerChunkTypeGroup
        {
            [ReadOnly] public ComponentTypeHandle<Collider>     collider;
            [ReadOnly] public ComponentTypeHandle<Translation>  translation;
            [ReadOnly] public ComponentTypeHandle<Rotation>     rotation;
            [ReadOnly] public ComponentTypeHandle<PhysicsScale> scale;
            [ReadOnly] public ComponentTypeHandle<Parent>       parent;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> localToWorld;
            [ReadOnly] public EntityTypeHandle                  entity;
        }

        public static LayerChunkTypeGroup BuildLayerChunkTypeGroup(ComponentSystemBase system)
        {
            LayerChunkTypeGroup result = new LayerChunkTypeGroup
            {
                collider     = system.GetComponentTypeHandle<Collider>(true),
                translation  = system.GetComponentTypeHandle<Translation>(true),
                rotation     = system.GetComponentTypeHandle<Rotation>(true),
                scale        = system.GetComponentTypeHandle<PhysicsScale>(true),
                parent       = system.GetComponentTypeHandle<Parent>(true),
                localToWorld = system.GetComponentTypeHandle<LocalToWorld>(true),
                entity       = system.GetEntityTypeHandle()
            };
            return result;
        }

        #region Jobs
        //Parallel
        //Calculate RigidTransform, AABB, and target bucket. Write the targetBucket as the layerIndex
        [BurstCompile]
        public struct Part1FromQueryJob : IJobChunk
        {
            public TestCollisionLayer             layer;
            public NativeArray<int>               layerIndices;
            public NativeArray<RigidTransform>    rigidTransforms;
            public NativeArray<Aabb>              aabbs;
            [ReadOnly] public LayerChunkTypeGroup typeGroup;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                if (chunk.Has(typeGroup.parent))
                {
                    ProcessMaybeScaled(chunk, firstEntityIndex);
                }
                else
                {
                    bool t = chunk.Has(typeGroup.translation);
                    bool r = chunk.Has(typeGroup.rotation);
                    bool s = chunk.Has(typeGroup.scale);
                    if (t & r)
                        ProcessTR(chunk, firstEntityIndex);
                    else if (t)
                        ProcessT(chunk, firstEntityIndex);
                    else if (r)
                        ProcessR(chunk, firstEntityIndex);
                    else if (s)
                        ProcessMaybeScaled(chunk, firstEntityIndex);

                    //If a chunk only has localToWorld, it is assumed to be static.
                    //In that case, the collider should have any scale pre-baked.
                    //However, we still have to extract the rigidTransform from a LTW with scale pre-applied
                    else
                        ProcessMaybeScaled(chunk, firstEntityIndex);
                }
            }

            private void ProcessMaybeScaled(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var ltws           = chunk.GetNativeArray(typeGroup.localToWorld);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var            ltw            = ltws[i];
                    quaternion     rot            = quaternion.LookRotationSafe(ltw.Forward, ltw.Up);
                    float3         pos            = ltw.Position;
                    RigidTransform rigidTransform = new RigidTransform(rot, pos);
                    ProcessEntity(firstEntityIndex + i, chunkColliders[i], rigidTransform);
                }
            }
            private void ProcessT(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var trans          = chunk.GetNativeArray(typeGroup.translation);
                for (int i = 0; i < chunk.Count; i++)
                {
                    RigidTransform rigidTransform = new RigidTransform(quaternion.identity, trans[i].Value);
                    ProcessEntity(firstEntityIndex + i, chunkColliders[i], rigidTransform);
                }
            }
            private void ProcessR(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var rots           = chunk.GetNativeArray(typeGroup.rotation);
                for (int i = 0; i < chunk.Count; i++)
                {
                    RigidTransform rigidTransform = new RigidTransform(rots[i].Value, float3.zero);
                    ProcessEntity(firstEntityIndex + i, chunkColliders[i], rigidTransform);
                }
            }
            private void ProcessTR(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var trans          = chunk.GetNativeArray(typeGroup.translation);
                var rots           = chunk.GetNativeArray(typeGroup.rotation);
                for (int i = 0; i < chunk.Count; i++)
                {
                    RigidTransform rigidTransform = new RigidTransform(rots[i].Value, trans[i].Value);
                    ProcessEntity(firstEntityIndex + i, chunkColliders[i], rigidTransform);
                }
            }

            private void ProcessEntity(int index, Collider collider, RigidTransform rigidTransform)
            {
                Aabb aabb              = Physics.CalculateAabb(collider, rigidTransform);
                rigidTransforms[index] = rigidTransform;
                aabbs[index]           = aabb;

                int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

                if (math.all(minBucket == maxBucket))
                {
                    layerIndices[index] = (minBucket.x * layer.worldSubdivisionsPerAxis.y + minBucket.y) * layer.worldSubdivisionsPerAxis.z + minBucket.z;
                }
                else
                {
                    layerIndices[index] = layer.bucketStartsAndCounts.Length - 1;
                }
            }
        }

        //Single
        //Count total in each bucket and assign global array position to layerIndex
        [BurstCompile]
        public struct Part2Job : IJob
        {
            public TestCollisionLayer layer;
            public NativeArray<int>   layerIndices;

            public void Execute()
            {
                NativeArray<int> countsPerBucket = new NativeArray<int>(layer.bucketStartsAndCounts.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int i = 0; i < layerIndices.Length; i++)
                {
                    countsPerBucket[layerIndices[i]] = countsPerBucket[layerIndices[i]] + 1;
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
                    int bucketIndex              = layerIndices[i];
                    layerIndices[i]              = layer.bucketStartsAndCounts[bucketIndex].x + countsPerBucket[bucketIndex];
                    countsPerBucket[bucketIndex] = countsPerBucket[bucketIndex] + 1;
                }
            }
        }

        //Parallel
        //Copy ECS data to bucket, correct collider scale, and split AABB
        [BurstCompile]
        public struct Part3FromQueryJob : IJobChunk
        {
            [NativeDisableParallelForRestriction]
            public TestCollisionLayer layer;

            [DeallocateOnJobCompletion]
            [ReadOnly]
            public NativeArray<int> layerIndices;

            [DeallocateOnJobCompletion]
            [ReadOnly]
            public NativeArray<RigidTransform> rigidTranforms;

            [DeallocateOnJobCompletion]
            [ReadOnly]
            public NativeArray<Aabb> aabbs;

            [ReadOnly]
            public LayerChunkTypeGroup typeGroup;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                if (chunk.Has(typeGroup.scale))
                    ProcessScale(chunk, firstEntityIndex);
                else
                    ProcessNoScale(chunk, firstEntityIndex);
            }

            private void ProcessNoScale(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var entities       = chunk.GetNativeArray(typeGroup.entity);
                for (int i = 0; i < chunk.Count; i++)
                {
                    ProcessEntity(entities[i], firstEntityIndex + i, chunkColliders[i]);
                }
            }
            private void ProcessScale(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var entities       = chunk.GetNativeArray(typeGroup.entity);
                var scales         = chunk.GetNativeArray(typeGroup.scale);
                for (int i = 0; i < chunk.Count; i++)
                {
                    ProcessEntity(entities[i], firstEntityIndex + i, Physics.ScaleCollider(chunkColliders[i], scales[i]));
                }
            }
            private void ProcessEntity(Entity entity, int index, Collider collider)
            {
                RigidTransform transform = rigidTranforms[index];
                Aabb           aabb      = aabbs[index];
                int            spot      = layerIndices[index];
                layer.xmins[spot]        = aabb.min.x;
                layer.xmaxs[spot]        = aabb.max.x;
                layer.yzminmaxs[spot]    = new float4(aabb.min.yz, aabb.max.yz);
                ColliderBody body        = new ColliderBody
                {
                    entity    = entity,
                    transform = transform,
                    collider  = collider
                };
                layer.bodies[spot] = body;
            }
        }

        //Parallel
        //Sort buckets
        [BurstCompile]
        public struct Part4Job : IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            public TestCollisionLayer layer;

            public void Execute(int index)
            {
                var tempArray  = new NativeArray<int>(0, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var slices     = layer.GetBucketSlices(index);
                var startIndex = layer.bucketStartsAndCounts[index].x;
                RadixSortBucket(slices, tempArray.Slice(), startIndex);
            }
        }

        //Parallel
        //Sort buckets
        [BurstCompile]
        public struct Part4JobBetter : IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            public TestCollisionLayer layer;

            public void Execute(int index)
            {
                var tempArray  = new NativeArray<int>(0, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var slices     = layer.GetBucketSlices(index);
                var startIndex = layer.bucketStartsAndCounts[index].x;
                UnitySortBucket(slices);
            }
        }

        //Parallel
        //Sort buckets
        /*[BurstCompile]
           public struct Part4JobBetter : IJobParallelFor
           {
            [NativeDisableParallelForRestriction]
            public TestCollisionLayer layer;

            public void Execute(int index)
            {
                var tempArray  = new NativeArray<int>(0, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var slices     = layer.GetBucketSlices(index);
                var startIndex = layer.bucketStartsAndCounts[index].x;
                RadixSortBucket2(slices, tempArray.Slice(), startIndex);
            }
           }*/

        //Parallel
        //Sort buckets
        [BurstCompile]
        public struct Part4JobNew : IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            public TestCollisionLayer layer;

            public void Execute(int index)
            {
                var tempArray  = new NativeArray<int>(0, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var slices     = layer.GetBucketSlices(index);
                var startIndex = layer.bucketStartsAndCounts[index].x;
                RadixSortBucket2(slices, tempArray.Slice(), startIndex);
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

        private static void UnitySortBucket(BucketSlices slices)
        {
            var ranks = new NativeArray<Ranker>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < slices.count; i++)
            {
                ranks[i] = new Ranker
                {
                    index = i,
                    key   = slices.xmins[i]
                };
            }

            ranks.Sort();

            NativeArray<float>        xminBackup     = new NativeArray<float>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<float>        xmaxBackup     = new NativeArray<float>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<float4>       yzminmaxBackup = new NativeArray<float4>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<ColliderBody> bodyBackup     = new NativeArray<ColliderBody>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            slices.xmins.CopyTo(xminBackup);
            slices.xmaxs.CopyTo(xmaxBackup);
            slices.yzminmaxs.CopyTo(yzminmaxBackup);
            slices.bodies.CopyTo(bodyBackup);

            for (int i = 0; i < slices.count; i++)
            {
                int src             = ranks[i].index;
                slices.xmins[i]     = xminBackup[src];
                slices.xmaxs[i]     = xmaxBackup[src];
                slices.yzminmaxs[i] = yzminmaxBackup[src];
                slices.bodies[i]    = bodyBackup[src];
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

        private static void calculatePrefixSum(NativeArray<int> counts, NativeArray<int> sums)
        {
            sums[0] = 0;
            for (int i = 0; i < counts.Length - 1; i++)
            {
                sums[i + 1] = sums[i] + counts[i];
            }
        }

        public static void RadixSortBucket(BucketSlices slices, NativeSlice<int> remapSrcIndices, int sliceSrcStartIndex)
        {
            if (slices.count <= 0)
                return;

            NativeArray<int> counts1    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts2    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts3    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts4    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> prefixSum1 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum2 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum3 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum4 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<Indexer> frontArray = new NativeArray<Indexer>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<Indexer> backArray  = new NativeArray<Indexer>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<float>        xminBackup     = new NativeArray<float>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<float>        xmaxBackup     = new NativeArray<float>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<float4>       yzminmaxBackup = new NativeArray<float4>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<ColliderBody> bodyBackup     = new NativeArray<ColliderBody>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            slices.xmins.CopyTo(xminBackup);
            slices.xmaxs.CopyTo(xmaxBackup);
            slices.yzminmaxs.CopyTo(yzminmaxBackup);
            slices.bodies.CopyTo(bodyBackup);

            //Counts
            for (int i = 0; i < slices.count; i++)
            {
                var keys            = Keys(slices.xmins[i]);
                counts1[keys.byte1] = counts1[keys.byte1] + 1;
                counts2[keys.byte2] = counts2[keys.byte2] + 1;
                counts3[keys.byte3] = counts3[keys.byte3] + 1;
                counts4[keys.byte4] = counts4[keys.byte4] + 1;
                frontArray[i]       = new Indexer { key = keys, index = i };
            }

            //Sums
            calculatePrefixSum(counts1, prefixSum1);
            calculatePrefixSum(counts2, prefixSum2);
            calculatePrefixSum(counts3, prefixSum3);
            calculatePrefixSum(counts4, prefixSum4);

            for (int i = 0; i < slices.count; i++)
            {
                byte key        = frontArray[i].key.byte1;
                int  dest       = prefixSum1[key];
                backArray[dest] = frontArray[i];
                prefixSum1[key] = prefixSum1[key] + 1;
            }

            for (int i = 0; i < slices.count; i++)
            {
                byte key         = backArray[i].key.byte2;
                int  dest        = prefixSum2[key];
                frontArray[dest] = backArray[i];
                prefixSum2[key]  = prefixSum2[key] + 1;
            }

            for (int i = 0; i < slices.count; i++)
            {
                byte key        = frontArray[i].key.byte3;
                int  dest       = prefixSum3[key];
                backArray[dest] = frontArray[i];
                prefixSum3[key] = prefixSum3[key] + 1;
            }

            if (remapSrcIndices.Length > 0)
            {
                for (int i = 0; i < slices.count; i++)
                {
                    byte key               = backArray[i].key.byte4;
                    int  dest              = prefixSum4[key];
                    int  src               = backArray[i].index;
                    remapSrcIndices[dest]  = src + sliceSrcStartIndex;
                    slices.xmins[dest]     = xminBackup[src];
                    slices.xmaxs[dest]     = xmaxBackup[src];
                    slices.yzminmaxs[dest] = yzminmaxBackup[src];
                    slices.bodies[dest]    = bodyBackup[src];
                    prefixSum4[key]        = prefixSum4[key] + 1;
                }
            }
            else
            {
                for (int i = 0; i < slices.count; i++)
                {
                    byte key               = backArray[i].key.byte4;
                    int  dest              = prefixSum4[key];
                    int  src               = backArray[i].index;
                    slices.xmins[dest]     = xminBackup[src];
                    slices.xmaxs[dest]     = xmaxBackup[src];
                    slices.yzminmaxs[dest] = yzminmaxBackup[src];
                    slices.bodies[dest]    = bodyBackup[src];
                    prefixSum4[key]        = prefixSum4[key] + 1;
                }
            }
        }

        public static void RadixSortBucket2(BucketSlices slices, NativeSlice<int> remapSrcIndices, int sliceSrcStartIndex)
        {
            if (slices.count <= 0)
                return;

            NativeArray<int> counts1    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts2    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts3    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts4    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> prefixSum1 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum2 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum3 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum4 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<Indexer> frontArray = new NativeArray<Indexer>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<Indexer> backArray  = new NativeArray<Indexer>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<float>        xminBackup     = new NativeArray<float>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<float>        xmaxBackup     = new NativeArray<float>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<float4>       yzminmaxBackup = new NativeArray<float4>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<ColliderBody> bodyBackup     = new NativeArray<ColliderBody>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            slices.xmins.CopyTo(xminBackup);
            slices.xmaxs.CopyTo(xmaxBackup);
            slices.yzminmaxs.CopyTo(yzminmaxBackup);
            slices.bodies.CopyTo(bodyBackup);

            //Counts
            for (int i = 0; i < slices.count; i++)
            {
                var keys            = Keys(slices.xmins[i]);
                counts1[keys.byte1] = counts1[keys.byte1] + 1;
                counts2[keys.byte2] = counts2[keys.byte2] + 1;
                counts3[keys.byte3] = counts3[keys.byte3] + 1;
                counts4[keys.byte4] = counts4[keys.byte4] + 1;
                frontArray[i]       = new Indexer { key = keys, index = i };
            }

            //Sums
            calculatePrefixSum(counts1, prefixSum1);
            calculatePrefixSum(counts2, prefixSum2);
            calculatePrefixSum(counts3, prefixSum3);
            calculatePrefixSum(counts4, prefixSum4);

            for (int i = 0; i < slices.count; i++)
            {
                byte key        = frontArray[i].key.byte1;
                int  dest       = prefixSum1[key];
                backArray[dest] = frontArray[i];
                prefixSum1[key] = prefixSum1[key] + 1;
            }

            for (int i = 0; i < slices.count; i++)
            {
                byte key         = backArray[i].key.byte2;
                int  dest        = prefixSum2[key];
                frontArray[dest] = backArray[i];
                prefixSum2[key]  = prefixSum2[key] + 1;
            }

            for (int i = 0; i < slices.count; i++)
            {
                byte key        = frontArray[i].key.byte3;
                int  dest       = prefixSum3[key];
                backArray[dest] = frontArray[i];
                prefixSum3[key] = prefixSum3[key] + 1;
            }

            for (int i = 0; i < slices.count; i++)
            {
                byte key         = backArray[i].key.byte4;
                int  dest        = prefixSum4[key];
                frontArray[dest] = backArray[i];
                prefixSum4[key]  = prefixSum4[key] + 1;
            }

            if (remapSrcIndices.Length > 0)
            {
                for (int i = 0; i < slices.count; i++)
                {
                    int src             = frontArray[i].index;
                    remapSrcIndices[i]  = src + sliceSrcStartIndex;
                    slices.xmins[i]     = xminBackup[src];
                    slices.xmaxs[i]     = xmaxBackup[src];
                    slices.yzminmaxs[i] = yzminmaxBackup[src];
                    slices.bodies[i]    = bodyBackup[src];
                }
            }
            else
            {
                for (int i = 0; i < slices.count; i++)
                {
                    int src             = frontArray[i].index;
                    slices.xmins[i]     = xminBackup[src];
                    slices.xmaxs[i]     = xmaxBackup[src];
                    slices.yzminmaxs[i] = yzminmaxBackup[src];
                    slices.bodies[i]    = bodyBackup[src];
                }
            }
        }

        public unsafe static void RadixSortBucket3(BucketSlices slices, NativeSlice<int> remapSrcIndices, int sliceSrcStartIndex)
        {
            if (slices.count <= 0)
                return;

            NativeArray<int> counts1    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts2    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts3    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts4    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> prefixSum1 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum2 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum3 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum4 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<Indexer> frontArray = new NativeArray<Indexer>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<Indexer> backArray  = new NativeArray<Indexer>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<float>        xminBackup     = new NativeArray<float>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<float>        xmaxBackup     = new NativeArray<float>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<float4>       yzminmaxBackup = new NativeArray<float4>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<ColliderBody> bodyBackup     = new NativeArray<ColliderBody>(slices.count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            slices.xmins.CopyTo(xminBackup);
            //slices.xmaxs.CopyTo(xmaxBackup);
            //slices.yzminmaxs.CopyTo(yzminmaxBackup);
            //slices.bodies.CopyTo(bodyBackup);

            //Counts
            for (int i = 0; i < slices.count; i++)
            {
                var keys            = Keys(slices.xmins[i]);
                counts1[keys.byte1] = counts1[keys.byte1] + 1;
                counts2[keys.byte2] = counts2[keys.byte2] + 1;
                counts3[keys.byte3] = counts3[keys.byte3] + 1;
                counts4[keys.byte4] = counts4[keys.byte4] + 1;
                frontArray[i]       = new Indexer { key = keys, index = i };
            }

            //Sums
            calculatePrefixSum(counts1, prefixSum1);
            calculatePrefixSum(counts2, prefixSum2);
            calculatePrefixSum(counts3, prefixSum3);
            calculatePrefixSum(counts4, prefixSum4);

            for (int i = 0; i < slices.count; i++)
            {
                byte key        = frontArray[i].key.byte1;
                int  dest       = prefixSum1[key];
                backArray[dest] = frontArray[i];
                prefixSum1[key] = prefixSum1[key] + 1;
            }

            for (int i = 0; i < slices.count; i++)
            {
                byte key         = backArray[i].key.byte2;
                int  dest        = prefixSum2[key];
                frontArray[dest] = backArray[i];
                prefixSum2[key]  = prefixSum2[key] + 1;
            }

            for (int i = 0; i < slices.count; i++)
            {
                byte key        = frontArray[i].key.byte3;
                int  dest       = prefixSum3[key];
                backArray[dest] = frontArray[i];
                prefixSum3[key] = prefixSum3[key] + 1;
            }

            for (int i = 0; i < slices.count; i++)
            {
                byte key         = backArray[i].key.byte4;
                int  dest        = prefixSum4[key];
                frontArray[dest] = backArray[i];
                prefixSum4[key]  = prefixSum4[key] + 1;
            }

            /*if (remapSrcIndices.Length > 0)
               {
               for (int i = 0; i < slices.count; i++)
               {
                 int src = frontArray[i].index;
                 remapSrcIndices[i] = src + sliceSrcStartIndex;
                 slices.xmins[i] = xminBackup[src];
                 slices.xmaxs[i] = xmaxBackup[src];
                 slices.yzminmaxs[i] = yzminmaxBackup[src];
                 slices.bodies[i] = bodyBackup[src];
               }
               }
               else
               {
               for (int i = 0; i < slices.count; i++)
               {
                 int src = frontArray[i].index;
                 slices.xmins[i] = xminBackup[src];
                 slices.xmaxs[i] = xmaxBackup[src];
                 slices.yzminmaxs[i] = yzminmaxBackup[src];
                 slices.bodies[i] = bodyBackup[src];
               }
               }*/
            for (int i = 0; i < slices.count; i++)
            {
                int src         = frontArray[i].index;
                slices.xmins[i] = xminBackup[src];
            }
        }
        #endregion
    }
}

