using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calci
{
    public static class MathShortcuts
    {
        /// <summary>
        /// Assumes both matrices have a bottom row of (0, 0, 0, 1) when performing the multiplication.
        /// This method does NOT support cross-platform determinism.
        /// </summary>
        /// <param name="a">The left matrix to multiply</param>
        /// <param name="b">The right matrix to multiply</param>
        /// <returns>The resulting matrix</returns>
        public static float4x4 MultiplyTransformMatrices(float4x4 a, float4x4 b)
        {
            if (X86.Fma.IsFmaSupported)
            {
                var ac0    = new v256(a.c0.x, a.c0.y, a.c0.z, a.c0.w, a.c0.x, a.c0.y, a.c0.z, a.c0.w);
                var ac1    = new v256(a.c1.x, a.c1.y, a.c1.z, a.c1.w, a.c1.x, a.c1.y, a.c1.z, a.c1.w);
                var ac2    = new v256(a.c2.x, a.c2.y, a.c2.z, a.c2.w, a.c2.x, a.c2.y, a.c2.z, a.c2.w);
                var bc01   = new v256(b.c0.x, b.c0.y, b.c0.z, b.c0.w, b.c1.x, b.c1.y, b.c1.z, b.c1.w);
                var bc23   = new v256(b.c2.x, b.c2.y, b.c2.z, b.c2.w, b.c3.x, b.c3.y, b.c3.z, b.c3.w);
                var bc01x  = X86.Avx.mm256_permute_ps(bc01, 0);
                var bc23x  = X86.Avx.mm256_permute_ps(bc23, 0);
                var cc01   = X86.Avx.mm256_mul_ps(ac0, bc01x);
                var cc23   = X86.Avx.mm256_mul_ps(ac0, bc23x);
                var bc01y  = X86.Avx.mm256_permute_ps(bc01, 0x55);
                var bc23y  = X86.Avx.mm256_permute_ps(bc23, 0x55);
                cc01       = X86.Fma.mm256_fmadd_ps(ac1, bc01y, cc01);
                cc23       = X86.Fma.mm256_fmadd_ps(ac1, bc23y, cc23);
                var bc01z  = X86.Avx.mm256_permute_ps(bc01, 0xaa);
                var bc23z  = X86.Avx.mm256_permute_ps(bc23, 0xaa);
                cc01       = X86.Fma.mm256_fmadd_ps(ac2, bc01z, cc01);
                cc23       = X86.Fma.mm256_fmadd_ps(ac2, bc23z, cc23);
                var c0     = new float4(cc01.Float0, cc01.Float1, cc01.Float2, cc01.Float3);
                var c1     = new float4(cc01.Float4, cc01.Float5, cc01.Float6, cc01.Float7);
                var c2     = new float4(cc23.Float0, cc23.Float1, cc23.Float2, cc23.Float3);
                var c3     = new float4(cc23.Float4, cc23.Float5, cc23.Float6, cc23.Float7);
                c3        += a.c3;
                return new float4x4(c0, c1, c2, c3);
            }
            else
            {
                return new float4x4(
                    a.c0 * b.c0.x + a.c1 * b.c0.y + a.c2 * b.c0.z,
                    a.c0 * b.c1.x + a.c1 * b.c1.y + a.c2 * b.c1.z,
                    a.c0 * b.c2.x + a.c1 * b.c2.y + a.c2 * b.c2.z,
                    a.c0 * b.c3.x + a.c1 * b.c3.y + a.c2 * b.c3.z + a.c3);
            }
        }
    }
}

