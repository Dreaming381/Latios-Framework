using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

using FrustumPlanes = Unity.Rendering.FrustumPlanes;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct FrustumCullSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_metaQuery;

        FindChunksNeedingFrustumCullingJob m_findJob;
        SingleSplitCullingJob              m_singleJob;
        MultiSplitCullingJob               m_multiJob;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_metaQuery = state.Fluent().With<ChunkWorldRenderBounds>(true).With<ChunkHeader>(true).With<ChunkPerFrameCullingMask>(true)
                          .With<ChunkPerCameraCullingMask>(false).With<ChunkPerCameraCullingSplitsMask>(false).UseWriteGroups().Build();

            m_findJob = new FindChunksNeedingFrustumCullingJob
            {
                perCameraCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkHeaderHandle          = state.GetComponentTypeHandle<ChunkHeader>(true)
            };

            m_singleJob = new SingleSplitCullingJob
            {
                chunkWorldRenderBoundsHandle = state.GetComponentTypeHandle<ChunkWorldRenderBounds>(true),
                perCameraCullingMaskHandle   = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                worldRenderBoundsHandle      = state.GetComponentTypeHandle<WorldRenderBounds>(true)
            };

            m_multiJob = new MultiSplitCullingJob
            {
                chunkWorldRenderBoundsHandle     = m_singleJob.chunkWorldRenderBoundsHandle,
                perCameraCullingMaskHandle       = m_singleJob.perCameraCullingMaskHandle,
                perCameraCullingSplitsMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingSplitsMask>(false),
                worldRenderBoundsHandle          = m_singleJob.worldRenderBoundsHandle
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var cullingContext = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>();
            var cullingPlanes  = latiosWorld.worldBlackboardEntity.GetBuffer<CullingPlane>(true).Reinterpret<Plane>().AsNativeArray();
            var cullingSplits  = latiosWorld.worldBlackboardEntity.GetBuffer<CullingSplitElement>(true).Reinterpret<CullingSplit>().AsNativeArray();
            var splits         = new CullingSplits(ref cullingContext, cullingPlanes, cullingSplits, QualitySettings.shadowProjection, state.WorldUpdateAllocator);

            var chunkList = new NativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);

            m_findJob.chunkHeaderHandle.Update(ref state);
            m_findJob.chunksToProcess = chunkList.AsParallelWriter();
            m_findJob.perCameraCullingMaskHandle.Update(ref state);
            state.Dependency = m_findJob.ScheduleParallelByRef(m_metaQuery, state.Dependency);

            if (cullingContext.viewType == BatchCullingViewType.Light)
            {
                m_multiJob.chunksToProcess = chunkList.AsDeferredJobArray();
                m_multiJob.chunkWorldRenderBoundsHandle.Update(ref state);
                m_multiJob.cullingSplits = splits;
                m_multiJob.perCameraCullingMaskHandle.Update(ref state);
                m_multiJob.perCameraCullingSplitsMaskHandle.Update(ref state);
                m_multiJob.worldRenderBoundsHandle.Update(ref state);
                state.Dependency = m_multiJob.ScheduleByRef(chunkList, 1, state.Dependency);
            }
            else
            {
                m_singleJob.chunksToProcess = chunkList.AsDeferredJobArray();
                m_singleJob.chunkWorldRenderBoundsHandle.Update(ref state);
                m_singleJob.cullingSplits = splits;
                m_singleJob.perCameraCullingMaskHandle.Update(ref state);
                m_singleJob.worldRenderBoundsHandle.Update(ref state);
                state.Dependency = m_singleJob.ScheduleByRef(chunkList, 1, state.Dependency);
            }
        }

        [BurstCompile]
        struct FindChunksNeedingFrustumCullingJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               chunkHeaderHandle;

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
                    if ((mask.lower.Value | mask.upper.Value) != 0)
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

            [ReadOnly] public CullingSplits                               cullingSplits;
            [ReadOnly] public ComponentTypeHandle<WorldRenderBounds>      worldRenderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkWorldRenderBounds> chunkWorldRenderBoundsHandle;

            public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraCullingMaskHandle;

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
                ref var splits        = ref cullingSplits;
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
                var inMask      = mask.lower.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn         = FrustumPlanes.Intersect2NoPartial(cullingPlanes, worldBounds[i].Value) != FrustumPlanes.IntersectResult.Out;
                    mask.lower.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                }
                inMask = mask.upper.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn         = FrustumPlanes.Intersect2NoPartial(cullingPlanes, worldBounds[i + 64].Value) != FrustumPlanes.IntersectResult.Out;
                    mask.upper.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                }
            }
        }

        [BurstCompile]
        unsafe struct MultiSplitCullingJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public CullingSplits                               cullingSplits;
            [ReadOnly] public ComponentTypeHandle<WorldRenderBounds>      worldRenderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkWorldRenderBounds> chunkWorldRenderBoundsHandle;

            public ComponentTypeHandle<ChunkPerCameraCullingMask>       perCameraCullingMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingSplitsMask> perCameraCullingSplitsMaskHandle;

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
                ref var splits = ref cullingSplits;

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

                int  visibleSplitMask        = ~0;
                bool skipEntitySphereCulling = false;
                if (splits.SphereTestEnabled)
                    visibleSplitMask = splits.ReceiverSphereCuller.CullBatch(chunkBounds.ValueRO.Value, out skipEntitySphereCulling);

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
                if (splits.SphereTestEnabled && !skipEntitySphereCulling && (mask.lower.Value | mask.upper.Value) != 0)
                {
                    var inMask = mask.lower.Value;
                    for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                    {
                        var  sphereMask          = splits.ReceiverSphereCuller.Cull(worldBounds[i].Value);
                        uint oldMask             = splitMasks.splitMasks[i];
                        splitMasks.splitMasks[i] = (byte)(math.asuint(sphereMask) & oldMask);
                    }
                    inMask = mask.upper.Value;
                    for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                    {
                        var  sphereMask               = splits.ReceiverSphereCuller.Cull(worldBounds[i + 64].Value);
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

        unsafe struct CullingSplits
        {
            public UnsafeList<Plane>                      BackfacingReceiverPlanes;
            public UnsafeList<FrustumPlanes.PlanePacket4> SplitPlanePackets;
            public UnsafeList<FrustumPlanes.PlanePacket4> ReceiverPlanePackets;
            public UnsafeList<FrustumPlanes.PlanePacket4> CombinedSplitAndReceiverPlanePackets;
            public UnsafeList<CullingSplitData>           Splits;
            public ReceiverSphereCuller                   ReceiverSphereCuller;
            public bool                                   SphereTestEnabled;

            public CullingSplits(ref CullingContext cullingContext,
                                 NativeArray<Plane>               cullingPlanes,
                                 NativeArray<CullingSplit>        cullingSplits,
                                 ShadowProjection shadowProjection,
                                 AllocatorManager.AllocatorHandle allocator)
            {
                BackfacingReceiverPlanes             = default;
                SplitPlanePackets                    = default;
                ReceiverPlanePackets                 = default;
                CombinedSplitAndReceiverPlanePackets = default;
                Splits                               = default;
                ReceiverSphereCuller                 = default;
                SphereTestEnabled                    = false;

                // Initialize receiver planes first, so they are ready to be combined in
                // InitializeSplits
                InitializeReceiverPlanes(ref cullingContext, cullingPlanes, allocator);
                InitializeSplits(ref cullingContext, cullingPlanes, cullingSplits, allocator);
                InitializeSphereTest(ref cullingContext, cullingPlanes, shadowProjection, allocator);
            }

            private void InitializeReceiverPlanes(ref CullingContext cullingContext, NativeArray<Plane> cullingPlanes, AllocatorManager.AllocatorHandle allocator)
            {
#if DISABLE_HYBRID_RECEIVER_CULLING
                bool disableReceiverCulling = true;
#else
                bool disableReceiverCulling = false;
#endif
                // Receiver culling is only used for shadow maps
                if ((cullingContext.viewType != BatchCullingViewType.Light) ||
                    (cullingContext.receiverPlaneCount == 0) ||
                    disableReceiverCulling)
                {
                    // Make an empty array so job system doesn't complain.
                    ReceiverPlanePackets = new UnsafeList<FrustumPlanes.PlanePacket4>(0, allocator);
                    return;
                }

                bool isOrthographic = cullingContext.projectionType == BatchCullingProjectionType.Orthographic;
                int  numPlanes      = 0;

                var planes = cullingPlanes.GetSubArray(
                    cullingContext.receiverPlaneOffset,
                    cullingContext.receiverPlaneCount);
                BackfacingReceiverPlanes = new UnsafeList<Plane>(planes.Length, allocator);
                BackfacingReceiverPlanes.Resize(planes.Length);

                float3  lightDir = cullingContext.localToWorldMatrix.c2.xyz;
                Vector3 lightPos = cullingContext.localToWorldMatrix.c3.xyz;

                for (int i = 0; i < planes.Length; ++i)
                {
                    var    p = planes[i];
                    float3 n = p.normal;

                    const float kEpsilon = (float)1e-12;

                    // Compare with epsilon so that perpendicular planes are not counted
                    // as back facing
                    bool isBackfacing = isOrthographic ?
                                        math.dot(n, lightDir) < -kEpsilon :
                                        p.GetSide(lightPos);

                    if (isBackfacing)
                    {
                        BackfacingReceiverPlanes[numPlanes] = p;
                        ++numPlanes;
                    }
                }

                ReceiverPlanePackets = FrustumPlanes.BuildSOAPlanePackets(
                    BackfacingReceiverPlanes.GetSubNativeArray(0, numPlanes),
                    allocator);
                BackfacingReceiverPlanes.Resize(numPlanes);
            }

            private void InitializeSplits(ref CullingContext cullingContext,
                                          NativeArray<Plane>               cullingPlanes,
                                          NativeArray<CullingSplit>        cullingSplits,
                                          AllocatorManager.AllocatorHandle allocator)
            {
                int numSplits = cullingSplits.Length;

                Unity.Assertions.Assert.IsTrue(numSplits > 0,  "No culling splits provided, expected at least 1");
                Unity.Assertions.Assert.IsTrue(numSplits <= 8, "Split count too high, only up to 8 splits supported");

                int planePacketCount         = 0;
                int combinedPlanePacketCount = 0;
                for (int i = 0; i < numSplits; ++i)
                {
                    int splitIndex = i;

                    planePacketCount         += (cullingSplits[splitIndex].cullingPlaneCount + 3) / 4;
                    combinedPlanePacketCount +=
                        ((cullingSplits[splitIndex].cullingPlaneCount + BackfacingReceiverPlanes.Length) + 3) / 4;
                }

                SplitPlanePackets                    = new UnsafeList<FrustumPlanes.PlanePacket4>(planePacketCount, allocator);
                CombinedSplitAndReceiverPlanePackets = new UnsafeList<FrustumPlanes.PlanePacket4>(combinedPlanePacketCount, allocator);
                Splits                               = new UnsafeList<CullingSplitData>(numSplits, allocator);

                var combinedPlanes = new UnsafeList<Plane>(combinedPlanePacketCount * 4, allocator);

                int planeIndex         = 0;
                int combinedPlaneIndex = 0;

                for (int i = 0; i < numSplits; ++i)
                {
                    int splitIndex = i;

                    var    s = cullingSplits[splitIndex];
                    float3 p = s.sphereCenter;
                    float  r = s.sphereRadius;

                    if (s.sphereRadius <= 0)
                        r = 0;

                    var splitCullingPlanes = cullingPlanes.GetSubArray(s.cullingPlaneOffset, s.cullingPlaneCount);

                    var planePackets = FrustumPlanes.BuildSOAPlanePackets(
                        splitCullingPlanes,
                        allocator);

                    foreach (var pp in planePackets)
                        SplitPlanePackets.Add(pp);

                    combinedPlanes.Resize(splitCullingPlanes.Length + BackfacingReceiverPlanes.Length);

                    // Make combined packets that have both the split planes and the receiver planes so
                    // they can be tested simultaneously
                    UnsafeUtility.MemCpy(
                        combinedPlanes.Ptr,
                        splitCullingPlanes.GetUnsafeReadOnlyPtr(),
                        splitCullingPlanes.Length * UnsafeUtility.SizeOf<Plane>());
                    UnsafeUtility.MemCpy(
                        combinedPlanes.Ptr + splitCullingPlanes.Length,
                        BackfacingReceiverPlanes.Ptr,
                        BackfacingReceiverPlanes.Length * UnsafeUtility.SizeOf<Plane>());

                    var combined = FrustumPlanes.BuildSOAPlanePackets(
                        combinedPlanes.AsNativeArray(),
                        allocator);

                    foreach (var pp in combined)
                        CombinedSplitAndReceiverPlanePackets.Add(pp);

                    Splits.Add(new CullingSplitData
                    {
                        CullingSphereCenter             = p,
                        CullingSphereRadius             = r,
                        ShadowCascadeBlendCullingFactor = s.cascadeBlendCullingFactor,
                        PlanePacketOffset               = planeIndex,
                        PlanePacketCount                = planePackets.Length,
                        CombinedPlanePacketOffset       = combinedPlaneIndex,
                        CombinedPlanePacketCount        = combined.Length,
                    });

                    planeIndex         += planePackets.Length;
                    combinedPlaneIndex += combined.Length;
                }
            }

            private void InitializeSphereTest(ref CullingContext cullingContext,
                                              NativeArray<Plane>               cullingPlanes,
                                              ShadowProjection shadowProjection,
                                              AllocatorManager.AllocatorHandle allocator)
            {
                // Receiver sphere testing is only enabled if the cascade projection is stable
                bool projectionIsStable                = shadowProjection == ShadowProjection.StableFit;
                bool allSplitsHaveValidReceiverSpheres = true;
                for (int i = 0; i < Splits.Length; ++i)
                {
                    // This should also catch NaNs, which return false
                    // for every comparison.
                    if (!(Splits[i].CullingSphereRadius > 0))
                    {
                        allSplitsHaveValidReceiverSpheres = false;
                        break;
                    }
                }

                if (projectionIsStable && allSplitsHaveValidReceiverSpheres)
                {
                    ReceiverSphereCuller = new ReceiverSphereCuller(cullingContext, cullingPlanes, this, allocator);
                    SphereTestEnabled    = true;
                }
            }
        }

        struct ReceiverSphereCuller
        {
            float4            ReceiverSphereCenterX4;
            float4            ReceiverSphereCenterY4;
            float4            ReceiverSphereCenterZ4;
            float4            LSReceiverSphereCenterX4;
            float4            LSReceiverSphereCenterY4;
            float4            LSReceiverSphereCenterZ4;
            float4            ReceiverSphereRadius4;
            float4            CoreSphereRadius4;
            UnsafeList<float> receiverPlaneData;
            int               receiverPlanePaddedCount;

            float3 LightAxisX;
            float3 LightAxisY;
            float3 LightAxisZ;

            public ReceiverSphereCuller(in CullingContext cullingContext, in NativeArray<Plane> cullingPlanes, in CullingSplits splits, AllocatorManager.AllocatorHandle allocator)
            {
                int numSplits = splits.Splits.Length;

                Unity.Assertions.Assert.IsTrue(numSplits <= 4, "More than 4 culling splits is not supported for sphere testing");
                Unity.Assertions.Assert.IsTrue(numSplits > 0,  "No valid culling splits for sphere testing");

                if (numSplits > 4)
                    numSplits = 4;

                // Initialize with values that will always fail the sphere test
                ReceiverSphereCenterX4   = new float4(float.PositiveInfinity);
                ReceiverSphereCenterY4   = new float4(float.PositiveInfinity);
                ReceiverSphereCenterZ4   = new float4(float.PositiveInfinity);
                LSReceiverSphereCenterX4 = new float4(float.PositiveInfinity);
                LSReceiverSphereCenterY4 = new float4(float.PositiveInfinity);
                LSReceiverSphereCenterZ4 = new float4(float.PositiveInfinity);
                ReceiverSphereRadius4    = float4.zero;
                CoreSphereRadius4        = float4.zero;

                LightAxisX = cullingContext.localToWorldMatrix.c0.xyz;
                LightAxisY = cullingContext.localToWorldMatrix.c1.xyz;
                LightAxisZ = cullingContext.localToWorldMatrix.c2.xyz;

                //ShadowFrustumPlanes = GetUnsafeListView(cullingPlanes,
                //                                        cullingContext.receiverPlaneOffset,
                //                                        cullingContext.receiverPlaneCount);
                //ShadowFrustumPlanes = new UnsafeList<Plane>(cullingContext.receiverPlaneCount, allocator);
                Span<int> validPlaneIndices = stackalloc int[cullingContext.receiverPlaneCount];
                int       validPlanes       = 0;
                for (int i = 0; i < cullingContext.receiverPlaneCount; i++)
                {
                    var plane = cullingPlanes[cullingContext.receiverPlaneOffset + i];

                    float vdot = math.dot(LightAxisZ, plane.normal);

                    // No collision if the ray it the plane from behind
                    if (vdot > 0)
                        continue;

                    // is line parallel to the plane? if so, even if the line is
                    // at the plane it is not considered as intersection because
                    // it would be impossible to determine the point of intersection
                    if (Mathf.Approximately(vdot, 0.0F))
                        continue;

                    validPlaneIndices[validPlanes] = i;
                    validPlanes++;
                }
                receiverPlanePaddedCount = CollectionHelper.Align(validPlanes, 4);
                receiverPlaneData        = new UnsafeList<float>(receiverPlanePaddedCount * 5, allocator);
                receiverPlaneData.Resize(receiverPlanePaddedCount * 5);
                for (int i = 0; i < receiverPlanePaddedCount; i++)
                {
                    var plane                                           = cullingPlanes[cullingContext.receiverPlaneOffset + validPlaneIndices[i % validPlanes]];
                    receiverPlaneData[i]                                = math.dot(LightAxisZ, plane.normal);
                    receiverPlaneData[receiverPlanePaddedCount + i]     = plane.normal.x;
                    receiverPlaneData[receiverPlanePaddedCount * 2 + i] = plane.normal.y;
                    receiverPlaneData[receiverPlanePaddedCount * 3 + i] = plane.normal.z;
                    receiverPlaneData[receiverPlanePaddedCount * 4 + i] = plane.distance;
                }

                for (int i = 0; i < numSplits; ++i)
                {
                    int                  elementIndex           = i & 3;
                    ref CullingSplitData split                  = ref splits.Splits.ElementAt(i);
                    float3               lsReceiverSphereCenter = TransformToLightSpace(split.CullingSphereCenter, LightAxisX, LightAxisY, LightAxisZ);

                    ReceiverSphereCenterX4[elementIndex] = split.CullingSphereCenter.x;
                    ReceiverSphereCenterY4[elementIndex] = split.CullingSphereCenter.y;
                    ReceiverSphereCenterZ4[elementIndex] = split.CullingSphereCenter.z;

                    LSReceiverSphereCenterX4[elementIndex] = lsReceiverSphereCenter.x;
                    LSReceiverSphereCenterY4[elementIndex] = lsReceiverSphereCenter.y;
                    LSReceiverSphereCenterZ4[elementIndex] = lsReceiverSphereCenter.z;

                    ReceiverSphereRadius4[elementIndex] = split.CullingSphereRadius;
                    CoreSphereRadius4[elementIndex]     = split.CullingSphereRadius * split.ShadowCascadeBlendCullingFactor;
                }
            }

            public int Cull(AABB aabb)
            {
                int visibleSplitMask = CullSIMD(aabb, false, out _);

                return visibleSplitMask;
            }

            public int CullBatch(AABB aabb, out bool appliesToWholeBatch)
            {
                int visibleSplitMask = CullSIMD(aabb, true, out int isBatchCompletelyInside);
                appliesToWholeBatch  = visibleSplitMask == isBatchCompletelyInside;
                return visibleSplitMask;
            }

            int CullSIMD(AABB aabb, bool testBatch, out int isBatchCompletelyInside)
            {
                float4 casterRadius4     = new float4(math.length(aabb.Extents));
                float4 combinedRadius4   = casterRadius4 + ReceiverSphereRadius4;
                float4 combinedRadiusSq4 = combinedRadius4 * combinedRadius4;

                float3 lsCasterCenter   = TransformToLightSpace(aabb.Center, LightAxisX, LightAxisY, LightAxisZ);
                float4 lsCasterCenterX4 = lsCasterCenter.xxxx;
                float4 lsCasterCenterY4 = lsCasterCenter.yyyy;
                float4 lsCasterCenterZ4 = lsCasterCenter.zzzz;

                float4 lsCasterToReceiverSphereX4   = lsCasterCenterX4 - LSReceiverSphereCenterX4;
                float4 lsCasterToReceiverSphereY4   = lsCasterCenterY4 - LSReceiverSphereCenterY4;
                float4 lsCasterToReceiverSphereSqX4 = lsCasterToReceiverSphereX4 * lsCasterToReceiverSphereX4;
                float4 lsCasterToReceiverSphereSqY4 = lsCasterToReceiverSphereY4 * lsCasterToReceiverSphereY4;

                float4 lsCasterToReceiverSphereDistanceSq4 = lsCasterToReceiverSphereSqX4 + lsCasterToReceiverSphereSqY4;
                bool4  doCirclesOverlap4                   = lsCasterToReceiverSphereDistanceSq4 <= combinedRadiusSq4;

                float4 lsZMaxAccountingForCasterRadius4 = LSReceiverSphereCenterZ4 + math.sqrt(combinedRadiusSq4 - lsCasterToReceiverSphereSqX4 - lsCasterToReceiverSphereSqY4);
                bool4  isBehindCascade4                 = lsCasterCenterZ4 <= lsZMaxAccountingForCasterRadius4;

                isBatchCompletelyInside = 0;
                if (testBatch)
                {
                    float4 lsCasterToReceiverSphereZ4   = lsCasterCenterZ4 - LSReceiverSphereCenterZ4;
                    float4 lsCasterToReceiverSphereSqZ4 = lsCasterToReceiverSphereZ4 * lsCasterToReceiverSphereZ4;
                    float4 distances4                   = math.sqrt(lsCasterToReceiverSphereDistanceSq4 + lsCasterToReceiverSphereSqZ4);
                    bool4  isBoundsFullyInside4         = distances4 + casterRadius4 < ReceiverSphereRadius4;
                    isBatchCompletelyInside             = math.bitmask(isBoundsFullyInside4);
                }

                int isFullyCoveredByCascadeMask = 0b1111;

#if !DISABLE_SHADOW_CULLING_CAPSULE_TEST
                float3 shadowCapsuleBegin;
                float3 shadowCapsuleEnd;
                float  shadowCapsuleRadius;
                ComputeShadowCapsule(aabb.Center, casterRadius4.x, out shadowCapsuleBegin, out shadowCapsuleEnd, out shadowCapsuleRadius);

                bool4 isFullyCoveredByCascade4 = IsCapsuleInsideSphereSIMD(shadowCapsuleBegin, shadowCapsuleEnd, shadowCapsuleRadius,
                                                                           ReceiverSphereCenterX4, ReceiverSphereCenterY4, ReceiverSphereCenterZ4, CoreSphereRadius4);

                if (math.any(isFullyCoveredByCascade4))
                {
                    // The goal here is to find the first non-zero bit in the mask, then set all the bits after it to 0 and all the ones before it to 1.

                    // So for example 1100 should become 0111. The transformation logic looks like this:
                    // Find first non-zero bit with tzcnt and build a mask -> 0100
                    // Left shift by one -> 1000
                    // Subtract 1 -> 0111

                    int boolMask                = math.bitmask(isFullyCoveredByCascade4);
                    isFullyCoveredByCascadeMask = 1 << math.tzcnt(boolMask);
                    isFullyCoveredByCascadeMask = isFullyCoveredByCascadeMask << 1;
                    isFullyCoveredByCascadeMask = isFullyCoveredByCascadeMask - 1;
                }
#endif

                return math.bitmask(doCirclesOverlap4 & isBehindCascade4) & isFullyCoveredByCascadeMask;
            }

            void ComputeShadowCapsule(float3 casterPosition, float casterRadius,
                                      out float3 shadowCapsuleBegin, out float3 shadowCapsuleEnd, out float shadowCapsuleRadius)
            {
                float shadowCapsuleLength = GetShadowVolumeLengthFromCasterAndFrustumAndLightDir(casterPosition, casterRadius);

                shadowCapsuleBegin  = casterPosition;
                shadowCapsuleEnd    = casterPosition + shadowCapsuleLength * LightAxisZ;
                shadowCapsuleRadius = casterRadius;
            }

            float GetShadowVolumeLengthFromCasterAndFrustumAndLightDir(float3 casterPosition, float casterRadius)
            {
                // The idea here is to find the capsule that goes from the caster and cover all possible shadow receiver in the frustum.
                // First we find the distance from the caster center to the frustum
                int   planeIndex;
                float distFromCasterToFrustumInLightDirection = RayDistanceToFrustumOriented(casterPosition, out planeIndex);
                if (planeIndex == -1)
                {
                    // Shadow caster center is outside of frustum and ray do not intersect it.
                    // Shadow volume is thus the caster bounding sphere.
                    return 0;
                }

                // Then we need to account for the radius of the capsule.
                // The distance returned might actually be too large in the case of a caster outside of the frustum
                // however detecting this would require to run another RayDistanceToFrustum and the case is rare enough
                // so its not a problem (these caster will just be less likely to be culled away).
                Unity.Assertions.Assert.IsTrue(planeIndex >= 0 && planeIndex < receiverPlanePaddedCount);

                var normal = new float3(receiverPlaneData[receiverPlanePaddedCount + planeIndex],
                                        receiverPlaneData[receiverPlanePaddedCount * 2 + planeIndex],
                                        receiverPlaneData[receiverPlanePaddedCount * 3 + planeIndex]);
                var   plane                              = new Plane(normal, receiverPlaneData[receiverPlanePaddedCount * 4 + planeIndex]);
                float distFromCasterToPlane              = math.abs(plane.GetDistanceToPoint(casterPosition));
                float sinAlpha                           = distFromCasterToPlane / (distFromCasterToFrustumInLightDirection + 0.0001f);
                float tanAlpha                           = sinAlpha / (math.sqrt(1.0f - (sinAlpha * sinAlpha)));
                distFromCasterToFrustumInLightDirection += casterRadius / (tanAlpha + 0.0001f);

                return distFromCasterToFrustumInLightDirection;
            }

            // Returns the shortest distance to the front facing plane from the ray.
            // Return -1 if no plane intersect this ray.
            // planeNumber will contain the index of the plane found or -1.
            unsafe float RayDistanceToFrustumOriented(float3 rayOrigin, out int planeNumber)
            {
                var    rayOriginX   = rayOrigin.xxxx;
                var    rayOriginY   = rayOrigin.yyyy;
                var    rayOriginZ   = rayOrigin.zzzz;
                float4 maxDistances = float.PositiveInfinity;
                int4   planeNumbers = -1;
                Unity.Burst.CompilerServices.Hint.Assume(receiverPlanePaddedCount >= 4);
                var stride         = receiverPlanePaddedCount / 4;
                var planeDataSimd  = new ReadOnlySpan<float4>(receiverPlaneData.Ptr, stride * 5);
                var vdots          = planeDataSimd.Slice(0, stride);
                var planeNormalXs  = planeDataSimd.Slice(stride, stride);
                var planeNormalYs  = planeDataSimd.Slice(stride + stride, stride);
                var planeNormalZs  = planeDataSimd.Slice(stride * 3, stride);
                var planeDistances = planeDataSimd.Slice(stride * 4, stride);
                for (int i = 0; i < receiverPlanePaddedCount; i += 4)
                {
                    var vdot          = vdots[i];
                    var planeNormalX  = planeNormalXs[i];
                    var planeNormalY  = planeNormalYs[i];
                    var planeNormalZ  = planeNormalZs[i];
                    var planeDistance = planeDistances[i];

                    var ndot     = -(rayOriginX * planeNormalX + rayOriginY * planeNormalY + rayOriginZ * planeNormalZ) - planeDistance;
                    var distance = ndot / vdot;
                    var isBest   = distance < maxDistances & distance > 0f;
                    maxDistances = math.select(maxDistances, distance, isBest);
                    planeNumbers = math.select(-1, i + new int4(0, 1, 2, 3), isBest);
                }
                var maxDistance = math.cmin(maxDistances);
                planeNumber     = math.cmax(planeNumbers);

                return planeNumber != -1 ? maxDistance : -1.0f;
            }

            static bool IntersectRayPlaneOriented(Ray ray, Plane plane, out float distance)
            {
                float vdot = math.dot(ray.direction, plane.normal);
                float ndot = -math.dot(ray.origin, plane.normal) - plane.distance;

                // the resulting intersection is behind the origin of the ray
                // if the result is negative ( enter < 0 )
                distance = ndot / vdot;

                return distance > 0.0F;
            }

            static bool4 IsInsideSphereSIMD(float4 sphereCenterX, float4 sphereCenterY, float4 sphereCenterZ, float4 sphereRadius,
                                            float4 containingSphereCenterX, float4 containingSphereCenterY, float4 containingSphereCenterZ, float4 containingSphereRadius)
            {
                float4 dx = containingSphereCenterX - sphereCenterX;
                float4 dy = containingSphereCenterY - sphereCenterY;
                float4 dz = containingSphereCenterZ - sphereCenterZ;

                float4 squaredDistance    = dx * dx + dy * dy + dz * dz;
                float4 radiusDelta        = containingSphereRadius - sphereRadius;
                float4 squaredRadiusDelta = radiusDelta * radiusDelta;

                bool4 canSphereFit = sphereRadius < containingSphereRadius;
                bool4 distanceTest = squaredDistance < squaredRadiusDelta;

                return canSphereFit & distanceTest;
            }

            static bool4 IsCapsuleInsideSphereSIMD(float3 capsuleBegin, float3 capsuleEnd, float capsuleRadius,
                                                   float4 sphereCenterX, float4 sphereCenterY, float4 sphereCenterZ, float4 sphereRadius)
            {
                float4 beginSphereX = capsuleBegin.xxxx;
                float4 beginSphereY = capsuleBegin.yyyy;
                float4 beginSphereZ = capsuleBegin.zzzz;

                float4 endSphereX = capsuleEnd.xxxx;
                float4 endSphereY = capsuleEnd.yyyy;
                float4 endSphereZ = capsuleEnd.zzzz;

                float4 capsuleRadius4 = new float4(capsuleRadius);

                bool4 isInsideBeginSphere = IsInsideSphereSIMD(beginSphereX, beginSphereY, beginSphereZ, capsuleRadius4,
                                                               sphereCenterX, sphereCenterY, sphereCenterZ, sphereRadius);

                bool4 isInsideEndSphere = IsInsideSphereSIMD(endSphereX, endSphereY, endSphereZ, capsuleRadius4,
                                                             sphereCenterX, sphereCenterY, sphereCenterZ, sphereRadius);

                return isInsideBeginSphere & isInsideEndSphere;
            }

            static float3 TransformToLightSpace(float3 positionWS, float3 lightAxisX, float3 lightAxisY, float3 lightAxisZ) => new float3(
                math.dot(positionWS, lightAxisX),
                math.dot(positionWS, lightAxisY),
                math.dot(positionWS, lightAxisZ));
        }
    }
}

