using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using AnimatorControllerParameterType = UnityEngine.AnimatorControllerParameterType;

using static Unity.Entities.SystemAPI;

namespace Latios.Mimic.Addons.Mecanim.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct MecanimStateMachineUpdateSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent()
                      .With<MecanimController>(false)
                      .With<MecanimLayerStateMachineStatus>(false)
                      .With<MecanimParameter>(false)
                      .With<TimedMecanimClipInfo>(false).Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                deltaTime = Time.DeltaTime,
                controllerHandle = GetComponentTypeHandle<MecanimController>(false),
                parameterHandle = GetBufferTypeHandle<MecanimParameter>(false),
                layerHandle = GetBufferTypeHandle<MecanimLayerStateMachineStatus>(false),
                previousFrameClipInfoHandle = GetBufferTypeHandle<TimedMecanimClipInfo>(false)
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        public partial struct Job : IJobChunk
        {
            public float deltaTime;
            [ReadOnly]
            public ComponentTypeHandle<MecanimController> controllerHandle;
            public BufferTypeHandle<MecanimLayerStateMachineStatus> layerHandle;
            public BufferTypeHandle<MecanimParameter> parameterHandle;
            public BufferTypeHandle<TimedMecanimClipInfo> previousFrameClipInfoHandle;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var controllers = chunk.GetNativeArray(ref controllerHandle);
                var layersBuffers = chunk.GetBufferAccessor(ref layerHandle);
                var parametersBuffers = chunk.GetBufferAccessor(ref parameterHandle);
                var previousFrameClipInfoBuffers = chunk.GetBufferAccessor(ref previousFrameClipInfoHandle);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var indexInChunk))
                {
                    var controller = controllers[indexInChunk];
                    ref var controllerBlob = ref controller.controller.Value;
                    var parameters = parametersBuffers[indexInChunk].AsNativeArray();
                    ref var parameterBlobs = ref controllerBlob.parameters;
                    var layers = layersBuffers[indexInChunk].AsNativeArray();
                    var previousFrameClipInfo = previousFrameClipInfoBuffers[indexInChunk].AsNativeArray();

                    var deltaTime = this.deltaTime * controller.speed;
                    float inertialBlendMaxTime = float.MaxValue;
                    bool needsInertialBlend = false;

                    for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                    {
                        var layer = layers[layerIndex];
                        ref var layerBlob = ref controllerBlob.layers[layerIndex];

                        layer.timeInState += deltaTime;

                        // Finish active transition and update previous clip info time fragments
                        if (layer.previousStateIndex >= 0 && layer.transitionEndTimeInState <= layer.timeInState)
                        {
                            for (int i = 0; i < previousFrameClipInfo.Length; i++)
                            {
                                var clipInfo = previousFrameClipInfo[i];
                                if (clipInfo.layerIndex == layerIndex && clipInfo.stateIndex == layer.previousStateIndex)
                                {
                                    clipInfo.timeFragment = layer.timeInState - layer.transitionEndTimeInState;
                                    previousFrameClipInfo[i] = clipInfo;
                                }
                            }

                            layer.previousStateIndex = -1;
                            layer.currentTransitionIndex = -1;
                            layer.currentTransitionIsAnyState = false;
                        }
                        else
                        {
                            for (int i = 0; i < previousFrameClipInfo.Length; i++)
                            {
                                var clipInfo = previousFrameClipInfo[i];
                                if (clipInfo.layerIndex == layerIndex)
                                {
                                    clipInfo.timeFragment = deltaTime;
                                    previousFrameClipInfo[i] = clipInfo;
                                }
                            }
                        }

                        // Only evaluate non-interrupting transitions if we are not already transitioning
                        if (!layer.isInTransition)
                        {
                            bool didLocalTransition = false;

                            // Check current state transitions
                            ref var currentState = ref layerBlob.states[layer.currentStateIndex];
                            for (short i = 0; i < currentState.transitions.Length; i++)
                            {
                                ref var transition = ref currentState.transitions[i];

                                // Early out if we have an exit time and we haven't reached it yet
                                ref var state = ref layerBlob.states[layer.currentStateIndex];
                                float normalizedTimeInState = (layer.timeInState % state.averageDuration) / state.averageDuration;
                                if (transition.hasExitTime && transition.exitTime > normalizedTimeInState)
                                    continue;

                                // Evaluate conditions
                                if (ConditionsMet(layer, ref layerBlob, ref transition, parameters, ref parameterBlobs, deltaTime))
                                {
                                    PerformTransition(ref layer, ref layerBlob, ref transition, i, false);
                                    ConsumeTriggers(ref transition, ref parameters, ref parameterBlobs);
                                    didLocalTransition = true;
                                    break;
                                }
                            }

                            // Check any state transitions
                            if (!didLocalTransition)
                            {
                                for (short i = 0; i < layerBlob.anyStateTransitions.Length; i++)
                                {
                                    ref var transition = ref layerBlob.anyStateTransitions[i];

                                    // If we have no conditions, we can only transition if we are done
                                    if (transition.conditions.Length == 0)
                                    {
                                        if (layer.currentStateIndex == -1)
                                        {
                                            PerformTransition(ref layer, ref layerBlob, ref transition, i, true);
                                            ConsumeTriggers(ref transition, ref parameters, ref parameterBlobs);
                                            break;
                                        }
                                    }
                                    else if (ConditionsMet(layer, ref layerBlob, ref transition, parameters, ref parameterBlobs, deltaTime))
                                    {
                                        PerformTransition(ref layer, ref layerBlob, ref transition, i, true);
                                        ConsumeTriggers(ref transition, ref parameters, ref parameterBlobs);
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Check if we can interrupt the current transition
                            if (layer.currentTransitionIsAnyState)
                            {
                                ref var currentTransition = ref layerBlob.anyStateTransitions[layer.currentTransitionIndex];
                                needsInertialBlend |= TryInterruptTransition(ref layer,
                                                                                    ref layerBlob,
                                                                                    ref currentTransition,
                                                                                    ref parameters,
                                                                                    ref parameterBlobs,
                                                                                    ref inertialBlendMaxTime);
                            }
                            else if (layer.currentTransitionIndex >= 0)
                            {
                                ref var currentTransition = ref layerBlob.states[layer.previousStateIndex].transitions[layer.currentTransitionIndex];
                                needsInertialBlend |= TryInterruptTransition(ref layer,
                                                                                    ref layerBlob,
                                                                                    ref currentTransition,
                                                                                    ref parameters,
                                                                                    ref parameterBlobs,
                                                                                    ref inertialBlendMaxTime);
                            }
                        }

                        layers[layerIndex] = layer;
                    }

                    if (needsInertialBlend)
                    {
                        if (controller.triggerStartInertialBlend)
                        {
                            // User triggered inertial blend
                            controller.newInertialBlendDuration = math.min(controller.newInertialBlendDuration, inertialBlendMaxTime);
                        }
                        else
                        {
                            controller.newInertialBlendDuration = inertialBlendMaxTime;
                        }

                        controller.triggerStartInertialBlend = true;
                    }
                }
            }

            // See the following resources:
            // https://www.gamedeveloper.com/design/unlocking-unity-animator-s-interruption-full-power
            // https://docs.unity3d.com/Manual/class-Transition.html#TransitionInterruption
            // https://blog.unity.com/technology/wait-ive-changed-my-mind-state-machine-transition-interruptions
            //
            // As discussed in the Unity blog, when an interruption happens, it captures the static pose
            // of the current transition blend and then blends between that and the new state.
            // Capturing a pose is really tricky for us since we update the pose information separately.
            // However, no one in their right mind is going to author animations specific to how Unity
            // does interruptions on blended transitions using the static poses. So instead of trying
            // to match how Unity handles this, let's use the superior technique of inertial blending,
            // which is built into optimized skeletons and we can easily add support for exposed skeletons.

            private bool TryInterruptTransition(ref MecanimLayerStateMachineStatus layer,
                                                ref MecanimControllerLayerBlob layerBlob,
                                                ref MecanimStateTransitionBlob currentTransition,
                                                ref NativeArray<MecanimParameter> parameters,
                                                ref BlobArray<MecanimParameterBlob> parameterBlobs,
                                                ref float maxTransitionDuration)
            {
                switch (currentTransition.interruptionSource)
                {
                    case MecanimStateTransitionBlob.InterruptionSource.Source:
                        {
                            ref var anyTransitions = ref layerBlob.anyStateTransitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref anyTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration, -1))
                                return true;
                            ref var sourceStateTransitions = ref layerBlob.states[currentTransition.originStateIndex].transitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref sourceStateTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration,
                                                      layer.currentTransitionIndex))
                                return true;

                            break;
                        }
                    case MecanimStateTransitionBlob.InterruptionSource.Destination:
                        {
                            ref var anyTransitions = ref layerBlob.anyStateTransitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref anyTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration, -1))
                                return true;
                            ref var destinationStateTransitions = ref layerBlob.states[currentTransition.destinationStateIndex].transitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref destinationStateTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration, -1))
                                return true;
                            break;
                        }
                    case MecanimStateTransitionBlob.InterruptionSource.SourceThenDestination:
                        {
                            ref var anyTransitions = ref layerBlob.anyStateTransitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref anyTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration, -1))
                                return true;
                            ref var sourceStateTransitions = ref layerBlob.states[currentTransition.originStateIndex].transitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref sourceStateTransitions, ref parameters, ref parameterBlobs,
                                                      ref maxTransitionDuration, layer.currentTransitionIndex))
                                return true;
                            ref var destinationStateTransitions = ref layerBlob.states[currentTransition.destinationStateIndex].transitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref destinationStateTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration, -1))
                                return true;
                            break;
                        }
                    case MecanimStateTransitionBlob.InterruptionSource.DestinationThenSource:
                        {
                            ref var anyTransitions = ref layerBlob.anyStateTransitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref anyTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration, -1))
                                return true;
                            ref var destinationStateTransitions = ref layerBlob.states[currentTransition.destinationStateIndex].transitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref destinationStateTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration, -1))
                                return true;
                            ref var sourceStateTransitions = ref layerBlob.states[currentTransition.originStateIndex].transitions;
                            if (TryInterruptFromState(ref layer, ref layerBlob, ref sourceStateTransitions, ref parameters, ref parameterBlobs, ref maxTransitionDuration,
                                                      layer.currentTransitionIndex))
                                return true;
                            break;
                        }
                }

                return false;
            }

            private bool TryInterruptFromState(ref MecanimLayerStateMachineStatus layer,
                                               ref MecanimControllerLayerBlob layerBlob,
                                               ref BlobArray<MecanimStateTransitionBlob> interruptingTransitions,
                                               ref NativeArray<MecanimParameter> parameters,
                                               ref BlobArray<MecanimParameterBlob> parameterBlobs,
                                               ref float transitionMaxDuration,
                                               short orderedTransitionStopIndex)
            {
                var transitionCount = math.select(interruptingTransitions.Length, orderedTransitionStopIndex, orderedTransitionStopIndex >= 0);
                for (short i = 0; i < transitionCount; i++)
                {
                    ref var interruptingTransition = ref interruptingTransitions[i];

                    if (ConditionsMet(layer, ref layerBlob, ref interruptingTransition, parameters, ref parameterBlobs, deltaTime))
                    {
                        PerformTransition(ref layer, ref layerBlob, ref interruptingTransition, i, false, true);
                        ConsumeTriggers(ref interruptingTransition, ref parameters, ref parameterBlobs);
                        transitionMaxDuration = math.min(transitionMaxDuration, interruptingTransition.duration);
                        return true;
                    }
                }
                return false;
            }

            private void PerformTransition(ref MecanimLayerStateMachineStatus layer,
                                           ref MecanimControllerLayerBlob layerBlob,
                                           ref MecanimStateTransitionBlob transitionBlob,
                                           short stateTransitionIndex,
                                           bool isAnyStateTransition,
                                           bool needsInertialBlend = false)
            {
                layer.previousStateIndex = layer.currentStateIndex;
                layer.previousStateExitTime = layer.timeInState;
                layer.currentTransitionIndex = stateTransitionIndex;
                layer.currentTransitionIsAnyState = isAnyStateTransition;
                layer.currentStateIndex = transitionBlob.destinationStateIndex != -1 ? transitionBlob.destinationStateIndex : layerBlob.defaultStateIndex;  // "Exit" state

                ref var destinationState = ref layerBlob.states[layer.currentStateIndex];
                ref var lastState = ref layerBlob.states[layer.previousStateIndex];
                layer.timeInState = transitionBlob.offset * destinationState.averageDuration;
                layer.transitionEndTimeInState = transitionBlob.hasFixedDuration ?
                                                 transitionBlob.duration :
                                                 lastState.averageDuration * transitionBlob.duration;
                layer.transitionIsInertialBlend = needsInertialBlend;
            }

            private bool ConditionsMet(in MecanimLayerStateMachineStatus layer,
                                       ref MecanimControllerLayerBlob layerBlob,
                                       ref MecanimStateTransitionBlob transitionBlob,
                                       in NativeArray<MecanimParameter> parameters,
                                       ref BlobArray<MecanimParameterBlob> parameterBlobs,
                                       float deltaTime)
            {
                bool conditionsMet = true;
                for (int j = 0; j < transitionBlob.conditions.Length && conditionsMet; j++)
                {
                    ref var condition = ref transitionBlob.conditions[j];
                    var parameter = parameters[condition.parameterIndex];
                    ref var parameterData = ref parameterBlobs[condition.parameterIndex];

                    if (parameterData.parameterType == AnimatorControllerParameterType.Trigger)
                    {
                        conditionsMet = parameter.triggerParam;
                        continue;
                    }

                    switch (condition.mode)
                    {
                        case MecanimConditionBlob.ConditionType.Equals:
                            if (parameterData.parameterType == AnimatorControllerParameterType.Int)
                            {
                                // Todo: We should probably bake threshold as a union of int and float
                                // so that we don't have to do this cast
                                conditionsMet = parameter.intParam == (int)condition.threshold;
                            }
                            else
                            {
                                conditionsMet = MecanimInternalUtilities.Approximately(parameter.floatParam, condition.threshold);
                            }

                            break;
                        case MecanimConditionBlob.ConditionType.NotEqual:
                            if (parameterData.parameterType == AnimatorControllerParameterType.Int)
                            {
                                conditionsMet = parameter.intParam != (int)condition.threshold;
                            }
                            else
                            {
                                conditionsMet = !MecanimInternalUtilities.Approximately(parameter.floatParam, condition.threshold);
                            }

                            break;
                        case MecanimConditionBlob.ConditionType.Greater:
                            if (parameterData.parameterType == AnimatorControllerParameterType.Int)
                            {
                                conditionsMet = parameter.intParam > (int)condition.threshold;
                            }
                            else
                            {
                                conditionsMet = parameter.floatParam > condition.threshold;
                            }

                            break;
                        case MecanimConditionBlob.ConditionType.Less:
                            if (parameterData.parameterType == AnimatorControllerParameterType.Int)
                            {
                                conditionsMet = parameter.intParam < (int)condition.threshold;
                            }
                            else
                            {
                                conditionsMet = parameter.floatParam < condition.threshold;
                            }

                            break;
                        case MecanimConditionBlob.ConditionType.If:
                            conditionsMet = parameter.boolParam;
                            break;
                        case MecanimConditionBlob.ConditionType.IfNot:
                            conditionsMet = !parameter.boolParam;
                            break;
                    }
                }

                if (transitionBlob.hasExitTime)
                {
                    ref var state = ref layerBlob.states[layer.currentStateIndex];

                    float normalizedTimeInState = (layer.timeInState % state.averageDuration) / state.averageDuration;
                    var normalizedDeltaTime = deltaTime / state.averageDuration;

                    conditionsMet &= transitionBlob.exitTime > normalizedTimeInState - normalizedDeltaTime && transitionBlob.exitTime <= normalizedTimeInState;
                }

                return conditionsMet;
            }

            private void ConsumeTriggers(ref MecanimStateTransitionBlob transitionBlob,
                                         ref NativeArray<MecanimParameter> parameters,
                                         ref BlobArray<MecanimParameterBlob> paramterBlobs)
            {
                for (int i = 0; i < transitionBlob.conditions.Length; i++)
                {
                    ref var condition = ref transitionBlob.conditions[i];
                    var parameterIndex = condition.parameterIndex;
                    if (paramterBlobs[parameterIndex].parameterType == AnimatorControllerParameterType.Trigger)
                        parameters[parameterIndex] = new MecanimParameter { triggerParam = false };
                }
            }
        }
    }
}