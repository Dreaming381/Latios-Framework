using Latios.Calligraphics.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics
{
    internal struct ColorTransitionProvider : ITransitionProvider
    {
        public void Initialize(ref TextAnimationTransition transition, ref Rng.RngSequence rng, GlyphMapper glyphMapper)
        {
            if (transition.currentIteration > 0 && (transition.endBehavior & TransitionEndBehavior.KeepFinalValue) == TransitionEndBehavior.KeepFinalValue)
            {
                (transition.startValueBlColor, transition.endValueBlColor) = (transition.endValueBlColor, transition.startValueBlColor);
                (transition.startValueTrColor, transition.endValueTrColor) = (transition.endValueTrColor, transition.startValueTrColor);
                (transition.startValueBrColor, transition.endValueBrColor) = (transition.endValueBrColor, transition.startValueBrColor);
                (transition.startValueTlColor, transition.endValueTlColor) = (transition.endValueTlColor, transition.startValueTlColor);
            }
        }

        public void SetValue(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                             TextAnimationTransition transition,
                             GlyphMapper glyphMapper,
                             int startIndex,
                             int endIndex,
                             float normalizedTime)
        {
            if (transition.currentTime >= transition.transitionDelay)
            {
                Color32 blValue = Interpolation.Interpolate(transition.startValueBlColor, transition.endValueBlColor, normalizedTime, transition.interpolation);
                Color32 trValue = Interpolation.Interpolate(transition.startValueTrColor, transition.endValueTrColor, normalizedTime, transition.interpolation);
                Color32 brValue = Interpolation.Interpolate(transition.startValueBrColor, transition.endValueBrColor, normalizedTime, transition.interpolation);
                Color32 tlValue = Interpolation.Interpolate(transition.startValueTlColor, transition.endValueTlColor, normalizedTime, transition.interpolation);

                //Apply new values
                for (int i = startIndex; i <= math.min(endIndex, renderGlyphs.Length - 1); i++)
                {
                    var renderGlyph = renderGlyphs[i];
                    SetColorValue(ref renderGlyph, blValue, brValue, tlValue, trValue);
                    renderGlyphs[i] = renderGlyph;
                }
            }
        }

        public void SetColorValue(ref RenderGlyph glyph, Color32 blColor, Color32 brColor, Color32 tlColor, Color32 trColor)
        {
            glyph.blColor = blColor;
            glyph.brColor = brColor;
            glyph.tlColor = tlColor;
            glyph.trColor = trColor;
        }
    }
}

