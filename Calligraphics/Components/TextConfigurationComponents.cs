using System;
using Latios.Calligraphics.HarfBuzz;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics
{
    /// <summary>
    /// The base settings of the text before any rich text tags or animations are applied.
    /// Usage: ReadWrite
    /// </summary>
    public struct TextBaseConfiguration : IComponentData
    {
        ///// <summary>The hash code of the font's family name, which can be computed using TypeHash.FNV1A64<T>()
        ///// Do NOT use the managed string version to compute the hash!
        ///// </summary>
        public int defaultFontFamilyHash;

        /// <summary>
        /// The line width of the font, in world units, only if word wrapping is enabled
        /// </summary>
        public float maxLineWidth;

        /// <summary>
        /// Additional word spacing in font units where a value of 1 equals 0.01 em
        /// </summary>
        public half wordSpacing;

        /// <summary>
        /// Additional line spacing in font units where a value of 1 equals 0.01 em
        /// </summary>
        public half lineSpacing;

        /// <summary>
        /// Additional paragraph spacing in font units where a value of 1 equals 0.01 em
        /// </summary>
        public half paragraphSpacing;

        /// <summary>
        /// The size of the font, in point sizes
        /// </summary>
        public half fontSize;

        //internal int samplingSize => fontTextureSize.GetSamplingSize();
        internal uint packed;  // 2 bits left unused, we may want to use these for text direction when we better support that

        /// <summary>
        /// The color of the rendered text
        /// </summary>
        public Color32 color;

        public BlobAssetReference<LanguageBlob> language;

        /// <summary>
        /// The various font styling flags. Readout of bold style only during authoring, otherwise this library will use fontweight (changeable via xml tags)
        /// </summary>
        public FontStyles fontStyles
        {
            get => (FontStyles)Bits.GetBits(packed, 16, 11);
            set => Bits.SetBits(ref packed, 16, 11, (uint)value);
        }

        /// <summary>
        /// The thickness or intensity of the font. Changeable via xml tags
        /// </summary>
        public FontWeight fontWeight
        {
            get => (FontWeight)Bits.GetBits(packed, 8, 4);
            set => Bits.SetBits(ref packed, 8, 4, (uint)value);
        }
        /// <summary>
        /// The compression or widening of the font across a line. Changeable via xml tags
        /// </summary>
        public FontWidth fontWidth
        {
            get => (FontWidth)Bits.GetBits(packed, 12, 4);
            set => Bits.SetBits(ref packed, 12, 4, (uint)value);
        }

        /// <summary>
        /// The horizontal alignment mode of the text
        /// </summary>
        public HorizontalAlignmentOptions lineJustification
        {
            get => (HorizontalAlignmentOptions)Bits.GetBits(packed, 0, 3);
            set => Bits.SetBits(ref packed, 0, 3, (uint)value);
        }
        /// <summary>
        /// The vertical alignment mode of the text
        /// </summary>
        public VerticalAlignmentOptions verticalAlignment
        {
            get => (VerticalAlignmentOptions)Bits.GetBits(packed, 4, 4);
            set => Bits.SetBits(ref packed, 4, 4, (uint)value);
        }
        /// <summary>
        /// The size of the characters in the texture
        /// </summary>
        public FontTextureSize fontTextureSize
        {
            get => (FontTextureSize)Bits.GetBits(packed, 27, 2);
            set => Bits.SetBits(ref packed, 27, 2, (uint)value);
        }

        /// <summary>
        /// Use orthographic-mode computation of character sizes
        /// </summary>
        public bool isOrthographic
        {
            get => Bits.GetBit(packed, 30);
            set => Bits.SetBit(ref packed, 30, value);
        }

        /// <summary>
        /// Enable word wrapping
        /// </summary>
        public bool wordWrap
        {
            get => Bits.GetBit(packed, 31);
            set => Bits.SetBit(ref packed, 31, value);
        }
        public void SetFamily(FixedString128Bytes fontFamily)
        {
            defaultFontFamilyHash = TextHelper.GetHashCodeCaseInsensitive(fontFamily);
        }
    }
    /// <summary>
    /// Horizontal text alignment options.
    /// </summary>
    public enum HorizontalAlignmentOptions : byte
    {
        Left,
        Center,
        Right,
        Justified,
        //Flush,
        //Geometry
    }

    /// <summary>
    /// Vertical text alignment options.
    /// </summary>
    public enum VerticalAlignmentOptions : byte
    {
        TopBase,
        TopAscent,
        TopDescent,
        TopCap,
        TopMean,
        BottomBase,
        BottomAscent,
        BottomDescent,
        BottomCap,
        BottomMean,
        MiddleTopAscentToBottomDescent,
    }

    public struct LanguageBlob
    {
        public BlobString langugage;
        public FixedString32Bytes ToFixedString()
        {
            var result = new FixedString32Bytes();
            if (langugage.Length > 0)
                langugage.CopyTo(ref result);
            return result;
        }
    }
    public enum FontWeight : byte
    {
        //https://learn.microsoft.com/en-us/typography/opentype/spec/os2#usweightclass
        // All values are divided by 100 for data compression
        // To-Do: store raw value as short because there are a number of fonts deviating
        // from the convention, e.g.
        // Microsoft YaHei Light (C:/WINDOWS/Fonts/msyhl.ttc) has a weight of 290,
        // Yu Gothic UI Semilight (C:/WINDOWS/Fonts/YuGothR.ttc) has a weight of 350
        Thin = 1,
        ExtraLight = 2,
        UltraLight = 2,
        Light = 3,
        Normal = 4,
        Regular = 4,
        Medium = 5,
        SemiBold = 6,
        DemiBold = 6,
        Bold = 7,
        ExtraBold = 8,
        UltraBold = 8,
        Black = 9,
        Heavy = 9,
    }

    public enum FontWidth : byte
    {
        //https://learn.microsoft.com/en-us/typography/opentype/spec/os2#uswidthclass
        // Enum values do not correspond to raw values
        UltraCondensed = 1,  // 50
        ExtraCondensed = 2,  // 62.5
        Narrow = 3,  // 75
        Condensed = 3,  // 75
        SemiCondensed = 4,  // 87.5
        Normal = 5,  // 100
        SemiExpanded = 6,  // 112.5
        Expanded = 7,  // 125
        ExtraExpanded = 8,  // 150
        UltraExpanded = 9  // 200
    }

    [Flags]
    public enum FontStyles
    {
        [InspectorName("N")] Normal = 0,
        [InspectorName("B")] Bold = 0x1,
        [InspectorName("I")] Italic = 0x2,
        //[InspectorName("_")] Underline = 0x4,
        [InspectorName("low")] LowerCase = 0x8,
        [InspectorName("UPP")] UpperCase = 0x10,
        [InspectorName("Sᴍᴀʟʟ")] SmallCaps = 0x20,
        //[InspectorName("-")] Strikethrough = 0x40,
        [InspectorName("x²")] Superscript = 0x80,
        [InspectorName("x₂")] Subscript = 0x100,
        //[InspectorName("[]")] Highlight = 0x200,
        [InspectorName("½")] Fraction = 0x400,
    }
    public enum FontTextureSize
    {
        Normal = 0,
        Big = 1,
        Massive = 2,
    }

    public struct LanguageCode
    {
        internal uint code;

        public LanguageCode(char a, char b, char c, char d)
        {
            code = Harfbuzz.HB_TAG(a, b, c, d);
        }
    }

    public static class FontEnumerationExtensions
    {
        public static float Value(this FontWidth fontWidth) => fontWidth switch
        {
            FontWidth.UltraCondensed => 50f,
            FontWidth.ExtraCondensed => 62.5f,
            FontWidth.Narrow => 75f,
            FontWidth.SemiCondensed => 87.5f,
            FontWidth.Normal => 100f,
            FontWidth.SemiExpanded => 112.5f,
            FontWidth.Expanded => 125f,
            FontWidth.ExtraExpanded => 150f,
            FontWidth.UltraExpanded => 200f,
            _ => float.NaN,
        };

        public static int Value(this FontWeight fontWeight) => 100 * (int)fontWeight;

        internal static int GetSamplingSize(this GlyphTable.Key key)
        {
            return GetSamplingSize(key.format, key.textureSize);
        }
        internal static int GetSamplingSize(RenderFormat format, FontTextureSize textureSize)
        {
            switch ((format, textureSize))
            {
                case (RenderFormat.SDF8, FontTextureSize.Normal):
                case (RenderFormat.SDF8, FontTextureSize.Big):
                case (RenderFormat.SDF8, FontTextureSize.Massive):
                    return 96;
                case (RenderFormat.SDF16, FontTextureSize.Big):
                    return 256;
                case (RenderFormat.SDF16, FontTextureSize.Massive):
                    return 1024;
                case (RenderFormat.Bitmap8888, FontTextureSize.Normal):
                    return 128;
                case (RenderFormat.Bitmap8888, FontTextureSize.Big):
                    return 512;
                case (RenderFormat.Bitmap8888, FontTextureSize.Massive):
                    return 2048;
                default:
                    return 64;
            }
        }
        internal static int GetSpread(this GlyphTable.Key key)
        {
            return GetSpread(key.format, key.textureSize);
        }
        internal static int GetSpread(RenderFormat format, FontTextureSize textureSize)
        {
            switch ((format, textureSize))
            {
                case (RenderFormat.SDF8, FontTextureSize.Normal):
                case (RenderFormat.SDF8, FontTextureSize.Big):
                case (RenderFormat.SDF8, FontTextureSize.Massive):
                    return 12;
                case (RenderFormat.SDF16, FontTextureSize.Normal):
                case (RenderFormat.SDF16, FontTextureSize.Big):
                    return 32;
                case (RenderFormat.SDF16, FontTextureSize.Massive):
                    return 128;
                case (RenderFormat.Bitmap8888, FontTextureSize.Normal):
                case (RenderFormat.Bitmap8888, FontTextureSize.Big):
                case (RenderFormat.Bitmap8888, FontTextureSize.Massive):
                    return -1;  // We add one to get padding
                default:
                    return 12;
            }
        }
    }
}

