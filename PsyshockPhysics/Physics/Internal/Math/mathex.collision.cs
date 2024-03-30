using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class mathex
    {
        //Formerly getFactoredLength
        public static float GetLengthAndNormal(float3 v, out float3 normal)
        {
            float lengthSq  = math.lengthsq(v);
            float invLength = math.rsqrt(lengthSq);
            normal          = v * invLength;
            return lengthSq * invLength;
        }

        public static float4 GetLengthAndNormal(in simdFloat3 v, out simdFloat3 normal)
        {
            float4 lengthSq  = simd.lengthsq(v);
            float4 invLength = math.rsqrt(lengthSq);
            normal           = v * invLength;
            return lengthSq * invLength;
        }

        // From Unity's CalculatePerpendicularNormalized.
        // Todo: If input is unscaledNormal, which is tangent and which is bitangent?
        public static void GetDualPerpendicularNormalized(float3 unscaledInput, out float3 perpendicularA, out float3 perpendicularB)
        {
            float3 v              = unscaledInput;
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

        public static float cproduct(float3 v) => v.x * v.y * v.z;

        internal static quaternion FromToRotation(float3 from, float3 to)
        {
            Unity.Assertions.Assert.IsTrue(math.abs(math.lengthsq(from) - 1.0f) < 1e-4f);
            Unity.Assertions.Assert.IsTrue(math.abs(math.lengthsq(to) - 1.0f) < 1e-4f);
            float3 cross = math.cross(from, to);
            GetDualPerpendicularNormalized(from, out float3 safeAxis, out _);  // for when angle ~= 180
            float  dot             = math.dot(from, to);
            float3 squares         = new float3(0.5f - new float2(dot, -dot) * 0.5f, math.lengthsq(cross));
            float3 inverses        = math.select(math.rsqrt(squares), 0.0f, squares < 1e-10f);
            float2 sinCosHalfAngle = squares.xy * inverses.xy;
            float3 axis            = math.select(cross * inverses.z, safeAxis, squares.z < 1e-10f);
            return new quaternion(new float4(axis * sinCosHalfAngle.x, sinCosHalfAngle.y));
        }

        /// <summary>   Calculate the eigenvectors and eigenvalues of a symmetric 3x3 matrix. </summary>
        internal static void DiagonalizeSymmetricApproximation(float3x3 a, out float3x3 eigenVectors, out float3 eigenValues)
        {
            float GetMatrixElement(float3x3 m, int row, int col)
            {
                switch (col)
                {
                    case 0: return m.c0[row];
                    case 1: return m.c1[row];
                    case 2: return m.c2[row];
                    default: UnityEngine.Assertions.Assert.IsTrue(false); return 0.0f;
                }
            }

            void SetMatrixElement(ref float3x3 m, int row, int col, float x)
            {
                switch (col)
                {
                    case 0: m.c0[row] = x; break;
                    case 1: m.c1[row] = x; break;
                    case 2: m.c2[row] = x; break;
                    default: UnityEngine.Assertions.Assert.IsTrue(false); break;
                }
            }

            eigenVectors            = float3x3.identity;
            float     epsSq         = 1e-14f * (math.lengthsq(a.c0) + math.lengthsq(a.c1) + math.lengthsq(a.c2));
            const int maxIterations = 10;
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Find the row (p) and column (q) of the off-diagonal entry with greater magnitude
                int p = 0, q = 1;
                {
                    float maxEntry = math.abs(a.c1[0]);
                    float mag02    = math.abs(a.c2[0]);
                    float mag12    = math.abs(a.c2[1]);
                    if (mag02 > maxEntry)
                    {
                        maxEntry = mag02;
                        p        = 0;
                        q        = 2;
                    }
                    if (mag12 > maxEntry)
                    {
                        maxEntry = mag12;
                        p        = 1;
                        q        = 2;
                    }

                    // Terminate if it's small enough
                    if (maxEntry * maxEntry < epsSq)
                    {
                        break;
                    }
                }

                // Calculate jacobia rotation
                float3x3 j = float3x3.identity;
                {
                    float apq = GetMatrixElement(a, p, q);
                    float tau = (GetMatrixElement(a, q, q) - GetMatrixElement(a, p, p)) / (2.0f * apq);
                    float t   = math.sqrt(1.0f + tau * tau);
                    if (tau > 0.0f)
                    {
                        t = 1.0f / (tau + t);
                    }
                    else
                    {
                        t = 1.0f / (tau - t);
                    }
                    float c = math.rsqrt(1.0f + t * t);
                    float s = t * c;

                    SetMatrixElement(ref j, p, p, c);
                    SetMatrixElement(ref j, q, q, c);
                    SetMatrixElement(ref j, p, q, s);
                    SetMatrixElement(ref j, q, p, -s);
                }

                // Rotate a
                a            = math.mul(math.transpose(j), math.mul(a, j));
                eigenVectors = math.mul(eigenVectors, j);
            }
            eigenValues = new float3(a.c0.x, a.c1.y, a.c2.z);
        }
    }
}

