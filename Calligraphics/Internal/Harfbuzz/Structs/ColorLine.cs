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
            uint count = 16;
            colorStops = new NativeList<ColorStop>((int)count, Allocator.Temp);
            colorStops.Length = (int)count;
            uint len = default;
            unsafe
            {
                len = Harfbuzz.hb_color_line_get_color_stops(ptr, 0, ref count, colorStops.GetUnsafePtr());
            }
            if (len > count)
            {
                Debug.Log("capacity of 16 was not sufficient, increasing");
                colorStops = new NativeList<ColorStop>((int)len, Allocator.Temp);
                unsafe
                {
                    len = Harfbuzz.hb_color_line_get_color_stops(ptr, 0, ref len, colorStops.GetUnsafePtr());
                }
            }
            colorStops.Length = (int)len;
            return (int)len;
        }

        public PaintExtend GetExtend()
        {
            return Harfbuzz.hb_color_line_get_extend(ptr);
        }
    };
    //struct hb_color_line_t
    //{
    //    void* data;

    //    hb_color_line_get_color_stops_func_t get_color_stops;
    //    void* get_color_stops_user_data;

    //    hb_color_line_get_extend_func_t get_extend;
    //    void* get_extend_user_data;

    //    void* reserved0;
    //    void* reserved1;
    //    void* reserved2;
    //    void* reserved3;
    //    void* reserved5;
    //    void* reserved6;
    //    void* reserved7;
    //    void* reserved8;
    //};
}
