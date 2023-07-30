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
                layerStatusesHandle       = GetBufferTypeHandle<MecanimLayerStateMachineStatus>(true),
                boneReferenceHandle       = GetBufferTypeHandle<BoneReference>(true),
                localTransformLookup      = m_localTransformLookup,
                inertialBlendStatesHandle = GetBufferTypeHandle<ExposedSkeletonInertialBlendState>(false),
                previousDeltaTime         = m_previousDeltaTime,
                deltaTime                 = Time.DeltaTime
            }.ScheduleParallel(m_query, state.Dependency);

            m_previousDeltaTime = Time.DeltaTime;
        }

        [BurstCompile]
        public partial struct Job : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<MecanimLayerStateMachineStatus> layerStatusesHandle;
            [ReadOnly] public BufferTypeHandle<MecanimParameter>               parametersHandle;
            [ReadOnly] public BufferTypeHandle<BoneReference>                  boneReferenceHandle;

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
                var boneReferencesBuffers      = chunk.GetBufferAccessor(ref boneReferenceHandle);
                var inertialBlendStatesBuffers = chunk.GetBufferAccessor(ref inertialBlendStatesHandle);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var indexInChunk))
                {
                    var     controller     = controllers[indexInChunk];
                    ref var controllerBlob = ref controller.controller.Value;
                    var     parameters     = parametersBuffers[indexInChunk].AsNativeArray();
                    var     layerStatuses  = layerStatusesBuffers[indexInChunk].AsNativeArray();
                    var     boneReferences = boneReferencesBuffers[indexInChunk].AsNativeArray();

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
                        if (i == 0 || layerBlob.blendingMode ==
                            MecanimControllerLayerBlob.LayerBlendingMode.Override)
                        {
                            MecanimInternalUtilities.AddLayerClipWeights(ref clipWeights, ref layerBlob,
                                                                         layer.currentStateIndex,
                                                                         math.select(layer.previousStateIndex, -3, layer.transitionIsInertialBlend),
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

                            MecanimInternalUtilities.AddLayerClipWeights(ref clipWeights, ref layerBlob,
                                                                         layer.currentStateIndex,
                                                                         math.select(layer.previousStateIndex, -3, layer.transitionIsInertialBlend),
                                                                         parameters,
                                                                         layer.timeInState,
                                                                         layer.transitionEndTimeInState,
                                                                         layer.previousStateExitTime,
                                                                         layerWeight,
                                                                         floatCache);
                        }
                    }

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

                    ref var clipSet = ref controller.clips.Value;
                    for (int i = 0; i < clipWeights.Length; i++)
                    {
                        ref var clip        = ref clipSet.clips[clipWeights[i].mecanimClipIndex];
                        var     clipWeight  = clipWeights[i];
                        var     blendWeight = clipWeight.weight / totalWeight;
                        //Cull clips with negligible weight
                        if (blendWeight < 0.0001f)
                            continue;

                        var time = clip.LoopToClipTime(clipWeight.motionTime);
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

                        if (i > 0)
                            localTransform.localTransform = localValue;
                    }
                }
            }
        }
    }
}

