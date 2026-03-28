using Latios.Calligraphics.HarfBuzz;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics.Systems
{
    public partial struct GenerateGlyphsSystem
    {
        [BurstCompile]
        partial struct GenerateRenderGlyphsJob : IJobChunk
        {
            public BufferTypeHandle<RenderGlyph>         renderGlyphHandle;
            public BufferTypeHandle<PreviousRenderGlyph> previousRenderGlyphHandle;

            [ReadOnly] internal FontTable  fontTable;
            [ReadOnly] internal GlyphTable glyphTable;

            [ReadOnly] public NativeStream.Reader glyphOTFStream;
            [ReadOnly] public NativeStream.Reader xmlTagStream;
            [ReadOnly] public NativeArray<int>    firstEntityIndexInChunk;

            [ReadOnly] public BufferTypeHandle<CalliByte>                calliByteHandle;
            [ReadOnly] public ComponentTypeHandle<TextBaseConfiguration> textBaseConfigurationHandle;

            public Entity                                     textColorGradientEntity;
            [ReadOnly] public BufferLookup<TextColorGradient> textColorGradientLookup;

            public uint lastSystemVersion;

            [NativeSetThreadIndex]
            int threadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!(chunk.DidChange(ref calliByteHandle, lastSystemVersion) ||
                      chunk.DidChange(ref textBaseConfigurationHandle, lastSystemVersion)))
                    return;

                var firstEntityIndex = firstEntityIndexInChunk[unfilteredChunkIndex];
                //Debug.Log("Generate glyphs job");
                var calliBytesBuffers          = chunk.GetBufferAccessor(ref calliByteHandle);
                var renderGlyphBuffers         = chunk.GetBufferAccessor(ref renderGlyphHandle);
                var previousRenderGlyphBuffers = chunk.GetBufferAccessor(ref previousRenderGlyphHandle);
                var textBaseConfigurations     = chunk.GetNativeArray(ref textBaseConfigurationHandle);

                TextColorGradientArray textColorGradientArray = default;
                textColorGradientArray.Initialize(textColorGradientEntity, textColorGradientLookup);

                for (int indexInChunk = 0; indexInChunk < chunk.Count; indexInChunk++)
                {
                    int entityIndex = firstEntityIndex + indexInChunk;
                    var xmlTagCount = xmlTagStream.BeginForEachIndex(entityIndex);
                    var xmlTags     = new NativeArray<XMLTag>(xmlTagCount, Allocator.Temp);
                    for (int i = 0; i < xmlTagCount; i++)
                        xmlTags[i] = xmlTagStream.Read<XMLTag>();
                    xmlTagStream.EndForEachIndex();

                    var calliBytes                = calliBytesBuffers[indexInChunk];
                    var renderGlyphs              = renderGlyphBuffers[indexInChunk];
                    var previousRenderGlyphBuffer = previousRenderGlyphBuffers[indexInChunk];
                    var textBaseConfiguration     = textBaseConfigurations[indexInChunk];

                    renderGlyphs.Clear();
                    previousRenderGlyphBuffer.Clear();
                    var glyphCount = glyphOTFStream.BeginForEachIndex(entityIndex);
                    if (glyphCount == 0)
                        continue;

                    previousRenderGlyphBuffer.Capacity = glyphCount;  //allocating here make this job 2x slower but UpdateChangedGlyphsJob 10x faster
                    //renderGlyphs.Capacity = glyphCount; //not needed when done via single threaded pre-allocationjob
                    CreateRenderGlyphs(ref renderGlyphs,
                                       in calliBytes,
                                       ref glyphOTFStream,
                                       ref xmlTags,
                                       in textBaseConfiguration,
                                       ref textColorGradientArray);
                    glyphOTFStream.EndForEachIndex();
                }
            }

            unsafe void CreateRenderGlyphs(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                                           in DynamicBuffer<CalliByte>    calliBytesBuffer,
                                           ref NativeStream.Reader glyphOTFStream,
                                           ref NativeArray<XMLTag>        xmlTags,
                                           in TextBaseConfiguration textBaseConfiguration,
                                           ref TextColorGradientArray textColorGradientArray)
            {
                //Debug.Log("CreateRenderGlyphs");
                var calliString = new CalliString(calliBytesBuffer);
                var characters  = calliString.GetEnumerator();

                var fontLookupKeys = fontTable.fontLookupKeys;
                var layoutConfig  = new LayoutConfig(in textBaseConfiguration);

                XMLTag currentTag                   = default;
                int    tagsCounter                  = 0;
                int    nextSegmentEndID             = xmlTags.Length > 0 ? xmlTags[tagsCounter].startID : calliString.Length;
                int    cleanedSegmentLength         = nextSegmentEndID - currentTag.endID;
                int    richTextOffset               = 0;
                int    nextTagPositionInCleanedText = cleanedSegmentLength;
                //Debug.Log($"{currentTag.tagType} {cleanedSegmentLength} {nextTagPositionInCleanedText}");

                int                    lastWordStartCharacterGlyphIndex                = 0;
                FixedList512Bytes<int> characterGlyphIndicesWithPreceedingSpacesInLine = default;
                int                    startOfLineGlyphIndex                           = 0;
                int                    lastCommittedStartOfLineGlyphIndex              = -1;
                bool                   isFirstLine                                     = true;
                bool                   isLineStart                                     = true;
                float                  currentLineHeight                               = 0f;
                float                  ascentLineDelta                                 = 0;
                float                  decentLineDelta                                 = 0;
                float                  accumulatedVerticalOffset                       = 0f;
                float                  maxLineAscender                                 = float.MinValue;
                float                  maxLineDescender                                = float.MaxValue;

                //var glyphOTF = glyphOTFBuffer[0];
                var glyphOTF   = glyphOTFStream.Peek<GlyphOTF>();
                var glyphID    = glyphTable.glyphHashToIdMap[glyphOTF.glyphKey];
                var glyphEntry = glyphTable.GetEntry(glyphID);

                var currentFaceIndex = glyphOTF.glyphKey.faceIndex;
                var currentFace      = fontTable.faces[currentFaceIndex];
                var currentFont      = fontTable.GetOrCreateFont(currentFaceIndex, threadIndex);
                if (currentFace.HasVarData && currentFont.currentVariableProfileIndex != glyphEntry.key.variableProfileIndex)
                    currentFont = fontTable.SetVariableProfile(currentFaceIndex, threadIndex, glyphEntry.key.variableProfileIndex);

                var currentFontSamplingPointSize = glyphOTF.glyphKey.GetSamplingSize();
                var currentFontWeigth            = currentFont.GetStyleTag(StyleTag.WEIGHT);
                var currentFontIsItalic          = (byte)currentFont.GetStyleTag(StyleTag.ITALIC) == 1;
                currentFont.SetScale(currentFontSamplingPointSize, currentFontSamplingPointSize);
                // Todo: Don't hardcode these when line-wrapping is moved to shaping
                currentFont.UpdateMetaData(Direction.LTR, Script.LATIN, Language.English);

                // Calculate the scale of the font based on selected font size and sampling point size.
                // baseScale is calculated using the font asset assigned to the text object.
                float baseScale           = textBaseConfiguration.fontSize / currentFontSamplingPointSize * (textBaseConfiguration.isOrthographic ? 1 : 0.1f);
                float currentElementScale = baseScale;
                float currentEmScale      = textBaseConfiguration.fontSize * 0.01f * (textBaseConfiguration.isOrthographic ? 1 : 0.1f);

                float topAnchor    = GetTopAnchorForConfig(ref currentFont, textBaseConfiguration.verticalAlignment, baseScale);
                float bottomAnchor = GetBottomAnchorForConfig(ref currentFont, textBaseConfiguration.verticalAlignment, baseScale);

                Unicode.Rune currentRune, previousRune = Unicode.BadRune;  //input text unicode
                //for (int k = 0, length = glyphOTFBuffer.Length; k < length; k++)
                int k = 0;
                while (glyphOTFStream.RemainingItemCount > 0)
                {
                    glyphOTF = glyphOTFStream.Read<GlyphOTF>();
                    //glyphOTF = glyphOTFBuffer[k];
                    glyphID    = glyphTable.glyphHashToIdMap[glyphOTF.glyphKey];
                    glyphEntry = glyphTable.GetEntry(glyphID);

                    var cluster = (int)glyphOTF.cluster;  //cluster is char index in cleaned text = aligned with glyphOTF buffer
                    if (currentFaceIndex != glyphOTF.glyphKey.faceIndex ||
                        (currentFace.HasVarData && currentFont.currentVariableProfileIndex != glyphOTF.glyphKey.variableProfileIndex) ||
                        currentFontSamplingPointSize != glyphOTF.glyphKey.GetSamplingSize())
                    {
                        //Debug.Log($"Switching font from {currentFaceIndex} to {glyphOTF.glyphKey.faceIndex}");
                        currentFaceIndex = glyphOTF.glyphKey.faceIndex;
                        currentFace      = fontTable.faces[currentFaceIndex];
                        currentFont      = fontTable.GetOrCreateFont(currentFaceIndex, threadIndex);
                        if(currentFace.HasVarData && currentFont.currentVariableProfileIndex != glyphOTF.glyphKey.variableProfileIndex)
                            currentFont = fontTable.SetVariableProfile(currentFaceIndex, threadIndex, glyphOTF.glyphKey.variableProfileIndex);

                        currentFontSamplingPointSize = glyphOTF.glyphKey.GetSamplingSize();
                        currentFontWeigth            = currentFont.GetStyleTag(StyleTag.WEIGHT);
                        currentFontIsItalic          = (byte)currentFont.GetStyleTag(StyleTag.ITALIC) == 1;
                        currentFont.SetScale(currentFontSamplingPointSize, currentFontSamplingPointSize);
                        // Todo: Don't hardcode these when line-wrapping is moved to shaping
                        currentFont.UpdateMetaData(Direction.LTR, Script.LATIN, Language.English);
                    }

                    while (cluster >= nextTagPositionInCleanedText)
                    {
                        if (tagsCounter < xmlTags.Length)
                        {
                            currentTag      = xmlTags[tagsCounter++];
                            richTextOffset += currentTag.Length;
                            layoutConfig.Update(ref currentTag, textBaseConfiguration, ref textColorGradientArray);
                            nextSegmentEndID             = tagsCounter < xmlTags.Length ? xmlTags[tagsCounter].startID - 1 : calliString.Length;
                            cleanedSegmentLength         = nextSegmentEndID - currentTag.endID;
                            nextTagPositionInCleanedText = cluster + cleanedSegmentLength;

                            //Debug.Log($"{currentTag.tagType} {cleanedSegmentLength} {nextTagPositionInCleanedText}");
                        }
                    }

                    // need to add richTextOffset to fetch correct char from richtext buffer.
                    // note: upper/lowercase is not applied in richtextBuffer (is only applied to cleaned text just before shaping)...should not cause any issues here
                    characters.GotoByteIndex(richTextOffset + cluster);
                    currentRune = characters.Current;

                    //if (currentFace.HasVarData)
                    //    Debug.Log($"char: {(char)currentRune.value} glyphIndex {glyphEntry.key.glyphIndex} cliprect {glyphEntry.ClipRect} glyphOTF {glyphOTF} namedVariationIndex: {currentFont.currentVariableProfileIndex} {currentFace.GetName(NameID.FONT_FAMILY, Language.English)}, {currentFace.GetName(currentFace.GetNamedInstanceSubFamilyNameID(currentFont.currentVariableProfileIndex), Language.English)}");
                    //else
                    //    Debug.Log($"char: {(char)currentRune.value} glyphIndex {glyphEntry.key.glyphIndex} cliprect {glyphEntry.ClipRect} glyphOTF {glyphOTF} faceIndex: {currentFaceIndex} ({currentFace.GetName(NameID.FONT_FAMILY, Language.English)}, {currentFace.GetName(NameID.FONT_SUBFAMILY, Language.English)})");

                    if (isFirstLine)
                        topAnchor = GetTopAnchorForConfig(ref currentFont, textBaseConfiguration.verticalAlignment, baseScale, topAnchor);
                    bottomAnchor  = GetBottomAnchorForConfig(ref currentFont, textBaseConfiguration.verticalAlignment, baseScale, bottomAnchor);

                    #region Look up Character Data
                    //Debug.Log($"Render Glyph {glyphEntry.key.glyphIndex} from face {currentFaceIndex} using rect {glyphEntry.x} {glyphEntry.y} {glyphEntry.width} {glyphEntry.height} ({glyphEntry.PaddedWidth} {glyphEntry.PaddedHeight})");
                    // review how to handle glyphOTF.codepoint = 0 (not defined glyph) which is retured for example for tab stop (9)
                    // see here why: https://github.com/harfbuzz/harfbuzz/commit/81ef4f407d9c7bd98cf62cef951dc538b13442eb#commitcomment-9469767
                    // should not be rendered, but xAdvance should be processed

                    // Cache glyph metrics
                    int x_bearing   = glyphEntry.xBearing;
                    int y_bearing   = glyphEntry.yBearing;
                    int glyphHeight = glyphEntry.height;
                    int glyphWidth  = glyphEntry.width;
                    int padding     = glyphEntry.padding;

                    float adjustedScale      = layoutConfig.m_currentFontSize / currentFontSamplingPointSize * (textBaseConfiguration.isOrthographic ? 1 : 0.1f);
                    float elementAscentLine  = currentFont.fontExtents.ascender;
                    float elementDescentLine = currentFont.fontExtents.descender;

                    //synthesize superscript and subscript redundant to opentype feature set during shaping.
                    //only purpose is to simulate missing subscript glyphs, but unclear how to determine this
                    float fontScaleMultiplier     = 1;
                    float m_subAndSupscriptOffset = 0;
                    //if ((layoutConfiguration.m_fontStyles & FontStyles.Subscript) == FontStyles.Subscript && !currentRune.IsDigit())
                    //{
                    //    //Debug.Log($"{currentFont.subScriptEmXSize} {currentFont.subScriptEmYOffset} {adjustedScale}");
                    //    fontScaleMultiplier = currentFont.subScriptEmXSize * adjustedScale;
                    //    m_SubAndSupscriptOffset = -currentFont.subScriptEmYOffset * adjustedScale;
                    //}
                    //else if ((layoutConfiguration.m_fontStyles & FontStyles.Superscript) == FontStyles.Superscript && !currentRune.IsDigit())
                    //{
                    //    fontScaleMultiplier = currentFont.superScriptEmXSize * adjustedScale;
                    //    m_SubAndSupscriptOffset = currentFont.superScriptEmYOffset * adjustedScale;
                    //}

                    currentElementScale  = adjustedScale * fontScaleMultiplier;
                    float baselineOffset = currentFont.baseLine * adjustedScale * fontScaleMultiplier;
                    #endregion

                    // Optimization to avoid calling this more than once per character.
                    bool isWhiteSpace = currentRune.value <= 0xFFFF && currentRune.IsWhiteSpace();

                    // Handle Mono Spacing
                    #region Handle Mono Spacing
                    float monoAdvance = 0;
                    if (layoutConfig.m_monoSpacing != 0)
                    {
                        monoAdvance =
                            (layoutConfig.m_monoSpacing / 2 - (glyphWidth / 2 + x_bearing) * currentElementScale);  // * (1 - charWidthAdjDelta);
                        layoutConfig.m_xAdvance += monoAdvance;
                    }
                    #endregion

                    // Set Padding based on selected font style
                    #region Handle Style Padding
                    float boldSpacingAdjustment = 0;
                    //if bold is requested and current font is not bold (=it has not been found), then simulate bold
                    bool simulateBold = (layoutConfig.fontWeight >= FontWeight.Bold.Value() && currentFontWeigth < FontWeight.Bold.Value());
                    if (simulateBold)
                    {
                        //Debug.Log($"Simulate Bold (current: {currentFontWeigth})");
                        boldSpacingAdjustment = 7;  //this is not a property of font so might as well just set it here
                    }
                    #endregion Handle Style Padding

                    var renderGlyph          = new RenderGlyph();
                    renderGlyph.arrayIndex   = (uint)k;
                    renderGlyph.glyphEntryId = glyphID;

                    // Determine the position of the vertices of the Character or Sprite.
                    #region Calculate Vertices Position

                    // top left is used to position the bottom left and top right
                    float2 topLeft;
                    topLeft.x = layoutConfig.m_xAdvance + (x_bearing * layoutConfig.m_fxScale - padding + glyphOTF.xOffset) * currentElementScale;
                    topLeft.y = baselineOffset + (y_bearing + padding + glyphOTF.yOffset) * currentElementScale + layoutConfig.m_baselineOffset + m_subAndSupscriptOffset;

                    float2 bottomLeft;
                    bottomLeft.x = topLeft.x;
                    bottomLeft.y = topLeft.y - ((glyphHeight + padding * 2) * currentElementScale);

                    float2 topRight;
                    topRight.x = bottomLeft.x + (glyphWidth * layoutConfig.m_fxScale + padding * 2) * currentElementScale;
                    topRight.y = topLeft.y;

                    float2 bottomRight;
                    bottomRight.x = topRight.x;
                    bottomRight.y = bottomLeft.y;
                    #endregion

                    // We don't set up UVA here, as that is the atlas texture coordinates.
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
                    renderGlyph.tlUVB = tlUVC;
                    renderGlyph.trUVB = trUVC;
                    renderGlyph.brUVB = brUVC;
                    #endregion

                    #region Setup Color
                    if (layoutConfig.useGradient)  //&& !isColorGlyph)
                    {
                        var gradient        = layoutConfig.m_gradient;
                        renderGlyph.blColor = GetColorAsHDRHalf4(gradient.bottomLeft);
                        renderGlyph.tlColor = GetColorAsHDRHalf4(gradient.topLeft);
                        renderGlyph.trColor = GetColorAsHDRHalf4(gradient.topRight);
                        renderGlyph.brColor = GetColorAsHDRHalf4(gradient.bottomRight);
                    }
                    else
                    {
                        var m_htmlColor     = GetColorAsHDRHalf4(layoutConfig.m_htmlColor);
                        renderGlyph.blColor = m_htmlColor;
                        renderGlyph.tlColor = m_htmlColor;
                        renderGlyph.trColor = m_htmlColor;
                        renderGlyph.brColor = m_htmlColor;
                    }
                    #endregion

                    #region Pack Scale into renderGlyph.scale
                    var scale = layoutConfig.m_currentFontSize;
                    if (simulateBold)
                        scale *= -1;

                    renderGlyph.scale = scale;
                    #endregion

                    // Check if we need to Shear the rectangles for Italic styles
                    #region Handle Italic & Shearing
                    float bottomShear = 0f;
                    //if italic is requested and current font is not italic (=it has not been found), then simulate italic
                    bool simulateItalic = (layoutConfig.m_fontStyles & FontStyles.Italic) == FontStyles.Italic && !currentFontIsItalic;
                    if (simulateItalic)
                    {
                        //Debug.Log($"Simulate Italic {currentFontIsItalic}");
                        // Shift Top vertices forward by half (Shear Value * height of character) and Bottom vertices back by same amount.
                        var   italicsStyleSlant = 35;  //this is not a property of font so might as well just set it here
                        float shear_value       = italicsStyleSlant * 0.01f;
                        float midPoint          = ((currentFont.capHeight - (currentFont.baseLine + layoutConfig.m_baselineOffset + m_subAndSupscriptOffset)) / 2) *
                                                  fontScaleMultiplier;
                        float topShear = shear_value * ((y_bearing + padding - midPoint) * currentElementScale);
                        bottomShear    = shear_value * ((y_bearing - glyphHeight - padding - midPoint) * currentElementScale);

                        topLeft.x     += topShear;
                        bottomLeft.x  += bottomShear;
                        topRight.x    += topShear;
                        bottomRight.x += bottomShear;
                    }
                    #endregion Handle Italics & Shearing

                    // Handle Character FX Rotation
                    #region Handle Character FX Rotation

                    float rotation = math.radians(layoutConfig.m_fxRotationAngleCCW_degree);
                    if (math.abs(rotation) > 0.0001f)
                    {
                        float2 pivot = (topLeft + bottomRight) * 0.5f;
                        math.sincos(rotation, out float sinRotation, out float cosRotation);

                        topLeft     = RotatePoint(topLeft, pivot, sinRotation, cosRotation);
                        bottomLeft  = RotatePoint(bottomLeft, pivot, sinRotation, cosRotation);
                        topRight    = RotatePoint(topRight, pivot, sinRotation, cosRotation);
                        bottomRight = RotatePoint(bottomRight, pivot, sinRotation, cosRotation);
                    }
                    #endregion

                    #region Store vertex information for the character or sprite.

                    renderGlyph.trPosition = topRight;
                    renderGlyph.tlPosition = topLeft;
                    renderGlyph.blPosition = bottomLeft;
                    renderGlyph.brPosition = bottomRight;
                    if (Hint.Likely(currentRune.value != 10))  //do not render LF
                    {
                        renderGlyphs.Add(renderGlyph);
                    }
                    #endregion

                    // Compute text metrics
                    #region Compute Ascender & Descender values
                    // Element Ascender in line space
                    float elementAscender = elementAscentLine * currentElementScale + layoutConfig.m_baselineOffset + m_subAndSupscriptOffset;

                    // Element Descender in line space
                    float elementDescender = elementDescentLine * currentElementScale + layoutConfig.m_baselineOffset + m_subAndSupscriptOffset;

                    float adjustedAscender  = elementAscender;
                    float adjustedDescender = elementDescender;

                    // Max line ascender and descender in line space
                    if (isLineStart || isWhiteSpace == false)
                    {
                        // Special handling for Superscript and Subscript where we use the unadjusted line ascender and descender
                        if (m_subAndSupscriptOffset != 0)  //To-Do: review (also voffset affecting m_baselineOffset), effect not clear.
                        {
                            adjustedAscender  = math.max((elementAscender - m_subAndSupscriptOffset) / fontScaleMultiplier, adjustedAscender);
                            adjustedDescender = math.min((elementDescender - m_subAndSupscriptOffset) / fontScaleMultiplier, adjustedDescender);
                        }
                        maxLineAscender  = math.max(adjustedAscender, maxLineAscender);
                        maxLineDescender = math.min(adjustedDescender, maxLineDescender);
                    }
                    #endregion

                    #region XAdvance, Tabulation & Stops
                    if (currentRune.value == 9)
                    {
                        float tabSize           = currentFont.TabAdvance() * currentElementScale;
                        float tabs              = math.ceil(layoutConfig.m_xAdvance / tabSize) * tabSize;
                        layoutConfig.m_xAdvance = tabs > layoutConfig.m_xAdvance ? tabs : layoutConfig.m_xAdvance + tabSize;
                    }
                    else if (layoutConfig.m_monoSpacing != 0)
                    {
                        float monoAdjustment     = layoutConfig.m_monoSpacing - monoAdvance;
                        layoutConfig.m_xAdvance += (monoAdjustment + layoutConfig.m_cSpacing);
                        if (isWhiteSpace || currentRune.value == 0x200B)
                            layoutConfig.m_xAdvance += textBaseConfiguration.wordSpacing * currentEmScale;
                    }
                    else
                    {
                        layoutConfig.m_xAdvance += (glyphOTF.xAdvance * layoutConfig.m_fxScale) * currentElementScale +
                                                   boldSpacingAdjustment * currentEmScale + layoutConfig.m_cSpacing;

                        if (isWhiteSpace || currentRune.value == 0x200B)
                            layoutConfig.m_xAdvance += textBaseConfiguration.wordSpacing * currentEmScale;
                    }
                    #endregion XAdvance, Tabulation & Stops

                    #region Check for Line Feed and Last Character
                    if (isLineStart)
                        isLineStart   = false;
                    currentLineHeight = (currentFont.fontExtents.ascender - currentFont.fontExtents.descender) * baseScale;
                    ascentLineDelta   = maxLineAscender - currentFont.fontExtents.ascender * baseScale;
                    decentLineDelta   = currentFont.fontExtents.descender * baseScale - maxLineDescender;
                    //if (currentRune.value == 10 || currentRune.value == 11 || currentRune.value == 0x03 || currentRune.value == 0x2028 ||
                    //    currentRune.value == 0x2029 || textConfiguration.m_characterCount == calliString.Length - 1)
                    if (currentRune.value == 10)
                    {
                        var renderGlyphsLine = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex, renderGlyphs.Length - startOfLineGlyphIndex);
                        var overrideMode     = layoutConfig.m_lineJustification;
                        if (overrideMode == HorizontalAlignmentOptions.Justified)
                        {
                            // Don't perform justified spacing for the last line in the paragraph.
                            overrideMode = HorizontalAlignmentOptions.Left;
                        }
                        ApplyHorizontalAlignmentToGlyphs(ref renderGlyphsLine,
                                                         ref characterGlyphIndicesWithPreceedingSpacesInLine,
                                                         textBaseConfiguration.maxLineWidth,
                                                         overrideMode);
                        startOfLineGlyphIndex = renderGlyphs.Length;
                        if (!isFirstLine)
                        {
                            accumulatedVerticalOffset += currentLineHeight + ascentLineDelta;
                            if (lastCommittedStartOfLineGlyphIndex != startOfLineGlyphIndex)
                            {
                                ApplyVerticalOffsetToGlyphs(ref renderGlyphsLine, accumulatedVerticalOffset);
                                lastCommittedStartOfLineGlyphIndex = startOfLineGlyphIndex;
                            }
                        }
                        accumulatedVerticalOffset += decentLineDelta;
                        //apply user configurable line and paragraph spacing
                        accumulatedVerticalOffset +=
                            (textBaseConfiguration.lineSpacing +
                             (currentRune.value == 10 || currentRune.value == 0x2029 ? textBaseConfiguration.paragraphSpacing : 0)) * currentEmScale;

                        //reset line status
                        maxLineAscender  = float.MinValue;
                        maxLineDescender = float.MaxValue;

                        isFirstLine  = false;
                        isLineStart  = true;
                        bottomAnchor = GetBottomAnchorForConfig(ref currentFont, textBaseConfiguration.verticalAlignment, baseScale);

                        layoutConfig.m_xAdvance = layoutConfig.m_tagIndent;
                        previousRune            = currentRune;
                        continue;
                    }
                    #endregion

                    #region Word Wrapping
                    // Handle word wrap
                    if (textBaseConfiguration.maxLineWidth < float.MaxValue &&
                        textBaseConfiguration.maxLineWidth > 0 &&
                        layoutConfig.m_xAdvance > textBaseConfiguration.maxLineWidth)
                    {
                        bool dropSpace = false;

                        if (currentRune.value == 32 && previousRune.value != 32)
                        {
                            // What pushed us past the line width was a space character.
                            // The previous character was not a space, and we don't
                            // want to render this character at the start of the next line.
                            // We drop this space character instead and allow the next
                            // character to line-wrap, space or not.
                            dropSpace = true;
                        }

                        var yOffsetChange = 0f;  //font.lineHeight * currentElementScale;
                                                 // TODO this line should be later replaced with renderGlyphs
                        var xOffsetChange = renderGlyphs[lastWordStartCharacterGlyphIndex].blPosition.x - bottomShear - layoutConfig.m_tagIndent;
                        if (xOffsetChange > 0 && !dropSpace)  // Always allow one visible character
                        {
                            // Finish line based on alignment
                            var renderGlyphsLine = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex, lastWordStartCharacterGlyphIndex - startOfLineGlyphIndex);
                            ApplyHorizontalAlignmentToGlyphs(ref renderGlyphsLine,
                                                             ref characterGlyphIndicesWithPreceedingSpacesInLine,
                                                             textBaseConfiguration.maxLineWidth,
                                                             layoutConfig.m_lineJustification);

                            if (!isFirstLine)
                            {
                                accumulatedVerticalOffset += currentLineHeight + ascentLineDelta;
                                ApplyVerticalOffsetToGlyphs(ref renderGlyphsLine, accumulatedVerticalOffset);
                                lastCommittedStartOfLineGlyphIndex = startOfLineGlyphIndex;
                            }
                            accumulatedVerticalOffset += decentLineDelta;  // Todo: Delta should be computed per glyph
                                                                           //apply user configurable line and paragraph spacing
                            accumulatedVerticalOffset += textBaseConfiguration.lineSpacing * currentEmScale;

                            //reset line status
                            maxLineAscender  = float.MinValue;
                            maxLineDescender = float.MaxValue;

                            startOfLineGlyphIndex = lastWordStartCharacterGlyphIndex;
                            isLineStart           = true;
                            isFirstLine           = false;

                            layoutConfig.m_xAdvance -= xOffsetChange;

                            // Adjust the vertices of the previous render glyphs in the word
                            ApplyOffsetChange(ref renderGlyphs, lastWordStartCharacterGlyphIndex, xOffsetChange, yOffsetChange);
                        }
                    }
                    //Detect start of word
                    if (currentRune.value == 32 ||  //Space
                        currentRune.value == 9 ||  //Tab
                        currentRune.value == 45 ||  //Hyphen Minus
                        currentRune.value == 173 ||  //Soft hyphen
                        currentRune.value == 8203 ||  //Zero width space
                        currentRune.value == 8204 ||  //Zero width non-joiner
                        currentRune.value == 8205)  //Zero width joiner
                    {
                        lastWordStartCharacterGlyphIndex = renderGlyphs.Length;
                    }
                    #endregion
                    previousRune = currentRune;
                    k++;
                }

                var finalRenderGlyphsLine = renderGlyphs.AsNativeArray().GetSubArray(startOfLineGlyphIndex, renderGlyphs.Length - startOfLineGlyphIndex);
                {
                    var overrideMode = layoutConfig.m_lineJustification;
                    if (overrideMode == HorizontalAlignmentOptions.Justified)
                    {
                        // Don't perform justified spacing for the last line.
                        overrideMode = HorizontalAlignmentOptions.Left;
                    }
                    ApplyHorizontalAlignmentToGlyphs(ref finalRenderGlyphsLine,
                                                     ref characterGlyphIndicesWithPreceedingSpacesInLine,
                                                     textBaseConfiguration.maxLineWidth,
                                                     overrideMode);
                    if (!isFirstLine)
                    {
                        accumulatedVerticalOffset += currentLineHeight;
                        ApplyVerticalOffsetToGlyphs(ref finalRenderGlyphsLine, accumulatedVerticalOffset);
                    }
                }
                isFirstLine = false;
                ApplyVerticalAlignmentToGlyphs(ref renderGlyphs, topAnchor, bottomAnchor, accumulatedVerticalOffset, textBaseConfiguration.verticalAlignment);

                // Remove all zero-sized glyphs since we don't rasterize those.
                {
                    var glyphArray = renderGlyphs.AsNativeArray();
                    int dst        = 0;
                    for (int src = 0; src < glyphArray.Length; src++)
                    {
                        var glyph = renderGlyphs[src];
                        var entry = glyphTable.GetEntry(glyph.glyphEntryId);
                        if (entry.width == 0 || entry.height == 0)
                            continue;
                        renderGlyphs[dst] = glyph;
                        dst++;
                    }
                    renderGlyphs.Length = dst;
                }
            }
        }
    }
}

