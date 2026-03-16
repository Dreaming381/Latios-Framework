using System;
using Latios.Calligraphics.HarfBuzz;
using Latios.Calligraphics.RichText;
using Unity.Collections;
using UnityEngine;

namespace Latios.Calligraphics.Systems
{
    public partial struct GenerateGlyphsSystem
    {
        partial struct ShapeJob
        {
            struct OpenTypeFeatureConfig : IDisposable
            {
                //https://learn.microsoft.com/en-us/typography/opentype/spec/featurelist

                public NativeList<Feature> values;
                public int                 smallCapsStartID;
                public int                 subscriptStartID;
                public int                 superscriptStartID;
                public int                 fractionStartID;
                public OpenTypeFeatureConfig(int size, Allocator allocator)
                {
                    values             = new NativeList<Feature>(size, allocator);
                    smallCapsStartID   = -1;
                    subscriptStartID   = -1;
                    superscriptStartID = -1;
                    fractionStartID    = -1;
                }
                public void FinalizeOpenTypeFeatures(int position)
                {
                    if (smallCapsStartID != -1)
                        values.Add(new Feature(Harfbuzz.HB_TAG('s', 'm', 'c', 'p'), 1, (uint)smallCapsStartID, (uint)position));
                    if (subscriptStartID != -1)
                        values.Add(new Feature(Harfbuzz.HB_TAG('s', 'u', 'b', 's'), 1, (uint)subscriptStartID, (uint)position));
                    if (superscriptStartID != -1)
                        values.Add(new Feature(Harfbuzz.HB_TAG('s', 'u', 'p', 's'), 1, (uint)superscriptStartID, (uint)position));
                    if (fractionStartID != -1)
                        values.Add(new Feature(Harfbuzz.HB_TAG('f', 'r', 'a', 'c'), 1, (uint)fractionStartID, (uint)position));
                }
                public void Update(ref XMLTag tag, int position)
                {
                    switch (tag.tagType)
                    {
                        case TagType.SmallCaps:
                            if (!tag.isClosing)
                            {
                                if (smallCapsStartID == -1)
                                    smallCapsStartID = position;
                            }
                            else
                            {
                                values.Add(new Feature(Harfbuzz.HB_TAG('s', 'm', 'c', 'p'), 1, (uint)smallCapsStartID, (uint)position));
                                smallCapsStartID = -1;
                            }
                            return;
                        case TagType.Subscript:
                            if (!tag.isClosing)
                            {
                                if (subscriptStartID == -1)
                                    subscriptStartID = position;
                            }
                            else
                            {
                                values.Add(new Feature(Harfbuzz.HB_TAG('s', 'u', 'b', 's'), 1, (uint)subscriptStartID, (uint)position));
                                subscriptStartID = -1;
                            }
                            return;
                        case TagType.Superscript:
                            if (!tag.isClosing)
                            {
                                if (superscriptStartID == -1)
                                    superscriptStartID = position;
                            }
                            else
                            {
                                values.Add(new Feature(Harfbuzz.HB_TAG('s', 'u', 'p', 's'), 1, (uint)superscriptStartID, (uint)position));
                                superscriptStartID = -1;
                            }
                            return;
                        case TagType.Fraction:
                            if (!tag.isClosing)
                            {
                                if (fractionStartID == -1)
                                    fractionStartID = position;
                            }
                            else
                            {
                                values.Add(new Feature(Harfbuzz.HB_TAG('f', 'r', 'a', 'c'), 1, (uint)fractionStartID, (uint)position));
                                fractionStartID = -1;
                            }
                            return;
                    }
                }
                public void SetGlobalFeatures(in TextBaseConfiguration textBaseConfiguration, uint textLength)
                {
                    if ((textBaseConfiguration.fontStyles & FontStyles.SmallCaps) == FontStyles.SmallCaps)
                        values.Add(new Feature(Harfbuzz.HB_TAG('s', 'm', 'c', 'p'), 1, 0, textLength));
                    if ((textBaseConfiguration.fontStyles & FontStyles.Subscript) == FontStyles.Subscript)
                        values.Add(new Feature(Harfbuzz.HB_TAG('s', 'u', 'b', 's'), 1, 0, textLength));
                    if ((textBaseConfiguration.fontStyles & FontStyles.Superscript) == FontStyles.Superscript)
                        values.Add(new Feature(Harfbuzz.HB_TAG('s', 'u', 'p', 's'), 1, 0, textLength));
                    if ((textBaseConfiguration.fontStyles & FontStyles.Fraction) == FontStyles.Fraction)
                        values.Add(new Feature(Harfbuzz.HB_TAG('f', 'r', 'a', 'c'), 1, 0, textLength));
                }
                public void Clear()
                {
                    values.Clear();
                    smallCapsStartID   = -1;
                    subscriptStartID   = -1;
                    superscriptStartID = -1;
                    fractionStartID    = -1;
                }

                public void Dispose()
                {
                    if (values.IsCreated)
                        values.Dispose();
                }
            }

            struct FontConfig
            {
                public int m_faceIndex;
                public int m_namedVariationIndex;

                public int                     m_fontFamilyHash;
                public FixedStack512Bytes<int> m_fontFamilyHashStack;

                public float                     m_fontWeight;
                public FixedStack512Bytes<float> m_fontWeightStack;

                public float                     m_fontWidth;
                public FixedStack512Bytes<float> m_fontWidthStack;

                public bool m_isItalic;

                public FontTextureSize m_fontTextureSize;

                FontLookupKey FontAssetRef
                {
                    get { return new FontLookupKey(m_fontFamilyHash, m_fontWeight, m_fontWidth, m_isItalic); }
                }

                public void Reset(in TextBaseConfiguration textBaseConfiguration, ref FontTable fontTable)
                {
                    m_isItalic = (textBaseConfiguration.fontStyles & FontStyles.Italic) == FontStyles.Italic;

                    m_fontFamilyHash = textBaseConfiguration.defaultFontFamilyHash;
                    m_fontFamilyHashStack.Clear();
                    m_fontFamilyHashStack.Add(m_fontFamilyHash);

                    m_fontWeight = textBaseConfiguration.fontWeight.Value();
                    m_fontWeightStack.Clear();
                    m_fontWeightStack.Add(m_fontWeight);

                    m_fontWidth = textBaseConfiguration.fontWidth.Value();
                    m_fontWidthStack.Clear();
                    m_fontWidthStack.Add(m_fontWidth);

                    m_fontTextureSize = textBaseConfiguration.fontTextureSize;

                    var defaultFaceIndex = fontTable.GetFaceIndex(FontAssetRef);
                    if(defaultFaceIndex == -1)
                    {
                        //fontTable does not contain a font of this family,
                        //so switch default font family to the family at faceIndex 0,
                        //leave everything else the same (weight, width etc), and search matching faceIndex
                        //Debug.Log($"Could not find FontLookupKey: {FontLookupKey}");
                        defaultFaceIndex        = 0;
                        var defaultFontAssetRef = fontTable.fontAssetRefs[defaultFaceIndex];
                        m_fontFamilyHash        = defaultFontAssetRef.familyHash;
                        defaultFaceIndex        = fontTable.GetFaceIndex(FontAssetRef);

                        //var face = fontTable.faces[defaultFaceIndex];
                        //var language = Language.English();
                        //Debug.Log($"set default FontLookupKey to {FontLookupKey} ({face.GetName(NameID.FONT_FAMILY, language)} {face.GetName(NameID.FONT_SUBFAMILY, language)})");
                    }
                    m_faceIndex = defaultFaceIndex == -1 ? m_faceIndex : defaultFaceIndex;

                    if (fontTable.faces[m_faceIndex].HasVarData)
                        fontTable.GetNamedVariationLookup(FontAssetRef, out m_namedVariationIndex);
                }

                public void Update(ref XMLTag tag, ref FontTable fontTable, ref CalliString calliStringRaw)
                {
                    int newFaceIndex;
                    switch (tag.tagType)
                    {
                        case TagType.Italic:
                            if (!tag.isClosing)
                                m_isItalic = true;
                            else
                                m_isItalic = false;

                            newFaceIndex = fontTable.GetFaceIndex(FontAssetRef);
                            m_faceIndex  = newFaceIndex == -1 ? m_faceIndex : newFaceIndex;
                            if (fontTable.faces[m_faceIndex].HasVarData)
                                fontTable.GetNamedVariationLookup(FontAssetRef, out m_namedVariationIndex);
                            return;
                        case TagType.Bold:
                            if (!tag.isClosing)
                            {
                                m_fontWeight = FontWeight.Bold.Value();
                                m_fontWeightStack.Add(m_fontWeight);
                            }
                            else
                                m_fontWeight = m_fontWeightStack.RemoveExceptRoot();

                            newFaceIndex = fontTable.GetFaceIndex(FontAssetRef);
                            m_faceIndex  = newFaceIndex == -1 ? m_faceIndex : newFaceIndex;
                            if (fontTable.faces[m_faceIndex].HasVarData)
                                fontTable.GetNamedVariationLookup(FontAssetRef, out m_namedVariationIndex);
                            return;
                        case TagType.FontWeight:
                            if (!tag.isClosing)
                            {
                                m_fontWeight = tag.value.NumericalValue;
                                m_fontWeightStack.Add(m_fontWeight);
                            }
                            else
                                m_fontWeight = m_fontWeightStack.RemoveExceptRoot();

                            newFaceIndex = fontTable.GetFaceIndex(FontAssetRef);
                            m_faceIndex  = newFaceIndex == -1 ? m_faceIndex : newFaceIndex;
                            if (fontTable.faces[m_faceIndex].HasVarData)
                                fontTable.GetNamedVariationLookup(FontAssetRef, out m_namedVariationIndex);
                            return;
                        case TagType.FontWidth:
                            if (!tag.isClosing)
                            {
                                m_fontWidth = tag.value.NumericalValue;
                                m_fontWidthStack.Add(m_fontWidth);
                            }
                            else
                                m_fontWidth = m_fontWidthStack.RemoveExceptRoot();

                            newFaceIndex = fontTable.GetFaceIndex(FontAssetRef);
                            m_faceIndex  = newFaceIndex == -1 ? m_faceIndex : newFaceIndex;
                            if (fontTable.faces[m_faceIndex].HasVarData)
                                fontTable.GetNamedVariationLookup(FontAssetRef, out m_namedVariationIndex);
                            return;
                        case TagType.Font:
                            if (!tag.isClosing)
                            {
                                if (tag.value.stringValue == StringValue.Default)
                                    m_fontFamilyHash = m_fontFamilyHashStack[0];
                                else
                                {
                                    //fetch name of font from calliStringRaw Buffer
                                    //To-Do: better to store valueHash in tag struct
                                    FixedString128Bytes stringValue = default;  //should not happen too often, so should be OK to allocate here
                                    calliStringRaw.GetSubString(ref stringValue, tag.value.valueStart, tag.value.valueLength);
                                    m_fontFamilyHash = TextHelper.GetHashCodeCaseInsensitive(stringValue);
                                    m_fontFamilyHashStack.Add(m_fontFamilyHash);
                                }

                                newFaceIndex = fontTable.GetFaceIndex(FontAssetRef);
                                m_faceIndex  = newFaceIndex == -1 ? m_faceIndex : newFaceIndex;
                                if (fontTable.faces[m_faceIndex].HasVarData)
                                    fontTable.GetNamedVariationLookup(FontAssetRef, out m_namedVariationIndex);
                                return;
                            }
                            else
                            {
                                m_fontFamilyHash = m_fontFamilyHashStack.RemoveExceptRoot();

                                newFaceIndex = fontTable.GetFaceIndex(FontAssetRef);
                                m_faceIndex  = newFaceIndex == -1 ? m_faceIndex : newFaceIndex;
                                if (fontTable.faces[m_faceIndex].HasVarData)
                                    fontTable.GetNamedVariationLookup(FontAssetRef, out m_namedVariationIndex);
                            }
                            return;
                    }
                }
            }

            // Use LayoutConfig to change case prior to hb-shape. Works only for latin text
            // Should this use cases really be in scope of Latios.Calligraphics?
            struct LayoutConfig
            {
                public FontStyles m_fontStyles;

                public LayoutConfig(in TextBaseConfiguration textBaseConfiguration)
                {
                    m_fontStyles = textBaseConfiguration.fontStyles;
                }
                public void Reset(in TextBaseConfiguration textBaseConfiguration)
                {
                    m_fontStyles = textBaseConfiguration.fontStyles;
                }
                public void Update(ref XMLTag tag)
                {
                    switch (tag.tagType)
                    {
                        case TagType.AllCaps:
                        case TagType.Uppercase:
                        {
                            if (tag.isClosing)
                                m_fontStyles &= ~FontStyles.UpperCase;
                            else
                                m_fontStyles |= FontStyles.UpperCase;
                        }
                        break;
                        case TagType.Lowercase:
                        {
                            if (tag.isClosing)
                                m_fontStyles &= ~FontStyles.LowerCase;
                            else
                                m_fontStyles |= FontStyles.LowerCase;
                        }
                        break;
                    }
                }
            }
        }
    }
}

