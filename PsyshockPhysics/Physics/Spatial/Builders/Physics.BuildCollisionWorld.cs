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
    /// A struct defining the type handles used when building a CollisionWorld from an EntityQuery.
    /// All handles are ReadOnly. You can cache this structure inside a system to accelerate scheduling costs,
    /// but you must also ensure the handles are updating during each OnUpdate before building any CollisionWorld.
    /// </summary>
    public struct BuildCollisionWorldTypeHandles
    {
        [ReadOnly] public ComponentTypeHandle<Collider>           collider;
        [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle worldTransform;
        [ReadOnly] public ComponentTypeHandle<CollisionWorldAabb> aabb;
        [ReadOnly] public EntityTypeHandle                        entity;
        public ComponentTypeHandle<CollisionWorldIndex>           index;

        /// <summary>
        /// Constructs the BuildCollsionWorld type handles using a managed system
        /// </summary>
        public BuildCollisionWorldTypeHandles(SystemBase system)
        {
            collider       = system.GetComponentTypeHandle<Collider>(true);
            worldTransform = new WorldTransformReadOnlyAspect.TypeHandle(ref system.CheckedStateRef);
            aabb           = system.GetComponentTypeHandle<CollisionWorldAabb>(true);
            entity         = system.GetEntityTypeHandle();
            index          = system.GetComponentTypeHandle<CollisionWorldIndex>(false);
        }

        /// <summary>
        /// Constructs the BuildCollisionWorld type handles using a SystemState
        /// </summary>
        public BuildCollisionWorldTypeHandles(ref SystemState system)
        {
            collider       = system.GetComponentTypeHandle<Collider>(true);
            worldTransform = new WorldTransformReadOnlyAspect.TypeHandle(ref system);
            aabb           = system.GetComponentTypeHandle<CollisionWorldAabb>(true);
            entity         = system.GetEntityTypeHandle();
            index          = system.GetComponentTypeHandle<CollisionWorldIndex>(false);
        }

        /// <summary>
        /// Updates the type handles using a managed system
        /// </summary>
        public void Update(SystemBase system)
        {
            collider.Update(system);
            worldTransform.Update(ref system.CheckedStateRef);
            aabb.Update(system);
            entity.Update(system);
            index.Update(system);
        }

        /// <summary>
        /// Updates the type handles using a SystemState
        /// </summary>
        public void Update(ref SystemState system)
        {
            collider.Update(ref system);
            worldTransform.Update(ref system);
            aabb.Update(ref system);
            entity.Update(ref system);
            index.Update(ref system);
        }
    }

    /// <summary>
    /// The config object used in Physics.BuildCollisionWorld fluent chains
    /// </summary>
    public struct BuildCollisionWorldConfig
    {
        internal BuildCollisionWorldTypeHandles typeGroup;
        internal EntityQuery                    query;

        internal CollisionLayerSettings settings;
        internal byte                   worldIndex;
    }

    public static partial class Physics
    {
        /// <summary>
        /// Adds the necessary components to an EntityQuery to ensure proper building of a CollisionWorld
        /// </summary>
        public static FluentQuery PatchQueryForBuildingCollisionWorld(this FluentQuery fluent)
        {
            return fluent.With<Collider>(true).WithWorldTransformReadOnly();
        }

        #region Starters
        /// <summary>
        /// Creates a new CollisionWorld by extracting collider and transform data from the entities in an EntityQuery.
        /// This is a start of a fluent chain.
        /// </summary>
        /// <param name="query">The EntityQuery from which to extract collider and transform data</param>
        /// <param name="requiredTypeHandles">Cached type handles that must be updated before invoking this method</param>
        public static BuildCollisionWorldConfig BuildCollisionWorld(EntityQuery query, in BuildCollisionWorldTypeHandles requiredTypeHandles)
        {
            var config = new BuildCollisionWorldConfig
            {
                query      = query,
                typeGroup  = requiredTypeHandles,
                settings   = CollisionLayerSettings.kDefault,
                worldIndex = 1
            };
            return config;
        }
        #endregion

        #region FluentChain
        /// <summary>
        /// Specifies a custom worldIndex to use when building the CollisionWorld. The worldIndex is stored in the CollisionWorldIndex
        /// component of entities added to the world with the component.
        /// </summary>
        /// <param name="worldIndex">An index which should be greater than zero</param>
        public static BuildCollisionWorldConfig WithWorldIndex(this BuildCollisionWorldConfig config, byte worldIndex)
        {
            CollisionWorld.CheckWorldIndexIsValid(worldIndex);
            config.worldIndex = worldIndex;
            return config;
        }

        /// <summary>
        /// Specifies the CollisionWorldSettings which should be used when building the CollisionWorld
        /// </summary>
        public static BuildCollisionWorldConfig WithSettings(this BuildCollisionWorldConfig config, CollisionLayerSettings settings)
        {
            config.settings = settings;
            return config;
        }

        /// <summary>
        /// Specifies the worldAabb member of the CollisionWorldSettings which should be used when building the CollisionWorld
        /// </summary>
        public static BuildCollisionWorldConfig WithWorldBounds(this BuildCollisionWorldConfig config, Aabb worldAabb)
        {
            config.settings.worldAabb = worldAabb;
            return config;
        }

        /// <summary>
        /// Specifies the worldSubdivisionsPerAxis member of the CollisionWorldSettings which should be used when building the CollisionWorld
        /// </summary>
        public static BuildCollisionWorldConfig WithSubdivisions(this BuildCollisionWorldConfig config, int3 subdivisions)
        {
            config.settings.worldSubdivisionsPerAxis = subdivisions;
            return config;
        }

        /// <summary>
        /// Specifies the worldAabb.min of the CollsionLayerSettings which should be used when building the CollisionWorld
        /// </summary>
        public static BuildCollisionWorldConfig WithWorldMin(this BuildCollisionWorldConfig config, float x, float y, float z)
        {
            config.settings.worldAabb.min = new float3(x, y, z);
            return config;
        }

        /// <summary>
        /// Specifies the worldAabb.max of the CollsionLayerSettings which should be used when building the CollisionWorld
        /// </summary>
        public static BuildCollisionWorldConfig WithWorldMax(this BuildCollisionWorldConfig config, float x, float y, float z)
        {
            config.settings.worldAabb.max = new float3(x, y, z);
            return config;
        }

        /// <summary>
        /// Specifies the worldAabb of the CollsionLayerSettings which should be used when building the CollisionWorld
        /// </summary>
        public static BuildCollisionWorldConfig WithWorldBounds(this BuildCollisionWorldConfig config, float3 min, float3 max)
        {
            var aabb = new Aabb(min, max);
            return config.WithWorldBounds(aabb);
        }

        /// <summary>
        /// Specifies the worldSubdivisionsPerAxis of the CollisionWorldSettings which should be used when building the CollisionWorld
        /// </summary>
        public static BuildCollisionWorldConfig WithSubdivisions(this BuildCollisionWorldConfig config, int x, int y, int z)
        {
            return config.WithSubdivisions(new int3(x, y, z));
        }

        #endregion

        #region Schedulers
        /// <summary>
        /// Generates the CollisionWorld on the same thread using Burst
        /// </summary>
        /// <param name="world">The generated CollisionWorld</param>
        /// <param name="allocator">The allocator to use for allocating the CollisionWorld</param>
        public static void Run(this BuildCollisionWorldConfig config, out CollisionWorld world, AllocatorManager.AllocatorHandle allocator)
        {
            config.ValidateSettings();

            world                  = new CollisionWorld(config.settings, allocator, config.worldIndex);
            var filteredChunkCache = new NativeList<BuildCollisionLayerInternal.FilteredChunkCache>(config.query.CalculateChunkCountWithoutFiltering(), Allocator.TempJob);
            new BuildCollisionLayerInternal.Part0PrefilterQueryJob
            {
                filteredChunkCache = filteredChunkCache
            }.Run(config.query);
            new BuildCollisionWorldInternal.BuildFromFilteredChunkCacheSingleJob
            {
                filteredChunkCache = filteredChunkCache.AsArray(),
                handles            = config.typeGroup,
                world              = world
            }.Run();
            filteredChunkCache.Dispose();
        }

        /// <summary>
        /// Generates the CollisionWorld inside a single-threaded job using Burst
        /// </summary>
        /// <param name="world">The generated CollisionWorld</param>
        /// <param name="allocator">The allocator to use for allocating the CollisionWorld</param>
        /// <param name="inputDeps">A JobHandle the scheduled job should wait on</param>
        /// <returns>The JobHandle associated with the scheduled builder job</returns>
        public static JobHandle ScheduleSingle(this BuildCollisionWorldConfig config,
                                               out CollisionWorld world,
                                               AllocatorManager.AllocatorHandle allocator,
                                               JobHandle inputDeps = default)
        {
            config.ValidateSettings();

            world                  = new CollisionWorld(config.settings, allocator, config.worldIndex);
            var filteredChunkCache = new NativeList<BuildCollisionLayerInternal.FilteredChunkCache>(config.query.CalculateChunkCountWithoutFiltering(), Allocator.TempJob);
            var jh                 = new BuildCollisionLayerInternal.Part0PrefilterQueryJob
            {
                filteredChunkCache = filteredChunkCache
            }.Schedule(config.query, inputDeps);
            jh = new BuildCollisionWorldInternal.BuildFromFilteredChunkCacheSingleJob
            {
                filteredChunkCache = filteredChunkCache.AsDeferredJobArray(),
                handles            = config.typeGroup,
                world              = world
            }.Schedule(jh);
            return filteredChunkCache.Dispose(jh);
        }

        /// <summary>
        /// Generates the CollisionWorld inside a sequence of multi-threaded jobs using Burst
        /// </summary>
        /// <param name="world">The generated CollisionWorld</param>
        /// <param name="allocator">The allocator to use for allocating the CollisionWorld</param>
        /// <param name="inputDeps">A JobHandle the scheduled jobs should wait on</param>
        /// <returns>The JobHandle associated with the scheduled builder jobs</returns>
        public static unsafe JobHandle ScheduleParallel(this BuildCollisionWorldConfig config,
                                                        out CollisionWorld world,
                                                        AllocatorManager.AllocatorHandle allocator,
                                                        JobHandle inputDeps = default)
        {
            config.ValidateSettings();

            world = new CollisionWorld(config.settings, allocator, config.worldIndex);

            var jh = inputDeps;

            var filteredChunkCache = new NativeList<BuildCollisionLayerInternal.FilteredChunkCache>(config.query.CalculateChunkCountWithoutFiltering(), Allocator.TempJob);
            jh                     = new BuildCollisionLayerInternal.Part0PrefilterQueryJob
            {
                filteredChunkCache = filteredChunkCache
            }.Schedule(config.query, jh);

            var sourceArchetypeIndices = new NativeList<short>(Allocator.TempJob);

            var jh1b = new BuildCollisionWorldInternal.Part1bBuildArchetypesFromCacheJob
            {
                archetypeBodyIndicesByBucket     = world.archetypeBodyIndicesByBucket,
                archetypeIndicesByBody           = world.archetypeIndicesByBody,
                archetypeIntervalTreesByBucket   = world.archetypeIntervalTreesByBucket,
                archetypes                       = world.archetypesInLayer,
                archetypeStartsAndCountsByBucket = world.archetypeStartsAndCountsByBucket,
                sourceArchetypeIndices           = sourceArchetypeIndices,
                bucketCountWithNaN               = IndexStrategies.BucketCountWithNaN(world.layer.cellCount),
                chunks                           = filteredChunkCache.AsDeferredJobArray()
            }.Schedule(jh);

            NativeList<int>                                         layerIndices;
            NativeList<float2>                                      xMinMaxs;
            NativeList<BuildCollisionWorldInternal.ColliderAoSData> aos;

            if (config.query.HasFilter() || config.query.UsesEnabledFiltering())
            {
                layerIndices = new NativeList<int>(Allocator.TempJob);
                xMinMaxs     = new NativeList<float2>(Allocator.TempJob);
                aos          = new NativeList<BuildCollisionWorldInternal.ColliderAoSData>(Allocator.TempJob);

                jh = new BuildCollisionWorldInternal.AllocateCollisionLayerFromFilteredQueryJob
                {
                    layer              = world.layer,
                    filteredChunkCache = filteredChunkCache.AsDeferredJobArray(),
                    colliderAoS        = aos,
                    layerIndices       = layerIndices,
                    xMinMaxs           = xMinMaxs
                }.Schedule(jh);
            }
            else
            {
                int count    = config.query.CalculateEntityCountWithoutFiltering();
                layerIndices = new NativeList<int>(count, Allocator.TempJob);
                layerIndices.ResizeUninitialized(count);
                xMinMaxs = new NativeList<float2>(count, Allocator.TempJob);
                xMinMaxs.ResizeUninitialized(count);
                aos = new NativeList<BuildCollisionWorldInternal.ColliderAoSData>(count, Allocator.TempJob);
                aos.ResizeUninitialized(count);
                world.layer.ResizeUninitialized(count);
            }

            jh = new BuildCollisionWorldInternal.Part1FromFilteredQueryJob
            {
                colliderAoS        = aos.AsDeferredJobArray(),
                filteredChunkCache = filteredChunkCache.AsDeferredJobArray(),
                layer              = world.layer,
                layerIndices       = layerIndices.AsDeferredJobArray(),
                typeGroup          = config.typeGroup,
                xMinMaxs           = xMinMaxs.AsDeferredJobArray(),
            }.Schedule(filteredChunkCache, 1, jh);

            jh = new BuildCollisionLayerInternal.Part2Job
            {
                layer        = world.layer,
                layerIndices = layerIndices.AsDeferredJobArray()
            }.Schedule(jh);

            jh = new BuildCollisionLayerInternal.Part3Job
            {
                layerIndices       = layerIndices.AsDeferredJobArray(),
                unsortedSrcIndices = world.layer.srcIndices.AsDeferredJobArray()
            }.Schedule(layerIndices, 512, jh);

            jh = new BuildCollisionLayerInternal.Part4Job
            {
                unsortedSrcIndices   = world.layer.srcIndices.AsDeferredJobArray(),
                xMinMaxs             = xMinMaxs.AsDeferredJobArray(),
                trees                = world.layer.intervalTrees.AsDeferredJobArray(),
                bucketStartAndCounts = world.layer.bucketStartsAndCounts.AsDeferredJobArray()
            }.Schedule(world.layer.bucketCount, 1, jh);

            jh = new BuildCollisionWorldInternal.Part5FromAoSJob
            {
                layer       = world.layer,
                colliderAoS = aos.AsDeferredJobArray(),
            }.Schedule(aos, 128, jh);

            var mjh = JobHandle.CombineDependencies(jh1b, jh);

            var p6jh = new BuildCollisionWorldInternal.Part6BuildBucketArchetypesJob
            {
                archetypeBodyIndicesByBucket     = world.archetypeBodyIndicesByBucket.AsDeferredJobArray(),
                archetypeIndicesByBody           = world.archetypeIndicesByBody.AsDeferredJobArray(),
                archetypeIntervalTreesByBucket   = world.archetypeIntervalTreesByBucket.AsDeferredJobArray(),
                archetypes                       = world.archetypesInLayer.AsDeferredJobArray(),
                archetypeStartsAndCountsByBucket = world.archetypeStartsAndCountsByBucket.AsDeferredJobArray(),
                layer                            = world.layer,
                sourceArchetypeIndices           = sourceArchetypeIndices.AsDeferredJobArray()
            }.ScheduleParallel(IndexStrategies.BucketCountWithNaN(world.layer.cellCount), 1, mjh);

            return CollectionsExtensions.CombineDependencies(stackalloc JobHandle[]
            {
                sourceArchetypeIndices.Dispose(p6jh),
                filteredChunkCache.Dispose(mjh),
                layerIndices.Dispose(jh),
                xMinMaxs.Dispose(jh),
                aos.Dispose(jh),
            });
        }
        #endregion

        #region Validators
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateSettings(this BuildCollisionWorldConfig config)
        {
            if (math.any(config.settings.worldAabb.min > config.settings.worldAabb.max))
                throw new InvalidOperationException("BuildCollisionWorld requires a valid worldBounds AABB");
            if (math.any(config.settings.worldSubdivisionsPerAxis < 1))
                throw new InvalidOperationException("BuildCollisionWorld requires positive Subdivision values per axis");
        }

        #endregion
    }
}

