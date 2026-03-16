using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct ShapePlan : IDisposable
    {
        public IntPtr ptr;
        public ShapePlan(Face face, ref SegmentProperties props, NativeList<Feature> features, IntPtr shaper_list)
        {
            unsafe
            {
                ptr = Harfbuzz.hb_shape_plan_create_cached(face.ptr, ref props, (IntPtr)features.GetUnsafePtr(), (uint)features.Length, shaper_list);
            }            
        }
        public void Execute(Font font, Buffer buffer, NativeList<Feature> features)
        {
            unsafe
            {
                Harfbuzz.hb_shape_plan_execute(ptr, font, buffer, (IntPtr)features.GetUnsafePtr(), (uint)features.Length);
            }
        }

        public void Dispose()
        {
            Harfbuzz.hb_shape_plan_destroy(ptr);
        }
    }    
}