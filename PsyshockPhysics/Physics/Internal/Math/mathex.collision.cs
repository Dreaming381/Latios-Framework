using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class mathex
    {
        //Formerly getFactoredLength
        public static float getLengthAndNormal(float3 v, out float3 normal)
        {
            float lengthSq  = math.lengthsq(v);
            float invLength = math.rsqrt(lengthSq);
            normal          = v * invLength;
            return lengthSq * invLength;
        }

        public static float4 getLengthAndNormal(simdFloat3 v, out simdFloat3 normal)
        {
            float4 lengthSq  = simd.lengthsq(v);
            float4 invLength = math.rsqrt(lengthSq);
            normal           = v * invLength;
            return lengthSq * invLength;
        }

        // From Unity's CalculatePerpendicularNormalized.
        // Todo: If input is unscaledNormal, which is tangent and which is bitangent?
        public static void getDualPerpendicularNormalized(float3 unsacledInput, out float3 perpendicularA, out float3 perpendicularB)
        {
            float3 v              = unsacledInput;
            float3 vSquared       = v * v;
            float3 lengthsSquared = vSquared + vSquared.xxx;  // y = ||j x v||^2, z = ||k x v||^2
            float3 invLengths     = math.rsqrt(lengthsSquared);

            // select first direction, j x v or k x v, whichever has greater magnitude
            float3 dir0 = new float3(-v.y, v.x, 0.0f);
            float3 dir1 = new float3(-v.z, 0.0f, v.x);
            bool   cmp  = (lengthsSquared.y > lengthsSquared.z);
            float3 dir  = math.select(dir1, dir0, cmp);

            // normalize and get the other direction
            float invLength = math.select(invLengths.z, invLengths.y, cmp);
            perpendicularA  = dir * invLength;
            float3 cross    = math.cross(v, dir);
            perpendicularB  = cross * invLength;
        }
    }
}

