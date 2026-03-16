using System.Runtime.InteropServices;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct FontExtents
    {
        public int ascender;
        public int descender;
        public int line_gap;
        public int reserved9;
        public int reserved8;
        public int reserved7;
        public int reserved6;
        public int reserved5;
        public int reserved4;
        public int reserved3;
        public int reserved2;
        public int reserved1;
    }
}
