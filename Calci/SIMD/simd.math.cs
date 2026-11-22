using Unity.Mathematics;

namespace Latios.Calci
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

        public static float4 distancesq(simdFloat3 a, float3 b) => lengthsq(a - b);
        public static float4 distancesq(float3 a, simdFloat3 b) => lengthsq(a - b);
        public static float4 distancesq(simdFloat3 a, simdFloat3 b) => lengthsq(a - b);

        public static float4 distance(simdFloat3 a, simdFloat3 b) => length(a - b);
        public static float4 distance(float3 a, simdFloat3 b) => length(a - b);
        public static float4 distance(simdFloat3 a, float3 b) => length(a - b);

        public static float4 lengthsq(simdFloat3 a) => dot(a, a);
        public static float4 length(simdFloat3 a) => math.sqrt(dot(a, a));

        public static simdFloat3 normalize(simdFloat3 x) {
            return math.rsqrt(dot(x, x)) * x;
        }

        public static simdFloat3 normalizesafe(simdFloat3 x, float3 defaultvalue = new float3())
        {
            var len = dot(x, x);
            return select(new simdFloat3(defaultvalue), x * math.rsqrt(len), len > math.FLT_MIN_NORMAL);
        }

        public static simdFloat3 select(simdFloat3 a, simdFloat3 b, bool4 c)
        {
            simdFloat3 result = default;
            result.x          = math.select(a.x, b.x, c);
            result.y          = math.select(a.y, b.y, c);
            result.z          = math.select(a.z, b.z, c);
            return result;
        }

        public static simdFloat3 select(simdFloat3 a, simdFloat3 b, bool3 c)
        {
            simdFloat3 result = default;
            result.x          = math.select(a.x, b.x, c.x);
            result.y          = math.select(a.y, b.y, c.y);
            result.z          = math.select(a.z, b.z, c.z);
            return result;
        }

        public static simdFloat3 select(simdFloat3 a, simdFloat3 b, bool4 xSelect, bool4 ySelect, bool4 zSelect)
        {
            simdFloat3 result = default;
            result.x          = math.select(a.x, b.x, xSelect);
            result.y          = math.select(a.y, b.y, ySelect);
            result.z          = math.select(a.z, b.z, zSelect);
            return result;
        }

        /*public static float3 shuffle(simdFloat3 left, simdFloat3 right, math.ShuffleComponent shuffleA)
           {
            float3 result = default;
            result.x      = math.shuffle(left.x, right.x, shuffleA);
            result.y      = math.shuffle(left.y, right.y, shuffleA);
            result.z      = math.shuffle(left.z, right.z, shuffleA);
            return result;
           }

           public static simdFloat3 shuffle(simdFloat3 left,
                                         simdFloat3 right,
                                         math.ShuffleComponent shuffleA,
                                         math.ShuffleComponent shuffleB,
                                         math.ShuffleComponent shuffleC,
                                         math.ShuffleComponent shuffleD)
           {
            simdFloat3 result = default;
            result.x          = math.shuffle(left.x, right.x, shuffleA, shuffleB, shuffleC, shuffleD);
            result.y          = math.shuffle(left.y, right.y, shuffleA, shuffleB, shuffleC, shuffleD);
            result.z          = math.shuffle(left.z, right.z, shuffleA, shuffleB, shuffleC, shuffleD);
            return result;
           }*/

        public static float3 shuffle(simdFloat3 left, simdFloat3 right, math.ShuffleComponent shuffleA)
        {
            int  code     = (int)shuffleA;
            bool useRight = code > 3;
            int  index    = code & 3;

            float3 result = default;
            result.x      = math.select(left.x, right.x, useRight)[index];
            result.y      = math.select(left.y, right.y, useRight)[index];
            result.z      = math.select(left.z, right.z, useRight)[index];
            return result;
        }

        public static simdFloat3 shuffle(simdFloat3 left,
                                         simdFloat3 right,
                                         math.ShuffleComponent shuffleA,
                                         math.ShuffleComponent shuffleB,
                                         math.ShuffleComponent shuffleC,
                                         math.ShuffleComponent shuffleD)
        {
            int4  code     = new int4((int)shuffleA, (int)shuffleB, (int)shuffleC, (int)shuffleD);
            bool4 useRight = code > 3;
            int4  index    = code & 3;

            simdFloat3 result = default;
            float4     l      = new float4(left.x[index.x], left.x[index.y], left.x[index.z], left.x[index.w]);
            float4     r      = new float4(right.x[index.x], right.x[index.y], right.x[index.z], right.x[index.w]);
            result.x          = math.select(l, r, useRight);
            l                 = new float4(left.y[index.x], left.y[index.y], left.y[index.z], left.y[index.w]);
            r                 = new float4(right.y[index.x], right.y[index.y], right.y[index.z], right.y[index.w]);
            result.y          = math.select(l, r, useRight);
            l                 = new float4(left.z[index.x], left.z[index.y], left.z[index.z], left.z[index.w]);
            r                 = new float4(right.z[index.x], right.z[index.y], right.z[index.z], right.z[index.w]);
            result.z          = math.select(l, r, useRight);
            return result;
        }

        public static simdFloat3 rotate(quaternion rotation, simdFloat3 directions) => mul(rotation, directions);

        public static simdFloat3 mul(quaternion rotation, simdFloat3 directions)
        {
            simdFloat3 t = 2 * cross(rotation.value.xyz, directions);
            return directions + rotation.value.w * t + cross(rotation.value.xyz, t);
        }

        public static simdFloat3 rotate(float3x3 matrix, simdFloat3 directions) => mul(matrix, directions);

        public static simdFloat3 mul(float3x3 matrix, simdFloat3 directions)
        {
            return new simdFloat3(matrix.c0) * directions.x + new simdFloat3(matrix.c1) * directions.y + new simdFloat3(matrix.c2) * directions.z;
        }

        public static simdFloat3 inverseRotate(float3x3 matrixToInverse, simdFloat3 directions)
        {
            return new simdFloat3(dot(matrixToInverse.c0, directions), dot(matrixToInverse.c1, directions), dot(matrixToInverse.c2, directions));
        }

        public static simdFloat3 transform(RigidTransform transform, simdFloat3 positions)
        {
            simdFloat3 t       = 2 * cross(transform.rot.value.xyz, positions);
            var        rotated = positions + transform.rot.value.w * t + cross(transform.rot.value.xyz, t);
            return rotated + transform.pos;
        }

        public static simdFloat3 transform(AffineTransform transform, simdFloat3 positions)
        {
            return mul(transform.rs, positions) + transform.t;
        }

        public static simdFloat3 inverseTransformIdentityScale(AffineTransform transformToInverse, simdFloat3 positions)
        {
            return inverseRotate(transformToInverse.rs, positions - transformToInverse.t);
        }

        public static float3 cminabcd(simdFloat3 s)
        {
            return new float3(math.cmin(s.x), math.cmin(s.y), math.cmin(s.z));
        }

        public static float3 cmaxabcd(simdFloat3 s)
        {
            return new float3(math.cmax(s.x), math.cmax(s.y), math.cmax(s.z));
        }

        public static float4 cminxyz(simdFloat3 s)
        {
            return math.min(math.min(s.x, s.y), s.z);
        }

        public static float4 cmaxxyz(simdFloat3 s)
        {
            return math.max(math.max(s.x, s.y), s.z);
        }

        public static float3 csumabcd(simdFloat3 s)
        {
            return new float3(math.csum(s.x), math.csum(s.y), math.csum(s.z));
        }

        public static simdFloat3 abs(simdFloat3 s)
        {
            return new simdFloat3(math.abs(s.x), math.abs(s.y), math.abs(s.z));
        }

        public static simdFloat3 project(simdFloat3 a, float3 target)
        {
            return (dot(a, target) / math.dot(target, target)) * new simdFloat3(target);
        }

        public static bool4 isfiniteallxyz(simdFloat3 s)
        {
            return math.isfinite(s.x) & math.isfinite(s.y) & math.isfinite(s.z);
        }

        public static simdFloat3 clamp(simdFloat3 s, float3 min, float3 max)
        {
            return new simdFloat3(math.clamp(s.x, min.x, max.x), math.clamp(s.y, min.y, max.y), math.clamp(s.z, min.z, max.z));
        }

        public static simdFloat3 chgsign(float3 a, simdFloat3 b)
        {
            return new simdFloat3(math.chgsign(a.x, b.x), math.chgsign(a.y, b.y), math.chgsign(a.z, b.z));
        }

        public static simdFloat3 chgsign(simdFloat3 a, float4 b)
        {
            return new simdFloat3(math.chgsign(a.x, b), math.chgsign(a.y, b), math.chgsign(a.z, b));
        }

        public static simdFloat3 chgsign0Positive(float3 a, simdFloat3 b)
        {
            return new simdFloat3(math.chgsign0Positive(a.x, b.x), math.chgsign0Positive(a.y, b.y), math.chgsign0Positive(a.z, b.z));
        }

        public static simdFloat3 chgsign0Positive(simdFloat3 a, float4 b)
        {
            return new simdFloat3(math.chgsign0Positive(a.x, b), math.chgsign0Positive(a.y, b), math.chgsign0Positive(a.z, b));
        }

        public static simdFloat3 extractSigns(simdFloat3 s)
        {
            return chgsign(1f, s);
        }

        public static simdFloat3 extractSigns0IsPositive(simdFloat3 s)
        {
            return new simdFloat3(math.select(1f, -1f, s.x < 0f), math.select(1f, -1f, s.y < 0f), math.select(1f, -1f, s.z < 0f));
            // Todo: Alternate implementation that is maybe faster?
            // return extractSigns(s - (-0f));
        }

        public static simdFloat3 rightCross(simdFloat3 b) => new simdFloat3(float4.zero, -b.z, b.y);  // (1,0,0) x b
        public static simdFloat3 upCross(simdFloat3 b) => new simdFloat3(b.z, float4.zero, -b.x);  // (0,1,0) x b
        public static simdFloat3 forwardCross(simdFloat3 b) => new simdFloat3(-b.y, b.x, float4.zero);  // (0,0,1) x b

        public static simdFloat3 crossRightUpForwardRight(float3 a)
        {
            // return new simdFloat3 { m_float3s = new float4x3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x) };
            float4 x = new float4(0f, -a.z, a.y, 0f);
            float4 y = new float4(a.z, 0f, -a.x, a.z);
            float4 z = new float4(-a.y, a.x, 0f, -a.y);
            return new simdFloat3(x, y, z);
        }

        public static simdFloat3 inverseRotateRightUpForwardRight(float3x3 matrixToInverse)
        {
            // return new simdFloat3(dot(matrixToInverse.c0, directions), dot(matrixToInverse.c1, directions), dot(matrixToInverse.c2, directions));
            return new simdFloat3(matrixToInverse.c0.xyzx, matrixToInverse.c1.xyzx, matrixToInverse.c2.xyzx);
        }
    }
}

