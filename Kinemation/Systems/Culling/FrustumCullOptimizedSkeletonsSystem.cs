using Latios.Kinemation.InternalSourceGen;
using Unity.Burst;
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
    public partial struct FrustumCullOptimizedSkeletonsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_query;

        SingleSplitCullingJob m_singleJob;
        MultiSplitCullingJob  m_multiJob;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_query = state.Fluent().With<DependentSkinnedMesh>(true).With<ChunkOptimizedSkeletonWorldBounds>(true, true).With<OptimizedSkeletonWorldBounds>(true)
                      .With<ChunkPerCameraSkeletonCullingMask>(false, true).With<ChunkPerCameraSkeletonCullingSplitsMask>(false, true).Build();

            m_singleJob = new SingleSplitCullingJob
            {
                chunkWorldRenderBoundsHandle = state.GetComponentTypeHandle<ChunkOptimizedSkeletonWorldBounds>(true),
                perCameraCullingMaskHandle   = state.GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(false),
                worldRenderBoundsHandle      = state.GetComponentTypeHandle<OptimizedSkeletonWorldBounds>(true)
            };

            m_multiJob = new MultiSplitCullingJob
            {
                chunkWorldRenderBoundsHandle     = m_singleJob.chunkWorldRenderBoundsHandle,
                perCameraCullingMaskHandle       = m_singleJob.perCameraCullingMaskHandle,
                perCameraCullingSplitsMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraSkeletonCullingSplitsMask>(false),
                worldRenderBoundsHandle          = m_singleJob.worldRenderBoundsHandle
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var splits = latiosWorld.worldBlackboardEntity.GetCollectionComponent<PackedCullingSplits>(true);

            var cullRequestType = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>().viewType;
            if (cullRequestType == BatchCullingViewType.Light)
            {
                m_multiJob.chunkWorldRenderBoundsHandle.Update(ref state);
                m_multiJob.cullingSplits = splits.packedSplits;
                m_multiJob.perCameraCullingMaskHandle.Update(ref state);
                m_multiJob.perCameraCullingSplitsMaskHandle.Update(ref state);
                m_multiJob.worldRenderBoundsHandle.Update(ref state);
                state.Dependency = m_multiJob.ScheduleParallelByRef(m_query, state.Dependency);
            }
            else
            {
                m_singleJob.chunkWorldRenderBoundsHandle.Update(ref state);
                m_singleJob.cullingSplits = splits.packedSplits;
                m_singleJob.perCameraCullingMaskHandle.Update(ref state);
                m_singleJob.worldRenderBoundsHandle.Update(ref state);
                state.Dependency = m_singleJob.ScheduleByRef(m_query, state.Dependency);
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        unsafe struct SingleSplitCullingJob : IJobChunk
        {
            [ReadOnly] public NativeReference<CullingSplits>                         cullingSplits;
            [ReadOnly] public ComponentTypeHandle<OptimizedSkeletonWorldBounds>      worldRenderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkOptimizedSkeletonWorldBounds> chunkWorldRenderBoundsHandle;

            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask> perCameraCullingMaskHandle;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var mask        = ref chunk.GetChunkComponentRefRW(ref perCameraCullingMaskHandle);
                var     chunkBounds = chunk.GetChunkComponentRefRO(ref chunkWorldRenderBoundsHandle);
                mask                = default;

                // Note: Unlike Entities Graphics, we always assume per-instance culling.
                // The only way disabling per-instance culling happens is via custom code
                // and it would require us to add more component types to this job.
                // If someone complains, I can add it back, but for now I'm excluding it
                // for simplicity.
                ref var splits        = ref UnsafeUtility.AsRef<CullingSplits>(cullingSplits.GetUnsafeReadOnlyPtr());
                var     cullingPlanes = splits.SplitPlanePackets.AsNativeArray();
                var     chunkIn       = FrustumPlanes.Intersect2(cullingPlanes, chunkBounds.ValueRO.chunkBounds);

                if (chunkIn == FrustumPlanes.IntersectResult.In)
                {
                    mask.lower.SetBits(0, true, math.min(64, chunk.Count));
                    if (chunk.Count > 64)
                        mask.upper.SetBits(0, true, chunk.Count - 64);
                    return;
                }
                if (chunkIn == FrustumPlanes.IntersectResult.Out)
                {
                    mask = default;
                    return;
                }

                var worldBounds = chunk.GetNativeArray(ref worldRenderBoundsHandle);
                for (int i = 0; i < math.min(chunk.Count, 64); i++)
                {
                    bool isIn         = FrustumPlanes.Intersect2NoPartial(cullingPlanes, worldBounds[i].bounds) != FrustumPlanes.IntersectResult.Out;
                    mask.lower.Value |= math.select(0ul, 1ul, isIn) << i;
                }
                for (int i = 0; i < chunk.Count - 64; i++)
                {
                    bool isIn         = FrustumPlanes.Intersect2NoPartial(cullingPlanes, worldBounds[i + 64].bounds) != FrustumPlanes.IntersectResult.Out;
                    mask.upper.Value |= math.select(0ul, 1ul, isIn) << i;
                }
            }
        }

        [BurstCompile]
        unsafe struct MultiSplitCullingJob : IJobChunk
        {
            [ReadOnly] public NativeReference<CullingSplits>                         cullingSplits;
            [ReadOnly] public ComponentTypeHandle<OptimizedSkeletonWorldBounds>      worldRenderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkOptimizedSkeletonWorldBounds> chunkWorldRenderBoundsHandle;

            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>       perCameraCullingMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingSplitsMask> perCameraCullingSplitsMaskHandle;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var mask        = ref chunk.GetChunkComponentRefRW(ref perCameraCullingMaskHandle);
                var     chunkBounds = chunk.GetChunkComponentRefRO(ref chunkWorldRenderBoundsHandle);
                mask                = default;

                // Note: Unlike Entities Graphics, we always assume per-instance culling.
                // The only way disabling per-instance culling happens is via custom code
                // and it would require us to add more component types to this job.
                // If someone complains, I can add it back, but for now I'm excluding it
                // for simplicity.
                ref var splits = ref UnsafeUtility.AsRef<CullingSplits>(cullingSplits.GetUnsafeReadOnlyPtr());

                if (!splits.ReceiverPlanePackets.IsEmpty)
                {
                    if (FrustumPlanes.Intersect2NoPartial(splits.ReceiverPlanePackets.AsNativeArray(), chunkBounds.ValueRO.chunkBounds) ==
                        FrustumPlanes.IntersectResult.Out)
                    {
                        mask = default;
                        return;
                    }
                }

                // Unlike Entities Graphics, we initialize the visibility mask as 1s and clear bits that are hidden.
                // However, for splits, it makes more sense to follow Entities Graphics approach.
                // Therefore the actual strategy is to clear splits, enable them as we progress,
                // and then mask the splits against our visibility mask.
                ref var splitMasks = ref chunk.GetChunkComponentRefRW(ref perCameraCullingSplitsMaskHandle);
                splitMasks         = default;

                var worldBounds = chunk.GetNativeArray(ref worldRenderBoundsHandle);

                int visibleSplitMask = ~0;
                if (splits.SphereTestEnabled)
                    visibleSplitMask = splits.ReceiverSphereCuller.Cull(chunkBounds.ValueRO.chunkBounds);

                var numEntities = chunk.Count;

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
                    var chunkIn = FrustumPlanes.Intersect2(splitPlanes, chunkBounds.ValueRO.chunkBounds);

                    if (chunkIn == FrustumPlanes.IntersectResult.Partial)
                    {
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            bool isIn                 = FrustumPlanes.Intersect2NoPartial(combinedSplitPlanes, worldBounds[i].bounds) != FrustumPlanes.IntersectResult.Out;
                            splitMasks.splitMasks[i] |= (byte)math.select(0u, splitMask, isIn);
                        }
                    }
                    else if (chunkIn == FrustumPlanes.IntersectResult.In)
                    {
                        for (int i = 0; i < numEntities; ++i)
                            splitMasks.splitMasks[i] |= splitMask;
                    }
                    else if (chunkIn == FrustumPlanes.IntersectResult.Out)
                    {
                        // No need to do anything. Split mask bits for this split should already
                        // be cleared since they were initialized to zero.
                    }
                }

                {
                    // Todo: Do we need to help Burst vectorize this?
                    ulong lower = 0;
                    for (int i = 0; i < 64; i++)
                        lower   |= math.select(0ul, 1ul, splitMasks.splitMasks[i] != 0) << i;
                    ulong upper  = 0;
                    for (int i = 0; i < 64; i++)
                        upper        |= math.select(0ul, 1ul, splitMasks.splitMasks[i + 64] != 0) << i;
                    mask.lower.Value  = lower;
                    mask.upper.Value  = upper;
                }

                // If anything survived the culling, perform sphere testing for each split
                if (splits.SphereTestEnabled && (mask.lower.Value | mask.upper.Value) != 0)
                {
                    var inMask = mask.lower.Value;
                    for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                    {
                        var  sphereMask          = splits.ReceiverSphereCuller.Cull(worldBounds[i].bounds);
                        uint oldMask             = splitMasks.splitMasks[i];
                        splitMasks.splitMasks[i] = (byte)(math.asuint(sphereMask) & oldMask);
                    }
                    inMask = mask.upper.Value;
                    for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                    {
                        var  sphereMask               = splits.ReceiverSphereCuller.Cull(worldBounds[i + 64].bounds);
                        uint oldMask                  = splitMasks.splitMasks[i + 64];
                        splitMasks.splitMasks[i + 64] = (byte)(math.asuint(sphereMask) & oldMask);
                    }

                    {
                        // Todo: Do we need to help Burst vectorize this?
                        ulong lower = 0;
                        for (int i = 0; i < 64; i++)
                            lower   |= math.select(0ul, 1ul, splitMasks.splitMasks[i] != 0) << i;
                        ulong upper  = 0;
                        for (int i = 0; i < 64; i++)
                            upper        |= math.select(0ul, 1ul, splitMasks.splitMasks[i + 64] != 0) << i;
                        mask.lower.Value &= lower;
                        mask.upper.Value &= upper;
                    }
                }
            }
        }
    }
}

