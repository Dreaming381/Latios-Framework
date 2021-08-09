using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Psyshock
{
    internal static class BuildCollisionLayerInternal
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

        public struct ColliderAoSData
        {
            public Collider       collider;
            public RigidTransform rigidTransform;
            public Aabb           aabb;
            public Entity         entity;
        }

        #region Jobs
        //Parallel
        //Calculate RigidTransform, AABB, and target bucket. Write the targetBucket as the layerIndex
        [BurstCompile]
        public struct Part1FromQueryJob : IJobEntityBatchWithIndex
        {
            public CollisionLayer                         layer;
            [NoAlias] public NativeArray<int>             layerIndices;
            [NoAlias] public NativeArray<ColliderAoSData> colliderAoS;
            [NoAlias] public NativeArray<float>           xmins;
            [ReadOnly] public LayerChunkTypeGroup         typeGroup;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                bool ltw = chunk.Has(typeGroup.localToWorld);
                bool p   = chunk.Has(typeGroup.parent);
                bool t   = chunk.Has(typeGroup.translation);
                bool r   = chunk.Has(typeGroup.rotation);
                bool s   = chunk.Has(typeGroup.scale);

                int mask  = math.select(0, 0x10, ltw);
                mask     += math.select(0, 0x8, p);
                mask     += math.select(0, 0x4, t);
                mask     += math.select(0, 0x2, r);
                mask     += math.select(0, 0x1, s);

                switch (mask)
                {
                    case 0x0: ProcessNoTransform(chunk, firstEntityIndex); break;
                    case 0x1: ProcessScale(chunk, firstEntityIndex); break;
                    case 0x2: ProcessRotation(chunk, firstEntityIndex); break;
                    case 0x3: ProcessRotationScale(chunk, firstEntityIndex); break;
                    case 0x4: ProcessTranslation(chunk, firstEntityIndex); break;
                    case 0x5: ProcessTranslationScale(chunk, firstEntityIndex); break;
                    case 0x6: ProcessTranslationRotation(chunk, firstEntityIndex); break;
                    case 0x7: ProcessTranslationRotationScale(chunk, firstEntityIndex); break;

                    case 0x8: ErrorCase(); break;
                    case 0x9: ErrorCase(); break;
                    case 0xa: ErrorCase(); break;
                    case 0xb: ErrorCase(); break;
                    case 0xc: ErrorCase(); break;
                    case 0xd: ErrorCase(); break;
                    case 0xe: ErrorCase(); break;
                    case 0xf: ErrorCase(); break;

                    case 0x10: ProcessLocalToWorld(chunk, firstEntityIndex); break;
                    case 0x11: ProcessScale(chunk, firstEntityIndex); break;
                    case 0x12: ProcessRotation(chunk, firstEntityIndex); break;
                    case 0x13: ProcessRotationScale(chunk, firstEntityIndex); break;
                    case 0x14: ProcessTranslation(chunk, firstEntityIndex); break;
                    case 0x15: ProcessTranslationScale(chunk, firstEntityIndex); break;
                    case 0x16: ProcessTranslationRotation(chunk, firstEntityIndex); break;
                    case 0x17: ProcessTranslationRotationScale(chunk, firstEntityIndex); break;

                    case 0x18: ProcessParent(chunk, firstEntityIndex); break;
                    case 0x19: ProcessParentScale(chunk, firstEntityIndex); break;
                    case 0x1a: ProcessParent(chunk, firstEntityIndex); break;
                    case 0x1b: ProcessParentScale(chunk, firstEntityIndex); break;
                    case 0x1c: ProcessParent(chunk, firstEntityIndex); break;
                    case 0x1d: ProcessParentScale(chunk, firstEntityIndex); break;
                    case 0x1e: ProcessParent(chunk, firstEntityIndex); break;
                    case 0x1f: ProcessParentScale(chunk, firstEntityIndex); break;

                    default: ErrorCase(); break;
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void ErrorCase()
            {
                throw new System.InvalidOperationException("BuildCollisionLayer.Part1FromQueryJob received an invalid EntityQuery");
            }

            private void ProcessNoTransform(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities  = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                for (int i = 0; i < chunk.Count; i++)
                {
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], chunkColliders[i], RigidTransform.identity);
                }
            }

            private void ProcessScale(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities  = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var chunkScales    = chunk.GetNativeArray(typeGroup.scale);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var collider = Physics.ScaleCollider(chunkColliders[i], chunkScales[i]);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], collider, RigidTransform.identity);
                }
            }

            private void ProcessRotation(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities  = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var chunkRotations = chunk.GetNativeArray(typeGroup.rotation);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var rigidTransform = new RigidTransform(chunkRotations[i].Value, float3.zero);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], chunkColliders[i], rigidTransform);
                }
            }

            private void ProcessRotationScale(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities  = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders = chunk.GetNativeArray(typeGroup.collider);
                var chunkRotations = chunk.GetNativeArray(typeGroup.rotation);
                var chunkScales    = chunk.GetNativeArray(typeGroup.scale);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var collider       = Physics.ScaleCollider(chunkColliders[i], chunkScales[i]);
                    var rigidTransform = new RigidTransform(chunkRotations[i].Value, float3.zero);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], collider, rigidTransform);
                }
            }

            private void ProcessTranslation(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities     = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders    = chunk.GetNativeArray(typeGroup.collider);
                var chunkTranslations = chunk.GetNativeArray(typeGroup.translation);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var rigidTransform = new RigidTransform(quaternion.identity, chunkTranslations[i].Value);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], chunkColliders[i], rigidTransform);
                }
            }

            private void ProcessTranslationScale(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities     = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders    = chunk.GetNativeArray(typeGroup.collider);
                var chunkTranslations = chunk.GetNativeArray(typeGroup.translation);
                var chunkScales       = chunk.GetNativeArray(typeGroup.scale);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var collider       = Physics.ScaleCollider(chunkColliders[i], chunkScales[i]);
                    var rigidTransform = new RigidTransform(quaternion.identity, chunkTranslations[i].Value);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], collider, rigidTransform);
                }
            }

            private void ProcessTranslationRotation(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities     = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders    = chunk.GetNativeArray(typeGroup.collider);
                var chunkTranslations = chunk.GetNativeArray(typeGroup.translation);
                var chunkRotations    = chunk.GetNativeArray(typeGroup.rotation);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var rigidTransform = new RigidTransform(chunkRotations[i].Value, chunkTranslations[i].Value);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], chunkColliders[i], rigidTransform);
                }
            }

            private void ProcessTranslationRotationScale(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities     = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders    = chunk.GetNativeArray(typeGroup.collider);
                var chunkTranslations = chunk.GetNativeArray(typeGroup.translation);
                var chunkRotations    = chunk.GetNativeArray(typeGroup.rotation);
                var chunkScales       = chunk.GetNativeArray(typeGroup.scale);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var collider       = Physics.ScaleCollider(chunkColliders[i], chunkScales[i]);
                    var rigidTransform = new RigidTransform(chunkRotations[i].Value, chunkTranslations[i].Value);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], collider, rigidTransform);
                }
            }

            private void ProcessLocalToWorld(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities      = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders     = chunk.GetNativeArray(typeGroup.collider);
                var chunkLocalToWorlds = chunk.GetNativeArray(typeGroup.localToWorld);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var localToWorld   = chunkLocalToWorlds[i];
                    var rotation       = quaternion.LookRotationSafe(localToWorld.Forward, localToWorld.Up);
                    var position       = localToWorld.Position;
                    var rigidTransform = new RigidTransform(rotation, position);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], chunkColliders[i], rigidTransform);
                }
            }

            private void ProcessParent(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities      = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders     = chunk.GetNativeArray(typeGroup.collider);
                var chunkLocalToWorlds = chunk.GetNativeArray(typeGroup.localToWorld);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var localToWorld   = chunkLocalToWorlds[i];
                    var rotation       = quaternion.LookRotationSafe(localToWorld.Forward, localToWorld.Up);
                    var position       = localToWorld.Position;
                    var rigidTransform = new RigidTransform(rotation, position);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], chunkColliders[i], rigidTransform);
                }
            }

            private void ProcessParentScale(ArchetypeChunk chunk, int firstEntityIndex)
            {
                var chunkEntities      = chunk.GetNativeArray(typeGroup.entity);
                var chunkColliders     = chunk.GetNativeArray(typeGroup.collider);
                var chunkLocalToWorlds = chunk.GetNativeArray(typeGroup.localToWorld);
                var chunkScales        = chunk.GetNativeArray(typeGroup.scale);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var localToWorld   = chunkLocalToWorlds[i];
                    var rotation       = quaternion.LookRotationSafe(localToWorld.Forward, localToWorld.Up);
                    var position       = localToWorld.Position;
                    var rigidTransform = new RigidTransform(rotation, position);
                    var collider       = Physics.ScaleCollider(chunkColliders[i], chunkScales[i]);
                    ProcessEntity(firstEntityIndex + i, chunkEntities[i], collider, rigidTransform);
                }
            }

            private void ProcessEntity(int index, Entity entity, Collider collider, RigidTransform rigidTransform)
            {
                Aabb aabb = Physics.AabbFrom(collider, rigidTransform);

                colliderAoS[index] = new ColliderAoSData
                {
                    collider       = collider,
                    rigidTransform = rigidTransform,
                    aabb           = aabb,
                    entity         = entity
                };
                xmins[index] = aabb.min.x;

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

        //Parallel
        //Calculated Target Bucket and write as layer index
        [BurstCompile]
        public struct Part1FromColliderBodyArrayJob : IJobFor
        {
            public CollisionLayer                       layer;
            [NoAlias] public NativeArray<int>           layerIndices;
            [ReadOnly] public NativeArray<ColliderBody> colliderBodies;
            [NoAlias] public NativeArray<Aabb>          aabbs;
            [NoAlias] public NativeArray<float>         xmins;

            public void Execute(int i)
            {
                var aabb = Physics.AabbFrom(colliderBodies[i].collider, colliderBodies[i].transform);
                aabbs[i] = aabb;
                xmins[i] = aabb.min.x;

                int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

                if (math.all(minBucket == maxBucket))
                {
                    layerIndices[i] = (minBucket.x * layer.worldSubdivisionsPerAxis.y + minBucket.y) * layer.worldSubdivisionsPerAxis.z + minBucket.z;
                }
                else
                {
                    layerIndices[i] = layer.bucketStartsAndCounts.Length - 1;
                }
            }
        }

        //Parallel
        //Calculated Target Bucket and write as layer index using the override AABB
        [BurstCompile]
        public struct Part1FromDualArraysJob : IJobFor
        {
            public CollisionLayer               layer;
            [NoAlias] public NativeArray<int>   layerIndices;
            [ReadOnly] public NativeArray<Aabb> aabbs;
            [NoAlias] public NativeArray<float> xmins;

            public void Execute(int i)
            {
                var aabb = aabbs[i];
                xmins[i] = aabb.min.x;

                int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
                int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
                minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
                maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

                if (math.all(minBucket == maxBucket))
                {
                    layerIndices[i] = (minBucket.x * layer.worldSubdivisionsPerAxis.y + minBucket.y) * layer.worldSubdivisionsPerAxis.z + minBucket.z;
                }
                else
                {
                    layerIndices[i] = layer.bucketStartsAndCounts.Length - 1;
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
        public struct Part3Job : IJobFor
        {
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int>          layerIndices;
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<int> unsortedSrcIndices;

            public void Execute(int i)
            {
                int spot                 = layerIndices[i];
                unsortedSrcIndices[spot] = i;
            }
        }

        //Parallel
        //Sort buckets
        [BurstCompile]
        public struct Part4Job : IJobFor
        {
            [NoAlias, NativeDisableParallelForRestriction] public NativeArray<int> unsortedSrcIndices;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<float>        xmins;
            [ReadOnly] public NativeArray<int2>                                    bucketStartAndCounts;

            public void Execute(int i)
            {
                var startAndCount = bucketStartAndCounts[i];

                var intSlice = unsortedSrcIndices.Slice(startAndCount.x, startAndCount.y);
                RadixSortBucket(intSlice, xmins);
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
        public struct Part5FromQueryJob : IJobFor
        {
            [NoAlias, NativeDisableParallelForRestriction]
            public CollisionLayer layer;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<ColliderAoSData> colliderAoS;

            [ReadOnly] public NativeArray<int> remapSrcIndices;

            public void Execute(int i)
            {
                var aos         = colliderAoS[remapSrcIndices[i]];
                layer.bodies[i] = new ColliderBody
                {
                    collider  = aos.collider,
                    transform = aos.rigidTransform,
                    entity    = aos.entity
                };
                layer.xmins[i]     = aos.aabb.min.x;
                layer.xmaxs[i]     = aos.aabb.max.x;
                layer.yzminmaxs[i] = new float4(aos.aabb.min.yz, aos.aabb.max.yz);
            }
        }

        //Parallel
        //Copy array data to layer
        [BurstCompile]
        public struct Part5FromArraysJob : IJobFor
        {
            [NativeDisableParallelForRestriction]
            public CollisionLayer layer;

            [ReadOnly] public NativeArray<Aabb>         aabbs;
            [ReadOnly] public NativeArray<ColliderBody> bodies;
            [ReadOnly] public NativeArray<int>          remapSrcIndices;

            public void Execute(int i)
            {
                int src            = remapSrcIndices[i];
                layer.bodies[i]    = bodies[src];
                layer.xmins[i]     = aabbs[src].min.x;
                layer.xmaxs[i]     = aabbs[src].max.x;
                layer.yzminmaxs[i] = new float4(aabbs[src].min.yz, aabbs[src].max.yz);
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
                var remapSrcArray = new NativeArray<int>(layer.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                BuildImmediate(layer, remapSrcArray, bodies);
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
                var remapSrcArray = new NativeArray<int>(layer.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                BuildImmediate(layer, remapSrcArray, bodies, aabbs);
            }
        }

        //Single
        //All five steps for custom arrays with remap
        [BurstCompile]
        public struct BuildFromColliderArraySingleWithRemapJob : IJob
        {
            public CollisionLayer layer;

            [ReadOnly] public NativeArray<ColliderBody> bodies;
            public NativeArray<int>                     remapSrcIndices;

            public void Execute()
            {
                BuildImmediate(layer, remapSrcIndices, bodies);
            }
        }

        //Single
        //All five steps for custom arrays with remap
        [BurstCompile]
        public struct BuildFromDualArraysSingleWithRemapJob : IJob
        {
            public CollisionLayer layer;

            [ReadOnly] public NativeArray<Aabb>         aabbs;
            [ReadOnly] public NativeArray<ColliderBody> bodies;
            public NativeArray<int>                     remapSrcIndices;

            public void Execute()
            {
                BuildImmediate(layer, remapSrcIndices, bodies, aabbs);
            }
        }

        #endregion

        #region Immediate
        public static void BuildImmediate(CollisionLayer layer, NativeArray<int> remapSrcArray, NativeArray<ColliderBody> bodies)
        {
            var aabbs        = new NativeArray<Aabb>(remapSrcArray.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var layerIndices = new NativeArray<int>(remapSrcArray.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var xmins        = new NativeArray<float>(remapSrcArray.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var p1 = new Part1FromColliderBodyArrayJob
            {
                aabbs          = aabbs,
                colliderBodies = bodies,
                layer          = layer,
                layerIndices   = layerIndices,
                xmins          = xmins
            };
            for (int i = 0; i < layer.Count; i++)
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
                unsortedSrcIndices = remapSrcArray
            };
            for (int i = 0; i < layer.Count; i++)
            {
                p3.Execute(i);
            }

            var p4 = new Part4Job
            {
                bucketStartAndCounts = layer.bucketStartsAndCounts,
                unsortedSrcIndices   = remapSrcArray,
                xmins                = xmins
            };
            for (int i = 0; i < layer.BucketCount; i++)
            {
                p4.Execute(i);
            }

            var p5 = new Part5FromArraysJob
            {
                aabbs           = aabbs,
                bodies          = bodies,
                layer           = layer,
                remapSrcIndices = remapSrcArray
            };
            for (int i = 0; i < layer.Count; i++)
            {
                p5.Execute(i);
            }
        }

        public static void BuildImmediate(CollisionLayer layer, NativeArray<int> remapSrcArray, NativeArray<ColliderBody> bodies, NativeArray<Aabb> aabbs)
        {
            var layerIndices = new NativeArray<int>(remapSrcArray.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var xmins        = new NativeArray<float>(remapSrcArray.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var p1 = new Part1FromDualArraysJob
            {
                aabbs        = aabbs,
                layer        = layer,
                layerIndices = layerIndices,
                xmins        = xmins
            };
            for (int i = 0; i < layer.Count; i++)
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
                unsortedSrcIndices = remapSrcArray
            };
            for (int i = 0; i < layer.Count; i++)
            {
                p3.Execute(i);
            }

            var p4 = new Part4Job
            {
                bucketStartAndCounts = layer.bucketStartsAndCounts,
                unsortedSrcIndices   = remapSrcArray,
                xmins                = xmins
            };
            for (int i = 0; i < layer.BucketCount; i++)
            {
                p4.Execute(i);
            }

            var p5 = new Part5FromArraysJob
            {
                aabbs           = aabbs,
                bodies          = bodies,
                layer           = layer,
                remapSrcIndices = remapSrcArray
            };
            for (int i = 0; i < layer.Count; i++)
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

        private static void calculatePrefixSum(NativeArray<int> counts, NativeArray<int> sums)
        {
            sums[0] = 0;
            for (int i = 0; i < counts.Length - 1; i++)
            {
                sums[i + 1] = sums[i] + counts[i];
            }
        }

        public static void RadixSortBucket(NativeSlice<int> unsortedSrcIndices, NativeArray<float> xmins)
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
                var keys            = Keys(xmins[unsortedSrcIndices[i]]);
                counts1[keys.byte1] = counts1[keys.byte1] + 1;
                counts2[keys.byte2] = counts2[keys.byte2] + 1;
                counts3[keys.byte3] = counts3[keys.byte3] + 1;
                counts4[keys.byte4] = counts4[keys.byte4] + 1;
                frontArray[i]       = new Indexer { key = keys, index = unsortedSrcIndices[i] };
            }

            //Sums
            calculatePrefixSum(counts1, prefixSum1);
            calculatePrefixSum(counts2, prefixSum2);
            calculatePrefixSum(counts3, prefixSum3);
            calculatePrefixSum(counts4, prefixSum4);

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
    }
}

