using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ColorLine
    {
        IntPtr ptr;

        public int GetColorStops(uint start, out NativeList<ColorStop> colorStops)
        {
            // Cheap first call — no buffer, no count pointer, just get the total
            uint totalCount = Harfbuzz.hb_color_line_get_color_stops(ptr, start, IntPtr.Zero, IntPtr.Zero);

            // Single allocation, exactly the right size
            colorStops = new NativeList<ColorStop>((int)totalCount, Allocator.Temp);
            colorStops.Length = (int)totalCount;

            unsafe
            {
                Harfbuzz.hb_color_line_get_color_stops(ptr, start, ref totalCount, colorStops.GetUnsafePtr());
            }

            colorStops.Length = (int)totalCount;
            return (int)totalCount;
        }

        public PaintExtend GetExtend()
        {
            return Harfbuzz.hb_color_line_get_extend(ptr);
        }
    };
}
