#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor.Animations;
using UnityEngine;

// Todo: This blob builder is very heavy on allocations with the use of Linq.
// It may be worth optimizing at some point.

namespace Latios.Mimic.Addons.Mecanim.Authoring
{
    public static class MecanimBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of an MecanimControllerLayerBlob Blob Asset
        /// </summary>
        /// <param name="animatorController">An animatorController whose layer to bake.</param>
        /// <param name="layerIndex">The index of the layer to bake.</param>
        public static SmartBlobberHandle<MecanimControllerBlob> RequestCreateBlobAsset(this IBaker baker, AnimatorController animatorController)
        {
            return baker.RequestCreateBlobAsset<MecanimControllerBlob, AnimatorControllerBakeData>(new AnimatorControllerBakeData
            {
                animatorController = animatorController
            });
        }
    }

    /// <summary>
    /// Input for the AnimatorController Smart Blobber
    /// </summary>
    public struct AnimatorControllerBakeData : ISmartBlobberRequestFilter<MecanimControllerBlob>
    {
        /// <summary>
        /// The UnityEngine.Animator to bake into a blob asset reference.
        /// </summary>
        public AnimatorController animatorController;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            baker.AddComponent(blobBakingEntity, new MecanimControllerBlobRequest
            {
                animatorController = new UnityObjectRef<AnimatorController> { Value = animatorController },
            });

            return true;
        }
    }

    [TemporaryBakingType]
    internal struct MecanimControllerBlobRequest : IComponentData
    {
        public UnityObjectRef<AnimatorController> animatorController;
    }
}

namespace Latios.Mimic.Addons.Mecanim.Authoring.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class MecanimAnimatorControllerSmartBlobberSystem : SystemBase
    {
        protected override void OnCreate()
        {
            new SmartBlobberTools<MecanimControllerBlob>().Register(World);
        }

        protected override void OnUpdate()
        {
            int count = SystemAPI.QueryBuilder().WithAll<MecanimControllerBlobRequest>().WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                        .Build().CalculateEntityCountWithoutFiltering();
            var hashmap = new NativeParallelHashMap<UnityObjectRef<AnimatorController>, BlobAssetReference<MecanimControllerBlob> >(count * 2, Allocator.TempJob);

            new GatherJob { hashmap = hashmap.AsParallelWriter() }.ScheduleParallel();
            CompleteDependency();

            foreach (var pair in hashmap)
            {
                pair.Value = BakeAnimatorController(pair.Key.Value);
            }

            Entities.WithReadOnly(hashmap).ForEach((ref SmartBlobberResult result, in MecanimControllerBlobRequest request) =>
            {
                var controllerBlob = hashmap[request.animatorController];
                result.blob        = UnsafeUntypedBlobAssetReference.Create(controllerBlob);
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ScheduleParallel();

            Dependency = hashmap.Dispose(Dependency);
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct GatherJob : IJobEntity
        {
            public NativeParallelHashMap<UnityObjectRef<AnimatorController>, BlobAssetReference<MecanimControllerBlob> >.ParallelWriter hashmap;

            public void Execute(in MecanimControllerBlobRequest request)
            {
                hashmap.TryAdd(request.animatorController, default);
            }
        }

        private void BakeAnimatorCondition(ref MecanimConditionBlob blobAnimatorCondition, AnimatorCondition condition,
                                           AnimatorControllerParameter[] parameters)
        {
            blobAnimatorCondition.mode      = (MecanimConditionBlob.ConditionType)condition.mode;
            blobAnimatorCondition.threshold = condition.threshold;

            var parameterIndex = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == condition.parameter)
                {
                    parameterIndex = i;
                    break;
                }
            }

            blobAnimatorCondition.parameterIndex = (short)parameterIndex;
        }

        private void BakeAnimatorStateTransition(ref BlobBuilder builder, ref MecanimStateTransitionBlob blobTransition, AnimatorStateTransition transition,
                                                 AnimatorState[] states, AnimatorControllerParameter[] parameters)
        {
            blobTransition.hasExitTime         = transition.hasExitTime;
            blobTransition.exitTime            = transition.exitTime;
            blobTransition.hasFixedDuration    = transition.hasFixedDuration;
            blobTransition.duration            = transition.duration;
            blobTransition.offset              = transition.offset;
            blobTransition.interruptionSource  = (MecanimStateTransitionBlob.InterruptionSource)transition.interruptionSource;
            blobTransition.orderedInterruption = transition.orderedInterruption;

            BlobBuilderArray<MecanimConditionBlob> conditionsBuilder =
                builder.Allocate(ref blobTransition.conditions, transition.conditions.Length);
            for (int i = 0; i < transition.conditions.Length; i++)
            {
                var conditionBlob = conditionsBuilder[i];
                BakeAnimatorCondition(ref conditionBlob, transition.conditions[i], parameters);
                conditionsBuilder[i] = conditionBlob;
            }

            var destinationStateIndex = -1;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] == transition.destinationState)
                {
                    destinationStateIndex = i;
                    break;
                }
            }

            blobTransition.destinationStateIndex = (short)destinationStateIndex;
        }

        private void AddStateSpecifics(ref BlobBuilder builder,
                                       ref MecanimStateBlob blobAnimatorState,
                                       AnimatorState state,
                                       AnimatorState[]               states,
                                       AnimatorControllerParameter[] parameters)
        {
            blobAnimatorState.name           = new FixedString64Bytes(state.name);
            blobAnimatorState.nameHash       = blobAnimatorState.name.GetHashCode();
            blobAnimatorState.editorNameHash = state.nameHash;

            BlobBuilderArray<MecanimStateTransitionBlob> transitionsBuilder =
                builder.Allocate(ref blobAnimatorState.transitions, state.transitions.Length);
            for (int i = 0; i < state.transitions.Length; i++)
            {
                ref var transitionBlob = ref transitionsBuilder[i];
                BakeAnimatorStateTransition(ref builder, ref transitionBlob, state.transitions[i], states, parameters);
                transitionsBuilder[i] = transitionBlob;
            }
        }

        private void BakeAnimatorMotion(ref BlobBuilder builder,
                                        ref MecanimStateBlob blobAnimatorState,
                                        Motion motion,
                                        AnimatorState parentState,
                                        List<ChildMotion>             motions,
                                        AnimatorControllerParameter[] parameters,
                                        AnimationClip[]               clips)
        {
            blobAnimatorState.name = new FixedString64Bytes(motion.name);

            blobAnimatorState.averageDuration     = motion.averageDuration;
            blobAnimatorState.averageSpeed        = motion.averageSpeed;
            blobAnimatorState.averageAngularSpeed = motion.averageAngularSpeed;
            blobAnimatorState.apparentSpeed       = motion.apparentSpeed;
            blobAnimatorState.isHumanMotion       = motion.isHumanMotion;
            blobAnimatorState.legacy              = motion.legacy;
            blobAnimatorState.isLooping           = motion.isLooping;

            blobAnimatorState.speed                         = parentState.speed;
            blobAnimatorState.cycleOffset                   = parentState.cycleOffset;
            blobAnimatorState.mirror                        = parentState.mirror;
            blobAnimatorState.ikOnFeet                      = parentState.iKOnFeet;
            blobAnimatorState.speedMultiplierParameterIndex = -1;

            if (parentState.speedParameterActive)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == parentState.speedParameter)
                    {
                        blobAnimatorState.speedMultiplierParameterIndex = (short)i;
                        break;
                    }
                }
            }

            blobAnimatorState.cycleOffsetParameterIndex = -1;
            if (parentState.cycleOffsetParameterActive)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == parentState.cycleOffsetParameter)
                    {
                        blobAnimatorState.cycleOffsetParameterIndex = (short)i;
                        break;
                    }
                }
            }

            blobAnimatorState.mirrorParameterIndex = -1;
            if (parentState.mirrorParameterActive)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == parentState.mirrorParameter)
                    {
                        blobAnimatorState.mirrorParameterIndex = (short)i;
                        break;
                    }
                }
            }

            blobAnimatorState.timeParameterIndex = -1;
            if (parentState.timeParameterActive)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == parentState.timeParameter)
                    {
                        blobAnimatorState.timeParameterIndex = (short)i;
                        break;
                    }
                }
            }

            if (motion is BlendTree blendTree)
            {
                blobAnimatorState.isBlendTree = true;

                blobAnimatorState.blendTreeType       = (MecanimStateBlob.BlendTreeType)blendTree.blendType;
                blobAnimatorState.blendParameterIndex = -1;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == blendTree.blendParameter)
                    {
                        blobAnimatorState.blendParameterIndex = (short)i;
                        break;
                    }
                }

                blobAnimatorState.blendParameterYIndex = -1;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].name == blendTree.blendParameterY)
                    {
                        blobAnimatorState.blendParameterYIndex = (short)i;
                        break;
                    }
                }

                blobAnimatorState.minThreshold = blendTree.minThreshold;
                blobAnimatorState.maxThreshold = blendTree.maxThreshold;

                BlobBuilderArray<short> motionBuilder =
                    builder.Allocate(ref blobAnimatorState.childMotionIndices, blendTree.children.Length);
                BlobBuilderArray<float2> motionPositionBuilder =
                    builder.Allocate(ref blobAnimatorState.childMotionPositions, blendTree.children.Length);
                BlobBuilderArray<float> motionThresholdBuilder =
                    builder.Allocate(ref blobAnimatorState.childMotionThresholds, blendTree.children.Length);
                BlobBuilderArray<short> directBlendParameterBuilder =
                    builder.Allocate(ref blobAnimatorState.directBlendParameterIndices, blendTree.children.Length);

                for (int i = 0; i < blendTree.children.Length; i++)
                {
                    short motionIndex = -1;
                    for (int j = 0; j < motions.Count; j++)
                    {
                        if (motions[j].Motion == blendTree.children[i].motion)
                        {
                            motionIndex = (short)j;
                            break;
                        }
                    }

                    if (motionIndex == -1)
                    {
                        Debug.LogWarning($"Kinemation Mecanim cannot find child motion {blendTree.children[i].motion} in blend tree {blendTree.name}.");
                    }

                    motionBuilder[i] = motionIndex;

                    motionPositionBuilder[i]  = blendTree.children[i].position;
                    motionThresholdBuilder[i] = blendTree.children[i].threshold;
                    if (string.IsNullOrWhiteSpace(blendTree.children[i].directBlendParameter))
                    {
                        directBlendParameterBuilder[i] = -1;
                    }
                    else
                    {
                        var parameterIndex = -1;
                        for (int j = 0; j < parameters.Length; j++)
                        {
                            if (parameters[j].name == blendTree.children[i].directBlendParameter)
                            {
                                parameterIndex = j;
                                break;
                            }
                        }
                        directBlendParameterBuilder[i] = (short)parameterIndex;
                    }
                }
            }
            else
            {
                //Get the clip index
                blobAnimatorState.clipIndex = -1;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] == motion)
                    {
                        blobAnimatorState.clipIndex = (short)i;
                        break;
                    }
                }
            }
        }

        private void BakeAnimatorParameter(ref MecanimParameterBlob blobAnimatorParameter, AnimatorControllerParameter parameter)
        {
            blobAnimatorParameter.parameterType  = parameter.type;
            blobAnimatorParameter.name           = parameter.name;
            blobAnimatorParameter.nameHash       = blobAnimatorParameter.name.GetHashCode();
            blobAnimatorParameter.editorNameHash = parameter.nameHash;
        }

        List<ChildMotion> m_motionCache = new List<ChildMotion>();

        private MecanimControllerLayerBlob BakeAnimatorControllerLayer(ref BlobBuilder builder,
                                                                       ref MecanimControllerLayerBlob blobAnimatorControllerLayer,
                                                                       AnimatorControllerLayer layer,
                                                                       AnimationClip[]                clips,
                                                                       AnimatorControllerParameter[]  parameters)
        {
            blobAnimatorControllerLayer.name                     = layer.name;
            blobAnimatorControllerLayer.defaultWeight            = layer.defaultWeight;
            blobAnimatorControllerLayer.syncedLayerAffectsTiming = layer.syncedLayerAffectsTiming;
            blobAnimatorControllerLayer.ikPass                   = layer.iKPass;
            blobAnimatorControllerLayer.syncedLayerIndex         = (short)layer.syncedLayerIndex;
            blobAnimatorControllerLayer.defaultStateIndex        = -1;
            blobAnimatorControllerLayer.blendingMode             = (MecanimControllerLayerBlob.LayerBlendingMode)layer.blendingMode;

            //Gather states and motions for reference
            var states = layer.stateMachine.states.Select(x => x.state).ToArray();
            m_motionCache.Clear();
            var childMotions = m_motionCache;
            foreach (var state in states)
            {
                PopulateChildMotions(ref childMotions, state.motion, state);
            }

            //States
            BlobBuilderArray<MecanimStateBlob> statesBuilder =
                builder.Allocate(ref blobAnimatorControllerLayer.states, states.Length);
            for (int i = 0; i < states.Length; i++)
            {
                if (layer.stateMachine.defaultState == states[i])
                {
                    blobAnimatorControllerLayer.defaultStateIndex = (short)i;
                }

                ref var stateBlob = ref statesBuilder[i];
                BakeAnimatorMotion(ref builder, ref stateBlob, states[i].motion, states[i], childMotions, parameters, clips);
                AddStateSpecifics(ref builder, ref stateBlob, states[i], states, parameters);
                statesBuilder[i] = stateBlob;
            }

            //Child motions for blend trees
            BlobBuilderArray<MecanimStateBlob> childMotionsBuilder =
                builder.Allocate(ref blobAnimatorControllerLayer.childMotions, childMotions.Count);
            for (int i = 0; i < childMotions.Count; i++)
            {
                ref var childMotionBlob = ref childMotionsBuilder[i];
                BakeAnimatorMotion(ref builder, ref childMotionBlob, childMotions[i].Motion, childMotions[i].ParentState, childMotions, parameters, clips);
                childMotionsBuilder[i] = childMotionBlob;
            }

            //Transitions
            BlobBuilderArray<MecanimStateTransitionBlob> anyStateTransitionsBuilder =
                builder.Allocate(ref blobAnimatorControllerLayer.anyStateTransitions, layer.stateMachine.anyStateTransitions.Length);
            for (int i = 0; i < layer.stateMachine.anyStateTransitions.Length; i++)
            {
                ref var anyStateTransitionBlob = ref anyStateTransitionsBuilder[i];
                BakeAnimatorStateTransition(ref builder, ref anyStateTransitionBlob, layer.stateMachine.anyStateTransitions[i], states, parameters);
                anyStateTransitionsBuilder[i] = anyStateTransitionBlob;
            }

            return blobAnimatorControllerLayer;
        }

        private BlobAssetReference<MecanimControllerBlob> BakeAnimatorController(AnimatorController animatorController)
        {
            var builder                = new BlobBuilder(Allocator.Temp);
            ref var blobAnimatorController = ref builder.ConstructRoot<MecanimControllerBlob>();
            blobAnimatorController.name = animatorController.name;

            BlobBuilderArray<MecanimControllerLayerBlob> layersBuilder =
                builder.Allocate(ref blobAnimatorController.layers, animatorController.layers.Length);
            for (int i = 0; i < animatorController.layers.Length; i++)
            {
                ref var layerBlob = ref layersBuilder[i];
                BakeAnimatorControllerLayer(ref builder, ref layerBlob, animatorController.layers[i], animatorController.animationClips, animatorController.parameters);
                layersBuilder[i] = layerBlob;
            }

            BlobBuilderArray<MecanimParameterBlob> parametersBuilder =
                builder.Allocate(ref blobAnimatorController.parameters, animatorController.parameters.Length);
            for (int i = 0; i < animatorController.parameters.Length; i++)
            {
                var parameterBlob = parametersBuilder[i];
                BakeAnimatorParameter(ref parameterBlob, animatorController.parameters[i]);
                parametersBuilder[i] = parameterBlob;
            }

            var result = builder.CreateBlobAssetReference<MecanimControllerBlob>(Allocator.Persistent);

            return result;
        }

        private void PopulateChildMotions(ref List<ChildMotion> motions, Motion motion, AnimatorState parentState)
        {
            if (motion is BlendTree blendTree)
            {
                foreach (var childMotion in blendTree.children)
                {
                    motions.Add(new ChildMotion { Motion = childMotion.motion, ParentState = parentState });

                    if (childMotion.motion is BlendTree childBlendTree)
                    {
                        PopulateChildMotions(ref motions, childBlendTree, parentState);
                    }
                }
            }
        }

        private struct ChildMotion
        {
            public Motion Motion;
            public AnimatorState ParentState;
        }
    }
}
#endif

