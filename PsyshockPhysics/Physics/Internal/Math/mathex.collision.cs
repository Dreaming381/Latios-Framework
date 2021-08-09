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
    }
}

