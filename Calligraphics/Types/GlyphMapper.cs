using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    /// <summary>
    /// A utility struct that is used for reading mapping data from the input TextRenderer string
    /// to output glyphs. This can be used to aid with glyph post-processing, such as animations.
    /// </summary>
    public struct GlyphMapper
    {
        NativeArray<int2> m_buffer;
        const int         kHeaderSize = 5;

        /// <summary>
        /// The number of lines that were mapped. 0 if line mapping was not enabled.
        /// </summary>
        public int lineCount => m_buffer[0].y;
        /// <summary>
        /// The number of words that were mapping. 0 if word mapping was not enabled.
        /// </summary>
        public int wordCount => m_buffer[1].y;
        /// <summary>
        /// The number of characters that were mapped, not counting characters which were part of rich text tag markups.
        /// 0 if this style of character mapping was not enabled.
        /// </summary>
        public int characterCountNoTags => m_buffer[2].y;
        /// <summary>
        /// The number of characters that were mapping. 0 if this style of character mapping was not enabled.
        /// </summary>
        public int characterCountWithTags => m_buffer[3].y;
        /// <summary>
        /// The number of raw bytes that were mapping. 0 if byte mapping was not enabled.
        /// </summary>
        public int byteCountWithTags => m_buffer[4].y;

        public GlyphMapper(DynamicBuffer<GlyphMappingElement> mappingBuffer)
        {
            m_buffer = mappingBuffer.AsNativeArray().Reinterpret<int2>();
        }

        /// <summary>
        /// Gets the start index and number of glyphs included in the line specified by lineIndex
        /// </summary>
        /// <param name="lineIndex">Which line in the source text to get the glyph range for</param>
        /// <returns>The x component contains the start index, while the y component contains the count</returns>
        public int2 GetGlyphStartIndexAndCountForLine(int lineIndex) => m_buffer[m_buffer[0].x + lineIndex];

        /// <summary>
        /// Gets the start index and number of glyphs included in the word specified by wordIndex
        /// </summary>
        /// <param name="wordIndex">Which word in the source text to get the glyph range for</param>
        /// <returns>The x component contains the start index, while the y component contains the count</returns>
        public int2 GetGlyphStartIndexAndCountForWord(int wordIndex) => m_buffer[m_buffer[1].x + wordIndex];

        /// <summary>
        /// Attempts to get the glyph index corresponding to the character excluding rich text tags.
        /// </summary>
        /// <param name="charIndex">The character index not counting rich text tags</param>
        /// <param name="glyphIndex">The corresponding glyph index, if one exists</param>
        /// <returns>True if there is a corresponding glyph, false otherwise</returns>
        public bool TryGetGlyphIndexForCharNoTags(int charIndex, out int glyphIndex)
        {
            var batchIndex = charIndex / 32;
            if (batchIndex < 0 || batchIndex >= m_buffer[2].y)
            {
                glyphIndex = 0;
                return false;
            }
            var bitIndex = charIndex % 32;
            var batch    = m_buffer[m_buffer[2].x + batchIndex];
            var mask     = 0xffffffff >> (32 - bitIndex);
            glyphIndex   = batch.x + math.select(math.countbits(mask & batch.y), 0, bitIndex == 0);
            return (batch.y & (1 << bitIndex)) != 0;
        }

        /// <summary>
        /// Attempts to get the glyph index corresponding to the character including rich text tags.
        /// </summary>
        /// <param name="charIndex">The character index</param>
        /// <param name="glyphIndex">The corresponding glyph index, if one exists</param>
        /// <returns>True if there is a corresponding glyph, false otherwise</returns>
        public bool TryGetGlyphIndexForCharWithTags(int charIndex, out int glyphIndex)
        {
            var batchIndex = charIndex / 32;
            if (batchIndex < 0 || batchIndex >= m_buffer[3].y)
            {
                glyphIndex = 0;
                return false;
            }
            var bitIndex = charIndex % 32;
            var batch    = m_buffer[m_buffer[3].x + batchIndex];
            var mask     = 0xffffffff >> (32 - bitIndex);
            glyphIndex   = batch.x + math.select(math.countbits(mask & batch.y), 0, bitIndex == 0);
            return (batch.y & (1 << bitIndex)) != 0;
        }

        /// <summary>
        /// Attempts to get the glyph index corresponding to the byte (which includes rich text tags).
        /// </summary>
        /// <param name="charIndex">The byte index</param>
        /// <param name="glyphIndex">The corresponding glyph index, if one exists</param>
        /// <returns>True if there is a corresponding glyph, false otherwise</returns>
        public bool TryGetGlyphIndexForByte(int byteIndex, out int glyphIndex)
        {
            var batchIndex = byteIndex / 32;
            if (batchIndex < 0 || batchIndex >= m_buffer[4].y)
            {
                glyphIndex = 0;
                return false;
            }
            var bitIndex = byteIndex % 32;
            var batch    = m_buffer[m_buffer[4].x + batchIndex];
            var mask     = 0xffffffff >> (32 - bitIndex);
            glyphIndex   = batch.x + math.select(math.countbits(mask & batch.y), 0, bitIndex == 0);
            return (batch.y & (1 << bitIndex)) != 0;
        }
    }
}

