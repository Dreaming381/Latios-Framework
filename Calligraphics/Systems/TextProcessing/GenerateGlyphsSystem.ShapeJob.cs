using Buffer = Latios.Calligraphics.HarfBuzz.Buffer;
using Latios.Calligraphics.HarfBuzz;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Profiling;

namespace Latios.Calligraphics.Systems
{
    public partial struct GenerateGlyphsSystem
    {
        [BurstCompile]
        partial struct ShapeJob : IJobChunk
        {
            [ReadOnly] public ProfilerMarker shapeMarker;
            [ReadOnly] public ProfilerMarker bufferMarker;

            public NativeStream.Writer                                       missingGlyphsStream;
            [NativeDisableParallelForRestriction] public NativeStream.Writer glyphOTFStream;
            [ReadOnly] public NativeStream.Reader                            xmlTagStream;
            [ReadOnly] public NativeArray<int>                               firstEntityIndexInChunk;

            [ReadOnly] public FontTable                                  fontTable;
            [ReadOnly] public GlyphTable                                 glyphTable;
            [ReadOnly] public ComponentTypeHandle<TextBaseConfiguration> textBaseConfigurationHandle;
            [ReadOnly] public BufferTypeHandle<CalliByte>                calliByteHandle;

            public uint lastSystemVersion;

            UnsafeHashSet<GlyphTable.Key> chunkMissingGlyphsSet;

            [NativeSetThreadIndex]
            int threadIndex;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if ((!chunk.DidChange(ref calliByteHandle, lastSystemVersion) && chunk.DidChange(ref textBaseConfigurationHandle, lastSystemVersion)) || fontTable.faces.IsEmpty)
                    return;

                if (!chunkMissingGlyphsSet.IsCreated)
                    chunkMissingGlyphsSet = new UnsafeHashSet<GlyphTable.Key>(128, Allocator.Temp);
                chunkMissingGlyphsSet.Clear();

                var firstEntityIndex = firstEntityIndexInChunk[unfilteredChunkIndex];
                missingGlyphsStream.BeginForEachIndex(unfilteredChunkIndex);

                //Debug.Log("Shape job");
                var calliBytesBuffers      = chunk.GetBufferAccessor(ref calliByteHandle);
                var textBaseConfigurations = chunk.GetNativeArray(ref textBaseConfigurationHandle);

                var buffer           = new Buffer(true);
                var openTypeFeatures = new OpenTypeFeatureConfig(16, Allocator.Temp);

                //shape plans can be cached..no use case found yet where there this makes a significant difference
                //var shaperList = HB.hb_shape_list_shapers();
                //var shapePlanCache = new NativeHashMap<FontLookupKey, ShapePlan>(16, Allocator.Temp);

                var          cleanedString = new NativeText(1024, Allocator.Temp);
                LayoutConfig layoutConfig  = default;
                FontConfig   fontConfig    = default;

                for (int indexInChunk = 0; indexInChunk < chunk.Count; indexInChunk++)
                {
                    int entityIndex = firstEntityIndex + indexInChunk;
                    glyphOTFStream.BeginForEachIndex(entityIndex);

                    var xmlTagCount = xmlTagStream.BeginForEachIndex(entityIndex);
                    var xmlTags     = new NativeArray<XMLTag>(xmlTagCount, Allocator.Temp);
                    for (int i = 0; i < xmlTagCount; i++)
                        xmlTags[i] = xmlTagStream.Read<XMLTag>();
                    xmlTagStream.EndForEachIndex();

                    var calliBytesBuffer      = calliBytesBuffers[indexInChunk].Reinterpret<byte>();
                    var textBaseConfiguration = textBaseConfigurations[indexInChunk];

                    var language = new Language(textBaseConfiguration.language.Value.ToFixedString());

                    fontConfig.Reset(textBaseConfiguration, ref fontTable);
                    layoutConfig.Reset(textBaseConfiguration);
                    var calliString        = new CalliString(calliBytesBuffer);
                    cleanedString.Capacity = calliString.Capacity;

                    if (xmlTagStream.Count() == 0)
                        ShapeNoRichText(calliString,
                                        ref layoutConfig,
                                        cleanedString,
                                        ref fontConfig,
                                        ref fontTable,
                                        ref openTypeFeatures,
                                        ref textBaseConfiguration,
                                        ref language,
                                        ref buffer,
                                        ref glyphOTFStream);
                    else
                        ShapeRichText(calliString,
                                      ref layoutConfig,
                                      cleanedString,
                                      ref fontConfig,
                                      ref fontTable,
                                      ref openTypeFeatures,
                                      ref textBaseConfiguration,
                                      ref language,
                                      ref buffer,
                                      ref glyphOTFStream,
                                      ref xmlTags);

                    cleanedString.Clear();
                    glyphOTFStream.EndForEachIndex();
                }
                //add missing glyphs identifed in chunks processed by this thread to missingGlyphs
                missingGlyphsStream.EndForEachIndex();
                buffer.Dispose();
            }

            void AppendAndConvertCase(NativeText cleanedString, FontStyles fontStyles, ref Unicode.Rune currentRune)
            {
                if ((fontStyles & FontStyles.UpperCase) == FontStyles.UpperCase)
                    cleanedString.Append(currentRune.ToUpper());
                else if ((fontStyles & FontStyles.LowerCase) == FontStyles.LowerCase)
                    cleanedString.Append(currentRune.ToLower());
                else
                    cleanedString.Append(currentRune);
            }
            void ShapeNoRichText(CalliString calliString,
                                 ref LayoutConfig layoutConfig,
                                 NativeText cleanedString,
                                 ref FontConfig fontConfig,
                                 ref FontTable fontTable,
                                 ref OpenTypeFeatureConfig openTypeFeatures,
                                 ref TextBaseConfiguration textBaseConfiguration,
                                 ref Language language,
                                 ref Buffer buffer,
                                 ref NativeStream.Writer glyphOTFStream)
            {
                var rawCharacters = calliString.GetEnumerator();
                //copy text into buffer used for shaping, convert case while doing so
                while (rawCharacters.MoveNext())
                {
                    var currentRune = rawCharacters.Current;
                    AppendAndConvertCase(cleanedString, layoutConfig.m_fontStyles, ref currentRune);
                }
                openTypeFeatures.SetGlobalFeatures(textBaseConfiguration, (uint)cleanedString.Length);
                Shape(buffer,
                      cleanedString,
                      0,
                      cleanedString.Length,
                      ref language,
                      ref fontTable,
                      ref fontConfig,
                      fontConfig.m_faceIndex,
                      fontConfig.m_namedVariationIndex,
                      openTypeFeatures.values,
                      ref glyphOTFStream);
            }

            void ShapeRichText(CalliString calliString,
                               ref LayoutConfig layoutConfig,
                               NativeText cleanedString,
                               ref FontConfig fontConfig,
                               ref FontTable fontTable,
                               ref OpenTypeFeatureConfig openTypeFeatures,
                               ref TextBaseConfiguration textBaseConfiguration,
                               ref Language language,
                               ref Buffer buffer,
                               ref NativeStream.Writer glyphOTFStream,
                               ref NativeArray<XMLTag>   xmlTags)
            {
                //text has richtext tags. Search segments where font, language, script and direction does does not change (To-Do: use ICU for that),
                //apply opentype features requested via richtext tags, and shape
                var rawCharacters              = calliString.GetEnumerator();
                var currentFaceIndex           = fontConfig.m_faceIndex;
                var currentNamedVariationIndex = fontConfig.m_namedVariationIndex;
                int tagsCounter                = 0;
                var nextTagPosition            = tagsCounter < xmlTags.Length ? xmlTags[tagsCounter].startID : calliString.Length;

                //copy text into buffer used for shaping, convert case while doing so
                bool   keepGoing;
                XMLTag currentTag;
                while (keepGoing = rawCharacters.MoveNext())
                {
                    var currentRune = rawCharacters.Current;
                    while (tagsCounter < xmlTags.Length && rawCharacters.NextRuneByteIndex > nextTagPosition)
                    {
                        currentTag = xmlTags[tagsCounter];
                        rawCharacters.GotoByteIndex(currentTag.endID);  // go to ">'
                        keepGoing = rawCharacters.MoveNext();  // go to char after '>'
                        layoutConfig.Update(ref currentTag);
                        currentRune = rawCharacters.Current;
                        tagsCounter++;
                        nextTagPosition = tagsCounter < xmlTags.Length ? xmlTags[tagsCounter].startID : calliString.Length;
                    }
                    if (!keepGoing)
                        continue;
                    AppendAndConvertCase(cleanedString, layoutConfig.m_fontStyles, ref currentRune);
                }

                var richTextStartID = 0;
                var cleanedEnd      = 0;
                var cleanedStart    = 0;
                tagsCounter         = 0;
                while (cleanedStart < cleanedString.Length)
                {
                    while (tagsCounter < xmlTags.Length && fontConfig.m_faceIndex == currentFaceIndex && fontConfig.m_namedVariationIndex == currentNamedVariationIndex)
                    {
                        currentTag                 = xmlTags[tagsCounter];
                        var cleanedInterTagLength  = (currentTag.startID - richTextStartID);
                        cleanedEnd                += cleanedInterTagLength;
                        fontConfig.Update(ref currentTag, ref fontTable, ref calliString);
                        openTypeFeatures.Update(ref currentTag, cleanedEnd);
                        tagsCounter++;
                        richTextStartID = currentTag.endID + 1;
                    }
                    openTypeFeatures.FinalizeOpenTypeFeatures(cleanedString.Length);
                    openTypeFeatures.SetGlobalFeatures(textBaseConfiguration, (uint)cleanedString.Length);
                    var cleanedSegmentLength = cleanedEnd - cleanedStart;
                    if(cleanedSegmentLength > 0 )
                        Shape(buffer,
                              cleanedString,
                              cleanedStart,
                              cleanedSegmentLength,
                              ref language,
                              ref fontTable,
                              ref fontConfig,
                              currentFaceIndex,
                              currentNamedVariationIndex,
                              openTypeFeatures.values,
                              ref glyphOTFStream);
                    currentFaceIndex           = fontConfig.m_faceIndex;
                    currentNamedVariationIndex = fontConfig.m_namedVariationIndex;
                    cleanedStart               = cleanedEnd;
                    if (tagsCounter == xmlTags.Length) //last loop in order to shape text between last tag and end of rich text buffer
                        cleanedEnd = cleanedString.Length;
                }
            }

            void Shape(Buffer buffer,
                       NativeText text,
                       int startIndex,
                       int length,
                       ref Language language,
                       ref FontTable fontTable,
                       ref FontConfig fontConfig,
                       int faceIndex,
                       int namedVariationIndex,
                       NativeList<Feature>     features,
                       ref NativeStream.Writer glyphOTFStream)
            {
                if (startIndex + length == text.Length && text[^ 1] == 0)
                    length--; //last byte of CalliBytes buffer appears to be always '0', which should not be shaped.
                buffer.AddText(text, (uint)startIndex, length);
                buffer.Language = language;
                buffer.GuessSegmentProperties();

                //a number of white spaces are regretably not replaced by "space" (needs to be handled in GenerateGlyphJob)
                //https://github.com/harfbuzz/harfbuzz/commit/81ef4f407d9c7bd98cf62cef951dc538b13442eb#commitcomment-9469767
                buffer.BufferFlag = BufferFlag.REMOVE_DEFAULT_IGNORABLES | BufferFlag.BOT | BufferFlag.EOT;

                var face = this.fontTable.faces[faceIndex];
                var font = this.fontTable.GetOrCreateFont(faceIndex, threadIndex);
                if (face.HasVarData && font.currentVariableProfileIndex != namedVariationIndex)
                    font = fontTable.SetVariableProfile(faceIndex, threadIndex, namedVariationIndex);

                var renderFormat = face.hasColor ? RenderFormat.Bitmap8888 : (fontConfig.m_fontTextureSize != FontTextureSize.Normal ? RenderFormat.SDF16 : RenderFormat.SDF8);
                var samplingSize = FontEnumerationExtensions.GetSamplingSize(renderFormat, fontConfig.m_fontTextureSize);
                font.SetScale(samplingSize, samplingSize);

                //Debug.Log($"shape {text} {startIndex} {length}");
                //if (face.HasVarData)
                //    Debug.Log($"namedVariationIndex: {namedVariationIndex} {face.GetName(NameID.FONT_FAMILY, Language.English)}, {face.GetName(face.GetNamedInstanceSubFamilyNameID(namedVariationIndex), Language.English)}");
                //else
                //    Debug.Log($"faceIndex: {faceIndex} {face.GetName(NameID.FONT_FAMILY, Language.English)}, {face.GetName(NameID.FONT_SUBFAMILY, Language.English)}");

                //if (!shapePlanCache.TryGetValue(lookupKey, out var shapePlan))
                //{
                //    shapePlan = new ShapePlan(nativeFontPointer.face, ref segmentProperties, features, shaperList);
                //    shapePlanCache.Add(lookupKey, shapePlan);
                //}
                //sShapeMarker.Begin();
                //shapePlan.Execute(font, buffer, features);
                //sShapeMarker.End();

                shapeMarker.Begin();
                font.Shape(buffer, features);
                shapeMarker.End();

                var glyphInfos     = buffer.GetGlyphInfosSpan();
                var glyphPositions = buffer.GetGlyphPositionsSpan();

                for (int i = 0, ii = glyphInfos.Length; i < ii; i++)
                {
                    var glyphInfo     = glyphInfos[i];
                    var glyphPosition = glyphPositions[i];
                    var codepoint     = glyphInfo.codepoint;

                    var glyphOTF = new GlyphOTF
                    {
                        glyphKey = new GlyphTable.Key
                        {
                            faceIndex            = faceIndex,
                            glyphIndex           = (ushort)glyphInfo.codepoint,
                            format               = renderFormat,
                            textureSize          = fontConfig.m_fontTextureSize,
                            variableProfileIndex = namedVariationIndex
                        },
                        cluster  = glyphInfo.cluster,
                        xAdvance = glyphPosition.xAdvance,
                        yAdvance = glyphPosition.yAdvance,
                        xOffset  = glyphPosition.xOffset,
                        yOffset  = glyphPosition.yOffset,
                    };
                    if (!glyphTable.glyphHashToIdMap.ContainsKey(glyphOTF.glyphKey))
                    {
                        // We use the hashset to avoid redundantly adding the same glyph for this chunk.
                        // The missingGlyphsStream may still have redundancies between chunks, but this reduces
                        // some of the work while still maintaining determinism.
                        if (chunkMissingGlyphsSet.Add(glyphOTF.glyphKey))
                            missingGlyphsStream.Write(glyphOTF.glyphKey);
                    }
                    glyphOTFStream.Write(glyphOTF);
                }
                buffer.ClearContent();
                features.Clear();
            }
        }
    }
}

