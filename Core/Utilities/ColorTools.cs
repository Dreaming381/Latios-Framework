using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios
{
    public static class ColorExtensions
    {
        public static float4 ToFloat4(this Color color)
        {
            return new float4(color.r, color.g, color.b, color.a);
        }

        public static half4 ToHalf4(this Color color)
        {
            return new half4(color.ToFloat4());
        }

        public static float4 ToFloat4(this Color32 color)
        {
            return ((Color)color).ToFloat4();
        }

        public static half4 ToHalf4(this Color32 color)
        {
            return ((Color)color).ToHalf4();
        }
    }
}

