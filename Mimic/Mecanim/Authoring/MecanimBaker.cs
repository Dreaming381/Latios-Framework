#if UNITY_EDITOR
using System;
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Collections;
using Unity.Entities;
using UnityEditor.Animations;
using UnityEngine;

namespace Latios.Mimic.Addons.Mecanim.Authoring
{
    [TemporaryBakingType]
    internal struct MecanimSmartBakeItem : ISmartBakeItem<Animator>
    {
        private SmartBlobberHandle<SkeletonClipSetBlob> m_clipSetBlobHandle;
        private SmartBlobberHandle<MecanimControllerBlob> m_controllerBlobHandle;
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
        private NativeArray<SmartBlobberHandle<ParameterClipSetBlob> > m_blendShapesBlobHandles;
#endif

        public bool Bake(Animator authoring, IBaker baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.Dynamic);

            var runtimeAnimatorController = authoring.runtimeAnimatorController;
            if (runtimeAnimatorController == null)
            {
                return false;
            }

#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            const int CLIP_SAMPLE_RATE = 60;

            // Get a list of skinned mesh renderers for blend shape baking
            var skinnedMeshRenderersWithBlendShapes = authoring
                                                      .GetComponentsInChildren<SkinnedMeshRenderer>()
                                                      .Where(x => x.sharedMesh != null && x.sharedMesh.blendShapeCount > 0)
                                                      .ToArray();
            var blendShapeEntities = new NativeList<Entity>(skinnedMeshRenderersWithBlendShapes.Length, Allocator.Temp);
            for (int i = 0; i < skinnedMeshRenderersWithBlendShapes.Length; i++)
            {
                var renderer       = skinnedMeshRenderersWithBlendShapes[i];
                var rendererEntity = baker.GetEntity(renderer, TransformUsageFlags.None);
                if (rendererEntity != default)
                {
                    blendShapeEntities.Add(rendererEntity);
                }
            }
#endif

            // Bake clips
            var sourceClips         = runtimeAnimatorController.animationClips;
            var sleletonClipConfigs = new NativeArray<SkeletonClipConfig>(sourceClips.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            var blendShapeEntityClipConfigs = new NativeParallelMultiHashMap<Entity, BlendShapeParameterConfig>(skinnedMeshRenderersWithBlendShapes.Length * sourceClips.Length,
                                                                                                                Allocator.Temp);
#endif
            for (int i = 0; i < sourceClips.Length; i++)
            {
                var sourceClip = sourceClips[i];

                sleletonClipConfigs[i] = new SkeletonClipConfig
                {
                    clip     = sourceClip,
                    events   = sourceClip.ExtractKinemationClipEvents(Allocator.Temp),
                    settings = SkeletonClipCompressionSettings.kDefaultSettings
                };

#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
                //Create clip configs for blend shapes
                var bindings = AnimationUtility.GetCurveBindings(sourceClip);
                for (int j = 0; j < skinnedMeshRenderersWithBlendShapes.Length; j++)
                {
                    var renderer       = skinnedMeshRenderersWithBlendShapes[j];
                    var rendererEntity = blendShapeEntities[j];

                    //Bake sample data for blend shapes
                    for (int k = 0; k < renderer.sharedMesh.blendShapeCount; k++)
                    {
                        const float MAX_BLEND_SHAPE_WEIGHT = 100f;
                        var shapeName              = renderer.sharedMesh.GetBlendShapeName(k);
                        var samples                = new NativeList<float>(Allocator.Temp);
                        bool foundMatchingBinding   = false;
                        foreach (var binding in bindings)
                        {
                            if (binding.type == typeof(SkinnedMeshRenderer))
                            {
                                if (AnimationUtility.CalculateTransformPath(renderer.transform, authoring.avatarRoot) ==
                                    binding.path &&
                                    $"blendShape.{shapeName}" == binding.propertyName)
                                {
                                    foundMatchingBinding = true;
                                    // Normally, we could use curve.keys[^ 1].time, but we need to have each curve have the same number of samples
                                    var curve     = AnimationUtility.GetEditorCurve(sourceClip, binding);
                                    var curveTime = sourceClip.averageDuration;

                                    var sampleInterval = curveTime / CLIP_SAMPLE_RATE;
                                    float totalTime      = 0f;

                                    while (totalTime <= curveTime)
                                    {
                                        var curveValue = curve.Evaluate(totalTime);
                                        samples.Add(curveValue / MAX_BLEND_SHAPE_WEIGHT);
                                        //samples.Add(0.9f);
                                        totalTime += sampleInterval;
                                    }

                                    break;
                                }
                            }
                        }

                        //If we don't have a matching binding, add samples so that they won't affect the blend shape
                        if (!foundMatchingBinding)
                        {
                            var curveTime      = sourceClip.averageDuration;
                            var sampleInterval = curveTime / CLIP_SAMPLE_RATE;
                            float totalTime      = 0f;
                            samples = new NativeList<float>(Allocator.Temp);
                            while (totalTime <= curveTime)
                            {
                                samples.Add(float.MinValue);
                                totalTime += sampleInterval;
                            }
                        }

                        blendShapeEntityClipConfigs.Add(rendererEntity, new BlendShapeParameterConfig
                        {
                            clipIndex     = i,
                            shapeIndex    = k,
                            parameterName = shapeName,
                            Parameter     = new ParameterClipConfig.Parameter
                            {
                                samples = samples.AsArray()
                            }
                        });
                    }
                }
#endif
            }

            baker.AddBuffer<MecanimActiveClipEvent>(entity);

            // Bake controller
            baker.AddComponent(entity, new MecanimController { speed = authoring.speed, applyRootMotion = authoring.applyRootMotion });
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

            m_clipSetBlobHandle    = baker.RequestCreateBlobAsset(authoring, sleletonClipConfigs);
            m_controllerBlobHandle = baker.RequestCreateBlobAsset(animatorController);

#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            //Bake blend shapes
            baker.AddBuffer<BlendShapeClipSet>(entity);
            m_blendShapesBlobHandles = new NativeArray<SmartBlobberHandle<ParameterClipSetBlob> >(blendShapeEntities.Length, Allocator.TempJob);
            for (int i = 0; i < blendShapeEntities.Length; i++)
            {
                var blendShapeEntity = blendShapeEntities[i];
                var parameterCount   = skinnedMeshRenderersWithBlendShapes[i].sharedMesh.blendShapeCount;
                baker.AppendToBuffer(entity, new BlendShapeClipSet
                {
                    meshEntity = blendShapeEntity
                });

                // Create clipset config for this entity
                var blendShapeClipConfigs    = new NativeArray<ParameterClipConfig>(sourceClips.Length, Allocator.Temp);
                var blendShapeParameterNames = new NativeArray<FixedString128Bytes>(parameterCount, Allocator.Temp);
                for (int j = 0; j < sourceClips.Length; j++)
                {
                    var enumerator           = blendShapeEntityClipConfigs.GetEnumerator();
                    var blendShapeParameters = new NativeArray<ParameterClipConfig.Parameter>(parameterCount, Allocator.Temp);
                    while (enumerator.MoveNext())
                    {
                        var current = enumerator.Current.Value;
                        if (enumerator.Current.Key == blendShapeEntity && current.clipIndex == j)
                        {
                            blendShapeParameters[current.shapeIndex]     = current.Parameter;
                            blendShapeParameterNames[current.shapeIndex] = current.parameterName;
                        }
                    }

                    blendShapeClipConfigs[j] = new ParameterClipConfig
                    {
                        name             = sourceClips[j].name,
                        sampleRate       = CLIP_SAMPLE_RATE,
                        parametersInClip = blendShapeParameters,
                        events           = new NativeArray<ClipEvent>(0, Allocator.Temp)
                    };
                }

                var blendShapeClipSetConfig = new ParameterClipSetConfig
                {
                    parameterNames = blendShapeParameterNames,
                    clips          = blendShapeClipConfigs,
                };

                m_blendShapesBlobHandles[i] = baker.RequestCreateBlobAsset(blendShapeClipSetConfig);
            }
#endif
            return true;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            var animatorController = entityManager.GetComponentData<MecanimController>(entity);
            animatorController.clips = m_clipSetBlobHandle.Resolve(entityManager);
#if LATIOS_MECANIM_EXPERIMENTAL_BLENDSHAPES
            var blendShapeClipSets = entityManager.GetBuffer<BlendShapeClipSet>(entity);
            for (int i = 0; i < blendShapeClipSets.Length; i++)
            {
                var blendShapeClipSet = blendShapeClipSets[i];
                blendShapeClipSet.clips = m_blendShapesBlobHandles[i].Resolve(entityManager);
                blendShapeClipSets[i]   = blendShapeClipSet;
            }
            m_blendShapesBlobHandles.Dispose();
#endif
            animatorController.controller = m_controllerBlobHandle.Resolve(entityManager);
            entityManager.SetComponentData(entity, animatorController);
        }
    }

    internal struct BlendShapeParameterConfig
    {
        public int clipIndex;
        public int shapeIndex;
        public FixedString128Bytes parameterName;
        public ParameterClipConfig.Parameter Parameter;
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

