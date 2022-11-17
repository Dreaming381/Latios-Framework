using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Psyshock
{
    /// <summary>
    /// A struct defining the type handles used when building a CollisionLayer from an EntityQuery.
    /// All handles are ReadOnly. You can cache this structure inside a system to accelerate scheduling costs,
    /// but you must also ensure the handles are updating during each OnUpdate before building any CollisionLayer.
    /// </summary>
    public struct BuildCollisionLayerTypeHandles
    {
        [ReadOnly] public ComponentTypeHandle<Collider>     collider;
        [ReadOnly] public ComponentTypeHandle<Translation>  translation;
        [ReadOnly] public ComponentTypeHandle<Rotation>     rotation;
        [ReadOnly] public ComponentTypeHandle<PhysicsScale> scale;
        [ReadOnly] public ComponentTypeHandle<Parent>       parent;
        [ReadOnly] public ComponentTypeHandle<LocalToWorld> localToWorld;
        [ReadOnly] public EntityTypeHandle                  entity;

        /// <summary>
        /// Constructs the BuildCollsionLayer type handles using a managed system
        /// </summary>
        public BuildCollisionLayerTypeHandles(ComponentSystemBase system)
        {
            collider     = system.GetComponentTypeHandle<Collider>(true);
            translation  = system.GetComponentTypeHandle<Translation>(true);
            rotation     = system.GetComponentTypeHandle<Rotation>(true);
            scale        = system.GetComponentTypeHandle<PhysicsScale>(true);
            parent       = system.GetComponentTypeHandle<Parent>(true);
            localToWorld = system.GetComponentTypeHandle<LocalToWorld>(true);
            entity       = system.GetEntityTypeHandle();
        }

        /// <summary>
        /// Constructs the BuildCollisionLayer type handles using a SystemState
        /// </summary>
        public BuildCollisionLayerTypeHandles(ref SystemState system)
        {
            collider     = system.GetComponentTypeHandle<Collider>(true);
            translation  = system.GetComponentTypeHandle<Translation>(true);
            rotation     = system.GetComponentTypeHandle<Rotation>(true);
            scale        = system.GetComponentTypeHandle<PhysicsScale>(true);
            parent       = system.GetComponentTypeHandle<Parent>(true);
            localToWorld = system.GetComponentTypeHandle<LocalToWorld>(true);
            entity       = system.GetEntityTypeHandle();
        }

        /// <summary>
        /// Updates the type handles using a managed system
        /// </summary>
        public void Update(SystemBase system)
        {
            collider.Update(system);
            translation.Update(system);
            rotation.Update(system);
            scale.Update(system);
            parent.Update(system);
            localToWorld.Update(system);
            entity.Update(system);
        }

        /// <summary>
        /// Updates the type handles using a SystemState
        /// </summary>
        public void Update(ref SystemState system)
        {
            collider.Update(ref system);
            translation.Update(ref system);
            rotation.Update(ref system);
            scale.Update(ref system);
            parent.Update(ref system);
            localToWorld.Update(ref system);
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

        internal NativeArray<int> remapSrcIndices;

        internal CollisionLayerSettings settings;

        internal bool hasQueryData;
        internal bool hasBodiesArray;
        internal bool hasAabbsArray;
        internal bool hasRemapSrcIndices;

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
            return fluent.WithAllWeak<Collider>();
        }

        #region Starters
        /// <summary>
        /// Creates a new CollisionLayer by extracting collider and transform data from the entities in an EntityQuery.
        /// This is a start of a fluent chain.
        /// </summary>
        /// <param name="query">The EntityQuery from which to extract collider and transform data</param>
        /// <param name="system">The system used for extracting ComponentTypeHandles</param>
        public static BuildCollisionLayerConfig BuildCollisionLayer(EntityQuery query, ComponentSystemBase system)
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

        /// <summary>
        /// Specifies a NativeArray that should be written to where when indexed by a bodyIndex in a FindPairsResult or FindObjects result
        /// specifies the original array index of entityInQueryIndex of the ColliderBody in the layer
        /// </summary>
        public static BuildCollisionLayerConfig WithRemapArray(this BuildCollisionLayerConfig config, NativeArray<int> remapSrcIndices)
        {
            ValidateRemapArrayIsRightLength(remapSrcIndices, config.count, config.hasQueryData);

            config.remapSrcIndices    = remapSrcIndices;
            config.hasRemapSrcIndices = true;
            return config;
        }

        /// <summary>
        /// Specifies a NativeArray that should be allocated and written to where when indexed by a bodyIndex in a FindPairsResult or FindObjects result
        /// specifies the original array index of entityInQueryIndex of the ColliderBody in the layer
        /// </summary>
        /// <param name="remapSrcIndices">The generated array containing source indices</param>
        /// <param name="allocator">The allocator to use for allocating the array</param>
        public static BuildCollisionLayerConfig WithRemapArray(this BuildCollisionLayerConfig config,
                                                               out NativeArray<int>             remapSrcIndices,
                                                               AllocatorManager.AllocatorHandle allocator)
        {
            remapSrcIndices = CollectionHelper.CreateNativeArray<int>(config.count, allocator, NativeArrayOptions.UninitializedMemory);

            config.remapSrcIndices    = remapSrcIndices;
            config.hasRemapSrcIndices = true;
            return config;
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
                if (config.hasRemapSrcIndices)
                    BuildCollisionLayerInternal.BuildImmediate(ref layer, config.remapSrcIndices, config.bodies, config.aabbs);
                else
                {
                    var remapArray = new NativeArray<int>(layer.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    BuildCollisionLayerInternal.BuildImmediate(ref layer, remapArray, config.bodies, config.aabbs);
                }
            }
            else if (config.hasBodiesArray)
            {
                layer = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                if (config.hasRemapSrcIndices)
                    BuildCollisionLayerInternal.BuildImmediate(ref layer, config.remapSrcIndices, config.bodies, config.aabbs);
                else
                {
                    var remapArray = new NativeArray<int>(layer.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    BuildCollisionLayerInternal.BuildImmediate(ref layer, remapArray, config.bodies, config.aabbs);
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

                NativeArray<int> remapSrcIndices = config.hasRemapSrcIndices ? config.remapSrcIndices : new NativeArray<int>(count,
                                                                                                                             Allocator.TempJob,
                                                                                                                             NativeArrayOptions.UninitializedMemory);

                new BuildCollisionLayerInternal.Part1FromQueryJob
                {
                    typeGroup    = config.typeGroup,
                    layer        = layer,
                    layerIndices = layerIndices,
                    colliderAoS  = aos,
                    xMinMaxs     = xMinMaxs
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
                    trees                = layer.intervalTrees,
                    xMinMaxs             = xMinMaxs
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

                NativeArray<int> remapSrcIndices = config.hasRemapSrcIndices ? config.remapSrcIndices : new NativeArray<int>(count,
                                                                                                                             Allocator.TempJob,
                                                                                                                             NativeArrayOptions.UninitializedMemory);

                jh = new BuildCollisionLayerInternal.Part1FromQueryJob
                {
                    typeGroup    = config.typeGroup,
                    layer        = layer,
                    layerIndices = layerIndices,
                    colliderAoS  = aos,
                    xMinMaxs     = xMinMaxs
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
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts,
                    unsortedSrcIndices   = remapSrcIndices,
                    trees                = layer.intervalTrees,
                    xMinMaxs             = xMinMaxs
                }.Schedule(jh);

                jh = new BuildCollisionLayerInternal.Part5FromQueryJob
                {
                    colliderAoS     = aos,
                    layer           = layer,
                    remapSrcIndices = remapSrcIndices
                }.Schedule(jh);

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

                NativeArray<int> remapSrcIndices = config.hasRemapSrcIndices ? config.remapSrcIndices : new NativeArray<int>(count,
                                                                                                                             Allocator.TempJob,
                                                                                                                             NativeArrayOptions.UninitializedMemory);

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
                    unsortedSrcIndices = remapSrcIndices
                }.Schedule(count, 512, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    unsortedSrcIndices   = remapSrcIndices,
                    xMinMaxs             = xMinMaxs,
                    trees                = layer.intervalTrees,
                    bucketStartAndCounts = layer.bucketStartsAndCounts
                }.Schedule(layer.BucketCount, 1, jh);

                jh = new BuildCollisionLayerInternal.Part5FromQueryJob
                {
                    layer           = layer,
                    colliderAoS     = aos,
                    remapSrcIndices = remapSrcIndices
                }.Schedule(count, 128, jh);

                if (!config.hasRemapSrcIndices)
                    jh = remapSrcIndices.Dispose(jh);

                return jh;
            }
            else if (config.hasBodiesArray)
            {
                layer            = new CollisionLayer(config.bodies.Length, config.settings, allocator);
                int count        = config.bodies.Length;
                var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var xMinMaxs     = new NativeArray<float2>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

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
                    unsortedSrcIndices = remapSrcIndices
                }.Schedule(count, 512, jh);

                jh = new BuildCollisionLayerInternal.Part4Job
                {
                    bucketStartAndCounts = layer.bucketStartsAndCounts,
                    unsortedSrcIndices   = remapSrcIndices,
                    trees                = layer.intervalTrees,
                    xMinMaxs             = xMinMaxs
                }.Schedule(layer.BucketCount, 1, jh);

                jh = new BuildCollisionLayerInternal.Part5FromArraysJob
                {
                    aabbs           = aabbs,
                    bodies          = config.bodies,
                    layer           = layer,
                    remapSrcIndices = remapSrcIndices
                }.Schedule(count, 128, jh);

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

