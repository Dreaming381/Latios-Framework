using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

// Unity's LOD system is currently a bit of a mess. The behavior of this version has been modified for more
// predictable behavior at the cost of performance. Some of the things that make no sense in Unity's original
// implementation which have been modified in this version include:
// 1) ForceLowLOD is created with cleared memory and then assigned to the SelectLodEnabled job as [ReadOnly].
//    It is not touched anywhere else in HR V2.
// 2) The algorithm tries to cache LOD levels by camera. This doesn't make much sense since there is typically
//    more than one camera rendering (shadows). However, since the checks are cheap and there's a chance that
//    two cameras have similar LOD requirements, I'm leaving that logic in.
// 3) There's a ResetLod function which is public API, but is only called at the beginning of each render by
//    HybridV2RenderSystem. I suspect this is to cover structural changes. But since we have a real system, we
//    can do that a smarter way using order versions.
// 4) CullingStats gets cleared immediately after scheduling a job using it every time there's a LOD update.
//    I think this is actually supposed to be cleared every frame.

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public unsafe partial class UpdateLODsSystem : SubSystem
    {
        LODGroupExtensions.LODParams m_PrevLODParams = default;
        float3                       m_PrevCameraPos;
        float                        m_PrevLodDistanceScale;

        EntityQuery m_query;

        int  m_lastLodRangeOrderVersion;
        int  m_lastChunkInfoOrderVersion;
        bool m_firstRun = true;

        protected override void OnCreate()
        {
            m_query = Fluent.WithAll<ChunkHeader>(true).WithAll<HybridChunkInfo>(false).Build();
        }

        protected override void OnUpdate()
        {
            var brgContext     = worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>(false);
            var cullingContext = brgContext.cullingContext;

            var lodParams = LODGroupExtensions.CalculateLODParams(cullingContext.lodParameters);

            var planes = FrustumPlanes.BuildSOAPlanePackets(cullingContext.cullingPlanes, Allocator.TempJob);

            bool lodParamsMatchPrev  = lodParams.Equals(m_PrevLODParams);
            var  resetLod            = !lodParamsMatchPrev;
            resetLod                |= m_firstRun;
            resetLod                |= (EntityManager.GetComponentOrderVersion<LODRange>() - m_lastLodRangeOrderVersion) > 0;
            resetLod                |= (EntityManager.GetComponentOrderVersion<HybridChunkInfo>() - m_lastChunkInfoOrderVersion) > 0;

            if (resetLod)
            {
                float cameraMoveDistance      = math.length(m_PrevCameraPos - lodParams.cameraPos);
                var   lodDistanceScaleChanged = lodParams.distanceScale != m_PrevLodDistanceScale;

                var selectLodEnabledJob = new SelectLodEnabled
                {
                    lodParamsMatchPrev = lodParamsMatchPrev,
                    lastSystemVersion  = LastSystemVersion,

                    LODParams                 = lodParams,
                    RootLODRanges             = GetComponentTypeHandle<RootLODRange>(true),
                    RootLODReferencePoints    = GetComponentTypeHandle<RootLODWorldReferencePoint>(true),
                    LODRanges                 = GetComponentTypeHandle<LODRange>(true),
                    LODReferencePoints        = GetComponentTypeHandle<LODWorldReferencePoint>(true),
                    HybridChunkInfo           = GetComponentTypeHandle<HybridChunkInfo>(),
                    ChunkHeader               = GetComponentTypeHandle<ChunkHeader>(),
                    CameraMoveDistanceFixed16 =
                        Fixed16CamDistance.FromFloatCeil(cameraMoveDistance * lodParams.distanceScale),
                    DistanceScale        = lodParams.distanceScale,
                    DistanceScaleChanged = lodDistanceScaleChanged,
                };

                Dependency = selectLodEnabledJob.ScheduleParallel(m_query, Dependency);

                m_PrevLODParams        = lodParams;
                m_PrevLodDistanceScale = lodParams.distanceScale;
                m_PrevCameraPos        = lodParams.cameraPos;
                m_firstRun             = false;
            }
            m_lastLodRangeOrderVersion  = EntityManager.GetComponentOrderVersion<LODRange>();
            m_lastChunkInfoOrderVersion = EntityManager.GetComponentOrderVersion<HybridChunkInfo>();
            Dependency                  = planes.Dispose(Dependency);
        }

        [BurstCompile]
        unsafe struct SelectLodEnabled : IJobEntityBatch
        {
            public bool lodParamsMatchPrev;
            public uint lastSystemVersion;

            [ReadOnly] public LODGroupExtensions.LODParams                    LODParams;
            [ReadOnly] public ComponentTypeHandle<RootLODRange>               RootLODRanges;
            [ReadOnly] public ComponentTypeHandle<RootLODWorldReferencePoint> RootLODReferencePoints;
            [ReadOnly] public ComponentTypeHandle<LODRange>                   LODRanges;
            [ReadOnly] public ComponentTypeHandle<LODWorldReferencePoint>     LODReferencePoints;
            public ushort                                                     CameraMoveDistanceFixed16;
            public float                                                      DistanceScale;
            public bool                                                       DistanceScaleChanged;

            public ComponentTypeHandle<HybridChunkInfo>        HybridChunkInfo;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader> ChunkHeader;

            public void Execute(ArchetypeChunk archetypeChunk, int chunkIndex)
            {
                var hybridChunkInfoArray = archetypeChunk.GetNativeArray(HybridChunkInfo);
                var chunkHeaderArray     = archetypeChunk.GetNativeArray(ChunkHeader);

                for (var entityIndex = 0; entityIndex < archetypeChunk.Count; entityIndex++)
                {
                    var hybridChunkInfo = hybridChunkInfoArray[entityIndex];
                    if (!hybridChunkInfo.Valid)
                        continue;

                    var chunkHeader = chunkHeaderArray[entityIndex];
                    var chunk       = chunkHeader.ArchetypeChunk;

                    var chunkOrderChanged = chunk.DidOrderChange(lastSystemVersion);

                    var internalBatchIndex = hybridChunkInfo.InternalIndex;
                    var chunkInstanceCount = chunk.Count;
                    var isOrtho            = LODParams.isOrtho;

                    ref var                 chunkCullingData      = ref hybridChunkInfo.CullingData;
                    ChunkInstanceLodEnabled chunkEntityLodEnabled = chunkCullingData.InstanceLodEnableds;

                    if (0 == (chunkCullingData.Flags & HybridChunkCullingData.kFlagHasLodData))
                    {
                        chunkEntityLodEnabled.Enabled[0] = 0;
                        chunkEntityLodEnabled.Enabled[1] = 0;
                        for (int i = 0; i < chunkInstanceCount; ++i)
                        {
                            int wordIndex                             = i >> 6;
                            int bitIndex                              = i & 63;
                            chunkEntityLodEnabled.Enabled[wordIndex] |= 1ul << bitIndex;
                        }
                    }
                    else
                    {
                        int diff                              = (int)chunkCullingData.MovementGraceFixed16 - CameraMoveDistanceFixed16;
                        chunkCullingData.MovementGraceFixed16 = (ushort)math.max(0, diff);

                        var graceExpired = chunkCullingData.MovementGraceFixed16 == 0;
                        if (graceExpired || DistanceScaleChanged || chunkOrderChanged)
                        {
                            chunkEntityLodEnabled.Enabled[0] = 0;
                            chunkEntityLodEnabled.Enabled[1] = 0;

                            var rootLODRanges          = chunk.GetNativeArray(RootLODRanges);
                            var rootLODReferencePoints = chunk.GetNativeArray(RootLODReferencePoints);
                            var lodRanges              = chunk.GetNativeArray(LODRanges);
                            var lodReferencePoints     = chunk.GetNativeArray(LODReferencePoints);

                            float graceDistance = float.MaxValue;

                            for (int i = 0; i < chunkInstanceCount; i++)
                            {
                                var rootLODRange          = rootLODRanges[i];
                                var rootLODReferencePoint = rootLODReferencePoints[i];

                                var rootLodDistance =
                                    math.select(
                                        DistanceScale *
                                        math.length(LODParams.cameraPos - rootLODReferencePoint.Value),
                                        DistanceScale, isOrtho);

                                float rootMinDist = rootLODRange.LOD.MinDist;
                                float rootMaxDist = rootLODRange.LOD.MaxDist;

                                graceDistance = math.min(math.abs(rootLodDistance - rootMinDist), graceDistance);
                                graceDistance = math.min(math.abs(rootLodDistance - rootMaxDist), graceDistance);

                                var rootLodIntersect = (rootLodDistance < rootMaxDist) && (rootLodDistance >= rootMinDist);

                                if (rootLodIntersect)
                                {
                                    var lodRange          = lodRanges[i];
                                    var lodReferencePoint = lodReferencePoints[i];

                                    var instanceDistance =
                                        math.select(
                                            DistanceScale *
                                            math.length(LODParams.cameraPos -
                                                        lodReferencePoint.Value), DistanceScale,
                                            isOrtho);

                                    var instanceLodIntersect =
                                        (instanceDistance < lodRange.MaxDist) &&
                                        (instanceDistance >= lodRange.MinDist);

                                    graceDistance = math.min(math.abs(instanceDistance - lodRange.MinDist),
                                                             graceDistance);
                                    graceDistance = math.min(math.abs(instanceDistance - lodRange.MaxDist),
                                                             graceDistance);

                                    if (instanceLodIntersect)
                                    {
                                        var index     = i;
                                        var wordIndex = index >> 6;
                                        var bitIndex  = index & 0x3f;
                                        var lodWord   = chunkEntityLodEnabled.Enabled[wordIndex];

                                        lodWord                                  |= 1UL << bitIndex;
                                        chunkEntityLodEnabled.Enabled[wordIndex]  = lodWord;
                                    }
                                }
                            }

                            chunkCullingData.MovementGraceFixed16 = Fixed16CamDistance.FromFloatFloor(graceDistance);
                        }
                    }

                    chunkCullingData.InstanceLodEnableds = chunkEntityLodEnabled;
                    hybridChunkInfoArray[entityIndex]    = hybridChunkInfo;
                }
            }
        }
    }
}

