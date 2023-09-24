using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Latios.Calligraphics.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Calligraphics/Text Animation")]
    [RequireComponent(typeof(TextRendererAuthoring))]
    public class TextAnimationAuthoring : MonoBehaviour
    {
        public List<GlyphAnimation> glyphAnimations = new List<GlyphAnimation>();
    }

    [Serializable]
    public class GlyphAnimation
    {
        public GlyphProperty           glyphProperty;
        public AnimationStyle          animationStyle;
        public InterpolationType       interpolation;
        public TransitionTextUnitScope unitScope;
        public int                     startIndex;
        public int                     endIndex;
        public TransitionEndBehavior   endBehavior;
        public int                     loopCount;
        public float                   loopDelay;
        public float                   transitionTimeOffset;
        public float                   transitionDuration    = .5f;
        public float                   progressiveTimeOffset = .1f;
        public byte                    startValueByte        = Byte.MinValue;
        public byte                    endValueByte          = Byte.MaxValue;
        public float                   startValueFloat       = float.MinValue;
        public float                   endValueFloat         = float.MaxValue;
        public Color32                 startValueColor       = UnityEngine.Color.white;
        public Color32                 endValueColor         = UnityEngine.Color.black;
        public float2                  startValueFloat2      = float2.zero;
        public float2                  endValueFloat2        = float2.zero;
        public float                   noiseScaleFloat       = 1;
        public float2                  noiseScaleFloat2      = float2.zero;
        public float3                  startValueFloat3      = float3.zero;
        public float3                  endValueFloat3        = float3.zero;
        public Gradient                startValueGradient    = new Gradient();
        public Gradient                endValueGradient      = new Gradient();
    }

    public enum AnimationStyle
    {
        Progressive,
        Simultaneous,
    }

    public class TextAnimationBaker : Baker<TextAnimationAuthoring>
    {
        public override void Bake(TextAnimationAuthoring authoring)
        {
            var textAuthoring = authoring.GetComponent<TextRendererAuthoring>();
            DependsOn(authoring);
            DependsOn(textAuthoring);

            var entity = GetEntity(TransformUsageFlags.Renderable);

            //Add buffers
            if (authoring.glyphAnimations.Any())
            {
                AddBuffer<TextAnimationTransition>(entity);
                //Add transitions
                foreach (var glyphAnimation in authoring.glyphAnimations)
                {
                    var startIndex = glyphAnimation.unitScope == TransitionTextUnitScope.All ? 0 : glyphAnimation.startIndex;
                    var endIndex   = glyphAnimation.unitScope == TransitionTextUnitScope.All ?
                                     textAuthoring.text.Length :
                                     glyphAnimation.endIndex;

                    if (glyphAnimation.animationStyle == AnimationStyle.Simultaneous)
                    {
                        var transition = new TextAnimationTransition
                        {
                            glyphProperty      = glyphAnimation.glyphProperty,
                            interpolation      = glyphAnimation.interpolation,
                            scope              = glyphAnimation.unitScope,
                            startIndex         = startIndex,
                            endIndex           = endIndex,
                            transitionDuration = glyphAnimation.transitionDuration,
                            transitionDelay    = glyphAnimation.transitionTimeOffset,
                            endBehavior        = glyphAnimation.endBehavior,
                            loopCount          = (glyphAnimation.endBehavior & TransitionEndBehavior.Loop) == TransitionEndBehavior.Loop ? glyphAnimation.loopCount : 0,
                            loopDelay          = (glyphAnimation.endBehavior & TransitionEndBehavior.Loop) == TransitionEndBehavior.Loop ? glyphAnimation.loopDelay : 0,
                        };
                        SetValue(glyphAnimation, ref transition);
                        AppendToBuffer(entity, transition);
                    }
                    else
                    {
                        var elementCount = 0;
                        for (int i = startIndex; i <= endIndex; i++)
                        {
                            var transition = new TextAnimationTransition
                            {
                                glyphProperty = glyphAnimation.glyphProperty,
                                interpolation = glyphAnimation.interpolation,
                                scope         = glyphAnimation.unitScope == TransitionTextUnitScope.All ?
                                                TransitionTextUnitScope.Glyph :
                                                glyphAnimation.unitScope,
                                startIndex         = i,
                                endIndex           = i,
                                transitionDuration = glyphAnimation.transitionDuration,
                                transitionDelay    = glyphAnimation.transitionTimeOffset + elementCount * glyphAnimation.progressiveTimeOffset,
                                endBehavior        = glyphAnimation.endBehavior,
                                loopCount          = (glyphAnimation.endBehavior & TransitionEndBehavior.Loop) == TransitionEndBehavior.Loop ? glyphAnimation.loopCount : 0,
                                loopDelay          = (glyphAnimation.endBehavior & TransitionEndBehavior.Loop) == TransitionEndBehavior.Loop ? glyphAnimation.loopDelay : 0,
                            };
                            SetValue(glyphAnimation, ref transition);

                            AppendToBuffer(entity, transition);
                            elementCount++;
                        }
                    }
                }
            }

            AddComponent(entity, new GlyphMappingMask
            {
                mask = GlyphMappingMask.WriteMask.Line | GlyphMappingMask.WriteMask.Word | GlyphMappingMask.WriteMask.CharNoTags
            });
            AddBuffer<GlyphMappingElement>(entity);
        }

        private void SetValue(GlyphAnimation glyphAnimation,
                              ref TextAnimationTransition transition)
        {
            switch (glyphAnimation.glyphProperty)
            {
                case GlyphProperty.Opacity:
                    transition.startValueByte = glyphAnimation.startValueByte;
                    transition.endValueByte   = glyphAnimation.endValueByte;
                    break;
                case GlyphProperty.Scale:
                    transition.startValueFloat2 = glyphAnimation.startValueFloat2;
                    transition.endValueFloat2   = glyphAnimation.endValueFloat2;
                    break;
                case GlyphProperty.Color:
                {
                    transition.startValueBlColor = glyphAnimation.startValueColor;
                    transition.endValueBlColor   = glyphAnimation.endValueColor;
                    transition.startValueTrColor = glyphAnimation.startValueColor;
                    transition.endValueTrColor   = glyphAnimation.endValueColor;
                    transition.startValueBrColor = glyphAnimation.startValueColor;
                    transition.endValueBrColor   = glyphAnimation.endValueColor;
                    transition.startValueTlColor = glyphAnimation.startValueColor;
                    transition.endValueTlColor   = glyphAnimation.endValueColor;
                    break;
                }
                case GlyphProperty.Position:
                {
                    transition.startValueFloat2 = glyphAnimation.startValueFloat2;
                    transition.endValueFloat2   = glyphAnimation.endValueFloat2;
                    break;
                }
                case GlyphProperty.PositionNoise:
                {
                    transition.noiseScaleFloat2 = glyphAnimation.noiseScaleFloat2;
                    break;
                }
                case GlyphProperty.RotationNoise:
                {
                    transition.noiseScaleFloat = glyphAnimation.noiseScaleFloat;
                    break;
                }
            }
        }
    }
}

