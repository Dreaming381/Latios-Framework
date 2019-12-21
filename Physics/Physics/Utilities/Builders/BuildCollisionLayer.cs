using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

//Todo: Switch to IJobChunks to optionally grab translation and rotation
namespace Latios.PhysicsEngine
{
    public static partial class Physics
    {
        public struct LayerChunkTypeGroup
        {
            [ReadOnly] public ArchetypeChunkComponentType<Collider>     collider;
            [ReadOnly] public ArchetypeChunkComponentType<Translation>  translation;
            [ReadOnly] public ArchetypeChunkComponentType<Rotation>     rotation;
            [ReadOnly] public ArchetypeChunkComponentType<PhysicsScale> scale;
            [ReadOnly] public ArchetypeChunkComponentType<Parent>       parent;
            [ReadOnly] public ArchetypeChunkComponentType<LocalToWorld> localToWorld;
            [ReadOnly] public ArchetypeChunkEntityType                  entity;
        }

        public static LayerChunkTypeGroup BuildLayerChunkTypeGroup(ComponentSystemBase system)
        {
            LayerChunkTypeGroup result = new LayerChunkTypeGroup
            {
                collider     = system.GetArchetypeChunkComponentType<Collider>(true),
                translation  = system.GetArchetypeChunkComponentType<Translation>(true),
                rotation     = system.GetArchetypeChunkComponentType<Rotation>(true),
                scale        = system.GetArchetypeChunkComponentType<PhysicsScale>(true),
                parent       = system.GetArchetypeChunkComponentType<Parent>(true),
                localToWorld = system.GetArchetypeChunkComponentType<LocalToWorld>(true),
                entity       = system.GetArchetypeChunkEntityType()
            };
            return result;
        }

        //Todo: Acount for simulation bodies
        //Warning: Allocates
        public static EntityQueryDesc BuildCollisionLayerEntityQueryDesc(EntityQueryDesc description)
        {
            return BuildCollisionLayerEntityQueryDesc(description, out _);
        }

        public static EntityQueryDesc BuildCollisionLayerEntityQueryDesc(EntityQueryDesc description, out EntityQueryDesc outDescriptionWithPreservedRW)
        {
            NativeList<ComponentType> any  = new NativeList<ComponentType>(Allocator.TempJob);
            NativeList<ComponentType> all  = new NativeList<ComponentType>(Allocator.TempJob);
            NativeList<ComponentType> none = new NativeList<ComponentType>(Allocator.TempJob);
            if (description != null)
            {
                AddRange(any,  description.Any);
                AddRange(all,  description.All);
                AddRange(none, description.None);
            }
            AddOrCombine<Collider>(     all);
            AddOrCombine<LocalToWorld>( all);

            outDescriptionWithPreservedRW = new EntityQueryDesc
            {
                All  = all.ToArray(),
                Any  = any.ToArray(),
                None = none.ToArray()
            };

            ForceReadOnly(all);
            ForceReadOnly(any);
            ForceReadOnly(none);

            EntityQueryDesc result = new EntityQueryDesc
            {
                All  = all.ToArray(),
                Any  = any.ToArray(),
                None = none.ToArray()
            };
            any.Dispose();
            all.Dispose();
            none.Dispose();
            return result;

            void AddRange(NativeList<ComponentType> types, ComponentType[] newTypes)
            {
                if (newTypes != null)
                {
                    foreach (var t in newTypes)
                        types.Add(t);
                }
            }

            void AddOrCombine<T>(NativeList<ComponentType> types)
            {
                ComponentType ctypeRW = ComponentType.ReadWrite<T>();
                ComponentType ctypeRO = ComponentType.ReadOnly<T>();
                if (!types.Contains(ctypeRW) && !types.Contains(ctypeRO))
                {
                    types.Add(ctypeRO);
                }
            }

            void ForceReadOnly(NativeList<ComponentType> types)
            {
                for (int i = 0; i < types.Length; i++)
                {
                    ComponentType t  = types[i];
                    t.AccessModeType = ComponentType.AccessMode.ReadOnly;
                    types[i]         = t;
                }
            }
        }

        public static JobHandle BuildCollisionLayer(EntityQuery query,
                                                    LayerChunkTypeGroup typeGroup,
                                                    CollisionLayerSettings settings,
                                                    Allocator allocator,
                                                    JobHandle inputDeps,
                                                    out CollisionLayer collisionLayer)
        {
            int count                                   = query.CalculateEntityCount();
            collisionLayer                              = new CollisionLayer(count, settings, allocator);
            NativeArray<int>            layerIndices    = new NativeArray<int>(collisionLayer.Count, allocator, NativeArrayOptions.UninitializedMemory);
            NativeArray<AABB>           aabbs           = new NativeArray<AABB>(collisionLayer.Count, allocator, NativeArrayOptions.UninitializedMemory);
            NativeArray<RigidTransform> rigidTransforms = new NativeArray<RigidTransform>(collisionLayer.Count, allocator, NativeArrayOptions.UninitializedMemory);

            JobHandle jh = new BuildCollisionLayerPart1
            {
                layer           = collisionLayer,
                layerIndices    = layerIndices,
                aabbs           = aabbs,
                rigidTransforms = rigidTransforms,
                typeGroup       = typeGroup
            }.Schedule(query, inputDeps);

            jh = new BuildCollisionLayerPart2
            {
                layer        = collisionLayer,
                layerIndices = layerIndices
            }.Schedule(jh);

            jh = new BuildCollisionLayerPart3
            {
                layer          = collisionLayer,
                layerIndices   = layerIndices,
                aabbs          = aabbs,
                rigidTranforms = rigidTransforms,
                typeGroup      = typeGroup
            }.Schedule(query, jh);
            //layerIndices, aabbs, and rigidTransforms are deallocated in Part3

            jh = new BuildCollisionLayerPart4
            {
                layer = collisionLayer
            }.Schedule(collisionLayer.BucketCount, 1, jh);

            return jh;
        }

        //Parallel
        //Calculate RigidTransform, AABB, and target bucket. Write the targetBucket as the layerIndex
        [BurstCompile]
        private struct BuildCollisionLayerPart1 : IJobChunk
        {
            public CollisionLayer                 layer;
            public NativeArray<int>               layerIndices;
            public NativeArray<RigidTransform>    rigidTransforms;
            public NativeArray<AABB>              aabbs;
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
                    else
                        ProcessStaticNoScale(chunk, firstEntityIndex);
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
            private void ProcessStaticNoScale(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var ltws           = chunk.GetNativeArray(typeGroup.localToWorld);
                for (int i = 0; i < chunk.Count; i++)
                {
                    RigidTransform rigidTransform = new RigidTransform(ltws[i].Value);
                    ProcessEntity(firstEntityIndex + i, chunkColliders[i], rigidTransform);
                }
            }

            private void ProcessEntity(int index, Collider collider, RigidTransform rigidTransform)
            {
                AABB aabb              = collider.CalculateAABB(rigidTransform);
                rigidTransforms[index] = rigidTransform;
                aabbs[index]           = aabb;

                //float4 xyminmax      = new float4(aabb.min.yz, aabb.max.yz);
                //int4   minMaxBuckets = math.int4(math.floor((xyminmax - layer.worldMinYZ.xyxy) / layer.worldStrideYZ.xyxy));
                //minMaxBuckets        = math.clamp(minMaxBuckets, 0, layer.worldBucketCountPerDimensionYZ.xyxy - 1);

                int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                minBucket      = math.clamp(minBucket, 0, layer.worldBucketCountPerAxis - 1);
                maxBucket      = math.clamp(maxBucket, 0, layer.worldBucketCountPerAxis - 1);

                //if (math.all(minMaxBuckets.xy == minMaxBuckets.zw))
                if (math.all(minBucket == maxBucket))
                {
                    layerIndices[index] = (minBucket.x * layer.worldBucketCountPerAxis.y + minBucket.y) * layer.worldBucketCountPerAxis.z + minBucket.z;
                }
                else
                {
                    layerIndices[index] = layer.bucketStartsAndCounts.Length - 1;
                }
            }
        }

        //Single
        //Count total in each bucket and assign global array position to layerIndex
        [BurstCompile(Debug = true)]
        private struct BuildCollisionLayerPart2 : IJob
        {
            public CollisionLayer   layer;
            public NativeArray<int> layerIndices;

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

        //Todo: Variants for different layer types.

        //Parallel
        //Assign to layer
        [BurstCompile]
        private struct BuildCollisionLayerPart3 : IJobChunk
        {
            [NativeDisableParallelForRestriction]
            public CollisionLayer layer;

            [DeallocateOnJobCompletion]
            [ReadOnly]
            public NativeArray<int> layerIndices;

            [DeallocateOnJobCompletion]
            [ReadOnly]
            public NativeArray<RigidTransform> rigidTranforms;

            [DeallocateOnJobCompletion]
            [ReadOnly]
            public NativeArray<AABB> aabbs;

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
                    ProcessEntity(entities[i], firstEntityIndex + i, ScaleCollider(chunkColliders[i], scales[i]));
                }
            }
            private void ProcessEntity(Entity entity, int index, Collider collider)
            {
                RigidTransform transform = rigidTranforms[index];
                AABB           aabb      = aabbs[index];
                int            spot      = layerIndices[index];
                layer.xmins[spot]        = aabb.min.x;
                layer.xmaxs[spot]        = aabb.max.x;
                layer.yzminmaxs[spot]    = new float4(aabb.min.yz, aabb.max.yz);
                ColliderBody body         = new ColliderBody
                {
                    entity    = entity,
                    transform = transform,
                    collider  = collider
                };
                layer.bodies[spot] = body;
            }
        }

        //Parallel
        //Sort each bucket
        [BurstCompile(Debug = true)]
        private struct BuildCollisionLayerPart4 : IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            public CollisionLayer layer;

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

            private UintAsBytes Keys(float val)
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

            private void calculatePrefixSum(NativeArray<int> counts, NativeArray<int> sums)
            {
                sums[0] = 0;
                for (int i = 0; i < counts.Length - 1; i++)
                {
                    sums[i + 1] = sums[i] + counts[i];
                }
            }

            public void RadixSortBucket(int bucketIndex)
            {
                int countInBucket = layer.bucketStartsAndCounts[bucketIndex].y;
                int bucketStart   = layer.bucketStartsAndCounts[bucketIndex].x;
                if (countInBucket <= 0)
                    return;

                NativeArray<int> counts1    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
                NativeArray<int> counts2    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
                NativeArray<int> counts3    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
                NativeArray<int> counts4    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
                NativeArray<int> prefixSum1 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<int> prefixSum2 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<int> prefixSum3 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<int> prefixSum4 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                NativeArray<Indexer> frontArray = new NativeArray<Indexer>(countInBucket, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<Indexer> backArray  = new NativeArray<Indexer>(countInBucket, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                NativeArray<float>       xminBackup     = new NativeArray<float>(countInBucket, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<float>       xmaxBackup     = new NativeArray<float>(countInBucket, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<float4>      yzminmaxBackup = new NativeArray<float4>(countInBucket, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                NativeArray<ColliderBody> bodyBackup     = new NativeArray<ColliderBody>(countInBucket, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                NativeArray<float>.Copy(layer.xmins, bucketStart, xminBackup, 0, countInBucket);
                NativeArray<float>.Copy(layer.xmaxs, bucketStart, xmaxBackup, 0, countInBucket);
                NativeArray<float4>.Copy(layer.yzminmaxs, bucketStart, yzminmaxBackup, 0, countInBucket);
                NativeArray<ColliderBody>.Copy(layer.bodies, bucketStart, bodyBackup, 0, countInBucket);

                //Counts
                for (int i = 0; i < countInBucket; i++)
                {
                    var keys            = Keys(layer.xmins[bucketStart + i]);
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

                for (int i = 0; i < countInBucket; i++)
                {
                    byte key        = frontArray[i].key.byte1;
                    int  dest       = prefixSum1[key];
                    backArray[dest] = frontArray[i];
                    prefixSum1[key] = prefixSum1[key] + 1;
                }

                for (int i = 0; i < countInBucket; i++)
                {
                    byte key         = backArray[i].key.byte2;
                    int  dest        = prefixSum2[key];
                    frontArray[dest] = backArray[i];
                    prefixSum2[key]  = prefixSum2[key] + 1;
                }

                for (int i = 0; i < countInBucket; i++)
                {
                    byte key        = frontArray[i].key.byte3;
                    int  dest       = prefixSum3[key];
                    backArray[dest] = frontArray[i];
                    prefixSum3[key] = prefixSum3[key] + 1;
                }

                for (int i = 0; i < countInBucket; i++)
                {
                    byte key              = backArray[i].key.byte4;
                    int  dest             = prefixSum4[key] + bucketStart;
                    int  src              = backArray[i].index;
                    layer.xmins[dest]     = xminBackup[src];
                    layer.xmaxs[dest]     = xmaxBackup[src];
                    layer.yzminmaxs[dest] = yzminmaxBackup[src];
                    layer.bodies[dest]    = bodyBackup[src];
                    prefixSum4[key]       = prefixSum4[key] + 1;
                }
            }

            public void Execute(int index)
            {
                RadixSortBucket(index);
            }
        }
    }
}

