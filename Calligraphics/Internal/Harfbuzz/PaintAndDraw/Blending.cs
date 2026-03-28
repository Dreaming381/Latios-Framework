using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz
{
    internal static class Blending
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorBGRA Blend(ColorBGRA source, ColorBGRA destination, PaintCompositeMode mode)
        {
            switch (mode)
            {
                case PaintCompositeMode.SRC:
                    return source;
                case PaintCompositeMode.DEST:
                    return destination;
                case PaintCompositeMode.SRC_OVER:
                    return SrcOver(source, destination);
                case PaintCompositeMode.DEST_OVER:
                    return DstOver(source, destination);
                case PaintCompositeMode.SRC_IN:
                    return SrcIn(source, destination);
                case PaintCompositeMode.DEST_IN:
                    return DstIn(source, destination);
                case PaintCompositeMode.SRC_OUT:
                    return SrcOut(source, destination);
                case PaintCompositeMode.DEST_OUT:
                    return DstOut(source, destination);
                case PaintCompositeMode.SRC_ATOP:
                    return SrcAtop(source, destination);
                case PaintCompositeMode.DEST_ATOP:
                    return DstAtop(source, destination);
                case PaintCompositeMode.XOR:
                    return Xor(source, destination);
                case PaintCompositeMode.PLUS:
                    return Plus(source, destination);
                case PaintCompositeMode.SCREEN:
                    return Screen(source, destination);
                case PaintCompositeMode.MULTIPLY:
                    return Multiply(source, destination);
                case PaintCompositeMode.COLOR_DODGE:
                    return ColorDodge(source, destination);
                case PaintCompositeMode.COLOR_BURN:
                    return ColorBurn(source, destination);
                default:
                    return SrcOver(source, destination);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorBGRA SrcOver(ColorBGRA s, ColorBGRA d)
        {
            // r = s*sa + (1-sa)*d*da
            var oneMinusAlpha = 255 - s.a;
            var ra = s.a + d.a * oneMinusAlpha / 255;
            var rr = (s.r * s.a / 255) + oneMinusAlpha * (d.r * d.a / 255) / 255;
            var rg = (s.g * s.a / 255) + oneMinusAlpha * (d.g * d.a / 255) / 255;
            var rb = (s.b * s.a / 255) + oneMinusAlpha * (d.b * d.a / 255) / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 SrcOver(uint4 s, uint4 d)
        {
            //extract individual color components into uint4 vectors for subsequent SIMD math
            //expects ColorBGRA byte layout
            var sa = s & 0xff;
            var sr = (s >> 8) & 0xff;
            var sg = (s >> 16) & 0xff;
            var sb = (s >> 24) & 0xff;

            var da = d & 0xff;
            var dr = (d >> 8) & 0xff;
            var dg = (d >> 16) & 0xff;
            var db = (d >> 24) & 0xff;

            //r = s * sa + (1 - sa) * d * da
            var oneMinusAlpha = 255 - sa;
            var ra = sa + da * oneMinusAlpha / 255;
            var rr = (sr * sa / 255) + oneMinusAlpha * (dr * da / 255) / 255;
            var rg = (sg * sa / 255) + oneMinusAlpha * (dg * da / 255) / 255;
            var rb = (sb * sa / 255) + oneMinusAlpha * (db * da / 255) / 255;
            return ra & 0xFF | (rr & 0xFF) << 8 | (rg & 0xFF) << 16 | (rb & 0xFF) << 24;
        }
        public static ColorBGRA DstOver(ColorBGRA s, ColorBGRA d)
        {
            // r = d*da + (1-da)*s*sa
            var oneMinusAlpha = 255 - d.a;
            var ra = d.a + s.a * oneMinusAlpha / 255;
            var rr = (d.r * d.a / 255) + oneMinusAlpha * (s.r * s.a / 255) / 255;
            var rg = (d.g * d.a / 255) + oneMinusAlpha * (s.g * s.a / 255) / 255;
            var rb = (d.b * d.a / 255) + oneMinusAlpha * (s.b * s.a / 255) / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 DstOver(uint4 s, uint4 d)
        {
            //expects ColorBGRA byte layout
            var sa = s & 0xff;
            var sr = (s >> 8) & 0xff;
            var sg = (s >> 16) & 0xff;
            var sb = (s >> 24) & 0xff;

            var da = d & 0xff;
            var dr = (d >> 8) & 0xff;
            var dg = (d >> 16) & 0xff;
            var db = (d >> 24) & 0xff;

            //r = s * sa + (1 - sa) * d * da
            var oneMinusAlpha = 255 - da;
            var ra = da + sa * oneMinusAlpha / 255;
            var rr = (dr * da / 255) + oneMinusAlpha * (sr * sa / 255) / 255;
            var rg = (dg * da / 255) + oneMinusAlpha * (sg * sa / 255) / 255;
            var rb = (db * da / 255) + oneMinusAlpha * (sb * sa / 255) / 255;
            return ra & 0xFF | (rr & 0xFF) << 8 | (rg & 0xFF) << 16 | (rb & 0xFF) << 24;
        }
        public static ColorBGRA SrcIn(ColorBGRA s, ColorBGRA d)
        {
            // a = sa * da
            // r = s * sa * da
            var ra = s.a * d.a / 255;
            var rr = s.r * s.a / 255 * d.a / 255;
            var rg = s.g * s.a / 255 * d.a / 255;
            var rb = s.b * s.a / 255 * d.a / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA DstIn(ColorBGRA s, ColorBGRA d)
        {
            // a = sa * da
            // r = s *sa * da
            var ra = d.a * s.a / 255;
            var rr = d.r * d.a / 255 * s.a / 255;
            var rg = d.g * d.a / 255 * s.a / 255;
            var rb = d.b * d.a / 255 * s.a / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA SrcOut(ColorBGRA s, ColorBGRA d)
        {
            // r = s * (1 - da)
            var ra = s.a * (255 - d.a) / 255;
            var rr = s.r * (255 - d.a) / 255;
            var rg = s.g * (255 - d.a) / 255;
            var rb = s.b * (255 - d.a) / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA DstOut(ColorBGRA s, ColorBGRA d)
        {
            // r = d * (1 - sa)
            var ra = d.a * (255 - s.a) / 255;
            var rr = d.r * (255 - s.a) / 255;
            var rg = d.g * (255 - s.a) / 255;
            var rb = d.b * (255 - s.a) / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        
        public static ColorBGRA SrcAtop(ColorBGRA s, ColorBGRA d)
        {
            // r = s*da + d*(1-sa)
            var ra = s.a * d.a / 255 + d.a * (255 - s.a) / 255;
            var rr = s.r * d.a / 255 + d.r * (255 - s.a) / 255;
            var rg = s.g * d.a / 255 + d.g * (255 - s.a) / 255;
            var rb = s.b * d.a / 255 + d.b * (255 - s.a) / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA DstAtop(ColorBGRA s, ColorBGRA d)
        {
            // r = d*sa + s*(1-da)
            var ra = d.a * s.a / 255 + s.a * (255 - d.a) / 255;
            var rr = d.r * s.a / 255 + s.r * (255 - d.a) / 255;
            var rg = d.g * s.a / 255 + s.g * (255 - d.a) / 255;
            var rb = d.b * s.a / 255 + s.b * (255 - d.a) / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA Xor(ColorBGRA s, ColorBGRA d)
        {
            // r = s*(1-da) + d*(1-sa)
            var ra = s.a * (255 - d.a) / 255 + d.a * (255 - s.a) / 255;
            var rr = s.r * (255 - d.a) / 255 + d.r * (255 - s.a) / 255;
            var rg = s.g * (255 - d.a) / 255 + d.g * (255 - s.a) / 255;
            var rb = s.b * (255 - d.a) / 255 + d.b * (255 - s.a) / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA Plus(ColorBGRA s, ColorBGRA d)
        {
            // r = min(s + d, 1)
            var ra = math.min(s.a + d.a, 255);
            var rr = math.min(s.r + d.r, 255);
            var rg = math.min(s.g + d.g, 255);
            var rb = math.min(s.b + d.b, 255);
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA Screen(ColorBGRA s, ColorBGRA d)
        {
            // r = s + d - s*d
            var ra = s.a + d.a - s.a * d.a / 255;
            var rr = s.r + d.r - s.r * d.r / 255;
            var rg = s.g + d.g - s.g * d.g / 255;
            var rb = s.b + d.b - s.b * d.b / 255;
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA Multiply(ColorBGRA s, ColorBGRA d)
        {
            // r = s*(1-da) + d*(1-sa) + s*d
            var ra = (s.a * (255 - d.a) / 255 + d.a * (255 - s.a) / 255 + (s.a * d.a / 255));
            var rr = (s.r * (255 - d.a) / 255 + d.r * (255 - s.a) / 255 + (s.r * d.r / 255));
            var rg = (s.g * (255 - d.a) / 255 + d.g * (255 - s.a) / 255 + (s.g * d.g / 255));
            var rb = (s.b * (255 - d.a) / 255 + d.b * (255 - s.a) / 255 + (s.b * d.b / 255));             
            return new ColorBGRA(rb, rg, rr, ra);
        }

        public static ColorBGRA ColorDodge(ColorBGRA s, ColorBGRA d)
        {
            // r = min(d / (1 - s), 1)
            var ra = s.a == 255 ? 255 : math.min(d.a * 255 / (255 - s.a), 255);
            var rr = s.r == 255 ? 255 : math.min(d.r * 255 / (255 - s.r), 255);
            var rg = s.g == 255 ? 255 : math.min(d.g * 255 / (255 - s.g), 255);
            var rb = s.b == 255 ? 255 : math.min(d.b * 255 / (255 - s.b), 255);
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static ColorBGRA ColorBurn(ColorBGRA s, ColorBGRA d)
        {
            // r = 1 - min((1 - d) / s, 1)
            var ra = s.a == 0 ? 0 : 255 - math.min((255 - d.a) * 255 / s.a, 255);
            var rr = s.r == 0 ? 0 : 255 - math.min((255 - d.r) * 255 / s.r, 255);
            var rg = s.g == 0 ? 0 : 255 - math.min((255 - d.g) * 255 / s.g, 255);
            var rb = s.b == 0 ? 0 : 255 - math.min((255 - d.b) * 255 / s.b, 255);
            return new ColorBGRA(rb, rg, rr, ra);
        }

        public static ColorBGRA Overlay(ColorBGRA s, ColorBGRA d)
        {
            // multiply or screen, depending on d
            //multiply: // r = s*(1-da) + d*(1-sa) + s*d
            //screen: // r = s + d - s*d
            var ra = math.select(255 - 2 * (255 - s.a) * (255 - d.a) / 255, 2 * s.a * d.a / 255, s.a < 127);
            var rr = math.select(255 - 2 * (255 - s.r) * (255 - d.r) / 255, 2 * s.r * d.r / 255, s.r < 127);
            var rg = math.select(255 - 2 * (255 - s.g) * (255 - d.g) / 255, 2 * s.g * d.g / 255, s.g < 127);
            var rb = math.select(255 - 2 * (255 - s.b) * (255 - d.b) / 255, 2 * s.b * d.b / 255, s.b < 127);
            return new ColorBGRA(rb, rg, rr, ra);
        }
        public static void SetWhite(NativeArray<ColorBGRA> result)
        {
            var color = new ColorBGRA(255, 255, 255, 255);
            for (int i = 0; i < result.Length; i++)
                result[i] = color;
        }
        public static void SetBlack(NativeArray<ColorBGRA> result)
        {
            var color = new ColorBGRA(0, 0, 0, 255);
            for (int i = 0; i < result.Length; i++)
                result[i] = color;
        }
        public static void SetTransparent(NativeArray<ColorBGRA> result)
        {
            var color = new ColorBGRA(0, 0, 0, 0);
            for (int i = 0; i < result.Length; i++)
                result[i] = color;
        }
        public static void Clear(NativeArray<ColorBGRA> result)
        {
            for (int i = 0; i < result.Length; i++)
                result[i] = 0;
        }
    }    
}
