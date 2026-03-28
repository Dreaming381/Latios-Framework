using System;
using Unity.Collections;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct Image : IDisposable
    {
        public IntPtr ptr;
        public void GetExtents(out RasterExtents rasterExtents)
        {
            Harfbuzz.hb_raster_image_get_extents(ptr, out rasterExtents);
        }

        public NativeArray<byte> GetAlpha(RasterExtents rasterExtents)
        {
            NativeArray<byte> result;
            unsafe
            {
                var bytes  = Harfbuzz.hb_raster_image_get_buffer(ptr);
                var length = (int)(rasterExtents.width * rasterExtents.height * 4);
                result     = Harfbuzz.GetNativeArray(bytes, length);
            }
            return result;
        }
        public NativeArray<ColorBGRA> GetColorBGRA(RasterExtents rasterExtents)
        {
            NativeArray<byte> result;
            unsafe
            {
                var bytes  = Harfbuzz.hb_raster_image_get_buffer(ptr);
                var length = (int)(rasterExtents.width * rasterExtents.height * 4);
                result     = Harfbuzz.GetNativeArray(bytes, length);
            }
            return result.Reinterpret<ColorBGRA>(sizeof(byte));
        }

        public bool TryDeserializeFromPNG(Blob png)
        {
            return Harfbuzz.hb_raster_image_deserialize_from_png_or_fail(ptr, png);
        }

        public void RecycleImage(Paint paint)
        {
            Harfbuzz.hb_raster_paint_recycle_image(paint.ptr, ptr);
        }

        public void Dispose()
        {
            Harfbuzz.hb_raster_image_destroy(ptr);
        }
    }
}

