using Unity.Mathematics;

namespace Latios
{
    public static partial class simd
    {
        public static float4 dot(simdFloat3 a, float3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float4 dot(float3 a, simdFloat3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float4 dot(simdFloat3 a, simdFloat3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static simdFloat3 cross(simdFloat3 a, float3 b)
        {
            return new simdFloat3 { m_float3s = new float4x3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x) };
        }
        public static simdFloat3 cross(float3 a, simdFloat3 b)
        {
            return new simdFloat3 { m_float3s = new float4x3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x) };
        }
        public static simdFloat3 cross(simdFloat3 a, simdFloat3 b)
        {
            return new simdFloat3 { m_float3s = new float4x3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x) };
        }

        public static simdFloat3 transform(RigidTransform transform, simdFloat3 positions)
        {
            simdFloat3 t       = 2 * cross(transform.rot.value.xyz, positions);
            var        rotated = positions + transform.rot.value.w * t + cross(transform.rot.value.xyz, t);
            return rotated + transform.pos;
        }

        public static float3 cminabcd(simdFloat3 s)
        {
            return new float3(math.cmin(s.x), math.cmin(s.y), math.cmin(s.z));
        }

        public static float3 cmaxabcd(simdFloat3 s)
        {
            return new float3(math.cmax(s.x), math.cmax(s.y), math.cmax(s.z));
        }
    }
}

