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
        /// <summary>
        /// The horizontal alignment mode of the text
        /// </summary>
        public HorizontalAlignmentOptions lineJustification
        {
            get => (HorizontalAlignmentOptions)((m_alignmentWeightOrtho & 0x70) >> 4);
            set => m_alignmentWeightOrtho = (ushort)((m_alignmentWeightOrtho & ~0x70) | ((ushort)value << 4));
        }
        /// <summary>
        /// The vertical alignment mode of the text
        /// </summary>
        public VerticalAlignmentOptions verticalAlignment
        {
            get => (VerticalAlignmentOptions)((m_alignmentWeightOrtho & 0x380) >> 7);
            set => m_alignmentWeightOrtho = (ushort)((m_alignmentWeightOrtho & ~0x380) | ((ushort)value << 7));
        }
        public FontWeight fontWeight
        {
            get => (FontWeight)(m_alignmentWeightOrtho & 0x0f);
            set => m_alignmentWeightOrtho = (ushort)((m_alignmentWeightOrtho & ~0x0f) | (ushort)value);
        }
        public FontStyles fontStyle
        {
            get => (FontStyles)m_fontStyleFlags;
            set => m_fontStyleFlags = (ushort)value;
        }
        public bool isOrthographic
        {
            get => (m_alignmentWeightOrtho & 0x8000) != 0;
            set => m_alignmentWeightOrtho = (ushort)((m_alignmentWeightOrtho & 0x7fff) | (value ? 0x8000 : 0));
        }
        public bool enableKerning;

        private ushort m_fontStyleFlags;  // 6 bits unused, but Unity may add more.
        ushort         m_alignmentWeightOrtho;  // 5 bits unused.
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
        Top,
        Middle,
        Bottom,
        Baseline,
        Geometry,
        Capline,
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

