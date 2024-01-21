using Latios.Kinemation;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Mimic.Addons.Mecanim.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ApplyMecanimLayersToOptimizedSkeletonsSystem : ISystem
    {
        private EntityQuery m_query;

        private float m_previousDeltaTime;

        private BlendShapesAspect.Lookup m_blendShapesLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                          .WithAllRW<MecanimController>()
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                          .WithAll<BlendShapeClipSet>()
#endif
                          .WithAll<MecanimLayerStateMachineStatus>()
                          .WithAllRW<TimedMecanimClipInfo>()
                          .WithAll<MecanimParameter>()
                          .WithAllRW<MecanimActiveClipEvent>()
                          .WithAspect<LocalTransformQvvsReadWriteAspect>()
                          .WithAspect<OptimizedSkeletonAspect>();
            m_query = builder.Build(ref state);
            builder.Dispose();

            m_blendShapesLookup = new BlendShapesAspect.Lookup(ref state);

            m_previousDeltaTime = 8f * math.EPSILON;
        }

        public void OnUpdate(ref SystemState state)
        {
            m_blendShapesLookup.Update(ref state);

            state.Dependency = new Job
            {
                previousDeltaTime = m_previousDeltaTime,
                deltaTime         = Time.DeltaTime,
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                blendShapesLookup = m_blendShapesLookup,
#endif
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

#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            [NativeDisableParallelForRestriction] public BlendShapesAspect.Lookup blendShapesLookup;
#endif

            [NativeDisableContainerSafetyRestriction] NativeList<TimedMecanimClipInfo> clipWeights;
            [NativeDisableContainerSafetyRestriction] NativeList<float>                floatCache;

            public void Execute(ref MecanimController controller,
                                OptimizedSkeletonAspect optimizedSkeleton,
                                in DynamicBuffer<MecanimLayerStateMachineStatus> layerStatuses,
                                in DynamicBuffer<MecanimParameter>               parametersBuffer,
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                                in DynamicBuffer<BlendShapeClipSet> blendShapeClipSetBuffer,
#endif

                                ref DynamicBuffer<MecanimActiveClipEvent> clipEvents,
                                ref DynamicBuffer<TimedMecanimClipInfo>   previousFrameClipInfo)
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

                //Root motion
                if (controller.applyRootMotion)
                {
                    //write the deltas to the root transform
                    var rootBone  = optimizedSkeleton.bones[0];
                    var rootDelta = MecanimInternalUtilities.GetRootMotionDelta(ref controllerBlob,
                                                                                ref clipSet,
                                                                                parameters,
                                                                                deltaTime,
                                                                                clipWeights,
                                                                                previousFrameClipInfo,
                                                                                totalWeight,
                                                                                CLIP_WEIGHT_CULL_THRESHOLD);

                    rootBone.localTransform = qvvs.mul(rootBone.localTransform, rootDelta);
                }

#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                //Blend shapes
                MecanimInternalUtilities.ApplyBlendShapeBlends(ref controllerBlob,
                                                               blendShapeClipSetBuffer,
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

        [BurstCompile]
        partial struct ApplyRootMotionJob : IJobEntity
        {
            public void Execute(LocalTransformQvvsReadWriteAspect localTransform, OptimizedRootDeltaROAspect root, in MecanimController controller)
            {
                if (controller.applyRootMotion)
                {
                    var newTransform               = localTransform.localTransform;
                    var rootDelta                  = root.rootDelta;
                    newTransform.position         += rootDelta.position;
                    newTransform.rotation          = math.mul(rootDelta.rotation, newTransform.rotation);
                    newTransform.scale            *= rootDelta.scale;
                    newTransform.stretch          *= rootDelta.stretch;
                    localTransform.localTransform  = newTransform;
                }
            }
        }
    }
}

