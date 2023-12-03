using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class RuntimeConstants
    {
        // Used to hide a constant from the Burst compiler which optimizes for the wrong codepath.
        struct Two { }
        public static readonly SharedStatic<int> two = SharedStatic<int>.GetOrCreate<Two>();

        public static void InitConstants()
        {
            two.Data = 2;
        }
    }
}

