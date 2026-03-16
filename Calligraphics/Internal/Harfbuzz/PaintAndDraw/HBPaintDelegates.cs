using System.Runtime.InteropServices;
using System;
using UnityEngine;
using Unity.Mathematics;
using static Latios.Calligraphics.HarfBuzz.DrawDelegates;
using Unity.Collections;
using Unity.Burst;
using AOT;
using Latios.Calligraphics.HarfBuzz.Bitmap;

namespace Latios.Calligraphics.HarfBuzz
{
    [BurstCompile]
    internal struct PaintDelegates : IDisposable
    {
        public IntPtr ptr;
        public void Dispose()
        {
            Harfbuzz.hb_paint_funcs_destroy(ptr);
        }

        // https://github.com/harfbuzz/harfbuzz/pull/4498 changed how PaintColrLayers (format 1) and PaintComposite (format 32) are rendered.
        // specificaly, it removes usage of push group and pop group for PaintColrLayers, and thereby alpha blending between layers
        // Replacing call to Raserize() by call to RasterizeAndBlend() using the SRC_OVER blend mode fixes that.
        // Where those functions are used, it is unknown if the caller was PaintColrLayers or PaintComposite
        // See https://github.com/harfbuzz/harfbuzz/blob/9fbc2d23b5bc3436cf2d99172ea09d56d857f583/src/OT/Color/COLR/COLR.hh for implementation of
        // PaintColrLayers and PaintComposite. Unknown if usage of RasterizeAndBlend breaks rendering of PaintComposite path.
        // To-Do: verify using the test included in the commit
        // Rendering algorithm for all formats: https://learn.microsoft.com/en-us/typography/opentype/spec/colr#colr-version-1-rendering-algorithm 
        public PaintDelegates(bool dummyProperty)
        {
            ptr = Harfbuzz.hb_paint_funcs_create();
            FunctionPointer<PushTransformDelegate> pushTransformFunctionPointer = BurstCompiler.CompileFunctionPointer<PushTransformDelegate>(HB_paint_push_transform_func_t);
            FunctionPointer<PopDelegate> popTransformFunctionPointer = BurstCompiler.CompileFunctionPointer<PopDelegate>(HB_paint_pop_transform_func_t);
            FunctionPointer<ColorGlyphDelegate> colorGlyphFunctionPointer = BurstCompiler.CompileFunctionPointer<ColorGlyphDelegate>(hb_paint_color_glyph_func_t);
            FunctionPointer<PushClipGlyphDelegate> pushClipGlyphFunctionPointer = BurstCompiler.CompileFunctionPointer<PushClipGlyphDelegate>(HB_paint_push_clip_glyph_func_t);
            FunctionPointer<PushClipRectangleDelegate> pushClipRectangleFunctionPointer = BurstCompiler.CompileFunctionPointer<PushClipRectangleDelegate>(HB_paint_push_clip_rectangle_func_t);
            FunctionPointer<PopDelegate> popClipFunctionPointer = BurstCompiler.CompileFunctionPointer<PopDelegate>(HB_paint_pop_clip_func_t);
            FunctionPointer<ColorDelegate> colorFunctionPointer = BurstCompiler.CompileFunctionPointer<ColorDelegate>(HB_paint_color_func_t);
            FunctionPointer<LinearOrRadialGradientDelegate> linearGradientFunctionPointer = BurstCompiler.CompileFunctionPointer<LinearOrRadialGradientDelegate>(HB_paint_linear_gradient_func_t);
            FunctionPointer<LinearOrRadialGradientDelegate> radialGradientFunctionPointer = BurstCompiler.CompileFunctionPointer<LinearOrRadialGradientDelegate>(HB_paint_radial_gradient_func_t);
            FunctionPointer<SweepGradientDelegate> sweepGradientFunctionPointer = BurstCompiler.CompileFunctionPointer<SweepGradientDelegate>(HB_paint_sweep_gradient_func_t);
            FunctionPointer<PopDelegate> pushGroupFunctionPointer = BurstCompiler.CompileFunctionPointer<PopDelegate>(HB_paint_push_group_func_t);
            FunctionPointer<PopGroupDelegate> popGroupFunctionPointer = BurstCompiler.CompileFunctionPointer<PopGroupDelegate>(HB_paint_pop_group_func_t);
            FunctionPointer<CustomPalette_colorDelegate> customPaletteColorFunctionPointer = BurstCompiler.CompileFunctionPointer<CustomPalette_colorDelegate>(hb_paint_custom_palette_color_func_t);
            FunctionPointer<ImageDelegate> imageFunctionPointer = BurstCompiler.CompileFunctionPointer<ImageDelegate>(hb_paint_image_func_t);
            FunctionPointer<ReleaseDelegate> releaseFunctionPointer = BurstCompiler.CompileFunctionPointer<ReleaseDelegate>(Test);

            Harfbuzz.hb_paint_funcs_set_push_transform_func(ptr, pushTransformFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_pop_transform_func(ptr, popTransformFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_color_glyph_func(ptr, colorGlyphFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_push_clip_glyph_func(ptr, pushClipGlyphFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_push_clip_rectangle_func(ptr, pushClipRectangleFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_pop_clip_func(ptr, popClipFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_color_func(ptr, colorFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_linear_gradient_func(ptr, linearGradientFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_radial_gradient_func(ptr, radialGradientFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_sweep_gradient_func(ptr, sweepGradientFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_push_group_func(ptr, pushGroupFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_pop_group_func(ptr, popGroupFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            //HB.hb_paint_funcs_set_custom_palette_color_func(ptr, customPaletteColorFunctionPointer, IntPtr.Zero, releaseFunctionPointer);
            Harfbuzz.hb_paint_funcs_set_image_func(ptr, imageFunctionPointer, IntPtr.Zero, releaseFunctionPointer);

            Harfbuzz.hb_paint_funcs_make_immutable(ptr);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(PushTransformDelegate))]
        public static void HB_paint_push_transform_func_t (IntPtr harfBuzzPaintFunct, ref PaintData data, float xx, float yx, float xy, float yy, float dx, float dy, IntPtr user_data)
        {

            //Debug.Log($"Push transform");
            var transform = new float2x3
            {
                c0 = new float2(xx, yx),
                c1 = new float2(xy, yy),
                c2 = new float2(dx, dy)
            };
            //transform = PaintUtils.mul(data.transformStack.Peek(), transform);
            transform = PaintUtils.mul(transform, data.transformStack.Peek());
            data.transformStack.Add(transform);           
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(PopDelegate))]
        public static void HB_paint_pop_transform_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, IntPtr user_data)
        {
            //Debug.Log($"Pop transform");
            data.transformStack.Pop();
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(ColorGlyphDelegate))]
        public static bool hb_paint_color_glyph_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, uint glyphID, IntPtr font, IntPtr user_data)
        {
            //Debug.Log($"Paint Color glyph {glyphID}");
            return true;
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(PushClipGlyphDelegate))]
        public static void HB_paint_push_clip_glyph_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, uint glyphID, IntPtr font, IntPtr user_data)
        {
            //Debug.Log($"Push clip glyph {glyphID}");
            data.glyphID = glyphID;
            Harfbuzz.hb_font_draw_glyph(font, glyphID, data.drawDelegates, ref data.clipGlyph);
            PaintUtils.TransformGlyph(ref data.clipGlyph, data.transformStack.Peek());
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(PushClipRectangleDelegate))]
        public static void HB_paint_push_clip_rectangle_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, float xmin, float ymin, float xmax, float ymax, IntPtr user_data)
        {
            //Debug.Log($"Push clip rect {xmin} {ymin} {xmax} {ymax}");
            var clipRect = new BBox(xmin, ymin, xmax, ymax);

            if (data.clipRect != BBox.Empty)
            {
                //Debug.Log($"clipRect was already set to {data.clipRect}, new clipRect {clipRect}");
                data.clipRect = clipRect;
            }

            var arraySize = clipRect.intWidth * clipRect.intHeight;
            if (!data.paintSurface.IsCreated || data.paintSurface.Length != arraySize)
                data.paintSurface = new NativeArray<ColorARGB>(arraySize, Allocator.Temp);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(PopDelegate))]
        public static void HB_paint_pop_clip_func_t (IntPtr harfBuzzPaintFunct, ref PaintData data, IntPtr user_data)
        {
            //Debug.Log($"Pop clip glyph");
            data.clipGlyph.Clear();
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(ColorDelegate))]
        public static void HB_paint_color_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, bool is_foreground, uint color, IntPtr user_data)
        {
            //Debug.Log($"Paint solid color ARGB {(ColorARGB)color}");
            var colorARGB = (ColorARGB)color;
            var solidColor = new SolidColor(colorARGB);

            //AntiAliasedRasterizer.Rasterize(ref data.clipGlyph, data.paintSurface, solidColor, data.clipRect);
            AntiAliasedRasterizer.RasterizeAndBlend(ref data.clipGlyph, data.paintSurface, solidColor, PaintCompositeMode.SRC_OVER, data.clipRect);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(LinearOrRadialGradientDelegate))]
        public static void HB_paint_linear_gradient_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, ColorLine colorLine, float x0, float y0, float x1, float y1, float x2, float y2, IntPtr user_data)
        {
            //Debug.Log($"Paint linear gradient");
            var lineGradient = new LineGradient(x0, y0, x1, y1, x2, y2, colorLine.GetExtend(), data.transformStack.Peek());
            if (!lineGradient.isValid)
                return;

            lineGradient.InitializeColorLine(colorLine);

            //AntiAliasedRasterizer.Rasterize(ref data.clipGlyph, data.paintSurface, lineGradient, data.clipRect);
            AntiAliasedRasterizer.RasterizeAndBlend(ref data.clipGlyph, data.paintSurface, lineGradient, PaintCompositeMode.SRC_OVER,data.clipRect);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(LinearOrRadialGradientDelegate))]
        public static void HB_paint_radial_gradient_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, ColorLine colorLine, float x0, float y0, float r0, float x1, float y1, float r1, IntPtr user_data)
        {
            //Debug.Log($"Paint radial gradient");
            var radialGradient = new RadialGradient(x0, y0, r0, x1, y1, r1, colorLine.GetExtend(), data.transformStack.Peek());
            if (!radialGradient.isValid)
                return;

            radialGradient.InitializeColorLine(colorLine);

            //AntiAliasedRasterizer.Rasterize(ref data.clipGlyph, data.paintSurface, radialGradient, data.clipRect);
            AntiAliasedRasterizer.RasterizeAndBlend(ref data.clipGlyph, data.paintSurface, radialGradient, PaintCompositeMode.SRC_OVER, data.clipRect);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(SweepGradientDelegate))]
        public static void HB_paint_sweep_gradient_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, ColorLine colorLine, float x0, float y0, float startAngle, float endAngle, IntPtr user_data)
        {
            //Debug.Log($"Paint sweep gradient");
            var sweepGradient = new SweepGradient(x0, y0, startAngle, endAngle, colorLine.GetExtend(), data.transformStack.Peek());
            sweepGradient.InitializeColorLine(colorLine);
            if (!sweepGradient.isValid)
                return;

            //AntiAliasedRasterizer.Rasterize(ref data.clipGlyph, data.paintSurface, sweepGradient, data.clipRect);
            AntiAliasedRasterizer.RasterizeAndBlend(ref data.clipGlyph, data.paintSurface, sweepGradient, PaintCompositeMode.SRC_OVER, data.clipRect);
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(PopDelegate))]
        public static void HB_paint_push_group_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, IntPtr user_data)
        {
            //Debug.Log($"Push group ({data.group})");
            //according to https://github.com/harfbuzz/harfbuzz/issues/3931, there should only be two intermediate surfaces requiered
            //to build COMPOSITE glyphs (foreground, background)...but emperically we find sometimes need for three (e.g. in 😱). Not clear why
            //
            // COMPOSITE: 
            // push_group()
            // // recurse for backdrop
            // push_group()
            // // recurse for source
            // pop_group_and_composite(composite_mode)
            // pop_group_and_composite(OVER)

            // layers:
            //foreach layer
            //    push_group()
            //    // recurse for layer paint
            //    pop_group_and_composite(OVER)

            data.group++;

            if (data.group == 1)
            {
                if(!data.tempSurface1.IsCreated)
                    data.tempSurface1 = new NativeArray<ColorARGB>(data.paintSurface.Length, Allocator.Temp);
                (data.paintSurface, data.tempSurface1) = (data.tempSurface1, data.paintSurface);
            }
            else if (data.group == 2)
            {
                if (!data.tempSurface2.IsCreated)
                    data.tempSurface2 = new NativeArray<ColorARGB>(data.paintSurface.Length, Allocator.Temp);
                (data.paintSurface, data.tempSurface2) = (data.tempSurface2, data.paintSurface);
            }
            else if(data.group == 3)
            {
                if (!data.tempSurface3.IsCreated)
                    data.tempSurface3 = new NativeArray<ColorARGB>(data.paintSurface.Length, Allocator.Temp);
                (data.paintSurface, data.tempSurface3) = (data.tempSurface3, data.paintSurface);
            }
            else
                Debug.Log($"No more intermediate surfaces available");
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(PopGroupDelegate))]
        public static void HB_paint_pop_group_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, PaintCompositeMode mode, IntPtr user_data)
        {
            //Debug.Log($"Pop group ({data.group}, blend mode {mode})");
            NativeArray<ColorARGB> result, source, destination = default;

            result = data.paintSurface;
            source = data.paintSurface;

            if (data.group == 1)
                destination = data.tempSurface1;
            else if (data.group == 2)
                destination = data.tempSurface2;
            else if (data.group == 3)
                destination = data.tempSurface3;
            else
                Debug.Log($"Unknown destination surface ");

            PaintUtils.blendMarker.Begin();
            switch (mode)
            {
                case PaintCompositeMode.SRC:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = source[i];
                    break;
                case PaintCompositeMode.DEST:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = destination[i];
                    break;
                case PaintCompositeMode.SRC_OVER:
                    {
                        // used for pretty much all blend operations, so make it fast
                        // main loop: proccess 4 pixel in each blend operation
                        int i, ii = result.Length / 4 * 4;
                        for (i = 0;  i < ii; i += 4)
                        {
                            var s = new uint4(source[i].argb, source[i + 1].argb, source[i + 2].argb, source[i + 3].argb);
                            var d = new uint4(destination[i].argb, destination[i + 1].argb, destination[i + 2].argb, destination[i + 3].argb);
                            var r = Blending.SrcOver(s, d);
                            result[i] = r[0];
                            result[i + 1] = r[1];
                            result[i + 2] = r[2];
                            result[i + 3] = r[3];
                        }
                        // remainder loop
                        for ( ; i < result.Length; i++)
                            result[i] = Blending.SrcOver(source[i], destination[i]);
                    }
                    break;
                case PaintCompositeMode.DEST_OVER:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.DstOver(source[i], destination[i]);
                    break;
                case PaintCompositeMode.SRC_IN:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.SrcIn(source[i], destination[i]);
                    break;
                case PaintCompositeMode.DEST_IN:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.DstIn(source[i], destination[i]);
                    break;
                case PaintCompositeMode.SRC_OUT:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.SrcOut(source[i], destination[i]);
                    break;
                case PaintCompositeMode.DEST_OUT:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.DstOut(source[i], destination[i]);
                    break;
                case PaintCompositeMode.SRC_ATOP:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.SrcAtop(source[i], destination[i]);
                    break;
                case PaintCompositeMode.DEST_ATOP:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.DstAtop(source[i], destination[i]);
                    break;
                case PaintCompositeMode.XOR:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.Xor(source[i], destination[i]);
                    break;
                case PaintCompositeMode.PLUS:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.Plus(source[i], destination[i]);
                    break;
                case PaintCompositeMode.SCREEN:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.Screen(source[i], destination[i]);
                    break;
                case PaintCompositeMode.MULTIPLY:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.Multiply(source[i], destination[i]);
                    break;
                case PaintCompositeMode.COLOR_DODGE:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.ColorDodge(source[i], destination[i]);
                    break;
                case PaintCompositeMode.COLOR_BURN:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.ColorBurn(source[i], destination[i]);
                    break;
                default:
                    for (int i = 0; i < result.Length; i++)
                        result[i] = Blending.SrcOver(source[i], destination[i]);
                    break;
            }
            PaintUtils.blendMarker.End();
            if (data.group == 1)
                Blending.Clear(data.tempSurface1);
            else if (data.group == 2)
                Blending.Clear(data.tempSurface2);
            else if (data.group == 3)
                Blending.Clear(data.tempSurface3);

            data.group--;            
        }
        [BurstCompile]
        [MonoPInvokeCallback(typeof(CustomPalette_colorDelegate))]
        public static bool hb_paint_custom_palette_color_func_t (IntPtr harfBuzzPaintFunct, ref PaintData data, uint color_index, uint color, IntPtr user_data)
        {
            //Debug.Log($"hb_paint_custom_palette_color color_index {color_index} color {color}");
            return true;
        }

        /// <summary>
        /// This callback converts the image data found for a given glyph either to a NativeArray of colors that can be directly applied to a texture 
        /// (in case of raw BRGA data stored in Apple sbix or Google CDBT), or to the raw PNG and SVG bytes. PNG can SVG can currently not be converted 
        /// to a NativeArray of colors in a BURST compatible way.
        /// </summary>
        [BurstCompile]
        [MonoPInvokeCallback(typeof(ImageDelegate))]
        public static bool hb_paint_image_func_t(IntPtr harfBuzzPaintFunct, ref PaintData data, Blob image, uint width, uint height, PaintImageFormat format, float slant, ref GlyphExtents extents, IntPtr user_data)
        {
            //Debug.Log("hb_paint_image");
            data.imageFormat = format;            
            data.imageWidth = (int)width;
            data.imageHeight = (int)height;
            var rawBytes= image.GetData();
            if (format == PaintImageFormat.BGRA)
            {
                var rawBytesLength = rawBytes.Length;
                var textureData = new NativeArray<ColorARGB>(rawBytesLength / 4, Allocator.Temp);
                int count = 0;
                for (int i = 0, ii = rawBytes.Length; i < ii; i += 4)
                    textureData[count++] = new ColorARGB(rawBytes[i+3], rawBytes[i+2], rawBytes[i+1], rawBytes[i]);
                data.paintSurface = textureData;
            }
            else // HB_PAINT_IMAGE_FORMAT.PNG, HB_PAINT_IMAGE_FORMAT.SVG To-Do: find BURST compatible decoder
                data.imageData = rawBytes;

            Debug.Log($"width {width} height {height}  format {format} {data.imageData.Length}");
            return true;
        }

        public delegate void PushTransformDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, float xx, float yx, float xy, float yy, float dx, float dy, IntPtr user_data);

        public delegate void PopDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, IntPtr user_data);

        [return: MarshalAs(UnmanagedType.I1)]
        public delegate bool ColorGlyphDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, uint glyph, IntPtr font, IntPtr user_data);

        public delegate void PushClipGlyphDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, uint glyph, IntPtr font, IntPtr user_data);

        public delegate void PushClipRectangleDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, float xmin, float ymin, float xmax, float ymax, IntPtr user_data);

        public delegate void ColorDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, bool is_foreground, uint color, IntPtr user_data);

        [return: MarshalAs(UnmanagedType.I1)]
        public delegate bool ImageDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, Blob image, uint width, uint height, PaintImageFormat format, float slant, ref GlyphExtents extents, IntPtr user_data);

        public delegate void LinearOrRadialGradientDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, ColorLine color_line, float x0, float y0, float x1, float y1, float x2, float y2, IntPtr user_data);

        public delegate void SweepGradientDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, ColorLine color_line, float x0, float y0, float start_angle, float end_angle, IntPtr user_data);

        public delegate void PopGroupDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, PaintCompositeMode mode, IntPtr user_data);

        [return: MarshalAs(UnmanagedType.I1)]
        public delegate bool CustomPalette_colorDelegate(IntPtr harfBuzzPaintFunct, ref PaintData data, uint color_index, uint color, IntPtr user_data);
    }
}
