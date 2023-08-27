using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    internal struct GlyphMappingWriter
    {
        UnsafeList<int2> m_glyphStartAndCountByLine;
        UnsafeList<int2> m_glyphStartAndCountByWord;
        UnsafeList<int2> m_glyphIndexByCharNoTags;
        UnsafeList<int2> m_glyphIndexByCharWithTags;
        UnsafeList<int2> m_glyphIndexByByte;

        int                        m_charNoTagsCount;
        int                        m_charWithTagsCount;
        int                        m_byteCount;
        bool                       m_startedLine;
        bool                       m_startedWord;
        GlyphMappingMask.WriteMask m_writeMask;

        public void StartWriter(GlyphMappingMask.WriteMask writeMask)
        {
            if (!m_glyphStartAndCountByLine.IsCreated)
            {
                m_glyphStartAndCountByLine = new UnsafeList<int2>(16, Allocator.Temp);
                m_glyphStartAndCountByWord = new UnsafeList<int2>(64, Allocator.Temp);
                m_glyphIndexByCharNoTags   = new UnsafeList<int2>(256, Allocator.Temp);
                m_glyphIndexByCharWithTags = new UnsafeList<int2>(256, Allocator.Temp);
                m_glyphIndexByByte         = new UnsafeList<int2>(256, Allocator.Temp);
            }

            m_glyphStartAndCountByLine.Clear();
            m_glyphStartAndCountByWord.Clear();
            m_glyphIndexByCharNoTags.Clear();
            m_glyphIndexByCharWithTags.Clear();
            m_glyphIndexByByte.Clear();

            m_charNoTagsCount   = 0;
            m_charWithTagsCount = 0;
            m_byteCount         = 0;
            m_startedLine       = false;
            m_startedWord       = false;
            m_writeMask         = writeMask;
        }

        public void AddLineStart(int glyphIndex)
        {
            if ((m_writeMask & GlyphMappingMask.WriteMask.Line) != GlyphMappingMask.WriteMask.Line)
                return;

            if (m_startedLine)
            {
                ref var back = ref m_glyphStartAndCountByLine.ElementAt(m_glyphStartAndCountByLine.Length - 1);
                back.y       = glyphIndex - back.x;
            }
            m_glyphStartAndCountByLine.Add(new int2(glyphIndex, 0));
            m_startedLine = true;
        }

        public void AddWordStart(int glyphIndex)
        {
            if ((m_writeMask & GlyphMappingMask.WriteMask.Word) != GlyphMappingMask.WriteMask.Word)
                return;

            if (m_startedWord)
            {
                ref var back = ref m_glyphStartAndCountByWord.ElementAt(m_glyphStartAndCountByWord.Length - 1);
                back.y       = glyphIndex - back.x;
            }
            m_glyphStartAndCountByWord.Add(new int2(glyphIndex, 0));
            m_startedWord = true;
        }

        public void AddCharNoTags(int charIndex, bool hasGlyph)
        {
            if ((m_writeMask & GlyphMappingMask.WriteMask.CharNoTags) != GlyphMappingMask.WriteMask.CharNoTags)
                return;

            while (m_charNoTagsCount < charIndex)
                AddCharNoTags(m_charNoTagsCount, false);

            var bitIndex = m_charNoTagsCount % 32;
            if (bitIndex == 0)
            {
                if (m_charNoTagsCount == 0)
                    m_glyphIndexByCharNoTags.Add(int2.zero);
                else
                {
                    var previous = m_glyphIndexByCharNoTags[m_glyphIndexByCharNoTags.Length - 1];
                    m_glyphIndexByCharNoTags.Add(new int2(previous.x + math.countbits(previous.y), 0));
                }
            }

            if (hasGlyph)
            {
                ref var back  = ref m_glyphIndexByCharNoTags.ElementAt(m_glyphIndexByCharNoTags.Length - 1);
                back.y       |= (1 << bitIndex);
            }
            m_charNoTagsCount++;
        }

        public void AddCharWithTags(int charIndex, bool hasGlyph)
        {
            if ((m_writeMask & GlyphMappingMask.WriteMask.CharWithTags) != GlyphMappingMask.WriteMask.CharWithTags)
                return;

            while (m_charWithTagsCount < charIndex)
                AddCharWithTags(m_charWithTagsCount, false);

            var bitIndex = m_charWithTagsCount % 32;
            if (bitIndex == 0)
            {
                if (m_charWithTagsCount == 0)
                    m_glyphIndexByCharWithTags.Add(int2.zero);
                else
                {
                    var previous = m_glyphIndexByCharWithTags[m_glyphIndexByCharWithTags.Length - 1];
                    m_glyphIndexByCharWithTags.Add(new int2(previous.x + math.countbits(previous.y), 0));
                }
            }

            if (hasGlyph)
            {
                ref var back  = ref m_glyphIndexByCharWithTags.ElementAt(m_glyphIndexByCharWithTags.Length - 1);
                back.y       |= (1 << bitIndex);
            }
            m_charWithTagsCount++;
        }

        public void AddBytes(int byteIndex, int byteCount, bool hasGlyph)
        {
            if ((m_writeMask & GlyphMappingMask.WriteMask.Byte) != GlyphMappingMask.WriteMask.Byte)
                return;

            while (m_byteCount < byteIndex)
                AddBytes(m_byteCount, 1, false);

            bool4 hasGlyphs = false;
            hasGlyphs.x     = hasGlyph;
            for (int i = 0; i < byteCount; i++)
            {
                var bitIndex = m_byteCount % 32;
                if (bitIndex == 0)
                {
                    if (m_byteCount == 0)
                        m_glyphIndexByByte.Add(int2.zero);
                    else
                    {
                        var previous = m_glyphIndexByByte[m_glyphIndexByByte.Length - 1];
                        m_glyphIndexByByte.Add(new int2(previous.x + math.countbits(previous.y), 0));
                    }
                }

                if (hasGlyphs[i])
                {
                    ref var back  = ref m_glyphIndexByByte.ElementAt(m_glyphIndexByByte.Length - 1);
                    back.y       |= (1 << bitIndex);
                }
                m_byteCount++;
            }
        }

        public void EndWriter(ref DynamicBuffer<GlyphMappingElement> mappingsBuffer, int glyphCount)
        {
            // Finish lines and words
            if (m_startedLine)
            {
                ref var back = ref m_glyphStartAndCountByLine.ElementAt(m_glyphStartAndCountByLine.Length - 1);
                back.y       = glyphCount - back.x;
            }
            if (m_startedWord)
            {
                ref var back = ref m_glyphStartAndCountByWord.ElementAt(m_glyphStartAndCountByWord.Length - 1);
                back.y       = glyphCount - back.x;
            }

            // Trim anticipated words and newlines that never came to fruition.
            while (m_glyphStartAndCountByLine[m_glyphStartAndCountByLine.Length - 1].x == glyphCount)
                m_glyphStartAndCountByLine.RemoveAtSwapBack(m_glyphStartAndCountByLine.Length - 1);
            while (m_glyphStartAndCountByWord[m_glyphStartAndCountByWord.Length - 1].x == glyphCount)
                m_glyphStartAndCountByWord.RemoveAtSwapBack(m_glyphStartAndCountByWord.Length - 1);

            mappingsBuffer.Clear();

            // The first five elements serve as a start and count header to the remaining embedded arrays. Starts are absolute.
            int elementsRequired = 5 + m_glyphStartAndCountByLine.Length + m_glyphStartAndCountByWord.Length + m_glyphIndexByCharNoTags.Length + m_glyphIndexByCharWithTags.Length +
                                   m_glyphIndexByByte.Length;
            mappingsBuffer.EnsureCapacity(elementsRequired);
            elementsRequired                                      = 5;
            mappingsBuffer.Add(new GlyphMappingElement { element  = new int2(elementsRequired, m_glyphStartAndCountByLine.Length) });
            elementsRequired                                     += m_glyphStartAndCountByLine.Length;
            mappingsBuffer.Add(new GlyphMappingElement { element  = new int2(elementsRequired, m_glyphStartAndCountByWord.Length) });
            elementsRequired                                     += m_glyphStartAndCountByWord.Length;
            mappingsBuffer.Add(new GlyphMappingElement { element  = new int2(elementsRequired, m_glyphIndexByCharNoTags.Length) });
            elementsRequired                                     += m_glyphIndexByCharNoTags.Length;
            mappingsBuffer.Add(new GlyphMappingElement { element  = new int2(elementsRequired, m_glyphIndexByCharWithTags.Length) });
            elementsRequired                                     += m_glyphIndexByCharWithTags.Length;
            mappingsBuffer.Add(new GlyphMappingElement { element  = new int2(elementsRequired, m_glyphIndexByByte.Length) });
            elementsRequired                                     += m_glyphIndexByByte.Length;

            foreach (var element in m_glyphStartAndCountByLine)
                mappingsBuffer.Add(new GlyphMappingElement { element = element });
            foreach (var element in m_glyphStartAndCountByWord)
                mappingsBuffer.Add(new GlyphMappingElement { element = element });
            foreach (var element in m_glyphIndexByCharNoTags)
                mappingsBuffer.Add(new GlyphMappingElement { element = element });
            foreach (var element in m_glyphIndexByCharWithTags)
                mappingsBuffer.Add(new GlyphMappingElement { element = element });
            foreach (var element in m_glyphIndexByByte)
                mappingsBuffer.Add(new GlyphMappingElement { element = element });
        }
    }
}

