using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.TextCore;

namespace Latios.Calligraphics.HarfBuzz.Bitmap
{
    internal static class SdfRasterizer
    {
        public static void RasterizeSdf8(DrawData drawData, NativeArray<byte> buffer, in GlyphRect atlasRect, int padding, int spread)
        {
            var signs   = RasterizeSigns(drawData, in atlasRect, padding);
            var distSqs = RasterizeSquaredDistances(drawData, in atlasRect, padding, spread);

            for (int i = 0; i < signs.Length; i++)
            {
                var sign           = signs[i];
                var distSq         = distSqs[i];
                var signedDistance = sign * math.min(math.sqrt(distSq) / spread, 1f);  // in [-1, 1] range
                var scaled         = (signedDistance + 1f) * 255f / 2f;  // unorm correction
                buffer[i]          = (byte)scaled;
            }
        }

        public static void RasterizeSdf16(DrawData drawData, NativeArray<ushort> buffer, in GlyphRect atlasRect, int padding, int spread)
        {
            var signs   = RasterizeSigns(drawData, in atlasRect, padding);
            var distSqs = RasterizeSquaredDistances(drawData, in atlasRect, padding, spread);

            for (int i = 0; i < signs.Length; i++)
            {
                var sign           = signs[i];
                var distSq         = distSqs[i];
                var signedDistance = sign * math.min(math.sqrt(distSq) / spread, 1f);  // in [-1, 1] range
                var scaled         = (signedDistance + 1f) * 65535f / 2f;  // unorm correction
                buffer[i]          = (byte)scaled;
            }
        }

        // Fills the array with -1f for outside the glyph, and 1f for inside the glyph
        static NativeArray<float> RasterizeSigns(DrawData drawData, in GlyphRect atlasRect, int padding)
        {
            var result             = new NativeArray<float>(atlasRect.width * atlasRect.height, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var edgeCount          = drawData.edges.Length;
            var scanLineEdges      = new NativeArray<float>(atlasRect.height * edgeCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var scanLineEdgeCounts = new NativeArray<int>(atlasRect.height, Allocator.Temp, NativeArrayOptions.ClearMemory);

            float2 offset = drawData.glyphRect.min - padding;
            foreach (var edge in drawData.edges)
            {
                var a = edge.start_pos - offset;
                var b = edge.end_pos - offset;

                // Skip horizontal edges
                if (a.y == b.y)
                    continue;

                // If an endpoint lands directly on the scanline's sample line, nudge it up so it doesn't.
                if (math.frac(a.y) == 0.5f)
                    a.y += 0.0001f;
                if (math.frac(b.y) == 0.5f)
                    b.y += 0.0001f;

                // Find the range of scanlines to process. Rounding causes the endpoints to be below the scanline by half a pixel.
                // The min will round to the index of the first scanline, while the max will round to one past the last scanline.
                var roundedScanlineStart = (int)math.round(math.min(a.y, b.y));
                var roundedScanlineEnd   = (int)math.round(math.max(a.y, b.y));
                if (roundedScanlineStart == roundedScanlineEnd)
                    continue;

                // Find the change in x for a given y distance from a (dx/dy)
                float xRate = (b.x - a.x) / (b.y - a.y);

                // Add scanline intersections
                for (int i = roundedScanlineStart; i < roundedScanlineEnd; i++)
                {
                    var yDelta                      = (i + 0.5f) - a.y;
                    var xIntersect                  = a.x + xRate * yDelta;
                    var scanline                    = scanLineEdges.GetSubArray(i * edgeCount, edgeCount);
                    scanline[scanLineEdgeCounts[i]] = xIntersect;
                    scanLineEdgeCounts[i]++;
                }
            }

            // We assume that the polygon is clean, and that because of padding, the scanline always starts outside.
            for (int scanlineIndex = 0; scanlineIndex < atlasRect.height; scanlineIndex++)
            {
                var scanlineResult        = result.GetSubArray(scanlineIndex * atlasRect.width, atlasRect.width);
                var edgeIntersectionCount = scanLineEdgeCounts[scanlineIndex];
                if (edgeIntersectionCount == 0)
                {
                    scanlineResult.AsSpan().Fill(-1f);
                    continue;
                }

                var intersectingEdges = scanLineEdges.GetSubArray(scanlineIndex * edgeCount, edgeIntersectionCount);
                intersectingEdges.Sort();
                float sign            = -1f;
                int   edgeTargetIndex = 0;
                float edgeTarget      = intersectingEdges[0];
                for (int i = 0; i < scanlineResult.Length; i++)
                {
                    while (i + 0.5f > edgeTarget)
                    {
                        sign = -sign;
                        edgeTargetIndex++;
                        if (edgeTargetIndex >= intersectingEdges.Length)
                            edgeTarget = scanlineResult.Length;
                        else
                            edgeTarget = intersectingEdges[edgeTargetIndex];
                    }
                    scanlineResult[i] = sign;
                }
            }

            return result;
        }

        static NativeArray<float> RasterizeSquaredDistances(DrawData drawData, in GlyphRect atlasRect, int padding, int spread)
        {
            var result = new NativeArray<float>(atlasRect.width * atlasRect.height, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            result.AsSpan().Fill(float.MaxValue);
            float2 offset = drawData.glyphRect.min - padding;
            foreach (var edge in drawData.edges)
            {
                var a    = edge.start_pos - offset;
                var b    = edge.end_pos - offset;
                var cbox = BezierMath.GetLineBBox(a, b);
                cbox.Expand(spread);

                int xStart = math.max((int)cbox.min.x, 0);
                int xEnd   = math.min((int)cbox.max.x, atlasRect.width);
                int yStart = math.max((int)cbox.min.y, 0);
                int yEnd   = math.min((int)cbox.max.y, atlasRect.height);

                float ax                = a.x;
                float ay                = a.y;
                float abx               = b.x - ax;
                float aby               = b.y - ay;
                float abLengthSq        = abx * abx + aby * aby;
                float abxOverAbLengthSq = abx / abLengthSq;
                float abyOverAbLengthSq = aby / abLengthSq;

                for (int y = yStart; y < yEnd; y++)
                {
                    var   scanline = result.GetSubArray(y * atlasRect.width, atlasRect.width);
                    float sampleY  = y + 0.5f;  // use the center of any pixel to be rendered within cbox
                    float apy      = sampleY - ay;

                    for (int x = xStart; x < xEnd; x++)
                    {
                        float sampleX   = x + 0.5f;
                        float apx       = sampleX - ax;
                        float dot       = abx * apx + aby * apy;
                        dot             = math.clamp(dot, 0f, abLengthSq);
                        float hitX      = ax + dot * abxOverAbLengthSq;
                        float hitY      = ay + dot * abyOverAbLengthSq;
                        float hpx       = hitX - sampleX;
                        float hpy       = hitY - sampleY;
                        float newDistSq = hpx * hpx + hpy * hpy;
                        float oldDistSq = scanline[x];
                        scanline[x]     = math.min(newDistSq, oldDistSq);
                    }
                }
            }
            return result;
        }
    }
}

