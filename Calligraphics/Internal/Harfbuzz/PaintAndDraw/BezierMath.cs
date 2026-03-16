using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz
{
    internal static class BezierMath
    {
		public const float epsilon1Float_abs = 1e-5f;   // at 1, next representable float step (ULP) is +- 2^(0 - 23) = 1.19e-7
		public const float epsilon1Float_rel = 1e-7f;   // at 1, next representable float step (ULP) is +- 2^(0 - 23) = 1.19e-7
		public const double epsilon1_abs = 1e-12;       // at 1, next representable double step (ULP) is +- 2^(0 - 52) = 2.22045e-16
		public const double epsilon1_rel = 1e-16;       // at 1, next representable double step (ULP) is +- 2^(0 - 52) = 2.22045e-16

		public const float epsilon100Float_abs = 1e-5f; // at 100, next representable float step (ULP) is +- 2^(6 - 23) = 7.62939e-06		
		public const double epsilon100_rel = 1e-10;     // at 100, next representable double step (ULP) is +- 2^(6 - 52) = 1.42109E-14

        /// <summary>Tolerance comparison for large and small values. https://realtimecollisiondetection.net/blog/?p=89</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GenericEquals(double x, double y)
        {
            return math.abs(x - y) <= math.max(epsilon1_abs, epsilon1_rel * math.max(math.abs(x), math.abs(y)));
        }
        /// <summary>Relative tolerance comparison of x and y, fails values become small </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsForLargeValues(float x, float y)
        {
            return math.abs(x - y) <= epsilon1Float_rel * math.max(math.abs(x), math.abs(y));
        }
        /// <summary>Absolute tolerance comparison of x and y, fails when values become large  </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsForSmallValues(float x, float y, float tolerance)
        {
            return (math.abs(x - y) <= tolerance);
        }
        /// <summary>Absolute tolerance comparison of x and y, fails when values become large  </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsForSmallValues(double x, double y, double tolerance)
        {
            return (math.abs(x - y) <= tolerance);
        }
        /// <summary>Relative tolerance comparison of x and y, fails values become small </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 EqualsForLargeValues(float4 x, float4 y)
        {
            return math.abs(x - y) <= epsilon1Float_rel * math.max(math.abs(x), math.abs(y));
        }
        /// <summary>Absolute tolerance comparison of x and y, fails when values become large  </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 EqualsForSmallValues(float4 x, float4 y, float tolerance)
        {
            return (math.abs(x - y) <= tolerance);
        }


        /// <summary>Finds the magnitude of the cross product of two vectors (if we pretend they're in three dimensions) </summary>
        /// <param name="a">First vector</param>
        /// <param name="b">Second vector</param>
        /// <returns>The magnitude of the cross product</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 cross2D(float4 ax, float4 ay, float4 bx, float4 by)
        {
            return (ax * by) - (ay * bx);
        }
        /// <summary>Finds the magnitude of the cross product of two vectors (if we pretend they're in three dimensions) </summary>
        /// <param name="a">First vector</param>
        /// <param name="b">Second vector</param>
        /// <returns>The magnitude of the cross product</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cross2D(float ax, float ay, float bx, float by)
        {
            return (ax * by) - (ay * bx);
        }

        /// <summary>Finds the magnitude of the cross product of two vectors (if we pretend they're in three dimensions) </summary>
        /// <param name="a">First vector</param>
        /// <param name="b">Second vector</param>
        /// <returns>The magnitude of the cross product</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double cross2D(double ax, double ay, double bx, double by)
        {
            return (ax * by) - (ay * bx);
        }

        /// <summary>Finds the magnitude of the cross product of two vectors (if we pretend they're in three dimensions) </summary>
        /// <param name="a">First vector</param>
        /// <param name="b">Second vector</param>
        /// <returns>The magnitude of the cross product</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double4 cross2D(double4 ax, double4 ay, double4 bx, double4 by)
        {
            return (ax * by) - (ay * bx);
        }
        /// <summary> Max permitted deviatition of generated lines from original bezier curve. 
        /// Sensible value is fontscale / 25). Too low values massively hit performance.
        /// </summary>
        public static float GetMaxDeviation(float scale)
        {
            return math.max(2, scale / 96);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetDeviationQuadratic(ref SDFEdge quadratic)
        {
            var p0 = quadratic.start_pos;
            var p1 = quadratic.control1;
            var p2 = quadratic.end_pos;

            var d1 = math.abs(p2 + p0 - 2 * p1);
            return math.max(d1.x, d1.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetDeviationCubic(ref SDFEdge cubic)
        {
            var p0 = cubic.start_pos;
            var p1 = cubic.control1;
            var p2 = cubic.control2;
            var p3 = cubic.end_pos;

            var d1 = math.abs(2 * p0 - 3 * p1 + p3);
            var d2 = math.abs(p0 - 3 * p2 + 2 * p3);

            return math.max(math.max(d1.x, d1.y), math.max(d2.x, d2.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SplitQuadraticEdge(NativeList<SDFEdge> edges, ref SDFEdge edge)
        {
            var p0 = edge.start_pos;
            var p1 = edge.control1;
            var p2 = edge.end_pos;
            edge.edge_type = SDFEdgeType.LINE;

            var dev = p0 - 2 * p1 + p2;
            var devsq = math.dot(dev, dev);
            if (devsq < 0.333)
            {
                edges.Add(edge);
                return;
            }
            var tol = 3.0f;
            var n = 1 + math.floor(math.sqrt(math.sqrt(tol * devsq)));

            //var maxDev = GetDeviationQuadratic(ref edge);
            //var n = 1;
            //while (maxDev > tol)
            //{
            //    maxDev /= 4;
            //    n *= 2;
            //}
            //Debug.Log($"Split quadtratic bezier curve into {n} lines");

            var p = p0;
            var nRcp = (float)math.rcp(n);
            var t = 0.0f;
            for (int i = 0; i < n; i++)
            {
                t += nRcp;
                //First level of lerps: Calculate the point between
                var q0 = math.lerp(p0, p1, t);
                var q1 = math.lerp(p1, p2, t);

                //split point
                var pn = math.lerp(q0, q1, t);

                edges.Add(new SDFEdge { start_pos = p, end_pos = pn, edge_type = SDFEdgeType.LINE });
                p = pn;
            }
            //edges.Add(new SDFEdge { start_pos = p, end_pos = p2, edge_type = SDFEdgeType.UNDEFINED });
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SplitCubicEdge(NativeList<SDFEdge> edges, ref SDFEdge edge)
        {
            var p0 = edge.start_pos;
            var p1 = edge.control1;
            var p2 = edge.control2;
            var p3 = edge.end_pos;
            edge.edge_type = SDFEdgeType.LINE;

            var dev1 =2 * p0 - 3 * p1 + p3;
            var dev2 = p0 - 3 * p2 + 2 * p3;
            var devsq1 = math.dot(dev1, dev1);
            var devsq2 = math.dot(dev2, dev2);
            var devsq = math.max(devsq1, devsq2);
            if (devsq < 0.333)
            {
                edges.Add(edge);
                return;
            }
            var tol = 3.0f;
            var n = 1 + math.floor(math.sqrt(math.sqrt(tol * devsq)));

            //var maxDev = GetDeviationCubic(ref edge);
            //var n = 1;
            //while (maxDev > tol)
            //{
            //    maxDev /= 4;
            //    n *= 2;
            //}
            //Debug.Log($"Split cubic bezier curve into {n} lines");

            var p = p0;
            var nRcp = (float)math.rcp(n);
            var t = 0.0f;
            for (int i = 0; i < n; i++)
            {
                t += nRcp;
                //First level of lerps: Calculate the point between
                var q0 = math.lerp(p0, p1, t);
                var q1 = math.lerp(p1, p2, t);
                var q2 = math.lerp(p2, p3, t);

                //Second level of lerps: Calculate the point between
                var r0 = math.lerp(q0, q1, t);
                var r1 = math.lerp(q1, q2, t);

                //split point
                var pn = math.lerp(r0, r1, t);
                edges.Add(new SDFEdge { start_pos = p, end_pos = pn, edge_type = SDFEdgeType.LINE });
                p = pn;
            }
            //edges.Add(new SDFEdge { start_pos = p, end_pos = p2, edge_type = SDFEdgeType.UNDEFINED });
        }

        /// <summary> This function splits a quadratic bezier into two quadratic bezier exactly half way at t = 0.5. </summary>
        public static NativeArray<SDFEdge> SplitQuadraticEdge(SDFEdge edge, int num_splits)
        {
            int numRows = 1 << (num_splits);
            var targetArray = new NativeArray<SDFEdge>(numRows, Allocator.Temp);
            targetArray[0] = edge;

            for (int split = num_splits - 1; split >= 0; split--)
            {
                int pairDistance = 1 << split;

                for (int row = 0; row < numRows - pairDistance; row += 2 * pairDistance)
                {
                    var sourceEdge = targetArray[row];
                    var A = sourceEdge.start_pos;
                    var B = sourceEdge.control1;
                    var C = sourceEdge.end_pos;

                    var D = (A + B) * 0.5f;
                    var E = (B + C) * 0.5f;
                    var F = (D + E) * 0.5f;

                    sourceEdge.start_pos = A;
                    sourceEdge.control1 = D;
                    sourceEdge.end_pos = F;
                    sourceEdge.edge_type = SDFEdgeType.LINE;
                    targetArray[row] = sourceEdge;

                    targetArray[row + pairDistance] = new SDFEdge
                    {
                        start_pos = F,
                        control1 = E,
                        end_pos = C,
                        edge_type = SDFEdgeType.LINE,
                    };
                }
            }
            return targetArray;
        }

        /// <summary> This function splits a cubic bezier into two cubic bezier exactly half way at t = 0.5. </summary>
        public static NativeArray<SDFEdge> SplitCubicEdge(SDFEdge edge, int num_splits)
        {
            int numRows = 1 << (num_splits);
            var targetArray = new NativeArray<SDFEdge>(numRows, Allocator.Temp);
            targetArray[0] = edge;

            for (int split = num_splits - 1; split >= 0; split--)
            {
                int pairDistance = 1 << split;

                for (int row = 0; row < numRows - pairDistance; row += 2 * pairDistance)
                {
                    var sourceEdge = targetArray[row];
                    var A = sourceEdge.start_pos;
                    var B = sourceEdge.control1;
                    var C = sourceEdge.control2;
                    var D = sourceEdge.end_pos;

                    var E = (A + B) * 0.5f;
                    var F = (B + C) * 0.5f;
                    var G = (C + D) * 0.5f;
                    var H = (E + F) * 0.5f;
                    var J = (F + G) * 0.5f;
                    var K = (H + J) * 0.5f;

                    sourceEdge.start_pos = A;
                    sourceEdge.control1 = E;
                    sourceEdge.control2 = H;
                    sourceEdge.end_pos = K;
                    sourceEdge.edge_type = SDFEdgeType.LINE;
                    targetArray[row] = sourceEdge;

                    targetArray[row + pairDistance] = new SDFEdge
                    {
                        start_pos = K,
                        control1 = J,
                        control2 = G,
                        end_pos = D,
                        edge_type = SDFEdgeType.LINE,
                    };
                }
            }
            return targetArray;
        }
        /// <summary>
        /// Bounding box for conic (quadratic) bezier 
        /// <see href="https://iquilezles.org/articles/bezierbbox/">https://iquilezles.org/articles/bezierbbox/</see> 
        /// </summary>
        /// <param name="p0">start point</param>
        /// <param name="p1">controll point</param>
        /// <param name="p2">end point</param>
        public static BBox GetQuadraticBezierBBox(float2 p0, float2 p1, float2 p2)
        {
            var min = math.min(p0, p2);
            var max = math.max(p0, p2);

            if (p1.x < min.x || p1.x > max.x || p1.y < min.y || p1.y > max.y)
            {
                float2 t = math.clamp((p0 - p1) / (p0 - 2.0f * p1 + p2), 0.0f, 1.0f);
                float2 s = 1.0f - t;
                float2 q = s * s * p0 + 2.0f * s * t * p1 + t * t * p2;
                min = math.min(min, q);
                max = math.max(max, q);
            }
            return new BBox(min, max);
        }
        /// <summary>
        /// Bounding box for conic (quadratic) bezier 
        /// <see href="https://iquilezles.org/articles/bezierbbox/">https://iquilezles.org/articles/bezierbbox/</see> 
        /// </summary>
        /// <param name="p0">start point</param>
        /// <param name="p1">controll point 1</param>
        /// <param name="p2">controll point 2</param>
        /// <param name="p3">end point</param>
        public static BBox GetCubicBezierBBox(float2 p0, float2 p1, float2 p2, float2 p3)
        {
            var min = math.min(p0, p3);
            var max = math.max(p0, p3);

            float2 c = -1.0f * p0 + 1.0f * p1;
            float2 b = 1.0f * p0 - 2.0f * p1 + 1.0f * p2;
            float2 a = -1.0f * p0 + 3.0f * p1 - 3.0f * p2 + 1.0f * p3;

            float2 h = b * b - a * c;

            if (math.any(h > float2.zero))
            {
                float2 g = math.sqrt(math.abs(h));
                float2 t1 = math.clamp((-b - g) / a, 0.0f, 1.0f); float2 s1 = 1.0f - t1;
                float2 t2 = math.clamp((-b + g) / a, 0.0f, 1.0f); float2 s2 = 1.0f - t2;
                float2 q1 = s1 * s1 * s1 * p0 + 3.0f * s1 * s1 * t1 * p1 + 3.0f * s1 * t1 * t1 * p2 + t1 * t1 * t1 * p3;
                float2 q2 = s2 * s2 * s2 * p0 + 3.0f * s2 * s2 * t2 * p1 + 3.0f * s2 * t2 * t2 * p2 + t2 * t2 * t2 * p3;

                if (h.x > 0.0)
                {
                    min.x = math.min(min.x, math.min(q1.x, q2.x));
                    max.x = math.max(max.x, math.max(q1.x, q2.x));
                }

                if (h.y > 0.0)
                {
                    min.y = math.min(min.y, math.min(q1.y, q2.y));
                    max.y = math.max(max.y, math.max(q1.y, q2.y));
                }
            }
            return new BBox(min, max);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BBox GetLineBBox(float2 p0, float2 p1)
        {
            var min = math.min(p0, p1);
            var max = math.max(p0, p1);
            return new BBox(min, max);
        }
        public static bool SplitCuvesToLines(ref DrawData drawData, float maxDeviation, out DrawData newBezierData)
        {
            var edges = drawData.edges;
            var contourIDs = drawData.contourIDs;
            newBezierData = new DrawData(edges.Length * 16, contourIDs.Length, maxDeviation, Allocator.Temp);
            var newEdges = newBezierData.edges;
            var newContourIDs = newBezierData.contourIDs;
            newBezierData.glyphRect = drawData.glyphRect;
            bool success = true;
            SDFEdge edge;
            for (int contourID = 0, end = contourIDs.Length - 1; contourID < end; contourID++) //for each contour
            {
                newContourIDs.Add(newEdges.Length);
                int startID = contourIDs[contourID];
                int nextStartID = contourIDs[contourID + 1];
                float dx;
                int num_splits;
                for (int edgeID = startID; edgeID < nextStartID; edgeID++) //for each edge
                {
                    edge = edges[edgeID];
                    switch (edge.edge_type)
                    {
                        case SDFEdgeType.LINE:
                            newEdges.Add(edge);
                            break;
                        case SDFEdgeType.QUADRATIC:

                            dx = GetDeviationQuadratic(ref edge);
                            if (dx > maxDeviation)
                            {
                                num_splits = 1;
                                while (dx > maxDeviation)
                                {
                                    dx /= 4;
                                    num_splits *= 2;
                                }
                                newEdges.AddRange(SplitQuadraticEdge(edge, num_splits));
                            }
                            else
                            {
                                edge.edge_type = SDFEdgeType.LINE;
                                newEdges.Add(edge);
                            }
                            break;
                        case SDFEdgeType.CUBIC:
                            dx = GetDeviationCubic(ref edge);
                            if (dx > maxDeviation)
                            {
                                num_splits = 1;
                                while (dx > maxDeviation)
                                {
                                    dx /= 4;
                                    num_splits *= 2;
                                }
                                newEdges.AddRange(SplitCubicEdge(edge, num_splits));
                            }
                            else
                            {
                                edge.edge_type = SDFEdgeType.LINE;
                                newEdges.Add(edge);
                            }
                            break;

                        default:
                            break;
                    }
                }
            }
            newContourIDs.Add(newEdges.Length);//close the last contour
            return success;
        }
    }
}
