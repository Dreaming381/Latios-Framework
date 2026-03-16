using System;
using Unity.Collections;

namespace Latios.Calligraphics.HarfBuzz
{
    internal unsafe struct Buffer : IDisposable
    {
        public IntPtr ptr;
        public Buffer(Direction direction, Script script, Language language)
        {
            ptr = Harfbuzz.hb_buffer_create();
            Direction = direction;
            Script = script;
            Language = language;
        }
        public Buffer(bool dummyProperty)
        {
            ptr = Harfbuzz.hb_buffer_create();
        }
        public Direction Direction {
            get { return Harfbuzz.hb_buffer_get_direction(ptr); }
            set { Harfbuzz.hb_buffer_set_direction(ptr, value); } 
        }
        public Script Script {
            get { return Harfbuzz.hb_buffer_get_script(ptr); }
            set { Harfbuzz.hb_buffer_set_script(ptr, value); } 
        }
        public Language Language
        {
            get => Harfbuzz.hb_buffer_get_language(ptr);
            set => Harfbuzz.hb_buffer_set_language(ptr, value);
        }
        public BufferFlag BufferFlag
        {
            get { return Harfbuzz.hb_buffer_get_flags(ptr); }
            set { Harfbuzz.hb_buffer_set_flags(ptr, value); }
        }

        public ContentType ContentType
        {
            get => Harfbuzz.hb_buffer_get_content_type(ptr);
            set => Harfbuzz.hb_buffer_set_content_type(ptr, value);
        }
        public ClusterLevel ClusterLevel
        {
            get => Harfbuzz.hb_buffer_get_cluster_level(ptr);
            set => Harfbuzz.hb_buffer_set_cluster_level(ptr, value);
        }

        public void GetSegmentProperties(out SegmentProperties segmentProperties)
        {
            Harfbuzz.hb_buffer_get_segment_properties(ptr, out segmentProperties);
        }
        public void SetSegmentProperties(ref SegmentProperties segmentProperties)
        {
            Harfbuzz.hb_buffer_set_segment_properties(ptr, ref segmentProperties);
        }
        public void GuessSegmentProperties()
        {
            Harfbuzz.hb_buffer_guess_segment_properties(ptr);
        }
        public uint Length => Harfbuzz.hb_buffer_get_length(ptr);
        public void Add(uint codepoint, uint cluster)
        {
            if ((int)Length != 0 && (ContentType != ContentType.UNICODE))
                throw new InvalidOperationException("Non empty buffer's ContentType must be of type Unicode.");
            if (ContentType == ContentType.GLYPHS)
                throw new InvalidOperationException("ContentType must not be of type Glyphs");

            Harfbuzz.hb_buffer_add(ptr, codepoint, cluster);
        }

        public void AddText(string str)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(str);
            fixed (byte* text = bytes)
            {
                Harfbuzz.hb_buffer_add_utf8(ptr, text, bytes.Length, 0, bytes.Length);
            }
        }
        public void AddText<T>(T text, uint startIndex, int length) where T : unmanaged, INativeList<byte>, IUTF8Bytes
        {
            Harfbuzz.hb_buffer_add_utf8(ptr, text.GetUnsafePtr(), text.Length, startIndex, length);
        }
        public void ClearContent()
        {
            Harfbuzz.hb_buffer_clear_contents(ptr);
        }
        public bool AllocationSuccessful()
        {
            return Harfbuzz.hb_buffer_allocation_successful(ptr);
        }
        public void Reset()
        {
            Harfbuzz.hb_buffer_reset(ptr);
        }
        public void Dispose()
        {
            Harfbuzz.hb_buffer_destroy(ptr);
        }

        public unsafe ReadOnlySpan<GlyphInfo> GetGlyphInfosSpan()
        {
            var infoPtrs = Harfbuzz.hb_buffer_get_glyph_infos(ptr, out uint length);
            return new ReadOnlySpan<GlyphInfo>(infoPtrs, (int)length);
        }

        public unsafe ReadOnlySpan<GlyphPosition> GetGlyphPositionsSpan()
        {
            var infoPtrs = Harfbuzz.hb_buffer_get_glyph_positions(ptr, out uint length);
            return new ReadOnlySpan<GlyphPosition>(infoPtrs, (int)length);
        }
    }
}