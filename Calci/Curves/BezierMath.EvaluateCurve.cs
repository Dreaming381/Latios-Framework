using System;
using Unity.Mathematics;

namespace Latios.Calci
{
    public static partial class BezierMath
    {
        /// <summary>
        /// Evaluates the curve at the parameter t in the range [0, 1]
        /// </summary>
        public static float3 PositionAt(in BezierCurve curve, float t)
        {
            var t2     = t * t;
            var t3     = t2 * t;
            var coeffs = new float4(-1f, 3f, -3f, 1f) * t3 +
                         new float4(3f, -6f, 3f, 0f) * t2 +
                         new float4(-3f, 3f, 0f, 0f) * t;
            coeffs.x += 1f;
            return coeffs.x * curve.endpointA + coeffs.y * curve.controlA + coeffs.z * curve.controlB + coeffs.w * curve.endpointB;
        }

        /// <summary>
        /// Evaluates the curve heading multiplied by speed (relative to a period from t = 0 to t = 1) at the parameter t in the range [0, 1]
        /// </summary>
        public static float3 VelocityAt(in BezierCurve curve, float t)
        {
            var t2     = t * t;
            var coeffs = new float4(-3f, 9f, -9f, 3f) * t2 +
                         new float4(6f, -12f, 6f, 0f) * t +
                         new float4(-3f, 3f, 0f, 0f);
            return coeffs.x * curve.endpointA + coeffs.y * curve.controlA + coeffs.z * curve.controlB + coeffs.w * curve.endpointB;
        }

        /// <summary>
        /// Evaluates the directional acceleration (relative to a period from t = 0 to t = 1) at the parameter t in the range [0, 1]
        /// </summary>
        public static float3 AccelerationAt(in BezierCurve curve, float t)
        {
            var coeffs = new float4(-6f, 18f, -18f, 6f) * t +
                         new float4(6f, -12f, 6f, 0f);
            return coeffs.x * curve.endpointA + coeffs.y * curve.controlA + coeffs.z * curve.controlB + coeffs.w * curve.endpointB;
        }

        /// <summary>
        /// Evaluates the individual lengths of the 32 segment subdivision of the curve
        /// </summary>
        /// <param name="curve">The curve to evaluate</param>
        /// <param name="lengths">A struct containing 32 length values</param>
        [Unity.Burst.CompilerServices.SkipLocalsInit]
        public static unsafe void SegmentLengthsOf(in BezierCurve curve, out BezierCurve.SegmentLengths lengths)
        {
            Span<float> pointsX = stackalloc float[33];
            Span<float> pointsY = stackalloc float[33];
            Span<float> pointsZ = stackalloc float[33];

            var endpointAx = curve.endpointA.x;
            var endpointAy = curve.endpointA.y;
            var endpointAz = curve.endpointA.z;
            var controlAx  = curve.controlA.x;
            var controlAy  = curve.controlA.y;
            var controlAz  = curve.controlA.z;
            var controlBx  = curve.controlB.x;
            var controlBy  = curve.controlB.y;
            var controlBz  = curve.controlB.z;
            var endpointBx = curve.endpointB.x;
            var endpointBy = curve.endpointB.y;
            var endpointBz = curve.endpointB.z;

            for (int i = 0; i < 32; i++)
            {
                var t       = i / 32f;
                var t2      = t * t;
                var t3      = t2 * t;
                var coeffsX = -t3 + 3f * t2 - 2f * t + 1f;
                var coeffsY = 3f * t3 - 6f * t2 + 3f * t;
                var coeffsZ = -3f * t3 + 3f * t2;
                //var coeffsW = t3;
                pointsX[i] = coeffsX * endpointAx + coeffsY * controlAx + coeffsZ * controlBx + t3 * endpointBx;
                pointsY[i] = coeffsX * endpointAy + coeffsY * controlAy + coeffsZ * controlBy + t3 * endpointBy;
                pointsZ[i] = coeffsX * endpointAz + coeffsY * controlAz + coeffsZ * controlBz + t3 * endpointBz;
            }
            pointsX[32] = endpointBx;
            pointsY[32] = endpointBy;
            pointsZ[32] = endpointBz;

            for (int i = 0; i < 32; i++)
            {
                lengths.lengths[i] = math.sqrt(math.square(pointsX[i + 1] - pointsX[i]) + math.square(pointsY[i + 1] - pointsY[i]) + math.square(pointsZ[i + 1] - pointsZ[i]));
            }
        }

        /// <summary>
        /// Sums the lengths to return an approximate curve length. The real curve length is slightly longer than this.
        /// </summary>
        public static unsafe float LengthOfApproximately(in BezierCurve.SegmentLengths lengths)
        {
            float result = 0f;
            for (int i = 0; i < 32; i++)
            {
                result += lengths.lengths[i];
            }
            return result;
        }

        /// <summary>
        /// Returns the approximate length of the curve by subdividing the curve into 32 segments and summing the lengths of the segments.
        /// The real curve length is slightly longer than this. Prefer to use the overload requesting BezierCurve.SegmentLengths if you
        /// already have them.
        /// </summary>
        public static float LengthOfApproximately(in BezierCurve curve)
        {
            SegmentLengthsOf(curve, out var lengths);
            return LengthOfApproximately(in lengths);
        }

        /// <summary>
        /// Finds an approximate factor t for the specified distance along the curve using the lengths of the curve segments.
        /// The algorithm incrementally sums segments until it finds one that exceeds the distance, then linearly interpolates
        /// a factor from that segment.
        /// </summary>
        /// <param name="lengths">The 32 segment lengths from a curve</param>
        /// <param name="distance">The distance along the curve to find a factor for</param>
        /// <returns>A factor t [0, 1] which can be used to sample a curve</returns>
        public static unsafe float FactorFromDistanceApproximately(in BezierCurve.SegmentLengths lengths, float distance)
        {
            // Todo: SIMD-optimize this.
            float sum = 0f;
            int   segment;
            for (segment = 0; segment < 32; segment++)
            {
                sum += lengths.lengths[segment];
                if (distance <= sum)
                    break;
            }
            // sum is the distance from the start of the curve to the end of the found segment.
            var distanceEnd     = sum;
            var distanceStart   = sum - lengths.lengths[segment];
            var distanceClamped = math.clamp(distance, distanceStart, distanceEnd);
            var tSegment        = math.unlerp(distanceStart, distanceEnd, distanceClamped);
            return (segment + tSegment) / 32f;
        }

        /// <summary>
        /// Finds an approximate factor t for the specified distance along the curve. The algorithm subdivides the curve into 32 segments.
        /// Then it incrementally sums segments until it finds one that exceeds the distance. It linearly interpolates a factor from that segment.
        /// Prefer to use the overload requesting BezierCurve.SegmentLengths if you have them.
        /// </summary>
        /// <param name="curve">The curve to find the factor for</param>
        /// <param name="distance">The distance along the curve to find a factor for</param>
        /// <returns>A factor t [0, 1] which can be used to sample a curve</returns>
        public static float FactorFromDistanceApproximately(in BezierCurve curve, float distance)
        {
            SegmentLengthsOf(in curve, out var lengths);
            return FactorFromDistanceApproximately(in lengths, distance);
        }

        /// <summary>
        /// Finds the approximate distance along the curve at sample factor t using the lengths of the curve segments.
        /// </summary>
        /// <param name="lengths">The 32 segment lengths from a curve</param>
        /// <param name="t">The factor t [0, 1] to sample the curve at</param>
        /// <returns>The approximate distance traveled along the curve from the start to the sample factor t</returns>
        public static unsafe float DistanceAlongApproximately(in BezierCurve.SegmentLengths lengths, float t)
        {
            var factor       = math.modf(32f * t, out var integerAsFloat);
            var segmentIndex = (int)integerAsFloat;
            var sum          = 0f;
            for (int i = 0; i < segmentIndex; i++)
                sum += lengths.lengths[i];
            return sum + factor * lengths.lengths[segmentIndex];
        }

        /// <summary>
        /// Finds the approximate distance along the curve at sample factor t. The algorithm subdivides the curve into 32 segments
        /// and uses their lengths and linear interpolation to compute the distance.
        /// Prefer to use the overload requesting BezierCurve.SegmentLengths if you have them.
        /// </summary>
        /// <param name="curve">The curve to find the distance along</param>
        /// <param name="t">The factor t [0, 1] to sample the curve at</param>
        /// <returns>The approximate distance traveled along the curve from the start to the sample factor t</returns>
        public static float DistanceAlongApproximately(in BezierCurve curve, float t)
        {
            SegmentLengthsOf(in curve, out var lengths);
            return DistanceAlongApproximately(in lengths, t);
        }
    }
}

