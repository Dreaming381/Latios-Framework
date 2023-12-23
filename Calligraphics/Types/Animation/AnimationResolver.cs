using Latios.Calligraphics.Rendering;
using Unity.Entities;

namespace Latios.Calligraphics
{
    internal static class AnimationResolver
    {
        internal static void Initialize(ref TextAnimationTransition transition, ref Rng.RngSequence rng, GlyphMapper glyphMapper)
        {
            switch (transition.glyphProperty)
            {
                case GlyphProperty.Color:
                {
                    new ColorTransitionProvider().Initialize(ref transition, ref rng, glyphMapper);;
                    break;
                }
                case GlyphProperty.Scale:
                {
                    new ScaleTransitionProvider().Initialize(ref transition, ref rng, glyphMapper);;
                    break;
                }
                case GlyphProperty.Position:
                {
                    new PositionTransitionProvider().Initialize(ref transition, ref rng, glyphMapper);;
                    break;
                }
                case GlyphProperty.PositionNoise:
                {
                    new NoisePositionTransitionProvider().Initialize(ref transition, ref rng, glyphMapper);;
                    break;
                }
                case GlyphProperty.RotationNoise:
                {
                    new NoiseRotationTransitionProvider().Initialize(ref transition, ref rng, glyphMapper);;
                    break;
                }
                default:
                {
                    new OpacityTransitionProvider().Initialize(ref transition, ref rng, glyphMapper);;
                    break;
                }
            }
        }

        internal static void SetValue(ref DynamicBuffer<RenderGlyph> renderGlyphs, TextAnimationTransition transition, GlyphMapper glyphMapper,
                                      int startIndex, int endIndex, float normalizedTime)
        {
            switch (transition.glyphProperty)
            {
                case GlyphProperty.Color:
                {
                    new ColorTransitionProvider().SetValue(ref renderGlyphs, transition, glyphMapper, startIndex, endIndex, normalizedTime);
                    break;
                }
                case GlyphProperty.Scale:
                {
                    new ScaleTransitionProvider().SetValue(ref renderGlyphs, transition, glyphMapper, startIndex, endIndex, normalizedTime);
                    break;
                }
                case GlyphProperty.Position:
                {
                    new PositionTransitionProvider().SetValue(ref renderGlyphs, transition, glyphMapper, startIndex, endIndex, normalizedTime);
                    break;
                }
                case GlyphProperty.PositionNoise:
                {
                    new NoisePositionTransitionProvider().SetValue(ref renderGlyphs, transition, glyphMapper, startIndex, endIndex, normalizedTime);
                    break;
                }
                case GlyphProperty.RotationNoise:
                {
                    new NoiseRotationTransitionProvider().SetValue(ref renderGlyphs, transition, glyphMapper, startIndex, endIndex, normalizedTime);
                    break;
                }
                default:
                {
                    new OpacityTransitionProvider().SetValue(ref renderGlyphs, transition, glyphMapper, startIndex, endIndex, normalizedTime);
                    break;
                }
            }
        }

        internal static void  DisposeTransition(ref TextAnimationTransition transition)
        {
            switch (transition.glyphProperty)
            {
                case GlyphProperty.PositionNoise:
                {
                    new NoisePositionTransitionProvider().DisposeTransition(ref transition);
                    break;
                }
                case GlyphProperty.RotationNoise:
                {
                    new NoiseRotationTransitionProvider().DisposeTransition(ref transition);
                    break;
                }
            }
        }
    }
}

