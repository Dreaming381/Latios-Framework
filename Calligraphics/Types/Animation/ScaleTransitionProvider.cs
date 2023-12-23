using Latios.Calligraphics.Rendering;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    internal struct ScaleTransitionProvider : ITransitionProvider
    {
        public void Initialize(ref TextAnimationTransition transition, ref Rng.RngSequence rng, GlyphMapper glyphMapper)
        {
            if (transition.currentIteration > 0 && (transition.endBehavior & TransitionEndBehavior.KeepFinalValue) == TransitionEndBehavior.KeepFinalValue)
            {
                (transition.startValueFloat2, transition.endValueFloat2) = (transition.endValueFloat2, transition.startValueFloat2);
            }
        }

        public void SetValue(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                             TextAnimationTransition transition,
                             GlyphMapper glyphMapper,
                             int startIndex,
                             int endIndex,
                             float normalizedTime)
        {
            float2 value = transition.startValueFloat2;

            if (transition.currentTime >= transition.transitionDelay)
            {
                value = Interpolation.Interpolate(transition.startValueFloat2, transition.endValueFloat2, normalizedTime, transition.interpolation);
            }

            //Apply new values
            for (int i = startIndex; i <= math.min(endIndex, renderGlyphs.Length - 1); i++)
            {
                var renderGlyph = renderGlyphs[i];
                SetScaleValue(ref renderGlyph, value);
                renderGlyphs[i] = renderGlyph;
            }
        }

        public void SetScaleValue(ref RenderGlyph glyph, float2 value)
        {
            var height          = glyph.trPosition.y - glyph.blPosition.y;
            var width           = glyph.trPosition.x - glyph.blPosition.x;
            var newHeight       = height * value.y;
            var newWidth        = width * value.x;
            var halfHeightDelta = (newHeight - height) / 2f;
            var halfWidthDelta  = (newWidth - width) / 2f;

            glyph.trPosition = new float2(glyph.trPosition.x + halfWidthDelta, glyph.trPosition.y + halfHeightDelta);
            glyph.blPosition = new float2(glyph.blPosition.x - halfWidthDelta, glyph.blPosition.y - halfHeightDelta);
        }
    }
}

