using System;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct Paint : IDisposable
    {
        public IntPtr ptr;

        public Paint(bool dummy)
        {
            ptr = Harfbuzz.hb_raster_paint_create_or_fail();
        }

        //  lifecycle

        //increase reference count
        public IntPtr Reference()
        => Harfbuzz.hb_raster_paint_reference(ptr);

        //decrease reference count
        public void Dispose()
        {
            if (ptr != IntPtr.Zero)
            {
                Harfbuzz.hb_raster_paint_destroy(ptr);
                ptr = IntPtr.Zero;
            }
        }

        // user data

        public bool SetUserData(IntPtr key, IntPtr data, IntPtr destroy, bool replace)
        => Harfbuzz.hb_raster_paint_set_user_data(ptr, key, data, destroy, replace);

        public IntPtr GetUserData(IntPtr key)
        => Harfbuzz.hb_raster_paint_get_user_data(ptr, key);

        // configuration

        public void SetScaleFactor(float xScaleFactor, float yScaleFactor)
        => Harfbuzz.hb_raster_paint_set_scale_factor(ptr, xScaleFactor, yScaleFactor);

        public void GetScaleFactor(out float xScaleFactor, out float yScaleFactor)
        => Harfbuzz.hb_raster_paint_get_scale_factor(ptr, out xScaleFactor, out yScaleFactor);

        public void SetTransform(float xx, float yx, float xy, float yy, float dx, float dy)
        => Harfbuzz.hb_raster_paint_set_transform(ptr, xx, yx, xy, yy, dx, dy);

        public void GetTransform(out float xx, out float yx, out float xy, out float yy, out float dx, out float dy)
        => Harfbuzz.hb_raster_paint_get_transform(ptr, out xx, out yx, out xy, out yy, out dx, out dy);

        public void SetForeground(uint colorBGRA)
        => Harfbuzz.hb_raster_paint_set_foreground(ptr, colorBGRA);

        // Convenience overload keeping your existing ColorBGRA type
        public void SetForeground(ColorBGRA foreground)
        => Harfbuzz.hb_raster_paint_set_foreground(ptr, foreground.bgra);

        public void SetExtents(ref RasterExtents extents)
        => Harfbuzz.hb_raster_paint_set_extents(ptr, ref extents);

        public void GetExtents(out RasterExtents extents)
        => Harfbuzz.hb_raster_paint_get_extents(ptr, out extents);

        public void SetGlyphExtents(ref GlyphExtents glyphExtents)
        => Harfbuzz.hb_raster_paint_set_glyph_extents(ptr, ref glyphExtents);

        // custom CPAL palette overrides

        // Sets a per-entry override for a CPAL palette colour.
        // colorIndex is the CPAL colour index; colorBGRA is unpremultiplied BGRA.
        public void SetCustomPaletteColor(uint colorIndex, uint colorBGRA)
        => Harfbuzz.hb_raster_paint_set_custom_palette_color(ptr, colorIndex, colorBGRA);

        public void ClearCustomPaletteColors()
        => Harfbuzz.hb_raster_paint_clear_custom_palette_colors(ptr);

        //  paint functions pointer (for hb_font_paint_glyph_or_fail)

        public IntPtr GetPaintFuncs()
        => Harfbuzz.hb_raster_paint_get_funcs();

        //  rendering

        // High-level convenience: sets svg_glyph/svg_font/svg_palette internally
        // then drives hb_font_paint_glyph_or_fail.
        // palette: CPAL palette index (pass 0 for default)
        public bool PaintGlyph(Font font, uint glyphID, float penX, float penY, uint palette, ColorBGRA foreground)
        => Harfbuzz.hb_raster_paint_glyph(ptr, font.ptr, glyphID, penX, penY, palette, foreground);

        /// <summary>
        /// Retrieve the rendered BGRA32 image (call after PaintGlyph). Caller must destroy or recycle the returned Image with RecycleImage when done.
        /// </summary>
        public Image Render()
        => Harfbuzz.hb_raster_paint_render(ptr);

        // Return an image obtained from Render() back to the internal pool.
        public void RecycleImage(Image image)
        => Harfbuzz.hb_raster_paint_recycle_image(ptr, image.ptr);

        // Reset paint state between glyphs (reuses allocated buffers).
        public void Reset()
        => Harfbuzz.hb_raster_paint_reset(ptr);
    }
}

