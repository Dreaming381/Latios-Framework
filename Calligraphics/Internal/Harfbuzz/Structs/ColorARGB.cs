using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    // no byte-offset union with uint (StructLayout(Explicit))
    // because harfbuzz uint32_t hb_color_t follows big-endian layout to encode
    // BGRA color 0xBBGGRRAA (most consumer IT would require little endian).
    // So rather accessed this struct in endianness independant way via bit shifts
    // like harfbuzz
    internal struct ColorARGB : IEquatable<ColorARGB>
    {
        public byte a;
        public byte r;
        public byte g;
        public byte b;

        public uint argb
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                a = (byte)((value >> 24) & 0xFF);
                r = (byte)((value >> 16) & 0xFF);
                g = (byte)((value >> 8) & 0xFF);
                g = (byte)(value & 0xFF);
            }
        }

        public static void ConvertARGB32ToBGRA32(Texture2D sourceARGB, out NativeArray<ColorBGRA> destBGRA)
        {
            var pixels = sourceARGB.GetRawTextureData<ColorARGB>();  // Access raw data directly

            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel                            = pixels[i];
                (pixel.a, pixel.r, pixel.g, pixel.b) = (pixel.b, pixel.g, pixel.r, pixel.a);  //swap to convert from ARGB to BGRA
                pixels[i]                            = pixel;
            }
            destBGRA = pixels.Reinterpret<ColorBGRA>();
        }

        public override int GetHashCode() => argb.GetHashCode();

        public override bool Equals(object other)
        {
            if (other is ColorARGB other2)
                return Equals(other2);
            return false;
        }

        // Compare via the computed uint so channel order doesn't matter
        public bool Equals(ColorARGB other) => argb == other.argb;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => $"ARGB({b}, {g}, {r}, {a})";
    }
}

