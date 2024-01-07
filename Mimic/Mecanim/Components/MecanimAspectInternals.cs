using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Mimic.Addons.Mecanim
{
    /// <summary>
    /// Aspect that exposes Unity animator functionality
    /// </summary>
    public readonly partial struct  MecanimAspect : IAspect
    {
        readonly EnabledRefRW<MecanimController>               m_enabledTag;
        readonly RefRW<MecanimController>                      m_controller;
        readonly DynamicBuffer<MecanimLayerStateMachineStatus> m_layers;
        readonly DynamicBuffer<MecanimParameter>               m_parameters;

        private short GetParameterIndex(FixedString64Bytes name)
        {
            ref var parameterBlobs = ref m_controller.ValueRO.controller.Value.parameters;
            for (short i = 0; i < m_parameters.Length; i++)
            {
                if (parameterBlobs[i].name == name)
                {
                    return i;
                }
            }
            return -1;
        }

        private short GetParameterIndex(int nameHash, bool isEditorHash)
        {
            ref var parameterBlobs = ref m_controller.ValueRO.controller.Value.parameters;
            for (short i = 0; i < m_parameters.Length; i++)
            {
                if (nameHash == math.select(parameterBlobs[i].nameHash, parameterBlobs[i].editorNameHash, isEditorHash))
                {
                    return i;
                }
            }

            return -1;
        }

        private float GetFloatParameter(short index)
        {
            if (index < 0)
            {
                UnityEngine.Debug.LogError("The Mecanim parameter was not found.");
                return default;
            }
            if (m_controller.ValueRO.controller.Value.parameters[index].parameterType != UnityEngine.AnimatorControllerParameterType.Float)
            {
                UnityEngine.Debug.Log($"The Mecanim parameter at index {index} is not of type 'float'");
                return default;
            }
            return m_parameters.ElementAt(index).floatParam;
        }

        private void SetFloatParameter(short index, float p)
        {
            if (index < 0)
            {
                UnityEngine.Debug.LogError("The Mecanim parameter was not found.");
                return;
            }
            if (m_controller.ValueRO.controller.Value.parameters[index].parameterType != UnityEngine.AnimatorControllerParameterType.Float)
            {
                UnityEngine.Debug.Log($"The Mecanim parameter at index {index} is not of type 'float'");
                return;
            }
            m_parameters.ElementAt(index).floatParam = p;
        }

        private int GetIntParameter(short index)
        {
            if (index < 0)
            {
                UnityEngine.Debug.LogError("The Mecanim parameter was not found.");
                return default;
            }
            if (m_controller.ValueRO.controller.Value.parameters[index].parameterType != UnityEngine.AnimatorControllerParameterType.Int)
            {
                UnityEngine.Debug.Log($"The Mecanim parameter at index {index} is not of type 'int'");
                return default;
            }
            return m_parameters.ElementAt(index).intParam;
        }

        private void SetIntParameter(short index, int p)
        {
            if (index < 0)
            {
                UnityEngine.Debug.LogError("The Mecanim parameter was not found.");
                return;
            }
            if (m_controller.ValueRO.controller.Value.parameters[index].parameterType != UnityEngine.AnimatorControllerParameterType.Int)
            {
                UnityEngine.Debug.Log($"The Mecanim parameter at index {index} is not of type 'int'");
                return;
            }
            m_parameters.ElementAt(index).intParam = p;
        }

        private bool GetBoolParameter(short index)
        {
            if (index < 0)
            {
                UnityEngine.Debug.LogError("The Mecanim parameter was not found.");
                return default;
            }
            if (m_controller.ValueRO.controller.Value.parameters[index].parameterType != UnityEngine.AnimatorControllerParameterType.Bool)
            {
                UnityEngine.Debug.Log($"The Mecanim parameter at index {index} is not of type 'bool'");
                return default;
            }
            return m_parameters.ElementAt(index).boolParam;
        }

        private void SetBoolParameter(short index, bool p)
        {
            if (index < 0)
            {
                UnityEngine.Debug.LogError("The Mecanim parameter was not found.");
                return;
            }
            if (m_controller.ValueRO.controller.Value.parameters[index].parameterType != UnityEngine.AnimatorControllerParameterType.Bool)
            {
                UnityEngine.Debug.Log($"The Mecanim parameter at index {index} is not of type 'bool'");
                return;
            }
            m_parameters.ElementAt(index).boolParam = p;
        }

        private bool GetTriggerParameter(short index)
        {
            if (index < 0)
            {
                UnityEngine.Debug.LogError("The Mecanim parameter was not found.");
                return default;
            }
            if (m_controller.ValueRO.controller.Value.parameters[index].parameterType != UnityEngine.AnimatorControllerParameterType.Trigger)
            {
                UnityEngine.Debug.Log($"The Mecanim parameter at index {index} is not of type 'Trigger'");
                return default;
            }
            return m_parameters.ElementAt(index).triggerParam;
        }

        private void SetTriggerParameter(short index, bool p)
        {
            if (index < 0)
            {
                UnityEngine.Debug.LogError("The Mecanim parameter was not found.");
                return;
            }
            if (m_controller.ValueRO.controller.Value.parameters[index].parameterType != UnityEngine.AnimatorControllerParameterType.Trigger)
            {
                UnityEngine.Debug.Log($"The Mecanim parameter at index {index} is not of type 'Trigger'");
                return;
            }
            m_parameters.ElementAt(index).triggerParam = p;
        }

        private short GetStateIndex(int stateNameHash, int layerIndex, bool isEditorHash)
        {
            ref var layerData = ref m_controller.ValueRO.controller.Value.layers[layerIndex];
            for (short i = 0; i < layerData.states.Length; i++)
            {
                ref var state = ref layerData.states[i];
                if (stateNameHash == math.select(state.nameHash, state.editorNameHash, isEditorHash))
                {
                    return i;
                }
            }
            return -1;
        }

        private short GetStateIndex(in FixedString64Bytes stateName, int layerIndex)
        {
            ref var layerData = ref m_controller.ValueRO.controller.Value.layers[layerIndex];
            for (short i = 0; i < layerData.states.Length; i++)
            {
                ref var state = ref layerData.states[i];
                if (state.name == stateName)
                {
                    return i;
                }
            }
            return -1;
        }

        private void CrossFadeInFixedTime(ref MecanimStateBlob state, short stateIndex, int layerIndex, float fixedTransitionDuration, float fixedTimeOffset)
        {
            ref var layer = ref m_layers.ElementAt(layerIndex);

            layer.previousStateIndex                       = layer.currentStateIndex;
            layer.previousStateExitTime                    = layer.timeInState % state.averageDuration;
            layer.currentStateIndex                        = stateIndex;
            layer.timeInState                              = fixedTimeOffset;
            layer.transitionEndTimeInState                 = fixedTimeOffset + fixedTransitionDuration;
            layer.currentTransitionIndex                   = -2;
            layer.currentTransitionIsAnyState              = false;
            layer.transitionIsInertialBlend                = true;
            m_controller.ValueRW.triggerStartInertialBlend = true;
            m_controller.ValueRW.newInertialBlendDuration  = fixedTransitionDuration;
        }
    }
}

