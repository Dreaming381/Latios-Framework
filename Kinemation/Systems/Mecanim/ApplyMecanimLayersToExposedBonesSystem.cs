using Latios.Transforms;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ApplyMecanimLayersToExposedBonesSystem : ISystem
    {
        private EntityQuery                              m_query;
        private LocalTransformQvvsReadWriteAspect.Lookup m_localTransformLookup;

        private float m_previousDeltaTime;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                          .WithAllRW<MecanimController>()
                          .WithAll<MecanimControllerEnabledFlag>()
                          .WithAll<MecanimLayerStateMachineStatus>()
                          .WithAll<MecanimParameter>()
                          .WithAll<MecanimActiveClipEvent>()
                          .WithAll<BoneReference>()
                          .WithAllRW<ExposedSkeletonInertialBlendState>();
            m_query = builder.Build(ref state);
            builder.Dispose();

            m_localTransformLookup = new LocalTransformQvvsReadWriteAspect.Lookup(ref state);

            m_previousDeltaTime = 8f * math.EPSILON;
        }

        public void OnUpdate(ref SystemState state)
        {
            m_localTransformLookup.Update(ref state);

            state.Dependency = new Job
            {
                controllerHandle          = GetComponentTypeHandle<MecanimController>(false),
                parametersHandle          = GetBufferTypeHandle<MecanimParameter>(true),
                clipEventsHandle          = GetBufferTypeHandle<MecanimActiveClipEvent>(false),
                layerStatusesHandle       = GetBufferTypeHandle<MecanimLayerStateMachineStatus>(true),
                boneReferenceHandle       = GetBufferTypeHandle<BoneReference>(true),
                localTransformLookup      = m_localTransformLookup,
                inertialBlendStatesHandle = GetBufferTypeHandle<ExposedSkeletonInertialBlendState>(false),
                previousFrameClipInfoHandle = GetBufferTypeHandle<TimedMecanimClipInfo>(false),
                previousDeltaTime         = m_previousDeltaTime,
                deltaTime                 = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(m_query, state.Dependency);

            m_previousDeltaTime = SystemAPI.Time.DeltaTime;
        }

        [BurstCompile]
        public partial struct Job : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<MecanimLayerStateMachineStatus> layerStatusesHandle;
            [ReadOnly] public BufferTypeHandle<MecanimParameter>               parametersHandle;
            [ReadOnly] public BufferTypeHandle<BoneReference>                  boneReferenceHandle;
            public BufferTypeHandle<MecanimActiveClipEvent>                    clipEventsHandle;
            public BufferTypeHandle<TimedMecanimClipInfo>                      previousFrameClipInfoHandle;

            public BufferTypeHandle<ExposedSkeletonInertialBlendState> inertialBlendStatesHandle;
            public ComponentTypeHandle<MecanimController>              controllerHandle;

            [NativeDisableParallelForRestriction] public LocalTransformQvvsReadWriteAspect.Lookup localTransformLookup;

            public float deltaTime;
            public float previousDeltaTime;

            [NativeDisableContainerSafetyRestriction] NativeList<TimedMecanimClipInfo> clipWeights;
            [NativeDisableContainerSafetyRestriction] NativeList<float>                floatCache;
            [NativeDisableContainerSafetyRestriction] NativeList<TransformQvvs>        transformCache;

            [BurstCompile]
            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var controllers                = chunk.GetNativeArray(ref controllerHandle);
                var layerStatusesBuffers       = chunk.GetBufferAccessor(ref layerStatusesHandle);
                var parametersBuffers          = chunk.GetBufferAccessor(ref parametersHandle);
                var clipEventsBuffers          = chunk.GetBufferAccessor(ref clipEventsHandle);
                var boneReferencesBuffers      = chunk.GetBufferAccessor(ref boneReferenceHandle);
                var previousFrameClipInfoBuffers = chunk.GetBufferAccessor(ref previousFrameClipInfoHandle);
                var inertialBlendStatesBuffers = chunk.GetBufferAccessor(ref inertialBlendStatesHandle);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var indexInChunk))
                {
                    var     controller     = controllers[indexInChunk];
                    ref var controllerBlob = ref controller.controller.Value;
                    var     parameters     = parametersBuffers[indexInChunk].AsNativeArray();
                    var     clipEvents     = clipEventsBuffers[indexInChunk];
                    var     layerStatuses  = layerStatusesBuffers[indexInChunk].AsNativeArray();
                    var     boneReferences = boneReferencesBuffers[indexInChunk].AsNativeArray();
                    var     previousFrameClipInfo = previousFrameClipInfoBuffers[indexInChunk];

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
                        var layer = layerStatuses[i];
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
                        totalWeight += clipWeights[i].weight;
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
                        if (blendWeight < 0.0001f)
                            continue;

                        ref var state = ref controllerBlob.layers[clipWeight.layerIndex].states[clipWeight.stateIndex];

                        var time = state.isLooping ? clip.LoopToClipTime(clipWeight.motionTime) : math.min(clipWeight.motionTime, clip.duration);
                        clipSet.clips[clipWeights[i].mecanimClipIndex].SamplePose(ref blender, time, blendWeight);
                    }

                    blender.NormalizeRotations();
                    
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
                        //Get the current clip deltas
                        var currentRoot = TransformQvvs.identity;
                        for (int i = 0; i < clipWeights.Length; i++)
                        {
                            var clipWeight = clipWeights[i];
                            ref var clip = ref clipSet.clips[clipWeight.mecanimClipIndex];
                            var blendWeight = clipWeight.weight / totalWeight;
                            //Cull clips with negligible weight
                            if (blendWeight < 0.0001f)
                                continue;

                            ref var state = ref controllerBlob.layers[clipWeight.layerIndex].states[clipWeight.stateIndex];
                            var time = state.isLooping ? clip.LoopToClipTime(clipWeight.motionTime) : math.min(clipWeight.motionTime, clip.duration);
                            var stateSpeed = state.speedMultiplierParameterIndex != -1 ?
                                parameters[state.speedMultiplierParameterIndex].floatParam * state.speed :
                                state.speed;
                            var speedModifiedDeltaTime = deltaTime * stateSpeed;
                            var deltaTransform = TransformQvvs.identity;
                            var hasLooped = state.isLooping && time - deltaTime < 0f;

                            //If the clip has looped, get a sample of the end of the clip to incorporate it into the delta
                            if (hasLooped)
                            {
                                deltaTransform = clip.SampleBone(0, time);
                                var previousClipSample = clip.SampleBone(0, time - speedModifiedDeltaTime);
                                
                                var sampleEnd = math.select(clip.duration, -clip.duration, stateSpeed < 0f);
                                var endClipSample = clip.SampleBone(0, sampleEnd);
                                
                                deltaTransform.position += endClipSample.position - previousClipSample.position;
                                deltaTransform.rotation = math.mul(deltaTransform.rotation, math.mul(math.inverse(endClipSample.rotation), previousClipSample.rotation));
                            }
                            else if (time < clip.duration)
                            {
                                //Get the delta as normal
                                var currentClipSample = clip.SampleBone(0, time);
                                var previousClipSample = clip.SampleBone(0, time - speedModifiedDeltaTime);
                                
                                deltaTransform.position += currentClipSample.position - previousClipSample.position;
                                deltaTransform.rotation = math.mul(math.inverse(currentClipSample.rotation), previousClipSample.rotation);
                            }

                            currentRoot.position += deltaTransform.position * blendWeight;
                            currentRoot.rotation = math.slerp(currentRoot.rotation, deltaTransform.rotation, blendWeight);
                        }

                        //Get the previous clip deltas
                        var previousRoot = TransformQvvs.identity;
                        for (int i = 0; i < previousFrameClipInfo.Length; i++)
                        {
                            var clipWeight = previousFrameClipInfo[i]; 
                            //We can tell if the clip is playing still by comparing the timeFragment to deltaTime
                            //If the clip is no longer playing, we need to capture the fragmented delta by sampling at the motion time and at the motion time + time fragment
                            var isPlaying = clipWeight.timeFragment == deltaTime;
                            if (isPlaying)
                                continue;

                            ref var clip = ref clipSet.clips[clipWeight.mecanimClipIndex];
                            var blendWeight = clipWeight.weight / totalWeight;
                            //Cull clips with negligible weight
                            if (blendWeight < 0.0001f) 
                                continue;
                    
                            ref var state = ref controllerBlob.layers[clipWeight.layerIndex].states[clipWeight.stateIndex];
                            var time = state.isLooping ? clip.LoopToClipTime(clipWeight.motionTime) : math.min(clipWeight.motionTime, clip.duration);
                            var stateSpeed = state.speedMultiplierParameterIndex != -1 ?
                                parameters[state.speedMultiplierParameterIndex].floatParam * state.speed :
                                state.speed;
                            var speedModifiedDeltaTime = deltaTime * stateSpeed;

                            var sampleTransform = clip.SampleBone(0, time);
                    
                            //If the clip has looped, we need the previous sample to capture the delta of the clip end
                            var hasLooped = state.isLooping && time - deltaTime < 0f;
                            if (hasLooped)
                            { 
                                var endClipSample = clip.SampleBone(0, clip.duration);
                         
                                sampleTransform.position -= endClipSample.position;
                                sampleTransform.rotation = math.mul(math.inverse(sampleTransform.rotation), endClipSample.rotation);
                
                                var remainderSample = clip.SampleBone(0, clipWeight.timeFragment - (clip.duration - time));
                     
                                sampleTransform.position -= remainderSample.position;
                                sampleTransform.rotation = math.mul(math.inverse(sampleTransform.rotation), remainderSample.rotation);
                            }
                            else
                            { 
                                //need to get the sample at the time fragment
                                var timeFragmentSample = clip.SampleBone(0, time + clipWeight.timeFragment);
                             
                                sampleTransform.position -= timeFragmentSample.position;
                                sampleTransform.rotation = math.mul(math.inverse(sampleTransform.rotation), timeFragmentSample.rotation);
                            }
                    
                            previousRoot.position += sampleTransform.position * blendWeight;
                            previousRoot.rotation = math.slerp(previousRoot.rotation, sampleTransform.rotation, blendWeight);
                        }
                        
                        //write the deltas to the root transform
                        var rootBone = localTransformLookup[boneReferences[0].bone];
                        var rootDelta = TransformQvvs.identity;

                        rootDelta.position = currentRoot.position - previousRoot.position;
                        rootDelta.rotation = math.mul(math.inverse(previousRoot.rotation), currentRoot.rotation);
                        
                        rootBone.localTransform = qvvs.mul(rootBone.localTransform, rootDelta);
                    }

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

