using Unity.Mathematics;

namespace Latios
{
    public static class LatiosMath
    {
        #region Easing

        public static float SmoothStart(float t)
        {
            return t * t;
        }

        public static float2 SmoothStart(float2 t)
        {
            return t * t;
        }

        public static float SmoothStop(float t)
        {
            float mt = 1f - t;
            return 1f - mt * mt;
        }

        public static float2 SmoothStop(float2 t)
        {
            float2 mt = 1f - t;
            return 1f - mt * mt;
        }

        public static float SmoothStep(float t)
        {
            //return math.lerp(SmoothStart(t), SmoothStop(t), t);
            var t2 = t * t;
            return 3 * t2 - 2 * t2 * t;
        }

        //Inserts a linear section in the middle of a SmoothStep and then renormalizes both the input and output
        public static float SmoothSlide(float linearStart, float linearStop, float t)
        {
            float l = linearStart;
            float h = linearStop;
            float a, b;

            if (l >= h)
                return SmoothStep(t);
            if (l <= 0f && h >= 1f)
                return t;

            if (l <= 0f)
            {
                a = 0;
                b = -2f * h / (h - 3f);
            }
            else if (h >= 1f)
            {
                a = 3 * l / (l + 2f);
                b = 1f;
            }
            else
            {
                a = 3 * l / (l - h + 3f);
                b = (l + 2 * h) / (l - h + 3f);
            }

            if (t < a)
            {
                float modTime = t * 0.5f / a;
                return (l / 0.5f) * SmoothStep(modTime);
            }
            else if (t > b)
            {
                float modTime = (t - b) * 0.5f / (1 - b) + 0.5f;
                return (SmoothStep(modTime) - 0.5f) * (1f - h) / h + h;
            }
            else
            {
                float modTime = math.unlerp(a, b, t);
                return math.lerp(l, h, modTime);
            }
        }

        #endregion Easing

        #region Transformations

        //From Unity.Rendering.Hybrid/AABB.cs
        public static float3 RotateExtents(float3 extents, float3 m0, float3 m1, float3 m2)
        {
            return math.abs(m0 * extents.x) + math.abs(m1 * extents.y) + math.abs(m2 * extents.z);
        }

        public static float3 RotateExtents(float extents, float3 m0, float3 m1, float3 m2)
        {
            return math.abs(m0 * extents) + math.abs(m1 * extents) + math.abs(m2 * extents);
        }

        public static float3 RotateExtents(float3 extents, float3x3 rotationMatrix)
        {
            return RotateExtents(extents, rotationMatrix.c0, rotationMatrix.c1, rotationMatrix.c2);
        }

        public static float2 ComplexMul(float2 a, float2 b)
        {
            return new float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
        }

        #endregion Transformations

        #region NumberTricks

        // Source: https://stackoverflow.com/questions/3154454/what-is-the-most-efficient-way-to-calculate-the-least-common-multiple-of-two-int
        public static int gcd(int a, int b)
        {
            if (b == 0)
                return a;
            return gcd(b, a % b);
        }

        public static int lcm(int a, int b)
        {
            if (a > b)
                return (a / gcd(a, b)) * b;
            else
                return (b / gcd(a, b)) * a;
        }

        #endregion NumberTricks
    }
}

