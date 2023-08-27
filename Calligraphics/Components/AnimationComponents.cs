using System;
using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics
{
    /// <summary>
    /// The text unit to which the transition will apply
    /// </summary>
    public enum TransitionTextUnitScope : byte
    {
        Glyph,
        Word,
        Line,
        All
    }

    /// <summary>
    /// The value to transition
    /// </summary>
    public enum GlyphProperty : byte
    {
        Opacity,
        Scale,
        Color,
        PositionNoise,
        RotationNoise,
        Position,
        Rotation,
    }

    /// <summary>
    /// The behavior to of the transition when its transition duration is reached
    /// </summary>
    [Flags]
    public enum TransitionEndBehavior : byte
    {
        Revert = 1,
        KeepFinalValue = 2,
        Loop = 4
    }

    /// <summary>
    /// Each element corresponds to a unique transition animation played on the text.
    /// Each element contains both parameters and state.
    /// Transitions are automatically removed upon finishing.
    /// </summary>
    [ChunkSerializable]
    [InternalBufferCapacity(0)]
    [StructLayout(LayoutKind.Explicit)]
    public struct TextAnimationTransition : IBufferElementData
    {
        #region Dynamic State
        /// <summary>
        /// The current time of the transition.  This will reset at the end of the transition if the transition loops.
        /// </summary>
        [FieldOffset(0)] public float currentTime;
        /// <summary>
        /// The current loop iteration
        /// </summary>
        [FieldOffset(4)] public int currentIteration;
        #endregion
        /// <summary>
        /// The duration of the transition.  On expiration, the transition will behave according to the end behavior.
        /// </summary>
        [FieldOffset(8)] public float transitionDuration;
        /// <summary>
        /// Delay time for the transition.  Useful for setting up a sequence of transitions.
        /// </summary>
        [FieldOffset(12)] public float transitionDelay;
        /// <summary>
        /// Delay time for looping.  Useful for setting up a sequence of looped transitions.
        /// </summary>
        [FieldOffset(16)] public float loopDelay;
        /// <summary>
        /// How many times the transition should loop.
        /// </summary>
        [FieldOffset(20)] public int loopCount;
        /// <summary>
        /// The start index of the transition scope.  Index would refer to glyph, word, or line depending on the unit scope.
        /// </summary>
        [FieldOffset(24)] public int startIndex;
        /// <summary>
        /// The end index of the transition scope.  Index would refer to glyph, word, or line depending on the unit scope.
        /// End index is inclusive.
        /// </summary>
        [FieldOffset(28)] public int endIndex;
        /// <summary>
        /// The text unit to which the transition will apply
        /// </summary>
        [FieldOffset(32)] public TransitionTextUnitScope scope;
        /// <summary>
        /// The interpolation type of the transition (linear, ease in, ease out, etc.)
        /// </summary>
        [FieldOffset(33)] public InterpolationType interpolation;
        /// <summary>
        /// The property to transition
        /// </summary>
        [FieldOffset(34)] public GlyphProperty glyphProperty;
        /// <summary>
        /// The behavior to of the transition when its transition duration is reached
        /// </summary>
        [FieldOffset(35)] public TransitionEndBehavior endBehavior;

        //Opacity
        [FieldOffset(36)] public byte startValueByte;
        [FieldOffset(37)] public byte endValueByte;

        //Scale, Position
        [FieldOffset(36)] public float2 startValueFloat2;
        [FieldOffset(44)] public float2 endValueFloat2;

        //Rotation
        [FieldOffset(36)] public float startValueFloat;
        [FieldOffset(40)] public float endValueFloat;

        //Color
        [FieldOffset(36)] public Color32 startValueBlColor;
        [FieldOffset(40)] public Color32 endValueBlColor;
        [FieldOffset(44)] public Color32 startValueTrColor;
        [FieldOffset(48)] public Color32 endValueTrColor;
        [FieldOffset(52)] public Color32 startValueBrColor;
        [FieldOffset(56)] public Color32 endValueBrColor;
        [FieldOffset(60)] public Color32 startValueTlColor;
        [FieldOffset(64)] public Color32 endValueTlColor;

        [FieldOffset(36)] public float3 startValueFloat3;
        [FieldOffset(48)] public float3 endValueFloat3;

        //Randomized Noise Values
        [FieldOffset(36)] public IntPtr startValuesBuffer;
        [FieldOffset(44)] public IntPtr endValuesBuffer;
        [FieldOffset(52)] public int    valuesBufferLength;

        //PositionNoise
        [FieldOffset(56)] public float2 noiseScaleFloat2;

        //RotationNoise
        [FieldOffset(56)] public float noiseScaleFloat;
    }
}

