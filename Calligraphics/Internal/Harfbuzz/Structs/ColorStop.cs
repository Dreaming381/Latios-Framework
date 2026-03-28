using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ColorStop
    {
        public float offset;
        private int isForeground; //hb_bool_t is 4 bytes!
        private uint colorRaw;        // raw 0xBBGGRRAA uint as written by C
        public bool IsForeground => isForeground != 0;

        // Converts on read using the bit-shift path — endianness-safe
        public ColorBGRA Color => colorRaw; // uses implicit operator ColorBGRA(uint)
    }
    internal struct ColorStopComparer : IComparer<ColorStop>
    {
        public int Compare(ColorStop a, ColorStop b)
        {
            return a.offset.CompareTo(b.offset);            
        }
    }
}
