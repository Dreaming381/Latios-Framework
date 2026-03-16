using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Calligraphics.HarfBuzz
{
    internal static class PaintUtils
    {
        //175: 9811 9812 9813 9814 9815 9816 9819 9820 9799 9801 9802 9803
        //🐢: 8534, 8535, 8536, 8537, 8538, 8539,
        //😉: 13293, 13288, 13317, 13318, 13287, 13319, 10662
        //🥰: 14483, 15667, 16927, 16928, 16929, 16930, 16931, 16932, 16933, 16934, 16935, 16936, 16937, 16938, 16939, 16940, 16941, 
        //😰: 13293, 13381, 13437, 13438, 13439, 13440, 13443, 13488, 13508, 13509, 
        //13: 6767, 6768, 6769, 6770, 6771, 6772, 6773, 6774, 6775, 6776, 6777, 6778, 6779, 6780, 6781,  6782, 6783, 6784, 6785, 6786, 6787, 6788, 6789, 6790, 6791, 6792,
        //😱: (push+pop group error)
        //🌁: 3941,  4009, 4010, 4011, 4012, 4013, 4014, 4015, 4016, 4017, 4018, 4019, 4020, 4021, 4022, 4023, 4024, 4025, 4026, 

        //public static readonly int filterGlyph = 6767;//13317;
        //public static FixedList4096Bytes<int> filterGlyphs = new()
        //{
        //    4025,
        //};
        //public static bool DrawGlyph(int glyphID)
        //{
        //    return true;
        //    if(!filterGlyphs.Contains(glyphID))
        //        return false;
        //    else 
        //        return true;
        //}
        public static readonly ProfilerMarker rasterizeCOLRMarker = new ProfilerMarker("Rasterize COLR");
        public static readonly ProfilerMarker rasterizeSDFMarker = new ProfilerMarker("Rasterize SDF");
        public static readonly ProfilerMarker removeOverlapsMarker = new ProfilerMarker("Remove Overlaps");
        public static readonly ProfilerMarker blendMarker = new ProfilerMarker("Blend");

        public readonly static float2x3 AffineTransformIdentity = new float2x3 {
                c0 = new float2(1, 0),  // xx, yx
                c1 = new float2(0, 1),  // xy, yy
                c2 = new float2(0, 0)}; // x0, y0

        public static bool GetGradientDirection(float x0, float y0, float x1, float y1, float x2, float y2, out float2 p3)
        {
            p3 = default;
            if (Hint.Unlikely((x0 == x1 && y0 == y1) || (x0 == x2 && y0 == y2)))
                return false; //points idential, gradient ill formed, draw nothing https://learn.microsoft.com/en-us/typography/opentype/spec/colr

            var x02 = x2 - x0;
            var y02 = y2 - y0;
            var x01 = x1 - x0;
            var y01 = y1 - y0;

            double det = cross(x01, y01, x02, y02);
            if (Hint.Unlikely(math.abs(det) < Epsilon))
                return false; //lines are parallel, gradient ill formed, draw nothing https://learn.microsoft.com/en-us/typography/opentype/spec/colr


            var squaredNorm02 = x02 * x02 + y02 * y02;
            if (squaredNorm02 < Epsilon)
            {
                p3 = new float2(x1, y1);
                return true;
            }
            var k = (x01 * x02 + y01 * y02) / squaredNorm02;
            var x = x1 - k * x02;
            var y = y1 - k * y02;
            p3 = new float2(x, y);
            return true;
        }

       
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float WrapAroundLimit(float val, float lim)
        {
            return math.clamp(val - math.floor(val / lim) * lim, 0f, lim);
        }
        public static void TransformGlyph(ref DrawData drawData, float2x3 transform)
        {
            //Debug.Log($"apply transform {transform.c0.x} {transform.c0.y} {transform.c1.x} {transform.c1.y} {transform.c2.x} {transform.c2.y}");
            var newGlyphRect = BBox.Empty;
            var edges = drawData.edges;
            for (int k = 0, kk = edges.Length; k < kk; k++)
            {
                ref var edge = ref edges.ElementAt(k);
                edge.start_pos = mul(transform, edge.start_pos);
                edge.end_pos = mul(transform, edge.end_pos);
                edge.control1 = mul(transform, edge.control1);
                edge.control2 = mul(transform, edge.control2);

                var edgeBBox = BezierMath.GetLineBBox(edge.start_pos, edge.end_pos);
                newGlyphRect = BBox.Union(newGlyphRect, edgeBBox);
            }
            var before = drawData.glyphRect;
            drawData.glyphRect=newGlyphRect;
            //SDFCommon.WriteGlyphOutlineToFile("ClipGlyph-Transformed.txt", ref drawData, false);
        }

        public static void BlitRawTexture(NativeArray<ColorARGB> src, int srcWidth, int srcHeight,  NativeArray<ColorARGB> dest, int dstWidth, int dstHeight, int destX, int destY)
        {
            for (int y = 0; y < srcHeight; y++)
                NativeArray<ColorARGB>.Copy(src, y * srcWidth, dest, (destY + y) * dstWidth + destX, srcWidth);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int QuadraticRoots(float a, float b, float c, out float2 roots, out bool tangent)
        {
            tangent = false;
            roots = default;
            if (math.abs(a) < Epsilon)
            {
                if (math.abs(c) < Epsilon)
                    return 0;                
                roots[0] = -c / b;
                return 1;
            }
            var discriminant = b * b - 4 * a * c;
            if(discriminant ==0)
                tangent = true;
            if (math.abs(discriminant) < Epsilon)
            {
               
                roots[0] = -b / (2 * a);
                return 1;
            }
            if (discriminant < 0)
                return 0;

            var DS = math.sqrt(discriminant);
            roots[0] = (-b - DS) / (2 * a);
            roots[1] = (-b + DS) / (2 * a);
            return 2;
        }        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2x3 mul(float2x3 a, float2x3 b)
        {
            return new float2x3(
                 a.c0.x * b.c0 + a.c0.y * b.c1,
                 a.c1.x * b.c0 + a.c1.y * b.c1,
                 a.c2.x * b.c0 + a.c2.y * b.c1 + b.c2);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 mul(float2x3 a, float2 b)
        {
            return a.c0 * b.x + a.c1 * b.y + a.c2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2x3 Translate(float x, float y)
        {
            return new float2x3(
                new float2(1, 0),
                new float2(0, 1),
                new float2(x, y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2x3 Scale(float width, float height)
        {
            return new float2x3(
                new float2(width, 0),
                new float2(0, height),
                new float2(0, 0));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2x3 Rotate(float angleRadians)
        {
            math.sincos(angleRadians, out float s, out float c);
            return new float2x3(
                new float2(c, s),
                new float2(-s, c),
                new float2(0, 0));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Inverse(float2x3 m, out float2x3 inverse)
        {
            inverse = default;
            var c0 = m.c0;
            var c1 = m.c1;
            var c2 = m.c2;
            var det = m.c0.x * m.c1.y - m.c0.y * m.c1.x;
            if (det == 0)
                return false;

            var ic0x = c1.y / det;
            var ic0y = -c0.y / det;
            var ic1x = -c1.x / det;
            var ic1y = c0.x / det;
            var ic2x = -ic0x * c2.x - ic0y * c2.y;
            var ic2y = -ic1x * c2.x - ic1y * c2.y;
            inverse = new float2x3(
                new float2(ic0x, ic0y),
                new float2(ic1x, ic1y),
                new float2(ic2x, ic2y));
            return true;
        }
        /// <summary>Finds the magnitude of the cross product of two vectors (if we pretend they're in three dimensions) </summary>
        /// <param name="a">First vector</param>
        /// <param name="b">Second vector</param>
        /// <returns>The magnitude of the cross product</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cross(float2 a, float2 b)
        {
            return (a.x * b.y) - (a.y * b.x);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double cross(float ax, float ay, float bx, float by)
        {
            return (ax * by) - (ay * bx);
        }
        public static readonly float Epsilon = 0.000001f;

        public static void ApplyWrapMode(ref float u, PaintExtend paintExtend)
        {
            switch (paintExtend)
            {
                case PaintExtend.REPEAT:
                    u = math.fmod(u, 1.0f);
                    u = u < 0 ? u + 1 : u;
                    break;
                case PaintExtend.PAD:
                    u = math.max(math.min(u, 1.0f), 0.0f);
                    break;
                case PaintExtend.REFLECT:
                    var w = math.fmod(u, 2.0f);
                    if (w > 0)
                    {
                        if (w > 1)
                            u = 1.0f - math.fmod(w, 1.0f);
                        else
                            u = w;
                    }
                    else
                    {
                        if (w < -1)
                            u = math.abs(-1.0f - math.fmod(w, 1.0f));
                        else
                            u = math.abs(w);
                    }
                    break;
            }
        }
        public static void ApplySweepWrapMode(ref float u, float minStop, float maxStop, PaintExtend paintExtend)
        {
            var range = maxStop - minStop;
            switch (paintExtend)
            {
                case PaintExtend.REPEAT:
                    u = math.fmod(u, range);
                    u = u < minStop ? u + range : u;
                    if (u > maxStop)
                        u = minStop + (u - maxStop);
                    if (u < minStop)
                        u = maxStop - (minStop - u);
                    break;
                case PaintExtend.PAD:
                    u = math.max(math.min(u, maxStop), minStop);
                    break;
                case PaintExtend.REFLECT:
                    u = math.fmod(u, 2 * range);
                    if (u < minStop)
                        u = minStop + (minStop - u);
                    if (u > maxStop)
                    {
                        u = maxStop - (u - maxStop);
                        if (u < minStop)
                            u = minStop + (minStop - u);
                    }
                    break;
            }
        }
        public static ColorARGB SampleGradient(NativeList<ColorStop> stops,  float u)
        {            
            if (stops.IsEmpty)
                return new ColorARGB(255, 255, 255, 255);

            int stop;
            var colorStopCount = stops.Length;
            for (stop = 0; stop < colorStopCount; stop++)
            {
                if (u < stops[stop].offset)
                    break;
            }
            if (stop >= colorStopCount)
            {
                //Debug.Log($"stops too long ( {stop} / {stops.Length - 1}), color {stops[colorStopLength - 1].color} ");
                return stops[colorStopCount - 1].color;
            }
            if (stop == 0)
            {
                //Debug.Log($"stop 0 ({stop} / {colorStopLength - 1}), color {stops[0].color} ");
                return stops[0].color;
            }

            float percentageRange = stops[stop].offset - stops[stop - 1].offset;
            if (percentageRange > Epsilon)
            {
                float blend = (u - stops[stop - 1].offset) / percentageRange;
                //Debug.Log($"blending between ({stop - 1}  and {stop}), color {ColorARGB.LerpUnclamped(stops[stop - 1].color, stops[stop].color, blend)} ");
                return ColorARGB.LerpUnclamped(stops[stop - 1].color, stops[stop].color, blend);
            }
            else
            {
                //Debug.Log($"last stop ({stop} / {colorStopLength - 1}), color {stops[stop - 1].color} ");
                return stops[stop - 1].color;
            }
        }        
    }
}
