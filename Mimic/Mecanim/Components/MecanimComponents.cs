using System.Runtime.InteropServices;
using Latios.Kinemation;
using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Mimic.Addons.Mecanim
{
    #region Components
    /// <summary>
    /// Represents the animator controller, containing animation clips and controller configuration.
    /// Prefer to use MecanimAspect instead of this component directly.
    /// </summary>
    public struct MecanimController : IComponentData, IEnableableComponent
    {
        /// <summary>
        /// The animation clips that the controller can play
        /// </summary>
        public BlobAssetReference<SkeletonClipSetBlob> clips;
        /// <summary>
        /// The configuration of the animator controller
        /// </summary>
        public BlobAssetReference<MecanimControllerBlob> controller;
        /// <summary>
        /// The speed at which the animator controller will play and progress through states
        /// </summary>
        public float speed;
        /// <summary>
        /// The time since the last inertial blend start
        /// </summary>
        public float timeSinceLastInertialBlendStart;
        /// <summary>
        /// The maximum time the inertial blend should last for a newly triggered inertial blend.
        /// The value is fudged and used for tracking the inertial blend duration once a triggered
        /// inertial blend is acknowledged, but is safe to overwrite again when triggering a new
        /// inertial blend.
        /// </summary>
        public float newInertialBlendDuration;
        /// <summary>
        /// Whether or not to apply root motion to the root bone
        /// </summary>
        public bool applyRootMotion;
        /// <summary>
        /// If true, the skeleton has an inertial blend started
        /// </summary>
        public bool isInInertialBlend;
        /// <summary>
        /// If set to true, the skeleton will start a new inertial blend
        /// </summary>
        public bool triggerStartInertialBlend;
    }

    /// <summary>
    /// Contains entity references to meshes that can be transformed by blend shapes.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct BlendShapeClipSet : IBufferElementData
    {
        public Entity meshEntity;
        /// <summary>
        /// The blend shape parameter values for each clip
        /// </summary>
        public BlobAssetReference<ParameterClipSetBlob> clips;
    }

    /// <summary>
    /// Contains the stateful data of an animator controller layer.
    /// Prefer to use MecanimAspect instead of this buffer directly.
    /// </summary>
    [InternalBufferCapacity(1)]
    public struct MecanimLayerStateMachineStatus : IBufferElementData
    {
        // Todo: Move TimeFragment here and add previousStatePreviousTimeInState

        /// <summary>
        /// The time in the current state in seconds.  This controls at what point in time an animation clip is sampled.
        /// </summary>
        public float timeInState;
        /// <summary>
        /// The exit time of the previous state in seconds.  This controls the blending between last and current states.
        /// </summary>
        public float previousStateExitTime;
        /// <summary>
        /// The time relative to the start of the current state in seconds at which the transition from the previous state to the current state ends.
        /// </summary>
        public float transitionEndTimeInState;
        /// <summary>
        /// The index of the current state
        /// </summary>
        public short currentStateIndex;
        /// <summary>
        /// The index of the previous state
        /// </summary>
        public short previousStateIndex;
        /// <summary>
        /// The index of the currently active transition from the last state.  This value will be -1 if no transition is active.  Used for transition interrupts.
        /// </summary>
        public short currentTransitionIndex;
        /// <summary>
        /// The current transition is an AnyState transition
        /// </summary>
        public bool currentTransitionIsAnyState;
        /// <summary>
        /// The current transition uses inertial blending. When the transition is an inertial blend transition,
        /// we completely ignore the previous state, EXCEPT for interruption rules. If it weren't for interruptions,
        /// transitions would be instantaneous for inertial blending.
        /// </summary>
        public bool transitionIsInertialBlend;
        /// <summary>
        /// If true, the layer is in a state transition, either predefined or custom
        /// </summary>
        public bool isInTransition => currentTransitionIndex != -1;
        /// <summary>
        /// If true, the layer is in a custom state transition which should always use inertial blending
        /// </summary>
        public bool isCustomTransition => currentTransitionIndex != -2;
    }

    /// <summary>
    /// An animator parameter value.  The index of this state in the buffer is synchronized with the index of the parameter in the controller blob asset reference
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    [InternalBufferCapacity(0)]
    public struct MecanimParameter : IBufferElementData
    {
        [FieldOffset(0)]
        public float floatParam;

        [FieldOffset(0)]
        public int intParam;

        [FieldOffset(0)]
        public bool boolParam;

        [FieldOffset(0)]
        public bool triggerParam;
    }

    /// <summary>
    /// An active animation clip event.  The indices refer to an animation clip event in a baked SkeletonClipSetBlob.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct MecanimActiveClipEvent : IBufferElementData
    {
        public int   nameHash;
        public int   parameter;
        public short clipIndex;
        public short eventIndex;
    }

    [InternalBufferCapacity(0)]
    public struct ExposedSkeletonInertialBlendState : IBufferElementData
    {
        public TransformQvvs                  previous;
        public TransformQvvs                  twoAgo;
        public InertialBlendingTransformState blendState;
    }

    #endregion

    #region Blob Assets

    /// <summary>
    /// Represents an animator transition condition
    /// </summary>
    public partial struct MecanimConditionBlob
    {
        public float         threshold;
        public short         parameterIndex;
        public ConditionType mode;

        public enum ConditionType : byte
        {
            If = 1,
            IfNot = 2,
            Greater = 3,
            Less = 4,
            Equals = 6,
            NotEqual = 7,
        }
    }

    public struct MecanimStateTransitionBlob
    {
        // See UnityEngine.Animations.AnimatorTransition
        public BlobArray<MecanimConditionBlob> conditions;
        public float                           duration;
        /// <summary>
        /// The time at which the destination state will start in the clip in seconds.
        /// </summary>
        public float offset;
        /// <summary>
        /// The normalized (0 - 1) time in the clip at which a transition is allowed to start.
        /// </summary>
        /// <remarks>
        /// If AnimatorStateTransition.hasExitTime is true, exitTime represents the exact time at which the transition can take effect.
        /// This is represented in normalized time, so for example an exit time of 0.75 means that on the first frame where 75% of
        /// the animation has played, the Exit Time condition will be true. On the next frame, the condition will be false.
        /// For looped animations, transitions with exit times smaller than 1 will be evaluated every loop, so you can use this to
        /// time your transition with the proper timing in the animation, every loop.
        /// Transitions with exit times greater than one will be evaluated only once, so they can be used to exit at a specific time,
        /// after a fixed number of loops. For example, a transition with an exit time of 3.5 will be evaluated once, after three and
        /// a half loops.
        /// </remarks>
        public float              exitTime;
        public short              destinationStateIndex;
        public short              originStateIndex;
        public InterruptionSource interruptionSource;
        public bool               hasExitTime;
        public bool               hasFixedDuration;
        public bool               orderedInterruption;

        public enum InterruptionSource : byte
        {
            None,
            Source,
            Destination,
            SourceThenDestination,
            DestinationThenSource,
        }
    }

    public struct MecanimStateBlob
    {
        // See UnityEditor.Animations.AnimatorState
        public FixedString64Bytes name;
        public int                nameHash;  // name.GetHashCode()
        public int                editorNameHash;  // nameHash from UnityEditor.Animations.AnimatorState
        public float              speed;
        public float              cycleOffset;
        public bool               mirror;  // Todo: Unused
        public bool               ikOnFeet;  // Todo: Unused

        public short speedMultiplierParameterIndex;
        public short cycleOffsetParameterIndex;
        public short mirrorParameterIndex;
        public short timeParameterIndex;

        public BlobArray<MecanimStateTransitionBlob> transitions;

        // See UnityEngine.Motion (mostly undocumented)
        public float  averageDuration;
        public float3 averageSpeed;  // Todo: Unused
        public float  averageAngularSpeed;  // Todo: Unused
        public float  apparentSpeed;  // Todo: Unused
        public bool   isHumanMotion;  // Todo: Unused
        public bool   isLooping;  // Todo: Unused
        public bool   legacy;  // Todo: Unused - Would this ever matter?

        // A Motion can either be a single clip or a blend tree.
        public bool isBlendTree;

        // Only valid if not a blend tree and has a valid clip, otherwise -1.
        public short clipIndex;

        // Only valid if a blend tree
        // See UnityEditor.Animations.BlendTree
        public BlobArray<short>  childMotionIndices;
        public BlobArray<short>  directBlendParameterIndices;
        public BlobArray<float>  childMotionThresholds;
        public BlobArray<float2> childMotionPositions;

        public BlendTreeType blendTreeType;
        public short         blendParameterIndex;
        public short         blendParameterYIndex;
        public float         minThreshold;
        public float         maxThreshold;

        public enum BlendTreeType
        {
            Simple1D,
            SimpleDirectional2D,
            FreeformDirectional2D,
            FreeformCartesian2D,
            Direct
        }
    }

    public struct MecanimControllerLayerBlob
    {
        public FixedString64Bytes name;
        public float              defaultWeight;
        public short              defaultStateIndex;
        public short              syncedLayerIndex;  // Todo: Unused
        public bool               syncedLayerAffectsTiming;  // Todo: Unused
        public bool               ikPass;  // todo: Unused

        public LayerBlendingMode blendingMode;

        public BlobArray<MecanimStateBlob>           states;
        public BlobArray<MecanimStateBlob>           childMotions;
        public BlobArray<MecanimStateTransitionBlob> anyStateTransitions;

        public enum LayerBlendingMode : byte
        {
            Override,
            Additive,
        }
    }

    public struct MecanimParameterBlob
    {
        public AnimatorControllerParameterType parameterType;
        public FixedString64Bytes              name;
        public int                             nameHash;  // name.GetHashCode()
        public int                             editorNameHash;  // nameHash from UnityEditor.Animations.AnimatorState
    }

    public struct MecanimControllerBlob
    {
        public FixedString128Bytes                   name;
        public BlobArray<MecanimParameterBlob>       parameters;
        public BlobArray<MecanimControllerLayerBlob> layers;
    }

    #endregion

    // Todo: Remove from entity
    [InternalBufferCapacity(0)]
    public struct TimedMecanimClipInfo : IBufferElementData
    {
        /// <summary>
        /// The index of the clip contained within the Mecanim Controller.
        /// </summary>
        public int mecanimClipIndex;
        /// <summary>
        /// The blend weight of the clip
        /// </summary>
        public float weight;
        /// <summary>
        /// The absolute motion time of the clip
        /// </summary>
        public float motionTime;
        /// <summary>
        /// The amount of time this clip will have remained active from the previous frame.
        /// This will be volatile during transitions.  Populated during the state machine update.
        /// Will be delta time if the same clip is active between frames.
        /// </summary>
        public float timeFragment;
        /// <summary>
        /// The index of the layer on which the clip is used
        /// </summary>
        public short layerIndex;
        /// <summary>
        /// The index of the state on which the clip is active.  This will be used to calculate time fragments.
        /// </summary>
        public short stateIndex;

        public TimedMecanimClipInfo(ref MecanimStateBlob state, NativeArray<MecanimParameter> parameters, float weightFactor, float timeInState, short layerIndex, short stateIndex)
        {
            mecanimClipIndex = state.clipIndex;
            weight           = weightFactor;
            var cycleOffset  = state.cycleOffsetParameterIndex != -1 ?
                               parameters[state.cycleOffsetParameterIndex].floatParam :
                               state.cycleOffset;
            var speed = state.speedMultiplierParameterIndex != -1 ?
                        parameters[state.speedMultiplierParameterIndex].floatParam * state.speed :
                        state.speed;
            motionTime = state.timeParameterIndex != -1 ?
                         parameters[state.timeParameterIndex].floatParam :
                         (timeInState * speed) + cycleOffset;

            this.layerIndex = layerIndex;
            this.stateIndex = stateIndex;
            timeFragment    = 0;
        }
    }
}

