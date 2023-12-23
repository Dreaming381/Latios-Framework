using System;
using System.Runtime.InteropServices;
using Latios.Calligraphics.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    internal struct NoisePositionTransitionProvider : ITransitionProvider
    {
        public unsafe void Initialize(ref TextAnimationTransition transition, ref Rng.RngSequence rng, GlyphMapper glyphMapper)
        {
            int valueCount = transition.endIndex - transition.startIndex + 1;
            
            if (transition.currentIteration < transition.loopCount && (transition.endBehavior & TransitionEndBehavior.Loop) == TransitionEndBehavior.Loop)
            {
                int valueSize = UnsafeUtility.SizeOf<float2>();
                int valueAlignment = UnsafeUtility.AlignOf<float2>();
                int bufferSize = valueCount * valueSize;

                if (transition.valuesBufferLength != valueCount)
                {
                    transition.valuesBufferLength = valueCount;
                    if (transition.startValuesBuffer != IntPtr.Zero)
                    {
                        UnsafeUtility.Free(transition.startValuesBuffer.ToPointer(), Allocator.Persistent);
                    }

                    transition.startValuesBuffer =
                        (IntPtr)UnsafeUtility.Malloc(bufferSize, valueAlignment, Allocator.Persistent);

                    if (transition.endValuesBuffer != IntPtr.Zero)
                    {
                        UnsafeUtility.Free(transition.endValuesBuffer.ToPointer(), Allocator.Persistent);
                    }

                    transition.endValuesBuffer =
                        (IntPtr)UnsafeUtility.Malloc(bufferSize, valueAlignment, Allocator.Persistent);

                    //Initialize the end values
                    for (int i = 0; i < valueCount; i++)
                    {
                        //Write the old end to the start
                        var endValue = new float2();
                        UnsafeUtility.WriteArrayElement(transition.endValuesBuffer.ToPointer(), i, endValue);
                    }
                }

                for (int i = 0; i < valueCount; i++)
                {
                    //Write the old end to the start
                    var oldEndValue = UnsafeUtility.ReadArrayElement<float2>(transition.endValuesBuffer.ToPointer(), i);
                    UnsafeUtility.WriteArrayElement(transition.startValuesBuffer.ToPointer(), i, oldEndValue);
                    var noiseSeedX = rng.NextFloat2(new float2(-1f, -1f), new float2(1f, 1f));
                    var noiseSeedY = rng.NextFloat2(new float2(-1f, -1f), new float2(1f, 1f));
                    var endValue = new float2(noise.cnoise(noiseSeedX) * transition.noiseScaleFloat2.x,
                        noise.cnoise(noiseSeedY) * transition.noiseScaleFloat2.y);
                    //Write the new end
                    UnsafeUtility.WriteArrayElement(transition.endValuesBuffer.ToPointer(), i, endValue);
                    var readStartValue =
                        UnsafeUtility.ReadArrayElement<float2>(transition.startValuesBuffer.ToPointer(), i);
                    var readEndValue =
                        UnsafeUtility.ReadArrayElement<float2>(transition.endValuesBuffer.ToPointer(), i);
                }
            }
        }

        public unsafe void SetValue(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                                    TextAnimationTransition transition,
                                    GlyphMapper glyphMapper,
                                    int startIndex,
                                    int endIndex,
                                    float normalizedTime)
        {
            var                 valuesCount = transition.endIndex - transition.startIndex + 1;
            NativeArray<float2> values      = new NativeArray<float2>(valuesCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            if (transition.currentTime >= transition.transitionDelay)
            {
                for (int i = 0; i < valuesCount; i++)
                {
                    var startValue = UnsafeUtility.ReadArrayElement<float2>(transition.startValuesBuffer.ToPointer(), i);
                    var endValue   =
                        UnsafeUtility.ReadArrayElement<float2>(transition.endValuesBuffer.ToPointer(), i);
                    values[i] = Interpolation.Interpolate(startValue, endValue, normalizedTime,
                                                          transition.interpolation);
                }
            }
            else
            {
                for (int i = 0; i < valuesCount; i++)
                {
                    values[i] = UnsafeUtility.ReadArrayElement<float2>(transition.startValuesBuffer.ToPointer(), i);
                }
            }

            //Apply new values
            for (int i = transition.startIndex; i <= transition.endIndex; i++)
            {
                switch (transition.scope)
                {
                    case TransitionTextUnitScope.Glyph:
                        SetPositionValue(ref renderGlyphs, i, i, values[transition.endIndex - i]);
                        break;
                    case TransitionTextUnitScope.Word:
                        SetPositionValue(ref renderGlyphs, glyphMapper.GetGlyphStartIndexAndCountForWord(i).x, glyphMapper.GetGlyphStartIndexAndCountForWord(i).y - 1, values[transition.endIndex - glyphMapper.GetGlyphStartIndexAndCountForWord(i).x]);
                        break;
                    case TransitionTextUnitScope.Line:
                        SetPositionValue(ref renderGlyphs, glyphMapper.GetGlyphStartIndexAndCountForLine(i).x, glyphMapper.GetGlyphStartIndexAndCountForLine(i).y - 1, values[transition.endIndex - glyphMapper.GetGlyphStartIndexAndCountForLine(i).x]);
                        break;
                    default:
                        SetPositionValue(ref renderGlyphs, i, i, values[0]);
                        break;
                }
            }
        }

        public unsafe void DisposeTransition(ref TextAnimationTransition transition)
        {
            if (transition.startValuesBuffer != IntPtr.Zero)
            {
                UnsafeUtility.Free(transition.startValuesBuffer.ToPointer(), Allocator.Persistent);
                transition.startValuesBuffer = IntPtr.Zero;
            }

            if (transition.endValuesBuffer != IntPtr.Zero)
            {
                UnsafeUtility.Free(transition.endValuesBuffer.ToPointer(), Allocator.Persistent);
                transition.endValuesBuffer = IntPtr.Zero;
            }
        }

        private void SetPositionValue(ref DynamicBuffer<RenderGlyph> renderGlyphs, int startIndex, int endIndex, float2 value)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                var glyph = renderGlyphs[i];
                
                var blPosition = glyph.blPosition + value;
                var trPosition = glyph.trPosition + value;

                glyph.blPosition = blPosition;
                glyph.trPosition = trPosition;
                
                renderGlyphs[i] = glyph;
            }
        }
    }
}

