using Latios.Psyshock;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CombineExposedBonesSystem : ISystem
    {
        EntityQuery m_boneQuery;
        EntityQuery m_skeletonQuery;

        LatiosWorldUnmanaged latiosWorld;

        ComponentTypeHandle<BoneWorldBounds>  m_boneWorldBoundsHandle;
        ComponentTypeHandle<BoneCullingIndex> m_boneCullingIndexHandle;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_boneQuery     = state.Fluent().With<BoneWorldBounds>(true).With<BoneCullingIndex>(true).Build();
            m_skeletonQuery = state.Fluent().With<ExposedSkeletonCullingIndex>(true).With<SkeletonBoundsOffsetFromMeshes>(true).Build();

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new ExposedSkeletonBoundsArrays
            {
                allAabbs          = new NativeList<AABB>(Allocator.Persistent),
                batchedAabbs      = new NativeList<AABB>(Allocator.Persistent),
                allAabbsPreOffset = new NativeList<AABB>(Allocator.Persistent),
                meshOffsets       = new NativeList<float>(Allocator.Persistent)
            });

            m_boneWorldBoundsHandle  = state.GetComponentTypeHandle<BoneWorldBounds>(true);
            m_boneCullingIndexHandle = state.GetComponentTypeHandle<BoneCullingIndex>(true);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_boneWorldBoundsHandle.Update(ref state);
            m_boneCullingIndexHandle.Update(ref state);

            var exposedCullingIndexManager = latiosWorld.worldBlackboardEntity.GetCollectionComponent<ExposedCullingIndexManager>(true);
            var boundsArrays               = latiosWorld.worldBlackboardEntity.GetCollectionComponent<ExposedSkeletonBoundsArrays>(false);

            var perThreadBitArrays = state.WorldUnmanaged.UpdateAllocator.AllocateNativeArray<UnsafeBitArray>(JobsUtility.MaxJobThreadCount);
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
                perThreadBitArrays[i] = default;

            var bonesJH = new FindDirtyBoundsJob
            {
                boundsHandle       = m_boneWorldBoundsHandle,
                indexHandle        = m_boneCullingIndexHandle,
                maxBitIndex        = exposedCullingIndexManager.maxIndex,
                perThreadBitArrays = perThreadBitArrays,
                allocator          = state.WorldUpdateAllocator,
                lastSystemVersion  = state.LastSystemVersion,
            }.ScheduleParallel(m_boneQuery, state.Dependency);

            bonesJH = new CollapseBitsJob
            {
                perThreadBitArrays = perThreadBitArrays
            }.Schedule(bonesJH);

            var perThreadBoundsArrays = state.WorldUnmanaged.UpdateAllocator.AllocateNativeArray<UnsafeList<Aabb> >(JobsUtility.MaxJobThreadCount);
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
                perThreadBoundsArrays[i] = default;

            bonesJH = new CombineBoundsPerThreadJob
            {
                boundsHandle                 = m_boneWorldBoundsHandle,
                indexHandle                  = m_boneCullingIndexHandle,
                maxBitIndex                  = exposedCullingIndexManager.maxIndex,
                perThreadBitArrays           = perThreadBitArrays,
                perThreadBoundsArrays        = perThreadBoundsArrays,
                allocator                    = state.WorldUpdateAllocator,
                finalAabbsToResize           = boundsArrays.allAabbs,
                finalBatchAabbsToResize      = boundsArrays.batchedAabbs,
                finalAabbsPreOffsetsToResize = boundsArrays.allAabbsPreOffset,
                offsetsByIndexToResize       = boundsArrays.meshOffsets,
            }.ScheduleParallel(m_boneQuery, bonesJH);

            var skeletonsJH = new CollectSkeletonMeshBoundsOffsetJob
            {
                indexHandle    = GetComponentTypeHandle<ExposedSkeletonCullingIndex>(true),
                offsetsHandle  = GetComponentTypeHandle<SkeletonBoundsOffsetFromMeshes>(true),
                offsetsByIndex = boundsArrays.meshOffsets.AsDeferredJobArray()
            }.ScheduleParallel(m_skeletonQuery, bonesJH);

            state.Dependency = new MergeThreadBoundsJob
            {
                perThreadBitArrays    = perThreadBitArrays,
                perThreadBoundsArrays = perThreadBoundsArrays,
                offsetsByIndex        = boundsArrays.meshOffsets.AsDeferredJobArray(),
                finalAabbsPreOffsets  = boundsArrays.allAabbsPreOffset.AsDeferredJobArray(),
                finalAabbs            = boundsArrays.allAabbs.AsDeferredJobArray(),
                finalBatchAabbs       = boundsArrays.batchedAabbs.AsDeferredJobArray()
            }.ScheduleBatch(exposedCullingIndexManager.maxIndex.Value + 1, 32, skeletonsJH);
        }

        [BurstCompile]
        struct FindDirtyBoundsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<BoneWorldBounds>                   boundsHandle;
            [ReadOnly] public ComponentTypeHandle<BoneCullingIndex>                  indexHandle;
            [ReadOnly] public NativeReference<int>                                   maxBitIndex;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeBitArray> perThreadBitArrays;
            public Allocator                                                         allocator;
            public uint                                                              lastSystemVersion;

            [NativeSetThreadIndex] int m_NativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.DidChange(ref boundsHandle, lastSystemVersion) || chunk.DidChange(ref indexHandle, lastSystemVersion))
                {
                    var perThreadBitArray = perThreadBitArrays[m_NativeThreadIndex];
                    if (!perThreadBitArray.IsCreated)
                    {
                        perThreadBitArray = new UnsafeBitArray(CollectionHelper.Align(maxBitIndex.Value + 1, 64),
                                                               allocator,
                                                               NativeArrayOptions.ClearMemory);
                        perThreadBitArrays[m_NativeThreadIndex] = perThreadBitArray;
                    }

                    var indices = chunk.GetNativeArray(ref indexHandle);
                    for (int i = 0; i < chunk.Count; i++)
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
        struct CombineBoundsPerThreadJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<BoneWorldBounds>                      boundsHandle;
            [ReadOnly] public ComponentTypeHandle<BoneCullingIndex>                     indexHandle;
            [ReadOnly] public NativeReference<int>                                      maxBitIndex;
            [ReadOnly] public NativeArray<UnsafeBitArray>                               perThreadBitArrays;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeList<Aabb> > perThreadBoundsArrays;
            public Allocator                                                            allocator;

            [NativeDisableParallelForRestriction] public NativeList<AABB>  finalAabbsToResize;
            [NativeDisableParallelForRestriction] public NativeList<AABB>  finalAabbsPreOffsetsToResize;
            [NativeDisableParallelForRestriction] public NativeList<AABB>  finalBatchAabbsToResize;
            [NativeDisableParallelForRestriction] public NativeList<float> offsetsByIndexToResize;

            [NativeSetThreadIndex] int m_NativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
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

                var indices = chunk.GetNativeArray(ref indexHandle);
                var bounds  = chunk.GetNativeArray(ref boundsHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var index = indices[i].cullingIndex;
                    if (perThreadBitArrays[0].IsSet(index))
                    {
                        perThreadBoundsArray[index] = Physics.CombineAabb(perThreadBoundsArray[index], bounds[i].bounds);
                    }
                }

                if (unfilteredChunkIndex == 0)
                {
                    // We do the resizing in this job to remove a single-threaded bubble.
                    int indexCount = maxBitIndex.Value + 1;
                    if (finalAabbsToResize.Length < indexCount)
                    {
                        finalAabbsToResize.Length           = indexCount;
                        finalAabbsPreOffsetsToResize.Length = indexCount;
                        offsetsByIndexToResize.Length       = indexCount;

                        int batchCount = indexCount / 32;
                        if (indexCount % 32 != 0)
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
        struct CollectSkeletonMeshBoundsOffsetJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ExposedSkeletonCullingIndex>    indexHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonBoundsOffsetFromMeshes> offsetsHandle;

            [NativeDisableParallelForRestriction] public NativeArray<float> offsetsByIndex;
            public uint                                                     lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!(chunk.DidChange(ref indexHandle, lastSystemVersion) || chunk.DidChange(ref offsetsHandle, lastSystemVersion)))
                    return;

                var indices    = chunk.GetNativeArray(ref indexHandle);
                var srcOffsets = chunk.GetNativeArray(ref offsetsHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var index = indices[i].cullingIndex;
                    if (index < offsetsByIndex.Length)
                        offsetsByIndex[index] = srcOffsets[i].radialBoundsInWorldSpace;
                }
            }
        }

        [BurstCompile]
        struct MergeThreadBoundsJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<UnsafeBitArray>    perThreadBitArrays;
            [ReadOnly] public NativeArray<UnsafeList<Aabb> > perThreadBoundsArrays;
            [ReadOnly] public NativeArray<float>             offsetsByIndex;

            [NativeDisableParallelForRestriction] public NativeArray<AABB> finalAabbs;
            [NativeDisableParallelForRestriction] public NativeArray<AABB> finalAabbsPreOffsets;
            [NativeDisableParallelForRestriction] public NativeArray<AABB> finalBatchAabbs;

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
                        var aabb = new Aabb(finalAabbsPreOffsets[startIndex + i].Min, finalAabbsPreOffsets[startIndex + i].Max);
                        cache.Add(aabb);
                        aabb.min                   -= offsetsByIndex[startIndex + i];
                        aabb.max                   += offsetsByIndex[startIndex + i];
                        finalAabbs[startIndex + i]  = FromAabb(aabb);
                        batchAabb                   = Physics.CombineAabb(batchAabb, aabb);
                    }
                }

                if (mergeMask.Value == 0)
                {
                    finalBatchAabbs[startIndex / 32] = FromAabb(batchAabb);
                    return;
                }

                for (int threadIndex = 0; threadIndex < perThreadBoundsArrays.Length; threadIndex++)
                {
                    if (!perThreadBoundsArrays[threadIndex].IsCreated)
                        continue;

                    var tempMask = mergeMask;
                    for (int i = tempMask.CountTrailingZeros(); i < count; tempMask.SetBits(i, false), i = tempMask.CountTrailingZeros())
                    {
                        cache[i] = Physics.CombineAabb(cache[i], perThreadBoundsArrays[threadIndex][startIndex + i]);
                    }
                }

                {
                    var tempMask = mergeMask;
                    for (int i = tempMask.CountTrailingZeros(); i < count; tempMask.SetBits(i, false), i = tempMask.CountTrailingZeros())
                    {
                        var aabb                              = cache[i];
                        finalAabbsPreOffsets[startIndex + i]  = FromAabb(aabb);
                        aabb.min                             -= offsetsByIndex[startIndex + i];
                        aabb.max                             += offsetsByIndex[startIndex + i];
                        finalAabbs[startIndex + i]            = FromAabb(aabb);
                        batchAabb                             = Physics.CombineAabb(batchAabb, aabb);
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

