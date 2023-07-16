using Latios.Calligraphics.RichText;
using Latios.Kinemation.TextBackend;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore;

namespace Latios.Calligraphics
{
    public static class GlyphGeneration
    {
        internal static void CreateRenderGlyphs(
            ref DynamicBuffer<RenderGlyph> renderGlyphs,
            ref FontBlob font,
            in DynamicBuffer<CalliByte>    calliBytes,
            in TextBaseConfiguration baseConfiguration,
            ref NativeList<RichTextTag>    tags)
        {
            renderGlyphs.Clear();

            float2 cumulativeOffset = new float2();  // Tracks text progression and word wrap
            float2 adjustmentOffset = new float2();  //Tracks placement adjustments

            var characterEnumerator = new RichTextInfluenceCharEnumerator(tags, calliBytes);

            while (characterEnumerator.MoveNext())
            {
                var unicode = characterEnumerator.Current.character;

                if (font.characters.TryGetGlyph(math.asuint(unicode.value), out var glyphIndex))
                {
                    ref var glyphBlob   = ref font.characters[glyphIndex];
                    var     renderGlyph = new RenderGlyph
                    {
                        blPosition = GetBottomLeftPosition(ref font, ref glyphBlob, baseConfiguration.fontSize, adjustmentOffset + cumulativeOffset, false, false),
                        trPosition = GetTopRightPosition(ref font, ref glyphBlob, baseConfiguration.fontSize, adjustmentOffset + cumulativeOffset, false, false),
                        blUVA      = glyphBlob.bottomLeftUV,
                        trUVA      = glyphBlob.topRightUV,
                        blUVB      = glyphBlob.bottomLeftUV2,
                        tlUVB      = glyphBlob.topLeftUV2,
                        trUVB      = glyphBlob.topRightUV2,
                        brUVB      = glyphBlob.bottomRightUV2,
                        blColor    = baseConfiguration.color,
                        tlColor    = baseConfiguration.color,
                        trColor    = baseConfiguration.color,
                        brColor    = baseConfiguration.color,
                        unicode    = glyphBlob.unicode,
                        scale      = baseConfiguration.fontSize,
                    };

                    var baseScale = font.baseScale;
                    ApplyTags(ref renderGlyph, ref font, ref glyphBlob, ref baseScale, characterEnumerator.Current);
                    renderGlyphs.Add(renderGlyph);

                    adjustmentOffset = float2.zero;

                    //TODO:  Word wrap etc
                    var xAdvanceAdjustment   = 0f;
                    var yAdvanceAdjustment   = 0f;
                    var xPlacementAdjustment = 0f;
                    var yPlacementAdjustment = 0f;

                    var peekEnumerator = characterEnumerator;
                    if (peekEnumerator.MoveNext())
                    {
                        var peekChar = peekEnumerator.Current.character;
                        for (int k = 0; k < glyphBlob.glyphAdjustments.Length; k++)
                        {
                            var glyphAdjustment = glyphBlob.glyphAdjustments[k];

                            if (glyphAdjustment.secondAdjustment.glyphUnicode == math.asuint(peekChar.value))
                            {
                                xAdvanceAdjustment   = glyphAdjustment.firstAdjustment.xAdvance * renderGlyph.scale * baseScale;
                                yAdvanceAdjustment   = glyphAdjustment.firstAdjustment.yAdvance * renderGlyph.scale * baseScale;
                                xPlacementAdjustment = glyphAdjustment.firstAdjustment.xPlacement * renderGlyph.scale * baseScale;
                                yPlacementAdjustment = glyphAdjustment.firstAdjustment.yPlacement * renderGlyph.scale * baseScale;
                                break;
                            }
                        }
                    }

                    adjustmentOffset.x = xPlacementAdjustment;
                    adjustmentOffset.y = yPlacementAdjustment;

                    cumulativeOffset.x += renderGlyph.scale * baseScale * glyphBlob.horizontalAdvance + xAdvanceAdjustment;
                    cumulativeOffset.y += yAdvanceAdjustment;
                }
            }
        }

        //TODO:  Modify advances and placements based on tags
        internal static void ApplyTags(ref RenderGlyph renderGlyph, ref FontBlob font, ref GlyphBlob glyph,  ref float baseScale, RichTextInfluenceContext enumerable)
        {
            //for (int i = 0; i < tags.Length; i++)
            foreach (var tag in enumerable)
            {
                switch (tag.tagType)
                {
                    case RichTextTagType.Align:
                        break;
                    case RichTextTagType.Alpha:
                        renderGlyph.blColor.a = tag.alpha;
                        renderGlyph.tlColor.a = tag.alpha;
                        renderGlyph.trColor.a = tag.alpha;
                        renderGlyph.brColor.a = tag.alpha;
                        break;
                    case RichTextTagType.Anchor:
                        break;
                    case RichTextTagType.Bold:
                        break;
                    case RichTextTagType.Color:
                    {
                        Color32 blColor       = renderGlyph.blColor;
                        renderGlyph.blColor   = tag.color;
                        renderGlyph.blColor.a = blColor.a;
                        Color32 tlColor       = renderGlyph.tlColor;
                        renderGlyph.tlColor   = tag.color;
                        renderGlyph.tlColor.a = tlColor.a;
                        Color32 trColor       = renderGlyph.trColor;
                        renderGlyph.trColor   = tag.color;
                        renderGlyph.trColor.a = trColor.a;
                        Color32 brColor       = renderGlyph.brColor;
                        renderGlyph.brColor   = tag.color;
                        renderGlyph.brColor.a = brColor.a;
                        break;
                    }
                    case RichTextTagType.Font:
                        break;
                    case RichTextTagType.Gradient:
                    {
                        Color32 blColor       = renderGlyph.blColor;
                        renderGlyph.blColor   = tag.blColor;
                        renderGlyph.blColor.a = blColor.a;
                        Color32 tlColor       = renderGlyph.tlColor;
                        renderGlyph.tlColor   = tag.tlColor;
                        renderGlyph.tlColor.a = tlColor.a;
                        Color32 trColor       = renderGlyph.trColor;
                        renderGlyph.trColor   = tag.trColor;
                        renderGlyph.trColor.a = trColor.a;
                        Color32 brColor       = renderGlyph.brColor;
                        renderGlyph.brColor   = tag.brColor;
                        renderGlyph.brColor.a = brColor.a;
                        break;
                    }
                    case RichTextTagType.Indent:
                        break;
                    case RichTextTagType.Italic:
                        break;
                    case RichTextTagType.Margin:
                        break;
                    case RichTextTagType.Mark:
                        break;
                    case RichTextTagType.Position:
                        break;
                    case RichTextTagType.Rotate:
                        break;
                    case RichTextTagType.Size:
                        renderGlyph.scale = tag.fontSize;
                        break;
                    case RichTextTagType.Space:
                        break;
                    case RichTextTagType.Sprite:
                        break;
                    case RichTextTagType.Style:
                        break;
                    case RichTextTagType.Subscript:
                        break;
                    case RichTextTagType.Superscript:
                        break;
                    case RichTextTagType.Underline:
                        break;
                    case RichTextTagType.Width:
                        break;
                    case RichTextTagType.AllCaps:
                        break;
                    case RichTextTagType.CharacterSpacing:
                        break;
                    case RichTextTagType.FontWeight:
                        break;
                    case RichTextTagType.LineBreak:
                        break;
                    case RichTextTagType.LineHeight:
                        break;
                    case RichTextTagType.LineIndent:
                        break;
                    case RichTextTagType.LowerCase:
                        break;
                    case RichTextTagType.MonoSpace:
                        break;
                    case RichTextTagType.NoBreak:
                        break;
                    case RichTextTagType.SmallCaps:
                        break;
                    case RichTextTagType.StrikeThrough:
                        break;
                    case RichTextTagType.UpperCase:
                        break;
                    case RichTextTagType.VerticalOffset:
                        break;
                }
            }
        }

        internal static float2 GetBottomLeftPosition(ref FontBlob font, ref GlyphBlob glyph, float scale, float2 offset, bool isItalics, bool isBold)
        {
            var bottomLeft = glyph.bottomLeftVertex;

            var fontWeight = glyph.scale;
            if (isBold)
            {
                fontWeight += font.boldStyleWeight * 0.1f;
            }

            bottomLeft.x = bottomLeft.x - bottomLeft.x * fontWeight;

            bottomLeft.x *= scale;
            bottomLeft.y *= scale;

            bottomLeft.x += offset.x;
            bottomLeft.y += offset.y;

            if (isItalics)
            {
                // Shift Top vertices forward by half (Shear Value * height of character) and Bottom vertices back by same amount.
                float  shear       = font.italicsStyleSlant * 0.01f;
                float  midPoint    = ((font.capLine - font.baseLine) / 2) * font.baseScale * scale;
                float2 bottomShear = new float2(shear * (((glyph.horizontalBearingY - glyph.height - font.materialPadding - midPoint)) * font.baseScale * scale), 0);

                bottomLeft += bottomShear;
            }

            return bottomLeft;
        }

        internal static float2 GetTopRightPosition(ref FontBlob font, ref GlyphBlob glyph, float scale, float2 offset, bool isItalics, bool isBold)
        {
            var topRight = glyph.topRightVertex;

            var fontWeight = glyph.scale;
            if (isBold)
            {
                fontWeight += font.boldStyleWeight * 0.1f;
            }

            topRight.x = topRight.x * fontWeight;

            topRight.x *= scale;
            topRight.y *= scale;

            topRight.x += offset.x;
            topRight.y += offset.y;

            if (isItalics)
            {
                // Shift Top vertices forward by half (Shear Value * height of character) and Bottom vertices back by same amount.
                float  shear    = font.italicsStyleSlant * 0.01f;
                float  midPoint = ((font.capLine - font.baseLine) / 2) * font.baseScale * scale;
                float2 topShear = new float2(shear * ((glyph.horizontalBearingY + font.materialPadding - midPoint) * font.baseScale * scale), 0);

                topRight += topShear;
            }

            return topRight;
        }
    }
}

