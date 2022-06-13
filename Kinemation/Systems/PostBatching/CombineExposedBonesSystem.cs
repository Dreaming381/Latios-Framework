using Latios;
using Latios.Psyshock;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation
{
    [DisableAutoCreation]
    public partial class CombineExposedBonesSystem : SubSystem
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = Fluent.WithAll<BoneWorldBounds>(true).WithAll<BoneCullingIndex>(true).Build();

            worldBlackboardEntity.AddCollectionComponent(new ExposedSkeletonBoundsArrays
            {
                allAabbs     = new NativeList<AABB>(Allocator.Persistent),
                batchedAabbs = new NativeList<AABB>(Allocator.Persistent)
            });
        }

        protected override void OnUpdate()
        {
            var exposedCullingIndexManager = worldBlackboardEntity.GetCollectionComponent<ExposedCullingIndexManager>(true, out var cullingJH);
            var boundsArrays               = worldBlackboardEntity.GetCollectionComponent<ExposedSkeletonBoundsArrays>(false);
            cullingJH.Complete();

            var perThreadBitArrays = World.UpdateAllocator.AllocateNativeArray<UnsafeBitArray>(JobsUtility.MaxJobThreadCount);
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
                perThreadBitArrays[i] = default;

            Dependency = new FindDirtyBoundsJob
            {
                boundsHandle       = GetComponentTypeHandle<BoneWorldBounds>(true),
                indexHandle        = GetComponentTypeHandle<BoneCullingIndex>(true),
                maxBitIndex        = exposedCullingIndexManager.maxIndex,
                perThreadBitArrays = perThreadBitArrays,
                allocator          = World.UpdateAllocator.ToAllocator,
                lastSystemVersion  = LastSystemVersion,
            }.ScheduleParallel(m_query, Dependency);

            Dependency = new CollapseBitsJob
            {
                perThreadBitArrays = perThreadBitArrays
            }.Schedule(Dependency);

            var perThreadBoundsArrays = World.UpdateAllocator.AllocateNativeArray<UnsafeList<Aabb> >(JobsUtility.MaxJobThreadCount);
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
                perThreadBoundsArrays[i] = default;

            Dependency = new CombineBoundsPerThreadJob
            {
                boundsHandle            = GetComponentTypeHandle<BoneWorldBounds>(true),
                indexHandle             = GetComponentTypeHandle<BoneCullingIndex>(true),
                maxBitIndex             = exposedCullingIndexManager.maxIndex,
                perThreadBitArrays      = perThreadBitArrays,
                perThreadBoundsArrays   = perThreadBoundsArrays,
                allocator               = World.UpdateAllocator.ToAllocator,
                finalAabbsToResize      = boundsArrays.allAabbs,
                finalBatchAabbsToResize = boundsArrays.batchedAabbs
            }.ScheduleParallel(m_query, Dependency);

            Dependency = new MergeThreadBoundsJob
            {
                perThreadBitArrays    = perThreadBitArrays,
                perThreadBoundsArrays = perThreadBoundsArrays,
                finalAabbs            = boundsArrays.allAabbs,
                finalBatchAabbs       = boundsArrays.batchedAabbs
            }.ScheduleBatch(exposedCullingIndexManager.maxIndex.Value + 1, 32, Dependency);
        }

        [BurstCompile]
        struct FindDirtyBoundsJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<BoneWorldBounds>                   boundsHandle;
            [ReadOnly] public ComponentTypeHandle<BoneCullingIndex>                  indexHandle;
            [ReadOnly] public NativeReference<int>                                   maxBitIndex;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeBitArray> perThreadBitArrays;
            public Allocator                                                         allocator;
            public uint                                                              lastSystemVersion;

            [NativeSetThreadIndex] int m_NativeThreadIndex;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (batchInChunk.DidChange(boundsHandle, lastSystemVersion) || batchInChunk.DidChange(indexHandle, lastSystemVersion))
                {
                    var perThreadBitArray = perThreadBitArrays[m_NativeThreadIndex];
                    if (!perThreadBitArray.IsCreated)
                    {
                        perThreadBitArray = new UnsafeBitArray(CollectionHelper.Align(maxBitIndex.Value + 1, 64),
                                                               allocator,
                                                               NativeArrayOptions.ClearMemory);
                        perThreadBitArrays[m_NativeThreadIndex] = perThreadBitArray;
                    }

                    var indices = batchInChunk.GetNativeArray(indexHandle);
                    for (int i = 0; i < batchInChunk.Count; i++)
                    {
                        perThreadBitArray.Set(indices[i].cullingIndex, true);
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct CollapseBitsJob : IJob
        {
            public NativeArray<UnsafeBitArray> perThreadBitArrays;

            public void Execute()
            {
                int startFrom = -1;
                for (int i = 0; i < perThreadBitArrays.Length; i++)
                {
                    if (perThreadBitArrays[i].IsCreated)
                    {
                        startFrom             = i + 1;
                        perThreadBitArrays[0] = perThreadBitArrays[i];
                        perThreadBitArrays[i] = default;
                        break;
                    }
                }

                if (startFrom == -1)
                {
                    // This happens if no bones have changed. Unlikely but possible.
                    // In this case, we will need to check for this in future jobs.
                    return;
                }

                for (int arrayIndex = startFrom; arrayIndex < perThreadBitArrays.Length; arrayIndex++)
                {
                    if (!perThreadBitArrays[arrayIndex].IsCreated)
                        continue;
                    var dstArray    = perThreadBitArrays[0];
                    var dstArrayPtr = dstArray.Ptr;
                    var srcArrayPtr = perThreadBitArrays[arrayIndex].Ptr;

                    for (int i = 0, bitCount = 0; bitCount < dstArray.Length; i++, bitCount += 64)
                    {
                        dstArrayPtr[i] |= srcArrayPtr[i];
                    }
                }
            }
        }

        [BurstCompile]
        struct CombineBoundsPerThreadJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<BoneWorldBounds>                      boundsHandle;
            [ReadOnly] public ComponentTypeHandle<BoneCullingIndex>                     indexHandle;
            [ReadOnly] public NativeReference<int>                                      maxBitIndex;
            [ReadOnly] public NativeArray<UnsafeBitArray>                               perThreadBitArrays;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeList<Aabb> > perThreadBoundsArrays;
            public Allocator                                                            allocator;

            [NativeDisableParallelForRestriction] public NativeList<AABB> finalAabbsToResize;
            [NativeDisableParallelForRestriction] public NativeList<AABB> finalBatchAabbsToResize;

            [NativeSetThreadIndex] int m_NativeThreadIndex;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (!perThreadBitArrays[0].IsCreated)
                    return;

                var perThreadBoundsArray = perThreadBoundsArrays[m_NativeThreadIndex];
                if (!perThreadBoundsArray.IsCreated)
                {
                    perThreadBoundsArray = new UnsafeList<Aabb>(maxBitIndex.Value + 1, allocator, NativeArrayOptions.UninitializedMemory);
                    perThreadBoundsArray.Resize(maxBitIndex.Value + 1);
                    perThreadBoundsArrays[m_NativeThreadIndex] = perThreadBoundsArray;
                    for (int i = 0; i < maxBitIndex.Value + 1; i++)
                    {
                        perThreadBoundsArray[i] = new Aabb(float.MaxValue, float.MinValue);
                    }
                }

                var indices = batchInChunk.GetNativeArray(indexHandle);
                var bounds  = batchInChunk.GetNativeArray(boundsHandle);
                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var index = indices[i].cullingIndex;
                    if (perThreadBitArrays[0].IsSet(index))
                    {
                        perThreadBoundsArray[index] = Physics.CombineAabb(perThreadBoundsArray[index], bounds[i].bounds);
                    }
                }

                if (batchIndex == 0)
                {
                    // We do the resizing in this job to remove a single-threaded bubble.
                    int indexCount = maxBitIndex.Value + 1;
                    if (finalAabbsToResize.Length < indexCount)
                    {
                        finalAabbsToResize.Length = indexCount;

                        int batchCount = indexCount / 16;
                        if (indexCount % 16 != 0)
                            batchCount++;

                        if (finalBatchAabbsToResize.Length < batchCount)
                        {
                            finalBatchAabbsToResize.Length = batchCount;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct MergeThreadBoundsJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<UnsafeBitArray>    perThreadBitArrays;
            [ReadOnly] public NativeArray<UnsafeList<Aabb> > perThreadBoundsArrays;

            [NativeDisableParallelForRestriction] public NativeList<AABB> finalAabbs;
            [NativeDisableParallelForRestriction] public NativeList<AABB> finalBatchAabbs;

            public void Execute(int startIndex, int count)
            {
                if (!perThreadBitArrays[0].IsCreated)
                    return;

                BitField32               mergeMask = default;
                FixedList4096Bytes<Aabb> cache     = default;
                Aabb                     batchAabb = new Aabb(float.MaxValue, float.MinValue);
                for (int i = 0; i < count; i++)
                {
                    if (perThreadBitArrays[0].IsSet(i + startIndex))
                    {
                        mergeMask.SetBits(i, true);
                        cache.Add(new Aabb(float.MaxValue, float.MinValue));
                    }
                    else
                    {
                        var aabb = new Aabb(finalAabbs[startIndex + i].Min, finalAabbs[startIndex + i].Max);
                        cache.Add(aabb);
                        batchAabb = Physics.CombineAabb(batchAabb, aabb);
                    }
                }

                if (mergeMask.Value == 0)
                    return;

                for (int threadIndex = 0; threadIndex < perThreadBoundsArrays.Length; threadIndex++)
                {
                    if (!perThreadBoundsArrays[threadIndex].IsCreated)
                        continue;

                    var tempMask = mergeMask;
                    for (int i = tempMask.CountTrailingZeros(); i < count; tempMask.SetBits(i, false), i = tempMask.CountTrailingZeros())
                    {
                        cache[i]  = Physics.CombineAabb(cache[i], perThreadBoundsArrays[threadIndex][startIndex + i]);
                        batchAabb = Physics.CombineAabb(batchAabb, perThreadBoundsArrays[threadIndex][startIndex + i]);
                    }
                }

                {
                    var tempMask = mergeMask;
                    for (int i = tempMask.CountTrailingZeros(); i < count; tempMask.SetBits(i, false), i = tempMask.CountTrailingZeros())
                    {
                        finalAabbs[startIndex + i] = FromAabb(cache[i]);
                    }
                    finalBatchAabbs[startIndex / 32] = FromAabb(batchAabb);
                }
            }

            public static AABB FromAabb(Aabb aabb)
            {
                Physics.GetCenterExtents(aabb, out float3 center, out float3 extents);
                return new AABB { Center = center, Extents = extents };
            }
        }
    }
}

