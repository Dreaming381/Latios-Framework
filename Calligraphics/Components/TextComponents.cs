using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Latios.Calligraphics
{
    /// <summary>
    /// A reference to the font blob asset used for text rendering.
    /// If you choose to change this at runtime, you must also change the material designed to work with the font.
    /// Usage: Typically don't touch, but can be read-write if you know what you are doing.
    /// </summary>
    public struct FontBlobReference : IComponentData
    {
        public BlobAssetReference<FontBlob> blob;
    }

    /// <summary>
    /// The base settings of the text before any rich text tags or animations are applied.
    /// Usage: ReadWrite
    /// </summary>
    public struct TextBaseConfiguration : IComponentData
    {
        // Todo: Current size is 28 bytes. We could make this 20 bytes by using half for
        // fontSize, wordSpacing, lineSpacing, and paragraphSpacing. Worth it?

        /// <summary>
        /// The size of the font, in font sizes
        /// </summary>
        public float fontSize;
        /// <summary>
        /// The line width of the font, in world units.
        /// </summary>
        public float maxLineWidth;
        /// <summary>
        /// The vertex colors of the rendered text
        /// </summary>
        public Color32 color;

        public float wordSpacing;
        public float lineSpacing;
        public float paragraphSpacing;
        /// <summary>
        /// The horizontal alignment mode of the text
        /// </summary>
        public HorizontalAlignmentOptions lineJustification
        {
            get => (HorizontalAlignmentOptions)((m_alignmentWeightOrthoKerning & 0x70) >> 4);
            set => m_alignmentWeightOrthoKerning = (ushort)((m_alignmentWeightOrthoKerning & ~0x70) | ((ushort)value << 4));
        }
        /// <summary>
        /// The vertical alignment mode of the text
        /// </summary>
        public VerticalAlignmentOptions verticalAlignment
        {
            get => (VerticalAlignmentOptions)((m_alignmentWeightOrthoKerning & 0x780) >> 7);
            set => m_alignmentWeightOrthoKerning = (ushort)((m_alignmentWeightOrthoKerning & ~0x780) | ((ushort)value << 7));
        }
        public FontWeight fontWeight
        {
            get => (FontWeight)(m_alignmentWeightOrthoKerning & 0x0f);
            set => m_alignmentWeightOrthoKerning = (ushort)((m_alignmentWeightOrthoKerning & ~0x0f) | (ushort)value);
        }
        public FontStyles fontStyle
        {
            get => (FontStyles)m_fontStyleFlags;
            set => m_fontStyleFlags = (ushort)value;
        }
        public bool isOrthographic
        {
            get => (m_alignmentWeightOrthoKerning & 0x8000) != 0;
            set => m_alignmentWeightOrthoKerning = (ushort)((m_alignmentWeightOrthoKerning & 0x7fff) | (value ? 0x8000 : 0));
        }
        public bool enableKerning
        {
            get => (m_alignmentWeightOrthoKerning & 0x4000) != 0;
            set => m_alignmentWeightOrthoKerning = (ushort)((m_alignmentWeightOrthoKerning & 0xbfff) | (value ? 0x4000 : 0));
        }

        ushort m_fontStyleFlags;  // 6 bits unused, but Unity may add more.
        ushort m_alignmentWeightOrthoKerning;  // 3 bits unused.
    }

    /// <summary>
    /// The raw byte element as part of the text string.
    /// Prefer to use TextRendererAspect or cast to CalliString instead.
    /// Usage: ReadWrite, but using the abstraction tools.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct CalliByte : IBufferElementData
    {
        public byte element;
    }

    /// <summary>
    /// The backing memory for a GlyphMapper struct. Cast to a GlyphMapper
    /// to get a mapping of source string to RenderGlyph to post-process the glyphs.
    /// Usage: ReadOnly
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct GlyphMappingElement : IBufferElementData
    {
        public int2 element;
    }

    /// <summary>
    /// The mask used to command glyph generation which mappings to generate.
    /// Generating more mappings has a slightly large performance cost and a
    /// potentially significant memory cost.
    /// </summary>
    public struct GlyphMappingMask : IComponentData
    {
        public enum WriteMask : byte
        {
            None = 0,
            Line = 0x1,
            Word = 0x2,
            CharNoTags = 0x4,
            CharWithTags = 0x8,
            Byte = 0x10,
        }

        public WriteMask mask;
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
        Flush,
        Geometry
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

    public enum FontWeight
    {
        Thin,
        ExtraLight,
        Light,
        Regular,
        Medium,
        SemiBold,
        Bold,
        Heavy,
        Black
    };
}

