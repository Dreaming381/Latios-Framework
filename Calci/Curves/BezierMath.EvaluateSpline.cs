using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calci
{
    public static partial class BezierMath
    {
        /// <summary>
        /// Returns the approximate length of the spline by subdividing each adjacent knot pair into 32 linear segments and summing the lengths of the segments.
        /// The real spline length is slightly longer than this.
        /// </summary>
        public static float LengthOfApproximately(ReadOnlySpan<BezierKnot> spline)
        {
            float result = 0f;
            for (int i = 1; i < spline.Length; i++)
            {
                var curve  = BezierCurve.FromKnots(in spline[i - 1], in spline[i]);
                result    += LengthOfApproximately(in curve);
            }
            return result;
        }

        /// <summary>
        /// Gets the specific curve and factor within the spline that is approximately the specified distance along. The approximation is based on the approximate
        /// lengths of each curve within the spline (where a curve exists between each pair of adjacent knots). Each curve is divided into 32 linear segements.
        /// </summary>
        /// <param name="spline">The spline to evaluate</param>
        /// <param name="approximateDistanceAlong">The distance along the spline to identify the curve and factor within the curve for</param>
        /// <param name="curve">The curve extracted from the spline</param>
        /// <param name="t">The factor within the curve that can be used to sample the curve</param>
        /// <param name="knotAIndex">The index of the first of the two adjacent knots that the curve comes from</param>
        public static void CurveAndFactorFromApproximateDistanceAlong(ReadOnlySpan<BezierKnot> spline,
                                                                      float approximateDistanceAlong,
                                                                      out BezierCurve curve,
                                                                      out float t,
                                                                      out int knotAIndex)
        {
            var accumulatedDistance = 0f;
            curve                   = default;
            knotAIndex              = spline.Length - 2;
            for (int i = 1; i < spline.Length; i++)
            {
                curve = BezierCurve.FromKnots(in spline[i - 1], in spline[i]);
                SegmentLengthsOf(in curve, out var lengths);
                var curveLength = LengthOfApproximately(in lengths);
                if (accumulatedDistance + curveLength >= approximateDistanceAlong)
                {
                    var distanceInCurve = approximateDistanceAlong - accumulatedDistance;
                    t                   = FactorFromDistanceApproximately(in lengths, distanceInCurve);
                    knotAIndex          = i - 1;
                    return;
                }
            }
            t = 1f;
            return;
        }
    }
}

