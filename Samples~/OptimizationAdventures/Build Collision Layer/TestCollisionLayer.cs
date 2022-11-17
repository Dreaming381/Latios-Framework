using System;
using Latios.Psyshock;
using Unity.Collections;
using Unity.Mathematics;

namespace OptimizationAdventures
{
    internal struct TestCollisionLayer : IDisposable
    {
        internal NativeArray<int2>                                               bucketStartsAndCounts;
        [NativeDisableParallelForRestriction] internal NativeArray<float>        xmins;
        [NativeDisableParallelForRestriction] internal NativeArray<float>        xmaxs;
        [NativeDisableParallelForRestriction] internal NativeArray<float4>       yzminmaxs;
        [NativeDisableParallelForRestriction] internal NativeArray<ColliderBody> bodies;
        internal float3                                                          worldMin;
        internal float3                                                          worldAxisStride;
        internal int3                                                            worldSubdivisionsPerAxis;

        //Todo: World settings?
        internal TestCollisionLayer(int bodyCount, CollisionLayerSettings settings, Allocator allocator)
        {
            worldMin                 = settings.worldAabb.min;
            worldAxisStride          = (settings.worldAabb.max - worldMin) / settings.worldSubdivisionsPerAxis;
            worldSubdivisionsPerAxis = settings.worldSubdivisionsPerAxis;

            bucketStartsAndCounts = new NativeArray<int2>(settings.worldSubdivisionsPerAxis.x * settings.worldSubdivisionsPerAxis.y * settings.worldSubdivisionsPerAxis.z + 1,
                                                          allocator,
                                                          NativeArrayOptions.UninitializedMemory);
            xmins     = new NativeArray<float>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            xmaxs     = new NativeArray<float>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            yzminmaxs = new NativeArray<float4>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
            bodies    = new NativeArray<ColliderBody>(bodyCount, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public TestCollisionLayer(TestCollisionLayer sourceLayer, Allocator allocator)
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

        public int Count => xmins.Length;
        public int BucketCount => bucketStartsAndCounts.Length;

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
}

