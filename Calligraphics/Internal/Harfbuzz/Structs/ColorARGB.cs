using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct ColorARGB : IEquatable<ColorARGB>
    {
        [FieldOffset(0)]
        public uint argb;

        [FieldOffset(0)]
        public byte a;

        [FieldOffset(1)]
        public byte r;

        [FieldOffset(2)]
        public byte g;

        [FieldOffset(3)]
        public byte b;        

        public ColorARGB(byte a, byte r, byte g, byte b)
        {
            argb = 0;
            this.a = a;
            this.r = r;
            this.g = g;
            this.b = b; 
        }
        public ColorARGB(int a, int r, int g, int b)
        {
            argb = 0;
            this.a = (byte)a;
            this.r = (byte)r;
            this.g = (byte)g;
            this.b = (byte)b;
        }
        //public static implicit operator uint(ColorARGB c)
        //{
        //    return (((uint)c.b & 0xFF) << 24) | (((uint)c.g & 0xFF) << 16) | (((uint)c.r & 0xFF) << 8) | ((uint)c.a & 0xFF);
        //}
        public static implicit operator ColorARGB(uint c)
        {
            return new ColorARGB { argb = c };
        }
        //public static explicit operator ColorARGB(int4 c)
        //{
        //    return new ColorARGB { a = (byte)c[0], r = (byte)c[1], g = (byte)c[2], b = (byte)c[3] };
        //}
        //public static explicit operator int4(ColorARGB c)
        //{
        //    return new int4 (c.a,c.r,c.g,c.b);
        //}

        #region Color32 interoperability
        public static implicit operator ColorARGB(Color32 c)
        {
            return new ColorARGB(c.a, c.r, c.g, c.b);
        }

        public static implicit operator Color32(ColorARGB c)
        {
            return new Color32(c.r, c.g, c.b, c.a);
        }
        #endregion

        #region Color interoperability
        public static implicit operator ColorARGB(Color c)
        {
            return (Color32)c;
        }

        public static implicit operator Color(ColorARGB c)
        {
            return (Color32)c;
        }
        #endregion

        public static ColorARGB Lerp(ColorARGB a, ColorARGB b, float t)
        {
            t = math.saturate(t);
            return new ColorARGB(
                (byte)(a.a + (b.a - a.a) * t),
                (byte)(a.r + (b.r - a.r) * t),
                (byte)(a.g + (b.g - a.g) * t),
                (byte)(a.b + (b.b - a.b) * t));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorARGB LerpUnclamped(ColorARGB a, ColorARGB b, float t)
        {
            return new ColorARGB(
                (byte)(a.a + (b.a - a.a) * t), 
                (byte)(a.r + (b.r - a.r) * t), 
                (byte)(a.g + (b.g - a.g) * t), 
                (byte)(a.b + (b.b - a.b) * t));
        }

        public override int GetHashCode()
        {
            return argb.GetHashCode();
        }

        public override bool Equals(object other)
        {
            if (other is ColorARGB other2)
            {
                return Equals(other2);
            }

            return false;
        }

        public bool Equals(ColorARGB other)
        {
            return argb == other.argb;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            return $"ARGB {a} {r} {g} {b} ";
        }
    }
}
