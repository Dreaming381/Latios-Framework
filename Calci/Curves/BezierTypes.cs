using System.Diagnostics;
using Unity.Mathematics;

namespace Latios.Calci
{
    /// <summary>
    /// A vertex with tangents that describes one end of a curve within a spline
    /// </summary>
    public struct BezierKnot
    {
        public float3 position;
        public float3 tangentIn;  // Always pointing away from position
        public float3 tangentOut;  // Always pointing away from position

        /// <summary>
        /// Constructs a knot from a position and the two tangents. If one tangent is in the exact opposite direction of the other,
        /// the spline point is smooth.
        /// </summary>
        /// <param name="position">The position of the knot</param>
        /// <param name="tangentIn">The tangent pointing in the direction that nearby previous points along the spline belong to</param>
        /// <param name="tangentOut">The tangent pointing in the direction that nearby following points along the spline belong to</param>
        public BezierKnot(float3 position, float3 tangentIn, float3 tangentOut)
        {
            this.position   = position;
            this.tangentIn  = tangentIn;
            this.tangentOut = tangentOut;
        }

        /// <summary>
        /// Reverses the direction of the knot, while preserving the same curve shape
        /// </summary>
        /// <returns></returns>
        public BezierKnot ToReverse() => new BezierKnot(position, tangentOut, tangentIn);

        /// <summary>
        /// Constructs a knot that defines a beginning of a spline, using the first curve of the spline
        /// </summary>
        /// <param name="curve">The first curve of a spline, in which endpointA is used as the knot point</param>
        /// <returns>A knot based on endpointA of the curve with smooth tangents</returns>
        public static BezierKnot FromCurveEndpointA(in BezierCurve curve)
        {
            var tangentOut = curve.controlA - curve.endpointA;
            return new BezierKnot(curve.endpointA, -tangentOut, tangentOut);
        }

        /// <summary>
        /// Constructs a knot that defines an end of a spline, using the last curve of the spline
        /// </summary>
        /// <param name="curve">The last curve of a spline, in which endpointB is used as the knot point</param>
        /// <returns>A knot based on endpointB of the curve with smooth tangents</returns>
        public static BezierKnot FromCurveEndpointB(in BezierCurve curve)
        {
            var tangentIn = curve.controlB - curve.endpointB;
            return new BezierKnot(curve.endpointA, tangentIn, -tangentIn);
        }

        /// <summary>
        /// Constructs a knot that defines a middle point of a spline between two curves
        /// </summary>
        /// <param name="curveA">The curve before the knot within the spline, where endpointB should be equal to the knot position</param>
        /// <param name="curveB">The curve after the knot within the spline, where endpointA should be equal to the knot position</param>
        /// <returns>A knot at the point that connects the two curves, with tangents compatible with the curves</returns>
        public static BezierKnot FromTwoCurves(in BezierCurve curveA, in BezierCurve curveB)
        {
            EndpointsMatch(curveA.endpointB, curveB.endpointA);
            return new BezierKnot(curveA.endpointB, curveA.controlB - curveA.endpointB, curveB.controlA - curveA.endpointB);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void EndpointsMatch(float3 fromA, float3 fromB)
        {
            if (!fromA.Equals(fromB))
                throw new System.ArgumentException($"The two curves do not connect. curveA.endpointB {fromA} != curveB.endpointA {fromB}");
        }
    }

    /// <summary>
    /// A cubic bezier curve in 3D space
    /// </summary>
    public struct BezierCurve
    {
        public float3 endpointA;  // also known as control point 0
        public float3 controlA;  // also known as control point 1
        public float3 controlB;  // also known as control point 2
        public float3 endpointB;  // also known as control point 3

        /// <summary>
        /// Constructs the cubic bezier curve from the 4 control points in sequence
        /// </summary>
        public BezierCurve(float3 endpointA, float3 controlA, float3 controlB, float3 endpointB)
        {
            this.endpointA = endpointA;
            this.controlA  = controlA;
            this.controlB  = controlB;
            this.endpointB = endpointB;
        }

        /// <summary>
        /// Constructs the cubic bezier from the 4 control points packed in sequence inside a simdFloat3
        /// </summary>
        public BezierCurve(in simdFloat3 controlPointsInSimd)
        {
            endpointA = controlPointsInSimd.a;
            controlA  = controlPointsInSimd.b;
            controlB  = controlPointsInSimd.c;
            endpointB = controlPointsInSimd.d;
        }

        /// <summary>
        /// Constructs a cubic bezier curve that defines a straight line segment from the passed in point a to point b
        /// </summary>
        /// <param name="a">Segment point a which should become the curve's endpointA</param>
        /// <param name="b">Segment point b which should become the curve's endpointB</param>
        /// <returns>A bezier curve that is a straight line segment</returns>
        public static BezierCurve FromLineSegment(float3 a, float3 b)
        {
            return new BezierCurve(a, b, a, b);
        }

        /// <summary>
        /// Constructs a cubic bezier curve that matches the passed in quadratic bezier curve control points
        /// </summary>
        /// <param name="endpointA">The first endpoint of the quadratic bezier curve</param>
        /// <param name="control">The intermediate control point of the quadratic bezier curve</param>
        /// <param name="endpointB">The second endpoint of the quadratic bezier curve</param>
        /// <returns>A cubic bezier curve that is identical to the quadratic bezier curve</returns>
        public static BezierCurve FromQuadratic(float3 endpointA, float3 control, float3 endpointB)
        {
            float3 tangent = 2f / 3f * control;
            return new BezierCurve(endpointA, endpointA / 3f + tangent, endpointB / 3f + tangent, endpointB);
        }

        /// <summary>
        /// Constructs a cubic bezier curve given the two endpoint bezier knots
        /// </summary>
        /// <param name="knotA">The first knot that defines the start of the bezier curve</param>
        /// <param name="knotB">The second knot that defines the end of the bezier curve</param>
        /// <returns>A bezier curve that connects the two knots</returns>
        public static BezierCurve FromKnots(in BezierKnot knotA, in BezierKnot knotB)
        {
            return new BezierCurve(knotA.position, knotA.position + knotA.tangentOut, knotB.position + knotB.tangentIn, knotB.position);
        }

        /// <summary>
        /// Flips the endpoints and direction of the bezier curve, while preserving the overall shape.
        /// Any factor t evaluated for the original curve will be equal to 1 - t evaluated for the flipped curve.
        /// </summary>
        /// <returns>The bezier curve flipped around</returns>
        public BezierCurve ToReverse() => new BezierCurve(endpointB, controlB, controlA, endpointA);

        /// <summary>
        /// Packs the control points in sequence into a simdFloat3
        /// </summary>
        public simdFloat3 ToSimdFloat3() => new simdFloat3(endpointA, controlA, controlB, endpointB);

        /// <summary>
        /// 32 segment subdivision lengths of the bezier curve
        /// </summary>
        public unsafe struct SegmentLengths
        {
            public fixed float lengths[32];
        }
    }

    /// <summary>
    /// A y-value with tangent weights and slopes relative to the x-axis,
    /// describing a single keyframe within a parameter curve
    /// </summary>
    public struct Keyframe
    {
        /// <summary>
        /// When tangent weight values are set to this value, a KeyedCurve becomes
        /// a Hermite curve and is significantly faster to evaluate. It is recommended
        /// to use this value as a default tangent weight for this reason.
        /// </summary>
        public const float kHermite = 1f / 3f;

        public float time;  // x-axis value
        public float value;  // y-axis value
        public float inTangentSlope;
        public float inTangentWeight;
        public float outTangentSlope;
        public float outTangentWeight;

        /// <summary>
        /// Constructs a keyframe using default Hermite weights for the tangents
        /// </summary>
        /// <param name="time">The time of the keyframe</param>
        /// <param name="value">The value at the time of the keyframe</param>
        /// <param name="inTangentSlope">The slope (change in value over time) going into the keyframe forward in time</param>
        /// <param name="outTangentSlope">The slope (change in value over time) going out of the keyframe forward in time</param>
        public Keyframe(float time, float value, float inTangentSlope, float outTangentSlope)
        {
            this.time            = time;
            this.value           = value;
            this.inTangentSlope  = inTangentSlope;
            inTangentWeight      = kHermite;
            this.outTangentSlope = outTangentSlope;
            outTangentWeight     = kHermite;
        }

        /// <summary>
        /// Constructs a keyframe using custom Bezier weights for the tangents
        /// </summary>
        /// <param name="time">The time of the keyframe</param>
        /// <param name="value">The value at the time of the keyframe</param>
        /// <param name="inTangentSlope">The slope (change in value over time) going into the keyframe forward in time</param>
        /// <param name="inTangentWeight">How closely the curve sticks to the line traced by the inTangentSlope going into the keyframe</param>
        /// <param name="outTangentSlope">The slope (change in value over time) going out of the keyframe forward in time</param>
        /// <param name="outTangentWeight">How closely the curve sticks to the line traced by the outTangentSlope going out of the keyframe</param>
        public Keyframe(float time, float value, float inTangentSlope, float inTangentWeight, float outTangentSlope, float outTangentWeight)
        {
            this.time             = time;
            this.value            = value;
            this.inTangentSlope   = inTangentSlope;
            this.inTangentWeight  = inTangentWeight;
            this.outTangentSlope  = outTangentSlope;
            this.outTangentWeight = outTangentWeight;
        }

        /// <summary>
        /// Constructs a keyframe that defines the start of a CurveTrack, using the first curve of the CurveTrack
        /// </summary>
        /// <param name="curve">The first curve of the CurveTrack, in which the "left" values are used for the keyframe</param>
        /// <returns>A keyframe based on the "left" values of the curve with aligned and equally-weighted tangent slopes</returns>
        public static Keyframe FromCurveLeftPoint(in KeyedCurve curve)
        {
            return new Keyframe(curve.leftTime, curve.leftValue, curve.leftTangentSlope, curve.leftTangentWeight, curve.leftTangentSlope, curve.leftTangentWeight);
        }

        /// <summary>
        /// Constructs a keyframe that defines the end of a CurveTrack, using the last curve of the CurveTrack
        /// </summary>
        /// <param name="curve">The last curve of the CurveTrack, in which the "right" values are used for the keyframe</param>
        /// <returns>A keyframe based on the "right" values of the curve with aligned and equally-weighted tangent slopes</returns>
        public static Keyframe FromCurveRightPoint(in KeyedCurve curve)
        {
            return new Keyframe(curve.rightTime, curve.rightValue, curve.rightTangentSlope, curve.rightTangentWeight, curve.rightTangentSlope, curve.rightTangentWeight);
        }

        /// <summary>
        /// Constructs a keyframe that defines a middle point of a CurveTrack between two curves
        /// </summary>
        /// <param name="leftCurve">The curve before the keyframe, that ends with the keyframe</param>
        /// <param name="rightCurve">The curve after the keyframe, that starts with the keyframe</param>
        /// <returns>The keyframe from where the curves connect</returns>
        public static Keyframe FromTwoCurves(in KeyedCurve leftCurve, in KeyedCurve rightCurve)
        {
            EndpointsMatch(leftCurve.rightTime, leftCurve.rightValue, rightCurve.leftTime, rightCurve.leftValue);
            return new Keyframe(leftCurve.rightTime,
                                leftCurve.rightValue,
                                leftCurve.rightTangentSlope,
                                leftCurve.rightTangentWeight,
                                rightCurve.leftTangentSlope,
                                rightCurve.leftTangentWeight);
        }

        public static implicit operator Keyframe(UnityEngine.Keyframe unityKeyframe)
        {
            float inWeight, outWeight;
            switch (unityKeyframe.weightedMode)
            {
                case UnityEngine.WeightedMode.None:
                    inWeight  = kHermite;
                    outWeight = kHermite;
                    break;
                case UnityEngine.WeightedMode.In:
                    inWeight  = unityKeyframe.inWeight;
                    outWeight = kHermite;
                    break;
                case UnityEngine.WeightedMode.Out:
                    inWeight  = kHermite;
                    outWeight = unityKeyframe.outWeight;
                    break;
                case UnityEngine.WeightedMode.Both:
                    inWeight  = unityKeyframe.inWeight;
                    outWeight = unityKeyframe.outWeight;
                    break;
                default:
                    inWeight  = kHermite;
                    outWeight = kHermite;
                    break;
            }
            return new Keyframe
            {
                time             = unityKeyframe.time,
                value            = unityKeyframe.value,
                inTangentSlope   = unityKeyframe.inTangent,
                inTangentWeight  = inWeight,
                outTangentSlope  = unityKeyframe.outTangent,
                outTangentWeight = outWeight
            };
        }

        public static implicit operator UnityEngine.Keyframe(Keyframe keyframe)
        {
            var                      inIsHermite  = keyframe.inTangentWeight == kHermite;
            var                      outIsHermite = keyframe.outTangentWeight == kHermite;
            UnityEngine.WeightedMode mode         = (inIsHermite, outIsHermite) switch
            {
                (false, false) => UnityEngine.WeightedMode.Both,
                (false, true) => UnityEngine.WeightedMode.Out,
                (true, false) => UnityEngine.WeightedMode.In,
                (true, true) => UnityEngine.WeightedMode.None
            };
            return new UnityEngine.Keyframe
            {
                time         = keyframe.time,
                value        = keyframe.value,
                inTangent    = keyframe.inTangentSlope,
                inWeight     = keyframe.inTangentWeight,
                outTangent   = keyframe.outTangentSlope,
                outWeight    = keyframe.outTangentWeight,
                weightedMode = mode
            };
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void EndpointsMatch(float leftTime, float leftValue, float rightTime, float rightValue)
        {
            if (leftTime != rightTime)
                throw new System.ArgumentException($"The two curves do not connect. left curve's end time {leftTime} != right curve's start time {rightTime}");
            if (leftValue != rightValue)
                throw new System.ArgumentException($"The two curves do not connect. left curve's end value {leftValue} != right curve's start value {rightValue}");
        }
    }

    /// <summary>
    /// A cubic bezier curve represented as a relationship between y (value) and x (time)
    /// </summary>
    public struct KeyedCurve
    {
        public float leftTime;
        public float leftValue;
        public float leftTangentSlope;
        public float leftTangentWeight;
        public float rightTime;
        public float rightValue;
        public float rightTangentSlope;
        public float rightTangentWeight;

        /// <summary>
        /// Constructs a cubic bezier keyed curve from two adjacent keyframes. If the keyframe time values are in the wrong order,
        /// they will be flipped to the correct order.
        /// WARNING: Do not construct from two keyframes with the same time values. See remarks.
        /// </summary>
        /// <param name="leftKey">The left keyframe from which to construct the curve</param>
        /// <param name="rightKey">The right keyframe from which to construct the curve</param>
        /// <returns>The curve between the two keyframes</returns>
        /// <remarks>The KeyedCurve must have a time delta between the keyframes, otherwise evaluations may fail.
        /// In the case of a step function, you want to find the first key from the left where keyTime is strictly less than sampleTime.
        /// Do not use less-equal.</remarks>
        public static KeyedCurve FromKeyframes(in Keyframe leftKey, in Keyframe rightKey)
        {
            if (leftKey.time > rightKey.time)
            {
                // We need to swap the keyframes around
                return new KeyedCurve
                {
                    leftTime           = rightKey.time,
                    leftValue          = rightKey.value,
                    leftTangentSlope   = rightKey.outTangentSlope,
                    leftTangentWeight  = rightKey.outTangentWeight,
                    rightTime          = leftKey.time,
                    rightValue         = leftKey.value,
                    rightTangentSlope  = leftKey.inTangentSlope,
                    rightTangentWeight = leftKey.inTangentWeight,
                };
            }
            return new KeyedCurve
            {
                leftTime           = leftKey.time,
                leftValue          = leftKey.value,
                leftTangentSlope   = leftKey.outTangentSlope,
                leftTangentWeight  = leftKey.outTangentWeight,
                rightTime          = rightKey.time,
                rightValue         = rightKey.value,
                rightTangentSlope  = rightKey.inTangentSlope,
                rightTangentWeight = rightKey.inTangentWeight,
            };
        }
    }
}

