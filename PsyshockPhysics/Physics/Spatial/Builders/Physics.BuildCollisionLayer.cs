using System;
using System.Diagnostics;
using Latios.Transforms.Abstract;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
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

        internal NativeArray<Aabb>         aabbsArray;
        internal NativeArray<ColliderBody> bodiesArray;
        internal NativeList<Aabb>          aabbsList;
        internal NativeList<ColliderBody>  bodiesList;

        internal CollisionLayerSettings settings;

        internal bool hasQueryData;
        internal bool hasBodiesArray;
        internal bool hasAabbsArray;
        internal bool usesLists;

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
            var config = new BuildCollisionLayerConfig
            {
                query        = query,
                typeGroup    = new BuildCollisionLayerTypeHandles(system),
                hasQueryData = true,
                settings     = BuildCollisionLayerConfig.defaultSettings,
            };
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
            var config = new BuildCollisionLayerConfig
            {
                query        = query,
                typeGroup    = new BuildCollisionLayerTypeHandles(ref system),
                hasQueryData = true,
                settings     = BuildCollisionLayerConfig.defaultSettings,
            };
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
            var config = new BuildCollisionLayerConfig
            {
                query        = query,
                typeGroup    = requiredTypeHandles,
                hasQueryData = true,
                settings     = BuildCollisionLayerConfig.defaultSettings,
            };
            return config;
        }

        /// <summary>
        /// Creates a new CollisionLayer using the collider and transform data provided by the bodies array
        /// </summary>
        /// <param name="bodies">The array of ColliderBody instances which should be baked into the CollisionLayer</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(NativeArray<ColliderBody> bodies)
        {
            var config = new BuildCollisionLayerConfig
            {
                bodiesArray    = bodies,
                hasBodiesArray = true,
                settings       = BuildCollisionLayerConfig.defaultSettings,
            };
            return config;
        }

        /// <summary>
        /// Creates a new CollisionLayer using the collider and transform data provided by the bodies array,
        /// but uses the AABBs provided by the overrideAabbs array instead of calculating AABBs from the bodies
        /// </summary>
        /// <param name="bodies">The array of ColliderBody instances which should be baked into the CollisionLayer</param>
        /// <param name="overrideAabbs">The array of AABBs parallel to the bodiesArray array specifying different AABBs to use for each body</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(NativeArray<ColliderBody> bodies, NativeArray<Aabb> overrideAabbs)
        {
            var config = new BuildCollisionLayerConfig
            {
                aabbsArray     = overrideAabbs,
                bodiesArray    = bodies,
                hasAabbsArray  = true,
                hasBodiesArray = true,
                settings       = BuildCollisionLayerConfig.defaultSettings
            };
            return config;
        }

        /// <summary>
        /// Creates a new CollisionLayer using the collider and transform data provided by the bodies list.
        /// If using a job scheduler, the list does not need to be populated at this time.
        /// </summary>
        /// <param name="bodies">The list of ColliderBody instances which should be baked into the CollisionLayer</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(NativeList<ColliderBody> bodies)
        {
            var config = new BuildCollisionLayerConfig
            {
                bodiesList     = bodies,
                hasBodiesArray = true,
                usesLists      = true,
                settings       = BuildCollisionLayerConfig.defaultSettings,
            };
            return config;
        }

        /// <summary>
        /// Creates a new CollisionLayer using the collider and transform data provided by the bodies list,
        /// but uses the AABBs provided by the overrideAabbs list instead of calculating AABBs from the bodies.
        /// If using a job scheduler, the lists do not need to be populated at this time.
        /// </summary>
        /// <param name="bodies">The array of ColliderBody instances which should be baked into the CollisionLayer</param>
        /// <param name="overrideAabbs">The array of AABBs parallel to the bodiesArray array specifying different AABBs to use for each body</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(NativeList<ColliderBody> bodies, NativeList<Aabb> overrideAabbs)
        {
            //ValidateOverrideAabbsAreRightLength(overrideAabbs, bodies.Length, false);

            var config = new BuildCollisionLayerConfig
            {
                aabbsList      = overrideAabbs,
                bodiesList     = bodies,
                hasAabbsArray  = true,
                hasBodiesArray = true,
                usesLists      = true,
                settings       = BuildCollisionLayerConfig.defaultSettings
            };
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
                if (config.usesLists)
                {
                    config.bodiesArray = config.bodiesList.AsArray();
                    config.aabbsArray  = config.aabbsList.AsArray();
                }

                layer = new CollisionLayer(config.settings, allocator);
                BuildCollisionLayerInternal.BuildImmediate(ref layer, config.bodiesArray, config.aabbsArray);
            }
            else if (config.hasBodiesArray)
            {
                if (config.usesLists)
                {
                    config.bodiesArray = config.bodiesList.AsArray();
                }

                layer = new CollisionLayer(config.settings, allocator);
                BuildCollisionLayerInternal.BuildImmediate(ref layer, config.bodiesArray);
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
                layer                  = new CollisionLayer(config.settings, allocator);
                var filteredChunkCache = new NativeList<BuildCollisionLayerInternal.FilteredChunkCache>(config.query.CalculateChunkCountWithoutFiltering(), Allocator.TempJob);
                new BuildCollisionLayerInternal.Part0PrefilterQueryJob
                {
                    filteredChunkCache = filteredChunkCache
                }.Run(config.query);
                new BuildCollisionLayerInternal.BuildFromFilteredChunkCacheSingleJob
                {
                    filteredChunkCache = filteredChunkCache.AsArray(),
                    handles            = config.typeGroup,
                    layer              = layer
                }.Run();
                filteredChunkCache.Dispose();
            }
            else if (config.hasAabbsArray && config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.settings, allocator);

                if (config.usesLists)
                {
                    config.bodiesArray = config.bodiesList.AsArray();
                    config.aabbsArray  = config.aabbsList.AsArray();
                }

                new BuildCollisionLayerInternal.BuildFromDualArraysSingleJob
                {
                    layer  = layer,
                    aabbs  = config.aabbsArray,
                    bodies = config.bodiesArray
                }.Run();
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.settings, allocator);

                if (config.usesLists)
                {
                    config.bodiesArray = config.bodiesList.AsArray();
                }

                new BuildCollisionLayerInternal.BuildFromColliderArraySingleJob
                {
                    layer  = layer,
                    bodies = config.bodiesArray
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

            if (config.hasQueryData)
            {
                layer                  = new CollisionLayer(config.settings, allocator);
                var filteredChunkCache = new NativeList<BuildCollisionLayerInternal.FilteredChunkCache>(config.query.CalculateChunkCountWithoutFiltering(), Allocator.TempJob);
                var jh                 = new BuildCollisionLayerInternal.Part0PrefilterQueryJob
                {
                    filteredChunkCache = filteredChunkCache
                }.Schedule(config.query, inputDeps);
                jh = new BuildCollisionLayerInternal.BuildFromFilteredChunkCacheSingleJob
                {
                    filteredChunkCache = filteredChunkCache.AsArray(),
                    handles            = config.typeGroup,
                    layer              = layer
                }.Schedule(jh);
                return filteredChunkCache.Dispose(jh);
            }
            else if (config.hasAabbsArray && config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.settings, allocator);

                if (config.usesLists)
                {
                    config.bodiesArray = config.bodiesList.AsDeferredJobArray();
                    config.aabbsArray  = config.aabbsList.AsDeferredJobArray();
                }

                return new BuildCollisionLayerInternal.BuildFromDualArraysSingleJob
                {
                    layer  = layer,
                    aabbs  = config.aabbsArray,
                    bodies = config.bodiesArray
                }.Schedule(inputDeps);
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.settings, allocator);

                if (config.usesLists)
                {
                    config.bodiesArray = config.bodiesList.AsDeferredJobArray();
                }

                return new BuildCollisionLayerInternal.BuildFromColliderArraySingleJob
                {
                    layer  = layer,
                    bodies = config.bodiesArray
                }.Schedule(inputDeps);
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

            layer = new CollisionLayer(config.settings, allocator);

            var jh = inputDeps;

            if (config.hasQueryData)
            {
                NativeList<int>                                         layerIndices;
                NativeList<float2>                                      xMinMaxs;
                NativeList<BuildCollisionLayerInternal.ColliderAoSData> aos;

                // A Unity bug causes some platforms to not recognize the != operator for JobHandle.
                // As a workaround, we use a secondary bool.
                JobHandle filteredCacheDisposeHandle    = default;
                bool      hasFilteredCacheDisposeHandle = false;

                if (config.query.HasFilter() || config.query.UsesEnabledFiltering())
                {
                    var filteredChunkCache = new NativeList<BuildCollisionLayerInternal.FilteredChunkCache>(config.query.CalculateChunkCountWithoutFiltering(), Allocator.TempJob);
                    jh                     = new BuildCollisionLayerInternal.Part0PrefilterQueryJob
                    {
                        filteredChunkCache = filteredChunkCache
                    }.Schedule(config.query, jh);

                    layerIndices = new NativeList<int>(Allocator.TempJob);
                    xMinMaxs     = new NativeList<float2>(Allocator.TempJob);
                    aos          = new NativeList<BuildCollisionLayerInternal.ColliderAoSData>(Allocator.TempJob);

                    jh = new BuildCollisionLayerInternal.AllocateCollisionLayerFromFilteredQueryJob
                    {
                        layer              = layer,
                        filteredChunkCache = filteredChunkCache.AsDeferredJobArray(),
                        colliderAoS        = aos,
                        layerIndices       = layerIndices,
                        xMinMaxs           = xMinMaxs
                    }.Schedule(jh);

                    jh = new BuildCollisionLayerInternal.Part1FromFilteredQueryJob
                    {
                        layer              = layer,
                        filteredChunkCache = filteredChunkCache.AsDeferredJobArray(),
                        colliderAoS        = aos.AsDeferredJobArray(),
                        layerIndices       = layerIndices.AsDeferredJobArray(),
                        typeGroup          = config.typeGroup,
                        xMinMaxs           = xMinMaxs.AsDeferredJobArray()
                    }.Schedule(filteredChunkCache, 1, jh);

                    filteredCacheDisposeHandle    = filteredChunkCache.Dispose(jh);
                    hasFilteredCacheDisposeHandle = true;
                }
                else
                {
                    int count    = config.query.CalculateEntityCountWithoutFiltering();
                    layerIndices = new NativeList<int>(count, Allocator.TempJob);
                    layerIndices.ResizeUninitialized(count);
                    xMinMaxs = new NativeList<float2>(count, Allocator.TempJob);
                    xMinMaxs.ResizeUninitialized(count);
                    aos = new NativeList<BuildCollisionLayerInternal.ColliderAoSData>(count, Allocator.TempJob);
                    aos.ResizeUninitialized(count);
                    layer.ResizeUninitialized(count);
                    var part1Indices = config.query.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, jh, out jh);

                    jh = new BuildCollisionLayerInternal.Part1FromUnfilteredQueryJob
                    {
                        layer                     = layer,
                        typeGroup                 = config.typeGroup,
                        layerIndices              = layerIndices.AsDeferredJobArray(),
                        xMinMaxs                  = xMinMaxs.AsDeferredJobArray(),
                        colliderAoS               = aos.AsDeferredJobArray(),
                        firstEntityInChunkIndices = part1Indices
                    }.ScheduleParallel(config.query, jh);
                }

                jh = new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices.AsDeferredJobArray()
                }.Schedule(jh);

                //if (filteredCacheDisposeHandle != default)
                if (hasFilteredCacheDisposeHandle)
                    jh = JobHandle.CombineDependencies(filteredCacheDisposeHandle, jh);

                jh = new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices.AsDeferredJobArray(),
                    unsortedSrcIndices = layer.srcIndices.AsDeferredJobArray()
                }.Schedule(layerIndices, 512, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    unsortedSrcIndices   = layer.srcIndices.AsDeferredJobArray(),
                    xMinMaxs             = xMinMaxs.AsDeferredJobArray(),
                    trees                = layer.intervalTrees.AsDeferredJobArray(),
                    bucketStartAndCounts = layer.bucketStartsAndCounts.AsDeferredJobArray()
                }.Schedule(layer.bucketCount, 1, jh);

                jh = new BuildCollisionLayerInternal.Part5FromAoSJob
                {
                    layer       = layer,
                    colliderAoS = aos.AsDeferredJobArray(),
                }.Schedule(aos, 128, jh);

                return JobHandle.CombineDependencies(layerIndices.Dispose(jh), xMinMaxs.Dispose(jh), aos.Dispose(jh));
            }
            else if (config.hasBodiesArray)
            {
                NativeList<int>    layerIndices;
                NativeList<float2> xMinMaxs;
                if (config.usesLists)
                {
                    if (!config.hasAabbsArray)
                    {
                        config.aabbsList = new NativeList<Aabb>(Allocator.TempJob);
                    }
                    layerIndices = new NativeList<int>(Allocator.TempJob);
                    xMinMaxs     = new NativeList<float2>(Allocator.TempJob);
                    jh           = new BuildCollisionLayerInternal.AllocateCollisionLayerFromBodiesListJob
                    {
                        aabbs            = config.aabbsList,
                        aabbsAreProvided = config.hasAabbsArray,
                        bodies           = config.bodiesList.AsDeferredJobArray(),
                        layer            = layer,
                        layerIndices     = layerIndices,
                        xMinMaxs         = xMinMaxs
                    }.Schedule(jh);
                    config.aabbsArray  = config.aabbsList.AsDeferredJobArray();
                    config.bodiesArray = config.bodiesList.AsDeferredJobArray();
                }
                else
                {
                    int count = config.bodiesArray.Length;
                    if (!config.hasAabbsArray)
                    {
                        config.aabbsList = new NativeList<Aabb>(count, Allocator.TempJob);
                        config.aabbsList.ResizeUninitialized(count);
                        config.aabbsArray = config.aabbsList.AsDeferredJobArray();
                    }
                    layerIndices = new NativeList<int>(count, Allocator.TempJob);
                    layerIndices.ResizeUninitialized(count);
                    xMinMaxs = new NativeList<float2>(count, Allocator.TempJob);
                    xMinMaxs.ResizeUninitialized(count);
                    layer.ResizeUninitialized(count);
                }

                if (config.hasAabbsArray)
                {
                    jh = new BuildCollisionLayerInternal.Part1FromDualArraysJob
                    {
                        layer        = layer,
                        aabbs        = config.aabbsArray,
                        layerIndices = layerIndices.AsDeferredJobArray(),
                        xMinMaxs     = xMinMaxs.AsDeferredJobArray()
                    }.Schedule(layerIndices, 64, jh);
                }
                else
                {
                    jh = new BuildCollisionLayerInternal.Part1FromColliderBodyArrayJob
                    {
                        layer          = layer,
                        aabbs          = config.aabbsArray,
                        colliderBodies = config.bodiesArray,
                        layerIndices   = layerIndices.AsDeferredJobArray(),
                        xMinMaxs       = xMinMaxs.AsDeferredJobArray()
                    }.Schedule(layerIndices, 64, jh);
                }

                jh = new BuildCollisionLayerInternal.Part2Job
                {
                    layer        = layer,
                    layerIndices = layerIndices.AsDeferredJobArray()
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part3Job
                {
                    layerIndices       = layerIndices.AsDeferredJobArray(),
                    unsortedSrcIndices = layer.srcIndices.AsDeferredJobArray()
                }.Schedule(layerIndices, 512, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts.AsDeferredJobArray(),
                    unsortedSrcIndices   = layer.srcIndices.AsDeferredJobArray(),
                    trees                = layer.intervalTrees.AsDeferredJobArray(),
                    xMinMaxs             = xMinMaxs.AsDeferredJobArray()
                }.Schedule(layer.bucketCount, 1, jh);

                jh = new BuildCollisionLayerInternal.Part5FromSplitArraysJob
                {
                    aabbs  = config.aabbsArray,
                    bodies = config.bodiesArray,
                    layer  = layer,
                }.Schedule(layer.bodies, 128, jh);

                JobHandle aabbDispose = default;
                if (!config.hasAabbsArray)
                {
                    aabbDispose = config.aabbsList.Dispose(jh);
                }
                return JobHandle.CombineDependencies(aabbDispose, xMinMaxs.Dispose(jh), layerIndices.Dispose(jh));
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

        #endregion
    }
}

