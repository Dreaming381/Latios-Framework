using Unity.Collections;
using UnityEngine;

namespace Latios.Calligraphics
{
    internal static class CalligraphicsInternalExtensions
    {
        public static void GetSubString(this in CalliString calliString, ref FixedString128Bytes htmlTag, int startIndex, int length)
        {
            htmlTag.Clear();
            for (int i = startIndex, end = startIndex + length; i < end; i++)
                htmlTag.Append((char)calliString[i]);
        }
        public static bool Compare(this Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }
    }
}

