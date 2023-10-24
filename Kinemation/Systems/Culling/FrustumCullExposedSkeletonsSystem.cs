using Latios.Kinemation.InternalSourceGen;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine.Rendering;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct FrustumCullExposedSkeletonsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_query;

        SingleSplitCullingJobPart2 m_singleJob2;
        MultiSplitCullingJobPart2  m_multiJob2;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_query = state.Fluent().With<DependentSkinnedMesh>(true).With<ExposedSkeletonCullingIndex>(true)
                      .With<ChunkPerCameraSkeletonCullingMask>(false, true).With<ChunkPerCameraSkeletonCullingSplitsMask>(false, true).Build();

            m_singleJob2 = new SingleSplitCullingJobPart2
            {
                perCameraCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(false),
                cullingIndexHandle         = state.GetComponentTypeHandle<ExposedSkeletonCullingIndex>(true)
            };

            m_multiJob2 = new MultiSplitCullingJobPart2
            {
                perCameraCullingMaskHandle       = m_singleJob2.perCameraCullingMaskHandle,
                perCameraCullingSplitsMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraSkeletonCullingSplitsMask>(false),
                cullingIndexHandle               = m_singleJob2.cullingIndexHandle
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var splits       = latiosWorld.worldBlackboardEntity.GetCollectionComponent<PackedCullingSplits>(true);
            var boundsArrays = latiosWorld.worldBlackboardEntity.GetCollectionComponent<ExposedSkeletonBoundsArrays>(true);

            var cullRequestType = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>().viewType;
            if (cullRequestType == BatchCullingViewType.Light)
            {
                var bitArray     = new NativeList<BitField32>(state.WorldUpdateAllocator);
                var splitsArray  = new NativeList<byte>(state.WorldUpdateAllocator);
                state.Dependency = new AllocateMultiSplitJob
                {
                    aabbs       = boundsArrays.allAabbs,
                    bitArray    = bitArray,
                    splitsArray = splitsArray
                }.Schedule(state.Dependency);

                state.Dependency = new MultiSplitCullingJobPart1
                {
                    aabbs         = boundsArrays.allAabbs.AsDeferredJobArray(),
                    batchAabbs    = boundsArrays.batchedAabbs.AsDeferredJobArray(),
                    bitArray      = bitArray.AsDeferredJobArray(),
                    cullingSplits = splits.packedSplits,
                    splitsArray   = splitsArray.AsDeferredJobArray()
                }.Schedule(boundsArrays.batchedAabbs, 1, state.Dependency);

                m_multiJob2.perCameraCullingMaskHandle.Update(ref state);
                m_multiJob2.perCameraCullingSplitsMaskHandle.Update(ref state);
                m_multiJob2.cullingIndexHandle.Update(ref state);
                m_multiJob2.bitArray    = bitArray.AsDeferredJobArray();
                m_multiJob2.splitsArray = splitsArray.AsDeferredJobArray();
                state.Dependency        = m_multiJob2.ScheduleParallelByRef(m_query, state.Dependency);
            }
            else
            {
                var bitArray     = new NativeList<BitField32>(state.WorldUpdateAllocator);
                state.Dependency = new AllocateSingleSplitJob
                {
                    batchAabbs = boundsArrays.batchedAabbs,
                    bitArray   = bitArray,
                }.Schedule(state.Dependency);

                state.Dependency = new SingleSplitCullingJobPart1
                {
                    aabbs         = boundsArrays.allAabbs.AsDeferredJobArray(),
                    batchAabbs    = boundsArrays.batchedAabbs.AsDeferredJobArray(),
                    bitArray      = bitArray.AsDeferredJobArray(),
                    cullingSplits = splits.packedSplits,
                }.Schedule(boundsArrays.batchedAabbs, 1, state.Dependency);

                m_singleJob2.perCameraCullingMaskHandle.Update(ref state);
                m_singleJob2.cullingIndexHandle.Update(ref state);
                m_singleJob2.bitArray = bitArray.AsDeferredJobArray();
                state.Dependency      = m_singleJob2.ScheduleParallelByRef(m_query, state.Dependency);
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct AllocateSingleSplitJob : IJob
        {
            public NativeList<BitField32>      bitArray;
            [ReadOnly] public NativeList<AABB> batchAabbs;

            public void Execute() => bitArray.ResizeUninitialized(batchAabbs.Length);
        }

        [BurstCompile]
        struct AllocateMultiSplitJob : IJob
        {
            public NativeList<BitField32>      bitArray;
            public NativeList<byte>            splitsArray;
            [ReadOnly] public NativeList<AABB> aabbs;

            public void Execute()
            {
                splitsArray.ResizeUninitialized(aabbs.Length);
                int count = aabbs.Length / 32;
                if ((aabbs.Length % 32) != 0)
                    count++;
                bitArray.ResizeUninitialized(count);
            }
        }

        [BurstCompile]
        unsafe struct SingleSplitCullingJobPart1 : IJobParallelForDefer
        {
            [ReadOnly] public NativeReference<CullingSplits> cullingSplits;
            [ReadOnly] public NativeArray<AABB>              aabbs;
            [ReadOnly] public NativeArray<AABB>              batchAabbs;
            public NativeArray<BitField32>                   bitArray;

            public void Execute(int i)
            {
                var start = i << 5;
                var count = math.min(32, aabbs.Length - start);
                Execute(start, count);
            }

            public void Execute(int startIndex, int count)
            {
                BitField32 mask = default;

                ref var splits        = ref UnsafeUtility.AsRef<CullingSplits>(cullingSplits.GetUnsafeReadOnlyPtr());
                var     cullingPlanes = splits.SplitPlanePackets.AsNativeArray();
                var     chunkIn       = FrustumPlanes.Intersect2(cullingPlanes, batchAabbs[startIndex >> 5]);

                if (chunkIn == FrustumPlanes.IntersectResult.Out)
                {
                    bitArray[startIndex >> 5] = mask;
                    return;
                }
                if (chunkIn == FrustumPlanes.IntersectResult.In)
                {
                    mask.SetBits(0, true, count);
                    bitArray[startIndex >> 5] = mask;
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    bool bit = FrustumPlanes.Intersect2NoPartial(cullingPlanes, aabbs[startIndex + i]) != FrustumPlanes.IntersectResult.Out;
                    mask.SetBits(i, bit);
                }

                bitArray[startIndex >> 5] = mask;
            }
        }

        [BurstCompile]
        unsafe struct MultiSplitCullingJobPart1 : IJobParallelForDefer
        {
            [ReadOnly] public NativeReference<CullingSplits>               cullingSplits;
            [ReadOnly] public NativeArray<AABB>                            aabbs;
            [ReadOnly] public NativeArray<AABB>                            batchAabbs;
            public NativeArray<BitField32>                                 bitArray;
            [NativeDisableParallelForRestriction] public NativeArray<byte> splitsArray;

            public void Execute(int i)
            {
                var start = i << 5;
                var count = math.min(32, aabbs.Length - start);
                Execute(start, count);
            }

            public void Execute(int startIndex, int count)
            {
                Hint.Assume(count > 0 && count <= 32);

                BitField32 mask = default;

                ref var splits      = ref UnsafeUtility.AsRef<CullingSplits>(cullingSplits.GetUnsafeReadOnlyPtr());
                var     batchBounds = batchAabbs[startIndex >> 5];
                if (!splits.ReceiverPlanePackets.IsEmpty)
                {
                    if (FrustumPlanes.Intersect2NoPartial(splits.ReceiverPlanePackets.AsNativeArray(), batchBounds) ==
                        FrustumPlanes.IntersectResult.Out)
                    {
                        bitArray[startIndex >> 5] = mask;
                        return;
                    }
                }

                // Unlike Entities Graphics, we initialize the visibility mask as 1s and clear bits that are hidden.
                // However, for splits, it makes more sense to follow Entities Graphics approach.
                // Therefore the actual strategy is to clear splits, enable them as we progress,
                // and then mask the splits against our visibility mask.
                var splitMasks = splitsArray.GetSubArray(startIndex, count);
                UnsafeUtility.MemClear(splitMasks.GetUnsafePtr(), count);

                var worldBounds = aabbs.GetSubArray(startIndex, count);

                int visibleSplitMask = ~0;
                if (splits.SphereTestEnabled)
                    visibleSplitMask = splits.ReceiverSphereCuller.Cull(batchBounds);

                // First, perform frustum and receiver plane culling for all splits
                for (int splitIndex = 0; splitIndex < splits.Splits.Length; ++splitIndex)
                {
                    var s = splits.Splits[splitIndex];

                    byte splitMask = (byte)(1 << splitIndex);

                    var splitPlanes = splits.SplitPlanePackets.GetSubNativeArray(
                        s.PlanePacketOffset,
                        s.PlanePacketCount);
                    var combinedSplitPlanes = splits.CombinedSplitAndReceiverPlanePackets.GetSubNativeArray(
                        s.CombinedPlanePacketOffset,
                        s.CombinedPlanePacketCount);

                    // If the entire chunk fails the sphere test, no need to consider further
                    if ((visibleSplitMask & (1 << splitIndex)) == 0)
                        continue;

                    // See note above about per-instance culling
                    var chunkIn = FrustumPlanes.Intersect2(splitPlanes, batchBounds);

                    if (chunkIn == FrustumPlanes.IntersectResult.Partial)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            bool isIn      = FrustumPlanes.Intersect2NoPartial(combinedSplitPlanes, worldBounds[i]) != FrustumPlanes.IntersectResult.Out;
                            splitMasks[i] |= (byte)math.select(0u, splitMask, isIn);
                        }
                    }
                    else if (chunkIn == FrustumPlanes.IntersectResult.In)
                    {
                        for (int i = 0; i < count; ++i)
                            splitMasks[i] |= splitMask;
                    }
                    else if (chunkIn == FrustumPlanes.IntersectResult.Out)
                    {
                        // No need to do anything. Split mask bits for this split should already
                        // be cleared since they were initialized to zero.
                    }
                }

                // Todo: Do we need to help Burst vectorize this?
                for (int i = 0; i < count; i++)
                    mask.SetBits(i, splitMasks[i] != 0);

                // If anything survived the culling, perform sphere testing for each split
                if (splits.SphereTestEnabled && mask.Value != 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var  sphereMask = splits.ReceiverSphereCuller.Cull(worldBounds[i]);
                        uint oldMask    = splitMasks[i];
                        splitMasks[i]   = (byte)(math.asuint(sphereMask) & oldMask);
                    }

                    // Todo: Do we need to help Burst vectorize this?
                    for (int i = 0; i < count; i++)
                        mask.SetBits(i, splitMasks[i] != 0);
                }

                bitArray[startIndex >> 5] = mask;
            }
        }

        [BurstCompile]
        unsafe struct SingleSplitCullingJobPart2 : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ExposedSkeletonCullingIndex> cullingIndexHandle;
            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>      perCameraCullingMaskHandle;

            [ReadOnly] public NativeArray<BitField32> bitArray;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var mask = ref chunk.GetChunkComponentRefRW(ref perCameraCullingMaskHandle);
                mask         = default;

                var cullingIndices = chunk.GetNativeArray(ref cullingIndexHandle);
                for (int i = 0; i < math.min(chunk.Count, 64); i++)
                {
                    bool isIn         = IsBitSet(cullingIndices[i].cullingIndex);
                    mask.lower.Value |= math.select(0ul, 1ul, isIn) << i;
                }
                for (int i = 0; i < chunk.Count - 64; i++)
                {
                    bool isIn         = IsBitSet(cullingIndices[i + 64].cullingIndex);
                    mask.upper.Value |= math.select(0ul, 1ul, isIn) << i;
                }
            }

            bool IsBitSet(int index)
            {
                var arrayIndex = index >> 5;
                var bitIndex   = index & 0x1f;
                return bitArray[arrayIndex].IsSet(bitIndex);
            }
        }

        [BurstCompile]
        unsafe struct MultiSplitCullingJobPart2 : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ExposedSkeletonCullingIndex>  cullingIndexHandle;
            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>       perCameraCullingMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingSplitsMask> perCameraCullingSplitsMaskHandle;

            [ReadOnly] public NativeArray<BitField32> bitArray;
            [ReadOnly] public NativeArray<byte>       splitsArray;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var mask       = ref chunk.GetChunkComponentRefRW(ref perCameraCullingMaskHandle);
                ref var splitMasks = ref chunk.GetChunkComponentRefRW(ref perCameraCullingSplitsMaskHandle);
                mask               = default;
                splitMasks         = default;

                var cullingIndices = chunk.GetNativeArray(ref cullingIndexHandle);
                for (int i = 0; i < math.min(chunk.Count, 64); i++)
                {
                    bool isIn                 = IsBitSet(cullingIndices[i].cullingIndex, out byte splits);
                    mask.lower.Value         |= math.select(0ul, 1ul, isIn) << i;
                    splitMasks.splitMasks[i]  = splits;
                }
                for (int i = 0; i < chunk.Count - 64; i++)
                {
                    bool isIn                      = IsBitSet(cullingIndices[i + 64].cullingIndex, out byte splits);
                    mask.upper.Value              |= math.select(0ul, 1ul, isIn) << i;
                    splitMasks.splitMasks[i + 64]  = splits;
                }
            }

            bool IsBitSet(int index, out byte splits)
            {
                var arrayIndex = index >> 5;
                var bitIndex   = index & 0x1f;
                var result     = bitArray[arrayIndex].IsSet(bitIndex);
                splits         = result ? splitsArray[index] : default;
                return result;
            }
        }
    }
}

