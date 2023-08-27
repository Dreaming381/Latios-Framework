using Latios.Kinemation.TextBackend;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    internal struct NoiseRotationTransitionProvider : ITransitionProvider
    {
        public void Initialize(ref TextAnimationTransition transition, ref Rng.RngSequence rng, GlyphMapper glyphMapper)
        {
            //TODO: if scope is glyph, then we need to generate a new noise value for each glyph
            transition.startValueFloat = transition.endValueFloat;
            var noiseSeed              = rng.NextFloat2(new float2(-1f, -1f), new float2(1f, 1f));
            transition.endValueFloat   = noise.cnoise(noiseSeed) * transition.noiseScaleFloat;
        }

        public void SetValue(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                             TextAnimationTransition transition,
                             GlyphMapper glyphMapper,
                             int startIndex,
                             int endIndex,
                             float normalizedTime)
        {
            var value = transition.startValueFloat;

            if (transition.currentTime >= transition.transitionDelay)
            {
                value = Interpolation.Interpolate(transition.startValueFloat, transition.endValueFloat, normalizedTime, transition.interpolation);
            }

            //Apply new values
            for (int i = startIndex; i <= math.min(endIndex, renderGlyphs.Length - 1); i++)
            {
                var renderGlyph = renderGlyphs[i];
                SetRotationValue(ref renderGlyph, value);
                renderGlyphs[i] = renderGlyph;
            }
        }

        private void SetRotationValue(ref RenderGlyph glyph, float value)
        {
            //TODO: If the scope is a word, we should rotate the center of the glyph relative to the pivot, and then add the change in the pivot to the vertices

            glyph.rotationCCW = value;
        }
    }
}

