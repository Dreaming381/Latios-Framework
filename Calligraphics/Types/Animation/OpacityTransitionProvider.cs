using Latios.Calligraphics.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics
{
    internal struct OpacityTransitionProvider : ITransitionProvider
    {
        public void Initialize(ref TextAnimationTransition transition, ref Rng.RngSequence rng, GlyphMapper glyphMapper)
        {
            if (transition.currentIteration > 0 && (transition.endBehavior & TransitionEndBehavior.KeepFinalValue) == TransitionEndBehavior.KeepFinalValue)
            {
                (transition.startValueByte, transition.endValueByte) = (transition.endValueByte, transition.startValueByte);
            }
        }

        public void SetValue(ref DynamicBuffer<RenderGlyph> renderGlyphs, TextAnimationTransition transition, GlyphMapper glyphMapper, int startIndex, int endIndex, float normalizedTime)
        {
            var startValue = new Color32(0, 0, 0, transition.startValueByte);
            var endValue  = new Color32(0, 0, 0, transition.endValueByte);
            Color32 value = startValue;

            if (transition.currentTime >= transition.transitionDelay)
            {
                value = Interpolation.Interpolate(startValue, endValue, normalizedTime, transition.interpolation);
            }

            //Apply new values
            for (int i = startIndex; i <= math.min(endIndex, renderGlyphs.Length - 1); i++)
            {
                var renderGlyph = renderGlyphs[i];
                SetOpacityValue(ref renderGlyph, value.a);
                renderGlyphs[i] = renderGlyph;
            }
        }
        
        private void SetOpacityValue(ref RenderGlyph glyph, byte value)
        {
            var blColor       = glyph.blColor;
            var brColor       = glyph.brColor;
            var tlColor       = glyph.tlColor;
            var trColor       = glyph.trColor;

            blColor.a = value;
            brColor.a = value;
            tlColor.a = value;
            trColor.a = value;
                                
            glyph.blColor = blColor;
            glyph.brColor = brColor;
            glyph.tlColor = tlColor;
            glyph.trColor = trColor;
        }
    }
}