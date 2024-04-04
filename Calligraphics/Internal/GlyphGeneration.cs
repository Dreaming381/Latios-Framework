using Latios.Calligraphics.Rendering;
using Latios.Calligraphics.RichText;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Latios.Calligraphics
{
    internal static class GlyphGeneration
    {
        internal static unsafe void CreateRenderGlyphs(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                                                       ref GlyphMappingWriter mappingWriter,
                                                       ref FontMaterialSet fontMaterialSet,
                                                       in DynamicBuffer<CalliByte>    calliBytes,
                                                       in TextBaseConfiguration baseConfiguration)
        {
            renderGlyphs.Clear();

            //initialized textConfiguration which stores all fields that are modified by RichText Tags
            var richTextTagIdentifiers = new FixedList512Bytes<RichTextTagIdentifier>();
            var textConfiguration      = new TextConfiguration(baseConfiguration);

            float2                 cumulativeOffset                                = new float2();  // Tracks text progression and word wrap
            float2                 adjustmentOffset                                = new float2();  //Tracks placement adjustments
            int                    lastWordStartCharacterGlyphIndex                = 0;
            FixedList512Bytes<int> characterGlyphIndicesWithPreceedingSpacesInLine = default;
            int                    accumulatedSpaces                               = 0;
            int                    startOfLineGlyphIndex                           = 0;
            bool                   prevWasSpace                                    = false;
            int                    lineCount                                       = 0;
            bool                   isLineStart                                     = true;
            ref FontBlob           font                                            = ref fontMaterialSet[0];

            var calliString         = new CalliString(calliBytes);
            var characterEnumerator = calliString.GetEnumerator();
            while (characterEnumerator.MoveNext())
            {
                var unicode = characterEnumerator.Current;
                textConfiguration.m_characterCount++;

                // Parse Rich Text Tag
                #region Parse Rich Text Tag
                if (unicode == '<')  // '<'
                {
                    textConfiguration.m_isParsingText = true;
                    // Check if Tag is valid. If valid, skip to the end of the validated tag.
                    if (RichTextParser.ValidateHtmlTag(in calliString, ref characterEnumerator, ref font, in baseConfiguration, ref textConfiguration, ref richTextTagIdentifiers))
                    {
                        // Continue to next character
                        continue;
                    }
                }
                #endregion

                textConfiguration.m_isParsingText = false;

                // Handle Font Styles like LowerCase, UpperCase and SmallCaps.
                #region Handling of LowerCase, UpperCase and SmallCaps Font Styles

                float smallCapsMultiplier = 1.0f;

                if ((textConfiguration.m_fontStyleInternal & FontStyles.UpperCase) == FontStyles.UpperCase)
                {
                    // If this character is lowercase, switch to uppercase.
                    if (char.IsLower((char)unicode.value))
                        unicode.value = char.ToUpper((char)unicode.value);

                }
                else if ((textConfiguration.m_fontStyleInternal & FontStyles.LowerCase) == FontStyles.LowerCase)
                {
                    // If this character is uppercase, switch to lowercase.
                    if (char.IsUpper((char)unicode.value))
                        unicode.value = char.ToLower((char)unicode.value);
                }
                else if ((textConfiguration.m_fontStyleInternal & FontStyles.SmallCaps) == FontStyles.SmallCaps)
                {
                    if (char.IsLower((char)unicode.value))
                    {
                        smallCapsMultiplier = 0.8f;
                        unicode.value = char.ToUpper((char)unicode.value);
                    }
                }
                #endregion

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
                    var overrideMode = textConfiguration.m_lineJustification;
                    if ((overrideMode) == HorizontalAlignmentOptions.Justified)
                    {
                        // Don't perform justified spacing for the last line in the paragraph.
                        overrideMode = HorizontalAlignmentOptions.Left;
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

                if (font.TryGetGlyphIndex(math.asuint(unicode.value), out var glyphIndex))
                {
                    ref var glyphBlob   = ref font.characters[glyphIndex];
                    var     renderGlyph = new RenderGlyph
                    {
                        blUVA   = glyphBlob.bottomLeftUV,
                        trUVA   = glyphBlob.topRightUV,
                        blUVB   = glyphBlob.bottomLeftUV2,
                        tlUVB   = glyphBlob.topLeftUV2,
                        trUVB   = glyphBlob.topRightUV2,
                        brUVB   = glyphBlob.bottomRightUV2,
                        blColor = textConfiguration.m_htmlColor,
                        tlColor = textConfiguration.m_htmlColor,
                        trColor = textConfiguration.m_htmlColor,
                        brColor = textConfiguration.m_htmlColor,
                        unicode = glyphBlob.unicode,
                        scale   = textConfiguration.m_currentFontSize,
                    };

                    // Set Padding based on selected font style
                    #region Handle Style Padding
                    //not yet implemented
                    var style_padding = 0;
                    #endregion Handle Style Padding

                    var adjustedScale = textConfiguration.m_currentFontSize * smallCapsMultiplier * font.baseScale;
                    var currentElementScale = adjustedScale * textConfiguration.m_fontScaleMultiplier * glyphBlob.scale;// * m_cached_TextElement.m_Scale * m_cached_TextElement.m_Glyph.scale;

                    var fontWeight = glyphBlob.scale;
                    if (((textConfiguration.m_fontStyleInternal & FontStyles.Bold) == FontStyles.Bold))
                    {
                        fontWeight += font.boldStyleWeight * 0.1f;
                    }

                    // Determine the position of the vertices of the Character.
                    #region Calculate Vertices Position
                    float2 topLeft = glyphBlob.topLeftVertex;
                    topLeft.x = topLeft.x - topLeft.x * fontWeight;
                    topLeft *= textConfiguration.m_currentFontSize * smallCapsMultiplier;

                    float2 bottomLeft = glyphBlob.bottomLeftVertex;
                    bottomLeft.x = bottomLeft.x - bottomLeft.x * fontWeight;
                    bottomLeft *= textConfiguration.m_currentFontSize * smallCapsMultiplier;

                    float2 topRight = glyphBlob.topRightVertex;
                    topRight.x = topRight.x * fontWeight;
                    topRight *= textConfiguration.m_currentFontSize * smallCapsMultiplier;

                    float2 bottomRight = glyphBlob.bottomLeftVertex;
                    bottomRight.x = bottomRight.x * fontWeight;
                    bottomRight *= textConfiguration.m_currentFontSize * smallCapsMultiplier;
                    #endregion

                    // Check if we need to Shear the rectangles for Italic styles
                    #region Handle Italic & Shearing
                    if (((textConfiguration.m_fontStyleInternal & FontStyles.Italic) == FontStyles.Italic))
                    {
                        // Shift Top vertices forward by half (Shear Value * height of character) and Bottom vertices back by same amount.
                        float shear = textConfiguration.m_italicAngle * 0.01f;
                        float2 topShear = new float2(shear * ((glyphBlob.horizontalBearingY + font.materialPadding + style_padding) * currentElementScale), 0);
                        float2 bottomShear = new float2(shear * (((glyphBlob.horizontalBearingY - glyphBlob.height - font.materialPadding - style_padding)) * currentElementScale), 0);
                        float2 shearAdjustment = (topShear - bottomShear) * 0.5f;

                        topShear -= shearAdjustment;
                        bottomShear -= shearAdjustment;

                        topLeft += topShear;
                        bottomLeft += bottomShear;
                        topRight += topShear;
                        bottomRight += bottomShear;

                        renderGlyph.shear = (topLeft.x - bottomLeft.x);
                    }
                    #endregion Handle Italics & Shearing

                    #region apply offsets
                    var offset = adjustmentOffset + cumulativeOffset;
                    topLeft += offset;
                    bottomLeft += offset;
                    topRight += offset;
                    bottomRight += offset;
                    #endregion

                    #region apply baselineoffset to glyph (influenced by <sub>, <sup>, <voffset>
                    bottomLeft.y += textConfiguration.m_baselineOffset;
                    topRight.y += textConfiguration.m_baselineOffset;
                    #endregion

                    renderGlyph.trPosition = topRight;
                    renderGlyph.blPosition = bottomLeft;

                    renderGlyphs.Add(renderGlyph);
                    fontMaterialSet.WriteFontMaterialIndexForGlyph(0);
                    mappingWriter.AddCharNoTags(textConfiguration.m_characterCount - 1, true);
                    mappingWriter.AddCharWithTags(characterEnumerator.CurrentCharIndex, true);
                    mappingWriter.AddBytes(characterEnumerator.CurrentByteIndex, unicode.LengthInUtf8Bytes(), true);

                    // Handle Kerning if Enabled.
                    #region Handle Kerning
                    adjustmentOffset = float2.zero;
                    float m_characterSpacing = 0;
                    GlyphBlob.GlyphAdjustment glyphAdjustments = new();
                    float characterSpacingAdjustment = m_characterSpacing;
                    float m_GlyphHorizontalAdvanceAdjustment = 0;
                    if (baseConfiguration.enableKerning)
                    {
                        GlyphBlob.AdjustmentPair adjustmentPair;
                        if (characterEnumerator.MoveNext())
                        {
                            var nextChar = characterEnumerator.Current.value;

                            for (int k = 0; k < glyphBlob.glyphAdjustments.Length; k++)
                            {
                                adjustmentPair = glyphBlob.glyphAdjustments[k];
                                if (adjustmentPair.secondAdjustment.glyphUnicode == math.asuint(nextChar))
                                {
                                    glyphAdjustments = adjustmentPair.firstAdjustment;
                                    characterSpacingAdjustment = (adjustmentPair.fontFeatureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) == FontFeatureLookupFlags.IgnoreSpacingAdjustments ? 0 : characterSpacingAdjustment;
                                    break;
                                }
                            }
                            characterEnumerator.MovePrevious();//rewind
                        }

                        if (textConfiguration.m_characterCount >= 1)
                        {
                            characterEnumerator.MovePrevious();
                            var prevChar = characterEnumerator.Current.value;

                            uint previousGlyphIndex;
                            if (font.TryGetGlyphIndex(math.asuint(characterEnumerator.Current.value), out var nextGlyphBlobIndex))
                                previousGlyphIndex = font.characters[glyphIndex].glyphIndex;

                            for (int k = 0; k < glyphBlob.glyphAdjustments.Length; k++)
                            {
                                adjustmentPair = glyphBlob.glyphAdjustments[k];
                                if (adjustmentPair.secondAdjustment.glyphUnicode == math.asuint(prevChar))
                                {
                                    glyphAdjustments += adjustmentPair.secondAdjustment;
                                    characterSpacingAdjustment = (adjustmentPair.fontFeatureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) == FontFeatureLookupFlags.IgnoreSpacingAdjustments ? 0 : characterSpacingAdjustment;
                                    break;
                                }
                            }
                            characterEnumerator.MoveNext();//undo rewind
                        }
                    }

                    m_GlyphHorizontalAdvanceAdjustment = glyphAdjustments.xAdvance;

                    adjustmentOffset.x = glyphAdjustments.xPlacement * currentElementScale;
                    adjustmentOffset.y = glyphAdjustments.yPlacement * currentElementScale;

                    cumulativeOffset.x += currentElementScale * glyphBlob.horizontalAdvance + glyphAdjustments.xAdvance * currentElementScale;
                    cumulativeOffset.y += glyphAdjustments.yAdvance * currentElementScale;
                    #endregion

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

                        var yOffsetChange = font.lineHeight * currentElementScale;
                        var xOffsetChange = renderGlyphs[lastWordStartCharacterGlyphIndex].blPosition.x;
                        if (xOffsetChange > 0 && !dropSpace)  // Always allow one visible character
                        {
                            // Finish line based on alignment
                            var glyphsLine = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex,
                                                                                      lastWordStartCharacterGlyphIndex - startOfLineGlyphIndex);
                            ApplyHorizontalAlignmentToGlyphs(ref glyphsLine,
                                                             ref characterGlyphIndicesWithPreceedingSpacesInLine,
                                                             baseConfiguration.maxLineWidth,
                                                             textConfiguration.m_lineJustification);
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
                var overrideMode = textConfiguration.m_lineJustification;
                if ((overrideMode) == HorizontalAlignmentOptions.Justified)
                {
                    // Don't perform justified spacing for the last line.
                    overrideMode = HorizontalAlignmentOptions.Left;
                }
                ApplyHorizontalAlignmentToGlyphs(ref finalGlyphsLine, ref characterGlyphIndicesWithPreceedingSpacesInLine, baseConfiguration.maxLineWidth, overrideMode);
            }
            lineCount++;
            ApplyVerticalAlignmentToGlyphs(ref renderGlyphs, lineCount, baseConfiguration.verticalAlignment, ref font, baseConfiguration.fontSize);
        }

        static unsafe void ApplyHorizontalAlignmentToGlyphs(ref NativeArray<RenderGlyph> glyphs,
                                                            ref FixedList512Bytes<int>   characterGlyphIndicesWithPreceedingSpacesInLine,
                                                            float width,
                                                            HorizontalAlignmentOptions alignMode)
        {
            if ((alignMode) == HorizontalAlignmentOptions.Left)
            {
                characterGlyphIndicesWithPreceedingSpacesInLine.Clear();
                return;
            }

            var glyphsPtr = (RenderGlyph*)glyphs.GetUnsafePtr();
            if ((alignMode) == HorizontalAlignmentOptions.Center)
            {
                float offset = glyphsPtr[glyphs.Length - 1].trPosition.x / 2f;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    glyphsPtr[i].blPosition.x -= offset;
                    glyphsPtr[i].trPosition.x -= offset;
                }
            }
            else if ((alignMode) == HorizontalAlignmentOptions.Right)
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

        static unsafe void ApplyVerticalAlignmentToGlyphs(ref DynamicBuffer<RenderGlyph> glyphs,
                                                          int fullLineCount,
                                                          VerticalAlignmentOptions alignMode,
                                                          ref FontBlob font,
                                                          float fontSize)
        {
            var glyphsPtr = (RenderGlyph*)glyphs.GetUnsafePtr();
            if ((alignMode) == VerticalAlignmentOptions.Top)
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
            else if ((alignMode) == VerticalAlignmentOptions.Middle)
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

