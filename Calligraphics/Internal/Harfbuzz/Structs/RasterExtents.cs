using System.Runtime.InteropServices;

namespace Latios.Calligraphics.HarfBuzz
{
    /// <summary> Dimensions of glyph according to harfbuzz definition (y is top to bottom ). Invert height for use in a coordinate systems that grows up.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct RasterExtents
    {
        public int  x_origin;
        public int  y_origin;
        public uint width;
        public uint height;
        public uint stride;
    }
}

