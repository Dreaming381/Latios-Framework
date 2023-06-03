#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
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
    public partial struct FrustumCullSkinnedPostProcessEntitiesSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_metaQuery;

        FindChunksNeedingFrustumCullingJob m_findJob;
        SingleSplitCullingJob              m_singleJob;
        MultiSplitCullingJob               m_multiJob;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_metaQuery = state.Fluent().WithAll<ChunkHeader>(true).WithAll<ChunkSkinningCullingTag>(true).WithAll<ChunkWorldRenderBounds>(true).WithAll<ChunkPerFrameCullingMask>(
                true)
                          .WithAll<ChunkPerCameraCullingMask>(false).WithAll<ChunkPerCameraCullingSplitsMask>(false).UseWriteGroups().Build();

            m_findJob = new FindChunksNeedingFrustumCullingJob
            {
                perCameraCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkHeaderHandle          = state.GetComponentTypeHandle<ChunkHeader>(true),
                postProcessMatrixHandle    = state.GetComponentTypeHandle<PostProcessMatrix>(true)
            };

            m_singleJob = new SingleSplitCullingJob
            {
                chunkWorldRenderBoundsHandle       = state.GetComponentTypeHandle<ChunkWorldRenderBounds>(true),
                perCameraCullingMaskHandle         = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                worldRenderBoundsHandle            = state.GetComponentTypeHandle<WorldRenderBounds>(true),
                perCameraSkeletonCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(false),
                skeletonDependentHandle            = state.GetComponentTypeHandle<SkeletonDependent>(true),
                storageLookup                      = state.GetEntityStorageInfoLookup()
            };

            m_multiJob = new MultiSplitCullingJob
            {
                chunkWorldRenderBoundsHandle       = m_singleJob.chunkWorldRenderBoundsHandle,
                perCameraCullingMaskHandle         = m_singleJob.perCameraCullingMaskHandle,
                perCameraCullingSplitsMaskHandle   = state.GetComponentTypeHandle<ChunkPerCameraCullingSplitsMask>(false),
                worldRenderBoundsHandle            = m_singleJob.worldRenderBoundsHandle,
                perCameraSkeletonCullingMaskHandle = m_singleJob.perCameraSkeletonCullingMaskHandle,
                skeletonDependentHandle            = m_singleJob.skeletonDependentHandle,
                storageLookup                      = m_singleJob.storageLookup
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var splits = latiosWorld.worldBlackboardEntity.GetCollectionComponent<PackedCullingSplits>(true);

            var chunkList = new NativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);

            m_findJob.chunkHeaderHandle.Update(ref state);
            m_findJob.chunksToProcess = chunkList.AsParallelWriter();
            m_findJob.perCameraCullingMaskHandle.Update(ref state);
            m_findJob.postProcessMatrixHandle.Update(ref state);
            state.Dependency = m_findJob.ScheduleParallelByRef(m_metaQuery, state.Dependency);

            var cullRequestType = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>().viewType;
            if (cullRequestType == BatchCullingViewType.Light)
            {
                m_multiJob.chunksToProcess = chunkList.AsDeferredJobArray();
                m_multiJob.chunkWorldRenderBoundsHandle.Update(ref state);
                m_multiJob.cullingSplits = splits.packedSplits;
                m_multiJob.perCameraCullingMaskHandle.Update(ref state);
                m_multiJob.perCameraCullingSplitsMaskHandle.Update(ref state);
                m_multiJob.worldRenderBoundsHandle.Update(ref state);
                m_multiJob.perCameraSkeletonCullingMaskHandle.Update(ref state);
                m_multiJob.skeletonDependentHandle.Update(ref state);
                m_multiJob.storageLookup.Update(ref state);
                state.Dependency = m_multiJob.ScheduleByRef(chunkList, 1, state.Dependency);
            }
            else
            {
                m_singleJob.chunksToProcess = chunkList.AsDeferredJobArray();
                m_singleJob.chunkWorldRenderBoundsHandle.Update(ref state);
                m_singleJob.cullingSplits = splits.packedSplits;
                m_singleJob.perCameraCullingMaskHandle.Update(ref state);
                m_singleJob.worldRenderBoundsHandle.Update(ref state);
                m_singleJob.perCameraSkeletonCullingMaskHandle.Update(ref state);
                m_singleJob.skeletonDependentHandle.Update(ref state);
                m_singleJob.storageLookup.Update(ref state);
                state.Dependency = m_singleJob.ScheduleByRef(chunkList, 1, state.Dependency);
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct FindChunksNeedingFrustumCullingJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               chunkHeaderHandle;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>         postProcessMatrixHandle;

            public NativeList<ArchetypeChunk>.ParallelWriter chunksToProcess;

            [Unity.Burst.CompilerServices.SkipLocalsInit]
            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunksCache = stackalloc ArchetypeChunk[128];
                int chunksCount = 0;
                var masks       = metaChunk.GetNativeArray(ref perCameraCullingMaskHandle);
                var headers     = metaChunk.GetNativeArray(ref chunkHeaderHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var mask = masks[i];
                    if ((mask.lower.Value | mask.upper.Value) != 0 && headers[i].ArchetypeChunk.Has(ref postProcessMatrixHandle))
                    {
                        chunksCache[chunksCount] = headers[i].ArchetypeChunk;
                        chunksCount++;
                    }
                }

                if (chunksCount > 0)
                {
                    chunksToProcess.AddRangeNoResize(chunksCache, chunksCount);
                }
            }
        }

        [BurstCompile]
        unsafe struct SingleSplitCullingJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public NativeReference<CullingSplits>              cullingSplits;
            [ReadOnly] public ComponentTypeHandle<WorldRenderBounds>      worldRenderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkWorldRenderBounds> chunkWorldRenderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>      skeletonDependentHandle;
            [ReadOnly] public EntityStorageInfoLookup                     storageLookup;

            public ComponentTypeHandle<ChunkPerCameraCullingMask>         perCameraCullingMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask> perCameraSkeletonCullingMaskHandle;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                ref var mask        = ref chunk.GetChunkComponentRefRW(ref perCameraCullingMaskHandle);
                var     chunkBounds = chunk.GetChunkComponentRefRO(ref chunkWorldRenderBoundsHandle);

                // Note: Unlike Entities Graphics, we always assume per-instance culling.
                // The only way disabling per-instance culling happens is via custom code
                // and it would require us to add more component types to this job.
                // If someone complains, I can add it back, but for now I'm excluding it
                // for simplicity.
                ref var splits        = ref UnsafeUtility.AsRef<CullingSplits>(cullingSplits.GetUnsafeReadOnlyPtr());
                var     cullingPlanes = splits.SplitPlanePackets.AsNativeArray();
                var     chunkIn       = FrustumPlanes.Intersect2(cullingPlanes, chunkBounds.ValueRO.Value);

                if (chunkIn == FrustumPlanes.IntersectResult.In)
                {
                    // Unlike Entities Graphics, we initialize as visible and clear bits that are hidden.
                    return;
                }
                if (chunkIn == FrustumPlanes.IntersectResult.Out)
                {
                    mask = default;
                    return;
                }

                var worldBounds = chunk.GetNativeArray(ref worldRenderBoundsHandle);
                var deps        = chunk.GetNativeArray(ref skeletonDependentHandle);
                var inMask      = mask.lower.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn         = FrustumPlanes.Intersect2NoPartial(cullingPlanes, worldBounds[i].Value) != FrustumPlanes.IntersectResult.Out;
                    mask.lower.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                    if (isIn)
                        MarkSkeletonVisible(deps[i].root);
                }
                inMask = mask.upper.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn         = FrustumPlanes.Intersect2NoPartial(cullingPlanes, worldBounds[i + 64].Value) != FrustumPlanes.IntersectResult.Out;
                    mask.upper.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                    if (isIn)
                        MarkSkeletonVisible(deps[i + 64].root);
                }
            }

#if !UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS
#error Latios Framework requires UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS to be defined in your scripting define symbols.
#endif

            void MarkSkeletonVisible(Entity skeletonEntity)
            {
                if (storageLookup.Exists(skeletonEntity))
                {
                    var     esi  = storageLookup[skeletonEntity];
                    ref var mask = ref esi.Chunk.GetChunkComponentRefRW(ref perCameraSkeletonCullingMaskHandle);
                    if (esi.IndexInChunk >= 64)
                    {
                        ulong target = 1ul << (esi.IndexInChunk - 64);
                        Common.InterlockedOr(ref mask.upper.Value, target);
                    }
                    else
                    {
                        ulong target = 1ul << esi.IndexInChunk;
                        Common.InterlockedOr(ref mask.lower.Value, target);
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct MultiSplitCullingJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public NativeReference<CullingSplits>              cullingSplits;
            [ReadOnly] public ComponentTypeHandle<WorldRenderBounds>      worldRenderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkWorldRenderBounds> chunkWorldRenderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>      skeletonDependentHandle;
            [ReadOnly] public EntityStorageInfoLookup                     storageLookup;

            public ComponentTypeHandle<ChunkPerCameraCullingMask>         perCameraCullingMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingSplitsMask>   perCameraCullingSplitsMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask> perCameraSkeletonCullingMaskHandle;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                ref var mask        = ref chunk.GetChunkComponentRefRW(ref perCameraCullingMaskHandle);
                var     chunkBounds = chunk.GetChunkComponentRefRO(ref chunkWorldRenderBoundsHandle);

                // Note: Unlike Entities Graphics, we always assume per-instance culling.
                // The only way disabling per-instance culling happens is via custom code
                // and it would require us to add more component types to this job.
                // If someone complains, I can add it back, but for now I'm excluding it
                // for simplicity.
                ref var splits = ref UnsafeUtility.AsRef<CullingSplits>(cullingSplits.GetUnsafeReadOnlyPtr());

                if (!splits.ReceiverPlanePackets.IsEmpty)
                {
                    if (FrustumPlanes.Intersect2NoPartial(splits.ReceiverPlanePackets.AsNativeArray(), chunkBounds.ValueRO.Value) ==
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

                    float2 receiverSphereLightSpace = splits.TransformToLightSpaceXY(s.CullingSphereCenter);

                    // If the entire chunk fails the sphere test, no need to consider further
                    if (splits.SphereTestEnabled && SphereTest(ref splits, s, chunkBounds.ValueRO.Value, receiverSphereLightSpace) == SphereTestResult.CannotCastShadow)
                        continue;

                    // See note above about per-instance culling
                    var chunkIn = FrustumPlanes.Intersect2(splitPlanes, chunkBounds.ValueRO.Value);

                    if (chunkIn == FrustumPlanes.IntersectResult.Partial)
                    {
                        var inMask = mask.lower.Value;
                        for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                        {
                            bool isIn                 = FrustumPlanes.Intersect2NoPartial(combinedSplitPlanes, worldBounds[i].Value) != FrustumPlanes.IntersectResult.Out;
                            splitMasks.splitMasks[i] |= (byte)math.select(0u, splitMask, isIn);
                        }
                        inMask = mask.upper.Value;
                        for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                        {
                            bool isIn                      = FrustumPlanes.Intersect2NoPartial(combinedSplitPlanes, worldBounds[i + 64].Value) != FrustumPlanes.IntersectResult.Out;
                            splitMasks.splitMasks[i + 64] |= (byte)math.select(0u, splitMask, isIn);
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
                    mask.lower.Value &= lower;
                    mask.upper.Value &= upper;
                }

                // If anything survived the culling, perform sphere testing for each split
                if (splits.SphereTestEnabled && (mask.lower.Value | mask.upper.Value) != 0)
                {
                    var inMask = mask.lower.Value;
                    for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                    {
                        var  sphereMask          = splits.SplitSOASphereTest.SOASphereTestSplitMask(ref splits, worldBounds[i].Value);
                        uint oldMask             = splitMasks.splitMasks[i];
                        splitMasks.splitMasks[i] = (byte)(math.asuint(sphereMask) & oldMask);
                    }
                    inMask = mask.upper.Value;
                    for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                    {
                        var  sphereMask               = splits.SplitSOASphereTest.SOASphereTestSplitMask(ref splits, worldBounds[i + 64].Value);
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

                {
                    var deps   = chunk.GetNativeArray(ref skeletonDependentHandle);
                    var inMask = mask.lower.Value;
                    for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                    {
                        MarkSkeletonVisible(deps[i].root);
                    }
                    inMask = mask.upper.Value;
                    for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                    {
                        MarkSkeletonVisible(deps[i + 64].root);
                    }
                }
            }

            private enum SphereTestResult
            {
                // The caster is guaranteed to not cast a visible shadow in the tested cascade
                CannotCastShadow,
                // The caster might cast a shadow in the tested cascade, and has to be rendered in the shadow map
                MightCastShadow,
            }

            private SphereTestResult SphereTest(ref CullingSplits cullingSplits, CullingSplitData split, AABB aabb, float2 receiverSphereLightSpace)
            {
                // This test has been ported from the corresponding test done by Unity's
                // built in shadow culling.

                float  casterRadius             = math.length(aabb.Extents);
                float2 casterCenterLightSpaceXY = cullingSplits.TransformToLightSpaceXY(aabb.Center);

                // A spherical caster casts a cylindrical shadow volume. In XY in light space this ends up being a circle/circle intersection test.
                // Thus we first check if the caster bounding circle is at least partially inside the cascade circle.
                float sqrDistBetweenCasterAndCascadeCenter = math.lengthsq(casterCenterLightSpaceXY - receiverSphereLightSpace);
                float combinedRadius                       = casterRadius + split.CullingSphereRadius;
                float sqrCombinedRadius                    = combinedRadius * combinedRadius;

                // If the 2D circles intersect, then the caster is potentially visible in the cascade.
                // If they don't intersect, then there is no way for the caster to cast a shadow that is
                // visible inside the circle.
                // Casters that intersect the circle but are behind the receiver sphere also don't cast shadows.
                // We don't consider that here, since those casters should be culled out by the receiver
                // plane culling.
                if (sqrDistBetweenCasterAndCascadeCenter <= sqrCombinedRadius)
                    return SphereTestResult.MightCastShadow;
                else
                    return SphereTestResult.CannotCastShadow;
            }

            void MarkSkeletonVisible(Entity skeletonEntity)
            {
                if (storageLookup.Exists(skeletonEntity))
                {
                    var     esi  = storageLookup[skeletonEntity];
                    ref var mask = ref esi.Chunk.GetChunkComponentRefRW(ref perCameraSkeletonCullingMaskHandle);
                    if (esi.IndexInChunk >= 64)
                    {
                        ulong target = 1ul << (esi.IndexInChunk - 64);
                        Common.InterlockedOr(ref mask.upper.Value, target);
                    }
                    else
                    {
                        ulong target = 1ul << esi.IndexInChunk;
                        Common.InterlockedOr(ref mask.lower.Value, target);
                    }
                }
            }
        }
    }
}
#endif

