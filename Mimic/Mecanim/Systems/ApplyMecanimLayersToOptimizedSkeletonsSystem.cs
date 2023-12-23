using Latios.Kinemation;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Mimic.Mecanim.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ApplyMecanimLayersToOptimizedSkeletonsSystem : ISystem
    {
        private EntityQuery m_query;

        private float m_previousDeltaTime;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                          .WithAllRW<MecanimController>()
                          .WithAll<MecanimLayerStateMachineStatus>()
                          .WithAll<MecanimLayerStateMachineStatus>()
                          .WithAllRW<TimedMecanimClipInfo>()
                          .WithAll<MecanimParameter>()
                          .WithAllRW<MecanimActiveClipEvent>()
                          .WithAspect<LocalTransformQvvsReadWriteAspect>()
                          .WithAspect<OptimizedSkeletonAspect>();
            m_query = builder.Build(ref state);
            builder.Dispose();

            m_previousDeltaTime = 8f * math.EPSILON;
        }

        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                previousDeltaTime = m_previousDeltaTime,
                deltaTime         = Time.DeltaTime
            }.ScheduleParallel(m_query, state.Dependency);
            state.Dependency = new ApplyRootMotionJob().ScheduleParallel(m_query, state.Dependency);

            m_previousDeltaTime = Time.DeltaTime;
        }

        [BurstCompile]
        partial struct Job : IJobEntity
        {
            const float CLIP_WEIGHT_CULL_THRESHOLD = 0.0001f;

            public float deltaTime;
            public float previousDeltaTime;

            [NativeDisableContainerSafetyRestriction] NativeList<TimedMecanimClipInfo> clipWeights;
            [NativeDisableContainerSafetyRestriction] NativeList<float>                floatCache;

            public void Execute(ref MecanimController controller,
                                OptimizedSkeletonAspect optimizedSkeleton,
                                in DynamicBuffer<MecanimLayerStateMachineStatus> layerStatuses,
                                in DynamicBuffer<MecanimParameter>               parametersBuffer,
                                ref DynamicBuffer<MecanimActiveClipEvent>        clipEvents,
                                ref DynamicBuffer<TimedMecanimClipInfo>          previousFrameClipInfo)
            {
                ref var controllerBlob = ref controller.controller.Value;
                var     parameters     = parametersBuffer.AsNativeArray();
                if (!clipWeights.IsCreated)
                {
                    clipWeights = new NativeList<TimedMecanimClipInfo>(Allocator.Temp);
                    floatCache  = new NativeList<float>(Allocator.Temp);
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

                for (int i = 0; i < clipWeights.Length; i++)
                {
                    ref var clip        = ref clipSet.clips[clipWeights[i].mecanimClipIndex];
                    var     clipWeight  = clipWeights[i];
                    var     blendWeight = clipWeight.weight / totalWeight;

                    //Cull clips with negligible weight
                    if (blendWeight < CLIP_WEIGHT_CULL_THRESHOLD)
                        continue;

                    ref var state = ref controllerBlob.layers[clipWeight.layerIndex].states[clipWeight.stateIndex];
                    var     time  = state.isLooping ? clip.LoopToClipTime(clipWeight.motionTime) : math.min(clipWeight.motionTime, clip.duration);
                    clipSet.clips[clipWeights[i].mecanimClipIndex].SamplePose(ref optimizedSkeleton, time, blendWeight);
                }

                if (controller.triggerStartInertialBlend)
                {
                    optimizedSkeleton.SyncHistory();
                    controller.newInertialBlendDuration += deltaTime;
                    optimizedSkeleton.StartNewInertialBlend(previousDeltaTime, controller.newInertialBlendDuration);
                    controller.timeSinceLastInertialBlendStart = 0f;
                    controller.isInInertialBlend               = true;
                    controller.triggerStartInertialBlend       = false;
                }
                if (controller.isInInertialBlend)
                {
                    controller.timeSinceLastInertialBlendStart += deltaTime;
                    if (controller.timeSinceLastInertialBlendStart > controller.newInertialBlendDuration)
                    {
                        controller.isInInertialBlend = false;
                    }
                    else
                    {
                        optimizedSkeleton.InertialBlend(controller.timeSinceLastInertialBlendStart);
                    }
                }
                optimizedSkeleton.EndSamplingAndSync();

                if (controller.applyRootMotion)
                {
                    //Get the current clip deltas
                    var currentRoot = TransformQvvs.identity;
                    for (int i = 0; i < clipWeights.Length; i++)
                    {
                        var     clipWeight  = clipWeights[i];
                        ref var clip        = ref clipSet.clips[clipWeight.mecanimClipIndex];
                        var     blendWeight = clipWeight.weight / totalWeight;
                        //Cull clips with negligible weight
                        if (blendWeight < CLIP_WEIGHT_CULL_THRESHOLD)
                            continue;

                        ref var state      = ref controllerBlob.layers[clipWeight.layerIndex].states[clipWeight.stateIndex];
                        var     time       = state.isLooping ? clip.LoopToClipTime(clipWeight.motionTime) : math.min(clipWeight.motionTime, clip.duration);
                        var     stateSpeed = state.speedMultiplierParameterIndex != -1 ?
                                         parameters[state.speedMultiplierParameterIndex].floatParam * state.speed :
                                         state.speed;
                        var speedModifiedDeltaTime = deltaTime * stateSpeed;
                        var deltaTransform         = TransformQvvs.identity;
                        var hasLooped              = state.isLooping && time - deltaTime < 0f;

                        //If the clip has looped, get a sample of the end of the clip to incorporate it into the delta
                        if (hasLooped)
                        {
                            deltaTransform         = clip.SampleBone(0, time);
                            var previousClipSample = clip.SampleBone(0, time - speedModifiedDeltaTime);

                            var sampleEnd     = math.select(clip.duration, -clip.duration, stateSpeed < 0f);
                            var endClipSample = clip.SampleBone(0, sampleEnd);

                            deltaTransform.position += endClipSample.position - previousClipSample.position;
                            deltaTransform.rotation  = math.mul(deltaTransform.rotation, math.mul(math.inverse(endClipSample.rotation), previousClipSample.rotation));
                        }
                        else if (time < clip.duration)
                        {
                            //Get the delta as normal
                            var currentClipSample  = clip.SampleBone(0, time);
                            var previousClipSample = clip.SampleBone(0, time - speedModifiedDeltaTime);

                            deltaTransform.position += currentClipSample.position - previousClipSample.position;
                            deltaTransform.rotation  = math.mul(currentClipSample.rotation, math.inverse(previousClipSample.rotation));
                        }

                        currentRoot.position += deltaTransform.position * blendWeight;
                        currentRoot.rotation  = math.slerp(currentRoot.rotation, math.mul(currentRoot.rotation, deltaTransform.rotation), blendWeight);
                    }

                    //Get the previous clip deltas
                    var previousFrameTotalWeight = 0f;
                    for (int i = 0; i < previousFrameClipInfo.Length; i++)
                    {
                        var clipWeight = previousFrameClipInfo[i].weight;
                        if (clipWeight < CLIP_WEIGHT_CULL_THRESHOLD)
                            continue;
                        previousFrameTotalWeight += clipWeight;
                    }
                    var previousRoot = TransformQvvs.identity;
                    for (int i = 0; i < previousFrameClipInfo.Length; i++)
                    {
                        var clipWeight = previousFrameClipInfo[i];
                        //We can tell if the clip is playing still by comparing the timeFragment to deltaTime
                        //If the clip is no longer playing, we need to capture the fragmented delta by sampling at the motion time and at the motion time + time fragment
                        var isPlaying = clipWeight.timeFragment == deltaTime;
                        if (isPlaying)
                            continue;

                        ref var clip        = ref clipSet.clips[clipWeight.mecanimClipIndex];
                        var     blendWeight = clipWeight.weight / previousFrameTotalWeight;
                        //Cull clips with negligible weight
                        if (blendWeight < CLIP_WEIGHT_CULL_THRESHOLD)
                            continue;

                        ref var state = ref controllerBlob.layers[clipWeight.layerIndex].states[clipWeight.stateIndex];
                        var     time  = state.isLooping ? clip.LoopToClipTime(clipWeight.motionTime) : math.min(clipWeight.motionTime, clip.duration);

                        var sampleTransform = clip.SampleBone(0, time);

                        //If the clip has looped, we need the previous sample to capture the delta of the clip end
                        var hasLooped = state.isLooping && time - deltaTime < 0f;
                        if (hasLooped)
                        {
                            var endClipSample = clip.SampleBone(0, clip.duration);

                            sampleTransform.position -= endClipSample.position;
                            sampleTransform.rotation  = math.mul(math.inverse(sampleTransform.rotation), endClipSample.rotation);

                            var remainderSample = clip.SampleBone(0, clipWeight.timeFragment - (clip.duration - time));

                            sampleTransform.position -= remainderSample.position;
                            sampleTransform.rotation  = math.mul(remainderSample.rotation, math.inverse(sampleTransform.rotation));
                        }
                        else
                        {
                            //need to get the sample at the time fragment
                            var timeFragmentSample = clip.SampleBone(0, time + clipWeight.timeFragment);

                            sampleTransform.position -= timeFragmentSample.position;
                            sampleTransform.rotation  = math.mul(timeFragmentSample.rotation, math.inverse(sampleTransform.rotation));
                        }

                        previousRoot.position += sampleTransform.position * blendWeight;
                        previousRoot.rotation  = math.slerp(previousRoot.rotation, sampleTransform.rotation, blendWeight);
                    }

                    //write the deltas to the root transform
                    var rootDelta = TransformQvvs.identity;

                    rootDelta.position = currentRoot.position - previousRoot.position;
                    rootDelta.rotation = math.mul(math.inverse(previousRoot.rotation), currentRoot.rotation);

                    var rootBone            = optimizedSkeleton.bones[0];
                    rootBone.localTransform = rootDelta;
                }

                //Store previous frame clip info
                previousFrameClipInfo.Clear();
                for (int i = 0; i < clipWeights.Length; i++)
                {
                    previousFrameClipInfo.Add(clipWeights[i]);
                }
            }
        }

        [BurstCompile]
        partial struct ApplyRootMotionJob : IJobEntity
        {
            public void Execute(LocalTransformQvvsReadWriteAspect localTransform, OptimizedRootDeltaROAspect root, in MecanimController controller)
            {
                if (controller.applyRootMotion)
                    localTransform.localTransform = qvvs.mul(localTransform.localTransform, root.rootDelta);
            }
        }
    }
}

