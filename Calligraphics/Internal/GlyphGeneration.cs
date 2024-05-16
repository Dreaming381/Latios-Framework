using Latios.Calligraphics.Rendering;
using Latios.Calligraphics.RichText;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Latios.Calligraphics
{
    internal static class GlyphGeneration
    {
        /// <summary> This function logic follows TMPro_Private.GenerateTextMesh() </summary>
        internal static unsafe void CreateRenderGlyphs(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                                                       ref GlyphMappingWriter mappingWriter,
                                                       ref FontMaterialSet fontMaterialSet,
                                                       ref TextConfigurationStack textConfigurationStack,
                                                       in DynamicBuffer<CalliByte>    calliBytes,
                                                       in TextBaseConfiguration baseConfiguration)
        {
            renderGlyphs.Clear();

            // Initialize textConfiguration which stores all fields that are modified by RichText Tags
            textConfigurationStack.Reset(baseConfiguration);
            var calliString = new CalliString(calliBytes);

            var          textConfiguration = textConfigurationStack.GetActiveConfiguration();
            ref FontBlob font              = ref fontMaterialSet[textConfiguration.m_currentFontMaterialIndex];

            int                    characterCount                                  = 0;
            int                    lastWordStartCharacterGlyphIndex                = 0;
            FixedList512Bytes<int> characterGlyphIndicesWithPreceedingSpacesInLine = default;
            int                    accumulatedSpaces                               = 0;
            int                    startOfLineGlyphIndex                           = 0;
            int                    lastCommittedStartOfLineGlyphIndex              = -1;
            int                    lineCount                                       = 0;
            bool                   isLineStart                                     = true;
            float                  currentLineHeight                               = 0f;
            float                  ascentLineDelta                                 = 0;
            float                  decentLineDelta                                 = 0;
            float                  accumulatedVerticalOffset                       = 0f;
            float                  maxLineAscender                                 = float.MinValue;
            float                  maxLineDescender                                = float.MaxValue;
            float                  xAdvance                                        = 0f;

            // Calculate the scale of the font based on selected font size and sampling point size.
            // baseScale is calculated using the font asset assigned to the text object.
            float baseScale           = baseConfiguration.fontSize / font.pointSize * font.scale * (baseConfiguration.isOrthographic ? 1 : 0.1f);
            float currentElementScale = baseScale;
            float currentEmScale      = baseConfiguration.fontSize * 0.01f * (baseConfiguration.isOrthographic ? 1 : 0.1f);

            float topAnchor    = GetTopAnchorForConfig(ref fontMaterialSet[0], baseConfiguration.verticalAlignment, baseScale);
            float bottomAnchor = GetBottomAnchorForConfig(ref fontMaterialSet[0], baseConfiguration.verticalAlignment, baseScale);

            PrevCurNext                 prevCurNext                 = new PrevCurNext(calliString.GetEnumerator());
            TextGenerationStateCommands textGenerationStateCommands = default;
            textGenerationStateCommands.Reset();
            bool needsActiveConfigurationUpdate = false;
            bool firstIteration                 = true;

            while (prevCurNext.nextIsValid)
            {
                // On the first iteration, we need to queue up the first character, which could be rich text.
                // So we process the first character, do rich text analysis, then skip to the second iteration
                // where we shift everything. Because from the second iteration on, we want to do rich text parsing
                // after advancing the prevCurNext.
                if (!firstIteration)
                {
                    // We update the count now so that RichTextParser receives the actual character count.
                    characterCount++;
                    prevCurNext.MoveNext();
                }

                // If on the previous iteration we detected a new Html tag, we need to update the active configuration.
                if (needsActiveConfigurationUpdate)
                {
                    textConfiguration = textConfigurationStack.GetActiveConfiguration();
                    if (textGenerationStateCommands.xAdvanceIsOverwrite)
                        xAdvance = textGenerationStateCommands.xAdvanceChange;
                    else
                        xAdvance += textGenerationStateCommands.xAdvanceChange;
                    textGenerationStateCommands.Reset();
                    needsActiveConfigurationUpdate = false;
                }

                while (prevCurNext.nextIsValid && prevCurNext.next.Current == '<')
                {
                    textConfigurationStack.m_isParsingText = true;
                    // Check if Tag is valid. If valid, skip to the end of the validated tag.
                    var nextEnumerator = prevCurNext.next;
                    if (RichTextParser.ValidateHtmlTag(in calliString, ref nextEnumerator, ref fontMaterialSet, in baseConfiguration, ref textConfigurationStack,
                                                       ref textGenerationStateCommands, characterCount))
                    {
                        prevCurNext.next               = nextEnumerator;
                        needsActiveConfigurationUpdate = true;
                        // Continue to next character
                        prevCurNext.nextIsValid = prevCurNext.next.MoveNext();
                        continue;
                    }
                    // We failed to parse something. We want to render the '<' instead.
                    break;
                }

                if (firstIteration)
                {
                    // Commit the first line to the writer
                    mappingWriter.AddLineStart(renderGlyphs.Length);
                    mappingWriter.AddWordStart(renderGlyphs.Length);
                    firstIteration = false;
                    continue;
                }

                var currentRune                        = prevCurNext.current.Current;
                font                                   = ref fontMaterialSet[textConfiguration.m_currentFontMaterialIndex];
                textConfigurationStack.m_isParsingText = false;
                if (lineCount == 0)
                    topAnchor = GetTopAnchorForConfig(ref font, baseConfiguration.verticalAlignment, baseScale, topAnchor);
                bottomAnchor  = GetBottomAnchorForConfig(ref font, baseConfiguration.verticalAlignment, baseScale, bottomAnchor);

                bool prevWasSpace = prevCurNext.previousIsValid && prevCurNext.prev.Current.value == 32;

                // Handle Font Styles like LowerCase, UpperCase and SmallCaps.
                SwapRune(ref currentRune, ref textConfiguration, out float smallCapsMultiplier);

                // Look up Character Data. TMP uses a backing array,
                // we pull character directly from FontBlob and continue when not found
                #region Look up Character Data
                if (!font.TryGetCharacterIndex(currentRune, out var currentCharIndex))
                    continue;

                ref var glyphBlob = ref font.characters[currentCharIndex];

                float adjustedScale      = textConfiguration.m_currentFontSize * smallCapsMultiplier / font.pointSize * font.scale * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                float elementAscentLine  = font.ascentLine;
                float elementDescentLine = font.descentLine;

                currentElementScale  = adjustedScale * textConfiguration.m_fontScaleMultiplier * glyphBlob.glyphScale;  //* m_cached_TextElement.m_Scale
                float baselineOffset = font.baseLine * adjustedScale * textConfiguration.m_fontScaleMultiplier * font.scale;
                #endregion

                // Handle Soft Hyphen
                #region Handle Soft Hyphen
                float currentElementUnmodifiedScale = currentElementScale;
                if (currentRune.value == 0xAD || currentRune.value == 0x03)
                    currentElementScale = 0;
                #endregion

                // Cache glyph metrics
                var currentGlyphMetrics = glyphBlob.glyphMetrics;

                // Optimization to avoid calling this more than once per character.
                bool isWhiteSpace = currentRune.value <= 0xFFFF && currentRune.IsWhiteSpace();

                // Handle Kerning if Enabled.
                #region Handle Kerning
                GlyphAdjustment glyphAdjustments           = new();
                float           characterSpacingAdjustment = 0;  //consider exposing initial characterSpacing in TextRenderer as characterSpacing;
                if (baseConfiguration.enableKerning)
                {
                    // Todo: If the active configuration changes between glyphs, we may want to cancel out of kerning.
                    if (prevCurNext.nextIsValid)
                    {
                        var nextRune = prevCurNext.next.Current;
                        SwapRune(ref nextRune, ref textConfiguration, out _);
                        if (glyphBlob.glyphAdjustmentsLookup.TryGetAdjustmentPairIndexForUnicodeAfter(nextRune.value, out var adjustmentIndex))
                        {
                            var adjustmentPair         = font.adjustmentPairs[adjustmentIndex];
                            glyphAdjustments           = adjustmentPair.firstAdjustment;
                            characterSpacingAdjustment = (adjustmentPair.fontFeatureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) ==
                                                         FontFeatureLookupFlags.IgnoreSpacingAdjustments ? 0 : characterSpacingAdjustment;
                        }
                    }

                    if (prevCurNext.previousIsValid)
                    {
                        var prevRune = prevCurNext.prev.Current;
                        SwapRune(ref prevRune, ref textConfiguration, out _);
                        if (glyphBlob.glyphAdjustmentsLookup.TryGetAdjustmentPairIndexForUnicodeBefore(prevRune.value, out var adjustmentIndex))
                        {
                            var adjustmentPair          = font.adjustmentPairs[adjustmentIndex];
                            glyphAdjustments           += adjustmentPair.secondAdjustment;
                            characterSpacingAdjustment  = (adjustmentPair.fontFeatureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) ==
                                                          FontFeatureLookupFlags.IgnoreSpacingAdjustments ? 0 : characterSpacingAdjustment;
                        }
                    }
                }
                #endregion

                // Handle Mono Spacing
                #region Handle Mono Spacing
                float monoAdvance = 0;
                if (textConfiguration.m_monoSpacing != 0)
                {
                    monoAdvance =
                        (textConfiguration.m_monoSpacing / 2 - (currentGlyphMetrics.width / 2 + currentGlyphMetrics.horizontalBearingX) * currentElementScale);  // * (1 - charWidthAdjDelta);
                    xAdvance += monoAdvance;
                }
                #endregion

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

                // Determine the position of the vertices of the Character or Sprite.
                #region Calculate Vertices Position
                var renderGlyph = new RenderGlyph { unicode = glyphBlob.unicode };

                // top left is used to position bottom left and top right
                float2 topLeft;
                topLeft.x = xAdvance +
                            ((currentGlyphMetrics.horizontalBearingX * textConfiguration.m_fxScale.x - font.materialPadding - style_padding + glyphAdjustments.xPlacement) *
                             currentElementScale);  // * (1 - m_charWidthAdjDelta));
                topLeft.y = baselineOffset + (currentGlyphMetrics.horizontalBearingY + font.materialPadding + glyphAdjustments.yPlacement) * currentElementScale -
                            textConfiguration.m_lineOffset + textConfiguration.m_baselineOffset;

                float2 bottomLeft;
                bottomLeft.x = topLeft.x;
                bottomLeft.y = topLeft.y - ((currentGlyphMetrics.height + font.materialPadding * 2) * currentElementScale);

                float2 topRight;
                topRight.x = bottomLeft.x + (currentGlyphMetrics.width * textConfiguration.m_fxScale.x + font.materialPadding * 2 + style_padding * 2) * currentElementScale;
                topRight.y = topLeft.y;

                // Bottom right unused
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

                #region Setup Color
                renderGlyph.blColor = textConfiguration.m_htmlColor;
                renderGlyph.tlColor = textConfiguration.m_htmlColor;
                renderGlyph.trColor = textConfiguration.m_htmlColor;
                renderGlyph.brColor = textConfiguration.m_htmlColor;
                #endregion

                #region Pack Scale into renderGlyph.scale
                var xScale = textConfiguration.m_currentFontSize;  // * math.abs(lossyScale) * (1 - m_charWidthAdjDelta);
                if ((textConfiguration.m_fontStyleInternal & FontStyles.Bold) == FontStyles.Bold)
                    xScale *= -1;

                renderGlyph.scale = xScale;
                #endregion

                // Check if we need to Shear the rectangles for Italic styles
                #region Handle Italic & Shearing
                float bottomShear = 0f;
                if ((textConfiguration.m_fontStyleInternal & FontStyles.Italic) == FontStyles.Italic)
                {
                    // Shift Top vertices forward by half (Shear Value * height of character) and Bottom vertices back by same amount.
                    float shear_value = textConfiguration.m_italicAngle * 0.01f;
                    float midPoint    = ((font.capLine - (font.baseLine + textConfiguration.m_baselineOffset)) / 2) * textConfiguration.m_fontScaleMultiplier * font.scale;
                    float topShear    = shear_value * ((currentGlyphMetrics.horizontalBearingY + font.materialPadding + style_padding - midPoint) * currentElementScale);
                    bottomShear       = shear_value *
                                        ((currentGlyphMetrics.horizontalBearingY - currentGlyphMetrics.height - font.materialPadding - style_padding - midPoint) *
                                         currentElementScale);

                    topLeft.x    += topShear;
                    bottomLeft.x += bottomShear;
                    topRight.x   += topShear;

                    renderGlyph.shear = topLeft.x - bottomLeft.x;
                }
                #endregion Handle Italics & Shearing

                // Handle Character FX Rotation
                #region Handle Character FX Rotation
                renderGlyph.rotationCCW = textConfiguration.m_fxRotationAngleCCW;
                #endregion

                #region Store vertex information for the character or sprite.
                renderGlyph.trPosition = topRight;
                renderGlyph.blPosition = bottomLeft;
                renderGlyphs.Add(renderGlyph);
                fontMaterialSet.WriteFontMaterialIndexForGlyph(textConfiguration.m_currentFontMaterialIndex);
                mappingWriter.AddCharNoTags(characterCount - 1, true);
                mappingWriter.AddCharWithTags(prevCurNext.current.CurrentCharIndex, true);
                mappingWriter.AddBytes(prevCurNext.current.CurrentByteIndex, currentRune.LengthInUtf8Bytes(), true);
                #endregion

                // Compute text metrics
                #region Compute Ascender & Descender values
                // Element Ascender in line space
                float elementAscender = elementAscentLine * currentElementScale / smallCapsMultiplier + textConfiguration.m_baselineOffset;

                // Element Descender in line space
                float elementDescender = elementDescentLine * currentElementScale / smallCapsMultiplier + textConfiguration.m_baselineOffset;

                float adjustedAscender  = elementAscender;
                float adjustedDescender = elementDescender;

                // Max line ascender and descender in line space
                if (isLineStart || isWhiteSpace == false)
                {
                    // Special handling for Superscript and Subscript where we use the unadjusted line ascender and descender
                    if (textConfiguration.m_baselineOffset != 0)
                    {
                        adjustedAscender  = math.max((elementAscender - textConfiguration.m_baselineOffset) / textConfiguration.m_fontScaleMultiplier, adjustedAscender);
                        adjustedDescender = math.min((elementDescender - textConfiguration.m_baselineOffset) / textConfiguration.m_fontScaleMultiplier, adjustedDescender);
                    }
                    maxLineAscender  = math.max(adjustedAscender, maxLineAscender);
                    maxLineDescender = math.min(adjustedDescender, maxLineDescender);
                }
                #endregion

                // Handle xAdvance & Tabulation Stops. Tab stops at every 25% of Font Size.
                #region XAdvance, Tabulation & Stops
                if (currentRune.value == 9)
                {
                    float tabSize = font.tabWidth * font.tabMultiple * currentElementScale;
                    float tabs    = math.ceil(xAdvance / tabSize) * tabSize;
                    xAdvance      = tabs > xAdvance ? tabs : xAdvance + tabSize;
                }
                else if (textConfiguration.m_monoSpacing != 0)
                {
                    float monoAdjustment  = textConfiguration.m_monoSpacing - monoAdvance;
                    xAdvance             += (monoAdjustment + ((font.regularStyleSpacing + characterSpacingAdjustment) * currentEmScale) + textConfiguration.m_cSpacing);  // * (1 - m_charWidthAdjDelta);
                    if (isWhiteSpace || currentRune.value == 0x200B)
                        xAdvance += baseConfiguration.wordSpacing * currentEmScale;
                }
                else
                {
                    xAdvance +=
                        ((currentGlyphMetrics.horizontalAdvance * textConfiguration.m_fxScale.x + glyphAdjustments.xAdvance) * currentElementScale +
                         (font.regularStyleSpacing + characterSpacingAdjustment + boldSpacingAdjustment) * currentEmScale + textConfiguration.m_cSpacing);  // * (1 - m_charWidthAdjDelta);

                    if (isWhiteSpace || currentRune.value == 0x200B)
                        xAdvance += baseConfiguration.wordSpacing * currentEmScale;
                }
                #endregion XAdvance, Tabulation & Stops

                #region Check for Line Feed and Last Character
                if (isLineStart)
                    isLineStart   = false;
                currentLineHeight = font.lineHeight * baseScale;  //why not (font.ascentLine-font.baseLine) * baseScale ?
                ascentLineDelta   = maxLineAscender - font.ascentLine * baseScale;
                decentLineDelta   = font.descentLine * baseScale - maxLineDescender;
                //if (currentRune.value == 10 || currentRune.value == 11 || currentRune.value == 0x03 || currentRune.value == 0x2028 ||
                //    currentRune.value == 0x2029 || textConfiguration.m_characterCount == calliString.Length - 1)
                if (currentRune.value == 10)
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
                    startOfLineGlyphIndex = renderGlyphs.Length;
                    if (lineCount > 0)
                    {
                        accumulatedVerticalOffset += currentLineHeight + ascentLineDelta;
                        if (lastCommittedStartOfLineGlyphIndex != startOfLineGlyphIndex)
                        {
                            ApplyVerticalOffsetToGlyphs(ref glyphsLine, accumulatedVerticalOffset);
                            lastCommittedStartOfLineGlyphIndex = startOfLineGlyphIndex;
                        }
                    }
                    accumulatedVerticalOffset += decentLineDelta;
                    //apply user configurable line and paragraph spacing
                    accumulatedVerticalOffset +=
                        (baseConfiguration.lineSpacing + (currentRune.value == 10 || currentRune.value == 0x2029 ? baseConfiguration.paragraphSpacing : 0)) * currentEmScale;
                    //reset line status
                    maxLineAscender  = float.MinValue;
                    maxLineDescender = float.MaxValue;

                    lineCount++;
                    isLineStart  = true;
                    bottomAnchor = GetBottomAnchorForConfig(ref font, baseConfiguration.verticalAlignment, baseScale);

                    // Commit the line to the writer
                    mappingWriter.AddLineStart(renderGlyphs.Length);

                    xAdvance = 0;
                }
                #endregion

                #region Word Wrapping
                // Handle word wrap
                if (currentRune.value != 10 &&
                    baseConfiguration.maxLineWidth < float.MaxValue &&
                    baseConfiguration.maxLineWidth > 0 &&
                    xAdvance > baseConfiguration.maxLineWidth)
                {
                    bool dropSpace = false;
                    if (currentRune.value == 32 && !prevWasSpace)
                    {
                        // What pushed us past the line width was a space character.
                        // The previous character was not a space, and we don't
                        // want to render this character at the start of the next line.
                        // We drop this space character instead and allow the next
                        // character to line-wrap, space or not.
                        dropSpace = true;
                        accumulatedSpaces--;
                    }

                    var yOffsetChange = 0f;  //font.lineHeight * currentElementScale;
                    var xOffsetChange = renderGlyphs[lastWordStartCharacterGlyphIndex].blPosition.x - bottomShear;
                    if (xOffsetChange > 0 && !dropSpace)  // Always allow one visible character
                    {
                        // Finish line based on alignment
                        var glyphsLine = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex,
                                                                                  lastWordStartCharacterGlyphIndex - startOfLineGlyphIndex);
                        ApplyHorizontalAlignmentToGlyphs(ref glyphsLine,
                                                         ref characterGlyphIndicesWithPreceedingSpacesInLine,
                                                         baseConfiguration.maxLineWidth,
                                                         textConfiguration.m_lineJustification);

                        if (lineCount > 0)
                        {
                            accumulatedVerticalOffset += currentLineHeight + ascentLineDelta;
                            ApplyVerticalOffsetToGlyphs(ref glyphsLine, accumulatedVerticalOffset);
                            lastCommittedStartOfLineGlyphIndex = startOfLineGlyphIndex;
                        }
                        accumulatedVerticalOffset += decentLineDelta;  // Todo: Delta should be computed per glyph
                        //apply user configurable line and paragraph spacing
                        accumulatedVerticalOffset +=
                            (baseConfiguration.lineSpacing +
                             (currentRune.value == 10 || currentRune.value == 0x2029 ? baseConfiguration.paragraphSpacing : 0)) * currentEmScale;

                        //reset line status
                        maxLineAscender  = float.MinValue;
                        maxLineDescender = float.MaxValue;

                        startOfLineGlyphIndex = lastWordStartCharacterGlyphIndex;
                        isLineStart           = true;
                        lineCount++;

                        xAdvance -= xOffsetChange;

                        // Adjust the vertices of the previous render glyphs in the word
                        var glyphPtr = (RenderGlyph*)renderGlyphs.GetUnsafePtr();
                        for (int i = lastWordStartCharacterGlyphIndex; i < renderGlyphs.Length; i++)
                        {
                            glyphPtr[i].blPosition.y -= yOffsetChange;
                            glyphPtr[i].blPosition.x -= xOffsetChange;
                            glyphPtr[i].trPosition.y -= yOffsetChange;
                            glyphPtr[i].trPosition.x -= xOffsetChange;
                        }

                        // Commit the line to the writer
                        // Todo: Currently, lines are paragraphs. Once we have soft lines,
                        // this is where we want the addition to go. I tested this. It works.
                        // mappingWriter.AddLineStart(lastWordStartCharacterGlyphIndex);
                    }
                }

                //Detect start of word
                if (currentRune.value == 10 ||  // line feed
                    currentRune.value == 11 ||  // vertical tab
                    currentRune.value == 13 ||  // carriage return
                    currentRune.value == 32 ||  //Space
                    currentRune.value == 9 ||  //Tab
                    currentRune.value == 45 ||  //Hyphen Minus
                    currentRune.value == 173 ||  //Soft hyphen
                    currentRune.value == 8203 ||  //Zero width space
                    currentRune.value == 8204 ||  //Zero width non-joiner
                    currentRune.value == 8205)  //Zero width joiner
                {
                    lastWordStartCharacterGlyphIndex = renderGlyphs.Length;
                }
                else if (lastWordStartCharacterGlyphIndex + 1 == renderGlyphs.Length)
                {
                    // We have to delay by one or else we register a word for every space.
                    mappingWriter.AddWordStart(lastWordStartCharacterGlyphIndex);
                }

                if (currentRune.value == 32)
                    accumulatedSpaces++;
                #endregion
            }

            var finalGlyphsLine = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex, renderGlyphs.Length - startOfLineGlyphIndex);
            {
                var overrideMode = textConfiguration.m_lineJustification;
                if (overrideMode == HorizontalAlignmentOptions.Justified)
                {
                    // Don't perform justified spacing for the last line.
                    overrideMode = HorizontalAlignmentOptions.Left;
                }
                ApplyHorizontalAlignmentToGlyphs(ref finalGlyphsLine, ref characterGlyphIndicesWithPreceedingSpacesInLine, baseConfiguration.maxLineWidth, overrideMode);
                if (lineCount > 0)
                {
                    accumulatedVerticalOffset += currentLineHeight;
                    ApplyVerticalOffsetToGlyphs(ref finalGlyphsLine, accumulatedVerticalOffset);
                }
            }
            lineCount++;
            ApplyVerticalAlignmentToGlyphs(ref renderGlyphs, topAnchor, bottomAnchor, accumulatedVerticalOffset, baseConfiguration.verticalAlignment);
        }

        public struct PrevCurNext
        {
            public CalliString.Enumerator prev;
            public CalliString.Enumerator current;
            public CalliString.Enumerator next;

            public bool previousIsValid;
            public bool currentIsValid;
            public bool nextIsValid;

            public PrevCurNext(CalliString.Enumerator enumerator)
            {
                previousIsValid = false;
                prev            = default;
                currentIsValid  = false;
                current         = default;
                nextIsValid     = enumerator.MoveNext();
                next            = enumerator;
            }

            public void MoveNext()
            {
                prev            = current;
                current         = next;
                previousIsValid = currentIsValid;
                currentIsValid  = nextIsValid;
                nextIsValid     = next.MoveNext();
            }
        }

        static float GetTopAnchorForConfig(ref FontBlob font, VerticalAlignmentOptions verticalMode, float baseScale, float oldValue = float.PositiveInfinity)
        {
            bool replace = oldValue == float.PositiveInfinity;
            switch (verticalMode)
            {
                case VerticalAlignmentOptions.TopBase: return 0f;
                case VerticalAlignmentOptions.MiddleTopAscentToBottomDescent:
                case VerticalAlignmentOptions.TopAscent: return baseScale * math.max(font.ascentLine - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                case VerticalAlignmentOptions.TopDescent: return baseScale * math.min(font.descentLine - font.baseLine, oldValue);
                case VerticalAlignmentOptions.TopCap: return baseScale * math.max(font.capLine - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                case VerticalAlignmentOptions.TopMean: return baseScale * math.max(font.meanLine - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                default: return 0f;
            }
        }

        static float GetBottomAnchorForConfig(ref FontBlob font, VerticalAlignmentOptions verticalMode, float baseScale, float oldValue = float.PositiveInfinity)
        {
            bool replace = oldValue == float.PositiveInfinity;
            switch (verticalMode)
            {
                case VerticalAlignmentOptions.BottomBase: return 0f;
                case VerticalAlignmentOptions.BottomAscent: return baseScale * math.max(font.ascentLine - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                case VerticalAlignmentOptions.MiddleTopAscentToBottomDescent:
                case VerticalAlignmentOptions.BottomDescent: return baseScale * math.min(font.descentLine - font.baseLine, oldValue);
                case VerticalAlignmentOptions.BottomCap: return baseScale * math.max(font.capLine - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                case VerticalAlignmentOptions.BottomMean: return baseScale * math.max(font.meanLine - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                default: return 0f;
            }
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

        static unsafe void ApplyVerticalOffsetToGlyphs(ref NativeArray<RenderGlyph> glyphs, float accumulatedVerticalOffset)
        {
            for (int i = 0; i < glyphs.Length; i++)
            {
                var glyph           = glyphs[i];
                glyph.blPosition.y -= accumulatedVerticalOffset;
                glyph.trPosition.y -= accumulatedVerticalOffset;
                glyphs[i]           = glyph;
            }
        }

        static unsafe void ApplyVerticalAlignmentToGlyphs(ref DynamicBuffer<RenderGlyph> glyphs,
                                                          float topAnchor,
                                                          float bottomAnchor,
                                                          float accumulatedVerticalOffset,
                                                          VerticalAlignmentOptions alignMode)
        {
            var glyphsPtr = (RenderGlyph*)glyphs.GetUnsafePtr();
            switch (alignMode)
            {
                case VerticalAlignmentOptions.TopBase:
                    return;
                case VerticalAlignmentOptions.TopAscent:
                case VerticalAlignmentOptions.TopDescent:
                case VerticalAlignmentOptions.TopCap:
                case VerticalAlignmentOptions.TopMean:
                {
                    // Positions were calculated relative to the baseline.
                    // Shift everything down so that y = 0 is on the target line.
                    for (int i = 0; i < glyphs.Length; i++)
                    {
                        glyphsPtr[i].blPosition.y -= topAnchor;
                        glyphsPtr[i].trPosition.y -= topAnchor;
                    }
                    break;
                }
                case VerticalAlignmentOptions.BottomBase:
                case VerticalAlignmentOptions.BottomAscent:
                case VerticalAlignmentOptions.BottomDescent:
                case VerticalAlignmentOptions.BottomCap:
                case VerticalAlignmentOptions.BottomMean:
                {
                    float offset = accumulatedVerticalOffset - bottomAnchor;
                    for (int i = 0; i < glyphs.Length; i++)
                    {
                        glyphsPtr[i].blPosition.y += offset;
                        glyphsPtr[i].trPosition.y += offset;
                    }
                    break;
                }
                case VerticalAlignmentOptions.MiddleTopAscentToBottomDescent:
                {
                    float fullHeight = accumulatedVerticalOffset - bottomAnchor + topAnchor;
                    float offset     = fullHeight / 2f;
                    for (int i = 0; i < glyphs.Length; i++)
                    {
                        glyphsPtr[i].blPosition.y += offset;
                        glyphsPtr[i].trPosition.y += offset;
                    }
                    break;
                }
            }
        }

        static unsafe void SwapRune(ref Unicode.Rune rune, ref ActiveTextConfiguration textConfiguration, out float smallCapsMultiplier)
        {
            smallCapsMultiplier = 1f;

            // Todo: Burst does not support language methods, and char only supports the UTF-16 subset
            // of characters. We should encode upper and lower cross-references into the font blobs or
            // figure out the formulas for all other languages. Right now only ascii is supported.
            if ((textConfiguration.m_fontStyleInternal & FontStyles.UpperCase) == FontStyles.UpperCase)
            {
                // If this character is lowercase, switch to uppercase.
                rune = rune.ToUpper();
            }
            else if ((textConfiguration.m_fontStyleInternal & FontStyles.LowerCase) == FontStyles.LowerCase)
            {
                // If this character is uppercase, switch to lowercase.
                rune = rune.ToLower();
            }
            else if ((textConfiguration.m_fontStyleInternal & FontStyles.SmallCaps) == FontStyles.SmallCaps)
            {
                var oldUnicode = rune;
                rune           = rune.ToUpper();
                if (rune != oldUnicode)
                {
                    smallCapsMultiplier = 0.8f;
                }
            }
        }
    }
}

