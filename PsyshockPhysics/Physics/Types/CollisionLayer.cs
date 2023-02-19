using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// The settings used to create a CollisionLayer
    /// </summary>
    /// <remarks>
    /// A collision layer divides a worldAabb into cells. All element AABBs get binned into cells
    /// which reduces the number of tests and improves parallelism. AABBs that span across multiple
    /// cells will be categorized in a "catch-all" cell that is tested against all other cells.
    /// Each cell contains its own additional acceleration structures. For extremely high element
    /// counts, a cell with several thousand elements may be acceptable.
    /// There is often a "sweet spot" for reducing the number of elements per cell without too many
    /// elements ending up in the "catch-all", and this will lead to the best performance.
    /// Element AABBs outside of the worldAabb will be binned into surface cells based on their
    /// projection to the surface of worldAabb. What this means is that CollisionLayerSettings
    /// in no way affect the correctness of the algorithms and only serve as a way to tune the
    /// mechanisms for better performance. It is recommended to ignore outliers and focus the
    /// worldAabb to encapsulate the majority of the elements.
    /// </remarks>
    public struct CollisionLayerSettings
    {
        /// <summary>
        /// An AABB which defines the bounds of the subdivision grid.
        /// Elements do not necessarily need to fit inside of it.
        /// </summary>
        public Aabb worldAabb;
        /// <summary>
        /// How many "cells" to divide the worldAabb into.
        /// </summary>
        public int3 worldSubdivisionsPerAxis;
    }

    /// <summary>
    /// A spatial query acceleration structure composed of native containers
    /// </summary>
    /// <remarks>
    /// This spatial query structure is composed of "cells" where each cell contains a batch of
    /// elements sorted by their AABB's minimum x component along with an interval tree of x-axis
    /// spans. Testing a full cell uses a highly optimized single-axis sweep-and-prune.
    /// Immediate queries use a combination of sweeping algorithms and traversal of the interval tree.
    /// Cells do not have a maximum capacity, but are are composed of spans of arrays.
    /// A CollisionLayer uses O(n) memory and has O(n) build times.
    /// It is possible (and often recommended) to build many CollisionLayers and test them against
    /// each other, as long as the CollisionLayers were built with the same CollisionLayerSettings.
    /// AABBs with NaN components are placed in a special cell that is never tested.
    /// </remarks>
    public struct CollisionLayer : INativeDisposable
    {
        internal NativeArray<int2>                                                   bucketStartsAndCounts;
        [NativeDisableParallelForRestriction] internal NativeArray<float>            xmins;
        [NativeDisableParallelForRestriction] internal NativeArray<float>            xmaxs;
        [NativeDisableParallelForRestriction] internal NativeArray<float4>           yzminmaxs;
        [NativeDisableParallelForRestriction] internal NativeArray<IntervalTreeNode> intervalTrees;
        [NativeDisableParallelForRestriction] internal NativeArray<ColliderBody>     bodies;
        internal float3                                                              worldMin;
        internal float3                                                              worldAxisStride;
        internal int3                                                                worldSubdivisionsPerAxis;
        AllocatorManager.AllocatorHandle                                             allocator;

        internal CollisionLayer(int bodyCount, CollisionLayerSettings settings, AllocatorManager.AllocatorHandle allocator)
        {
            worldMin                 = settings.worldAabb.min;
            worldAxisStride          = (settings.worldAabb.max - worldMin) / settings.worldSubdivisionsPerAxis;
            worldSubdivisionsPerAxis = settings.worldSubdivisionsPerAxis;

            bucketStartsAndCounts = CollectionHelper.CreateNativeArray<int2>(
                settings.worldSubdivisionsPerAxis.x * settings.worldSubdivisionsPerAxis.y * settings.worldSubdivisionsPerAxis.z + 2,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            xmins          = CollectionHelper.CreateNativeArray<float>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            xmaxs          = CollectionHelper.CreateNativeArray<float>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            yzminmaxs      = CollectionHelper.CreateNativeArray<float4>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            intervalTrees  = CollectionHelper.CreateNativeArray<IntervalTreeNode>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            bodies         = CollectionHelper.CreateNativeArray<ColliderBody>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            this.allocator = allocator;
        }

        /// <summary>
        /// Copy a CollisionLayer
        /// </summary>
        /// <param name="sourceLayer">The layer to copy from</param>
        /// <param name="allocator">The allocator to use for the new layer</param>
        public CollisionLayer(in CollisionLayer sourceLayer, AllocatorManager.AllocatorHandle allocator)
        {
            worldMin                 = sourceLayer.worldMin;
            worldAxisStride          = sourceLayer.worldAxisStride;
            worldSubdivisionsPerAxis = sourceLayer.worldSubdivisionsPerAxis;

            bucketStartsAndCounts = CollectionHelper.CreateNativeArray(sourceLayer.bucketStartsAndCounts, allocator);
            xmins                 = CollectionHelper.CreateNativeArray(sourceLayer.xmins, allocator);
            xmaxs                 = CollectionHelper.CreateNativeArray(sourceLayer.xmaxs, allocator);
            yzminmaxs             = CollectionHelper.CreateNativeArray(sourceLayer.yzminmaxs, allocator);
            intervalTrees         = CollectionHelper.CreateNativeArray(sourceLayer.intervalTrees, allocator);
            bodies                = CollectionHelper.CreateNativeArray(sourceLayer.bodies, allocator);
            this.allocator        = allocator;
        }

        /// <summary>
        /// Disposes the layer immediately
        /// </summary>
        public void Dispose()
        {
            worldSubdivisionsPerAxis = 0;
            CollectionHelper.DisposeNativeArray(bucketStartsAndCounts, allocator);
            CollectionHelper.DisposeNativeArray(xmins,                 allocator);
            CollectionHelper.DisposeNativeArray(xmaxs,                 allocator);
            CollectionHelper.DisposeNativeArray(yzminmaxs,             allocator);
            CollectionHelper.DisposeNativeArray(intervalTrees,         allocator);
            CollectionHelper.DisposeNativeArray(bodies,                allocator);
        }

        /// <summary>
        /// Disposes the layer using jobs
        /// </summary>
        /// <param name="inputDeps">A JobHandle to wait upon before disposing</param>
        /// <returns>The final jobHandle of the disposed layers</returns>
        public unsafe JobHandle Dispose(JobHandle inputDeps)
        {
            worldSubdivisionsPerAxis = 0;
            if (allocator.IsCustomAllocator)
            {
                // Todo: No DisposeJob for NativeArray from CollectionHelper
                inputDeps.Complete();
                Dispose();
                return default;
            }
            JobHandle* deps = stackalloc JobHandle[6]
            {
                bucketStartsAndCounts.Dispose(inputDeps),
                xmins.Dispose(inputDeps),
                xmaxs.Dispose(inputDeps),
                yzminmaxs.Dispose(inputDeps),
                intervalTrees.Dispose(inputDeps),
                bodies.Dispose(inputDeps)
            };
            return Unity.Jobs.LowLevel.Unsafe.JobHandleUnsafeUtility.CombineDependencies(deps, 6);
        }

        /// <summary>
        /// The number of elements in the layer
        /// </summary>
        public int Count => xmins.Length;
        /// <summary>
        /// The number of cells in the layer, including the "catch-all" cell but ignoring the NaN cell
        /// </summary>
        public int BucketCount => bucketStartsAndCounts.Length - 1;  // For algorithmic purposes, we pretend the nan bucket doesn't exist.
        /// <summary>
        /// True if the CollisionLayer has been created
        /// </summary>
        public bool IsCreated => worldSubdivisionsPerAxis.x > 0;
        /// <summary>
        /// Read-Only access to the collider bodies stored in the CollisionLayer ordered by bodyIndex
        /// </summary>
        public NativeArray<ColliderBody>.ReadOnly colliderBodies => bodies.AsReadOnly();

        internal BucketSlices GetBucketSlices(int bucketIndex)
        {
            int start = bucketStartsAndCounts[bucketIndex].x;
            int count = bucketStartsAndCounts[bucketIndex].y;

            return new BucketSlices
            {
                xmins             = xmins.GetSubArray(start, count),
                xmaxs             = xmaxs.GetSubArray(start, count),
                yzminmaxs         = yzminmaxs.GetSubArray(start, count),
                intervalTree      = intervalTrees.GetSubArray(start, count),
                bodies            = bodies.GetSubArray(start, count),
                bucketIndex       = bucketIndex,
                bucketGlobalStart = start
            };
        }
    }

    internal struct BucketSlices
    {
        public NativeArray<float>            xmins;
        public NativeArray<float>            xmaxs;
        public NativeArray<float4>           yzminmaxs;
        public NativeArray<IntervalTreeNode> intervalTree;
        public NativeArray<ColliderBody>     bodies;
        public int count => xmins.Length;
        public int bucketIndex;
        public int bucketGlobalStart;
    }

    internal struct IntervalTreeNode
    {
        public float xmin;
        public float xmax;
        public float subtreeXmax;
        public int   bucketRelativeBodyIndex;
    }

    /*public struct RayQueryLayer : IDisposable
     *  {
     *   public NativeArray<int2>   bucketRanges;
     *   public NativeArray<float>  xmin;
     *   public NativeArray<float>  xmax;
     *   public NativeArray<float4> yzminmax;
     *   public NativeArray<Entity> entity;
     *   public NativeArray<Ray>    ray;
     *   public float               gridSpacing;
     *   public int                 gridCells1DFromOrigin;
     *
     *   public RayQueryLayer(EntityQuery query, int gridCells1DFromOrigin, float worldHalfExtent, Allocator allocator)
     *   {
     *       this.gridCells1DFromOrigin = gridCells1DFromOrigin;
     *       gridSpacing                = worldHalfExtent / gridCells1DFromOrigin;
     *       int entityCount            = query.CalculateLength();
     *       bucketRanges               = CollectionHelper.CreateNativeArray<int2>(gridCells1DFromOrigin * gridCells1DFromOrigin + 1, allocator, NativeArrayOptions.UninitializedMemory);
     *       xmin                       = CollectionHelper.CreateNativeArray<float>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *       xmax                       = CollectionHelper.CreateNativeArray<float>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *       yzminmax                   = CollectionHelper.CreateNativeArray<float4>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *       entity                     = CollectionHelper.CreateNativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *       ray                        = CollectionHelper.CreateNativeArray<Ray>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *   }
     *
     *   public void Dispose()
     *   {
     *       bucketRanges.Dispose();
     *       xmin.Dispose();
     *       xmax.Dispose();
     *       yzminmax.Dispose();
     *       entity.Dispose();
     *       ray.Dispose();
     *   }
     *  }*/
}

