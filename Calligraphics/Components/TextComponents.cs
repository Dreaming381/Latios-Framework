using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

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
        /// The horizontal and vertical alignment modes of the text
        /// </summary>
        public AlignMode alignMode;
    }

    /// <summary>
    /// An additional rendered text entity containing a different font and material.
    /// Currently unsupported.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct AdditionalFontMaterialEntity : IBufferElementData
    {
        public EntityWith<FontBlobReference> entity;
    }

    /// <summary>
    /// A per-glyph index into the font and material that should be used to render it.
    /// Currently unsupported.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct FontMaterialSelectorForGlyph : IBufferElementData
    {
        public byte fontMaterialIndex;
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
    /// The packed alignment modes for both horizontal and vertical directions.
    /// </summary>
    public enum AlignMode : byte
    {
        Left = 0x0,
        Right = 0x1,
        Center = 0x2,
        Justified = 0x3,
        //
        Top = 0x0,
        Middle = 0x1 << 2,
        Bottom = 0x2 << 2,
        //
        HorizontalMask = 0x3,
        VerticalMask = 0xc,
    }
}

