using System;
using Latios.Calligraphics.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    internal struct NoiseRotationTransitionProvider : ITransitionProvider
    {
        public unsafe void Initialize(ref TextAnimationTransition transition, ref Rng.RngSequence rng, GlyphMapper glyphMapper)
        {
            int valueCount = transition.endIndex - transition.startIndex + 1;
            
            if (transition.currentIteration < transition.loopCount && (transition.endBehavior & TransitionEndBehavior.Loop) == TransitionEndBehavior.Loop)
            {
                int valueSize = UnsafeUtility.SizeOf<float>();
                int valueAlignment = UnsafeUtility.AlignOf<float>();
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
                        var endValue = new float();
                        UnsafeUtility.WriteArrayElement(transition.endValuesBuffer.ToPointer(), i, endValue);
                    }
                }

                for (int i = 0; i < valueCount; i++)
                {
                    //Write the old end to the start
                    var oldEndValue = UnsafeUtility.ReadArrayElement<float>(transition.endValuesBuffer.ToPointer(), i);
                    UnsafeUtility.WriteArrayElement(transition.startValuesBuffer.ToPointer(), i, oldEndValue);
                    var noiseSeed = rng.NextFloat2(new float2(-1f, -1f), new float2(1f, 1f));
                    var endValue = noise.cnoise(noiseSeed) * transition.noiseScaleFloat;
                    //Write the new end
                    UnsafeUtility.WriteArrayElement(transition.endValuesBuffer.ToPointer(), i, endValue);
                    var readStartValue =
                        UnsafeUtility.ReadArrayElement<float>(transition.startValuesBuffer.ToPointer(), i);
                    var readEndValue =
                        UnsafeUtility.ReadArrayElement<float>(transition.endValuesBuffer.ToPointer(), i);
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
            NativeArray<float> values      = new NativeArray<float>(valuesCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            if (transition.currentTime >= transition.transitionDelay)
            {
                for (int i = 0; i < valuesCount; i++)
                {
                    var startValue = UnsafeUtility.ReadArrayElement<float>(transition.startValuesBuffer.ToPointer(), i);
                    var endValue   =
                        UnsafeUtility.ReadArrayElement<float>(transition.endValuesBuffer.ToPointer(), i);
                    values[i] = Interpolation.Interpolate(startValue, endValue, normalizedTime,
                                                          transition.interpolation);
                }
            }
            else
            {
                for (int i = 0; i < valuesCount; i++)
                {
                    values[i] = UnsafeUtility.ReadArrayElement<float>(transition.startValuesBuffer.ToPointer(), i);
                }
            }

            //Apply new values
            for (int i = transition.startIndex; i <= transition.endIndex; i++)
            {
                switch (transition.scope)
                {
                    case TransitionTextUnitScope.Glyph:
                        SetRotationValue(ref renderGlyphs, i, i, values[transition.endIndex - i]);
                        break;
                    case TransitionTextUnitScope.Word:
                        SetRotationValue(ref renderGlyphs, glyphMapper.GetGlyphStartIndexAndCountForWord(i).x, glyphMapper.GetGlyphStartIndexAndCountForWord(i).y - 1, values[transition.endIndex - glyphMapper.GetGlyphStartIndexAndCountForWord(i).x]);
                        break;
                    case TransitionTextUnitScope.Line:
                        SetRotationValue(ref renderGlyphs, glyphMapper.GetGlyphStartIndexAndCountForLine(i).x, glyphMapper.GetGlyphStartIndexAndCountForLine(i).y - 1, values[transition.endIndex - glyphMapper.GetGlyphStartIndexAndCountForLine(i).x]);
                        break;
                    default:
                        SetRotationValue(ref renderGlyphs, i, i, values[0]);
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

        private void SetRotationValue(ref DynamicBuffer<RenderGlyph> renderGlyphs, int startIndex, int endIndex, float value)
        {
            //TODO: Implement word and line scope to adjust rotation ccw of each member glyph appropriately
            
            var min = renderGlyphs[startIndex].blPosition;
            var max = renderGlyphs[endIndex].trPosition;

            var center = max + min / 2f;
            
            for (int i = startIndex; i <= endIndex; i++)
            {
                var glyph = renderGlyphs[i];

                var glyphCenter = glyph.trPosition + glyph.blPosition / 2f;
                
                var fromCenter = glyphCenter - center;
                var fromCenter3 = new float3(fromCenter.x, fromCenter.y, 0f);
                var fromCenter3Rotated = math.mul(quaternion.Euler(new float3(0f, 0f, value)), fromCenter3);
                var fromCenterRotated = new float2(fromCenter3Rotated.x, fromCenter3Rotated.y);
                var rotatedDelta = fromCenterRotated - fromCenter;

                glyph.blPosition +=  rotatedDelta;
                glyph.trPosition +=  rotatedDelta;
                glyph.rotationCCW = value;
                
                renderGlyphs[i] = glyph;
            }
        }
    }
}

