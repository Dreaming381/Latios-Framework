using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Recommended resources:
// https://cdn.gearsofwar.com/thecoalition/publications/GDC%202018%20-%20Inertialization%20-%20High%20Performance%20Animation%20Transitions%20in%20Gears%20of%20War.pdf
// https://github.com/Unity-Technologies/Unity.Animation.Samples/blob/master/UnityAnimationHDRPExamples/Assets/Scenes/Advanced/InertialMotionBlending/InertialBlendingNode.cs
// The Unity.Animation 0.9.0 package

namespace Latios.Kinemation
{
    /// <summary>
    /// The inertial blending states per bone in the optimized skeleton.
    /// Usage: Prefer to use OptimizedSkeletonAspect instead of this component directly.
    /// </summary>
    public struct OptimizedBoneInertialBlendState : IBufferElementData
    {
        public InertialBlendingTransformState inertialBlendState;
    }

    /// <summary>
    /// A structure used for performing inertial blending.
    /// You can reuse an instance for multiple inertial blending operations
    /// if they all have the same blendProgressTime.
    /// </summary>
    public struct InertialBlendingTimingData
    {
        internal float t;
        internal float t2;
        internal float t3;
        internal float t4;
        internal float t5;

        /// <summary>
        /// Creates the InertialBlendingTimingData using the blendProgressTime.
        /// </summary>
        /// <param name="blendProgressTime">The blendProgressTime is measured from the time of the
        /// "previous pose" when the blend was started.</param>
        public InertialBlendingTimingData(float blendProgressTime)
        {
            t  = blendProgressTime;
            t2 = t * t;
            t3 = t2 * t;
            t4 = t3 * t;
            t5 = t4 * t;
        }
    }

    /// <summary>
    /// Contains the state and utility methods for starting and performing inertial blending on
    /// a TransformQvvs instance.
    /// </summary>
    public struct InertialBlendingTransformState
    {
        internal float4 coeffA;
        internal float4 coeffB;
        internal float4 coeffC;
        internal float4 coeffD;
        internal float4 coeffE;
        internal float4 coeffF;
        internal float4 durations;  // Needed in-case something is not finite, in which F/E becomes 0/0

        internal float3 rotationAxis;  // rotation
        internal float3 direction;  // position
        internal float3 expansion;  // stretch

        /// <summary>
        /// Start a new inertial blend operation
        /// </summary>
        /// <param name="current">The new transform state that may be wildly disconnected from previous transform states</param>
        /// <param name="previous">The previous transform state, often the "tick starting" state</param>
        /// <param name="twoAgo">The transform state before the previous, often the "previous tick starting" state</param>
        /// <param name="rcpDeltaTimeBetweenPreviousAndTwoAgo">One divided by the delta time between the transform from two-ago
        /// and the previous transform. This is typically the previous frame's deltaTime, though some may choose to approximate it
        /// with the current frame's deltaTime.</param>
        /// <param name="maxBlendDurationStartingFromTimeOfPrevious">How long the blend should last at maximum, starting from when
        /// the previous pose was sampled. This is equivalent to "time from now" + deltaTime.</param>
        public void StartNewBlend(in TransformQvvs current,
                                  in TransformQvvs previous,
                                  in TransformQvvs twoAgo,
                                  float rcpDeltaTimeBetweenPreviousAndTwoAgo,
                                  float maxBlendDurationStartingFromTimeOfPrevious)
        {
            // Todo: Unity.Animation has custom methods for handling quaternions.
            // Should we be using those implementations instead?

            float4 x0s;
            float4 v0s;

            {
                var currentInverse = math.conjugate(current.rotation);
                // Todo: Is this normalization necessary?
                // Also, can we please get a ToAngleAxis equivalent in Unity.Mathematics?
                UnityEngine.Quaternion q0 = math.normalize(math.mul(previous.rotation, currentInverse));
                q0.ToAngleAxis(out var q0AngleDegrees, out var q0Angle);
                rotationAxis = q0Angle;
                x0s.x        = q0AngleDegrees / UnityEngine.Mathf.Rad2Deg;

                // Ensure that rotations are the shortest possible
                if (x0s.x > math.PI)
                {
                    x0s.x        = 2f * math.PI - x0s.x;
                    rotationAxis = -rotationAxis;
                }

                var q1  = math.normalize(math.mul(twoAgo.rotation, currentInverse));
                var x1  = 2 * math.atan2(math.dot(q1.value.xyz, rotationAxis), q1.value.w);
                x1      = math.select(x1, x1 - 2 * math.PI, x1 > math.PI);
                x1      = math.select(x1, x1 + 2 * math.PI, x1 < math.PI);
                v0s.x   = x0s.x - x1;
                v0s.x   = math.select(v0s.x, v0s.x - 2 * math.PI, v0s.x > math.PI);
                v0s.x   = math.select(v0s.x, v0s.x + 2 * math.PI, v0s.x < math.PI);
                v0s.x  *= rcpDeltaTimeBetweenPreviousAndTwoAgo;
            }

            {
                var x0vec = previous.position - current.position;
                x0s.y     = math.length(x0vec);
                direction = math.normalizesafe(x0vec, float3.zero);
                var x1vec = twoAgo.position - current.position;
                var x1    = math.dot(x1vec, direction);
                v0s.y     = (x0s.y - x1) * rcpDeltaTimeBetweenPreviousAndTwoAgo;
            }

            {
                var x0vec = previous.stretch - current.stretch;
                x0s.z     = math.length(x0vec);
                expansion = math.normalizesafe(x0vec, float3.zero);
                var x1vec = twoAgo.stretch - current.stretch;
                var x1    = math.dot(x1vec, expansion);
                v0s.z     = (x0s.z - x1) * rcpDeltaTimeBetweenPreviousAndTwoAgo;
            }

            {
                x0s.w = previous.scale - current.scale;
                v0s.w = (previous.scale - twoAgo.scale) * rcpDeltaTimeBetweenPreviousAndTwoAgo;
            }

            // Coefficients in SIMD
            {
                float4 sign  = math.sign(x0s);
                x0s          = math.abs(x0s);
                v0s         *= sign;

                v0s = math.select(v0s, -0f, v0s >= 0);

                var maxDuration = -5f * x0s / v0s;
                durations       = math.min(maxBlendDurationStartingFromTimeOfPrevious, maxDuration);
                durations       = math.select(durations, maxBlendDurationStartingFromTimeOfPrevious, !math.isfinite(durations));

                var durations2 = durations * durations;
                var durations3 = durations * durations2;
                var durations4 = durations * durations3;
                var durations5 = durations * durations4;
                var numX       = v0s * durations;

                var rawCoeffA0 = math.select(k_CoefficientsAccelerationNonzero.c0.x,
                                             k_CoefficientsAccelerationZero.c0.x,
                                             2f * durations < maxBlendDurationStartingFromTimeOfPrevious);
                var rawCoeffA1 = math.select(k_CoefficientsAccelerationNonzero.c1.x,
                                             k_CoefficientsAccelerationZero.c1.x,
                                             2f * durations < maxBlendDurationStartingFromTimeOfPrevious);
                coeffA = sign * (rawCoeffA0 * numX + rawCoeffA1 * x0s) / durations5;

                var rawCoeffB0 = math.select(k_CoefficientsAccelerationNonzero.c0.y,
                                             k_CoefficientsAccelerationZero.c0.y,
                                             2f * durations < maxBlendDurationStartingFromTimeOfPrevious);
                var rawCoeffB1 = math.select(k_CoefficientsAccelerationNonzero.c1.y,
                                             k_CoefficientsAccelerationZero.c1.y,
                                             2f * durations < maxBlendDurationStartingFromTimeOfPrevious);
                coeffB = sign * (rawCoeffB0 * numX + rawCoeffB1 * x0s) / durations4;

                var rawCoeffC0 = math.select(k_CoefficientsAccelerationNonzero.c0.z,
                                             k_CoefficientsAccelerationZero.c0.z,
                                             2f * durations < maxBlendDurationStartingFromTimeOfPrevious);
                var rawCoeffC1 = math.select(k_CoefficientsAccelerationNonzero.c1.z,
                                             k_CoefficientsAccelerationZero.c1.z,
                                             2f * durations < maxBlendDurationStartingFromTimeOfPrevious);
                coeffC = sign * (rawCoeffC0 * numX + rawCoeffC1 * x0s) / durations3;

                var rawCoeffD0 = math.select(k_CoefficientsAccelerationNonzero.c0.w,
                                             k_CoefficientsAccelerationZero.c0.w,
                                             2f * durations < maxBlendDurationStartingFromTimeOfPrevious);
                var rawCoeffD1 = math.select(k_CoefficientsAccelerationNonzero.c1.w,
                                             k_CoefficientsAccelerationZero.c1.w,
                                             2f * durations < maxBlendDurationStartingFromTimeOfPrevious);
                coeffD = sign * (rawCoeffD0 * numX + rawCoeffD1 * x0s) / durations2;

                var finites = math.isfinite(coeffA) & math.isfinite(coeffB) & math.isfinite(coeffC) & math.isfinite(coeffD);
                coeffA      = math.select(0f, coeffA, finites);
                coeffB      = math.select(0f, coeffB, finites);
                coeffC      = math.select(0f, coeffC, finites);
                coeffD      = math.select(0f, coeffD, finites);
                coeffE      = math.select(0f, sign * v0s, finites);
                coeffF      = math.select(0f, sign * x0s, finites);
                durations   = math.select(0f, durations, finites);
            }
        }

        /// <summary>
        /// Performs the inertial blending corrections to a transform using the current blend state
        /// and the timingData.
        /// </summary>
        /// <param name="current">The transform to smooth out with inertial blending</param>
        /// <param name="timingData">Timing data relative to the start of the inertial blend operation</param>
        public void Blend(ref TransformQvvs current, in InertialBlendingTimingData timingData)
        {
            var factors = timingData.t5 * coeffA + timingData.t4 * coeffB + timingData.t3 * coeffC +
                          timingData.t2 * coeffD + timingData.t * coeffE + coeffF;
            factors = math.select(0f, factors, timingData.t < durations);

            current.rotation  = math.mul(quaternion.AxisAngle(rotationAxis, factors.x), current.rotation);
            current.position += factors.y * direction;
            current.stretch  += factors.z * expansion;
            current.scale    += factors.w;
        }

        static readonly float4x2 k_CoefficientsAccelerationZero = math.float4x2(
            -3f, -6f,
            8f, 15f,
            -6f, -10f,
            0f, 0f);

        static readonly float4x2 k_CoefficientsAccelerationNonzero = math.float4x2(
            1f, 4f,
            -4f, -15f,
            6f, 20f,
            -4f, -10f);
    }
}

