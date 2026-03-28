using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    // no byte-offset union with uint (StructLayout(Explicit))
    // because harfbuzz uint32_t hb_color_t follows big-endian layout to encode
    // BGRA color 0xBBGGRRAA (most consumer IT would require little endian).
    // So rather accessed this struct in endianness independant way via bit shifts
    // like harfbuzz
    internal struct ColorBGRA : IEquatable<ColorBGRA>
    {
        public byte b;
        public byte g;
        public byte r;
        public byte a;

        // Computed property: assembles the uint the same way HarfBuzz does:
        // blue in MSB, alpha in LSB 0xBBGGRRAA
        public uint bgra
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ((uint)b << 24) | ((uint)g << 16) | ((uint)r << 8) | (uint)a;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                b = (byte)((value >> 24) & 0xFF);
                g = (byte)((value >> 16) & 0xFF);
                r = (byte)((value >> 8) & 0xFF);
                a = (byte)(value & 0xFF);
            }
        }

        public ColorBGRA(byte b, byte g, byte r, byte a)
        {
            this.b = b;
            this.g = g;
            this.r = r;
            this.a = a;
        }

        public ColorBGRA(int b, int g, int r, int a)
        {
            this.b = (byte)b;
            this.g = (byte)g;
            this.r = (byte)r;
            this.a = (byte)a;
        }

        // From HarfBuzz uint (0xBBGGRRAA) — uses the setter above
        public static implicit operator ColorBGRA(uint c)
        {
            var result  = default(ColorBGRA);
            result.bgra = c;
            return result;
        }

        // To HarfBuzz uint (0xBBGGRRAA) — uses the getter above
        public static implicit operator uint(ColorBGRA c) => c.bgra;

        #region int4 interoperability
        public static explicit operator ColorBGRA(int4 c)
        {
            return new ColorBGRA(c[0], c[1], c[2], c[3]);
        }

        public static explicit operator int4(ColorBGRA c)
        {
            return new int4(c.b, c.g, c.r, c.a);
        }
        #endregion

        #region Color32 interoperability
        // Color32 is always RGBA regardless of platform, so map explicitly
        public static implicit operator ColorBGRA(Color32 c)
        {
            return new ColorBGRA(c.b, c.g, c.r, c.a);
        }

        public static implicit operator Color32(ColorBGRA c)
        {
            return new Color32(c.r, c.g, c.b, c.a);
        }
        #endregion

        #region Color interoperability
        public static implicit operator ColorBGRA(Color c)
        {
            return (Color32)c;
        }

        public static implicit operator Color(ColorBGRA c)
        {
            return (Color32)c;
        }
        #endregion

        public static ColorBGRA Lerp(ColorBGRA x, ColorBGRA y, float t)
        {
            t = math.saturate(t);
            return new ColorBGRA(
                (byte)(x.b + (y.b - x.b) * t),
                (byte)(x.g + (y.g - x.g) * t),
                (byte)(x.r + (y.r - x.r) * t),
                (byte)(x.a + (y.a - x.a) * t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ColorBGRA LerpUnclamped(ColorBGRA x, ColorBGRA y, float t)
        {
            return new ColorBGRA(
                (byte)(x.b + (y.b - x.b) * t),
                (byte)(x.g + (y.g - x.g) * t),
                (byte)(x.r + (y.r - x.r) * t),
                (byte)(x.a + (y.a - x.a) * t));
        }

        public override int GetHashCode() => bgra.GetHashCode();

        public override bool Equals(object other)
        {
            if (other is ColorBGRA other2)
                return Equals(other2);
            return false;
        }

        // Compare via the computed uint so channel order doesn't matter
        public bool Equals(ColorBGRA other) => bgra == other.bgra;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => $"BGRA({b}, {g}, {r}, {a})";
    }
}

