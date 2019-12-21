using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.PhysicsEngine.Tests
{
    [BurstCompile]
    public struct NaiveSweep : IJob
    {
        [ReadOnly] public NativeArray<AabbEntity> aabbs;
        public NativeList<EntityPair>             overlaps;

        public void Execute()
        {
            for (int i = 0; i < aabbs.Length - 1; i++)
            {
                AabbEntity current = aabbs[i];

                for (int j = i + 1; j < aabbs.Length && aabbs[j].aabb.min.x <= current.aabb.max.x; j++)
                {
                    if (!(current.aabb.max.y < aabbs[j].aabb.min.y ||
                          current.aabb.min.y > aabbs[j].aabb.max.y ||
                          current.aabb.max.z < aabbs[j].aabb.min.z ||
                                               current.aabb.min.z > aabbs[j].aabb.max.z))
                    {
                        overlaps.Add(new EntityPair(current.entity, aabbs[j].entity));
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct Bool4Sweep : IJob
    {
        [ReadOnly] public NativeArray<AabbEntity> aabbs;
        public NativeList<EntityPair>             overlaps;

        public void Execute()
        {
            for (int i = 0; i < aabbs.Length - 1; i++)
            {
                AabbEntity current = aabbs[i];

                for (int j = i + 1; j < aabbs.Length && aabbs[j].aabb.min.x <= current.aabb.max.x; j++)
                {
                    bool4 invalidated;
                    invalidated.x = current.aabb.max.y < aabbs[j].aabb.min.y;
                    invalidated.y = current.aabb.min.y > aabbs[j].aabb.max.y;
                    invalidated.z = current.aabb.max.z < aabbs[j].aabb.min.z;
                    invalidated.w = current.aabb.min.z > aabbs[j].aabb.max.z;
                    if (!math.any(invalidated))
                    {
                        overlaps.Add(new EntityPair(current.entity, aabbs[j].entity));
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct BoolPierreSweep : IJob
    {
        [ReadOnly] public NativeArray<AabbEntity> aabbs;
        public NativeList<EntityPair>             overlaps;

        public void Execute()
        {
            for (int i = 0; i < aabbs.Length - 1; i++)
            {
                AabbEntity current = aabbs[i];

                for (int j = i + 1; j < aabbs.Length && aabbs[j].aabb.min.x <= current.aabb.max.x; j++)
                {
                    bool x      = current.aabb.max.y < aabbs[j].aabb.min.y;
                    bool y      = current.aabb.min.y > aabbs[j].aabb.max.y;
                    bool z      = current.aabb.max.z < aabbs[j].aabb.min.z;
                    bool w      = current.aabb.min.z > aabbs[j].aabb.max.z;
                    bool result = x | y | z | w;
                    if (!result)
                    {
                        overlaps.Add(new EntityPair(current.entity, aabbs[j].entity));
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct LessNaiveSweep : IJob
    {
        [ReadOnly] public NativeArray<AabbEntity> aabbs;
        public NativeList<EntityPair>             overlaps;

        public void Execute()
        {
            for (int i = 0; i < aabbs.Length - 1; i++)
            {
                AabbEntity current = aabbs[i];

                for (int j = i + 1; j < aabbs.Length && aabbs[j].aabb.min.x <= current.aabb.max.x; j++)
                {
                    int invalidatedx = math.select(0, 1, current.aabb.max.y < aabbs[j].aabb.min.y);
                    int invalidatedy = math.select(0, 2, current.aabb.min.y > aabbs[j].aabb.max.y);
                    int invalidatedz = math.select(0, 4, current.aabb.max.z < aabbs[j].aabb.min.z);
                    int invalidatedw = math.select(0, 8, current.aabb.min.z > aabbs[j].aabb.max.z);
                    if (0 == (invalidatedx | invalidatedy | invalidatedz | invalidatedw))
                    {
                        overlaps.Add(new EntityPair(current.entity, aabbs[j].entity));
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct FunnySweep : IJob
    {
        [ReadOnly] public NativeArray<AabbEntity> aabbs;
        public NativeList<EntityPair>             overlaps;

        public void Execute()
        {
            for (int i = 0; i < aabbs.Length - 1; i++)
            {
                AabbEntity current = aabbs[i];

                for (int j = i + 1; j < aabbs.Length && aabbs[j].aabb.min.x <= current.aabb.max.x; j++)
                {
                    bool invalidatedx = current.aabb.max.y < aabbs[j].aabb.min.y;
                    bool invalidatedy = current.aabb.min.y > aabbs[j].aabb.max.y;
                    bool invalidatedz = current.aabb.max.z < aabbs[j].aabb.min.z;
                    bool invalidatedw = current.aabb.min.z > aabbs[j].aabb.max.z;
                    bool xy           = invalidatedx | invalidatedy;
                    bool zw           = invalidatedz | invalidatedw;
                    if (!(xy | zw))
                    {
                        overlaps.Add(new EntityPair(current.entity, aabbs[j].entity));
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct BetterSweep : IJob
    {
        [ReadOnly] public NativeArray<AabbEntity> aabbs;
        public NativeList<EntityPair>             overlaps;

        public void Execute()
        {
            for (int i = 0; i < aabbs.Length - 1; i++)
            {
                AabbEntity current = aabbs[i];

                for (int j = i + 1; j < aabbs.Length && aabbs[j].aabb.min.x <= current.aabb.max.x; j++)
                {
                    float4 less = new float4(current.aabb.max.y, aabbs[j].aabb.max.y, current.aabb.max.z, aabbs[j].aabb.max.z);
                    float4 more = new float4(aabbs[j].aabb.min.y, current.aabb.min.y, aabbs[j].aabb.min.z, current.aabb.min.z);

                    //bool4 tests = less < more;
                    //if (!math.any(tests))
                    if (!math.any(less < more))
                    {
                        overlaps.Add(new EntityPair(current.entity, aabbs[j].entity));
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct NewSweep : IJob
    {
        [ReadOnly] public NativeArray<AabbEntity> aabbs;
        public NativeList<EntityPair>             overlaps;

        public void Execute()
        {
            for (int i = 0; i < aabbs.Length - 1; i++)
            {
                AabbEntity current = aabbs[i];

                for (int j = i + 1; j < aabbs.Length && aabbs[j].aabb.min.x <= current.aabb.max.x; j++)
                {
                    float4 less = new float4(current.aabb.max.y, aabbs[j].aabb.max.y, current.aabb.max.z, aabbs[j].aabb.max.z);
                    float4 more = new float4(aabbs[j].aabb.min.y, current.aabb.min.y, aabbs[j].aabb.min.z, current.aabb.min.z);

                    if (math.bitmask(less < more) == 0)
                    {
                        overlaps.Add(new EntityPair(current.entity, aabbs[j].entity));
                    }
                }
            }
        }
    }
}

