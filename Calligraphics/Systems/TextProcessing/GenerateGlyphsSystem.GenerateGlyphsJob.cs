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

                //Debug.Log("Generate glyphs job");
                var calliBytesBuffers          = chunk.GetBufferAccessor(ref calliByteHandle);
                var renderGlyphBuffers         = chunk.GetBufferAccessor(ref renderGlyphHandle);
                var previousRenderGlyphBuffers = chunk.GetBufferAccessor(ref previousRenderGlyphHandle);
                var textBaseConfigurations     = chunk.GetNativeArray(ref textBaseConfigurationHandle);

                TextColorGradientArray textColorGradientArray = default;
                textColorGradientArray.Initialize(textColorGradientEntity, textColorGradientLookup);

                xmlTagStream.BeginForEachIndex(unfilteredChunkIndex);
                bool hasAnyGlyphs = glyphOTFStream.BeginForEachIndex(unfilteredChunkIndex) != 0;

                for (int indexInChunk = 0; indexInChunk < chunk.Count && hasAnyGlyphs; indexInChunk++)
                {
                    var xmlTagCount = xmlTagStream.Read<XMLTagStreamHeader>().tagCount;
                    var xmlTags     = new NativeArray<XMLTag>(xmlTagCount, Allocator.Temp);
                    for (int i = 0; i < xmlTagCount; i++)
                        xmlTags[i] = xmlTagStream.Read<XMLTag>();

                    var renderGlyphs = renderGlyphBuffers[indexInChunk];
                    renderGlyphs.Clear();

                    var header = glyphOTFStream.Read<GlyphOTFStreamHeader>();
                    if (header.glyphCount == 0)
                        continue;

                    var calliBytes                = calliBytesBuffers[indexInChunk];
                    var previousRenderGlyphBuffer = previousRenderGlyphBuffers[indexInChunk];
                    var textBaseConfiguration     = textBaseConfigurations[indexInChunk];

                    previousRenderGlyphBuffer.EnsureCapacity(header.glyphCount);  //allocating here make this job 2x slower but UpdateChangedGlyphsJob 10x faster
                    //renderGlyphs.Capacity = glyphCount; //not needed when done via single threaded pre-allocationjob
                    CreateRenderGlyphs(ref renderGlyphs,
                                       in calliBytes,
                                       ref glyphOTFStream,
                                       ref xmlTags,
                                       in textBaseConfiguration,
                                       ref textColorGradientArray,
                                       header);
                }

                glyphOTFStream.EndForEachIndex();
                xmlTagStream.EndForEachIndex();
            }

            unsafe void CreateRenderGlyphs(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                                           in DynamicBuffer<CalliByte>    calliBytesBuffer,
                                           ref NativeStream.Reader glyphOTFStream,
                                           ref NativeArray<XMLTag>        xmlTags,
                                           in TextBaseConfiguration textBaseConfiguration,
                                           ref TextColorGradientArray textColorGradientArray,
                                           GlyphOTFStreamHeader header)
            {
                //Debug.Log("CreateRenderGlyphs");
                var calliString = new CalliString(calliBytesBuffer);
                var characters  = calliString.GetEnumerator();

                var layoutConfig = new LayoutConfig(in textBaseConfiguration);

                XMLTag currentTag                   = default;
                int    tagsCounter                  = 0;
                int    nextSegmentEndID             = xmlTags.Length > 0 ? xmlTags[tagsCounter].startID : calliString.Length;
                int    cleanedSegmentLength         = nextSegmentEndID - currentTag.endID;
                int    richTextOffset               = 0;
                int    nextTagPositionInCleanedText = cleanedSegmentLength;
                //Debug.Log($"{currentTag.tagType} {cleanedSegmentLength} {nextTagPositionInCleanedText}");

                float2 pen = header.penStart;

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
                currentFont.GetMetrics(MetricTag.CAP_HEIGHT, out var currentFontCapHeight);  // Needed for italics

                Unicode.Rune currentRune;  //input text unicode
                for (int i = 0; i < header.glyphCount; i++)
                {
                    glyphOTF   = glyphOTFStream.Read<GlyphOTF>();
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
                        currentFont.GetMetrics(MetricTag.CAP_HEIGHT, out currentFontCapHeight);  // Needed for italics
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

                    float adjustedScale = layoutConfig.m_currentFontSize / currentFontSamplingPointSize * (textBaseConfiguration.isOrthographic ? 1 : 0.1f);

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

                    float currentElementScale = adjustedScale * fontScaleMultiplier;
                    float baselineOffset      = glyphOTF.baseline * adjustedScale * fontScaleMultiplier;
                    #endregion

                    // Handle Mono Spacing. This is just to center the glyph quad within the fixed-width character space area.
                    #region Handle Mono Spacing
                    float monoAdvance = 0;
                    if (layoutConfig.m_monoSpacing != 0)
                    {
                        monoAdvance = (layoutConfig.m_monoSpacing / 2 - (glyphWidth / 2 + x_bearing) * currentElementScale);  // * (1 - charWidthAdjDelta);
                    }
                    #endregion

                    // Set Padding based on selected font style
                    #region Handle Style Padding
                    //if bold is requested and current font is not bold (=it has not been found), then simulate bold
                    bool simulateBold = layoutConfig.fontWeight >= FontWeight.Bold.Value() && currentFontWeigth < FontWeight.Bold.Value();
                    #endregion Handle Style Padding

                    var renderGlyph          = new RenderGlyph();
                    renderGlyph.glyphEntryId = glyphID;

                    // Determine the position of the vertices of the Character or Sprite.
                    #region Calculate Vertices Position

                    // top left is used to position the bottom left and top right
                    float2 topLeft;
                    topLeft.x = pen.x + monoAdvance + (x_bearing * layoutConfig.m_fxScale - padding + glyphOTF.xOffset) * currentElementScale;
                    topLeft.y = pen.y + baselineOffset + (y_bearing + padding + glyphOTF.yOffset) * currentElementScale + layoutConfig.m_baselineOffset + m_subAndSupscriptOffset;

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
                    //if italic is requested and current font is not italic (=it has not been found), then simulate italic
                    bool simulateItalic = (layoutConfig.m_fontStyles & FontStyles.Italic) == FontStyles.Italic && !currentFontIsItalic;
                    if (simulateItalic)
                    {
                        //Debug.Log($"Simulate Italic {currentFontIsItalic}");
                        // Shift Top vertices forward by half (Shear Value * height of character) and Bottom vertices back by same amount.
                        var   italicsStyleSlant = 35;  //this is not a property of font so might as well just set it here
                        float shear_value       = italicsStyleSlant * 0.01f;
                        float midPoint          = ((currentFontCapHeight - (glyphOTF.baseline + layoutConfig.m_baselineOffset + m_subAndSupscriptOffset)) / 2) *
                                                  fontScaleMultiplier;
                        float topShear    = shear_value * ((y_bearing + padding - midPoint) * currentElementScale);
                        float bottomShear = shear_value * ((y_bearing - glyphHeight - padding - midPoint) * currentElementScale);

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

                    pen += new float2(glyphOTF.xAdvance, glyphOTF.yAdvance);
                }

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

