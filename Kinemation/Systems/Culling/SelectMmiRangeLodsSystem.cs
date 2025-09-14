using Latios.Transforms;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct SelectMmiRangeLodsSystem : ISystem, ISystemShouldUpdate
    {
        LatiosWorldUnmanaged                    latiosWorld;
        WorldTransformReadOnlyAspect.TypeHandle m_worldTransformHandle;

        EntityQuery m_query;

        int   m_maximumLODLevel;
        float m_lodBias;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld            = state.GetLatiosWorldUnmanaged();
            m_worldTransformHandle = new WorldTransformReadOnlyAspect.TypeHandle(ref state);

            m_query = state.Fluent().With<MaterialMeshInfo, LodCrossfade>(false).With<UseMmiRangeLodTag, WorldRenderBounds>(true)
                      .WithAnyEnabled<MmiRange2LodSelect, MmiRange3LodSelect>(true).WithWorldTransformReadOnly().Build();

            latiosWorld.worldBlackboardEntity.AddComponentDataIfMissing(new MeshLodCrossfadeMargin { margin = (half)0.05f });
        }

        public bool ShouldUpdateSystem(ref SystemState state)
        {
            m_maximumLODLevel = UnityEngine.QualitySettings.maximumLODLevel;
            m_lodBias         = UnityEngine.QualitySettings.lodBias;
            return m_maximumLODLevel < 2;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var parameters = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>().lodParameters;

            m_worldTransformHandle.Update(ref state);

            float cameraFactorNoBias = LodUtilities.CameraFactorFrom(in parameters, 1f);

            state.Dependency = new Job
            {
                perCameraMaskHandle    = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                worldTransformHandle   = m_worldTransformHandle,
                boundsHandle           = GetComponentTypeHandle<WorldRenderBounds>(true),
                select2Handle          = GetComponentTypeHandle<MmiRange2LodSelect>(true),
                select3Handle          = GetComponentTypeHandle<MmiRange3LodSelect>(true),
                lodGroupCrossfades     = GetComponentTypeHandle<LodHeightPercentagesWithCrossfadeMargins>(true),
                mmiHandle              = GetComponentTypeHandle<MaterialMeshInfo>(false),
                crossfadeHandle        = GetComponentTypeHandle<LodCrossfade>(false),
                meshLodHandle          = GetComponentTypeHandle<MeshLod>(false),
                meshLodCurveHandle     = GetComponentTypeHandle<MeshLodCurve>(true),
                meshLodCrossfadeMargin = latiosWorld.worldBlackboardEntity.GetComponentData<MeshLodCrossfadeMargin>().margin,
                cameraPosition         = parameters.cameraPosition,
                isPerspective          = !parameters.isOrthographic,
                cameraFactor           = cameraFactorNoBias * m_lodBias,
                cameraFactorNoBias     = cameraFactorNoBias,
                maxResolutionLodLevel  = m_maximumLODLevel
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        unsafe struct Job : IJobChunk
        {
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle                       worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<WorldRenderBounds>                        boundsHandle;
            [ReadOnly] public ComponentTypeHandle<MmiRange2LodSelect>                       select2Handle;
            [ReadOnly] public ComponentTypeHandle<MmiRange3LodSelect>                       select3Handle;
            [ReadOnly] public ComponentTypeHandle<LodHeightPercentagesWithCrossfadeMargins> lodGroupCrossfades;
            [ReadOnly] public ComponentTypeHandle<MeshLodCurve>                             meshLodCurveHandle;

            public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraMaskHandle;
            public ComponentTypeHandle<MaterialMeshInfo>          mmiHandle;
            public ComponentTypeHandle<LodCrossfade>              crossfadeHandle;
            public ComponentTypeHandle<MeshLod>                   meshLodHandle;

            public float3 cameraPosition;
            public float  cameraFactor;
            public float  cameraFactorNoBias;
            public float  meshLodCrossfadeMargin;
            public int    maxResolutionLodLevel;
            public bool   isPerspective;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var mask = ref chunk.GetChunkComponentRefRW(ref perCameraMaskHandle);
                if ((mask.upper.Value | mask.lower.Value) == 0)
                    return;

                var transforms              = worldTransformHandle.Resolve(chunk);
                var boundsArray             = (WorldRenderBounds*)chunk.GetRequiredComponentDataPtrRO(ref boundsHandle);
                var mmis                    = (MaterialMeshInfo*)chunk.GetRequiredComponentDataPtrRW(ref mmiHandle);
                var crossfades              = (LodCrossfade*)chunk.GetRequiredComponentDataPtrRW(ref crossfadeHandle);
                var crossfadesEnabled       = chunk.GetEnabledMask(ref crossfadeHandle);
                var select2s                = chunk.GetComponentDataPtrRO(ref select2Handle);
                var select3s                = chunk.GetComponentDataPtrRO(ref select3Handle);
                var lodGroupPercentages     = chunk.GetComponentDataPtrRO(ref lodGroupCrossfades);
                var meshLods                = chunk.GetComponentDataPtrRW(ref meshLodHandle);
                var enableMeshLodCrossfades = chunk.GetEnabledMask(ref meshLodHandle);
                var meshLodCurves           = chunk.GetComponentDataPtrRO(ref meshLodCurveHandle);
                var enumerator              = new ChunkEntityEnumerator(true, new v128(mask.lower.Value, mask.upper.Value), chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    MmiRange3LodSelect select;
                    MaterialMeshInfo   mmi;
                    int                maxLodSupported;
                    if (select3s != null)
                    {
                        select          = select3s[i];
                        mmi             = mmis[i];
                        maxLodSupported = 2;
                    }
                    else if (select2s != null)
                    {
                        select = new MmiRange3LodSelect
                        {
                            fullLod0ScreenHeightFraction    = select2s[i].fullLod0ScreenHeightFraction,
                            fullLod1ScreenHeightMaxFraction = (half)math.abs(select2s[i].fullLod1ScreenHeightFraction),
                            fullLod1ScreenHeightMinFraction = default,
                            fullLod2ScreenHeightFraction    = (half)math.select(0f, -1f, select2s[i].fullLod1ScreenHeightFraction < 0f),
                        };
                        mmi             = mmis[i];
                        maxLodSupported = 1;
                    }
                    else
                    {
                        select = new MmiRange3LodSelect
                        {
                            fullLod0ScreenHeightFraction    = default,
                            fullLod1ScreenHeightMaxFraction = default,
                            fullLod1ScreenHeightMinFraction = default,
                            fullLod2ScreenHeightFraction    = default,
                        };
                        mmi             = default;
                        maxLodSupported = 0;
                    }

                    float height = math.cmax(boundsArray[i].Value.Extents) * 2f;
                    float groupMin, groupMax;
                    if (lodGroupPercentages != null)
                    {
                        // Todo: This is the only reason we still need to use the transform instead of using the AABB center.
                        var   transform        = transforms[i].worldTransformQvvs;
                        float groupWorldHeight = math.abs(lodGroupPercentages[i].localSpaceHeight) * math.abs(transform.scale) * math.cmax(math.abs(transform.stretch));
                        float factor           = height / groupWorldHeight;
                        groupMin               = factor * lodGroupPercentages[i].minCrossFadeEdge;
                        groupMax               = factor * lodGroupPercentages[i].maxCrossFadeEdge;
                    }
                    else
                    {
                        groupMin = 0f;
                        groupMax = float.MaxValue;
                    }

                    MeshLod      meshLodDummy = default;
                    ref var      meshLod      = ref meshLodDummy;
                    MeshLodCurve meshLodCurve = default;
                    if (meshLods != null && meshLodCurves != null)
                    {
                        meshLod      = ref meshLods[i];
                        meshLodCurve = meshLodCurves[i];
                    }

                    DoEntity(ref mmi,
                             ref crossfades[i],
                             out var crossfadeEnabled,
                             out var cull,
                             select,
                             transforms[i].worldTransformQvvs,
                             height,
                             groupMin,
                             groupMax,
                             maxLodSupported,
                             ref meshLod,
                             meshLodCurve,
                             out var enableMeshLodCrossfade);

                    if (cull)
                        mask.ClearBitAtIndex(i);
                    if (lodGroupPercentages == null || !crossfadesEnabled[i])
                        crossfadesEnabled[i] = crossfadeEnabled;
                    if (enableMeshLodCrossfade)
                        enableMeshLodCrossfades[i] = true;
                    if (select2s != null || select3s != null)
                        mmis[i] = mmi;
                }
            }

            void DoEntity(ref MaterialMeshInfo mmi,
                          ref LodCrossfade crossfade,
                          out bool crossfadeEnabled,
                          out bool cull,
                          MmiRange3LodSelect select,
                          in TransformQvvs transform,
                          float height,
                          float groupMin,
                          float groupMax,
                          int maxLodSupported,
                          ref MeshLod meshLod,
                          in MeshLodCurve meshLodCurve,
                          out bool enableMeshLodCrossfade)
            {
                cull                   = false;
                enableMeshLodCrossfade = false;
                int minLod             = 0;
                int maxLod             = 2;
                if (select.fullLod1ScreenHeightMinFraction < groupMin)
                    maxLod = 0;
                else if (select.fullLod2ScreenHeightFraction < groupMin)
                    maxLod = 1;
                if (select.fullLod1ScreenHeightMaxFraction > groupMax)
                    minLod = 2;
                else if (select.fullLod0ScreenHeightFraction > groupMax)
                    minLod = 1;
                minLod     = math.max(minLod, maxResolutionLodLevel);
                maxLod     = math.max(maxLod, maxResolutionLodLevel);
                minLod     = math.min(minLod, maxLodSupported);
                maxLod     = math.min(maxLod, maxLodSupported);
                minLod     = math.min(minLod, maxLodSupported);
                groupMin   = math.min(groupMin, select.fullLod0ScreenHeightFraction);
                if (minLod == maxLod)
                {
                    crossfadeEnabled = false;
                    mmi.SetCurrentLodRegion(minLod, false);
                }
                else
                {
                    if (minLod == 1)
                    {
                        select.fullLod1ScreenHeightMaxFraction = half.MaxValueAsHalf;
                        select.fullLod0ScreenHeightFraction    = half.MaxValueAsHalf;
                    }
                    else if (maxLod == 1)
                    {
                        select.fullLod1ScreenHeightMinFraction = default;
                        select.fullLod2ScreenHeightFraction    = (half)math.min(select.fullLod2ScreenHeightFraction, 0f);
                    }

                    var biasHeight = height * cameraFactor;
                    var distance   = math.select(1f, math.distance(transform.position, cameraPosition), isPerspective);

                    var zeroHeight   = select.fullLod0ScreenHeightFraction * distance;
                    var oneMaxHeight = select.fullLod1ScreenHeightMaxFraction * distance;
                    var oneMinHeight = select.fullLod1ScreenHeightMinFraction * distance;
                    var twoHeight    = select.fullLod2ScreenHeightFraction * distance;

                    if (biasHeight >= zeroHeight)
                    {
                        crossfadeEnabled = false;
                        mmi.SetCurrentLodRegion(0, false);
                    }
                    else if (biasHeight <= twoHeight)
                    {
                        crossfadeEnabled = false;
                        mmi.SetCurrentLodRegion(2, false);
                        if (select.fullLod2ScreenHeightFraction < 0f)
                            cull = true;
                    }
                    else if ((biasHeight <= oneMaxHeight) && biasHeight >= oneMinHeight)
                    {
                        crossfadeEnabled = false;
                        mmi.SetCurrentLodRegion(1, false);
                    }
                    else if (biasHeight > oneMaxHeight)
                    {
                        crossfadeEnabled = true;
                        mmi.SetCurrentLodRegion(0, true);
                        crossfade.SetFromHiResOpacity(math.unlerp(oneMaxHeight, zeroHeight, biasHeight), false);
                    }
                    else
                    {
                        crossfadeEnabled = true;
                        mmi.SetCurrentLodRegion(1, true);
                        crossfade.SetFromHiResOpacity(math.unlerp(twoHeight, oneMinHeight, biasHeight), false);
                    }
                }

                if (meshLod.levelCount <= 0)
                    return;

                var heights           = new float3(height, groupMax, groupMin);
                var meshLodDistance   = math.select(1f, math.distance(transform.position, cameraPosition), isPerspective);
                var screenFractions   = heights * new float3(cameraFactorNoBias, cameraFactor, cameraFactor) / meshLodDistance;
                var preClamp          = math.log2(screenFractions) * meshLodCurve.slope + meshLodCurve.preClampBias;
                var postClamp         = math.max(0f, preClamp) + meshLodCurve.postClampBias;
                var rounded           = math.round(postClamp);
                var isWithinCrossfade = math.abs(postClamp - rounded) <= meshLodCrossfadeMargin;
                var groupClampRegion  = rounded + math.select(float3.zero, new float3(0f, meshLodCrossfadeMargin, -meshLodCrossfadeMargin), isWithinCrossfade);
                var meshLodLevel      = math.clamp(groupClampRegion.x, groupClampRegion.y, groupClampRegion.z);
                meshLodLevel          = math.min(meshLodLevel, meshLod.levelCount - 1.5f);  // The extra 0.5 is to prevent crossfading
                var meshLodRounded    = math.round(meshLodLevel);
                if (math.distance(meshLodLevel, meshLodRounded) < meshLodCrossfadeMargin)
                {
                    var hiResOpacity = math.unlerp(meshLodRounded - meshLodCrossfadeMargin, meshLodRounded + meshLodCrossfadeMargin, meshLodLevel);
                    meshLod.lodLevel = (ushort)meshLodLevel;
                    if (meshLodLevel > meshLodRounded)
                        meshLod.lodLevel--;
                    crossfade.SetFromHiResOpacity(hiResOpacity, false);
                    crossfadeEnabled       = true;
                    enableMeshLodCrossfade = true;
                }
                else
                {
                    crossfadeEnabled = false;
                    meshLod.lodLevel = (ushort)meshLodLevel;
                }
            }
        }
    }
}

