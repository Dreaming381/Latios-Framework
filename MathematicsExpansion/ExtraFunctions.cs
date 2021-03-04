using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Unity.Mathematics
{
    public static partial class math
    {
        /// <summary>Returns b if c is true, a otherwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool select(bool a, bool b, bool c)
        {
            return a ^ ((a ^ b) & c);
        }

        /// <summary>Returns b if c is true, a otherwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool2 select(bool2 a, bool2 b, bool c)
        {
            return a ^ ((a ^ b) & c);
        }

        /// <summary>Returns b if c is true, a otherwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool3 select(bool3 a, bool3 b, bool c)
        {
            return a ^ ((a ^ b) & c);
        }

        /// <summary>Returns b if c is true, a otherwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 select(bool4 a, bool4 b, bool c)
        {
            return a ^ ((a ^ b) & c);
        }

        /// <summary>
        /// Returns a componentwise selection between two bool2 vectors a and b based on a bool2 selection mask c.
        /// Per component, the component from b is selected when c is true, otherwise the component from a is selected.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool2 select(bool2 a, bool2 b, bool2 c)
        {
            return a ^ ((a ^ b) & c);
        }

        /// <summary>
        /// Returns a componentwise selection between two bool3 vectors a and b based on a bool3 selection mask c.
        /// Per component, the component from b is selected when c is true, otherwise the component from a is selected.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool3 select(bool3 a, bool3 b, bool3 c)
        {
            return a ^ ((a ^ b) & c);
        }

        /// <summary>
        /// Returns a componentwise selection between two bool4 vectors a and b based on a bool4 selection mask c.
        /// Per component, the component from b is selected when c is true, otherwise the component from a is selected.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 select(bool4 a, bool4 b, bool4 c)
        {
            return a ^ ((a ^ b) & c);
        }
    }
}

