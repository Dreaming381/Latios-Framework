//using System.Runtime.InteropServices;
//using System;
//using UnityEngine;
//using Unity.Mathematics;
//using static Latios.Calligraphics.HarfBuzz.DrawDelegates;
//using Unity.Collections;
//using Unity.Burst;
//using AOT;
//using Latios.Calligraphics.HarfBuzz.Bitmap;

//namespace Latios.Calligraphics.HarfBuzz
//{
//    [BurstCompile]
//    public struct PaintDeferredDelegates : IDisposable
//    {
//        public IntPtr ptr;
//        public void Dispose()
//        {
//            HB.hb_paint_funcs_destroy(ptr);
//        }
//        public PaintDeferredDelegates(bool dummyProperty)
//        {
//            ptr = HB.hb_paint_funcs_create();
//            //FunctionPointer<PushTransformDelegate> pushTransformFunctionPointer = BurstCompiler.CompileFunctionPointer<PushTransformDelegate>(HB_paint_push_transform_func_t);
//            //FunctionPointer<PopDelegate> popTransformFunctionPointer = BurstCompiler.CompileFunctionPointer<PopDelegate>(HB_paint_pop_transform_func_t);
//            //FunctionPointer<ColorGlyphDelegate> colorGlyphFunctionPointer = BurstCompiler.CompileFunctionPointer<ColorGlyphDelegate>(hb_paint_color_glyph_func_t);
//            //FunctionPointer<PushClipGlyphDelegate> pushClipGlyphFunctionPointer = BurstCompiler.CompileFunctionPointer<PushClipGlyphDelegate>(HB_paint_push_clip_glyph_func_t);
//            //FunctionPointer<PushClipRectangleDelegate> pushClipRectangleFunctionPointer = BurstCompiler.CompileFunctionPointer<PushClipRectangleDelegate>(HB_paint_push_clip_rectangle_func_t);
//            //FunctionPointer<PopDelegate> popClipFunctionPointer = BurstCompiler.CompileFunctionPointer<PopDelegate>(HB_paint_pop_clip_func_t);
//            //FunctionPointer<ColorDelegate> colorFunctionPointer = BurstCompiler.CompileFunctionPointer<ColorDelegate>(HB_paint_color_func_t);
//            //FunctionPointer<LinearOrRadialGradientDelegate> linearGradientFunctionPointer = BurstCompiler.CompileFunctionPointer<LinearOrRadialGradientDelegate>(HB_paint_linear_gradient_func_t);
//            //FunctionPointer<LinearOrRadialGradientDelegate> radialGradientFunctionPointer = BurstCompiler.CompileFunctionPointer<LinearOrRadialGradientDelegate>(HB_paint_radial_gradient_func_t);
//            //FunctionPointer<SweepGradientDelegate> sweepGradientFunctionPointer = BurstCompiler.CompileFunctionPointer<SweepGradientDelegate>(HB_paint_sweep_gradient_func_t);
//            //FunctionPointer<PopDelegate> pushGroupFunctionPointer = BurstCompiler.CompileFunctionPointer<PopDelegate>(HB_paint_push_group_func_t);
//            //FunctionPointer<PopGroupDelegate> popGroupFunctionPointer = BurstCompiler.CompileFunctionPointer<PopGroupDelegate>(HB_paint_pop_group_func_t);
//            //FunctionPointer<CustomPalette_colorDelegate> customPaletteColorFunctionPointer = BurstCompiler.CompileFunctionPointer<CustomPalette_colorDelegate>(hb_paint_custom_palette_color_func_t);
//            //FunctionPointer<ImageDelegate> imageFunctionPointer = BurstCompiler.CompileFunctionPointer<ImageDelegate>(hb_paint_image_func_t);
//            //FunctionPointer<ReleaseDelegate> releaseFunctionPointer = BurstCompiler.CompileFunctionPointer<ReleaseDelegate>(Test);

//            //HB.hb_paint_funcs_set_push_transform_func(ptr, pushTransformFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_pop_transform_func(ptr, popTransformFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_color_glyph_func(ptr, colorGlyphFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_push_clip_glyph_func(ptr, pushClipGlyphFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_push_clip_rectangle_func(ptr, pushClipRectangleFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_pop_clip_func(ptr, popClipFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_color_func(ptr, colorFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_linear_gradient_func(ptr, linearGradientFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_radial_gradient_func(ptr, radialGradientFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_sweep_gradient_func(ptr, sweepGradientFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_push_group_func(ptr, pushGroupFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_pop_group_func(ptr, popGroupFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            ////HB.hb_paint_funcs_set_custom_palette_color_func(ptr, customPaletteColorFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
//            //HB.hb_paint_funcs_set_image_func(ptr, imageFunctionPointer, IntPtr.Zero, releaseFunctionPointer);

//            //HB.hb_paint_funcs_make_immutable(ptr);
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(PushTransformDelegate))]
//        public static void HB_paint_push_transform_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, float xx, float yx, float xy, float yy, float dx, float dy, IntPtr user_data)
//        {
//            var transform = new float2x3
//            {
//                c0 = new float2(xx, yx),
//                c1 = new float2(xy, yy),
//                c2 = new float2(dx, dy)
//            };
//            transform = PaintUtils.mul(transform, data.transformStack.Peek());
//            data.transformStack.Add(transform);
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(PopDelegate))]
//        public static void HB_paint_pop_transform_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, IntPtr user_data)
//        {
//            data.transformStack.Pop();
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(ColorGlyphDelegate))]
//        public static bool hb_paint_color_glyph_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, uint glyphID, IntPtr font, IntPtr user_data)
//        {
//            return true;
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(PushClipGlyphDelegate))]
//        public static void HB_paint_push_clip_glyph_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, uint glyphID, IntPtr font, IntPtr user_data)
//        {
//            data.glyphID = glyphID;
//            //when deferring the rasterization, we cannot clear the transformed clipGlyph in
//            //HB_paint_pop_clip_func_t or nothing will be rendered. Rather clear when setting a new clipGlyph
//            data.clipGlyph.Clear();
//            HB.hb_font_draw_glyph(font, glyphID, data.drawDelegates, ref data.clipGlyph);
//            PaintUtils.TransformGlyph(ref data.clipGlyph, data.transformStack.Peek());
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(PushClipRectangleDelegate))]
//        public static void HB_paint_push_clip_rectangle_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, float xmin, float ymin, float xmax, float ymax, IntPtr user_data)
//        {
//            var clipRect = new BBox(xmin, ymin,xmax, ymax);
//            data.clipRect = clipRect;
//            data.paintSurface = new NativeArray<ColorARGB>((int)(clipRect.width) * (int)clipRect.height, Allocator.Temp);
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(PopDelegate))]
//        public static void HB_paint_pop_clip_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, IntPtr user_data)
//        {
//            //when deferring the rasterization, we cannot clear the transformed clipGlyph in
//            //HB_paint_pop_clip_func_t or nothing will be rendered. Rather clear when setting a new clipGlyph
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(ColorDelegate))]
//        public static void HB_paint_color_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, bool is_foreground, uint color, IntPtr user_data)
//        {
//            var colorARGB = (ColorARGB)color;
//            data.solidColor = new SolidColor(colorARGB);
//            data.patterType = PatterType.SolidColor;
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(LinearOrRadialGradientDelegate))]
//        public static void HB_paint_linear_gradient_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, ColorLine colorLine, float x0, float y0, float x1, float y1, float x2, float y2, IntPtr user_data)
//        {
//            var lineGradient = new LineGradient(x0, y0, x1, y1, x2, y2, colorLine.GetExtend(), data.transformStack.Peek());
//            if (!lineGradient.isValid)
//                return;

//            lineGradient.InitializeColorLine(colorLine);
//            data.lineGradient = lineGradient;
//            data.patterType = PatterType.LineGradient;
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(LinearOrRadialGradientDelegate))]
//        public static void HB_paint_radial_gradient_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, ColorLine colorLine, float x0, float y0, float r0, float x1, float y1, float r1, IntPtr user_data)
//        {
//            var radialGradient = new RadialGradient(x0, y0, r0, x1, y1, r1, colorLine.GetExtend(), data.transformStack.Peek());
//            if (!radialGradient.isValid)
//                return;

//            radialGradient.InitializeColorLine(colorLine);
//            data.radialGradient = radialGradient;
//            data.patterType = PatterType.RadialGradient;
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(SweepGradientDelegate))]
//        public static void HB_paint_sweep_gradient_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, ColorLine colorLine, float x0, float y0, float startAngle, float endAngle, IntPtr user_data)
//        {
//            var sweepGradient = new SweepGradient(x0, y0, startAngle, endAngle, colorLine.GetExtend(), data.transformStack.Peek());
//            sweepGradient.InitializeColorLine(colorLine);
//            if (!sweepGradient.isValid)
//                return;

//            data.sweepGradient = sweepGradient;
//            data.patterType = PatterType.SweepGradient;
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(PopDelegate))]
//        public static void HB_paint_push_group_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, IntPtr user_data)
//        {
//            // COMPOSITE: 
//            // push_group()
//            // // recurse for backdrop
//            // push_group()
//            // // recurse for source
//            // pop_group_and_composite(composite_mode)
//            // pop_group_and_composite(OVER)

//            // layers:
//            //foreach layer
//            //    push_group()
//            //    // recurse for layer paint
//            //    pop_group_and_composite(OVER)

//            data.group++;
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(PopGroupDelegate))]
//        public static void HB_paint_pop_group_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, PaintCompositeMode mode, IntPtr user_data)
//        {
//            //deferred renderer: render transformed clipGlyph using the pattern defined in data.patterType,
//            //and blend it already rendered data existing data.paintSurface using defined PaintCompositeMode
//            //(review: does this work in all cases? or would background and foreground need to be rendered into their own empty texture
//            //before blending the 2 together? See also combining logic in comment under HB_paint_push_group_func_t 
//            switch (data.patterType)
//            {
//                case PatterType.SolidColor:
//                    AntiAliasedRasterizer.RasterizeAndBlend(ref data.clipGlyph, data.paintSurface, data.solidColor, mode, data.clipRect);
//                    break;
//                case PatterType.LineGradient:
//                    AntiAliasedRasterizer.RasterizeAndBlend(ref data.clipGlyph, data.paintSurface, data.lineGradient, mode, data.clipRect);
//                    break;
//                case PatterType.RadialGradient:
//                    AntiAliasedRasterizer.RasterizeAndBlend(ref data.clipGlyph, data.paintSurface, data.radialGradient, mode, data.clipRect);
//                    break;
//                case PatterType.SweepGradient:
//                    AntiAliasedRasterizer.RasterizeAndBlend(ref data.clipGlyph, data.paintSurface, data.sweepGradient, mode, data.clipRect);
//                    break;
//            }
//            data.patterType = PatterType.Undefined;
//            data.group--;
//        }
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(CustomPalette_colorDelegate))]
//        public static bool hb_paint_custom_palette_color_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, uint color_index, uint color, IntPtr user_data)
//        {
//            //Debug.Log($"hb_paint_custom_palette_color color_index {color_index} color {color}");
//            return true;
//        }

//        /// <summary>
//        /// This callback converts the image data found for a given glyph either to a NativeArray of colors that can be directly applied to a texture 
//        /// (in case of raw BRGA data stored in Apple sbix or Google CDBT), or to the raw PNG and SVG bytes. PNG can SVG can currently not be converted 
//        /// to a NativeArray of colors in a BURST compatible way.
//        /// </summary>
//        [BurstCompile]
//        [MonoPInvokeCallback(typeof(ImageDelegate))]
//        public static bool hb_paint_image_func_t(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, Blob image, uint width, uint height, PaintImageFormat format, float slant, ref GlyphExtents extents, IntPtr user_data)
//        {
//            Debug.Log("hb_paint_image");
//            data.imageFormat = format;
//            data.imageWidth = (int)width;
//            data.imageHeight = (int)height;
//            var rawBytes = image.GetData();
//            if (format == PaintImageFormat.BGRA)
//            {
//                var rawBytesLength = rawBytes.Length;
//                var textureData = new NativeArray<ColorARGB>(rawBytesLength / 4, Allocator.Temp);
//                int count = 0;
//                for (int i = 0, ii = rawBytes.Length; i < ii; i += 4)
//                    textureData[count++] = new ColorARGB(rawBytes[i + 3], rawBytes[i + 2], rawBytes[i + 1], rawBytes[i]);
//                data.paintSurface = textureData;
//            }
//            else // HB_PAINT_IMAGE_FORMAT.PNG, HB_PAINT_IMAGE_FORMAT.SVG To-Do: find BURST compatible decoder
//                data.imageData = rawBytes;

//            Debug.Log($"width {width} height {height}  format {format} {data.imageData.Length}");
//            return true;
//        }

//        public delegate void PushTransformDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, float xx, float yx, float xy, float yy, float dx, float dy, IntPtr user_data);

//        public delegate void PopDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, IntPtr user_data);

//        [return: MarshalAs(UnmanagedType.I1)]
//        public delegate bool ColorGlyphDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, uint glyph, IntPtr font, IntPtr user_data);

//        public delegate void PushClipGlyphDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, uint glyph, IntPtr font, IntPtr user_data);

//        public delegate void PushClipRectangleDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, float xmin, float ymin, float xmax, float ymax, IntPtr user_data);

//        public delegate void ColorDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, bool is_foreground, uint color, IntPtr user_data);

//        [return: MarshalAs(UnmanagedType.I1)]
//        public delegate bool ImageDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, Blob image, uint width, uint height, PaintImageFormat format, float slant, ref GlyphExtents extents, IntPtr user_data);

//        public delegate void LinearOrRadialGradientDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, ColorLine color_line, float x0, float y0, float x1, float y1, float x2, float y2, IntPtr user_data);

//        public delegate void SweepGradientDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, ColorLine color_line, float x0, float y0, float start_angle, float end_angle, IntPtr user_data);

//        public delegate void PopGroupDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, PaintCompositeMode mode, IntPtr user_data);

//        [return: MarshalAs(UnmanagedType.I1)]
//        public delegate bool CustomPalette_colorDelegate(IntPtr harfBuzzPaintFunct, ref PaintDeferredData data, uint color_index, uint color, IntPtr user_data);
//    }


//}
