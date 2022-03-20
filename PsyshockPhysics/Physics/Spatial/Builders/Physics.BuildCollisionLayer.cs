using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

//Todo: Switch to IJobChunks to optionally grab translation and rotation
namespace Latios.Psyshock
{
    public struct BuildCollisionLayerConfig
    {
        internal BuildCollisionLayerInternal.LayerChunkTypeGroup typeGroup;
        internal ComponentSystemBase                             system;
        internal EntityQuery                                     query;

        internal NativeArray<Aabb>         aabbs;
        internal NativeArray<ColliderBody> bodies;

        internal NativeArray<int> remapSrcIndices;

        internal CollisionLayerSettings settings;

        internal bool hasQueryData;
        internal bool hasBodiesArray;
        internal bool hasAabbsArray;
        internal bool hasRemapSrcIndices;

        internal int count;

        public static readonly CollisionLayerSettings defaultSettings = new CollisionLayerSettings
        {
            worldAABB                = new Aabb(new float3(-1f), new float3(1f)),
            worldSubdivisionsPerAxis = new int3(2, 2, 2)
        };
    }

    public static partial class Physics
    {
        public static FluentQuery PatchQueryForBuildingCollisionLayer(this FluentQuery fluent)
        {
            return fluent.WithAllWeak<Collider>();
        }

        #region Starters
        public static BuildCollisionLayerConfig BuildCollisionLayer(EntityQuery query, ComponentSystemBase system)
        {
            var config          = new BuildCollisionLayerConfig();
            config.query        = query;
            config.system       = system;
            config.typeGroup    = BuildCollisionLayerInternal.BuildLayerChunkTypeGroup(system);
            config.hasQueryData = true;
            config.settings     = BuildCollisionLayerConfig.defaultSettings;
            config.count        = query.CalculateEntityCount();
            return config;
        }

        public static BuildCollisionLayerConfig BuildCollisionLayer(NativeArray<ColliderBody> bodies)
        {
            var config            = new BuildCollisionLayerConfig();
            config.bodies         = bodies;
            config.hasBodiesArray = true;
            config.settings       = BuildCollisionLayerConfig.defaultSettings;
            config.count          = bodies.Length;
            return config;
        }

        public static BuildCollisionLayerConfig BuildCollisionLayer(NativeArray<ColliderBody> bodies, NativeArray<Aabb> overrideAabbs)
        {
            var config = new BuildCollisionLayerConfig();
            ValidateOverrideAabbsAreRightLength(overrideAabbs, bodies.Length, false);

            config.aabbs          = overrideAabbs;
            config.bodies         = bodies;
            config.hasAabbsArray  = true;
            config.hasBodiesArray = true;
            config.settings       = BuildCollisionLayerConfig.defaultSettings;
            config.count          = bodies.Length;
            return config;
        }
        #endregion

        #region FluentChain
        public static BuildCollisionLayerConfig WithSettings(this BuildCollisionLayerConfig config, CollisionLayerSettings settings)
        {
            config.settings = settings;
            return config;
        }

        public static BuildCollisionLayerConfig WithWorldBounds(this BuildCollisionLayerConfig config, Aabb worldAabb)
        {
            config.settings.worldAABB = worldAabb;
            return config;
        }

        public static BuildCollisionLayerConfig WithSubdivisions(this BuildCollisionLayerConfig config, int3 subdivisions)
        {
            config.settings.worldSubdivisionsPerAxis = subdivisions;
            return config;
        }

        public static BuildCollisionLayerConfig WithWorldMin(this BuildCollisionLayerConfig config, float x, float y, float z)
        {
            config.settings.worldAABB.min = new float3(x, y, z);
            return config;
        }

        public static BuildCollisionLayerConfig WithWorldMax(this BuildCollisionLayerConfig config, float x, float y, float z)
        {
            config.settings.worldAABB.max = new float3(x, y, z);
            return config;
        }

        public static BuildCollisionLayerConfig WithWorldBounds(this BuildCollisionLayerConfig config, float3 min, float3 max)
        {
            var aabb = new Aabb(min, max);
            return config.WithWorldBounds(aabb);
        }

        public static BuildCollisionLayerConfig WithSubdivisions(this BuildCollisionLayerConfig config, int x, int y, int z)
        {
            return config.WithSubdivisions(new int3(x, y, z));
        }

        public static BuildCollisionLayerConfig WithRemapArray(this BuildCollisionLayerConfig config, NativeArray<int> remapSrcIndices)
        {
            ValidateRemapArrayIsRightLength(remapSrcIndices, config.count, config.hasQueryData);

            config.remapSrcIndices    = remapSrcIndices;
            config.hasRemapSrcIndices = true;
            return config;
        }

        public static BuildCollisionLayerConfig WithRemapArray(this BuildCollisionLayerConfig config, out NativeArray<int> remapSrcIndices, Allocator allocator)
        {
            remapSrcIndices = new NativeArray<int>(config.count, allocator, NativeArrayOptions.UninitializedMemory);

            config.remapSrcIndices    = remapSrcIndices;
            config.hasRemapSrcIndices = true;
            return config;
        }

        #endregion

        #region Schedulers
        public static void RunImmediate(this BuildCollisionLayerConfig config, out CollisionLayer layer, Allocator allocator)
        {
            config.ValidateSettings();

            if (config.hasQueryData)
            {
                ThrowEntityQueryInImmediateMode();
                layer = default;
            }
            else if (config.hasAabbsArray && config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                if (config.hasRemapSrcIndices)
                    BuildCollisionLayerInternal.BuildImmediate(layer, config.remapSrcIndices, config.bodies, config.aabbs);
                else
                {
                    var remapArray = new NativeArray<int>(layer.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    BuildCollisionLayerInternal.BuildImmediate(layer, remapArray, config.bodies, config.aabbs);
                }
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                if (config.hasRemapSrcIndices)
                    BuildCollisionLayerInternal.BuildImmediate(layer, config.remapSrcIndices, config.bodies, config.aabbs);
                else
                {
                    var remapArray = new NativeArray<int>(layer.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    BuildCollisionLayerInternal.BuildImmediate(layer, remapArray, config.bodies, config.aabbs);
                }
            }
            else
            {
                ThrowUnknownConfiguration();
                layer = default;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowEntityQueryInImmediateMode()
        {
            throw new InvalidOperationException("Running immediate mode on an EntityQuery is not supported. Use Run instead.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowUnknownConfiguration()
        {
            throw new InvalidOperationException("Something went wrong with the BuildCollisionError configuration.");
        }

        public static void Run(this BuildCollisionLayerConfig config, out CollisionLayer layer, Allocator allocator)
        {
            config.ValidateSettings();

            if (config.hasQueryData)
            {
                int count        = config.count;
                layer            = new CollisionLayer(count, config.settings, allocator);
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var aos          = new NativeArray<BuildCollisionLayerInternal.ColliderAoSData>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xmins        = new NativeArray<float>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                NativeArray<int> remapSrcIndices = config.hasRemapSrcIndices ? config.remapSrcIndices : new NativeArray<int>(count,
                                                                                                                             Allocator.TempJob,
                                                                                                                             NativeArrayOptions.UninitializedMemory);

                new BuildCollisionLayerInternal.Part1FromQueryJob
                {
                    typeGroup    = config.typeGroup,
                    layer        = layer,
                    layerIndices = layerIndices,
                    colliderAoS  = aos,
                    xmins        = xmins
                }.Run(config.query);

                new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices
                }.Run();

                new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices,
                    unsortedSrcIndices = remapSrcIndices
                }.Run(count);

                new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts,
                    unsortedSrcIndices   = remapSrcIndices,
                    xmins                = xmins
                }.Run(layer.BucketCount);

                new BuildCollisionLayerInternal.Part5FromQueryJob
                {
                    colliderAoS     = aos,
                    layer           = layer,
                    remapSrcIndices = remapSrcIndices
                }.Run(count);

                if (!config.hasRemapSrcIndices)
                    remapSrcIndices.Dispose();
            }
            else if (config.hasAabbsArray && config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.aabbs.Length, config.settings, allocator);
                if (config.hasRemapSrcIndices)
                {
                    new BuildCollisionLayerInternal.BuildFromDualArraysSingleWithRemapJob
                    {
                        layer           = layer,
                        aabbs           = config.aabbs,
                        bodies          = config.bodies,
                        remapSrcIndices = config.remapSrcIndices
                    }.Run();
                }
                else
                {
                    new BuildCollisionLayerInternal.BuildFromDualArraysSingleJob
                    {
                        layer  = layer,
                        aabbs  = config.aabbs,
                        bodies = config.bodies
                    }.Run();
                }
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                if (config.hasRemapSrcIndices)
                {
                    new BuildCollisionLayerInternal.BuildFromColliderArraySingleWithRemapJob
                    {
                        layer           = layer,
                        bodies          = config.bodies,
                        remapSrcIndices = config.remapSrcIndices
                    }.Run();
                }
                else
                {
                    new BuildCollisionLayerInternal.BuildFromColliderArraySingleJob
                    {
                        layer  = layer,
                        bodies = config.bodies
                    }.Run();
                }
            }
            else
                throw new InvalidOperationException("Something went wrong with the BuildCollisionError configuration.");
        }

        public static JobHandle ScheduleSingle(this BuildCollisionLayerConfig config, out CollisionLayer layer, Allocator allocator, JobHandle inputDeps = default)
        {
            config.ValidateSettings();

            var jh = inputDeps;

            if (config.hasQueryData)
            {
                int count        = config.count;
                layer            = new CollisionLayer(count, config.settings, allocator);
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var aos          = new NativeArray<BuildCollisionLayerInternal.ColliderAoSData>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xmins        = new NativeArray<float>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                NativeArray<int> remapSrcIndices = config.hasRemapSrcIndices ? config.remapSrcIndices : new NativeArray<int>(count,
                                                                                                                             Allocator.TempJob,
                                                                                                                             NativeArrayOptions.UninitializedMemory);

                jh = new BuildCollisionLayerInternal.Part1FromQueryJob
                {
                    typeGroup    = config.typeGroup,
                    layer        = layer,
                    layerIndices = layerIndices,
                    colliderAoS  = aos,
                    xmins        = xmins
                }.Schedule(config.query, jh);

                jh = new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices,
                    unsortedSrcIndices = remapSrcIndices
                }.Schedule(count, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts,
                    unsortedSrcIndices   = remapSrcIndices,
                    xmins                = xmins
                }.Schedule(layer.BucketCount, jh);

                jh = new BuildCollisionLayerInternal.Part5FromQueryJob
                {
                    colliderAoS     = aos,
                    layer           = layer,
                    remapSrcIndices = remapSrcIndices
                }.Schedule(count, jh);

                if (!config.hasRemapSrcIndices)
                    jh = remapSrcIndices.Dispose(jh);
                return jh;
            }
            else if (config.hasAabbsArray && config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.aabbs.Length, config.settings, allocator);
                if (config.hasRemapSrcIndices)
                {
                    jh = new BuildCollisionLayerInternal.BuildFromDualArraysSingleWithRemapJob
                    {
                        layer           = layer,
                        aabbs           = config.aabbs,
                        bodies          = config.bodies,
                        remapSrcIndices = config.remapSrcIndices
                    }.Schedule(jh);
                }
                else
                {
                    jh = new BuildCollisionLayerInternal.BuildFromDualArraysSingleJob
                    {
                        layer  = layer,
                        aabbs  = config.aabbs,
                        bodies = config.bodies
                    }.Schedule(jh);
                }
                return jh;
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                if (config.hasRemapSrcIndices)
                {
                    jh = new BuildCollisionLayerInternal.BuildFromColliderArraySingleWithRemapJob
                    {
                        layer           = layer,
                        bodies          = config.bodies,
                        remapSrcIndices = config.remapSrcIndices
                    }.Schedule(jh);
                }
                else
                {
                    jh = new BuildCollisionLayerInternal.BuildFromColliderArraySingleJob
                    {
                        layer  = layer,
                        bodies = config.bodies
                    }.Schedule(jh);
                }
                return jh;
            }
            else
                throw new InvalidOperationException("Something went wrong with the BuildCollisionError configuration.");
        }

        public static JobHandle ScheduleParallel(this BuildCollisionLayerConfig config, out CollisionLayer layer, Allocator allocator, JobHandle inputDeps = default)
        {
            config.ValidateSettings();

            var jh = inputDeps;

            if (config.hasQueryData)
            {
                int count        = config.query.CalculateEntityCount();
                layer            = new CollisionLayer(count, config.settings, allocator);
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xmins        = new NativeArray<float>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var aos          = new NativeArray<BuildCollisionLayerInternal.ColliderAoSData>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                NativeArray<int> remapSrcIndices = config.hasRemapSrcIndices ? config.remapSrcIndices : new NativeArray<int>(count,
                                                                                                                             Allocator.TempJob,
                                                                                                                             NativeArrayOptions.UninitializedMemory);

                jh = new BuildCollisionLayerInternal.Part1FromQueryJob
                {
                    layer        = layer,
                    typeGroup    = config.typeGroup,
                    layerIndices = layerIndices,
                    xmins        = xmins,
                    colliderAoS  = aos
                }.ScheduleParallel(config.query, jh);

                jh = new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices,
                    unsortedSrcIndices = remapSrcIndices
                }.ScheduleParallel(count, 512, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    unsortedSrcIndices   = remapSrcIndices,
                    xmins                = xmins,
                    bucketStartAndCounts = layer.bucketStartsAndCounts
                }.ScheduleParallel(layer.BucketCount, 1, jh);

                jh = new BuildCollisionLayerInternal.Part5FromQueryJob
                {
                    layer           = layer,
                    colliderAoS     = aos,
                    remapSrcIndices = remapSrcIndices
                }.ScheduleParallel(count, 128, jh);

                if (!config.hasRemapSrcIndices)
                    jh = remapSrcIndices.Dispose(jh);

                return jh;
            }
            else if (config.hasBodiesArray)
            {
                layer            = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                int count        = config.bodies.Length;
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xmins        = new NativeArray<float>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                NativeArray<int> remapSrcIndices = config.hasRemapSrcIndices ? config.remapSrcIndices : new NativeArray<int>(count,
                                                                                                                             Allocator.TempJob,
                                                                                                                             NativeArrayOptions.UninitializedMemory);

                NativeArray<Aabb> aabbs = config.hasAabbsArray ? config.aabbs : new NativeArray<Aabb>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                if (config.hasAabbsArray)
                {
                    jh = new BuildCollisionLayerInternal.Part1FromDualArraysJob
                    {
                        layer        = layer,
                        aabbs        = aabbs,
                        layerIndices = layerIndices,
                        xmins        = xmins
                    }.ScheduleParallel(count, 64, jh);
                }
                else
                {
                    jh = new BuildCollisionLayerInternal.Part1FromColliderBodyArrayJob
                    {
                        layer          = layer,
                        aabbs          = aabbs,
                        colliderBodies = config.bodies,
                        layerIndices   = layerIndices,
                        xmins          = xmins
                    }.ScheduleParallel(count, 64, jh);
                }

                jh = new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices,
                    unsortedSrcIndices = remapSrcIndices
                }.ScheduleParallel(count, 512, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts,
                    unsortedSrcIndices   = remapSrcIndices,
                    xmins                = xmins
                }.ScheduleParallel(layer.BucketCount, 1, jh);

                jh = new BuildCollisionLayerInternal.Part5FromArraysJob
                {
                    aabbs           = aabbs,
                    bodies          = config.bodies,
                    layer           = layer,
                    remapSrcIndices = remapSrcIndices
                }.ScheduleParallel(count, 128, jh);

                if ((!config.hasAabbsArray) && (!config.hasRemapSrcIndices))
                    jh = JobHandle.CombineDependencies(remapSrcIndices.Dispose(jh), aabbs.Dispose(jh));
                else if (!config.hasRemapSrcIndices)
                    jh = remapSrcIndices.Dispose(jh);
                else if (!config.hasAabbsArray)
                    jh = aabbs.Dispose(jh);

                return jh;
            }
            else
                throw new InvalidOperationException("Something went wrong with the BuildCollisionError configuration.");
        }
        #endregion

        #region Validators
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateSettings(this BuildCollisionLayerConfig config)
        {
            if (math.any(config.settings.worldAABB.min > config.settings.worldAABB.max))
                throw new InvalidOperationException("BuildCollisionLayer requires a valid worldBounds AABB");
            if (math.any(config.settings.worldSubdivisionsPerAxis < 1))
                throw new InvalidOperationException("BuildCollisionLayer requires positive Subdivision values per axis");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateOverrideAabbsAreRightLength(NativeArray<Aabb> aabbs, int count, bool query)
        {
            if (aabbs.Length != count)
                throw new InvalidOperationException(
                    $"The number of elements in overrideAbbs does not match the { (query ? "number of entities in the query" : "number of bodies in the bodies array")}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateRemapArrayIsRightLength(NativeArray<int> remap, int count, bool query)
        {
            if (remap.Length != count)
                throw new InvalidOperationException(
                    $"The number of elements in remapSrcArray does not match the { (query ? "number of entities in the query" : "number of bodies in the bodies array")}");
        }
        #endregion
    }
}

