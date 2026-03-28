using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz.Bitmap
{
    /*
        developed based on description in  https://nothings.org/gamedev/rasterize/, but instead of using a
        list of active edges, edges are rasterized as they are encountered in the polygon
     */
    internal static class AntiAliasedRasterizer
    {
        public static void Rasterize<T>(ref DrawData drawData, NativeArray<ColorBGRA> textureData, T pattern, BBox clipRect, bool invert = false) where T : IPattern
        {
            PaintUtils.rasterizeCOLRMarker.Begin();
            var edges      = drawData.edges;
            var contourIDs = drawData.contourIDs;
            var width      = clipRect.intWidth;
            var height     = clipRect.intHeight;

            //"areas" stores the partial signed pixel coverage (triangle, right trapezoids, rectangle+trapezoid) encountered when edges are cutting pixel
            //"areas_fill" stores the 100% coverage (signed!) of the 1st pixel to the right of an edge cutting the previous pixel
            //"areas fill" is used in a cumulative sum to determine the fill of a given line in the bitmap. The cumulative sum needs to be 100% accurate,
            //to ensure areas cancel eachother out (upwards egde = +1, downwards edge = -1). Floating point errors would lead to banding
            //For this reasons, the signed coverage stored in "areas" is not included in the cumulative sum, and only added as a last step
            //(if desired: ommit this last step to get an aliased bitmap)
            //Alternatively, we could also just use 1 array, and instead of storing triangle and trapezoid areas, store the delta areas of
            //current pixel (cut by edge) vs previous pixel. The cumulative sum of those delta areas should then result in the correct coverage for
            //each pixel, however this is prone to floating point errors, and accumulation of such errors results in banding
            var arraySize  = width * height;
            var areas      = new NativeArray<float>(arraySize, Allocator.Temp);
            var areas_fill = new NativeArray<float>(arraySize, Allocator.Temp);

            var offset = clipRect.min;
            for (int contourID = 0, end = contourIDs.Length - 1; contourID < end; contourID++)  //for each contour
            {
                int startID     = contourIDs[contourID];
                int nextStartID = contourIDs[contourID + 1];
                for (int edgeID = startID; edgeID < nextStartID; edgeID++)  //for each edge
                {
                    var  edge    = edges[edgeID];
                    var  p0      = edge.start_pos - offset;
                    var  p1      = edge.end_pos - offset;
                    bool inverse = p1.y < p0.y;
                    var  dir     = math.select(1.0f, -1.0f, inverse);
                    if (inverse)
                        (p0, p1) = (p1, p0);
                    var dxdy     = (p1.x - p0.x) / (p1.y - p0.y);
                    var x        = p0.x;
                    x            = math.select(x, x - p0.y * dxdy, x < 0.0f);
                    for (int y = math.max(0, (int)p0.y), yy = math.min(height, (int)math.ceil(p1.y)); y < yy; y++)
                    {
                        var linestart     = y * width;
                        var sy0           = math.max(y, p0.y);
                        var sy1           = math.min(y + 1, p1.y);
                        var dy            = sy1 - sy0;
                        var xnext         = x + dxdy * dy;
                        var d             = dy * dir;
                        var x0            = math.select(xnext, x, x < xnext);
                        var x1            = math.select(x, xnext, x < xnext);
                        var x0floor       = math.floor(x0);
                        var x0i           = (int)x0floor;
                        var x1ceil        = math.ceil(x1);
                        var x1i           = (int)x1ceil;
                        var linestart_x0i = linestart + x0i;
                        if (Hint.Unlikely(linestart_x0i < 0) || Hint.Unlikely(linestart_x0i > arraySize - 1))  // index is out of bounds
                        {
                            x = xnext;
                            continue;
                        }

                        if (x1i <= x0i + 1)
                        {
                            // simple case, edge only cuts one pixel (includes vertical case)
                            var bottomWidth       = (x0floor + 1.0f) - x1;
                            var topWidth          = (x0floor + 1.0f) - x0;
                            var trapezoidArea     = d * (bottomWidth + topWidth) * 0.5f;
                            areas[linestart_x0i] += trapezoidArea;
                            if (Hint.Likely(x1i < width)) //happens sometimes that last x exceeds glyph rect by <1 pixel due to xnext extrapolation
                                areas_fill[linestart_x0i + 1] += d; // everything right of this pixel is filled
                        }
                        else
                        {
                            // edge cuts several pixel
                            var topY    = math.select(sy1, sy0, inverse);
                            var bottomY = math.select(sy0, sy1, inverse);

                            //need math.abs(dydx) so that calculation of areas works correctly for all 4 possible orientation of an edge
                            //(NE to SW, SW to NE, NW to SE, SE to NW) the sign of the resulting area is ultimately determined by dir (and not by dydx!)
                            var dydx    = math.abs(1.0f / dxdy);
                            var x0width = x0floor + 1.0f - x0;

                            // calculate area of the triangle in the first pixel cut by the edge;
                            var firstYcrossing    = bottomY + dydx * dir * x0width;
                            var firstYHeight      = firstYcrossing - bottomY;
                            areas[linestart_x0i] += firstYHeight * x0width * 0.5f;  //area of triangle

                            // next, iteratively increase the firstYHeight and use it to determine the
                            // trapezoid area for all pixel crossed by edge
                            var step = dir * dydx;
                            for (int xi = linestart + x0i + 1, xii = linestart + x1i - 1; xi < xii; xi++)
                            {
                                areas[xi]    += firstYHeight + step * 0.5f;  // area of trapezoid is 1*step/2
                                firstYHeight += step;
                            }

                            // determine rectangle area + trapezoid area of last pixel cut by the edge
                            var lastYcrossing           = bottomY + dydx * dir * (x1ceil - 1.0f - x0);
                            var lastTriangleHeight      = topY - lastYcrossing;
                            var lastWidth               = x1ceil - x1;
                            var lastTrapezoidArea       = lastTriangleHeight * (lastWidth + 1.0f) * 0.5f;
                            areas[linestart + x1i - 1] += firstYHeight + lastTrapezoidArea;  //area of rectangle + area of trapezoid

                            // everything right of the last pixel is filled
                            if (Hint.Likely(x1i < width)) //happens sometimes that last x exceeds glyph rect by <1 pixel due to extrapolation
                                areas_fill[linestart + x1i] += d;
                        }
                        x = xnext;
                    }
                }
            }

            //this loop is ~15 % of rendering time(so not much SIMD speedup potential)
            for (int y = 0; y < height; y++)
            {
                float sum       = 0;  //important to reset sum at every line start to not accumulate errors over the entire picture
                var   linestart = y * width;
                for (int x = 0; x < width; x++)
                {
                    var index      = linestart + x;
                    sum           += areas_fill[index];  //+1 = filled, 0 = not filled
                    var k          = sum + areas[index];  //add or substract partial pixel coverage from current fill
                    var alpha      = math.abs(k);
                    alpha          = math.select(1.0f, alpha, alpha < 1.0f);  //clip coverage to 1 (=filled)
                    var alphaByte  = (byte)(255 * alpha);
                    if (alphaByte > 1)
                    {
                        var color          = pattern.GetColor(new float2(x, y) + offset);
                        color.a            = (byte)(color.a * alphaByte / 255);
                        textureData[index] = color;
                    }
                }
            }
            PaintUtils.rasterizeCOLRMarker.End();
        }

        public static void RasterizeAndBlend<T>(ref DrawData drawData, NativeArray<ColorBGRA> textureData, T pattern, PaintCompositeMode mode, BBox clipRect,
                                                bool invert = false) where T : IPattern
        {
            PaintUtils.rasterizeCOLRMarker.Begin();
            var edges      = drawData.edges;
            var contourIDs = drawData.contourIDs;
            var width      = clipRect.intWidth;
            var height     = clipRect.intHeight;

            //"areas" stores the partial signed pixel coverage (triangle, right trapezoids, rectangle+trapezoid) encountered when edges are cutting pixel
            //"areas_fill" stores the 100% coverage (signed!) of the 1st pixel to the right of an edge cutting the previous pixel
            //"areas fill" is used in a cumulative sum to determine the fill of a given line in the bitmap. The cumulative sum needs to be 100% accurate,
            //to ensure areas cancel eachother out (upwards egde = +1, downwards edge = -1). Floating point errors would lead to banding
            //For this reasons, the signed coverage stored in "areas" is not included in the cumulative sum, and only added as a last step
            //(if desired: ommit this last step to get an aliased bitmap)
            //Alternatively, we could also just use 1 array, and instead of storing triangle and trapezoid areas, store the delta areas of
            //current pixel (cut by edge) vs previous pixel. The cumulative sum of those delta areas should then result in the correct coverage for
            //each pixel, however this is prone to floating point errors, and accumulation of such errors results in banding
            var arraySize  = width * height;
            var areas      = new NativeArray<float>(arraySize, Allocator.Temp);
            var areas_fill = new NativeArray<float>(arraySize, Allocator.Temp);
            var offset     = clipRect.min;
            for (int contourID = 0, end = contourIDs.Length - 1; contourID < end; contourID++)  //for each contour
            {
                int startID     = contourIDs[contourID];
                int nextStartID = contourIDs[contourID + 1];
                for (int edgeID = startID; edgeID < nextStartID; edgeID++)  //for each edge
                {
                    var  edge    = edges[edgeID];
                    var  p0      = edge.start_pos - offset;
                    var  p1      = edge.end_pos - offset;
                    bool inverse = p1.y < p0.y;
                    var  dir     = math.select(1.0f, -1.0f, inverse);
                    if (inverse)
                        (p0, p1) = (p1, p0);
                    var dxdy     = (p1.x - p0.x) / (p1.y - p0.y);
                    var x        = p0.x;
                    x            = math.select(x, x - p0.y * dxdy, x < 0.0f);
                    for (int y = math.max(0, (int)p0.y), yy = math.min(height, (int)math.ceil(p1.y)); y < yy; y++)
                    {
                        var linestart     = y * width;
                        var sy0           = math.max(y, p0.y);
                        var sy1           = math.min(y + 1, p1.y);
                        var dy            = sy1 - sy0;
                        var xnext         = x + dxdy * dy;
                        var d             = dy * dir;
                        var x0            = math.select(xnext, x, x < xnext);
                        var x1            = math.select(x, xnext, x < xnext);
                        var x0floor       = math.floor(x0);
                        var x0i           = (int)x0floor;
                        var x1ceil        = math.ceil(x1);
                        var x1i           = (int)x1ceil;
                        var linestart_x0i = linestart + x0i;
                        if (Hint.Unlikely(linestart_x0i < 0) || Hint.Unlikely(linestart_x0i > arraySize - 1))  // index is out of bounds
                        {
                            x = xnext;
                            continue;
                        }

                        if (x1i <= x0i + 1)
                        {
                            // simple case, edge only cuts one pixel (includes vertical case)
                            var bottomWidth       = (x0floor + 1.0f) - x1;
                            var topWidth          = (x0floor + 1.0f) - x0;
                            var trapezoidArea     = d * (bottomWidth + topWidth) * 0.5f;
                            areas[linestart_x0i] += trapezoidArea;
                            if (Hint.Likely(x1i < width)) //happens sometimes that last x exceeds glyph rect by <1 pixel due to xnext extrapolation
                                areas_fill[linestart_x0i + 1] += d; // everything right of this pixel is filled
                        }
                        else
                        {
                            // edge cuts several pixel
                            var topY    = math.select(sy1, sy0, inverse);
                            var bottomY = math.select(sy0, sy1, inverse);

                            //need math.abs(dydx) so that calculation of areas works correctly for all 4 possible orientation of an edge
                            //(NE to SW, SW to NE, NW to SE, SE to NW) the sign of the resulting area is ultimately determined by dir (and not by dydx!)
                            var dydx    = math.abs(1.0f / dxdy);
                            var x0width = x0floor + 1.0f - x0;

                            // calculate area of the triangle in the first pixel cut by the edge;
                            var firstYcrossing    = bottomY + dydx * dir * x0width;
                            var firstYHeight      = firstYcrossing - bottomY;
                            areas[linestart_x0i] += firstYHeight * x0width * 0.5f;  //area of triangle

                            // next, iteratively increase the firstYHeight and use it to determine the
                            // trapezoid area for all pixel crossed by edge
                            var step = dir * dydx;
                            for (int xi = linestart + x0i + 1, xii = linestart + x1i - 1; xi < xii; xi++)
                            {
                                areas[xi]    += firstYHeight + step * 0.5f;  // area of trapezoid is 1*step/2
                                firstYHeight += step;
                            }

                            // determine rectangle area + trapezoid area of last pixel cut by the edge
                            var lastYcrossing           = bottomY + dydx * dir * (x1ceil - 1.0f - x0);
                            var lastTriangleHeight      = topY - lastYcrossing;
                            var lastWidth               = x1ceil - x1;
                            var lastTrapezoidArea       = lastTriangleHeight * (lastWidth + 1.0f) * 0.5f;
                            areas[linestart + x1i - 1] += firstYHeight + lastTrapezoidArea;  //area of rectangle + area of trapezoid

                            // everything right of the last pixel is filled
                            if (Hint.Likely(x1i < width)) //happens sometimes that last x exceeds glyph rect by <1 pixel due to extrapolation
                                areas_fill[linestart + x1i] += d;
                        }
                        x = xnext;
                    }
                }
            }

            //this loop is ~15 % of rendering time(so not much SIMD speedup potential)
            for (int y = 0; y < height; y++)
            {
                float sum       = 0;  //important to reset sum at every line start to not accumulate errors over the entire picture
                var   linestart = y * width;
                for (int x = 0; x < width; x++)
                {
                    var index      = linestart + x;
                    sum           += areas_fill[index];
                    var k          = areas[index] + sum;
                    var alpha      = math.abs(k);
                    alpha          = math.select(1.0f, alpha, alpha < 1.0f);
                    var alphaByte  = (byte)(255 * alpha);
                    if (alphaByte > 1)
                    {
                        var color          = pattern.GetColor(new float2(x, y) + offset);
                        color.a            = (byte)(color.a * alphaByte / 255);
                        textureData[index] = Blending.Blend(color, textureData[index], mode);
                    }
                }
            }
            PaintUtils.rasterizeCOLRMarker.End();
        }
    }
}

