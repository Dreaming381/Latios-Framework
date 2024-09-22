using System;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

// The code within this file is a derivative of two separate works, licensed as follows:
//
// https://theobald.brandeis.edu/qcp/ - BSD-3-Clause
//
// Copyright (c) 2009-2016 Pu Liu and Douglas L. Theobald
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted
// provided that the following conditions are met:
//
// * Redistributions of source code must retain the above copyright notice, this list of
//   conditions and the following disclaimer.
// * Redistributions in binary form must reproduce the above copyright notice, this list
//   of conditions and the following disclaimer in the documentation and/or other materials
//   provided with the distribution.
// * Neither the name of the <ORGANIZATION> nor the names of its contributors may be used to
//   endorse or promote products derived from this software without specific prior written
//   permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
// PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// &&
//
// https://github.com/EGjoni/Everything-Will-Be-IK/tree/cushion_test - MIT
//
// The MIT License (MIT)
//
// Copyright(c) 2016 Eron Gjoni
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace Latios
{
    public static class Qcp
    {
        /// <summary>
        /// Performs the Quaternion Characteristic Polynomial method to find a rotation and optionally a translation
        /// which best transforms the current points to match the target points, measured by the least-squares distance
        /// of corresponding points at each index.
        /// </summary>
        /// <param name="currentPoints">The points to be transformed to align with the target points</param>
        /// <param name="targetPoints">The reference points to try to match</param>
        /// <param name="weights">Optional weight values (leave default to ignore) that bias the result to favor distances of specific indices</param>
        /// <param name="translate">If true, a translation is also included in the computation</param>
        /// <returns>A rotation and an optional translation that best transforms currentPoints to match targetPoints</returns>
        public static RigidTransform Solve(ReadOnlySpan<float3> currentPoints, ReadOnlySpan<float3> targetPoints, ReadOnlySpan<float> weights, bool translate)
        {
            float3 currentShift = 0f;
            float3 targetShift  = 0f;

            if (translate)
            {
                if (weights.Length > 0)
                {
                    currentShift = CenterOfWeighted(currentPoints, weights);
                    targetShift  = CenterOfWeighted(targetPoints, weights);
                }
                else
                {
                    currentShift = CenterOf(currentPoints);
                    targetShift  = CenterOf(targetPoints);
                }
            }

            if (currentPoints.Length == 1)
            {
                return new RigidTransform(UnityEngine.Quaternion.FromToRotation(currentPoints[0] - currentShift, targetPoints[0] - targetShift), targetShift - currentShift);
            }

            // Inline the calculation of the inner product.
            // A = targetPoints, while B = currentPoints, and we are trying to map B onto A
            float    innerA = 0f;
            float    innerB = 0f;
            float3x3 mat    = default;

            if (weights.Length > 0)
            {
                for (int i = 0; i < currentPoints.Length; i++)
                {
                    var aRaw  = targetPoints[i] - targetShift;
                    var a     = aRaw * weights[i];
                    innerA   += math.dot(a, aRaw);
                    var b     = currentPoints[i];
                    innerB   += weights[i] * math.distancesq(b, currentShift);
                    mat      += new float3x3(a.x * b, a.y * b, a.z * b);
                }
            }
            else
            {
                for (int i = 0; i < currentPoints.Length; i++)
                {
                    var a   = targetPoints[i];
                    innerA += math.distancesq(a, targetShift);
                    var b   = currentPoints[i];
                    innerB += math.distancesq(b, currentShift);
                    mat    += new float3x3(a.x * b, a.y * b, a.z * b);
                }
            }

            var eigen0 = (innerA + innerB) * 0.5f;

            float4x4 kMat;
            kMat.c0.x = mat.c0.x + mat.c1.y + mat.c2.z - eigen0;
            kMat.c0.y = mat.c1.z - mat.c2.y;
            kMat.c0.z = mat.c2.x - mat.c0.z;
            kMat.c0.w = mat.c0.y - mat.c1.x;

            kMat.c1.x = kMat.c0.y;
            kMat.c1.y = mat.c0.x - mat.c1.y - mat.c2.z - eigen0;
            kMat.c1.z = mat.c0.y + mat.c1.x;
            kMat.c1.w = mat.c2.x + mat.c0.z;

            kMat.c2.x = kMat.c0.z;
            kMat.c2.y = kMat.c1.z;
            kMat.c2.z = -mat.c0.x + mat.c1.y - mat.c2.z - eigen0;
            kMat.c2.w = mat.c1.z + mat.c2.y;

            kMat.c3.x = kMat.c0.w;
            kMat.c3.y = kMat.c1.w;
            kMat.c3.z = kMat.c2.w;
            kMat.c3.w = -mat.c0.x - mat.c1.y + mat.c2.z - eigen0;

            var a2233_3223 = kMat.c2.z * kMat.c3.w - kMat.c3.z * kMat.c2.w;
            var a2133_3123 = kMat.c2.y * kMat.c3.w - kMat.c3.y * kMat.c2.w;
            var a2132_3122 = kMat.c2.y * kMat.c3.z - kMat.c3.y * kMat.c2.z;
            var a2032_3022 = kMat.c2.x * kMat.c3.z - kMat.c3.x * kMat.c2.z;
            var a2033_3023 = kMat.c2.x * kMat.c3.w - kMat.c3.x * kMat.c2.w;
            var a2031_3021 = kMat.c2.x * kMat.c3.y - kMat.c3.x * kMat.c2.y;

            float4 quat;
            quat.w = kMat.c1.y * a2233_3223 - kMat.c1.z * a2133_3123 + kMat.c1.w * a2132_3122;
            quat.x = -kMat.c1.x * a2233_3223 + kMat.c1.z * a2033_3023 - kMat.c1.w * a2032_3022;
            quat.y = kMat.c1.x * a2133_3123 - kMat.c1.y * a2033_3023 + kMat.c1.w * a2031_3021;
            quat.z = -kMat.c1.x * a2132_3122 + kMat.c1.y * a2032_3022 + kMat.c1.z * a2031_3021;

            var quatSq = math.lengthsq(quat);

            // The following code tries to calculate another column in the adjoint matrix when the norm of the current column is too small.
            // Usually this code will never be ran, but is there for absolute safety.
            const float kEvecPrec = 1e-6f;
            if (Hint.Unlikely(quatSq < kEvecPrec))
            {
                quat.w = kMat.c0.y * a2233_3223 - kMat.c0.z * a2133_3123 + kMat.c0.w * a2132_3122;
                quat.x = -kMat.c0.x * a2233_3223 + kMat.c0.z * a2033_3023 - kMat.c0.w * a2032_3022;
                quat.y = kMat.c0.x * a2133_3123 - kMat.c0.y * a2033_3023 + kMat.c0.w * a2031_3021;
                quat.z = -kMat.c0.x * a2132_3122 + kMat.c0.y * a2032_3022 + kMat.c0.z * a2031_3021;
                quatSq = math.lengthsq(quat);

                if (quatSq < kEvecPrec)
                {
                    var a0213_0312 = kMat.c0.z * kMat.c1.w - kMat.c0.w * kMat.c1.z;
                    var a0113_0311 = kMat.c0.y * kMat.c1.w - kMat.c0.w * kMat.c1.y;
                    var a0112_0211 = kMat.c0.y * kMat.c1.z - kMat.c0.z * kMat.c1.y;
                    var a0013_0310 = kMat.c0.x * kMat.c1.w - kMat.c0.w * kMat.c1.x;
                    var a0012_0210 = kMat.c0.x * kMat.c1.z - kMat.c0.z * kMat.c1.x;
                    var a0011_0110 = kMat.c0.x * kMat.c1.y - kMat.c0.y * kMat.c1.x;

                    quat.w = kMat.c3.y * a0213_0312 - kMat.c3.z * a0113_0311 + kMat.c3.w * a0112_0211;
                    quat.x = -kMat.c3.x * a0213_0312 + kMat.c3.z * a0013_0310 - kMat.c3.w * a0012_0210;
                    quat.y = kMat.c3.x * a0113_0311 - kMat.c3.y * a0013_0310 + kMat.c3.w * a0011_0110;
                    quat.z = -kMat.c3.x * a0112_0211 + kMat.c3.y * a0012_0210 - kMat.c3.z * a0011_0110;
                    quatSq = math.lengthsq(quat);

                    if (quatSq < kEvecPrec)
                    {
                        quat.w = kMat.c2.y * a0213_0312 - kMat.c2.z * a0113_0311 + kMat.c2.w * a0112_0211;
                        quat.x = -kMat.c2.x * a0213_0312 + kMat.c2.z * a0013_0310 - kMat.c2.w * a0012_0210;
                        quat.y = kMat.c2.x * a0113_0311 - kMat.c2.y * a0013_0310 + kMat.c2.w * a0011_0110;
                        quat.z = -kMat.c2.x * a0112_0211 + kMat.c2.y * a0012_0210 - kMat.c2.z * a0011_0110;
                        quatSq = math.lengthsq(quat);

                        if ( quatSq < kEvecPrec)
                        {
                            quat = quaternion.identity.value;
                        }
                    }
                }
            }

            quat.xyz = -quat.xyz;
            var min  = math.cmin(math.abs(quat));
            if (min > kEvecPrec)
                quat /= min;
            var rot   = math.normalize(new quaternion(quat));
            return new RigidTransform(rot, targetShift - currentShift);
        }

        static float3 CenterOf(ReadOnlySpan<float3> points)
        {
            float3 result = 0f;
            foreach (var p in points)
                result += p;
            return result / points.Length;
        }

        static float3 CenterOfWeighted(ReadOnlySpan<float3> points, ReadOnlySpan<float> weights)
        {
            float3 result = 0f;
            float  sum    = 0f;
            int    i      = 0;
            foreach (var p in points)
            {
                result += p * weights[i];
                sum    += weights[i];
                i++;
            }
            return result / sum;
        }
    }
}

