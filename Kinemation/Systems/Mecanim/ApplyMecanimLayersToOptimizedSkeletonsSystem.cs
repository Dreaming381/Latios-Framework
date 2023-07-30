using Unity.Burst;
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
    public partial struct ApplyMecanimLayersToOptimizedSkeletonsSystem : ISystem
    {
        private EntityQuery                        m_query;
        private OptimizedSkeletonAspect.TypeHandle m_optimizedSkeletonHandle;

        private float m_previousDeltaTime;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                          .WithAllRW<MecanimController>()
                          .WithAll<MecanimControllerEnabledFlag>()
                          .WithAll<MecanimLayerStateMachineStatus>()
                          .WithAll<MecanimParameter>()
                          .WithAspect<OptimizedSkeletonAspect>();
            m_query = builder.Build(ref state);
            builder.Dispose();

            m_optimizedSkeletonHandle = new OptimizedSkeletonAspect.TypeHandle(ref state);

            m_previousDeltaTime = 8f * math.EPSILON;
        }

        public void OnUpdate(ref SystemState state)
        {
            m_optimizedSkeletonHandle.Update(ref state);

            state.Dependency = new Job
            {
                controllerHandle        = GetComponentTypeHandle<MecanimController>(false),
                parametersHandle        = GetBufferTypeHandle<MecanimParameter>(true),
                layerStatusesHandle     = GetBufferTypeHandle<MecanimLayerStateMachineStatus>(true),
                optimizedSkeletonHandle = m_optimizedSkeletonHandle,
                previousDeltaTime       = m_previousDeltaTime,
                deltaTime               = Time.DeltaTime
            }.ScheduleParallel(m_query, state.Dependency);

            m_previousDeltaTime = Time.DeltaTime;
        }

        [BurstCompile]
        public partial struct Job : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<MecanimLayerStateMachineStatus> layerStatusesHandle;
            [ReadOnly] public BufferTypeHandle<MecanimParameter>               parametersHandle;
            public ComponentTypeHandle<MecanimController>                      controllerHandle;
            public OptimizedSkeletonAspect.TypeHandle                          optimizedSkeletonHandle;

            public float deltaTime;
            public float previousDeltaTime;

            [NativeDisableContainerSafetyRestriction] NativeList<TimedMecanimClipInfo> clipWeights;
            [NativeDisableContainerSafetyRestriction] NativeList<float>                floatCache;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                                in v128 chunkEnabledMask)
            {
                var controllers        = chunk.GetNativeArray(ref controllerHandle);
                var optimizedSkeletons = optimizedSkeletonHandle.Resolve(chunk);
                var layersBuffers      = chunk.GetBufferAccessor(ref layerStatusesHandle);
                var parametersBuffers  = chunk.GetBufferAccessor(ref parametersHandle);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var indexInChunk))
                {
                    var     controller        = controllers[indexInChunk];
                    ref var controllerBlob    = ref controller.controller.Value;
                    var     optimizedSkeleton = optimizedSkeletons[indexInChunk];
                    var     parameters        = parametersBuffers[indexInChunk].AsNativeArray();
                    var     layers            = layersBuffers[indexInChunk].AsNativeArray();

                    if (!clipWeights.IsCreated)
                    {
                        clipWeights = new NativeList<TimedMecanimClipInfo>(Allocator.Temp);
                        floatCache  = new NativeList<float>(Allocator.Temp);
                    }
                    else
                    {
                        clipWeights.Clear();
                    }

                    for (int i = 0; i < layers.Length; i++)
                    {
                        var     layer     = layers[i];
                        ref var layerBlob = ref controllerBlob.layers[i];
                        if (i == 0 || layerBlob.blendingMode == MecanimControllerLayerBlob.LayerBlendingMode.Override)
                        {
                            MecanimInternalUtilities.AddLayerClipWeights(ref clipWeights,
                                                                         ref layerBlob,
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

                            MecanimInternalUtilities.AddLayerClipWeights(ref clipWeights,
                                                                         ref layerBlob,
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
                    controllers[indexInChunk] = controller;
                }
            }
        }
    }
}

