using System;
using System.Runtime.CompilerServices;
#if UNITY_6000_0_OR_NEWER
using Unity.Burst.Intrinsics;
#endif

namespace Latios.Calci.Clipper2
{
    internal static class Math128
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int128Like Mul128x64(in Int128Like a, long b)
        {
            // Handle zero early
            if (b == 0 || (a.hi64 == 0 && a.lo64 == 0))
                return default;

            // Determine sign
            bool neg = (Sign128(a) < 0) ^ (b < 0);

            // Work with absolute values
            Int128Like absA = Abs128(a);
            ulong      absB = (ulong) (b < 0 ? -b : b);

            // absA = (hi:lo) * absB
            ulong loLow;
            ulong loHigh = BigMul(absA.lo64, absB, out loLow);

            ulong hiLow;
            BigMul((ulong) absA.hi64, absB, out hiLow);

            ulong lo = loLow;
            long  hi = (long) (loHigh + hiLow);

            var result = new Int128Like(hi, lo);
            return neg ? Neg128(result) : result;
        }

        /// <summary>Produces the full product of two 64-bit numbers.</summary>
        /// <param name="a">The first number to multiply.</param>
        /// <param name="b">The second number to multiply.</param>
        /// <param name="low">The low 64-bit of the product of the specified numbers.</param>
        /// <returns>The high 64-bit of the product of the specified numbers.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long BigMul(long a, long b, out long low)
        {
            //if (ArmBase.Arm64.IsSupported)
            //{
            //    low = a * b;
            //    return ArmBase.Arm64.MultiplyHigh(a, b);
            //}

            ulong high = BigMul((ulong) a, (ulong) b, out ulong ulow);
            low        = (long) ulow;
            return (long) high - ((a >> 63) & b) - ((b >> 63) & a);
        }

        /// <summary>Produces the full product of two unsigned 64-bit numbers.</summary>
        /// <param name="a">The first number to multiply.</param>
        /// <param name="b">The second number to multiply.</param>
        /// <param name="low">The low 64-bit of the product of the specified numbers.</param>
        /// <returns>The high 64-bit of the product of the specified numbers.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong BigMul(ulong a, ulong b, out ulong low)
        {
            // cannot access hardware intrinsics in .NetStandard 2.1
            // what a shame because multiplication of 64bit int with 128 bit result
            // is hardware supported by most platforms. Anyhow, at least Unity BURST provides access to the X86 Intrinsic
#if UNITY_6000_0_OR_NEWER
            if (X86.Bmi2.IsBmi2Supported)
            {
                low = X86.Bmi2.mulx_u64(a, b, out ulong high);
                return high;
            }
#endif
            return SoftwareFallback(a, b, out low);

            static ulong SoftwareFallback(ulong a, ulong b, out ulong low)
            {
                // Adaptation of algorithm for multiplication
                // of 32-bit unsigned integers described
                // in Hacker's Delight by Henry S. Warren, Jr. (ISBN 0-201-91465-4), Chapter 8
                // Basically, it's an optimized version of FOIL method applied to
                // low and high dwords of each operand

                // Use 32-bit uints to optimize the fallback for 32-bit platforms.
                uint al = (uint) a;
                uint ah = (uint) (a >> 32);
                uint bl = (uint) b;
                uint bh = (uint) (b >> 32);

                ulong mull = ((ulong) al) * bl;
                ulong t    = ((ulong) ah) * bl + (mull >> 32);
                ulong tl   = ((ulong) al) * bh + (uint) t;

                low = tl << 32 | (uint) mull;

                return ((ulong) ah) * bh + (t >> 32) + (tl >> 32);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int128Like Abs128(in Int128Like v)
        {
            if (v.hi64 >= 0)
                return v;
            return Neg128(v);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int128Like Add128(in Int128Like a, in Int128Like b)
        {
            ulong lo = a.lo64 + b.lo64;
            // unsigned carry
            long carry = (lo < a.lo64) ? 1L : 0L;
            long hi    = a.hi64 + b.hi64 + carry;
            return new Int128Like(hi, lo);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int128Like Sub128(in Int128Like a, in Int128Like b)
        {
            ulong lo     = a.lo64 - b.lo64;
            long  borrow = a.lo64 < b.lo64 ? 1L : 0L;
            //long hi = a.hi64 - b.hi64 - borrow;
            long hi = unchecked (a.hi64 - b.hi64 - borrow);
            return new Int128Like(hi, lo);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int128Like Mul128(in Int128Like a, in Int128Like b)
        {
            // a.lo * b.lo → 128-bit
            ulong ll_lo;
            ulong ll_hi = BigMul(a.lo64, b.lo64, out ll_lo);

            // a.hi * b.lo → 128-bit (we need only low 64)
            long hl_lo;
            BigMul(a.hi64, (long)b.lo64, out hl_lo);

            // a.lo * b.hi → 128-bit (we need only low 64)
            long lh_lo;
            BigMul((long)a.lo64, b.hi64, out lh_lo);

            // Combine
            ulong lo = ll_lo;
            long  hi = (long)ll_hi + hl_lo + lh_lo;

            return new Int128Like(hi, lo);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int128Like Neg128(in Int128Like v)
        {
            ulong lo = ~v.lo64 + 1UL;
            long  hi = ~v.hi64;
            // propagate carry from low word
            if (lo == 0)
                hi++;
            return new Int128Like(hi, lo);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign128(in Int128Like v)
        {
            int hiNeg     = (int)((ulong)v.hi64 >> 63);  // hiNeg = 1 if hi < 0, else 0
            int hiPos     = (int)((ulong)(-v.hi64) >> 63) & ~hiNeg;  // hiPos = 1 if hi > 0, else 0
            int hiSign    = hiPos - hiNeg;  // hiSign = +1, -1, or 0
            int loNonZero = (int)((v.lo64 | (ulong)-(long)v.lo64) >> 63);  // loNonZero = 1 if lo != 0, else 0
            return hiSign | (loNonZero & ~(-hiSign >> 31));  // If hi != 0 → hiSign  Else → loNonZero
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int128Like Sub128(long a, long b)
        {
            long lo     = a - b;
            long borrow = ((ulong)a < (ulong)b) ? 1L : 0L;
            long hi     = -borrow;
            return new Int128Like(hi, (ulong)lo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int128Like Mul128(long a, long b)
        {
            long lo;
            long hi = BigMul(a, b, out lo);
            return new Int128Like(hi, unchecked ((ulong) lo));
        }

        //[BurstCompile]
        //public static void Test()
        //{
        //    long p0a = 10368;
        //    long da = 1152;
        //    var tA = Rational.Zero;
        //    long p0b = 4800;
        //    long db = 6720;
        //    var tB = Rational.Zero;
        //    var compare64 = Segment.CompareCoord(p0a, da, tA, p0b, db, tB);
        //    var compare128 = Segment.CompareCoord128(p0a, da, tA, p0b, db, tB);
        //    if (compare64 != compare128)
        //        Debug.Log($"compare64 {compare64} != {compare128}:\np0.x={p0a} pd.x={da} t={tA.num}/{tA.den}\np0.x={p0b}  pd.x={db} t={tB.num}/{tB.den}");

        //    var mul64 = p0a * tA.den;
        //    var mul128 = Mul128(p0a, tA.den);
        //    if (mul64 != mul128.ToInt64())
        //    {
        //        //Debug.Log($"mul64 {mul64} != mul128 {mul128.hi64} {mul128.lo64}");
        //        var hi = BigMul(p0a, tA.den, out long lo);
        //        Debug.Log($"mul64 {mul64} != mul128 {hi} {lo}");
        //    }
        //}
    }
}

