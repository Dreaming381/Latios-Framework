using System.Runtime.CompilerServices;
using Latios.Calligraphics.Rendering;
using Latios.Calligraphics.RichText;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

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
                    if (RichTextParser.ValidateHtmlTag(in calliString, ref characterEnumerator, ref fontMaterialSet, in baseConfiguration, ref textConfiguration,
                                                       ref richTextTagIdentifiers))
                    {
                        // Continue to next character
                        continue;
                    }
                }
                #endregion

                ref FontBlob font                 = ref fontMaterialSet[textConfiguration.m_currentFontMaterialIndex];
                textConfiguration.m_isParsingText = false;

                // Handle Font Styles like LowerCase, UpperCase and SmallCaps.
                #region Handling of LowerCase, UpperCase and SmallCaps Font Styles

                float smallCapsMultiplier = 1.0f;

                // Todo: Burst does not support language methods, and char only supports the UTF-16 subset
                // of characters. We should encode upper and lower cross-references into the font blobs or
                // figure out the formulas for all other languages. Right now only ascii is supported.
                if ((textConfiguration.m_fontStyleInternal & FontStyles.UpperCase) == FontStyles.UpperCase)
                {
                    // If this character is lowercase, switch to uppercase.
                    unicode = unicode.ToUpper();
                }
                else if ((textConfiguration.m_fontStyleInternal & FontStyles.LowerCase) == FontStyles.LowerCase)
                {
                    // If this character is uppercase, switch to lowercase.
                    unicode = unicode.ToLower();
                }
                else if ((textConfiguration.m_fontStyleInternal & FontStyles.SmallCaps) == FontStyles.SmallCaps)
                {
                    var oldUnicode = unicode;
                    unicode        = unicode.ToUpper();
                    if (unicode != oldUnicode)
                    {
                        smallCapsMultiplier = 0.8f;
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

                if (font.TryGetGlyphIndex(unicode, out var glyphIndex))
                {
                    ref var glyphBlob   = ref font.characters[glyphIndex];
                    var     renderGlyph = new RenderGlyph
                    {
                        unicode = glyphBlob.unicode,
                        blColor = textConfiguration.m_htmlColor,
                        tlColor = textConfiguration.m_htmlColor,
                        trColor = textConfiguration.m_htmlColor,
                        brColor = textConfiguration.m_htmlColor,
                    };

                    // Set Padding based on selected font style
                    #region Handle Style Padding
                    float boldSpacingAdjustment = 0;
                    float style_padding         = 0;
                    if ((textConfiguration.m_fontStyleInternal & FontStyles.Bold) == FontStyles.Bold)
                    {
                        style_padding         = 0;
                        boldSpacingAdjustment = font.boldStyleSpacing;
                    }
                    #endregion Handle Style Padding

                    var adjustedScale = textConfiguration.m_currentFontSize * smallCapsMultiplier / font.pointSize * font.scale *
                                        (baseConfiguration.isOrthographic ? 1 : 0.1f);
                    var   currentElementScale = adjustedScale * textConfiguration.m_fontScaleMultiplier * glyphBlob.glyphScale;  //* m_cached_TextElement.m_Scale
                    float currentEmScale      = baseConfiguration.fontSize * 0.01f * (baseConfiguration.isOrthographic ? 1 : 0.1f);

                    // Determine the position of the vertices of the Character
                    #region Calculate Vertices Position
                    var    currentGlyphMetrics = glyphBlob.glyphMetrics;
                    float2 topLeft;
                    topLeft.x = (currentGlyphMetrics.horizontalBearingX * textConfiguration.m_FXScale.x - font.materialPadding - style_padding) * currentElementScale;
                    topLeft.y = (currentGlyphMetrics.horizontalBearingY + font.materialPadding) * currentElementScale - textConfiguration.m_lineOffset +
                                textConfiguration.m_baselineOffset;

                    float2 bottomLeft;
                    bottomLeft.x = topLeft.x;
                    bottomLeft.y = topLeft.y - ((currentGlyphMetrics.height + font.materialPadding * 2) * currentElementScale);

                    float2 topRight;
                    topRight.x = bottomLeft.x + (currentGlyphMetrics.width * textConfiguration.m_FXScale.x + font.materialPadding * 2 + style_padding * 2) * currentElementScale;
                    topRight.y = topLeft.y;

                    float2 bottomRight;
                    bottomRight.x = topRight.x;
                    bottomRight.y = bottomLeft.y;
                    #endregion

                    #region Setup UVA
                    var    glyphRect = glyphBlob.glyphRect;
                    float2 blUVA, tlUVA, trUVA, brUVA;
                    blUVA.x = (glyphRect.x - font.materialPadding - style_padding) / font.atlasWidth;
                    blUVA.y = (glyphRect.y - font.materialPadding - style_padding) / font.atlasHeight;

                    tlUVA.x = blUVA.x;
                    tlUVA.y = (glyphRect.y + font.materialPadding + style_padding + glyphRect.height) / font.atlasHeight;

                    trUVA.x = (glyphRect.x + font.materialPadding + style_padding + glyphRect.width) / font.atlasWidth;
                    trUVA.y = tlUVA.y;

                    brUVA.x = trUVA.x;
                    brUVA.y = blUVA.y;

                    renderGlyph.blUVA = blUVA;
                    renderGlyph.trUVA = trUVA;
                    #endregion

                    #region Setup UVB
                    //Setup UV2 based on Character Mapping Options Selected
                    //m_horizontalMapping case TextureMappingOptions.Character
                    float2 blUVC, tlUVC, trUVC, brUVC;
                    blUVC.x = 0;
                    tlUVC.x = 0;
                    trUVC.x = 1;
                    brUVC.x = 1;

                    //m_verticalMapping case case TextureMappingOptions.Character
                    blUVC.y = 0;
                    tlUVC.y = 1;
                    trUVC.y = 1;
                    brUVC.y = 0;

                    renderGlyph.blUVB = blUVC;
                    renderGlyph.tlUVB = tlUVA;
                    renderGlyph.trUVB = trUVC;
                    renderGlyph.brUVB = brUVA;
                    #endregion

                    // Check if we need to Shear the rectangles for Italic styles
                    #region Handle Italic & Shearing
                    if (((textConfiguration.m_fontStyleInternal & FontStyles.Italic) == FontStyles.Italic))
                    {
                        // Shift Top vertices forward by half (Shear Value * height of character) and Bottom vertices back by same amount.
                        float  shear       = textConfiguration.m_italicAngle * 0.01f;
                        float2 topShear    = new float2(shear * ((currentGlyphMetrics.horizontalBearingY + font.materialPadding + style_padding) * currentElementScale), 0);
                        float2 bottomShear =
                            new float2(
                                shear * (((currentGlyphMetrics.horizontalBearingY - currentGlyphMetrics.height - font.materialPadding - style_padding)) * currentElementScale),
                                0);
                        float2 shearAdjustment = (topShear - bottomShear) * 0.5f;

                        topShear    -= shearAdjustment;
                        bottomShear -= shearAdjustment;

                        topLeft     += topShear;
                        bottomLeft  += bottomShear;
                        topRight    += topShear;
                        bottomRight += bottomShear;

                        renderGlyph.shear = (topLeft.x - bottomLeft.x);
                    }
                    #endregion Handle Italics & Shearing

                    // Handle Character FX Rotation
                    #region Handle Character FX Rotation
                    renderGlyph.rotationCCW = textConfiguration.m_FXRotationAngle;
                    #endregion

                    #region handle bold
                    var xScale = textConfiguration.m_currentFontSize;  // * math.abs(lossyScale) * (1 - m_charWidthAdjDelta);
                    if ((textConfiguration.m_fontStyleInternal & FontStyles.Bold) == FontStyles.Bold)
                        xScale *= -1;

                    renderGlyph.scale = xScale;
                    #endregion

                    #region apply offsets
                    var offset   = adjustmentOffset + cumulativeOffset;
                    topLeft     += offset;
                    bottomLeft  += offset;
                    topRight    += offset;
                    bottomRight += offset;
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
                    adjustmentOffset                                   = float2.zero;
                    float           m_characterSpacing                 = 0;
                    GlyphAdjustment glyphAdjustments                   = new();
                    float           characterSpacingAdjustment         = m_characterSpacing;
                    float           m_GlyphHorizontalAdvanceAdjustment = 0;
                    if (baseConfiguration.enableKerning)
                    {
                        if (characterEnumerator.MoveNext())
                        {
                            var nextChar = characterEnumerator.Current;

                            if (font.TryGetGlyphIndex(nextChar, out var nextGlyphIndex))
                            {
                                if (glyphBlob.glyphAdjustmentsLookup.TryGetAdjustmentPairIndexForGlyphAfter(nextGlyphIndex, out var adjustmentIndex))
                                {
                                    var adjustmentPair         = font.adjustmentPairs[adjustmentIndex];
                                    glyphAdjustments           = adjustmentPair.firstAdjustment;
                                    characterSpacingAdjustment = (adjustmentPair.fontFeatureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) ==
                                                                 FontFeatureLookupFlags.IgnoreSpacingAdjustments ? 0 : characterSpacingAdjustment;
                                }
                            }
                            characterEnumerator.MovePrevious();  //rewind
                        }

                        if (textConfiguration.m_characterCount >= 1)
                        {
                            characterEnumerator.MovePrevious();
                            var prevChar = characterEnumerator.Current;

                            if (font.TryGetGlyphIndex(prevChar, out var previousGlyphIndex))
                            {
                                if (glyphBlob.glyphAdjustmentsLookup.TryGetAdjustmentPairIndexForGlyphBefore(previousGlyphIndex, out var adjustmentIndex))
                                {
                                    var adjustmentPair          = font.adjustmentPairs[adjustmentIndex];
                                    glyphAdjustments           += adjustmentPair.secondAdjustment;
                                    characterSpacingAdjustment  = (adjustmentPair.fontFeatureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) ==
                                                                  FontFeatureLookupFlags.IgnoreSpacingAdjustments ? 0 : characterSpacingAdjustment;
                                }
                            }
                            characterEnumerator.MoveNext();  //undo rewind
                        }
                    }

                    m_GlyphHorizontalAdvanceAdjustment = glyphAdjustments.xAdvance;

                    adjustmentOffset.x = glyphAdjustments.xPlacement * currentElementScale;
                    adjustmentOffset.y = glyphAdjustments.yPlacement * currentElementScale;

                    cumulativeOffset.x +=
                        ((currentGlyphMetrics.horizontalAdvance * textConfiguration.m_FXScale.x + glyphAdjustments.xAdvance) * currentElementScale +
                         (font.regularStyleSpacing + characterSpacingAdjustment + boldSpacingAdjustment) * currentEmScale + textConfiguration.m_cSpacing);  // * (1 - m_charWidthAdjDelta);
                    cumulativeOffset.y += glyphAdjustments.yAdvance * currentElementScale;
                    #endregion

                    #region Word Wrapping
                    // Apply accumulated spaces to non-space character
                    while (unicode.value != 32 && accumulatedSpaces > 0)
                    {
                        // We add the glyph entry for each proceeding whitespace, so that the justified offset is
                        // "weighted" by the preceeding number of spaces.
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
                            // What pushed us past the line width was a space character.
                            // The previous character was not a space, and we don't
                            // want to render this character at the start of the next line.
                            // We drop this space character instead and allow the next
                            // character to line-wrap, space or not.
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
                    #endregion
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
            ApplyVerticalAlignmentToGlyphs(ref renderGlyphs, lineCount, baseConfiguration.verticalAlignment, ref fontMaterialSet[0], baseConfiguration.fontSize);
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

