namespace Latios.Calligraphics.HarfBuzz
{
    internal enum Direction
    {
        INVALID = 0,
        LTR = 4,
        RTL,
        TTB,
        BTT
    }

    internal static class DirectionExtensions
    {
        public static string ToString(this Direction dir)
        {
            return "";
        }

        public static bool IsHorizontal(this Direction dir)
        {
            return ((int)dir & ~1) == 4;
        }

        public static bool IsVertical(this Direction dir)
        {
            return ((int)dir & ~1) == 6;
        }

        public static bool IsForward(this Direction dir)
        {
            return ((int)dir & ~2) == 4;
        }

        public static bool IsBackward(this Direction dir)
        {
            return ((int)dir & ~2) == 5;
        }

        public static bool IsValid(this Direction dir)
        {
            return ((int)dir & ~3) == 4;
        }

        public static Direction Reverse(this Direction dir)
        {
            return (Direction)((int)dir ^ 1);
        }
    }
}
