using Latios.Transforms;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    public static partial class UnityRig
    {
        /// <summary>
        /// Performs the Unity Animation Rigging 2-Bone IK Algorithm. Scale and stretch are left untouched.
        /// All transforms must be in the same coordinate space (all root space, or all world space).
        /// </summary>
        /// <param name="root">The root-most bone in the 3-bone chain</param>
        /// <param name="mid">The middle bone in the 3-bone chain</param>
        /// <param name="tip">The final bone in the 3-bone chain that is directly drawn to the target</param>
        /// <param name="target">The target transfrom that influences the tip bone</param>
        /// <param name="hint">The hint position to guide the orientation of the 2 IK bones</param>
        /// <param name="positionWeight">The positional weight of the target in the range [0, 1]</param>
        /// <param name="rotationWeight">The rotational weight of the target in the range [0, 1]</param>
        /// <param name="hintWeight">The weight of the hint in the range [0, 1]. A value of 0 disables the hint.</param>
        public static void SolveTwoBoneIK(ref TransformQvvs root,
                                          ref TransformQvvs mid,
                                          ref TransformQvvs tip,
                                          in TransformQvvs target,
                                          float3 hint,
                                          float positionWeight,
                                          float rotationWeight,
                                          float hintWeight)
        {
            var aPosition = root.position;
            var bPosition = mid.position;
            var cPosition = tip.position;
            var targetPos = target.position;
            var targetRot = target.rotation;
            var tPosition = math.lerp(cPosition, targetPos, positionWeight);
            var tRotation = math.slerp(tip.rotation, targetRot, rotationWeight);
            var hasHint   = hintWeight > 0f;

            var ab = bPosition - aPosition;
            var bc = cPosition - bPosition;
            var ac = cPosition - aPosition;
            var at = tPosition - aPosition;

            var abLen = math.length(ab);
            var bcLen = math.length(bc);
            var acLen = math.length(ac);
            var atLen = math.length(at);

            var oldAbcAngle = TriangleAngle(acLen, abLen, bcLen);
            var newAbcAngle = TriangleAngle(atLen, abLen, bcLen);

            // Bend normal strategy is to take whatever has been provided in the animation
            // stream to minimize configuration changes, however if this is collinear
            // try computing a bend normal given the desired target position.
            // If this also fails, try resolving axis using hint if provided.
            var axis = math.cross(ab, bc);
            if (math.lengthsq(axis) < k_SqrEpsilon)
            {
                axis = hasHint ? math.cross(hint - aPosition, bc) : float3.zero;

                if (math.lengthsq(axis) < k_SqrEpsilon)
                    axis = math.cross(at, bc);

                if (math.lengthsq(axis) < k_SqrEpsilon)
                    axis = math.up();
            }
            axis = math.normalize(axis);

            var a        = 0.5f * (oldAbcAngle - newAbcAngle);
            var sin      = math.sin(a);
            var cos      = math.cos(a);
            var deltaR   = new quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
            mid.rotation = math.mul(deltaR, mid.rotation);

            cPosition     = tip.position;
            ac            = cPosition - aPosition;
            root.rotation = math.mul(FromToRotation(ac, at), root.rotation);

            if (hasHint)
            {
                var acSqrMag = math.lengthsq(ac);
                if (acSqrMag > 0f)
                {
                    bPosition = mid.position;
                    cPosition = tip.position;
                    ab        = bPosition - aPosition;
                    ac        = cPosition - aPosition;

                    var acNorm = ac / math.sqrt(acSqrMag);
                    var ah     = hint - aPosition;
                    var abProj = ab - acNorm * math.dot(ab, acNorm);
                    var ahProj = ah - acNorm * math.dot(ah, acNorm);

                    float maxReach = abLen + bcLen;
                    if (math.lengthsq(abProj) > maxReach * maxReach * 0.001f && math.lengthsq(ahProj) > 0)
                    {
                        var hintR        = FromToRotation(abProj, ahProj);
                        hintR.value.xyz *= hintWeight;
                        math.normalizesafe(hintR);
                        root.rotation = math.mul(hintR, root.rotation);
                    }
                }
            }

            tip.rotation = tRotation;
        }

        /// <summary>
        /// Performs the Unity Animation Rigging Algorithm for retrieving a hint position that would result in
        /// the current 2-Bone IK configuration.
        /// </summary>
        /// <param name="rootPosition">The root-most bone position in the 3-bone chain</param>
        /// <param name="midPosition">The middle bone position in the 3-bone chain</param>
        /// <param name="tipPosition">The final bone position in the 3-bone chain that is directly drawn to some target</param>
        /// <returns>The hint position that is used to guide the orientation of the 2 IK bones</returns>
        public static float3 SolveHintPositionForTwoBoneIK(float3 rootPosition, float3 midPosition, float3 tipPosition)
        {
            var ac = tipPosition - rootPosition;
            var ab = midPosition - rootPosition;
            var bc = tipPosition - midPosition;

            var abLen = math.length(ab);
            var bcLen = math.length(bc);

            var acSqrMag        = math.dot(ac, ac);
            var projectionPoint = rootPosition;
            if (acSqrMag > k_SqrEpsilon)
                projectionPoint     += math.dot(ab / acSqrMag, ac) * ac;
            var poleVectorDirection  = midPosition - projectionPoint;

            var scale = abLen + bcLen;
            return projectionPoint + math.normalize(poleVectorDirection) * scale;
        }
    }
}

