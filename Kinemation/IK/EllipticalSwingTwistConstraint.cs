using System;
using Latios.Psyshock;
using Latios.Transforms;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// A rotation constraint for a single bone for an IK solver.
    /// The constraint splits the rotation into a swing part and a twist part relative to a reference rotation
    /// within the bone's local space. The swing-part constrains the bone's axis direction to within an ellipse region
    /// projection on the unit sphere with the semi-major and semi-minor axes aligned to the other two axes of the
    /// reference rotation. The bone's axis serves as the twist axis which is independently constrained using min and
    /// max angles. Bone roll relative to the reference rotation should be incorporated into these min and max constraints.
    /// Which axis serves as the twist axis can be chosen using the ordinate index (0 = x, 1 = y, 2 = z).
    /// The bone behaves as if it was split into two, where one only swings and the other only twists. By default,
    /// the twist part behaves like a child of the swing part, but this can be swapped by setting twistBeforeSwing to true.
    /// </summary>
    public struct EllipticalSwingTwistConstraint
    {
        public quaternion referenceRotation;
        public float2     swingMaxSinHalfAngle;
        public float      twistMinSinHalfAngle;
        public float      twistMaxSinHalfAngle;
        public float      maxIterationAngleRadians;
        public byte       twistAxisIndex;
        public bool       isFixed;
        public bool       twistBeforeSwing;

        /// <summary>
        /// Creates an elliptical swing-twist constraint using authoring-friendly parameters.
        /// </summary>
        /// <param name="referenceRotation">The reference rotation in local space which defines the x, y, and z axes the constraint parameters are applied to</param>
        /// <param name="swingMaxDegrees">The elliptical semi-major and semi-minor halfAngles, where this argument's x coordinate corresponds to the first
        /// non-twist axis in the x-y-z sequence, and this argument's y coordinate corresponds to the second non-twist axis in the x-y-z sequence.
        /// These values must be in the range of [0, 180].</param>
        /// <param name="twistMinDegrees">The twist axis minimum angle in the range [-180, 180]</param>
        /// <param name="twistMaxDegrees">The twist axis maximum angle in the range [-180, 180]</param>
        /// <param name="maxIterationAngleDegrees">The maximum number of degrees the bone is allowed to rotate per solver iteration.
        /// Lower values decrease the likelihood of "popping".</param>
        /// <param name="twistAxisIndex">The twist axis index in the range of [0, 2]. Default is 2 (z axis)</param>
        /// <param name="twistBeforeSwing">If true, the twist rotation is propogated to the swing. Default is false.</param>
        /// <returns>A constraint for a bone in an optimized form which can be used in an IK solver.</returns>
        public static EllipticalSwingTwistConstraint FromDegrees(quaternion referenceRotation,
                                                                 float2 swingMaxDegrees,
                                                                 float twistMinDegrees,
                                                                 float twistMaxDegrees,
                                                                 float maxIterationAngleDegrees,
                                                                 int twistAxisIndex = 2,
                                                                 bool twistBeforeSwing = false)
        {
            var packed = new float4(swingMaxDegrees, twistMinDegrees, twistMaxDegrees);
            packed     = math.sin(math.radians(packed * 0.5f));
            return new EllipticalSwingTwistConstraint
            {
                referenceRotation        = referenceRotation,
                swingMaxSinHalfAngle     = packed.xy,
                twistMinSinHalfAngle     = packed.z,
                twistMaxSinHalfAngle     = packed.w,
                maxIterationAngleRadians = math.radians(maxIterationAngleDegrees),
                twistAxisIndex           = (byte)twistAxisIndex,
                isFixed                  = false,
                twistBeforeSwing         = twistBeforeSwing
            };
        }

        /// <summary>
        /// Creates an elliptical swing-twist constraint that represents a bone that is not allowed to rotate in local space.
        /// </summary>
        public static EllipticalSwingTwistConstraint FromFixed() => new EllipticalSwingTwistConstraint
        {
            isFixed = true
        };

        /// <summary>
        /// Evaluates the constraint for a given rotation in local space. maxIterationAngleRadians and isFixed are not accounted for
        /// and should instead be accounted for at the callsite.
        /// </summary>
        /// <param name="rotationToConstrain">A new local rotation that should be clamped to the swing-twist constraint</param>
        /// <returns>The clamped local rotation, or the original if no clamping was required</returns>
        public quaternion ApplyConstraint(quaternion rotationToConstrain)
        {
            // The is a modified implementation of the SwingTwistJointLimits here:
            // https://github.com/dtecta/motion-toolkit/blob/master/jointlimits/SwingTwistJointLimits.cpp
            // The license is as follows:
            // Copyright(c) 2006 Gino van den Bergen, DTECTA
            //
            // Permission is hereby granted, free of charge, to any person obtaining a copy
            // of this software and associated documentation files(the "Software"), to deal
            // in the Software without restriction, including without limitation the rights
            // to use, copy, modify, merge, publish, distribute, sublicense, and/ or sell
            // copies of the Software, and to permit persons to whom the Software is
            // furnished to do so, subject to the following conditions:
            //
            // The above copyright notice and this permission notice shall be included in
            // all copies or substantial portions of the Software.
            //
            // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            // IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
            // AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            // LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
            // THE SOFTWARE.
            var q = math.InverseRotateFast(referenceRotation, rotationToConstrain).value;
            q     = math.select(q, -q, q.w < 0f);

            // Rotate ordinates so that x is twist axis to match reference implementation
            float2 swingMax = swingMaxSinHalfAngle;
            if (twistAxisIndex == 2)
            {
                q.yzx = q.xyz;
            }
            else if (twistAxisIndex == 1)
            {
                q.zxy    = q.xyz;
                swingMax = swingMax.yx;
            }

            // Here swing and twist are dependent. The twist can be applied before or after the swing. After (parent ->swing -> twist -> child) makes the most sense
            float rx, ry, rz;
            float s = q.x * q.x + q.w * q.w;
            if (s < math.EPSILON)
            {
                // swing by 180 degrees is a singularity. We assume twist is zero.
                rx = 0;
                ry = q.y;
                rz = q.z;
            }
            else
            {
                float r = math.rsqrt(s);

                rx = q.x * r;

                if (twistBeforeSwing)
                {
                    ry = (q.w * q.y + q.x * q.z) * r;
                    rz = (q.w * q.z - q.x * q.y) * r;
                }
                else
                {
                    ry = (q.w * q.y - q.x * q.z) * r;
                    rz = (q.w * q.z + q.x * q.y) * r;
                }
            }

            var old = new float3(rx, ry, rz);

            rx = math.clamp(rx, twistMinSinHalfAngle, twistMaxSinHalfAngle);

            var testPoint = new float2(ry, rz);
            if (!QuickTests.IsOnOrInsideEllipse(swingMax, testPoint))
            {
                var clamped = QuickTests.ClosestPointOnEllipse(swingMax, testPoint);
                ry          = clamped.x;
                rz          = clamped.y;
            }

            // Note: I added this early-out
            if (old.Equals(new float3(rx, ry, rz)))
                return rotationToConstrain;

            quaternion qTwist = new quaternion(rx, 0, 0, math.sqrt(math.max(0, 1 - rx * rx)));
            quaternion qSwing = new quaternion(0, ry, rz, math.sqrt(math.max(0, 1 - ry * ry - rz * rz)));

            quaternion qResult;
            if (twistBeforeSwing)
                qResult = math.mul(qTwist, qSwing);
            else
                qResult = math.mul(qSwing, qTwist);

            // Flip back
            if (twistAxisIndex == 2)
            {
                qResult.value.xyz = qResult.value.yzx;
            }
            else if (twistAxisIndex == 1)
            {
                qResult.value.xyz = qResult.value.zxy;
            }
            return math.mul(referenceRotation, qResult);
        }
    }

    /// <summary>
    /// A constraint solver for the EWBIK alogorithm that uses an EllipticalSwingTwistConstraint for each bone.
    /// </summary>
    public struct EllipticalSwingTwistEwbikSolver : Ewbik.IConstraintSolver
    {
        /// <summary>
        /// The array of constraints, with each index corresponding to the bone index it applies to
        /// </summary>
        public UnsafeList<EllipticalSwingTwistConstraint> constraints;
        /// <summary>
        /// The maximum number of full-skeleton iterations the solver will execute before terminating. If a full-skeleton iteration
        /// fails to rotate any bone, the solver will terminate early.
        /// </summary>
        public int maxIterations;

        bool madeBoneChange;

        bool Ewbik.IConstraintSolver.ApplyConstraintsToBone(OptimizedBone bone, in RigidTransform proposedTransformDelta, in Ewbik.BoneSolveState boneSolveState)
        {
            var newLocalRotation   = math.mul(proposedTransformDelta.rot, bone.rootRotation);
            var parentRootRotation = bone.index > 0 ? bone.parent.rootRotation : quaternion.identity;
            newLocalRotation       = math.InverseRotateFast(parentRootRotation, newLocalRotation);
            newLocalRotation       = constraints[bone.index].ApplyConstraint(newLocalRotation);
            var oldLocalRotation   = bone.localRotation;
            var newAngle           = math.angle(oldLocalRotation, newLocalRotation);
            if (newAngle <= math.EPSILON)
                return false;
            var maxAngle = constraints[bone.index].maxIterationAngleRadians;
            if (newAngle > maxAngle)
                newLocalRotation = math.slerp(oldLocalRotation, newLocalRotation, maxAngle / newAngle);
            // Make sure that after all out damping and constraints, that we actually got closer to the solution.
            // If not, we don't apply the transform. For this, we need to rederive our delta from our new local rotation.
            // newRoot = delta * parent * local
            // newRoot * (parent * local)^-1 = delta * (parent * local) * (parent * local)^-1
            // newRoot * (parent * local)^-1 = delta
            // newRoot = parent * newLocal
            // parent * newLocal * (parent * local)^-1 = delta
            var correctedDelta = math.normalize(math.mul(parentRootRotation, math.mul(newLocalRotation, math.conjugate(bone.rootRotation))));
            var oldMsd         = boneSolveState.MeanSquareDistanceFrom(TransformQvvs.identity);
            var newMsd         = boneSolveState.MeanSquareDistanceFrom(new TransformQvvs(float3.zero, correctedDelta));
            if (newMsd < oldMsd)
            {
                bone.localRotation = newLocalRotation;
                madeBoneChange     = true;
            }
            return false;
        }

        bool Ewbik.IConstraintSolver.IsFixedToParent(OptimizedBone bone)
        {
            return constraints[bone.index].isFixed;
        }

        bool Ewbik.IConstraintSolver.NeedsSkeletonIteration(OptimizedSkeletonAspect skeleton, ReadOnlySpan<Ewbik.Target> sortedTargets, int iterationsPerformedSoFar)
        {
            if (iterationsPerformedSoFar >= maxIterations)
                return false;
            if (iterationsPerformedSoFar == 0)
            {
                madeBoneChange = false;
                return true;
            }
            if (!madeBoneChange)
                return false;
            madeBoneChange = false;
            return true;
        }

        bool Ewbik.IConstraintSolver.UseTranslationInSolve(OptimizedBone bone, int iterationsSoFarForThisBone)
        {
            return false;
        }
    }
}

