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
    }
}

