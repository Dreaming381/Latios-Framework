using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct Font : IDisposable
    {
        public IntPtr ptr;

        //variable profile data
        internal int currentVariableProfileIndex;

        //cache a couple of font meta data to avoid fetching them upon every font access
        internal int         baseLine;
        internal FontExtents fontExtents;
        internal int         capHeight;
        internal int         xHeight;
        internal int         xWidth;
        public Font(Face face)
        {
            ptr                         = Harfbuzz.hb_font_create(face.ptr);
            currentVariableProfileIndex = -1;
            baseLine                    = default;
            fontExtents                 = default;
            capHeight                   = default;
            xHeight                     = default;
            xWidth                      = default;
        }

        /// <summary>
        /// Update font metadata, some of which depends on language and script direction..so watchout when to call this method
        /// </summary>
        public void UpdateMetaData(Direction direction, Script script, Language language)
        {
            this.GetBaseline(direction, script, language, out baseLine);
            this.GetFontExtentsForDirection(direction, out fontExtents);
            this.GetMetrics(MetricTag.CAP_HEIGHT, out capHeight);
            this.GetMetrics(MetricTag.X_HEIGHT, out xHeight);

            // get width of space -->TO-DO: need to find an easier way to do this !!!
            // Why is this not accessible via a metric tag such as MetricTag.X_WIDTH?
            var buffer         = new Buffer(direction, script, language);
            buffer.ContentType = ContentType.UNICODE;
            buffer.Add(0x20, 0);
            this.Shape(buffer);
            var glyphPosition = buffer.GetGlyphPositionsSpan();
            xWidth            = glyphPosition[0].xAdvance;
            buffer.Dispose();
        }
        public int TabAdvance() => xWidth * 10;
        public float GetStyleTag(StyleTag styleTag)
        {
            return Harfbuzz.hb_style_get_value(ptr, styleTag);
        }
        public uint2 GetPPEM()
        {
            Harfbuzz.hb_font_get_ppem(ptr, out uint x_ppem, out uint y_ppem);
            return new uint2(x_ppem, y_ppem);
        }
        public void SetPPEM(uint2 ppem)
        {
            Harfbuzz.hb_font_set_ppem(ptr, ppem.x, ppem.y);
        }
        public float GetPTEM()
        {
            return Harfbuzz.hb_font_get_ptem(ptr);
        }
        public void SetPTEM(float ptem)
        {
            Harfbuzz.hb_font_set_ptem(ptr, ptem);
        }
        public void DrawGlyph(uint glyphID, DrawDelegates drawFunctions, ref DrawData drawData)
        {
            Harfbuzz.hb_font_draw_glyph(ptr, glyphID, drawFunctions, ref drawData);
        }
        public void PaintGlyph(uint glyphID, ref PaintData paintData, PaintDelegates paintFunctions, uint palette, ColorBGRA foreground)
        {
            Harfbuzz.hb_font_paint_glyph(ptr, glyphID, paintFunctions, ref paintData, palette, foreground);
        }
        public bool TryPaintGlyph(uint glyphID, IntPtr paintFunctions, IntPtr paintData, uint palette, ColorBGRA foreground)
        {
            return Harfbuzz.hb_font_paint_glyph_or_fail(ptr, glyphID, paintFunctions, paintData, palette, foreground);
        }        

        public void GetSyntheticBold(out float x_embolden, out float y_embolden, out bool in_place)
        {
            Harfbuzz.hb_font_get_synthetic_bold(ptr, out x_embolden, out y_embolden, out in_place);
        }
        public float GetSynthesticSlant()
        {
            return Harfbuzz.hb_font_get_synthetic_slant(ptr);
        }
        public int2 GetScale()
        {
            Harfbuzz.hb_font_get_scale(ptr, out int x_scale, out int y_scale);
            return new int2(x_scale, y_scale);
        }
        public void SetScale(int x_scale, int y_scale)
        {
            Harfbuzz.hb_font_set_scale(ptr, x_scale, y_scale);
        }
        public void GetMetrics(MetricTag metricTag, out int position)
        {
            Harfbuzz.hb_ot_metrics_get_position(ptr, metricTag, out position);
        }
        /// <summary> Get Glyph extends form harfbuzz, but invert the height as y axis is asumed to go up in this library </summary>
        public bool GetGlyphExtents(uint glyph, out GlyphExtents extends)
        {
            var success = Harfbuzz.hb_font_get_glyph_extents(ptr, glyph, out extends);
            extends.InvertY();  // For legacy reasons, Harfbuzz returns height as negative.
            return success;
        }
        public void GetFontExtentsForDirection(Direction direction, out FontExtents fontExtents)
        {
            Harfbuzz.hb_font_get_extents_for_direction(ptr, direction, out fontExtents);
        }
        public void GetBaseline(Direction direction, Script script, Language language, out int baseline)
        {
            Harfbuzz.hb_ot_layout_get_baseline(ptr, LayoutBaselineTag.ROMAN, direction, script, language, out baseline);
        }

        public void SetVariation(AxisTag axisTag, float value)
        {
            Harfbuzz.hb_font_set_variation(ptr, axisTag, value);
        }
        public void SetVariations(NativeList<Variation> variations)
        {
            unsafe
            {
                Harfbuzz.hb_font_set_variations(ptr, (IntPtr)variations.GetUnsafePtr(), (uint)variations.Length);
            }
        }

        public uint VariationNamedInstance
        {
            get { return Harfbuzz.hb_font_get_var_named_instance(ptr); }
            set { Harfbuzz.hb_font_set_var_named_instance(ptr, value); }
        }

        public void Shape(Buffer buffer, NativeList<Feature> features)
        {
            unsafe
            {
                Harfbuzz.hb_shape(ptr, buffer.ptr, (IntPtr)features.GetUnsafePtr(), (uint)features.Length);
            }
        }
        public void Shape(Buffer buffer)
        {
            Harfbuzz.hb_shape(ptr, buffer.ptr, IntPtr.Zero, 0u);
        }

        //public void GetGlyphAdvanceForDirection(uint glyph, Direction direction, out int x, out int y)
        //{
        //    fixed (int* xPtr = &x)
        //    fixed (int* yPtr = &y)
        //    {
        //        HarfBuzzApi.hb_font_get_glyph_advance_for_direction(ptr, glyph, direction, xPtr, yPtr);
        //    }
        //}
        public bool IsImmutable() => Harfbuzz.hb_font_is_immutable(ptr);
        public void MakeImmutable()
        {
            Harfbuzz.hb_font_make_immutable(ptr);
        }
        public void Dispose()
        {
            Harfbuzz.hb_font_destroy(ptr);
        }
    }
}

