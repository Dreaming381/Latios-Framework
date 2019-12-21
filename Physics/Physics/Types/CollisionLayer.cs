using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    public enum CollisionLayerType
    {
        Static,
        Discrete,
        Simulated,
        Continuous,
        StaticTrigger,
        DiscreteTrigger,
        ContinuousTrigger
    }

    public struct CollisionLayerSettings
    {
        public AABB               worldAABB;
        public int3               worldBucketCountPerAxis;
        public CollisionLayerType layerType;
    }

    public struct CollisionLayer : IDisposable
    {
        internal NativeArray<int2>                                              bucketStartsAndCounts;
        [NativeDisableParallelForRestriction] internal NativeArray<float>       xmins;
        [NativeDisableParallelForRestriction] internal NativeArray<float>       xmaxs;
        [NativeDisableParallelForRestriction] internal NativeArray<float4>      yzminmaxs;
        [NativeDisableParallelForRestriction] internal NativeArray<ColliderBody> bodies;
        internal float3                                                         worldMin;
        internal float3                                                         worldAxisStride;
        internal int3                                                           worldBucketCountPerAxis;
        internal CollisionLayerType                                             layerType;

        //Todo: World settings?
        internal CollisionLayer(int bodyCount, CollisionLayerSettings settings, Allocator allocator)
        {
            worldMin                = settings.worldAABB.min;
            worldAxisStride         = (settings.worldAABB.max - worldMin) / settings.worldBucketCountPerAxis;
            worldBucketCountPerAxis = settings.worldBucketCountPerAxis;
            layerType               = settings.layerType;

            bucketStartsAndCounts = new NativeArray<int2>(settings.worldBucketCountPerAxis.x * settings.worldBucketCountPerAxis.y * settings.worldBucketCountPerAxis.z + 1,
                                                          allocator,
                                                          NativeArrayOptions.UninitializedMemory);
            xmins     = new NativeArray<float>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            xmaxs     = new NativeArray<float>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            yzminmaxs = new NativeArray<float4>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            bodies    = new NativeArray<ColliderBody>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public CollisionLayer(CollisionLayer sourceLayer, Allocator allocator)
        {
            worldMin                = sourceLayer.worldMin;
            worldAxisStride         = sourceLayer.worldAxisStride;
            worldBucketCountPerAxis = sourceLayer.worldBucketCountPerAxis;
            layerType               = sourceLayer.layerType;

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

        public int Count => xmins.Length;
        public int BucketCount => bucketStartsAndCounts.Length;

        internal BucketSlices GetBucketSlices(int bucketIndex)
        {
            int start = bucketStartsAndCounts[bucketIndex].x;
            int count = bucketStartsAndCounts[bucketIndex].y;

            return new BucketSlices
            {
                xmins     = xmins.Slice(start, count),
                xmaxs     = xmaxs.Slice(start, count),
                yzminmaxs = yzminmaxs.Slice(start, count),
                bodies    = bodies.Slice(start, count)
            };
        }
    }

    internal struct BucketSlices
    {
        public NativeSlice<float>       xmins;
        public NativeSlice<float>       xmaxs;
        public NativeSlice<float4>      yzminmaxs;
        public NativeSlice<ColliderBody> bodies;
        public int count => xmins.Length;
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

