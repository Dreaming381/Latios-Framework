using Latios.Kinemation;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using static Unity.Entities.SystemAPI;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Mimic.Addons.Mecanim.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ApplyMecanimLayersToExposedBonesSystem : ISystem
    {
        private EntityQuery                              m_query;
        private LocalTransformQvvsReadWriteAspect.Lookup m_localTransformLookup;
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
        private BlendShapesAspect.Lookup m_blendShapesLookup;
#endif
        private float m_previousDeltaTime;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                          .WithAllRW<MecanimController>()
                          .WithAll<MecanimLayerStateMachineStatus>()
                          .WithAll<MecanimParameter>()
                          .WithAll<MecanimActiveClipEvent>()
                          .WithAll<BoneReference>()
                          .WithAllRW<ExposedSkeletonInertialBlendState>();
            m_query = builder.Build(ref state);
            builder.Dispose();

            m_localTransformLookup = new LocalTransformQvvsReadWriteAspect.Lookup(ref state);
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            m_blendShapesLookup = new BlendShapesAspect.Lookup(ref state);
#endif
            m_previousDeltaTime = 8f * math.EPSILON;
        }

        public void OnUpdate(ref SystemState state)
        {
            m_localTransformLookup.Update(ref state);
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            m_blendShapesLookup.Update(ref state);
#endif

            state.Dependency = new Job
            {
                controllerHandle    = GetComponentTypeHandle<MecanimController>(false),
                parametersHandle    = GetBufferTypeHandle<MecanimParameter>(true),
                clipEventsHandle    = GetBufferTypeHandle<MecanimActiveClipEvent>(false),
                layerStatusesHandle = GetBufferTypeHandle<MecanimLayerStateMachineStatus>(true),
                boneReferenceHandle = GetBufferTypeHandle<BoneReference>(true),
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                blendShapeClipSetHandle = GetBufferTypeHandle<BlendShapeClipSet>(true),
                blendShapesLookup       = m_blendShapesLookup,
#endif
                localTransformLookup        = m_localTransformLookup,
                inertialBlendStatesHandle   = GetBufferTypeHandle<ExposedSkeletonInertialBlendState>(false),
                previousFrameClipInfoHandle = GetBufferTypeHandle<TimedMecanimClipInfo>(false),
                previousDeltaTime           = m_previousDeltaTime,
                deltaTime                   = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(m_query, state.Dependency);

            m_previousDeltaTime = SystemAPI.Time.DeltaTime;
        }

        [BurstCompile]
        public partial struct Job : IJobChunk
        {
            const float CLIP_WEIGHT_CULL_THRESHOLD = 0.0001f;

            [ReadOnly] public BufferTypeHandle<MecanimLayerStateMachineStatus> layerStatusesHandle;
            [ReadOnly] public BufferTypeHandle<MecanimParameter>               parametersHandle;
            [ReadOnly] public BufferTypeHandle<BoneReference>                  boneReferenceHandle;
            public BufferTypeHandle<MecanimActiveClipEvent>                    clipEventsHandle;
            public BufferTypeHandle<TimedMecanimClipInfo>                      previousFrameClipInfoHandle;

#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            [ReadOnly] public BufferTypeHandle<BlendShapeClipSet> blendShapeClipSetHandle;
#endif

            public BufferTypeHandle<ExposedSkeletonInertialBlendState> inertialBlendStatesHandle;
            public ComponentTypeHandle<MecanimController>              controllerHandle;

            [NativeDisableParallelForRestriction] public LocalTransformQvvsReadWriteAspect.Lookup localTransformLookup;

#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            [NativeDisableParallelForRestriction] public BlendShapesAspect.Lookup blendShapesLookup;
#endif

            public float deltaTime;
            public float previousDeltaTime;

            [NativeDisableContainerSafetyRestriction] NativeList<TimedMecanimClipInfo> clipWeights;
            [NativeDisableContainerSafetyRestriction] NativeList<float>                floatCache;
            [NativeDisableContainerSafetyRestriction] NativeList<TransformQvvs>        transformCache;

            [BurstCompile]
            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var controllers                  = chunk.GetNativeArray(ref controllerHandle);
                var layerStatusesBuffers         = chunk.GetBufferAccessor(ref layerStatusesHandle);
                var parametersBuffers            = chunk.GetBufferAccessor(ref parametersHandle);
                var clipEventsBuffers            = chunk.GetBufferAccessor(ref clipEventsHandle);
                var boneReferencesBuffers        = chunk.GetBufferAccessor(ref boneReferenceHandle);
                var previousFrameClipInfoBuffers = chunk.GetBufferAccessor(ref previousFrameClipInfoHandle);
                var inertialBlendStatesBuffers   = chunk.GetBufferAccessor(ref inertialBlendStatesHandle);
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                var blendShapeClipSetBuffers = chunk.GetBufferAccessor(ref blendShapeClipSetHandle);
#endif

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var indexInChunk))
                {
                    var     controller            = controllers[indexInChunk];
                    ref var controllerBlob        = ref controller.controller.Value;
                    var     parameters            = parametersBuffers[indexInChunk].AsNativeArray();
                    var     clipEvents            = clipEventsBuffers[indexInChunk];
                    var     layerStatuses         = layerStatusesBuffers[indexInChunk].AsNativeArray();
                    var     boneReferences        = boneReferencesBuffers[indexInChunk].AsNativeArray();
                    var     previousFrameClipInfo = previousFrameClipInfoBuffers[indexInChunk];
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                    var blendShapeClipSets = blendShapeClipSetBuffers[indexInChunk];
#endif

                    if (!clipWeights.IsCreated)
                    {
                        clipWeights    = new NativeList<TimedMecanimClipInfo>(Allocator.Temp);
                        floatCache     = new NativeList<float>(Allocator.Temp);
                        transformCache = new NativeList<TransformQvvs>(Allocator.Temp);
                    }
                    else
                    {
                        clipWeights.Clear();
                    }

                    for (int i = 0; i < layerStatuses.Length; i++)
                    {
                        var     layer     = layerStatuses[i];
                        ref var layerBlob = ref controllerBlob.layers[i];
                        if (i == 0 || layerBlob.blendingMode == MecanimControllerLayerBlob.LayerBlendingMode.Override)
                        {
                            MecanimInternalUtilities.AddLayerClipWeights(ref clipWeights,
                                                                         ref layerBlob,
                                                                         (short)i,
                                                                         layer.currentStateIndex,
                                                                         (short)math.select(layer.previousStateIndex, -3, layer.transitionIsInertialBlend),
                                                                         parameters,
                                                                         layer.timeInState,
                                                                         layer.transitionEndTimeInState,
                                                                         layer.previousStateExitTime,
                                                                         1f,
                                                                         floatCache);
                        }
                        else
                        {
                            //TODO:  Compare this to base functionality with avatar masks
                            var layerWeight = layerBlob.defaultWeight;

                            MecanimInternalUtilities.AddLayerClipWeights(ref clipWeights,
                                                                         ref layerBlob,
                                                                         (short)i,
                                                                         layer.currentStateIndex,
                                                                         (short)math.select(layer.previousStateIndex, -3, layer.transitionIsInertialBlend),
                                                                         parameters,
                                                                         layer.timeInState,
                                                                         layer.transitionEndTimeInState,
                                                                         layer.previousStateExitTime,
                                                                         layerWeight,
                                                                         floatCache);
                        }
                    }

                    //Add clip events
                    ref var clipSet = ref controller.clips.Value;
                    clipEvents.Clear();
                    MecanimInternalUtilities.AddClipEvents(clipWeights, previousFrameClipInfo, ref clipSet, ref clipEvents, deltaTime);

                    //Grab total weight for normalization
                    var totalWeight = 0f;
                    for (int i = 0; i < clipWeights.Length; i++)
                    {
                        var clipWeight = clipWeights[i].weight;
                        if (clipWeight < CLIP_WEIGHT_CULL_THRESHOLD)
                            continue;
                        totalWeight += clipWeight;
                    }

                    //Initialize Qvvs transforms
                    transformCache.ResizeUninitialized(boneReferences.Length);
                    NativeArray<TransformQvvs> transforms = transformCache.AsArray();
                    var                        blender    = new BufferPoseBlender(transforms);

                    for (int i = 0; i < clipWeights.Length; i++)
                    {
                        ref var clip        = ref clipSet.clips[clipWeights[i].mecanimClipIndex];
                        var     clipWeight  = clipWeights[i];
                        var     blendWeight = clipWeight.weight / totalWeight;
                        //Cull clips with negligible weight
                        if (blendWeight < CLIP_WEIGHT_CULL_THRESHOLD)
                            continue;

                        ref var state = ref controllerBlob.layers[clipWeight.layerIndex].states[clipWeight.stateIndex];

                        var time = state.isLooping ? clip.LoopToClipTime(clipWeight.motionTime) : math.min(clipWeight.motionTime, clip.duration);
                        clipSet.clips[clipWeights[i].mecanimClipIndex].SamplePose(ref blender, time, blendWeight);
                    }

                    blender.Normalize();

                    // Begin write-back with inertial blending
                    bool startInertialBlend = controller.triggerStartInertialBlend;
                    if (controller.triggerStartInertialBlend)
                    {
                        controller.newInertialBlendDuration        += deltaTime;
                        controller.timeSinceLastInertialBlendStart  = 0f;
                        controller.isInInertialBlend                = true;
                        controller.triggerStartInertialBlend        = false;
                    }
                    if (controller.isInInertialBlend)
                    {
                        controller.timeSinceLastInertialBlendStart += deltaTime;
                        if (controller.timeSinceLastInertialBlendStart > controller.newInertialBlendDuration)
                            controller.isInInertialBlend = false;
                    }

                    var  inertialStatesBuffer = inertialBlendStatesBuffers[indexInChunk];
                    bool firstTime            = inertialStatesBuffer.Length != transforms.Length;
                    if (firstTime)
                    {
                        inertialStatesBuffer.Clear();
                        inertialStatesBuffer.ResizeUninitialized(transforms.Length);
                    }

                    var   inertialStates = (ExposedSkeletonInertialBlendState*)inertialBlendStatesBuffers[indexInChunk].GetUnsafePtr();
                    var   inertialTime   = new InertialBlendingTimingData(controller.timeSinceLastInertialBlendStart);
                    float rcpPreviousDt  = math.rcp(previousDeltaTime);
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        if (i == 0)
                            continue;

                        var localTransform = localTransformLookup[boneReferences[i].bone];
                        var localValue     = localTransform.localTransform;
                        var worldIndex     = localValue.worldIndex;

                        if (Hint.Unlikely(firstTime))
                        {
                            inertialStates[i].previous = localValue;
                            inertialStates[i].twoAgo   = inertialStates[i].previous;
                        }
                        else
                        {
                            inertialStates[i].twoAgo   = inertialStates[i].previous;
                            inertialStates[i].previous = localValue;
                        }

                        localValue = transforms[i];

                        if (Hint.Unlikely(startInertialBlend))
                            inertialStates[i].blendState.StartNewBlend(in localValue,
                                                                       in inertialStates[i].previous,
                                                                       in inertialStates[i].twoAgo,
                                                                       rcpPreviousDt,
                                                                       controller.newInertialBlendDuration);

                        if (controller.isInInertialBlend)
                            inertialStates[i].blendState.Blend(ref localValue, in inertialTime);

                        localValue.worldIndex = worldIndex;

                        localTransform.localTransform = localValue;
                    }

                    //Root motion
                    if (controller.applyRootMotion)
                    {
                        //write the deltas to the root transform
                        var rootBone  = localTransformLookup[boneReferences[0].bone];
                        var rootDelta = MecanimInternalUtilities.GetRootMotionDelta(ref controllerBlob,
                                                                                    ref clipSet,
                                                                                    parameters,
                                                                                    deltaTime,
                                                                                    clipWeights,
                                                                                    previousFrameClipInfo,
                                                                                    totalWeight,
                                                                                    CLIP_WEIGHT_CULL_THRESHOLD);

                        var newTransform         = rootBone.localTransform;
                        newTransform.position   += rootDelta.position;
                        newTransform.rotation    = math.mul(rootDelta.rotation, newTransform.rotation);
                        newTransform.scale      *= rootDelta.scale;
                        newTransform.stretch    *= rootDelta.stretch;
                        rootBone.localTransform  = newTransform;
                    }

#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                    //Blend shapes
                    MecanimInternalUtilities.ApplyBlendShapeBlends(ref controllerBlob,
                                                                   blendShapeClipSets,
                                                                   ref blendShapesLookup,
                                                                   clipWeights,
                                                                   totalWeight,
                                                                   CLIP_WEIGHT_CULL_THRESHOLD);
#endif

                    //Store previous frame clip info
                    previousFrameClipInfo.Clear();
                    for (int i = 0; i < clipWeights.Length; i++)
                    {
                        previousFrameClipInfo.Add(clipWeights[i]);
                    }
                }
            }
        }
    }
}

