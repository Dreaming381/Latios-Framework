using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ColorStop
    {
        public float offset;
        [MarshalAs(UnmanagedType.I1)]
        public bool isForeground;
        public ColorARGB color;
    }
    internal struct ColorStopComparer : IComparer<ColorStop>
    {
        public int Compare(ColorStop a, ColorStop b)
        {
            return a.offset.CompareTo(b.offset);            
        }
    }
}
