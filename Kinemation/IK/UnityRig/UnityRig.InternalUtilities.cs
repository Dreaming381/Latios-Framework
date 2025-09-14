using Unity.Mathematics;

namespace Latios.Kinemation
{
    public static partial class UnityRig
    {
        const float k_SqrEpsilon = 1e-8f;

        static float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            var c = math.clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (aLen1 * aLen2) / 2.0f, -1.0f, 1.0f);
            return math.acos(c);
        }

        static quaternion FromToRotation(float3 from, float3 to)
        {
            var teta = math.dot(math.normalize(from), math.normalize(to));
            if (teta >= 1f)
                return quaternion.identity;

            if (teta <= -1f)
            {
                var axis = math.cross(from, math.right());
                if (math.lengthsq(axis) == 0f)
                    axis = math.cross(from, math.up());

                return quaternion.AxisAngle(axis, math.PI);
            }

            return quaternion.AxisAngle(math.normalize(math.cross(from, to)), math.acos(teta));
        }
    }
}

