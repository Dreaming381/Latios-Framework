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
        [NativeDisableParallelForRestriction] internal NativeArray<int>              srcIndices;
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
            srcIndices     = CollectionHelper.CreateNativeArray<int>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
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
            srcIndices            = CollectionHelper.CreateNativeArray(sourceLayer.srcIndices, allocator);
            this.allocator        = allocator;
        }

        /// <summary>
        /// Creates an empty CollisionLayer. This is useful when you just need an empty layer in order to reuse some other codepath.
        /// However, if you need a normal layer, you should use Physics.BuildCollisionLayer() instead.
        /// </summary>
        /// <param name="settings">The settings to use for the layer. You typically want to match this with other layers when using FindPairs.</param>
        /// <param name="allocator">The Allocator to use for this layer. Despite being empty, this layer is still allocated and may require disposal.</param>
        /// <returns>A CollisionLayer with zero bodies, but with the bucket distribution matching the specified settings</returns>
        public static CollisionLayer CreateEmptyCollisionLayer(CollisionLayerSettings settings, AllocatorManager.AllocatorHandle allocator)
        {
            var layer = new CollisionLayer(0, settings, allocator);
            for (int i = 0; i < layer.bucketStartsAndCounts.Length; i++)
                layer.bucketStartsAndCounts[i] = 0;
            layer.worldSubdivisionsPerAxis.x   = math.max(1, layer.worldSubdivisionsPerAxis.x);  // Ensure IsCreated is true.
            return layer;
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
            CollectionHelper.DisposeNativeArray(srcIndices,            allocator);
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
            JobHandle* deps = stackalloc JobHandle[7]
            {
                bucketStartsAndCounts.Dispose(inputDeps),
                xmins.Dispose(inputDeps),
                xmaxs.Dispose(inputDeps),
                yzminmaxs.Dispose(inputDeps),
                intervalTrees.Dispose(inputDeps),
                bodies.Dispose(inputDeps),
                srcIndices.Dispose(inputDeps)
            };
            return Unity.Jobs.LowLevel.Unsafe.JobHandleUnsafeUtility.CombineDependencies(deps, 7);
        }

        /// <summary>
        /// The number of elements in the layer
        /// </summary>
        public int count => xmins.Length;
        /// <summary>
        /// The number of cells in the layer, including the "catch-all" cell but ignoring the NaN cell
        /// </summary>
        public int bucketCount => bucketStartsAndCounts.Length - 1;  // For algorithmic purposes, we pretend the nan bucket doesn't exist.
        /// <summary>
        /// True if the CollisionLayer has been created
        /// </summary>
        public bool IsCreated => worldSubdivisionsPerAxis.x > 0;
        /// <summary>
        /// Read-Only access to the collider bodies stored in the CollisionLayer ordered by bodyIndex
        /// </summary>
        public NativeArray<ColliderBody>.ReadOnly colliderBodies => bodies.AsReadOnly();
        /// <summary>
        /// Read-Only access to the source indices corresponding to each bodyIndex. CollisionLayers
        /// reorder bodies for better performance. The source indices specify the original index of
        /// each body in an EntityQuery or NativeArray of ColliderBody.
        /// </summary>
        public NativeArray<int>.ReadOnly sourceIndices => srcIndices.AsReadOnly();
        /// <summary>
        /// Gets an Aabb for an associated index in the collision layer ordered by bodyIndex
        /// </summary>
        public Aabb GetAabb(int index)
        {
            var yzminmax = yzminmaxs[index];
            var xmin     = xmins[index];
            var xmax     = xmaxs[index];
            return new Aabb(new float3(xmin, yzminmax.xy), new float3(xmax, -yzminmax.zw));
        }

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
                srcIndices        = srcIndices.GetSubArray(start, count),
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
        public NativeArray<int>              srcIndices;
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
}

