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

        /// <summary>
        /// Returns the vector (or point) transformed by the inverse of the rotation. The rotation quaternion is assumed to be normalized.
        /// </summary>
        /// <param name="normalizedRotation">The rotaiton to inversely rotate the point by</param>
        /// <param name="vector">The vector or point to be inversely rotated</param>
        /// <returns>The resulting vector or point</returns>
        public static float3 InverseRotateFast(quaternion normalizedRotation, float3 vector)
        {
            return rotate(conjugate(normalizedRotation), vector);
        }

        /// <summary>
        /// Return the second quaternion rotated by the inverse of the assumed normalized first quaternion.
        /// </summary>
        /// <param name="normalizedRotation">The first quaternion which is assumed to be normalized (left side of mul)</param>
        /// <param name="rotationToBeRotated">The second quaternion (right side of mul)</param>
        /// <returns>The resulting quaternion that is rotated</returns>
        public static quaternion InverseRotateFast(quaternion normalizedRotation, quaternion rotationToBeRotated)
        {
            return mul(conjugate(normalizedRotation), rotationToBeRotated);
        }
    }

    public partial struct float3x4
    {
        public static float3x4 TRS(float3 translation, quaternion rotation, float3 scale)
        {
            float3x3 r = new float3x3(rotation);
            return new float3x4((r.c0 * scale.x),
                                (r.c1 * scale.y),
                                (r.c2 * scale.z),
                                (translation   ));
        }

        public static float3x4 Scale(float3 scale)
        {
            return new float3x4(new float3(scale.x, 0f, 0f),
                                new float3(0f, scale.y, 0f),
                                new float3(0f, 0f, scale.z),
                                float3.zero);
        }

        public static readonly float3x4 identity = new float3x4(new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), new float3(0f, 0f, 1f), float3.zero);
    }
}

