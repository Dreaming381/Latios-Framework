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
        #region Controller
        /// <summary>
        /// Sets whether the animator controller state machine is enabled and allowed to update
        /// </summary>
        public bool enabled
        {
            get => m_enabledTag.ValueRO;
            set => m_enabledTag.ValueRW = value;
        }

        /// <summary>
        /// The speed of the controller relative to normal Time.deltaTime
        /// </summary>
        public float speed
        {
            get => m_controller.ValueRO.speed;
            set => m_controller.ValueRW.speed = value;
        }
        #endregion

        #region Crossfades
        /// <summary>
        /// Creates a crossfade from the current state to any other state using times in seconds.
        /// This is always handled with an inertial blend.
        /// </summary>
        /// <param name="stateIndex">The index of the target state (fastest version).</param>
        /// <param name="fixedTransitionMaximumDuration">The duration of the transition (in seconds).</param>
        /// <param name="layerIndex">The mecanim layer where the crossfade occurs.</param>
        /// <param name="fixedTimeOffset">The time of the target state (in seconds) to start at.</param>
        public void CrossFadeInFixedTime(short stateIndex, float fixedTransitionMaximumDuration, int layerIndex, float fixedTimeOffset = 0f)
        {
            if (stateIndex > -1)
            {
                ref var layerData = ref m_controller.ValueRO.controller.Value.layers[layerIndex];
                ref var state     = ref layerData.states[stateIndex];

                CrossFadeInFixedTime(ref state, stateIndex, layerIndex, fixedTransitionMaximumDuration, fixedTimeOffset);
            }
            else
                UnityEngine.Debug.LogError("The target Mecanim state could not be found.");
        }

        /// <summary>
        /// Creates a crossfade from the current state to any other state using times in seconds.
        /// This is always handled with an inertial blend.
        /// </summary>
        /// <param name="stateNameHash">The hashed name of the target state.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        /// <param name="fixedTransitionMaximumDuration">The duration of the transition (in seconds).</param>
        /// <param name="layerIndex">The mecanim layer where the crossfade occurs.</param>
        /// <param name="fixedTimeOffset">The time of the target state (in seconds) to start at.</param>
        public void CrossFadeInFixedTime(int stateNameHash, bool isEditorHash, float fixedTransitionMaximumDuration, int layerIndex, float fixedTimeOffset = 0f)
        {
            var stateIndex = GetStateIndex(stateNameHash, layerIndex, isEditorHash);
            CrossFadeInFixedTime(stateIndex, fixedTransitionMaximumDuration, layerIndex, fixedTimeOffset);
        }

        /// <summary>
        /// Creates a crossfade from the current state to any other state using times in seconds.
        /// This is always handled with an inertial blend.
        /// </summary>
        /// <param name="stateName">The name of the target state.</param>
        /// <param name="fixedTransitionMaximumDuration">The duration of the transition (in seconds).</param>
        /// <param name="layerIndex">The mecanim layer where the crossfade occurs.</param>
        /// <param name="fixedTimeOffset">The time of the target state (in seconds) to start at.</param>
        public void CrossFadeInFixedTime(in FixedString64Bytes stateName, float fixedTransitionMaximumDuration, int layerIndex, float fixedTimeOffset = 0f)
        {
            var stateIndex = GetStateIndex(in stateName, layerIndex);
            CrossFadeInFixedTime(stateIndex, fixedTransitionMaximumDuration, layerIndex, fixedTimeOffset);
        }

        /// <summary>
        /// Creates a crossfade from the current state to any other state using normalized times.
        /// This is always handled with an inertial blend.
        /// </summary>
        /// <param name="stateIndex">The index of the target state (fastest version).</param>
        /// <param name="normalizedTransitionMaximumDuration">The normalized duration of the transition.</param>
        /// <param name="layerIndex">The mecanim layer where the crossfade occurs.</param>
        /// <param name="normalizedTimeOffset">The normalized time of the target state to start at.</param>
        public void Crossfade(short stateIndex, float normalizedTransitionMaximumDuration, int layerIndex, float normalizedTimeOffset = 0f)
        {
            if (stateIndex > -1)
            {
                ref var layerData                      = ref m_controller.ValueRO.controller.Value.layers[layerIndex];
                ref var state                          = ref layerData.states[stateIndex];
                var     fixedTimeOffset                = normalizedTimeOffset * state.averageDuration;
                var     fixedTransitionMaximumDuration = normalizedTransitionMaximumDuration * state.averageDuration;

                CrossFadeInFixedTime(ref state, stateIndex, layerIndex, fixedTransitionMaximumDuration, fixedTimeOffset);
            }
            else
                UnityEngine.Debug.LogError("The target Mecanim state could not be found.");
        }

        /// <summary>
        /// Creates a crossfade from the current state to any other state using normalized times.
        /// This is always handled with an inertial blend.
        /// </summary>
        /// <param name="stateNameHash">The hashed name of the target state.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        /// <param name="normalizedTransitionMaximumDuration">The normalized duration of the transition.</param>
        /// <param name="layerIndex">The mecanim layer where the crossfade occurs.</param>
        /// <param name="normalizedTimeOffset">The normalized time of the target state to start at.</param>
        public void Crossfade(int stateNameHash, bool isEditorHash, float normalizedTransitionMaximumDuration, int layerIndex, float normalizedTimeOffset = 0f)
        {
            var stateIndex = GetStateIndex(stateNameHash, layerIndex, isEditorHash);
            Crossfade(stateIndex, normalizedTransitionMaximumDuration, layerIndex, normalizedTimeOffset);
        }

        /// <summary>
        /// Creates a crossfade from the current state to any other state using normalized times.
        /// This is always handled with an inertial blend.
        /// </summary>
        /// <param name="stateName">The name of the target state.</param>
        /// <param name="normalizedTransitionMaximumDuration">The normalized duration of the transition.</param>
        /// <param name="layerIndex">The mecanim layer where the crossfade occurs.</param>
        /// <param name="normalizedTimeOffset">The normalized time of the target state to start at.</param>
        public void Crossfade(FixedString64Bytes stateName, float normalizedTransitionMaximumDuration, int layerIndex, float normalizedTimeOffset = 0f)
        {
            var stateIndex = GetStateIndex(stateName, layerIndex);
            Crossfade(stateIndex, normalizedTransitionMaximumDuration, layerIndex, normalizedTimeOffset);
        }
        #endregion

        #region ClipInfo
        /// <summary>
        /// Returns a NativeList of all the TimedMecanimClipInfo in the current state of the given layer.
        /// This is always handled with an inertial blend.
        /// </summary>
        /// <param name="layerIndex">The layer index.</param>
        /// <param name="allocator">The allocator to use for the created NativeList.</param>
        /// <returns>
        /// An array of all the TimedMecanimClipInfo in the next state.
        /// </returns>
        public NativeList<TimedMecanimClipInfo> GetCurrentMecanimClipInfo(int layerIndex, Allocator allocator)
        {
            NativeList<TimedMecanimClipInfo> clipInfo = new NativeList<TimedMecanimClipInfo>(allocator);
            GetCurrentMecanimClipInfo(layerIndex, ref clipInfo);
            return clipInfo;
        }

        /// <summary>
        /// Adds all the TimedMecanimClipInfo in the current state of the given layer to the passed in list.
        /// </summary>
        /// <param name="layerIndex">The layer index.</param>
        /// <param name="clipInfo">The NativeList to populate with the clip info.</param>
        public void GetCurrentMecanimClipInfo(int layerIndex, ref NativeList<TimedMecanimClipInfo> clipInfo)
        {
            var     layer     = m_layers[layerIndex];
            ref var layerData = ref m_controller.ValueRO.controller.Value.layers[layerIndex];
            ref var state     = ref layerData.states[layer.currentStateIndex];
            MecanimInternalUtilities.AddLayerClipWeights(ref clipInfo,
                                                         ref layerData,
                                                         (short)layerIndex,
                                                         layer.currentStateIndex,
                                                         (short)math.select(layer.previousStateIndex, -3, layer.transitionIsInertialBlend),
                                                         m_parameters.AsNativeArray(),
                                                         layer.timeInState,
                                                         layer.transitionEndTimeInState,
                                                         layer.previousStateExitTime,
                                                         1f);
        }
        #endregion

        #region Parameters
        /// <summary>
        /// Returns the value of the given float parameter.
        /// </summary>
        /// <param name="index">The parameter index in the mecanim controller's list of parameters. (fastest)</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public float GetFloat(short index) => GetFloatParameter(index);

        /// <summary>
        /// Returns the value of the given float parameter.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public float GetFloat(FixedString64Bytes name) => GetFloatParameter(GetParameterIndex(name));

        /// <summary>
        /// Returns the value of the given float parameter.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public float GetFloat(int nameHash, bool isEditorHash) => GetFloatParameter(GetParameterIndex(nameHash, isEditorHash));

        /// <summary>
        /// Returns the value of the given int parameter.
        /// </summary>
        /// <param name="index">The parameter index in the mecanim controller's list of parameters. (fastest)</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public int GetInt(short index) => GetIntParameter(index);

        /// <summary>
        /// Returns the value of the given int parameter.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public int GetInt(FixedString64Bytes name) => GetIntParameter(GetParameterIndex(name));

        /// <summary>
        /// Returns the value of the given int parameter.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public int GetInt(int nameHash, bool isEditorHash) => GetIntParameter(GetParameterIndex(nameHash, isEditorHash));

        /// <summary>
        /// Returns the value of the given bool parameter.
        /// </summary>
        /// <param name="index">The parameter index in the mecanim controller's list of parameters. (fastest)</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public bool GetBool(short index) => GetBoolParameter(index);

        /// <summary>
        /// Returns the value of the given bool parameter.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public bool GetBool(FixedString64Bytes name) => GetBoolParameter(GetParameterIndex(name));

        /// <summary>
        /// Returns the value of the given bool parameter.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public bool GetBool(int nameHash, bool isEditorHash) => GetBoolParameter(GetParameterIndex(nameHash, isEditorHash));

        /// <summary>
        /// Returns the value of the given Trigger parameter.
        /// </summary>
        /// <param name="index">The parameter index in the mecanim controller's list of parameters. (fastest)</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public bool GetTrigger(short index) => GetTriggerParameter(index);

        /// <summary>
        /// Returns the value of the given Trigger parameter.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public bool GetTrigger(FixedString64Bytes name) => GetTriggerParameter(GetParameterIndex(name));

        /// <summary>
        /// Returns the value of the given Trigger parameter.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        /// <returns>
        /// The value of the parameter.
        /// </returns>
        public bool GetTrigger(int nameHash, bool isEditorHash) => GetTriggerParameter(GetParameterIndex(nameHash, isEditorHash));

        /// <summary>
        /// Send float values to affect transitions.
        /// </summary>
        /// <param name="name">The parameter index in the mecanim controller's list of parameters.</param>
        /// <param name="value">The new parameter value.</param>
        public void SetFloat(short index, float value) => SetFloatParameter(index, value);

        /// <summary>
        /// Send float values to affect transitions.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The new parameter value.</param>
        public void SetFloat(FixedString64Bytes name, float value) => SetFloatParameter(GetParameterIndex(name), value);

        /// <summary>
        /// Send float values to affect transitions.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="value">The new parameter value.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        public void SetFloat(int nameHash, float value, bool isEditorHash) => SetFloatParameter(GetParameterIndex(nameHash, isEditorHash), value);

        /// <summary>
        /// Send int values to affect transitions.
        /// </summary>
        /// <param name="name">The parameter index in the mecanim controller's list of parameters.</param>
        /// <param name="value">The new parameter value.</param>
        public void SetInt(short index, int value) => SetIntParameter(index, value);

        /// <summary>
        /// Send int values to affect transitions.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The new parameter value.</param>
        public void SetInt(FixedString64Bytes name, int value) => SetIntParameter(GetParameterIndex(name), value);

        /// <summary>
        /// Send int values to affect transitions.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="value">The new parameter value.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        public void SetInt(int nameHash, int value, bool isEditorHash) => SetIntParameter(GetParameterIndex(nameHash, isEditorHash), value);

        /// <summary>
        /// Send bool values to affect transitions.
        /// </summary>
        /// <param name="name">The parameter index in the mecanim controller's list of parameters.</param>
        /// <param name="value">The new parameter value.</param>
        public void SetBool(short index, bool value) => SetBoolParameter(index, value);

        /// <summary>
        /// Send bool values to affect transitions.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The new parameter value.</param>
        public void SetBool(FixedString64Bytes name, bool value) => SetBoolParameter(GetParameterIndex(name), value);

        /// <summary>
        /// Send bool values to affect transitions.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="value">The new parameter value.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        public void SetBool(int nameHash, bool value, bool isEditorHash) => SetBoolParameter(GetParameterIndex(nameHash, isEditorHash), value);

        /// <summary>
        /// Enables the trigger to affect transitions.
        /// </summary>
        /// <param name="name">The parameter index in the mecanim controller's list of parameters.</param>
        public void SetTrigger(short index) => SetTriggerParameter(index, true);

        /// <summary>
        /// Enables the trigger to affect transitions.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        public void SetTrigger(FixedString64Bytes name) => SetTriggerParameter(GetParameterIndex(name), true);

        /// <summary>
        /// Enables the trigger to affect transitions.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        public void SetTrigger(int nameHash, bool isEditorHash) => SetTriggerParameter(GetParameterIndex(nameHash, isEditorHash), true);

        /// <summary>
        /// Disables the trigger to not affect transitions.
        /// </summary>
        /// <param name="name">The parameter index in the mecanim controller's list of parameters.</param>
        public void ClearTrigger(short index) => SetTriggerParameter(index, false);

        /// <summary>
        /// Disables the trigger to not affect transitions.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        public void ClearTrigger(FixedString64Bytes name) => SetTriggerParameter(GetParameterIndex(name), false);

        /// <summary>
        /// Disables the trigger to not affect transitions.
        /// </summary>
        /// <param name="nameHash">The hashed parameter name.</param>
        /// <param name="isEditorHash">True if the hash was the baked editor hash, false if the hash is FixedString64Bytes.GetHashCode()</param>
        public void ClearTrigger(int nameHash, bool isEditorHash) => SetTriggerParameter(GetParameterIndex(nameHash, isEditorHash), false);
        #endregion
    }
}

