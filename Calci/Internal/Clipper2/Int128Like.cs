using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Latios.Calci.Clipper2
{
    internal readonly struct Int128Like : IEquatable<Int128Like>, IComparable<Int128Like>
    {
        public readonly long  hi64;
        public readonly ulong lo64;

        public Int128Like(long hi, ulong lo)
        {
            hi64 = hi;
            lo64 = lo;
        }
        public long ToInt64() => unchecked ((long) lo64);
        public static Int128Like FromBigInteger(BigInteger v)
        {
            BigInteger mask64 = (BigInteger.One << 64) - 1;

            ulong lo = (ulong) (v & mask64);
            long  hi = (long) (v >> 64);

            return new Int128Like(hi, lo);
        }
        public long ToInt64Saturating()
        {
            if (hi64 > 0)
                return long.MaxValue;

            if (hi64 < -1)
                return long.MinValue;

            // hi64 == 0 or hi64 == -1 → value fits exactly
            return (long) lo64;
        }
        public static bool operator <(Int128Like a, Int128Like b) => a.CompareTo(b) < 0;
        public static bool operator >(Int128Like a, Int128Like b) => a.CompareTo(b) > 0;
        public static bool operator <=(Int128Like a, Int128Like b) => a.CompareTo(b) <= 0;
        public static bool operator >=(Int128Like a, Int128Like b) => a.CompareTo(b) >= 0;
        public static bool operator ==(Int128Like a, Int128Like b) => a.hi64 == b.hi64 && a.lo64 == b.lo64;
        public static bool operator !=(Int128Like a, Int128Like b) => !(a == b);
        public override bool Equals(object obj)
        {
            return obj is Int128Like other && Equals(other);
        }
        public bool Equals(Int128Like other)
        {
            return this != other;
        }
        public override int GetHashCode()
        {
            //return HashCode.Combine(num, den);
            int hashCode = 2055808453;
            hashCode     = hashCode * -1521134295 + hi64.GetHashCode();
            hashCode     = hashCode * -1521134295 + lo64.GetHashCode();
            return hashCode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(Int128Like other)
        {
            int hiCmp = hi64.CompareTo(other.hi64);
            if (hiCmp != 0)
                return hiCmp;

            return lo64.CompareTo(other.lo64);
        }
    }
}

