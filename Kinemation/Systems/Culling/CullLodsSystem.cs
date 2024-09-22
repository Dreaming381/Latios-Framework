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

// Unity's implementation of LODs in ECS and GPU Resident Drawer prefer to encode
// LODs as distances. The problem with this is that LODs are really defined as
// percentages of screen heights. And such percentages are dependent on the scale
// values of the objects. We want to derive from Unity's formulas a test that permits
// a dynamic worldSpaceSize.
// Unity defines a LODRange distance as follows:
//    lodRangeDistance = worldSpaceSize / lodGroupLodScreenHeightPercent
// Unity also defines a distanceScale which is one of two values depending on whether
// or not the camera is orthographic, and is constant per job:
//    distanceScale = 2 * orthoSize / lodBias [orthographic]
//    distanceScale = 2 * tan(fov / 2) / lodBias [perspective]
// Next, there's distance defined as:
//    distance = vectorLength(cameraPosition - lodReferencePoint)
// Lastly, we'll define the complex comparison query operation with the symbol <=>
// The queries Unity performs in the culling job are defined like this:
//    lodRangeDistance <=> distanceScale [orthographic]
//    lodRangeDistance <=> distance * distanceScale [perspective]
//
// We can perform any algebraic operation across the <=> operator, at the risk of
// changing which type of comparison we end up needing (> vs <). But we can reason
// about the correct operator via intuition when we are done. Expanding lodRangeDistance
// into its formulation, we can perform the following sequence to arrive at a desirable
// comparison for orthographic
//    worldSpaceSize / lodGroupLodScreenHeightPercent <=> distanceScale
//    worldSpaceSize <=> distanceScale * lodGroupLodScreenHeightPercent
//    worldSpaceSize / distanceScale <=> lodGroupLodScreenHeightPercent
// Now we have a compact formula that relates our dynamic value and a constant to
// bake-able values defined in the LOD Group.
// We can further optimize by using the reciprocal of the distanceScale to convert
// our division into a multiplication at runtime.
//
// For perspective, we can perform a similar sequence:
//    worldSpaceSize / lodGroupLodScreenHeightPercent <=> distance * distanceScale
//    worldSpaceSize <=> distance * distanceScale * lodGroupLodScreenHeightPercent
//    worldSpaceSize / distanceScale <=> distance * lodGroupLodScreenHeightPercent
// We could move distance to the left to get a cleaner formulation, however, as
// distance is always positive, the comparison directions won't change based on which
// side it is on, and having the multiplication on the right instead of the division
// on the left is more optimal. We can still intuitively reason about the directionality
// of comparisons.
//
// Because lodGroupLodScreenHeightPercent is now in our formulation instead of the
// full lodRangeDistance, we can do even more optimizations. This value is loosely
// authored using sliders in the editor for a limited range of [0, 1]. Half precision
// should be more than sufficient to represent these values, of which there are multiple.
// Thus, we can save precious chunk memory at the cost of converting these to higher
// precision in the job.
//
// Additionally, the common case for LODs is to have identity transforms relative to
// their LOD Group (except for maybe scale, which we can encode into the exponent
// part of our lodGroupLodScreenHeightPercent). Because of this, we make the assumption
// that the worldTransform of the LOD Entity is also the lodReferencePoint.

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    [BurstCompile]
    public unsafe partial struct CullLodsSystem : ISystem, ISystemShouldUpdate
    {
        EntityQuery m_query;
        EntityQuery m_metaQuery;

        LatiosWorldUnmanaged latiosWorld;

        WorldTransformReadOnlyAspect.TypeHandle m_worldTransformHandle;

        float3 m_previousCameraPosition;
        float  m_previousCameraHeightMultiplier;
        int    m_previousMaxResolutionLodLevel;
        bool   m_previousWasPerspective;
        int    m_maximumLODLevel;
        float  m_lodBias;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().WithAnyEnabled<LodHeightPercentages, LodHeightPercentagesWithCrossfadeMargins>(true).WithWorldTransformReadOnly()
                          .With<EntitiesGraphicsChunkInfo>(false, true).Build();
            m_metaQuery = state.Fluent().With<ChunkHeader>(true).With<EntitiesGraphicsChunkInfo>(false).With<ChunkPerCameraCullingMask>(false).Build();

            m_worldTransformHandle = new WorldTransformReadOnlyAspect.TypeHandle(ref state);

            state.RequireForUpdate(m_query);
        }

        public bool ShouldUpdateSystem(ref SystemState state)
        {
            m_maximumLODLevel = UnityEngine.QualitySettings.maximumLODLevel;
            m_lodBias         = UnityEngine.QualitySettings.lodBias;
            return true;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var context = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>();

            var   cameraPosition   = context.lodParameters.cameraPosition;
            var   isPerspective    = !context.lodParameters.isOrthographic;
            float cameraMultiplier = LodUtilities.CameraFactorFrom(in context.lodParameters, m_lodBias);

            var needsCulling  = context.cullIndexThisFrame == 0;
            needsCulling     |= cameraMultiplier != m_previousCameraHeightMultiplier;
            needsCulling     |= !cameraPosition.Equals(m_previousCameraPosition);
            needsCulling     |= m_maximumLODLevel != m_previousMaxResolutionLodLevel;
            needsCulling     |= isPerspective != m_previousWasPerspective;

            m_previousCameraHeightMultiplier = cameraMultiplier;
            m_previousCameraPosition         = cameraPosition;
            m_previousMaxResolutionLodLevel  = m_maximumLODLevel;
            m_previousWasPerspective         = isPerspective;

            if (needsCulling)
            {
                m_worldTransformHandle.Update(ref state);
                state.Dependency = new CullLodsJob
                {
                    worldTransformHandle                           = m_worldTransformHandle,
                    lodHeightPercentagesHandle                     = GetComponentTypeHandle<LodHeightPercentages>(true),
                    lodHeightPercentagesWithCrossfadeMarginsHandle = GetComponentTypeHandle<LodHeightPercentagesWithCrossfadeMargins>(true),
                    speedTreeTagHandle                             = GetComponentTypeHandle<SpeedTreeCrossfadeTag>(true),
                    chunkInfoHandle                                = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(false),
                    crossfadeHandle                                = GetComponentTypeHandle<LodCrossfade>(false),
                    cameraPosition                                 = cameraPosition,
                    cameraHeightMultiplier                         = cameraMultiplier,
                    maxResolutionLodLevel                          = m_maximumLODLevel,
                    isPerspective                                  = isPerspective,
                }.ScheduleParallel(m_query, state.Dependency);
            }

            state.Dependency = new CopyLodsToPerCameraVisisbilitiesJob
            {
                chunkInfoHandle     = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                perCameraMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false)
            }.ScheduleParallel(m_metaQuery, state.Dependency);
        }

        [BurstCompile]
        unsafe struct CullLodsJob : IJobChunk
        {
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle                       worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<LodHeightPercentages>                     lodHeightPercentagesHandle;
            [ReadOnly] public ComponentTypeHandle<LodHeightPercentagesWithCrossfadeMargins> lodHeightPercentagesWithCrossfadeMarginsHandle;
            [ReadOnly] public ComponentTypeHandle<SpeedTreeCrossfadeTag>                    speedTreeTagHandle;

            public ComponentTypeHandle<EntitiesGraphicsChunkInfo> chunkInfoHandle;
            public ComponentTypeHandle<LodCrossfade>              crossfadeHandle;

            public float3 cameraPosition;
            public float  cameraHeightMultiplier;
            public int    maxResolutionLodLevel;
            public bool   isPerspective;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var chunkInfo                         = ref chunk.GetChunkComponentRefRW(ref chunkInfoHandle);
                chunkInfo.CullingData.InstanceLodEnableds = default;

                var                               transforms          = worldTransformHandle.Resolve(chunk);
                var                               percentsWithMargins = chunk.GetNativeArray(ref lodHeightPercentagesWithCrossfadeMarginsHandle);
                NativeArray<LodHeightPercentages> percents            = default;
                NativeArray<LodCrossfade>         crossfades          = default;
                bool                              hasCrossfades       = percentsWithMargins.Length > 0;
                if (hasCrossfades)
                {
                    crossfades             = chunk.GetNativeArray(ref crossfadeHandle);
                    var  crossfadeEnableds = chunk.GetEnabledMask(ref crossfadeHandle);
                    bool isSpeedTree       = chunk.Has(ref speedTreeTagHandle);

                    chunk.SetComponentEnabledForAll(ref crossfadeHandle, false);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var lodValues = percentsWithMargins[i];
                        var transform = transforms[i].worldTransformQvvs;
                        if (!TestInRange(lodValues.localSpaceHeight, lodValues.minPercent, lodValues.maxPercent, in transform, out var computedParams))
                            continue;

                        float maxMargin = lodValues.maxCrossFadeEdge;
                        if (isPerspective)
                            maxMargin *= computedParams.distance;
                        if (!computedParams.isPromotedLod && computedParams.worldHeight > maxMargin)
                        {
                            // We are crossfading with a higher resolution LOD
                            if (isSpeedTree)
                            {
                                // For speedTree, the lower-res LOD is unused as the higher-res LOD is deformed towards the lower-res LOD surface
                                continue;
                            }

                            LodCrossfade newCrossfade = default;
                            float        opacity      = math.unlerp(maxMargin, computedParams.maxPercent, computedParams.worldHeight);
                            newCrossfade.SetFromHiResOpacity(opacity, true);
                            crossfades[i]        = newCrossfade;
                            crossfadeEnableds[i] = true;
                        }
                        else
                        {
                            float minMargin = lodValues.minCrossFadeEdge;
                            if (isPerspective)
                                minMargin *= computedParams.distance;
                            if (computedParams.worldHeight < minMargin)
                            {
                                // We are crossfading with a lower resolution LOD
                                // The formula is the same for both SpeedTree and dithered, meaning SpeedTree only uses half the snorm space
                                LodCrossfade newCrossfade = default;
                                float        opacity      = math.unlerp(computedParams.minPercent, minMargin, computedParams.worldHeight);
                                newCrossfade.SetFromHiResOpacity(opacity, false);
                                crossfades[i]        = newCrossfade;
                                crossfadeEnableds[i] = true;
                            }
                        }

                        // We have a hit.
                        if (i < 64)
                            chunkInfo.CullingData.InstanceLodEnableds.Enabled[0] |= 1ul << i;
                        else
                            chunkInfo.CullingData.InstanceLodEnableds.Enabled[0] |= 1ul << (i - 64);
                    }
                }
                else
                {
                    percents = chunk.GetNativeArray(ref lodHeightPercentagesHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var lodValues = percents[i];
                        var transform = transforms[i].worldTransformQvvs;

                        if (!TestInRange(lodValues.localSpaceHeight, lodValues.minPercent, lodValues.maxPercent, in transform, out var computedParams))
                            continue;

                        // We have a hit.
                        if (i < 64)
                            chunkInfo.CullingData.InstanceLodEnableds.Enabled[0] |= 1ul << i;
                        else
                            chunkInfo.CullingData.InstanceLodEnableds.Enabled[0] |= 1ul << (i - 64);
                    }
                }
            }

            struct ComputedRangeParams
            {
                public float worldHeight;
                public float distance;
                public float minPercent;
                public float maxPercent;
                public bool  isPromotedLod;
            }

            bool TestInRange(float localHeight, half minPercent, half maxPercent, in TransformQvvs transform, out ComputedRangeParams computedParams)
            {
                computedParams = default;

                if (maxResolutionLodLevel > 0)
                {
                    bool bit0   = localHeight < 0f;
                    bool bit1   = minPercent < 0f;
                    bool bit2   = maxPercent < 0f;
                    var  lowLod = math.bitmask(new bool4(bit0, bit1, bit2, false));
                    if (lowLod < maxResolutionLodLevel)
                        return false;
                    computedParams.isPromotedLod = lowLod == maxResolutionLodLevel;
                }
                else
                    computedParams.isPromotedLod = false;

                computedParams.worldHeight = LodUtilities.ViewHeightFrom(localHeight, transform.scale, transform.stretch, cameraHeightMultiplier);
                computedParams.minPercent  = math.abs(minPercent);
                if (isPerspective)
                {
                    computedParams.distance    = math.distance(transform.position, cameraPosition);
                    computedParams.minPercent *= computedParams.distance;
                }
                if (computedParams.worldHeight < computedParams.minPercent)
                    return false;
                computedParams.maxPercent = math.abs(maxPercent);
                if (isPerspective)
                    computedParams.maxPercent *= computedParams.distance;
                if (!computedParams.isPromotedLod && computedParams.worldHeight >= computedParams.maxPercent)
                    return false;

                return true;
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
                    var mask = maskArray[i];
                    if ((mask.lower.Value | mask.upper.Value) == 0)
                        continue;

                    var cullingData = chunkInfoArray[i].CullingData;
                    if ((cullingData.Flags & EntitiesGraphicsChunkCullingData.kFlagHasLodData) != EntitiesGraphicsChunkCullingData.kFlagHasLodData)
                        continue;

                    var lods          = cullingData.InstanceLodEnableds;
                    mask.lower.Value &= lods.Enabled[0];
                    mask.upper.Value &= lods.Enabled[1];
                    maskArray[i]      = mask;
                }
            }
        }
    }
}

