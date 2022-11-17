using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    public static class RngToolkit
    {
        public static bool AsBool(uint u)
        {
            return (u & 0x1) == 0;
        }

        public static bool2 AsBool2(uint u)
        {
            return (new uint2(u) & new uint2(0x1, 0x2)) == 0;
        }

        public static bool3 AsBool3(uint u)
        {
            return (new uint3(u) & new uint3(0x1, 0x2, 0x4)) == 0;
        }

        public static bool4 AsBool4(uint u)
        {
            return (new uint4(u) & new uint4(0x1, 0x2, 0x4, 0x8)) == 0;
        }

        public static int AsInt(uint u)
        {
            return math.asint(u);
        }

        public static int2 AsInt2(uint2 u)
        {
            return math.asint(u);
        }

        public static int3 AsInt3(uint3 u)
        {
            return math.asint(u);
        }

        public static int4 AsInt4(uint4 u)
        {
            return math.asint(u);
        }

        public static int AsInt(uint u, int min, int max)
        {
            CheckIntMinMax(min, max);
            uint range = (uint)(max - min);
            return (int)(u * (ulong)range >> 32) + min;
        }

        public static int2 AsInt2(uint2 u, int2 min, int2 max)
        {
            CheckIntMinMax(min.xyxy, max.xyxy);
            uint2 range = (uint2)(max - min);
            return new int2((int)(u.x * (ulong)range.x >> 32),
                            (int)(u.y * (ulong)range.y >> 32)) + min;
        }

        public static int3 AsInt3(uint3 u, int3 min, int3 max)
        {
            CheckIntMinMax(min.xyzx, max.xyzx);
            uint3 range = (uint3)(max - min);
            return new int3((int)(u.x * (ulong)range.x >> 32),
                            (int)(u.y * (ulong)range.y >> 32),
                            (int)(u.z * (ulong)range.z >> 32)) + min;
        }

        public static int4 AsInt4(uint4 u, int4 min, int4 max)
        {
            CheckIntMinMax(min, max);
            uint4 range = (uint4)(max - min);
            return new int4((int)(u.x * (ulong)range.x >> 32),
                            (int)(u.y * (ulong)range.y >> 32),
                            (int)(u.z * (ulong)range.z >> 32),
                            (int)(u.w * (ulong)range.w >> 32)) + min;
        }

        public static uint AsUInt(uint u, uint min, uint max)
        {
            CheckUIntMinMax(min, max);
            uint range = max - min;
            return (uint)(u * (ulong)range >> 32) + min;
        }

        public static uint2 AsUInt2(uint2 u, uint2 min, uint2 max)
        {
            CheckUIntMinMax(min.xyxy, max.xyxy);
            uint2 range = max - min;
            return new uint2((uint)(u.x * (ulong)range.x >> 32),
                             (uint)(u.y * (ulong)range.y >> 32)) + min;
        }

        public static uint3 AsUInt3(uint3 u, uint3 min, uint3 max)
        {
            CheckUIntMinMax(min.xyzx, max.xyzx);
            uint3 range = max - min;
            return new uint3((uint)(u.x * (ulong)range.x >> 32),
                             (uint)(u.y * (ulong)range.y >> 32),
                             (uint)(u.z * (ulong)range.z >> 32)) + min;
        }

        public static uint4 AsUInt4(uint4 u, uint4 min, uint4 max)
        {
            CheckUIntMinMax(min, max);
            uint4 range = max - min;
            return new uint4((uint)(u.x * (ulong)range.x >> 32),
                             (uint)(u.y * (ulong)range.y >> 32),
                             (uint)(u.z * (ulong)range.z >> 32),
                             (uint)(u.w * (ulong)range.w >> 32)) + min;
        }

        public static float AsFloat(uint u)
        {
            return math.asfloat(0x3f800000 | (u >> 9)) - 1.0f;
        }

        public static float2 AsFloat2(uint2 u)
        {
            return math.asfloat(0x3f800000 | (u >> 9)) - 1.0f;
        }

        public static float3 AsFloat3(uint3 u)
        {
            return math.asfloat(0x3f800000 | (u >> 9)) - 1.0f;
        }

        public static float4 AsFloat4(uint4 u)
        {
            return math.asfloat(0x3f800000 | (u >> 9)) - 1.0f;
        }

        public static float AsFloat(uint u, float min, float max)
        {
            CheckFloatMinMax(min, max);
            return AsFloat(u) * (max - min) + min;
        }

        public static float2 AsFloat2(uint2 u, float2 min, float2 max)
        {
            CheckFloatMinMax(min.xyxy, max.xyxy);
            return AsFloat2(u) * (max - min) + min;
        }

        public static float3 AsFloat3(uint3 u, float3 min, float3 max)
        {
            CheckFloatMinMax(min.xyzx, max.xyzx);
            return AsFloat3(u) * (max - min) + min;
        }

        public static float4 AsFloat4(uint4 u, float4 min, float4 max)
        {
            CheckFloatMinMax(min, max);
            return AsFloat4(u) * (max - min) + min;
        }

        // Todo: There has to be a way to avoid trig in these
        public static float2 AsFloat2Direction(uint u)
        {
            float angle = AsFloat(u) * math.PI * 2.0f;
            math.sincos(angle, out float s, out float c);
            return new float2(c, s);
        }

        public static float3 AsFloat3Direction(uint2 u)
        {
            float2 rnd   = AsFloat2(u);
            float  z     = rnd.x * 2.0f - 1.0f;
            float  r     = math.sqrt(math.max(1.0f - z * z, 0.0f));
            float  angle = rnd.y * math.PI * 2.0f;
            math.sincos(angle, out float s, out float c);
            return new float3(c * r, s * r, z);
        }

        public static quaternion AsQuaternionRotation(uint3 u)
        {
            float3 rnd       = AsFloat3(u, 0f, new float3(2.0f * math.PI, 2.0f * math.PI, 1.0f));
            float  u1        = rnd.z;
            float2 theta_rho = rnd.xy;

            float i = math.sqrt(1.0f - u1);
            float j = math.sqrt(u1);

            float2 sin_theta_rho;
            float2 cos_theta_rho;
            math.sincos(theta_rho, out sin_theta_rho, out cos_theta_rho);

            quaternion q = new quaternion(i * sin_theta_rho.x, i * cos_theta_rho.x, j * sin_theta_rho.y, j * cos_theta_rho.y);
            return new quaternion(math.select(q.value, -q.value, q.value.w < 0.0f));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckIntMinMax(int4 min, int4 max)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (math.any(min > max))
                throw new System.ArgumentException("min must be less than or equal to max");
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckUIntMinMax(uint4 min, uint4 max)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (math.any(min > max))
                throw new System.ArgumentException("min must be less than or equal to max");
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckFloatMinMax(float4 min, float4 max)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (math.any(min > max))
                throw new System.ArgumentException("min must be less than or equal to max");
#endif
        }
    }
}

