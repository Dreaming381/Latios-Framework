using Latios.Transforms;
using Unity.Collections;
using Unity.Mathematics;

// Todo: When scaling a knot or curve relative to a reference point,
// do we scale the endpoint positions or the tangent vectors?

namespace Latios.Calci
{
    public static partial class BezierMath
    {
        /// <summary>
        /// Splits the curve at factor t into two parts which when concatenated represent the path of the original curve
        /// </summary>
        /// <param name="curve">The curve to split</param>
        /// <param name="t">The factor along the curve to split</param>
        /// <param name="outA">The first split curve which shares endpointA with the original</param>
        /// <param name="outB">The second split curve which shares endpointB with the original</param>
        public static void SplitCurve(in BezierCurve curve, float t, out BezierCurve outA, out BezierCurve outB)
        {
            // Lerp the 3 control segments
            var splitA = math.lerp(curve.endpointA, curve.controlA, t);
            var splitB = math.lerp(curve.controlA, curve.controlB, t);
            var splitC = math.lerp(curve.controlB, curve.endpointB, t);

            // Lerp the lerps
            var splitAB = math.lerp(splitA, splitB, t);
            var splitBC = math.lerp(splitB, splitC, t);

            // Lerp the lerped lerps
            var superSplit = math.lerp(splitAB, splitBC, t);

            outA = new BezierCurve(curve.endpointA, splitA, splitAB, superSplit);
            outB = new BezierCurve(superSplit, splitBC, splitC, curve.endpointB);
        }

        #region Transform Knots
        /// <summary>
        /// Applies a translation to the knot
        /// </summary>
        /// <param name="translation">The offset to apply to the knot</param>
        /// <param name="knot">The knot to be translated</param>
        /// <returns>The resulting translated knot</returns>
        public static BezierKnot TranslateKnot(float3 translation, in BezierKnot knot)
        {
            var result       = knot;
            result.position += translation;
            return result;
        }

        /// <summary>
        /// Applies rotation to the knot's tangents
        /// </summary>
        /// <param name="rotation">The rotation to apply to the knot</param>
        /// <param name="knot">The knot to be rotated</param>
        /// <returns>The resulting rotated knot</returns>
        public static BezierKnot RotateKnot(quaternion rotation, in BezierKnot knot)
        {
            var newTangentIn  = math.rotate(rotation, knot.tangentIn);
            var newTangentOut = math.rotate(rotation, knot.tangentOut);
            return new BezierKnot(knot.position, newTangentIn, newTangentOut);
        }

        /// <summary>
        /// Rotates the knot about the reference point
        /// </summary>
        /// <param name="referencePoint">The position around which the knot is rotated, or the pivot of the rotation</param>
        /// <param name="rotation">The rotation to rotate the knot about the reference point</param>
        /// <param name="knot">The knot to be rotated about the reference point</param>
        /// <returns>The resulting transformed knot</returns>
        public static BezierKnot RotateKnotAbout(float3 referencePoint, quaternion rotation, in BezierKnot knot)
        {
            var knotRelativePosition = knot.position - referencePoint;
            var newPosition          = referencePoint + math.rotate(rotation, knotRelativePosition);
            var newTangentIn         = math.rotate(rotation, knot.tangentIn);
            var newTangentOut        = math.rotate(rotation, knot.tangentOut);
            return new BezierKnot(newPosition, newTangentIn, newTangentOut);
        }

        /// <summary>
        /// Applies scale to the knot's tangents
        /// </summary>
        /// <param name="scale">The scale to apply to the knot</param>
        /// <param name="knot">The knot to be scaled</param>
        /// <returns>The resulting scaled knot</returns>
        public static BezierKnot ScaleKnot(float scale, in BezierKnot knot)
        {
            var newTangentIn  = knot.tangentIn * scale;
            var newTangentOut = knot.tangentOut * scale;
            return new BezierKnot(knot.position, newTangentIn, newTangentOut);
        }

        /// <summary>
        /// Applies stretch to the knot's tangents
        /// </summary>
        /// <param name="stretch">The stretch to apply to the knot</param>
        /// <param name="knot">The knot to be stretched</param>
        /// <returns>The resulting stretched knot</returns>
        public static BezierKnot StetchKnot(float3 stretch, in BezierKnot knot)
        {
            var newTangentIn  = knot.tangentIn * stretch;
            var newTangentOut = knot.tangentOut * stretch;
            return new BezierKnot(knot.position, newTangentIn, newTangentOut);
        }

        /// <summary>
        /// Applies translation and rotation to the knot relative to the knot itself
        /// </summary>
        /// <param name="transform">The transform to apply to the knot</param>
        /// <param name="knot">The knot to be transformed</param>
        /// <returns>The resulting transformed knot</returns>
        public static BezierKnot TransformKnot(in RigidTransform transform, in BezierKnot knot)
        {
            var newPosition   = math.transform(transform, knot.position);
            var newTangentIn  = math.rotate(transform, knot.tangentIn);
            var newTangentOut = math.rotate(transform, knot.tangentOut);
            return new BezierKnot(newPosition, newTangentIn, newTangentOut);
        }

        /// <summary>
        /// Transforms the knot about the reference point
        /// </summary>
        /// <param name="referencePoint">The position the knot is transformed relative to</param>
        /// <param name="transform">The transform to apply to the knot relative to the reference point</param>
        /// <param name="knot">The knot to be transformed</param>
        /// <returns>The transformed knot</returns>
        public static BezierKnot TransformKnotAbout(float3 referencePoint, in RigidTransform transform, in BezierKnot knot)
        {
            var knotRelativePosition = knot.position - referencePoint;
            var newPosition          = referencePoint + math.transform(transform, knotRelativePosition);
            var newTangentIn         = math.rotate(transform, knot.tangentIn);
            var newTangentOut        = math.rotate(transform, knot.tangentOut);
            return new BezierKnot(newPosition, newTangentIn, newTangentOut);
        }

        /// <summary>
        /// Applies the matrix transformation to the knot relative to the knot itself
        /// </summary>
        /// <param name="transform">The matrix transform to apply to the knot</param>
        /// <param name="knot">The knot to be transformed</param>
        /// <returns>The resulting transformed knot</returns>
        public static BezierKnot TransformKnot(in float4x4 transform, in BezierKnot knot)
        {
            var newPosition   = math.transform(transform, knot.position);
            var newTangentIn  = math.rotate(transform, knot.tangentIn);
            var newTangentOut = math.rotate(transform, knot.tangentOut);
            return new BezierKnot(newPosition, newTangentIn, newTangentOut);
        }

        /// <summary>
        /// Applies translation, rotation, scale, and stretch to the knot relative to the knot itself
        /// </summary>
        /// <param name="transform">The transform to apply to the knot</param>
        /// <param name="knot">The knot to be transformed</param>
        /// <returns>The resulting transformed knot</returns>
        public static BezierKnot TransformKnot(in TransformQvvs transform, in BezierKnot knot)
        {
            var newPosition   = qvvs.TransformPoint(in transform, knot.position);
            var newTangentIn  = qvvs.TransformDirectionScaledAndStretched(in transform, knot.tangentIn);
            var newTangentOut = qvvs.TransformDirectionScaledAndStretched(in transform, knot.tangentOut);
            return new BezierKnot(newPosition, newTangentIn, newTangentOut);
        }

        /// <summary>
        /// Applies inverse translation, rotation, scale, and stretch to the knot relative to the knot itself
        /// </summary>
        /// <param name="transformToInverseApply">The transform whose inverse should be used to transform the knot</param>
        /// <param name="knot">The knot to be transformed</param>
        /// <returns>The resulting transformed knot</returns>
        public static BezierKnot InverseTransformKnot(in TransformQvvs transformToInverseApply, in BezierKnot knot)
        {
            var newPosition   = qvvs.InverseTransformPoint(in transformToInverseApply, knot.position);
            var newTangentIn  = qvvs.InverseTransformDirectionScaledAndStretched(in transformToInverseApply, knot.tangentIn);
            var newTangentOut = qvvs.InverseTransformDirectionScaledAndStretched(in transformToInverseApply, knot.tangentOut);
            return new BezierKnot(newPosition, newTangentIn, newTangentOut);
        }
        #endregion

        #region Transform Curves
        /// <summary>
        /// Applies translation to the cubic bezier curve
        /// </summary>
        /// <param name="translation">The translation to apply</param>
        /// <param name="curve">The curve to apply the translation to</param>
        /// <returns>The translated curve</returns>
        public static BezierCurve TranslateCurve(float3 translation, in BezierCurve curve)
        {
            return new BezierCurve(curve.endpointA + translation, curve.controlA + translation, curve.controlB + translation, curve.endpointB + translation);
        }

        /// <summary>
        /// Rotates the curve about the reference point
        /// </summary>
        /// <param name="referencePoint">The position around which the curve is rotated, or the pivot of the rotation</param>
        /// <param name="rotation">The rotation to rotate the curve about the reference point</param>
        /// <param name="curve">The curve to be rotated about the reference point</param>
        /// <returns>The transformed curve</returns>
        public static BezierCurve RotateCurveAbout(float3 referencePoint, quaternion rotation, in BezierCurve curve)
        {
            var cps    = curve.ToSimdFloat3();
            var relCps = cps - referencePoint;
            cps        = referencePoint + simd.mul(rotation, relCps);
            return new BezierCurve(cps);
        }

        /// <summary>
        /// Transforms the curve about the reference point
        /// </summary>
        /// <param name="referencePoint">The position about which the curve is transformed</param>
        /// <param name="transform">The transform to apply to the curve about the reference point</param>
        /// <param name="curve">The curve to be transformed</param>
        /// <returns>The transformed curve</returns>
        public static BezierCurve TransformCurveAbout(float3 referencePoint, in RigidTransform transform, in BezierCurve curve)
        {
            var cps    = curve.ToSimdFloat3();
            var relCps = cps - referencePoint;
            cps        = referencePoint + simd.transform(transform, relCps);
            return new BezierCurve(cps);
        }
        #endregion
    }
}

