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

namespace Latios.Kinemation.Systems
{
    [DontSyncPreviousUpdatesThisFrame(32)]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct SelectMmiRangeLodsSystem : ISystem, ILatiosApi, ISystemShouldUpdate
    {
        EntityQuery m_query;

        int   m_maximumLODLevel;
        float m_lodBias;
        float m_meshLodThreshold;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var api = this.OnCreateForLatios(ref state);

            m_query = state.Fluent().With<MaterialMeshInfo, LodCrossfade>(false).With<WorldRenderBounds>(true).With<ChunkPerCameraCullingMask>(false, true)
                      .WithAnyEnabled<MmiRange2LodSelect, MmiRange3LodSelect, MeshLodCurve>(true).WithWorldTransformReadOnly().Build();

            api.worldBlackboardEntity.AddComponentDataIfMissing(new MeshLodCrossfadeMargin { margin = (half)0.05f });
        }

        public bool ShouldUpdateSystem(ref SystemState state)
        {
            m_maximumLODLevel = UnityEngine.QualitySettings.maximumLODLevel;
            m_lodBias         = UnityEngine.QualitySettings.lodBias;
#if UNITY_6000_2_OR_NEWER
            m_meshLodThreshold = UnityEngine.QualitySettings.meshLodThreshold;
#endif
            return m_maximumLODLevel < 2;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api        = this.GetApi(ref state);
            var context    = api.worldBlackboardEntity.GetComponentData<CullingContext>();
            var parameters = context.lodParameters;

            float cameraFactorNoBias = LodUtilities.CameraFactorFrom(in parameters, 1f);

            state.Dependency = new Job
            {
                meshLodCrossfadeMargin = api.worldBlackboardEntity.GetComponentData<MeshLodCrossfadeMargin>().margin,
                cameraPosition         = parameters.cameraPosition,
                isPerspective          = !parameters.isOrthographic,
                isShadowCasting        = context.viewType == UnityEngine.Rendering.BatchCullingViewType.Light,
                cameraFactor           = cameraFactorNoBias * m_lodBias,
                meshLodFactor          = m_meshLodThreshold / (cameraFactorNoBias * parameters.cameraPixelHeight),
                inverseLodBias         = 1f / m_lodBias,
                maxResolutionLodLevel  = m_maximumLODLevel
            }.Inject(api).ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        unsafe partial struct Job : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] WorldTransformReadOnlyAspect.TypeHandle                       worldTransformHandle;
            [ReadOnly, Inject] ComponentTypeHandle<WorldRenderBounds>                        boundsHandle;
            [ReadOnly, Inject] ComponentTypeHandle<MmiRange2LodSelect>                       select2Handle;
            [ReadOnly, Inject] ComponentTypeHandle<MmiRange3LodSelect>                       select3Handle;
            [ReadOnly, Inject] ComponentTypeHandle<MmiRangeLodFlags>                         rangeLodFlagsHandle;
            [ReadOnly, Inject] ComponentTypeHandle<LodHeightPercentagesWithCrossfadeMargins> lodGroupCrossfades;
            [ReadOnly, Inject] ComponentTypeHandle<LodGroupReferencePoint>                   hlodReferencePointHandle;
            [ReadOnly, Inject] ComponentTypeHandle<MeshLodCurve>                             meshLodCurveHandle;

            [Inject] ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraMaskHandle;
            [Inject] ComponentTypeHandle<MaterialMeshInfo>          mmiHandle;
            [Inject] ComponentTypeHandle<LodCrossfade>              crossfadeHandle;
            [Inject] ComponentTypeHandle<MeshLod>                   meshLodHandle;

            public float3 cameraPosition;
            public float  cameraFactor;
            public float  meshLodFactor;
            public float  inverseLodBias;
            public float  meshLodCrossfadeMargin;
            public int    maxResolutionLodLevel;
            public bool   isPerspective;
            public bool   isShadowCasting;

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
                var rangeLodFlagsArray      = isShadowCasting ? chunk.GetComponentDataPtrRO(ref rangeLodFlagsHandle) : null;
                var lodGroupPercentages     = chunk.GetComponentDataPtrRO(ref lodGroupCrossfades);
                var hlodReferencePoints     = lodGroupPercentages != null? chunk.GetComponentDataPtrRO(ref hlodReferencePointHandle) : null;
                var meshLods                = chunk.GetComponentDataPtrRW(ref meshLodHandle);
                var enableMeshLodCrossfades = chunk.GetEnabledMask(ref meshLodHandle);
                var meshLodCurves           = meshLods != null? chunk.GetComponentDataPtrRO(ref meshLodCurveHandle) : null;
                var enumerator              = new ChunkEntityBatchEnumerator(true, new v128(mask.lower.Value, mask.upper.Value), chunk.Count);
                while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                {
                    for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                    {
                        MmiRange3LodSelect select;
                        bool3              nullSelect;
                        MaterialMeshInfo   mmi;
                        int                maxLodSupported;
                        if (select3s != null)
                        {
                            select                                 = select3s[i];
                            nullSelect                             = new bool3(false, select.fullLod1ScreenHeightMaxFraction < 0f, select.fullLod2ScreenHeightFraction < 0f);
                            select.fullLod1ScreenHeightMaxFraction = (half)math.abs(select.fullLod1ScreenHeightMaxFraction);
                            select.fullLod2ScreenHeightFraction    = (half)math.abs(select.fullLod2ScreenHeightFraction);
                            mmi                                    = mmis[i];
                            maxLodSupported                        = 2;
                        }
                        else if (select2s != null)
                        {
                            select = new MmiRange3LodSelect
                            {
                                fullLod0ScreenHeightFraction    = select2s[i].fullLod0ScreenHeightFraction,
                                fullLod1ScreenHeightMaxFraction = (half)math.abs(select2s[i].fullLod1ScreenHeightFraction),
                                fullLod1ScreenHeightMinFraction = default,
                                fullLod2ScreenHeightFraction    = default,
                            };
                            nullSelect      = new bool3(false, new bool2(select2s[i].fullLod1ScreenHeightFraction < 0f));
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
                            nullSelect      = default;
                            mmi             = default;
                            maxLodSupported = 0;
                        }
                        if (rangeLodFlagsArray != null)
                        {
                            var flags     = rangeLodFlagsArray[i];
                            nullSelect.y &= !flags.disableLod1ShadowCasting;
                            nullSelect.z &= !flags.disableLod2ShadowCasting;
                        }

                        float  height = math.cmax(boundsArray[i].Value.Extents) * 2f;
                        float3 center = boundsArray[i].Value.Center;
                        float  groupMin, groupMax;
                        if (lodGroupPercentages != null)
                        {
                            // We need to convert group LOD thresholds into local LOD thresholds. The key differences is that they use different center points and relative heights.
                            var transform = transforms[i].worldTransformQvvs;
                            if (hlodReferencePoints != null)
                                transform.position = qvvs.TransformPoint(in transform, hlodReferencePoints[i].localPosition);
                            float groupWorldHeight = math.abs(lodGroupPercentages[i].localSpaceHeight) * math.abs(transform.scale) * math.cmax(math.abs(transform.stretch));
                            float factor           = height / groupWorldHeight;
                            if (isPerspective)
                                factor *= math.distance(cameraPosition, transform.position) / math.distance(cameraPosition, center);
                            groupMin    = factor * lodGroupPercentages[i].minCrossFadeEdge;
                            groupMax    = factor * lodGroupPercentages[i].maxCrossFadeEdge;
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
                                 nullSelect,
                                 center,
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
                        if (meshLods != null)
                            enableMeshLodCrossfades[i] = enableMeshLodCrossfade;
                        if (select2s != null || select3s != null)
                            mmis[i] = mmi;
                    }
                }
            }

            void DoEntity(ref MaterialMeshInfo mmi,
                          ref LodCrossfade crossfade,
                          out bool crossfadeEnabled,
                          out bool cull,
                          MmiRange3LodSelect select,
                          bool3 nullSelect,
                          float3 center,
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
                    var distance   = math.select(1f, math.distance(center, cameraPosition), isPerspective);

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
                        if (nullSelect.z)
                            cull = true;
                    }
                    else if ((biasHeight <= oneMaxHeight) && biasHeight >= oneMinHeight)
                    {
                        crossfadeEnabled = false;
                        mmi.SetCurrentLodRegion(1, false);
                        if (nullSelect.y)
                            cull = true;
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

                if (meshLod.levelCount <= 0 || cull)
                    return;

                var heights            = new float3(height, groupMax, groupMin);
                heights.yz            *= inverseLodBias;
                var meshLodDistance    = math.select(1f, math.distance(center, cameraPosition), isPerspective);
                var screenFractions    = meshLodFactor * meshLodDistance / heights;
                var preClamp           = math.log2(screenFractions) * meshLodCurve.slope + meshLodCurve.preClampBias;
                var postClamp          = math.max(0f, preClamp) + meshLodCurve.postClampBias;
                var rounded            = math.round(postClamp);
                var isWithinCrossfade  = math.abs(postClamp - rounded) <= meshLodCrossfadeMargin;
                var groupClampRegion   = rounded + math.select(float3.zero, new float3(0f, meshLodCrossfadeMargin, -meshLodCrossfadeMargin), isWithinCrossfade);
                var meshLodLevel       = math.clamp(groupClampRegion.x, groupClampRegion.y, groupClampRegion.z);
                meshLodLevel           = math.min(meshLodLevel, meshLod.levelCount - 1.5f);  // The extra 0.5 is to prevent crossfading
                var meshLodRounded     = math.round(meshLodLevel);
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

