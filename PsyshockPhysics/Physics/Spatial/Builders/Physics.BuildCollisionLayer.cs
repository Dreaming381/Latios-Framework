using System;
using System.Diagnostics;
using Latios.Transforms.Abstract;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A struct defining the type handles used when building a CollisionLayer from an EntityQuery.
    /// All handles are ReadOnly. You can cache this structure inside a system to accelerate scheduling costs,
    /// but you must also ensure the handles are updating during each OnUpdate before building any CollisionLayer.
    /// </summary>
    public struct BuildCollisionLayerTypeHandles
    {
        [ReadOnly] public ComponentTypeHandle<Collider>           collider;
        [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle worldTransform;
        [ReadOnly] public EntityTypeHandle                        entity;

        /// <summary>
        /// Constructs the BuildCollsionLayer type handles using a managed system
        /// </summary>
        public BuildCollisionLayerTypeHandles(SystemBase system)
        {
            collider       = system.GetComponentTypeHandle<Collider>(true);
            worldTransform = new WorldTransformReadOnlyAspect.TypeHandle(ref system.CheckedStateRef);
            entity         = system.GetEntityTypeHandle();
        }

        /// <summary>
        /// Constructs the BuildCollisionLayer type handles using a SystemState
        /// </summary>
        public BuildCollisionLayerTypeHandles(ref SystemState system)
        {
            collider       = system.GetComponentTypeHandle<Collider>(true);
            worldTransform = new WorldTransformReadOnlyAspect.TypeHandle(ref system);
            entity         = system.GetEntityTypeHandle();
        }

        /// <summary>
        /// Updates the type handles using a managed system
        /// </summary>
        public void Update(SystemBase system)
        {
            collider.Update(system);
            worldTransform.Update(ref system.CheckedStateRef);
            entity.Update(system);
        }

        /// <summary>
        /// Updates the type handles using a SystemState
        /// </summary>
        public void Update(ref SystemState system)
        {
            collider.Update(ref system);
            worldTransform.Update(ref system);
            entity.Update(ref system);
        }
    }

    /// <summary>
    /// The config object used in Physics.BuildCollisionLayer fluent chains
    /// </summary>
    public struct BuildCollisionLayerConfig
    {
        internal BuildCollisionLayerTypeHandles typeGroup;
        internal EntityQuery                    query;

        internal NativeArray<Aabb>         aabbs;
        internal NativeArray<ColliderBody> bodies;

        internal CollisionLayerSettings settings;

        internal bool hasQueryData;
        internal bool hasBodiesArray;
        internal bool hasAabbsArray;

        internal int count;

        /// <summary>
        /// The default CollisionLayerSettings used when none is specified.
        /// These settings divide the world into 8 cells associated with the 8 octants of world space
        /// </summary>
        public static readonly CollisionLayerSettings defaultSettings = new CollisionLayerSettings
        {
            worldAabb                = new Aabb(new float3(-1f), new float3(1f)),
            worldSubdivisionsPerAxis = new int3(2, 2, 2)
        };
    }

    public static partial class Physics
    {
        /// <summary>
        /// Adds the necessary components to an EntityQuery to ensure proper building of a CollisionLayer
        /// </summary>
        public static FluentQuery PatchQueryForBuildingCollisionLayer(this FluentQuery fluent)
        {
            return fluent.With<Collider>(true).WithWorldTransformReadOnly();
        }

        #region Starters
        /// <summary>
        /// Creates a new CollisionLayer by extracting collider and transform data from the entities in an EntityQuery.
        /// This is a start of a fluent chain.
        /// </summary>
        /// <param name="query">The EntityQuery from which to extract collider and transform data</param>
        /// <param name="system">The system used for extracting ComponentTypeHandles</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(EntityQuery query, SystemBase system)
        {
            var config          = new BuildCollisionLayerConfig();
            config.query        = query;
            config.typeGroup    = new BuildCollisionLayerTypeHandles(system);
            config.hasQueryData = true;
            config.settings     = BuildCollisionLayerConfig.defaultSettings;
            config.count        = query.CalculateEntityCount();
            return config;
        }

        /// <summary>
        /// Creates a new CollisionLayer by extracting collider and transform data from the entities in an EntityQuery.
        /// This is a start of a fluent chain.
        /// </summary>
        /// <param name="query">The EntityQuery from which to extract collider and transform data</param>
        /// <param name="system">The system used for extracting ComponentTypeHandles</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(EntityQuery query, ref SystemState system)
        {
            var config          = new BuildCollisionLayerConfig();
            config.query        = query;
            config.typeGroup    = new BuildCollisionLayerTypeHandles(ref system);
            config.hasQueryData = true;
            config.settings     = BuildCollisionLayerConfig.defaultSettings;
            config.count        = query.CalculateEntityCount();
            return config;
        }

        /// <summary>
        /// Creates a new CollisionLayer by extracting collider and transform data from the entities in an EntityQuery.
        /// This is a start of a fluent chain.
        /// </summary>
        /// <param name="query">The EntityQuery from which to extract collider and transform data</param>
        /// <param name="requiredTypeHandles">Cached type handles that must be updated before invoking this method</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(EntityQuery query, in BuildCollisionLayerTypeHandles requiredTypeHandles)
        {
            var config          = new BuildCollisionLayerConfig();
            config.query        = query;
            config.typeGroup    = requiredTypeHandles;
            config.hasQueryData = true;
            config.settings     = BuildCollisionLayerConfig.defaultSettings;
            config.count        = query.CalculateEntityCount();
            return config;
        }

        /// <summary>
        /// Creates a new CollisionLayer using the collider and transform data provided by the bodies array
        /// </summary>
        /// <param name="bodies">The array of ColliderBody instances which should be baked into the CollisionLayer</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(NativeArray<ColliderBody> bodies)
        {
            var config            = new BuildCollisionLayerConfig();
            config.bodies         = bodies;
            config.hasBodiesArray = true;
            config.settings       = BuildCollisionLayerConfig.defaultSettings;
            config.count          = bodies.Length;
            return config;
        }

        /// <summary>
        /// Creates a new CollisionLayer using the collider and transform data provided by the bodies array,
        /// but uses the AABBs provided by the overrideAabbs array instead of calculating AABBs from the bodies
        /// </summary>
        /// <param name="bodies">The array of ColliderBody instances which should be baked into the CollisionLayer</param>
        /// <param name="overrideAabbs">The array of AABBs parallel to the bodies array specifying different AABBs to use for each body</param>
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
        /// <summary>
        /// Specifies the CollisionLayerSettings which should be used when building the CollisionLayer
        /// </summary>
        public static BuildCollisionLayerConfig WithSettings(this BuildCollisionLayerConfig config, CollisionLayerSettings settings)
        {
            config.settings = settings;
            return config;
        }

        /// <summary>
        /// Specifies the worldAabb member of the CollisionLayerSettings which should be used when building the CollisionLayer
        /// </summary>
        public static BuildCollisionLayerConfig WithWorldBounds(this BuildCollisionLayerConfig config, Aabb worldAabb)
        {
            config.settings.worldAabb = worldAabb;
            return config;
        }

        /// <summary>
        /// Specifies the worldSubdivisionsPerAxis member of the CollisionLayerSettings which should be used when building the CollisionLayer
        /// </summary>
        public static BuildCollisionLayerConfig WithSubdivisions(this BuildCollisionLayerConfig config, int3 subdivisions)
        {
            config.settings.worldSubdivisionsPerAxis = subdivisions;
            return config;
        }

        /// <summary>
        /// Specifies the worldAabb.min of the CollsionLayerSettings which should be used when building the CollisionLayer
        /// </summary>
        public static BuildCollisionLayerConfig WithWorldMin(this BuildCollisionLayerConfig config, float x, float y, float z)
        {
            config.settings.worldAabb.min = new float3(x, y, z);
            return config;
        }

        /// <summary>
        /// Specifies the worldAabb.max of the CollsionLayerSettings which should be used when building the CollisionLayer
        /// </summary>
        public static BuildCollisionLayerConfig WithWorldMax(this BuildCollisionLayerConfig config, float x, float y, float z)
        {
            config.settings.worldAabb.max = new float3(x, y, z);
            return config;
        }

        /// <summary>
        /// Specifies the worldAabb of the CollsionLayerSettings which should be used when building the CollisionLayer
        /// </summary>
        public static BuildCollisionLayerConfig WithWorldBounds(this BuildCollisionLayerConfig config, float3 min, float3 max)
        {
            var aabb = new Aabb(min, max);
            return config.WithWorldBounds(aabb);
        }

        /// <summary>
        /// Specifies the worldSubdivisionsPerAxis of the CollisionLayerSettings which should be used when building the CollisionLayer
        /// </summary>
        public static BuildCollisionLayerConfig WithSubdivisions(this BuildCollisionLayerConfig config, int x, int y, int z)
        {
            return config.WithSubdivisions(new int3(x, y, z));
        }

        #endregion

        #region Schedulers
        /// <summary>
        /// Generates the CollisionLayer immediately without using the Job System
        /// </summary>
        /// <param name="layer">The generated CollisionLayer</param>
        /// <param name="allocator">The allocator to use for allocating the CollisionLayer</param>
        public static void RunImmediate(this BuildCollisionLayerConfig config, out CollisionLayer layer, AllocatorManager.AllocatorHandle allocator)
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
                BuildCollisionLayerInternal.BuildImmediate(ref layer, config.bodies, config.aabbs);
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                BuildCollisionLayerInternal.BuildImmediate(ref layer, config.bodies);
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

        /// <summary>
        /// Generates the CollisionLayer on the same thread using Burst
        /// </summary>
        /// <param name="layer">The generated CollisionLayer</param>
        /// <param name="allocator">The allocator to use for allocating the CollisionLayer</param>
        public static void Run(this BuildCollisionLayerConfig config, out CollisionLayer layer, AllocatorManager.AllocatorHandle allocator)
        {
            config.ValidateSettings();

            if (config.hasQueryData)
            {
                int count        = config.count;
                layer            = new CollisionLayer(count, config.settings, allocator);
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var aos          = new NativeArray<BuildCollisionLayerInternal.ColliderAoSData>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xMinMaxs     = new NativeArray<float2>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var part1Indices = config.query.CalculateBaseEntityIndexArray(Allocator.TempJob);
                new BuildCollisionLayerInternal.Part1FromQueryJob
                {
                    typeGroup                 = config.typeGroup,
                    layer                     = layer,
                    layerIndices              = layerIndices,
                    colliderAoS               = aos,
                    xMinMaxs                  = xMinMaxs,
                    firstEntityInChunkIndices = part1Indices
                }.Run(config.query);

                new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices
                }.Run();

                new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices,
                    unsortedSrcIndices = layer.srcIndices
                }.Run(count);

                new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts,
                    unsortedSrcIndices   = layer.srcIndices,
                    trees                = layer.intervalTrees,
                    xMinMaxs             = xMinMaxs
                }.Run(layer.bucketCount);

                new BuildCollisionLayerInternal.Part5FromQueryJob
                {
                    colliderAoS = aos,
                    layer       = layer,
                }.Run(count);
            }
            else if (config.hasAabbsArray && config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.aabbs.Length, config.settings, allocator);

                new BuildCollisionLayerInternal.BuildFromDualArraysSingleJob
                {
                    layer  = layer,
                    aabbs  = config.aabbs,
                    bodies = config.bodies
                }.Run();
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.bodies.Length, config.settings, allocator);

                new BuildCollisionLayerInternal.BuildFromColliderArraySingleJob
                {
                    layer  = layer,
                    bodies = config.bodies
                }.Run();
            }
            else
                throw new InvalidOperationException("Something went wrong with the BuildCollisionError configuration.");
        }

        /// <summary>
        /// Generates the CollisionLayer inside a single-threaded job using Burst
        /// </summary>
        /// <param name="layer">The generated CollisionLayer</param>
        /// <param name="allocator">The allocator to use for allocating the CollisionLayer</param>
        /// <param name="inputDeps">A JobHandle the scheduled job should wait on</param>
        /// <returns>The JobHandle associated with the scheduled builder job</returns>
        public static JobHandle ScheduleSingle(this BuildCollisionLayerConfig config,
                                               out CollisionLayer layer,
                                               AllocatorManager.AllocatorHandle allocator,
                                               JobHandle inputDeps = default)
        {
            config.ValidateSettings();

            var jh = inputDeps;

            if (config.hasQueryData)
            {
                int count        = config.count;
                layer            = new CollisionLayer(count, config.settings, allocator);
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var aos          = new NativeArray<BuildCollisionLayerInternal.ColliderAoSData>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xMinMaxs     = new NativeArray<float2>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var part1Indices = config.query.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, jh, out jh);
                jh               = new BuildCollisionLayerInternal.Part1FromQueryJob
                {
                    typeGroup                 = config.typeGroup,
                    layer                     = layer,
                    layerIndices              = layerIndices,
                    colliderAoS               = aos,
                    xMinMaxs                  = xMinMaxs,
                    firstEntityInChunkIndices = part1Indices
                }.Schedule(config.query, jh);

                jh = new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices,
                    unsortedSrcIndices = layer.srcIndices
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts,
                    unsortedSrcIndices   = layer.srcIndices,
                    trees                = layer.intervalTrees,
                    xMinMaxs             = xMinMaxs
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part5FromQueryJob
                {
                    colliderAoS = aos,
                    layer       = layer,
                }.Schedule(jh);

                return jh;
            }
            else if (config.hasAabbsArray && config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.aabbs.Length, config.settings, allocator);

                jh = new BuildCollisionLayerInternal.BuildFromDualArraysSingleJob
                {
                    layer  = layer,
                    aabbs  = config.aabbs,
                    bodies = config.bodies
                }.Schedule(jh);

                return jh;
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.bodies.Length, config.settings, allocator);

                jh = new BuildCollisionLayerInternal.BuildFromColliderArraySingleJob
                {
                    layer  = layer,
                    bodies = config.bodies
                }.Schedule(jh);

                return jh;
            }
            else
                throw new InvalidOperationException("Something went wrong with the BuildCollisionError configuration.");
        }

        /// <summary>
        /// Generates the CollisionLayer inside a sequence of multi-threaded jobs using Burst
        /// </summary>
        /// <param name="layer">The generated CollisionLayer</param>
        /// <param name="allocator">The allocator to use for allocating the CollisionLayer</param>
        /// <param name="inputDeps">A JobHandle the scheduled jobs should wait on</param>
        /// <returns>The JobHandle associated with the scheduled builder jobs</returns>
        public static JobHandle ScheduleParallel(this BuildCollisionLayerConfig config,
                                                 out CollisionLayer layer,
                                                 AllocatorManager.AllocatorHandle allocator,
                                                 JobHandle inputDeps = default)
        {
            config.ValidateSettings();

            var jh = inputDeps;

            if (config.hasQueryData)
            {
                int count        = config.query.CalculateEntityCount();
                layer            = new CollisionLayer(count, config.settings, allocator);
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xMinMaxs     = new NativeArray<float2>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var aos          = new NativeArray<BuildCollisionLayerInternal.ColliderAoSData>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var part1Indices = config.query.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, jh, out jh);
                jh               = new BuildCollisionLayerInternal.Part1FromQueryJob
                {
                    layer                     = layer,
                    typeGroup                 = config.typeGroup,
                    layerIndices              = layerIndices,
                    xMinMaxs                  = xMinMaxs,
                    colliderAoS               = aos,
                    firstEntityInChunkIndices = part1Indices
                }.ScheduleParallel(config.query, jh);

                jh = new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices,
                    unsortedSrcIndices = layer.srcIndices
                }.Schedule(count, 512, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    unsortedSrcIndices   = layer.srcIndices,
                    xMinMaxs             = xMinMaxs,
                    trees                = layer.intervalTrees,
                    bucketStartAndCounts = layer.bucketStartsAndCounts
                }.Schedule(layer.bucketCount, 1, jh);

                jh = new BuildCollisionLayerInternal.Part5FromQueryJob
                {
                    layer       = layer,
                    colliderAoS = aos,
                }.Schedule(count, 128, jh);

                return jh;
            }
            else if (config.hasBodiesArray)
            {
                layer            = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                int count        = config.bodies.Length;
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xMinMaxs     = new NativeArray<float2>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                NativeArray<Aabb> aabbs = config.hasAabbsArray ? config.aabbs : new NativeArray<Aabb>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                if (config.hasAabbsArray)
                {
                    jh = new BuildCollisionLayerInternal.Part1FromDualArraysJob
                    {
                        layer        = layer,
                        aabbs        = aabbs,
                        layerIndices = layerIndices,
                        xMinMaxs     = xMinMaxs
                    }.Schedule(count, 64, jh);
                }
                else
                {
                    jh = new BuildCollisionLayerInternal.Part1FromColliderBodyArrayJob
                    {
                        layer          = layer,
                        aabbs          = aabbs,
                        colliderBodies = config.bodies,
                        layerIndices   = layerIndices,
                        xMinMaxs       = xMinMaxs
                    }.Schedule(count, 64, jh);
                }

                jh = new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices,
                    unsortedSrcIndices = layer.srcIndices
                }.Schedule(count, 512, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts,
                    unsortedSrcIndices   = layer.srcIndices,
                    trees                = layer.intervalTrees,
                    xMinMaxs             = xMinMaxs
                }.Schedule(layer.bucketCount, 1, jh);

                jh = new BuildCollisionLayerInternal.Part5FromArraysJob
                {
                    aabbs  = aabbs,
                    bodies = config.bodies,
                    layer  = layer,
                }.Schedule(count, 128, jh);

                if (!config.hasAabbsArray)
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
            if (math.any(config.settings.worldAabb.min > config.settings.worldAabb.max))
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
        #endregion
    }
}

