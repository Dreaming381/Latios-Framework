using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public struct CollisionLayerSettings
    {
        public Aabb worldAABB;
        public int3 worldSubdivisionsPerAxis;
    }

    public struct CollisionLayer : IDisposable
    {
        [NoAlias] internal NativeArray<int2>                                              bucketStartsAndCounts;
        [NoAlias, NativeDisableParallelForRestriction] internal NativeArray<float>        xmins;
        [NoAlias, NativeDisableParallelForRestriction] internal NativeArray<float>        xmaxs;
        [NoAlias, NativeDisableParallelForRestriction] internal NativeArray<float4>       yzminmaxs;
        [NoAlias, NativeDisableParallelForRestriction] internal NativeArray<ColliderBody> bodies;
        internal float3                                                                   worldMin;
        internal float3                                                                   worldAxisStride;
        internal int3                                                                     worldSubdivisionsPerAxis;

        //Todo: World settings?
        internal CollisionLayer(int bodyCount, CollisionLayerSettings settings, Allocator allocator)
        {
            worldMin                 = settings.worldAABB.min;
            worldAxisStride          = (settings.worldAABB.max - worldMin) / settings.worldSubdivisionsPerAxis;
            worldSubdivisionsPerAxis = settings.worldSubdivisionsPerAxis;

            bucketStartsAndCounts = new NativeArray<int2>(settings.worldSubdivisionsPerAxis.x * settings.worldSubdivisionsPerAxis.y * settings.worldSubdivisionsPerAxis.z + 1,
                                                          allocator,
                                                          NativeArrayOptions.UninitializedMemory);
            xmins     = new NativeArray<float>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            xmaxs     = new NativeArray<float>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            yzminmaxs = new NativeArray<float4>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            bodies    = new NativeArray<ColliderBody>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public CollisionLayer(CollisionLayer sourceLayer, Allocator allocator)
        {
            worldMin                 = sourceLayer.worldMin;
            worldAxisStride          = sourceLayer.worldAxisStride;
            worldSubdivisionsPerAxis = sourceLayer.worldSubdivisionsPerAxis;

            bucketStartsAndCounts = new NativeArray<int2>(sourceLayer.bucketStartsAndCounts, allocator);
            xmins                 = new NativeArray<float>(sourceLayer.xmins, allocator);
            xmaxs                 = new NativeArray<float>(sourceLayer.xmaxs, allocator);
            yzminmaxs             = new NativeArray<float4>(sourceLayer.yzminmaxs, allocator);
            bodies                = new NativeArray<ColliderBody>(sourceLayer.bodies, allocator);
        }

        public void Dispose()
        {
            bucketStartsAndCounts.Dispose();
            xmins.Dispose();
            xmaxs.Dispose();
            yzminmaxs.Dispose();
            bodies.Dispose();
        }

        public unsafe JobHandle Dispose(JobHandle inputDeps)
        {
            JobHandle* deps = stackalloc JobHandle[5]
            {
                bucketStartsAndCounts.Dispose(inputDeps),
                xmins.Dispose(inputDeps),
                xmaxs.Dispose(inputDeps),
                yzminmaxs.Dispose(inputDeps),
                bodies.Dispose(inputDeps)
            };
            return Unity.Jobs.LowLevel.Unsafe.JobHandleUnsafeUtility.CombineDependencies(deps, 5);
        }

        public int Count => xmins.Length;
        public int BucketCount => bucketStartsAndCounts.Length;

        public bool IsCreated => bucketStartsAndCounts.IsCreated;

        internal BucketSlices GetBucketSlices(int bucketIndex)
        {
            int start = bucketStartsAndCounts[bucketIndex].x;
            int count = bucketStartsAndCounts[bucketIndex].y;

            return new BucketSlices
            {
                xmins       = xmins.Slice(start, count),
                xmaxs       = xmaxs.Slice(start, count),
                yzminmaxs   = yzminmaxs.Slice(start, count),
                bodies      = bodies.Slice(start, count),
                bucketIndex = bucketIndex
            };
        }
    }

    internal struct BucketSlices
    {
        public NativeSlice<float>        xmins;
        public NativeSlice<float>        xmaxs;
        public NativeSlice<float4>       yzminmaxs;
        public NativeSlice<ColliderBody> bodies;
        public int count => xmins.Length;
        public int bucketIndex;
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
     *       bucketRanges               = new NativeArray<int2>(gridCells1DFromOrigin * gridCells1DFromOrigin + 1, allocator, NativeArrayOptions.UninitializedMemory);
     *       xmin                       = new NativeArray<float>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *       xmax                       = new NativeArray<float>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *       yzminmax                   = new NativeArray<float4>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *       entity                     = new NativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
     *       ray                        = new NativeArray<Ray>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
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

