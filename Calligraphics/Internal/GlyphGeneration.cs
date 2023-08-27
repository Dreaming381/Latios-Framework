using Latios.Calligraphics.RichText;
using Latios.Kinemation.TextBackend;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics
{
    public static class GlyphGeneration
    {
        internal static unsafe void CreateRenderGlyphs(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                                                       ref GlyphMappingWriter mappingWriter,
                                                       ref FontBlob font,
                                                       in DynamicBuffer<CalliByte>    calliBytes,
                                                       in TextBaseConfiguration baseConfiguration,
                                                       ref NativeList<RichTextTag>    tags)
        {
            renderGlyphs.Clear();

            float2                 cumulativeOffset                                = new float2();  // Tracks text progression and word wrap
            float2                 adjustmentOffset                                = new float2();  //Tracks placement adjustments
            int                    characterCount                                  = 0;
            int                    lastWordStartCharacterGlyphIndex                = 0;
            FixedList512Bytes<int> characterGlyphIndicesWithPreceedingSpacesInLine = default;
            int                    accumulatedSpaces                               = 0;
            int                    startOfLineGlyphIndex                           = 0;
            bool                   prevWasSpace                                    = false;
            int                    lineCount                                       = 0;
            bool                   isLineStart                                     = true;

            var calliString         = new CalliString(calliBytes);
            var characterEnumerator = new RichTextInfluenceCharEnumerator(tags, calliString);

            while (characterEnumerator.MoveNext())
            {
                var unicode = characterEnumerator.Current.character;
                characterCount++;

                if (isLineStart)
                {
                    isLineStart = false;
                    mappingWriter.AddLineStart(renderGlyphs.Length);
                    if (!prevWasSpace)
                    {
                        mappingWriter.AddWordStart(renderGlyphs.Length);
                    }
                }

                //Handle line break
                if (unicode.value == 10)  //Line feed
                {
                    var glyphsLine   = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex, renderGlyphs.Length - startOfLineGlyphIndex);
                    var overrideMode = baseConfiguration.alignMode;
                    if ((overrideMode & AlignMode.HorizontalMask) == AlignMode.Justified)
                    {
                        // Don't perform justified spacing for the last line in the paragraph.
                        overrideMode &= ~AlignMode.HorizontalMask;
                        overrideMode |= AlignMode.Left;
                    }
                    ApplyHorizontalAlignmentToGlyphs(ref glyphsLine,
                                                     ref characterGlyphIndicesWithPreceedingSpacesInLine,
                                                     baseConfiguration.maxLineWidth,
                                                     overrideMode);
                    lineCount++;
                    isLineStart = true;

                    cumulativeOffset.x  = 0;
                    cumulativeOffset.y -= font.lineHeight * font.baseScale * baseConfiguration.fontSize;
                    continue;
                }

                if (font.characters.TryGetGlyph(math.asuint(unicode.value), out var glyphIndex))
                {
                    ref var glyphBlob   = ref font.characters[glyphIndex];
                    var     renderGlyph = new RenderGlyph
                    {
                        blPosition = GetBottomLeftPosition(ref font, ref glyphBlob, baseConfiguration.fontSize,
                                                           adjustmentOffset + cumulativeOffset, false, false),
                        trPosition = GetTopRightPosition(ref font, ref glyphBlob, baseConfiguration.fontSize,
                                                         adjustmentOffset + cumulativeOffset, false, false),
                        blUVA   = glyphBlob.bottomLeftUV,
                        trUVA   = glyphBlob.topRightUV,
                        blUVB   = glyphBlob.bottomLeftUV2,
                        tlUVB   = glyphBlob.topLeftUV2,
                        trUVB   = glyphBlob.topRightUV2,
                        brUVB   = glyphBlob.bottomRightUV2,
                        blColor = baseConfiguration.color,
                        tlColor = baseConfiguration.color,
                        trColor = baseConfiguration.color,
                        brColor = baseConfiguration.color,
                        unicode = glyphBlob.unicode,
                        scale   = baseConfiguration.fontSize,
                    };

                    var baseScale = font.baseScale;
                    ApplyTags(ref renderGlyph, ref font, ref glyphBlob, ref baseScale, characterEnumerator.Current);
                    renderGlyphs.Add(renderGlyph);
                    mappingWriter.AddCharNoTags(characterCount - 1, true);
                    mappingWriter.AddCharWithTags(characterEnumerator.CurrentCharIndex, true);
                    mappingWriter.AddBytes(characterEnumerator.CurrentByteIndex, unicode.LengthInUtf8Bytes(), true);

                    adjustmentOffset = float2.zero;

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

                    // Apply accumulated spaces to non-space character
                    while (unicode.value != 32 && accumulatedSpaces > 0)
                    {
                        characterGlyphIndicesWithPreceedingSpacesInLine.Add(renderGlyphs.Length - 1 - startOfLineGlyphIndex);
                        accumulatedSpaces--;
                    }

                    // Handle word wrap
                    if (baseConfiguration.maxLineWidth < float.MaxValue &&
                        baseConfiguration.maxLineWidth > 0 &&
                        cumulativeOffset.x > baseConfiguration.maxLineWidth)
                    {
                        bool dropSpace = false;
                        if (unicode.value == 32 && !prevWasSpace)
                        {
                            dropSpace = true;
                            accumulatedSpaces--;
                        }

                        var yOffsetChange = font.lineHeight * renderGlyph.scale * baseScale;
                        var xOffsetChange = renderGlyphs[lastWordStartCharacterGlyphIndex].blPosition.x;
                        if (xOffsetChange > 0 && !dropSpace)  // Always allow one visible character
                        {
                            // Finish line based on alignment
                            var glyphsLine = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex,
                                                                                      lastWordStartCharacterGlyphIndex - startOfLineGlyphIndex);
                            ApplyHorizontalAlignmentToGlyphs(ref glyphsLine,
                                                             ref characterGlyphIndicesWithPreceedingSpacesInLine,
                                                             baseConfiguration.maxLineWidth,
                                                             baseConfiguration.alignMode);
                            startOfLineGlyphIndex = lastWordStartCharacterGlyphIndex;
                            lineCount++;

                            cumulativeOffset.x -= xOffsetChange;
                            cumulativeOffset.y -= yOffsetChange;

                            //Adjust the vertices of the previous render glyphs in the word
                            var glyphPtr = (RenderGlyph*)renderGlyphs.GetUnsafePtr();
                            for (int i = lastWordStartCharacterGlyphIndex; i < renderGlyphs.Length; i++)
                            {
                                glyphPtr[i].blPosition.y -= yOffsetChange;
                                glyphPtr[i].blPosition.x -= xOffsetChange;
                                glyphPtr[i].trPosition.y -= yOffsetChange;
                                glyphPtr[i].trPosition.x -= xOffsetChange;
                            }
                        }
                    }

                    //Detect start of word
                    if (unicode.value == 32 ||  //Space
                        unicode.value == 9 ||  //Tab
                        unicode.value == 45 ||  //Hyphen Minus
                        unicode.value == 173 ||  //Soft hyphen
                        unicode.value == 8203 ||  //Zero width space
                        unicode.value == 8204 ||  //Zero width non-joiner
                        unicode.value == 8205)  //Zero width joiner
                    {
                        lastWordStartCharacterGlyphIndex = renderGlyphs.Length;
                        mappingWriter.AddWordStart(renderGlyphs.Length);
                    }

                    if (unicode.value == 32)
                    {
                        accumulatedSpaces++;
                        prevWasSpace = true;
                    }
                    else if (prevWasSpace)
                    {
                        prevWasSpace = false;
                    }
                }
            }

            var finalGlyphsLine = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex, renderGlyphs.Length - startOfLineGlyphIndex);
            {
                var overrideMode = baseConfiguration.alignMode;
                if ((overrideMode & AlignMode.HorizontalMask) == AlignMode.Justified)
                {
                    // Don't perform justified spacing for the last line.
                    overrideMode &= ~AlignMode.HorizontalMask;
                    overrideMode |= AlignMode.Left;
                }
                ApplyHorizontalAlignmentToGlyphs(ref finalGlyphsLine, ref characterGlyphIndicesWithPreceedingSpacesInLine, baseConfiguration.maxLineWidth, overrideMode);
            }
            lineCount++;
            ApplyVerticalAlignmentToGlyphs(ref renderGlyphs, lineCount, baseConfiguration.alignMode, ref font, baseConfiguration.fontSize);
        }

        //TODO:  Modify advances and placements based on tags
        static void ApplyTags(ref RenderGlyph renderGlyph, ref FontBlob font, ref GlyphBlob glyph,  ref float baseScale, RichTextInfluenceContext enumerable)
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

        static float2 GetBottomLeftPosition(ref FontBlob font, ref GlyphBlob glyph, float scale, float2 offset, bool isItalics, bool isBold)
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

        static float2 GetTopRightPosition(ref FontBlob font, ref GlyphBlob glyph, float scale, float2 offset, bool isItalics, bool isBold)
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

        static unsafe void ApplyHorizontalAlignmentToGlyphs(ref NativeArray<RenderGlyph> glyphs,
                                                            ref FixedList512Bytes<int>   characterGlyphIndicesWithPreceedingSpacesInLine,
                                                            float width,
                                                            AlignMode alignMode)
        {
            if ((alignMode & AlignMode.HorizontalMask) == AlignMode.Left)
            {
                characterGlyphIndicesWithPreceedingSpacesInLine.Clear();
                return;
            }

            var glyphsPtr = (RenderGlyph*)glyphs.GetUnsafePtr();
            if ((alignMode & AlignMode.HorizontalMask) == AlignMode.Center)
            {
                float offset = glyphsPtr[glyphs.Length - 1].trPosition.x / 2f;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    glyphsPtr[i].blPosition.x -= offset;
                    glyphsPtr[i].trPosition.x -= offset;
                }
            }
            else if ((alignMode & AlignMode.HorizontalMask) == AlignMode.Right)
            {
                float offset = glyphsPtr[glyphs.Length - 1].trPosition.x;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    glyphsPtr[i].blPosition.x -= offset;
                    glyphsPtr[i].trPosition.x -= offset;
                }
            }
            else  // Justified
            {
                float nudgePerSpace     = (width - glyphsPtr[glyphs.Length - 1].trPosition.x) / characterGlyphIndicesWithPreceedingSpacesInLine.Length;
                float accumulatedOffset = 0f;
                int   indexInIndices    = 0;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    while (indexInIndices < characterGlyphIndicesWithPreceedingSpacesInLine.Length &&
                           characterGlyphIndicesWithPreceedingSpacesInLine[indexInIndices] == i)
                    {
                        accumulatedOffset += nudgePerSpace;
                        indexInIndices++;
                    }

                    glyphsPtr[i].blPosition.x += accumulatedOffset;
                    glyphsPtr[i].trPosition.x += accumulatedOffset;
                }
            }
            characterGlyphIndicesWithPreceedingSpacesInLine.Clear();
        }

        static unsafe void ApplyVerticalAlignmentToGlyphs(ref DynamicBuffer<RenderGlyph> glyphs, int fullLineCount, AlignMode alignMode, ref FontBlob font, float fontSize)
        {
            var glyphsPtr = (RenderGlyph*)glyphs.GetUnsafePtr();
            if ((alignMode & AlignMode.VerticalMask) == AlignMode.Top)
            {
                // Positions were calculated relative to the baseline.
                // Shift everything down so that y = 0 is on the ascent line.
                var offset  = font.ascentLine - font.baseLine;
                offset     *= font.baseScale * fontSize;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    glyphsPtr[i].blPosition.y -= offset;
                    glyphsPtr[i].trPosition.y -= offset;
                }
            }
            else if ((alignMode & AlignMode.VerticalMask) == AlignMode.Middle)
            {
                float newlineSpace = (fullLineCount - 1) * font.lineHeight * font.baseScale * fontSize;
                float fullHeight   = newlineSpace + (font.ascentLine - font.baseLine) * font.baseScale * fontSize;
                var   offset       = fullHeight / 2f - font.ascentLine * font.baseScale * fontSize;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    glyphsPtr[i].blPosition.y += offset;
                    glyphsPtr[i].trPosition.y += offset;
                }
            }
            else  // Bottom
            {
                // Todo: Should we just leave the y = 0 on the baseline instead of the descent line?
                float newlineSpace = (fullLineCount - 1) * font.lineHeight * font.baseScale * fontSize;
                var   offset       = newlineSpace + (font.baseLine - font.descentLine) * font.baseScale * fontSize;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    glyphsPtr[i].blPosition.y += offset;
                    glyphsPtr[i].trPosition.y += offset;
                }
            }
        }
    }
}

