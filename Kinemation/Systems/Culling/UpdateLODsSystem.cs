using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

// Note: The following comment pertains to Entities 0.51. The behavior has not been compared to 1.0 yet.
// Unity's LOD system is currently a bit of a mess. The behavior of this version has been modified for more
// predictable behavior at the cost of performance. Some of the things that make no sense in Unity's original
// implementation which have been modified in this version include:
// 1) ForceLowLOD is created with cleared memory and then assigned to the SelectLodEnabled job as [ReadOnly].
//    It is not touched anywhere else in HR V2.
// 2) The algorithm tries to cache LOD levels by camera. This doesn't make much sense since there is typically
//    more than one camera rendering (shadows). However, since the checks are cheap and there's a chance that
//    two cameras have similar LOD requirements, I'm leaving that logic in.
// 3) There's a ResetLod function which is public API, but is only called at the beginning of each render by
//    EntitiesGraphicsV2RenderSystem. I suspect this is to cover structural changes. But since we have a real system, we
//    can do that a smarter way using order versions.
// 4) CullingStats gets cleared immediately after scheduling a job using it every time there's a LOD update.
//    I think this is actually supposed to be cleared every frame.

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public unsafe partial struct UpdateLODsSystem : ISystem
    {
        LODGroupExtensions.LODParams m_PrevLODParams;
        float3                       m_PrevCameraPos;
        float                        m_PrevLodDistanceScale;

        EntityQuery m_query;

        int  m_lastLodRangeOrderVersion;
        int  m_lastChunkInfoOrderVersion;
        bool m_firstRun;

        SelectLodEnabledJob                 m_job;
        CopyLodsToPerCameraVisisbilitiesJob m_copyJob;

        LatiosWorldUnmanaged latiosWorld;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().WithAll<ChunkHeader>(true).WithAll<EntitiesGraphicsChunkInfo>(false).WithAll<ChunkPerCameraCullingMask>(false).Build();
            m_firstRun  = true;

            m_job = new SelectLodEnabledJob
            {
                RootLODRanges             = state.GetComponentTypeHandle<RootLODRange>(true),
                RootLODReferencePoints    = state.GetComponentTypeHandle<RootLODWorldReferencePoint>(true),
                LODRanges                 = state.GetComponentTypeHandle<LODRange>(true),
                LODReferencePoints        = state.GetComponentTypeHandle<LODWorldReferencePoint>(true),
                EntitiesGraphicsChunkInfo = state.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(),
                ChunkHeader               = state.GetComponentTypeHandle<ChunkHeader>(),
            };

            m_copyJob = new CopyLodsToPerCameraVisisbilitiesJob
            {
                chunkInfoHandle     = state.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                perCameraMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>()
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var lodParams = LODGroupExtensions.CalculateLODParams(latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>().lodParameters);

            bool lodParamsMatchPrev  = lodParams.Equals(m_PrevLODParams);
            var  resetLod            = !lodParamsMatchPrev;
            resetLod                |= m_firstRun;
            resetLod                |= (state.EntityManager.GetComponentOrderVersion<LODRange>() - m_lastLodRangeOrderVersion) > 0;
            resetLod                |= (state.EntityManager.GetComponentOrderVersion<EntitiesGraphicsChunkInfo>() - m_lastChunkInfoOrderVersion) > 0;

            if (resetLod)
            {
                float cameraMoveDistance      = math.length(m_PrevCameraPos - lodParams.cameraPos);
                var   lodDistanceScaleChanged = lodParams.distanceScale != m_PrevLodDistanceScale;

                m_job.RootLODRanges.Update(ref state);
                m_job.RootLODReferencePoints.Update(ref state);
                m_job.LODRanges.Update(ref state);
                m_job.LODReferencePoints.Update(ref state);
                m_job.EntitiesGraphicsChunkInfo.Update(ref state);
                m_job.ChunkHeader.Update(ref state);

                m_job.lodParamsMatchPrev = lodParamsMatchPrev;
                m_job.lastSystemVersion  = state.LastSystemVersion;

                m_job.LODParams                 = lodParams;
                m_job.CameraMoveDistanceFixed16 = Fixed16CamDistance.FromFloatCeil(cameraMoveDistance * lodParams.distanceScale);
                m_job.DistanceScale             = lodParams.distanceScale;
                m_job.DistanceScaleChanged      = lodDistanceScaleChanged;

                state.Dependency = m_job.ScheduleParallelByRef(m_query, state.Dependency);

                m_PrevLODParams        = lodParams;
                m_PrevLodDistanceScale = lodParams.distanceScale;
                m_PrevCameraPos        = lodParams.cameraPos;
                m_firstRun             = false;
            }
            m_lastLodRangeOrderVersion  = state.EntityManager.GetComponentOrderVersion<LODRange>();
            m_lastChunkInfoOrderVersion = state.EntityManager.GetComponentOrderVersion<EntitiesGraphicsChunkInfo>();

            m_copyJob.perCameraMaskHandle.Update(ref state);
            m_copyJob.chunkInfoHandle.Update(ref state);
            state.Dependency = m_copyJob.ScheduleParallelByRef(m_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        unsafe struct SelectLodEnabledJob : IJobChunk
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

            public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>    ChunkHeader;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entitiesGraphicsChunkInfoArray = metaChunk.GetNativeArray(ref EntitiesGraphicsChunkInfo);
                var chunkHeaderArray               = metaChunk.GetNativeArray(ref ChunkHeader);

                for (var entityIndex = 0; entityIndex < metaChunk.Count; entityIndex++)
                {
                    var entitiesGraphicsChunkInfo = entitiesGraphicsChunkInfoArray[entityIndex];
                    if (!entitiesGraphicsChunkInfo.Valid)
                        continue;

                    var chunkHeader = chunkHeaderArray[entityIndex];
                    var chunk       = chunkHeader.ArchetypeChunk;

                    var chunkOrderChanged = chunk.DidOrderChange(lastSystemVersion);

                    var batchIndex         = entitiesGraphicsChunkInfo.BatchIndex;
                    var chunkInstanceCount = chunk.Count;
                    var isOrtho            = LODParams.isOrtho;

                    ref var                 chunkCullingData      = ref entitiesGraphicsChunkInfo.CullingData;
                    ChunkInstanceLodEnabled chunkEntityLodEnabled = chunkCullingData.InstanceLodEnableds;

                    if (0 == (chunkCullingData.Flags & EntitiesGraphicsChunkCullingData.kFlagHasLodData))
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

                            var rootLODRanges          = chunk.GetNativeArray(ref RootLODRanges);
                            var rootLODReferencePoints = chunk.GetNativeArray(ref RootLODReferencePoints);
                            var lodRanges              = chunk.GetNativeArray(ref LODRanges);
                            var lodReferencePoints     = chunk.GetNativeArray(ref LODReferencePoints);

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

                    chunkCullingData.InstanceLodEnableds        = chunkEntityLodEnabled;
                    entitiesGraphicsChunkInfoArray[entityIndex] = entitiesGraphicsChunkInfo;
                }
            }
        }

        [BurstCompile]
        struct CopyLodsToPerCameraVisisbilitiesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> chunkInfoHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingMask>            perCameraMaskHandle;

            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunkInfoArray = metaChunk.GetNativeArray(ref chunkInfoHandle);
                var maskArray      = metaChunk.GetNativeArray(ref perCameraMaskHandle);

                for (int i = 0; i < metaChunk.Count; i++)
                {
                    if (maskArray[i].lower.Value == 0)
                        continue;

                    var lods = chunkInfoArray[i].CullingData.InstanceLodEnableds;
#if UNITY_EDITOR
                    // In the editor, picking and highlighting results in granular filtering in InitializeAndFilterPerCamerSystem.
                    var mask = maskArray[i];
                    mask.lower.Value &= lods.Enabled[0];
                    mask.upper.Value &= lods.Enabled[1];
                    maskArray[i]      = mask;
#else
                    maskArray[i] = new ChunkPerCameraCullingMask
                    {
                        lower = new BitField64(lods.Enabled[0]),
                        upper = new BitField64(lods.Enabled[1])
                    };
#endif
                }
            }
        }
    }
}

