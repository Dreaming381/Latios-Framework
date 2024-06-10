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

        public static int AsInt(uint u, int minInclusive, int maxExclusive)
        {
            CheckIntMinMax(minInclusive, maxExclusive);
            uint range = (uint)(maxExclusive - minInclusive);
            return (int)(u * (ulong)range >> 32) + minInclusive;
        }

        public static int2 AsInt2(uint2 u, int2 minInclusive, int2 maxExclusive)
        {
            CheckIntMinMax(minInclusive.xyxy, maxExclusive.xyxy);
            uint2 range = (uint2)(maxExclusive - minInclusive);
            return new int2((int)(u.x * (ulong)range.x >> 32),
                            (int)(u.y * (ulong)range.y >> 32)) + minInclusive;
        }

        public static int3 AsInt3(uint3 u, int3 minInclusive, int3 maxExclusive)
        {
            CheckIntMinMax(minInclusive.xyzx, maxExclusive.xyzx);
            uint3 range = (uint3)(maxExclusive - minInclusive);
            return new int3((int)(u.x * (ulong)range.x >> 32),
                            (int)(u.y * (ulong)range.y >> 32),
                            (int)(u.z * (ulong)range.z >> 32)) + minInclusive;
        }

        public static int4 AsInt4(uint4 u, int4 minInclusive, int4 maxExclusive)
        {
            CheckIntMinMax(minInclusive, maxExclusive);
            uint4 range = (uint4)(maxExclusive - minInclusive);
            return new int4((int)(u.x * (ulong)range.x >> 32),
                            (int)(u.y * (ulong)range.y >> 32),
                            (int)(u.z * (ulong)range.z >> 32),
                            (int)(u.w * (ulong)range.w >> 32)) + minInclusive;
        }

        public static uint AsUInt(uint u, uint minInclusive, uint maxExclusive)
        {
            CheckUIntMinMax(minInclusive, maxExclusive);
            uint range = maxExclusive - minInclusive;
            return (uint)(u * (ulong)range >> 32) + minInclusive;
        }

        public static uint2 AsUInt2(uint2 u, uint2 minInclusive, uint2 maxExclusive)
        {
            CheckUIntMinMax(minInclusive.xyxy, maxExclusive.xyxy);
            uint2 range = maxExclusive - minInclusive;
            return new uint2((uint)(u.x * (ulong)range.x >> 32),
                             (uint)(u.y * (ulong)range.y >> 32)) + minInclusive;
        }

        public static uint3 AsUInt3(uint3 u, uint3 minInclusive, uint3 maxExclusive)
        {
            CheckUIntMinMax(minInclusive.xyzx, maxExclusive.xyzx);
            uint3 range = maxExclusive - minInclusive;
            return new uint3((uint)(u.x * (ulong)range.x >> 32),
                             (uint)(u.y * (ulong)range.y >> 32),
                             (uint)(u.z * (ulong)range.z >> 32)) + minInclusive;
        }

        public static uint4 AsUInt4(uint4 u, uint4 minInclusive, uint4 maxExclusive)
        {
            CheckUIntMinMax(minInclusive, maxExclusive);
            uint4 range = maxExclusive - minInclusive;
            return new uint4((uint)(u.x * (ulong)range.x >> 32),
                             (uint)(u.y * (ulong)range.y >> 32),
                             (uint)(u.z * (ulong)range.z >> 32),
                             (uint)(u.w * (ulong)range.w >> 32)) + minInclusive;
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

        public static float AsFloat(uint u, float minInclusive, float maxExclusive)
        {
            return AsFloat(u) * (maxExclusive - minInclusive) + minInclusive;
        }

        public static float2 AsFloat2(uint2 u, float2 minInclusive, float2 maxExclusive)
        {
            return AsFloat2(u) * (maxExclusive - minInclusive) + minInclusive;
        }

        public static float3 AsFloat3(uint3 u, float3 minInclusive, float3 maxExclusive)
        {
            return AsFloat3(u) * (maxExclusive - minInclusive) + minInclusive;
        }

        public static float4 AsFloat4(uint4 u, float4 minInclusive, float4 maxExclusive)
        {
            return AsFloat4(u) * (maxExclusive - minInclusive) + minInclusive;
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
        private static void CheckIntMinMax(int4 minInclusive, int4 maxExclusive)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (math.any(minInclusive > maxExclusive))
                throw new System.ArgumentException("minInclusive must be less than or equal to maxExclusive");
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckUIntMinMax(uint4 minInclusive, uint4 maxExclusive)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (math.any(minInclusive > maxExclusive))
                throw new System.ArgumentException("minInclusive must be less than or equal to maxExclusive");
#endif
        }
    }
}

