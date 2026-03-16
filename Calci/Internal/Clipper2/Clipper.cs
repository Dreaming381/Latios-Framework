using Unity.Mathematics;

namespace Latios.Calci.Clipper2
{
    internal static class ClipperFunc
    {
        public static Rect64 MaxInvalidRect64()
        {
            return new Rect64(long.MaxValue, long.MaxValue, long.MinValue, long.MinValue);
        }
        public static double Sqr(double value)
        {
            return value * value;
        }
        public static bool PointsNearEqual(double2 pt1, double2 pt2, double distanceSqrd)
        {
            return Sqr(pt1.x - pt2.x) + Sqr(pt1.y - pt2.y) < distanceSqrd;
        }
        public static double PerpendicDistFromLineSqrd(long2 pt, long2 line1, long2 line2)
        {
            double a = (double)pt.x - line1.x;
            double b = (double)pt.y - line1.y;
            double c = (double)line2.x - line1.x;
            double d = (double)line2.y - line1.y;
            if (c == 0 && d == 0)
                return 0;
            return Sqr(a * d - c * b) / (c * c + d * d);
        }
    }
}

