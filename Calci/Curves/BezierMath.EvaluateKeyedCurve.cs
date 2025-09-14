using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Calci
{
    public static partial class BezierMath
    {
        /// <summary>
        /// Evaluates the curve at the specified time. The time passed in must be within the time range the curve is valid for,
        /// or else this method may produce unusable results including infinities and NaNs.
        /// </summary>
        /// <param name="curve">The curve to evaluate</param>
        /// <param name="time">The time value to evaluate</param>
        /// <returns>The evaluation of the curve</returns>
        public static float Evaluate(in KeyedCurve curve, float time)
        {
            if (curve.leftTangentWeight == Keyframe.kHermite && curve.rightTangentWeight == Keyframe.kHermite)
            {
                // Use hermite interpolation
                // The following implementation is taken from the old Unity.Animation package.
                // The hermite function is:
                // (2 * t^3 -3 * t^2 +1) * p0 + (t^3 - 2 * t^2 + t) * m0 + (-2 * t^3 + 3 * t^2) * p1 + (t^3 - t^2) * m1
                // The key observation here is that most of the terms are t^3 and t^2. We can factor out t^2 from these
                // terms and sum the terms first which leads to more floating point stability.
                // Additionally, this factored form happens to be more efficient. Ignoring loads, the optimal implementation
                // is fully scalar with FMAs. However, there might be an SLP optimization geared towards vector loads.
                var dx = curve.rightTime - curve.leftTime;
                var t  = (time - curve.leftTime) / dx;
                var p0 = curve.leftValue;
                var m0 = curve.leftTangentSlope * dx;
                var p1 = curve.rightValue;
                var m1 = curve.rightTangentSlope * dx;

                var a = 2.0f * p0 + m0 - 2.0f * p1 + m1;
                var b = -3.0f * p0 - 2.0f * m0 + 3.0f * p1 - m1;
                var c = m0;
                var d = p0;

                return t * (t * (a * t + b) + c) + d;
            }
            else
            {
                // Use cubic bezier interpolation
                static float FindT(float normalizedTime, float leftWeight, float rightWeight)
                {
                    // This implementation is taken from the old Unity.Animation package method BezierExtractU.
                    // No attempt has been made to optimize it yet.
                    // The algorithm here is simply solving a cubic to find the real root t for a given x on the Bezier curve.
                    static float CubeRootPositive(float a) => math.exp(math.log(a) / 3f);
                    static float CubeRoot(float a) => a < 0f ? -math.exp(math.log(-a) / 3f) : CubeRootPositive(a);

                    var t  = normalizedTime;
                    var w1 = leftWeight;
                    var w2 = rightWeight;

                    float a = 3f * w1 - 3f * w2 + 1f;
                    float b = -6f * w1 + 3f * w2;
                    float c = 3f * w1;
                    float d = -t;

                    if (math.abs(a) > 1e-3f)
                    {
                        float p  = -b / (3f * a);
                        float p2 = p * p;
                        float p3 = p2 * p;

                        float q  = p3 + (b * c - 3f * a * d) / (6f * a * a);
                        float q2 = q * q;

                        float r    = c / (3f * a);
                        float rmp2 = r - p2;

                        float s = q2 + rmp2 * rmp2 * rmp2;

                        if (s < 0f)
                        {
                            float ssi = math.sqrt(-s);
                            float r_1 = math.sqrt(-s + q2);
                            float phi = math.atan2(ssi, q);

                            float r_3   = CubeRootPositive(r_1);
                            float phi_3 = phi / 3f;

                            // Extract cubic roots.
                            float u1 = 2f * r_3 * math.cos(phi_3) + p;
                            float u2 = 2f * r_3 * math.cos(phi_3 + 2f * math.PI / 3.0f) + p;
                            float u3 = 2f * r_3 * math.cos(phi_3 - 2f * math.PI / 3.0f) + p;

                            if (u1 >= 0f && u1 <= 1f)
                                return u1;
                            else if (u2 >= 0f && u2 <= 1f)
                                return u2;
                            else if (u3 >= 0f && u3 <= 1f)
                                return u3;

                            // Aiming at solving numerical imprecisions when root is outside [0,1].
                            return (t < 0.5f) ? 0f : 1f;
                        }
                        else
                        {
                            float ss = math.sqrt(s);
                            float u  = CubeRoot(q + ss) + CubeRoot(q - ss) + p;

                            if (u >= 0f && u <= 1f)
                                return u;

                            // Aiming at solving numerical imprecisions when root is outside [0,1].
                            return (t < 0.5f) ? 0f : 1f;
                        }
                    }

                    if (math.abs(b) > 1e-3f)
                    {
                        float s  = c * c - 4f * b * d;
                        float ss = math.sqrt(s);

                        float u1 = (-c - ss) / (2f * b);
                        float u2 = (-c + ss) / (2f * b);

                        if (u1 >= 0f && u1 <= 1f)
                            return u1;
                        else if (u2 >= 0f && u2 <= 1f)
                            return u2;

                        // Aiming at solving numerical imprecisions when root is outside [0,1].
                        return (t < 0.5f) ? 0f : 1f;
                    }

                    if (math.abs(c) > 1e-3f)
                    {
                        return -d / c;
                    }

                    return 0f;
                }

                var dx = curve.rightTime - curve.leftTime;
                var t  = FindT(time - curve.leftTime / dx, curve.leftTangentWeight, 1f - curve.rightTangentWeight);
                var p0 = curve.leftValue;
                var p1 = curve.leftValue + dx * curve.leftTangentSlope * curve.leftTangentWeight;
                var p2 = curve.rightValue - dx * curve.rightTangentSlope * curve.rightTangentWeight;
                var p3 = curve.rightValue;

                float t2   = t * t;
                float t3   = t2 * t;
                float omt  = 1f - t;
                float omt2 = omt * omt;
                float omt3 = omt2 * omt;

                return omt3 * p0 + 3f * t * omt2 * p1 + 3f * t2 * omt * p2 + t3 * p3;
            }
        }
    }
}

