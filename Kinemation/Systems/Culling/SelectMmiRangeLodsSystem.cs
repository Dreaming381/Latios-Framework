using Latios;
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

namespace Latios.Kinemation
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

            m_query = state.Fluent().With<MaterialMeshInfo, LodCrossfade>(false).With<UseMmiRangeLodTag>(true)
                      .WithAnyEnabled<MmiRange2LodSelect, MmiRange3LodSelect>(true).WithWorldTransformReadOnly().Build();
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

            state.Dependency = new Job
            {
                perCameraMaskHandle   = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                worldTransformHandle  = m_worldTransformHandle,
                select2Handle         = GetComponentTypeHandle<MmiRange2LodSelect>(true),
                select3Handle         = GetComponentTypeHandle<MmiRange3LodSelect>(true),
                mmiHandle             = GetComponentTypeHandle<MaterialMeshInfo>(false),
                crossfadeHandle       = GetComponentTypeHandle<LodCrossfade>(false),
                cameraPosition        = parameters.cameraPosition,
                isPerspective         = !parameters.isOrthographic,
                cameraFactor          = LodUtilities.CameraFactorFrom(in parameters, m_lodBias),
                maxResolutionLodLevel = m_maximumLODLevel
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<MmiRange2LodSelect> select2Handle;
            [ReadOnly] public ComponentTypeHandle<MmiRange3LodSelect> select3Handle;

            public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraMaskHandle;
            public ComponentTypeHandle<MaterialMeshInfo>          mmiHandle;
            public ComponentTypeHandle<LodCrossfade>              crossfadeHandle;

            public float3 cameraPosition;
            public float  cameraFactor;
            public int    maxResolutionLodLevel;
            public bool   isPerspective;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var mask = ref chunk.GetChunkComponentRefRW(ref perCameraMaskHandle);
                if ((mask.upper.Value | mask.lower.Value) == 0)
                    return;

                var transforms        = worldTransformHandle.Resolve(chunk);
                var mmis              = chunk.GetNativeArray(ref mmiHandle);
                var crossfades        = chunk.GetNativeArray(ref crossfadeHandle);
                var crossfadesEnabled = chunk.GetEnabledMask(ref crossfadeHandle);
                var select2s          = chunk.GetNativeArray(ref select2Handle);
                var enumerator        = new ChunkEntityEnumerator(true, new v128(mask.lower.Value, mask.upper.Value), chunk.Count);
                if (select2s.Length > 0)
                {
                    if (maxResolutionLodLevel >= 1)
                    {
                        while (enumerator.NextEntityIndex(out var i))
                        {
                            crossfadesEnabled[i] = false;
                            var mmi              = mmis[i];
                            mmi.SetCurrentLodRegion(1, false);
                            mmis[i] = mmi;
                            if (select2s[i].height < 0f)
                            {
                                mask.ClearBitAtIndex(i);
                            }
                        }
                        return;
                    }

                    if (isPerspective)
                    {
                        while (enumerator.NextEntityIndex(out var i))
                        {
                            var select    = select2s[i];
                            var transform = transforms[i].worldTransformQvvs;
                            var height    = LodUtilities.ViewHeightFrom(select.height, transform.scale, transform.stretch, cameraFactor);
                            var distance  = math.distance(transform.position, cameraPosition);
                            var mmi       = mmis[i];

                            var zeroHeight = select.fullLod0ScreenHeightFraction * distance;
                            var oneHeight  = select.fullLod1ScreenHeightFraction * distance;

                            if (height >= zeroHeight)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(0, false);
                            }
                            else if (height <= oneHeight)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(1, false);
                                if (select.height < 0f)
                                    mask.ClearBitAtIndex(i);
                                //if (transform.scale > 0.9f)
                                //    UnityEngine.Debug.Log(
                                //        $"selectHeight: {select.height}, height: {height}, oneHeight: {oneHeight}, distance: {distance}, fraction: {(float)select.fullLod1ScreenHeightFraction}");
                            }
                            else
                            {
                                crossfadesEnabled[i] = true;
                                mmi.SetCurrentLodRegion(0, true);
                                LodCrossfade fade = default;
                                fade.SetFromHiResOpacity(math.unlerp(oneHeight, zeroHeight, height), false);
                                crossfades[i] = fade;
                            }

                            mmis[i] = mmi;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var select    = select2s[i];
                            var transform = transforms[i].worldTransformQvvs;
                            var height    = LodUtilities.ViewHeightFrom(select.height, transform.scale, transform.stretch, cameraFactor);
                            var mmi       = mmis[i];

                            if (height >= select.fullLod0ScreenHeightFraction)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(0, false);
                            }
                            else if (height <= select.fullLod1ScreenHeightFraction)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(1, false);
                                mask.ClearBitAtIndex(i);
                            }
                            else
                            {
                                crossfadesEnabled[i] = true;
                                mmi.SetCurrentLodRegion(0, true);
                                LodCrossfade fade = default;
                                fade.SetFromHiResOpacity(math.unlerp(select.fullLod1ScreenHeightFraction, select.fullLod0ScreenHeightFraction, height), false);
                                crossfades[i] = fade;
                            }

                            mmis[i] = mmi;
                        }
                    }
                }
                else
                {
                    var select3s = chunk.GetNativeArray(ref select3Handle);
                    if (maxResolutionLodLevel >= 2)
                    {
                        while (enumerator.NextEntityIndex(out var i))
                        {
                            crossfadesEnabled[i] = false;
                            var mmi              = mmis[i];
                            mmi.SetCurrentLodRegion(2, false);
                            mmis[i] = mmi;
                            if (select2s[i].height < 0f)
                            {
                                mask.ClearBitAtIndex(i);
                            }
                        }
                        return;
                    }

                    if (isPerspective)
                    {
                        while (enumerator.NextEntityIndex(out var i))
                        {
                            var select    = select3s[i];
                            var transform = transforms[i].worldTransformQvvs;
                            var height    = LodUtilities.ViewHeightFrom(select.height, transform.scale, transform.stretch, cameraFactor);
                            var distance  = math.distance(transform.position, cameraPosition);
                            var mmi       = mmis[i];

                            var zeroHeight   = select.fullLod0ScreenHeightFraction * distance;
                            var oneMaxHeight = select.fullLod1ScreenHeightMaxFraction * distance;
                            var oneMinHeight = select.fullLod1ScreenHeightMinFraction * distance;
                            var twoHeight    = select.fullLod2ScreenHeightFraction * distance;

                            if (height >= zeroHeight)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(maxResolutionLodLevel, false);
                            }
                            else if (height <= twoHeight)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(2, false);
                                if (select.height < 0f)
                                    mask.ClearBitAtIndex(i);
                                //if (transform.scale > 0.9f)
                                //    UnityEngine.Debug.Log(
                                //        $"selectHeight: {select.height}, height: {height}, oneHeight: {oneHeight}, distance: {distance}, fraction: {(float)select.fullLod1ScreenHeightFraction}");
                            }
                            else if ((height <= oneMaxHeight || maxResolutionLodLevel > 0) && height >= oneMinHeight)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(1, false);
                            }
                            else if (height > oneMaxHeight)
                            {
                                crossfadesEnabled[i] = true;
                                mmi.SetCurrentLodRegion(0, true);
                                LodCrossfade fade = default;
                                fade.SetFromHiResOpacity(math.unlerp(oneMaxHeight, zeroHeight, height), false);
                                crossfades[i] = fade;
                            }
                            else
                            {
                                crossfadesEnabled[i] = true;
                                mmi.SetCurrentLodRegion(1, true);
                                LodCrossfade fade = default;
                                fade.SetFromHiResOpacity(math.unlerp(twoHeight, oneMinHeight, height), false);
                                crossfades[i] = fade;
                            }

                            mmis[i] = mmi;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var select    = select3s[i];
                            var transform = transforms[i].worldTransformQvvs;
                            var height    = LodUtilities.ViewHeightFrom(select.height, transform.scale, transform.stretch, cameraFactor);
                            var mmi       = mmis[i];

                            if (height >= select.fullLod0ScreenHeightFraction)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(maxResolutionLodLevel, false);
                            }
                            else if (height <= select.fullLod2ScreenHeightFraction)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(2, false);
                                if (select.height < 0f)
                                    mask.ClearBitAtIndex(i);
                            }
                            else if ((height <= select.fullLod1ScreenHeightMaxFraction || maxResolutionLodLevel > 0) && height >= select.fullLod1ScreenHeightMinFraction)
                            {
                                crossfadesEnabled[i] = false;
                                mmi.SetCurrentLodRegion(1, false);
                            }
                            else if (height > select.fullLod1ScreenHeightMaxFraction)
                            {
                                crossfadesEnabled[i] = true;
                                mmi.SetCurrentLodRegion(0, true);
                                LodCrossfade fade = default;
                                fade.SetFromHiResOpacity(math.unlerp(select.fullLod1ScreenHeightMaxFraction, select.fullLod0ScreenHeightFraction, height), false);
                                crossfades[i] = fade;
                            }
                            else
                            {
                                crossfadesEnabled[i] = true;
                                mmi.SetCurrentLodRegion(1, true);
                                LodCrossfade fade = default;
                                fade.SetFromHiResOpacity(math.unlerp(select.fullLod2ScreenHeightFraction, select.fullLod1ScreenHeightMinFraction, height), false);
                                crossfades[i] = fade;
                            }

                            mmis[i] = mmi;
                        }
                    }
                }
            }
        }
    }
}

