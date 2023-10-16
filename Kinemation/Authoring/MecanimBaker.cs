#if UNITY_EDITOR
using System;
using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using UnityEditor.Animations;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    [TemporaryBakingType]
    internal struct MecanimSmartBakeItem : ISmartBakeItem<Animator>
    {
        private SmartBlobberHandle<SkeletonClipSetBlob> m_clipSetBlobHandle;
        private SmartBlobberHandle<MecanimControllerBlob> m_controllerBlobHandle;

        public bool Bake(Animator authoring, IBaker baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.Dynamic);

            var runtimeAnimatorController = authoring.runtimeAnimatorController;
            if (runtimeAnimatorController == null)
            {
                return false;
            }

            // Bake clips
            var clips       = new NativeArray<SkeletonClipConfig>(runtimeAnimatorController.animationClips.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var sourceClips = runtimeAnimatorController.animationClips;
            for (int i = 0; i < sourceClips.Length; i++)
            {
                var sourceClip = sourceClips[i];

                clips[i] = new SkeletonClipConfig
                {
                    clip     = sourceClip,
                    events   = sourceClip.ExtractKinemationClipEvents(Allocator.Temp),
                    settings = SkeletonClipCompressionSettings.kDefaultSettings
                };
            }

            baker.AddBuffer<MecanimActiveClipEvent>(entity);

            // Bake controller
            baker.AddComponent( entity, new MecanimController { speed = authoring.speed, applyRootMotion = authoring.applyRootMotion});
            baker.SetComponentEnabled<MecanimController>(entity, authoring.enabled);

            AnimatorController animatorController = baker.FindAnimatorController(runtimeAnimatorController);

            // Add previous state buffer
            baker.AddBuffer<TimedMecanimClipInfo>(entity);

            // Bake parameters
            var parameters       = animatorController.parameters;
            var parametersBuffer = baker.AddBuffer<MecanimParameter>(entity);
            foreach (var parameter in parameters)
            {
                var parameterData = new MecanimParameter();
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger:
                    {
                        parameterData.boolParam = parameter.defaultBool;
                        break;
                    }
                    case AnimatorControllerParameterType.Float:
                    {
                        parameterData.floatParam = parameter.defaultFloat;
                        break;
                    }
                    case AnimatorControllerParameterType.Int:
                    {
                        parameterData.intParam = parameter.defaultInt;
                        break;
                    }
                }
                parametersBuffer.Add(parameterData);
            }

            // Bake layers
            var maskCount = 0;
            var layers    = animatorController.layers;

            var layerStatusBuffer = baker.AddBuffer<MecanimLayerStateMachineStatus>(entity);
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];

                var maskIndex = -1;
                //Layer mask
                if (layer.avatarMask != null)
                {
                    maskIndex = maskCount;
                    maskCount++;
                }

                int defaultStateIndex = -1;
                for (int j = 0; j < layer.stateMachine.states.Length; j++)
                {
                    if (layer.stateMachine.defaultState == layer.stateMachine.states[j].state)
                    {
                        defaultStateIndex = j;
                        break;
                    }
                }

                if (defaultStateIndex == -1)
                {
                    Debug.LogWarning($"No default state was found for {animatorController.name} for layer {layer.name}. Assuming the first state is default.");
                    defaultStateIndex = 0;
                }

                layerStatusBuffer.Add(new MecanimLayerStateMachineStatus
                {
                    currentStateIndex      = (short)defaultStateIndex,
                    previousStateIndex     = -1,
                    currentTransitionIndex = -1
                });
            }

            // Bake extra for exposed skeletons
            if (authoring.hasTransformHierarchy)
                baker.AddBuffer<ExposedSkeletonInertialBlendState>(entity);

            m_clipSetBlobHandle    = baker.RequestCreateBlobAsset(authoring, clips);
            m_controllerBlobHandle = baker.RequestCreateBlobAsset(animatorController);

            return true;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            var animatorController = entityManager.GetComponentData<MecanimController>(entity);
            animatorController.clips      = m_clipSetBlobHandle.Resolve(entityManager);
            animatorController.controller = m_controllerBlobHandle.Resolve(entityManager);
            entityManager.SetComponentData(entity, animatorController);
        }
    }

    [DisableAutoCreation]
    internal class MecanimSmartBaker : SmartBaker<Animator, MecanimSmartBakeItem>
    {
    }

    public static class MecanimAnimatorControllerExtensions
    {
        /// <summary>
        /// Finds the reference AnimatorController from a RuntimeAnimatorController
        /// </summary>
        /// <param name="runtimeAnimatorController">The RuntimeAnimatorController, perhaps obtained from an Animator</param>
        /// <returns>The base AnimatorController that the RuntimeAnimatorController is or is an override of</returns>
        public static AnimatorController FindAnimatorController(this IBaker baker, RuntimeAnimatorController runtimeAnimatorController)
        {
            if (runtimeAnimatorController is AnimatorController animatorController)
            {
                baker.DependsOn(animatorController);
                return animatorController;
            }
            else if (runtimeAnimatorController is AnimatorOverrideController animatorOverrideController)
            {
                baker.DependsOn(animatorOverrideController);
                return FindAnimatorController(baker, animatorOverrideController.runtimeAnimatorController);
            }
            else
            {
                throw new Exception(
                    $"Encountered unknown animator controller type {runtimeAnimatorController.GetType()}. If you see this, please report a bug to the Latios Framework developers.");
            }
        }

        /// <summary>
        /// Finds a parameter index in an array of parameters (which can be retrieved from an animator controller
        /// </summary>
        /// <param name="parameters">The array of parameters</param>
        /// <param name="parameterName">The name of the parameter to find</param>
        /// <param name="parameterIndex">The found index of the parameter if found, otherwise -1</param>
        /// <returns>True if a parameter with the specified name was found</returns>
        public static bool TryGetParameter(this AnimatorControllerParameter[] parameters, string parameterName, out short parameterIndex)
        {
            parameterIndex = -1;
            for (short i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName)
                {
                    parameterIndex = i;
                    return true;
                }
            }
            return false;
        }
    }
}
#endif

