/*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  12 December 2025                                                *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  Core structures and functions for the Clipper Library           *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*******************************************************************************/

using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calci.Clipper2
{
    internal struct Rect64
    {
        public long left;
        public long top;
        public long right;
        public long bottom;

        public Rect64(long l, long t, long r, long b)
        {
            left   = l;
            top    = t;
            right  = r;
            bottom = b;
        }
        public static Rect64 InvalidRect64 => new Rect64 {
            left  = long.MaxValue, top = long.MaxValue,
            right                      = long.MinValue, bottom = long.MinValue
        };
        public Rect64(bool isValid)
        {
            if (isValid)
            {
                left = 0; top = 0; right = 0; bottom = 0;
            }
            else
            {
                left  = long.MaxValue; top = long.MaxValue;
                right                      = long.MinValue; bottom = long.MinValue;
            }
        }

        public Rect64(Rect64 rec)
        {
            left   = rec.left;
            top    = rec.top;
            right  = rec.right;
            bottom = rec.bottom;
        }

        public long Width
        {
            readonly get => right - left;
            set => right = left + value;
        }

        public long Height
        {
            readonly get => bottom - top;
            set => bottom = top + value;
        }

        public readonly bool IsEmpty()
        {
            return bottom <= top || right <= left;
        }
        public readonly bool IsValid()
        {
            return left < long.MaxValue;
        }
        public readonly long2 MidPoint()
        {
            return new long2((left + right) / 2, (top + bottom) / 2);
        }
        public readonly bool Contains(long2 pt)
        {
            return pt.x > left && pt.x < right &&
                   pt.y > top && pt.y < bottom;
        }

        public readonly bool Contains(Rect64 rec)
        {
            return rec.left >= left && rec.right <= right &&
                   rec.top >= top && rec.bottom <= bottom;
        }
        public readonly bool Intersects(Rect64 rec)
        {
            return (Math.Max(left, rec.left) <= Math.Min(right, rec.right)) &&
                   (Math.Max(top, rec.top) <= Math.Min(bottom, rec.bottom));
        }
    }

    //Note: all clipping operations except for Difference are commutative.
    internal enum ClipType
    {
        NoClip,
        Intersection,
        Union,
        Difference,
        Xor
    }

    internal enum PathType
    {
        Subject,
        Clip
    };

    //By far the most widely used filling rules for polygons are EvenOdd
    //and NonZero, sometimes called Alternate and Winding respectively.
    //https://en.wikipedia.org/wiki/Nonzero-rule
    internal enum FillRule
    {
        EvenOdd,
        NonZero,
        Positive,
        Negative
    };

    internal static class InternalClipper
    {
        internal const long   MaxInt64  = 9223372036854775807;
        internal const long   MaxCoord  = MaxInt64 / 4;
        internal const double max_coord = MaxCoord;
        internal const double min_coord = -MaxCoord;
        internal const long   Invalid64 = MaxInt64;

        public const double floatingPointTolerance   = 1E-12;
        public const double defaultMinimumEdgeLength = 0.1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double CrossProduct(long2 pt1, long2 pt2, long2 pt3)
        {
            //typecast to double to avoid potential int overflow
            return ((double)(pt2.x - pt1.x) * (pt3.y - pt2.y) -
                    (double)(pt2.y - pt1.y) * (pt3.x - pt2.x));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CrossProductSign(long2 pt1, long2 pt2, long2 pt3)
        {
            long a = pt2.x - pt1.x;
            long b = pt3.y - pt2.y;
            long c = pt2.y - pt1.y;
            long d = pt3.x - pt2.x;
            return Math128.Sign128(Math128.Sub128(Math128.Mul128(a, b), Math128.Mul128(c, d)));
        }
        //public static int CrossProductSign(long2 pt1, long2 pt2, long2 pt3)
        //{
        //    long a = pt2.x - pt1.x;
        //    long b = pt3.y - pt2.y;
        //    long c = pt2.y - pt1.y;
        //    long d = pt3.x - pt2.x;
        //    UInt128Struct ab = MultiplyUInt64((ulong)math.abs(a), (ulong)math.abs(b));
        //    UInt128Struct cd = MultiplyUInt64((ulong)math.abs(c), (ulong)math.abs(d));
        //    int signAB = TriSign(a) * TriSign(b);
        //    int signCD = TriSign(c) * TriSign(d);

        //    if (signAB == signCD)
        //    {
        //        int result;
        //        if (ab.hi64 == cd.hi64)
        //        {
        //            if (ab.lo64 == cd.lo64) return 0;
        //            result = (ab.lo64 > cd.lo64) ? 1 : -1;
        //        }
        //        else result = (ab.hi64 > cd.hi64) ? 1 : -1;
        //        return (signAB > 0) ? result : -result;
        //    }
        //    return (signAB > signCD) ? 1 : -1;
        //}
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void CheckPrecision(int precision)
        {
            if (precision < -8 || precision > 8)
                Debug.LogError("Precision is out of range.");
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsAlmostZero(double value)
        {
            return (math.abs(value) <= floatingPointTolerance);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int TriSign(long x)  // returns 0, 1 or -1
        {
            return (x < 0) ? -1 : (x > 0) ? 1 : 0;
        }
        public struct UInt128Struct
        {
            public ulong lo64;
            public ulong hi64;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt128Struct MultiplyUInt64(ulong a, ulong b)  // #834,#835
        {
            ulong         x1 = (a & 0xFFFFFFFF) * (b & 0xFFFFFFFF);
            ulong         x2 = (a >> 32) * (b & 0xFFFFFFFF) + (x1 >> 32);
            ulong         x3 = (a & 0xFFFFFFFF) * (b >> 32) + (x2 & 0xFFFFFFFF);
            UInt128Struct result;
            result.lo64 = (x3 & 0xFFFFFFFF) << 32 | (x1 & 0xFFFFFFFF);
            result.hi64 = (a >> 32) * (b >> 32) + (x2 >> 32) + (x3 >> 32);
            return result;
        }
        // returns true if (and only if) a * b == c * d
        //internal static bool ProductsAreEqual(long a, long b, long c, long d)
        //{
        //    // nb: unsigned values will be needed for CalcOverflowCarry()
        //    ulong absA = (ulong)math.abs(a);
        //    ulong absB = (ulong)math.abs(b);
        //    ulong absC = (ulong)math.abs(c);
        //    ulong absD = (ulong)math.abs(d);

        //    UInt128Struct mul_ab = MultiplyUInt64(absA, absB);
        //    UInt128Struct mul_cd = MultiplyUInt64(absC, absD);

        //    // nb: it's important to differentiate 0 values here from other values
        //    int sign_ab = TriSign(a) * TriSign(b);
        //    int sign_cd = TriSign(c) * TriSign(d);

        //    return mul_ab.lo64 == mul_cd.lo64 && mul_ab.hi64 == mul_cd.hi64 && sign_ab == sign_cd;
        //}
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ProductsAreEqual(long a, long b, long c, long d)
        {
            var mul_ab = Math128.Mul128(a, b);
            var mul_cd = Math128.Mul128(c, d);
            return mul_ab.CompareTo(mul_cd) == 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCollinear(long2 pt1, long2 sharedPt, long2 pt2)
        {
            long a = sharedPt.x - pt1.x;
            long b = pt2.y - sharedPt.y;
            long c = sharedPt.y - pt1.y;
            long d = pt2.x - sharedPt.x;
            // When checking for collinearity with very large coordinate values
            // then ProductsAreEqual is more accurate than using CrossProduct.
            return ProductsAreEqual(a, b, c, d);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DotProduct(long2 pt1, long2 pt2, long2 pt3)
        {
            //typecast to double to avoid potential int overflow
            return ((double)(pt2.x - pt1.x) * (pt3.x - pt2.x) +
                    (double)(pt2.y - pt1.y) * (pt3.y - pt2.y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double CrossProduct(double2 vec1, double2 vec2)
        {
            return (vec1.y * vec2.x - vec2.y * vec1.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double DotProduct(double2 vec1, double2 vec2)
        {
            return (vec1.x * vec2.x + vec1.y * vec2.y);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long CheckCastInt64(double val)
        {
            if ((val >= max_coord) || (val <= min_coord))
                return Invalid64;
            return (long)math.round(val);
        }

        // GetLineIntersectPt - a 'true' result is non-parallel. The 'ip' will also
        // be constrained to seg1. However, it's possible that 'ip' won't be inside
        // seg2, even when 'ip' hasn't been constrained (ie 'ip' is inside seg1).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetLineIntersectPt(long2 ln1a,
                                              long2 ln1b, long2 ln2a, long2 ln2b, out long2 ip)
        {
            double dy1 = (ln1b.y - ln1a.y);
            double dx1 = (ln1b.x - ln1a.x);
            double dy2 = (ln2b.y - ln2a.y);
            double dx2 = (ln2b.x - ln2a.x);
            double det = dy1 * dx2 - dy2 * dx1;
            if (det == 0.0)
            {
                ip = new long2();
                return false;
            }

            double t = ((ln1a.x - ln2a.x) * dy2 - (ln1a.y - ln2a.y) * dx2) / det;
            if (t <= 0.0)
                ip = ln1a;
            else if (t >= 1.0)
                ip = ln1b;
            else
            {
                // avoid using constructor (and rounding too) as they affect performance //664
                ip.x = (long)(ln1a.x + t * dx1);
                ip.y = (long)(ln1a.y + t * dy1);
            }
            return true;
        }
        internal static bool SegsIntersect(long2 seg1a,
                                           long2 seg1b, long2 seg2a, long2 seg2b, bool inclusive = false)
        {
            double dy1 = (seg1b.y - seg1a.y);
            double dx1 = (seg1b.x - seg1a.x);
            double dy2 = (seg2b.y - seg2a.y);
            double dx2 = (seg2b.x - seg2a.x);
            double cp  = dy1 * dx2 - dy2 * dx1;
            if (cp == 0)
                return false; // ie parallel segments

            if (inclusive)
            {
                //result **includes** segments that touch at an end point
                double t = ((seg1a.x - seg2a.x) * dy2 - (seg1a.y - seg2a.y) * dx2);
                if (t == 0)
                    return true;
                if (t > 0)
                {
                    if (cp < 0 || t > cp)
                        return false;
                }
                else if (cp > 0 || t < cp)
                    return false; // false when t more neg. than cp

                t = ((seg1a.x - seg2a.x) * dy1 - (seg1a.y - seg2a.y) * dx1);
                if (t == 0)
                    return true;
                if (t > 0)
                    return (cp > 0 && t <= cp);
                else
                    return (cp < 0 && t >= cp); // true when t less neg. than cp
            }
            else
            {
                //result **excludes** segments that touch at an end point
                double t = ((seg1a.x - seg2a.x) * dy2 - (seg1a.y - seg2a.y) * dx2);
                if (t == 0)
                    return false;
                if (t > 0)
                {
                    if (cp < 0 || t >= cp)
                        return false;
                }
                else if (cp > 0 || t <= cp)
                    return false; // false when t more neg. than cp

                t = ((seg1a.x - seg2a.x) * dy1 - (seg1a.y - seg2a.y) * dx1);
                if (t == 0)
                    return false;
                if (t > 0)
                    return (cp > 0 && t < cp);
                else
                    return (cp < 0 && t > cp); // true when t less neg. than cp
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rect64 GetBounds(NativeList<OutPt> _outPtList, int opID)
        {
            ref var op     = ref _outPtList.ElementAt(opID);
            Rect64  result = Rect64.InvalidRect64;
            int     op2ID  = opID;
            do
            {
                ref var op2 = ref _outPtList.ElementAt(op2ID);
                var     pt  = op2.pt;
                if (pt.x < result.left)
                    result.left = pt.x;
                if (pt.x > result.right)
                    result.right = pt.x;
                if (pt.y < result.top)
                    result.top = pt.y;
                if (pt.y > result.bottom)
                    result.bottom = pt.y;
                op2ID             = op2.next;
            }
            while (op2ID != opID);
            return result;
        }
        public static Rect64 GetBounds(NativeList<long2> path)
        {
            if (path.Length == 0)
                return new Rect64();
            Rect64 result = Rect64.InvalidRect64;
            foreach (long2 pt in path)
            {
                if (pt.x < result.left)
                    result.left = pt.x;
                if (pt.x > result.right)
                    result.right = pt.x;
                if (pt.y < result.top)
                    result.top = pt.y;
                if (pt.y > result.bottom)
                    result.bottom = pt.y;
            }
            return result;
        }
        public static long2 GetClosestPtOnSegment(long2 offPt,
                                                  long2 seg1, long2 seg2)
        {
            if (seg1.x == seg2.x && seg1.y == seg2.y)
                return seg1;
            double dx = (seg2.x - seg1.x);
            double dy = (seg2.y - seg1.y);
            double q  = ((offPt.x - seg1.x) * dx +
                         (offPt.y - seg1.y) * dy) / ((dx * dx) + (dy * dy));
            if (q < 0)
                q = 0;
            else if (q > 1)
                q = 1;
            return new long2(
                // use MidpointRounding.ToEven in order to explicitly match the nearbyint behaviour on the C++ side
                seg1.x + Math.Round(q * dx, MidpointRounding.ToEven),
                seg1.y + Math.Round(q * dy, MidpointRounding.ToEven)
                );
        }
        public static PointInPolygonResult PointInPolygon(long2 pt, NativeList<long2> polygon)
        {
            int len = polygon.Length, start = 0;
            if (len < 3)
                return PointInPolygonResult.IsOutside;

            while (start < len && polygon[start].y == pt.y)
                start++;
            if (start == len)
                return PointInPolygonResult.IsOutside;

            bool isAbove = polygon[start].y < pt.y, startingAbove = isAbove;
            int  val     = 0, i = start + 1, end = len;
            while (true)
            {
                if (i == end)
                {
                    if (end == 0 || start == 0)
                        break;
                    end = start;
                    i   = 0;
                }

                if (isAbove)
                {
                    while (i < end && polygon[i].y < pt.y)
                        i++;
                }
                else
                {
                    while (i < end && polygon[i].y > pt.y)
                        i++;
                }

                if (i == end)
                    continue;

                long2 curr = polygon[i], prev;
                if (i > 0)
                    prev = polygon[i - 1];
                else
                    prev = polygon[len - 1];

                if (curr.y == pt.y)
                {
                    if (curr.x == pt.x || (curr.y == prev.y &&
                                           ((pt.x < prev.x) != (pt.x < curr.x))))
                        return PointInPolygonResult.IsOn;
                    i++;
                    if (i == start)
                        break;
                    continue;
                }

                if (pt.x < curr.x && pt.x < prev.x)
                {
                    // we're only interested in edges crossing on the left
                }
                else if (pt.x > prev.x && pt.x > curr.x)
                {
                    val = 1 - val;  // toggle val
                }
                else
                {
                    int cps2 = CrossProductSign(prev, curr, pt);
                    if (cps2 == 0)
                        return PointInPolygonResult.IsOn;
                    if ((cps2 < 0) == isAbove)
                        val = 1 - val;
                }
                isAbove = !isAbove;
                i++;
            }

            if (isAbove == startingAbove)
                return val == 0 ? PointInPolygonResult.IsOutside : PointInPolygonResult.IsInside;
            if (i == len)
                i   = 0;
            int cps = (i == 0) ?
                      CrossProductSign(polygon[len - 1], polygon[0], pt) :
                      CrossProductSign(polygon[i - 1],   polygon[i], pt);

            if (cps == 0)
                return PointInPolygonResult.IsOn;
            if ((cps < 0) == isAbove)
                val = 1 - val;
            return val == 0 ? PointInPolygonResult.IsOutside : PointInPolygonResult.IsInside;
        }
        public static bool Path2ContainsPath1(NativeList<long2> path1, NativeList<long2> path2)
        {
            // we need to make some accommodation for rounding errors
            // so we won't jump if the first vertex is found outside
            PointInPolygonResult pip = PointInPolygonResult.IsOn;
            foreach (long2 pt in path1)
            {
                switch (PointInPolygon(pt, path2))
                {
                    case PointInPolygonResult.IsOutside:
                        if (pip == PointInPolygonResult.IsOutside)
                            return false;
                        pip = PointInPolygonResult.IsOutside;
                        break;
                    case PointInPolygonResult.IsInside:
                        if (pip == PointInPolygonResult.IsInside)
                            return true;
                        pip = PointInPolygonResult.IsInside;
                        break;
                    default: break;
                }
            }
            // since path1's location is still equivocal, check its midpoint
            long2 mp = GetBounds(path1).MidPoint();
            return InternalClipper.PointInPolygon(mp, path2) != PointInPolygonResult.IsOutside;
        }
    }  //InternalClipperFuncs
}  //namespace

