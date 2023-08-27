using System;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace Latios.Calligraphics
{
    [Serializable]
    [BurstCompile]
    public static class Interpolation
    {
        public static float Interpolate(float from, float to, float normalizedTime, InterpolationType interpolation = InterpolationType.Linear)
        {
            return interpolation switch
                   {
                       InterpolationType.Linear => Lerp(from, to, normalizedTime),
                       InterpolationType.QuadIn => QuadIn(from, to, normalizedTime),
                       InterpolationType.QuadOut => QuadOut(from, to, normalizedTime),
                       InterpolationType.QuadInOut => QuadInOut(from, to, normalizedTime),
                       InterpolationType.QuadOutIn => QuadOutIn(from, to, normalizedTime),
                       InterpolationType.CubicIn => CubicIn(from, to, normalizedTime),
                       InterpolationType.CubicOut => CubicOut(from, to, normalizedTime),
                       InterpolationType.CubicInOut => CubicInOut(from, to, normalizedTime),
                       InterpolationType.CubicOutIn => CubicOutIn(from, to, normalizedTime),
                       InterpolationType.QuartIn => QuartIn(from, to, normalizedTime),
                       InterpolationType.QuartOut => QuartOut(from, to, normalizedTime),
                       InterpolationType.QuartInOut => QuartInOut(from, to, normalizedTime),
                       InterpolationType.QuartOutIn => QuartOutIn(from, to, normalizedTime),
                       InterpolationType.QuintIn => QuintIn(from, to, normalizedTime),
                       InterpolationType.QuintOut => QuintOut(from, to, normalizedTime),
                       InterpolationType.QuintInOut => QuintInOut(from, to, normalizedTime),
                       InterpolationType.QuintOutIn => QuintOutIn(from, to, normalizedTime),
                       InterpolationType.SineIn => SineIn(from, to, normalizedTime),
                       InterpolationType.SineOut => SineOut(from, to, normalizedTime),
                       InterpolationType.SineInOut => SineInOut(from, to, normalizedTime),
                       InterpolationType.SineOutIn => SineOutIn(from, to, normalizedTime),
                       InterpolationType.ExpoIn => ExpoIn(from, to, normalizedTime),
                       InterpolationType.ExpoOut => ExpoOut(from, to, normalizedTime),
                       InterpolationType.ExpoInOut => ExpoInOut(from, to, normalizedTime),
                       InterpolationType.ExpoOutIn => ExpoOutIn(from, to, normalizedTime),
                       InterpolationType.CircIn => CircIn(from, to, normalizedTime),
                       InterpolationType.CircOut => CircOut(from, to, normalizedTime),
                       InterpolationType.CircInOut => CircInOut(from, to, normalizedTime),
                       InterpolationType.CircOutIn => CircOutIn(from, to, normalizedTime),
                       InterpolationType.ElasticIn => ElasticIn(from, to, normalizedTime),
                       InterpolationType.ElasticOut => ElasticOut(from, to, normalizedTime),
                       InterpolationType.ElasticInOut => ElasticInOut(from, to, normalizedTime),
                       InterpolationType.ElasticOutIn => ElasticOutIn(from, to, normalizedTime),
                       InterpolationType.BackIn => BackIn(from, to, normalizedTime),
                       InterpolationType.BackOut => BackOut(from, to, normalizedTime),
                       InterpolationType.BackInOut => BackInOut(from, to, normalizedTime),
                       InterpolationType.BackOutIn => BackOutIn(from, to, normalizedTime),
                       InterpolationType.BounceIn => BounceIn(from, to, normalizedTime),
                       InterpolationType.BounceOut => BounceOut(from, to, normalizedTime),
                       InterpolationType.BounceInOut => BounceInOut(from, to, normalizedTime),
                       InterpolationType.BounceOutIn => BounceOutIn(from, to, normalizedTime),
                       _ => 0.0f
                   };
        }

        public static float2 Interpolate(float2 from, float2 to, float normalizedTime, InterpolationType interpolation = InterpolationType.Linear)
        {
            return interpolation switch
                   {
                       InterpolationType.Linear => Lerp(from, to, normalizedTime),
                       InterpolationType.QuadIn => QuadIn(from, to, normalizedTime),
                       InterpolationType.QuadOut => QuadOut(from, to, normalizedTime),
                       InterpolationType.QuadInOut => QuadInOut(from, to, normalizedTime),
                       InterpolationType.QuadOutIn => QuadOutIn(from, to, normalizedTime),
                       InterpolationType.CubicIn => CubicIn(from, to, normalizedTime),
                       InterpolationType.CubicOut => CubicOut(from, to, normalizedTime),
                       InterpolationType.CubicInOut => CubicInOut(from, to, normalizedTime),
                       InterpolationType.CubicOutIn => CubicOutIn(from, to, normalizedTime),
                       InterpolationType.QuartIn => QuartIn(from, to, normalizedTime),
                       InterpolationType.QuartOut => QuartOut(from, to, normalizedTime),
                       InterpolationType.QuartInOut => QuartInOut(from, to, normalizedTime),
                       InterpolationType.QuartOutIn => QuartOutIn(from, to, normalizedTime),
                       InterpolationType.QuintIn => QuintIn(from, to, normalizedTime),
                       InterpolationType.QuintOut => QuintOut(from, to, normalizedTime),
                       InterpolationType.QuintInOut => QuintInOut(from, to, normalizedTime),
                       InterpolationType.QuintOutIn => QuintOutIn(from, to, normalizedTime),
                       InterpolationType.SineIn => SineIn(from, to, normalizedTime),
                       InterpolationType.SineOut => SineOut(from, to, normalizedTime),
                       InterpolationType.SineInOut => SineInOut(from, to, normalizedTime),
                       InterpolationType.SineOutIn => SineOutIn(from, to, normalizedTime),
                       InterpolationType.ExpoIn => ExpoIn(from, to, normalizedTime),
                       InterpolationType.ExpoOut => ExpoOut(from, to, normalizedTime),
                       InterpolationType.ExpoInOut => ExpoInOut(from, to, normalizedTime),
                       InterpolationType.ExpoOutIn => ExpoOutIn(from, to, normalizedTime),
                       InterpolationType.CircIn => CircIn(from, to, normalizedTime),
                       InterpolationType.CircOut => CircOut(from, to, normalizedTime),
                       InterpolationType.CircInOut => CircInOut(from, to, normalizedTime),
                       InterpolationType.CircOutIn => CircOutIn(from, to, normalizedTime),
                       InterpolationType.ElasticIn => ElasticIn(from, to, normalizedTime),
                       InterpolationType.ElasticOut => ElasticOut(from, to, normalizedTime),
                       InterpolationType.ElasticInOut => ElasticInOut(from, to, normalizedTime),
                       InterpolationType.ElasticOutIn => ElasticOutIn(from, to, normalizedTime),
                       InterpolationType.BackIn => BackIn(from, to, normalizedTime),
                       InterpolationType.BackOut => BackOut(from, to, normalizedTime),
                       InterpolationType.BackInOut => BackInOut(from, to, normalizedTime),
                       InterpolationType.BackOutIn => BackOutIn(from, to, normalizedTime),
                       InterpolationType.BounceIn => BounceIn(from, to, normalizedTime),
                       InterpolationType.BounceOut => BounceOut(from, to, normalizedTime),
                       InterpolationType.BounceInOut => BounceInOut(from, to, normalizedTime),
                       InterpolationType.BounceOutIn => BounceOutIn(from, to, normalizedTime),
                       _ => new float2()
                   };
        }

        public static float3 Interpolate(float3 from, float3 to, float normalizedTime, InterpolationType interpolation = InterpolationType.Linear)
        {
            return interpolation switch
                   {
                       InterpolationType.Linear => Lerp(from, to, normalizedTime),
                       InterpolationType.QuadIn => QuadIn(from, to, normalizedTime),
                       InterpolationType.QuadOut => QuadOut(from, to, normalizedTime),
                       InterpolationType.QuadInOut => QuadInOut(from, to, normalizedTime),
                       InterpolationType.QuadOutIn => QuadOutIn(from, to, normalizedTime),
                       InterpolationType.CubicIn => CubicIn(from, to, normalizedTime),
                       InterpolationType.CubicOut => CubicOut(from, to, normalizedTime),
                       InterpolationType.CubicInOut => CubicInOut(from, to, normalizedTime),
                       InterpolationType.CubicOutIn => CubicOutIn(from, to, normalizedTime),
                       InterpolationType.QuartIn => QuartIn(from, to, normalizedTime),
                       InterpolationType.QuartOut => QuartOut(from, to, normalizedTime),
                       InterpolationType.QuartInOut => QuartInOut(from, to, normalizedTime),
                       InterpolationType.QuartOutIn => QuartOutIn(from, to, normalizedTime),
                       InterpolationType.QuintIn => QuintIn(from, to, normalizedTime),
                       InterpolationType.QuintOut => QuintOut(from, to, normalizedTime),
                       InterpolationType.QuintInOut => QuintInOut(from, to, normalizedTime),
                       InterpolationType.QuintOutIn => QuintOutIn(from, to, normalizedTime),
                       InterpolationType.SineIn => SineIn(from, to, normalizedTime),
                       InterpolationType.SineOut => SineOut(from, to, normalizedTime),
                       InterpolationType.SineInOut => SineInOut(from, to, normalizedTime),
                       InterpolationType.SineOutIn => SineOutIn(from, to, normalizedTime),
                       InterpolationType.ExpoIn => ExpoIn(from, to, normalizedTime),
                       InterpolationType.ExpoOut => ExpoOut(from, to, normalizedTime),
                       InterpolationType.ExpoInOut => ExpoInOut(from, to, normalizedTime),
                       InterpolationType.ExpoOutIn => ExpoOutIn(from, to, normalizedTime),
                       InterpolationType.CircIn => CircIn(from, to, normalizedTime),
                       InterpolationType.CircOut => CircOut(from, to, normalizedTime),
                       InterpolationType.CircInOut => CircInOut(from, to, normalizedTime),
                       InterpolationType.CircOutIn => CircOutIn(from, to, normalizedTime),
                       InterpolationType.ElasticIn => ElasticIn(from, to, normalizedTime),
                       InterpolationType.ElasticOut => ElasticOut(from, to, normalizedTime),
                       InterpolationType.ElasticInOut => ElasticInOut(from, to, normalizedTime),
                       InterpolationType.ElasticOutIn => ElasticOutIn(from, to, normalizedTime),
                       InterpolationType.BackIn => BackIn(from, to, normalizedTime),
                       InterpolationType.BackOut => BackOut(from, to, normalizedTime),
                       InterpolationType.BackInOut => BackInOut(from, to, normalizedTime),
                       InterpolationType.BackOutIn => BackOutIn(from, to, normalizedTime),
                       InterpolationType.BounceIn => BounceIn(from, to, normalizedTime),
                       InterpolationType.BounceOut => BounceOut(from, to, normalizedTime),
                       InterpolationType.BounceInOut => BounceInOut(from, to, normalizedTime),
                       InterpolationType.BounceOutIn => BounceOutIn(from, to, normalizedTime),
                       _ => new float3()
                   };
        }

        public static Color32 Interpolate(Color32 from, Color32 to, float normalizedTime, InterpolationType interpolation = InterpolationType.Linear)
        {
            return interpolation switch
                   {
                       InterpolationType.Linear => Lerp(from, to, normalizedTime),
                       InterpolationType.QuadIn => QuadIn(from, to, normalizedTime),
                       InterpolationType.QuadOut => QuadOut(from, to, normalizedTime),
                       InterpolationType.QuadInOut => QuadInOut(from, to, normalizedTime),
                       InterpolationType.QuadOutIn => QuadOutIn(from, to, normalizedTime),
                       InterpolationType.CubicIn => CubicIn(from, to, normalizedTime),
                       InterpolationType.CubicOut => CubicOut(from, to, normalizedTime),
                       InterpolationType.CubicInOut => CubicInOut(from, to, normalizedTime),
                       InterpolationType.CubicOutIn => CubicOutIn(from, to, normalizedTime),
                       InterpolationType.QuartIn => QuartIn(from, to, normalizedTime),
                       InterpolationType.QuartOut => QuartOut(from, to, normalizedTime),
                       InterpolationType.QuartInOut => QuartInOut(from, to, normalizedTime),
                       InterpolationType.QuartOutIn => QuartOutIn(from, to, normalizedTime),
                       InterpolationType.QuintIn => QuintIn(from, to, normalizedTime),
                       InterpolationType.QuintOut => QuintOut(from, to, normalizedTime),
                       InterpolationType.QuintInOut => QuintInOut(from, to, normalizedTime),
                       InterpolationType.QuintOutIn => QuintOutIn(from, to, normalizedTime),
                       InterpolationType.SineIn => SineIn(from, to, normalizedTime),
                       InterpolationType.SineOut => SineOut(from, to, normalizedTime),
                       InterpolationType.SineInOut => SineInOut(from, to, normalizedTime),
                       InterpolationType.SineOutIn => SineOutIn(from, to, normalizedTime),
                       InterpolationType.ExpoIn => ExpoIn(from, to, normalizedTime),
                       InterpolationType.ExpoOut => ExpoOut(from, to, normalizedTime),
                       InterpolationType.ExpoInOut => ExpoInOut(from, to, normalizedTime),
                       InterpolationType.ExpoOutIn => ExpoOutIn(from, to, normalizedTime),
                       InterpolationType.CircIn => CircIn(from, to, normalizedTime),
                       InterpolationType.CircOut => CircOut(from, to, normalizedTime),
                       InterpolationType.CircInOut => CircInOut(from, to, normalizedTime),
                       InterpolationType.CircOutIn => CircOutIn(from, to, normalizedTime),
                       InterpolationType.ElasticIn => ElasticIn(from, to, normalizedTime),
                       InterpolationType.ElasticOut => ElasticOut(from, to, normalizedTime),
                       InterpolationType.ElasticInOut => ElasticInOut(from, to, normalizedTime),
                       InterpolationType.ElasticOutIn => ElasticOutIn(from, to, normalizedTime),
                       InterpolationType.BackIn => BackIn(from, to, normalizedTime),
                       InterpolationType.BackOut => BackOut(from, to, normalizedTime),
                       InterpolationType.BackInOut => BackInOut(from, to, normalizedTime),
                       InterpolationType.BackOutIn => BackOutIn(from, to, normalizedTime),
                       InterpolationType.BounceIn => BounceIn(from, to, normalizedTime),
                       InterpolationType.BounceOut => BounceOut(from, to, normalizedTime),
                       InterpolationType.BounceInOut => BounceInOut(from, to, normalizedTime),
                       InterpolationType.BounceOutIn => BounceOutIn(from, to, normalizedTime),
                       _ => new Color32()
                   };
        }

        private static float Lerp(float from, float to, float t)
        {
            return In(new Linear(), t, from, to - from);
        }

        private static float2 Lerp(float2 from, float2 to, float t)
        {
            return In(new Linear(), t, from, to - from);
        }

        private static float3 Lerp(float3 from, float3 to, float t)
        {
            return In(new Linear(), t, from, to - from);
        }

        private static Color32 Lerp(Color32 from, Color32 to, float t)
        {
            return In(new Linear(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuadIn(float from, float to, float t)
        {
            return In(new Quad(), t, from, to - from);
        }

        private static float2 QuadIn(float2 from, float2 to, float t)
        {
            return In(new Quad(), t, from, to - from);
        }

        private static float3 QuadIn(float3 from, float3 to, float t)
        {
            return In(new Quad(), t, from, to - from);
        }

        private static Color32 QuadIn(Color32 from, Color32 to, float t)
        {
            return In(new Quad(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuadOut(float from, float to, float t)
        {
            return Out(new Quad(), t, from, to - from);
        }

        private static float2 QuadOut(float2 from, float2 to, float t)
        {
            return Out(new Quad(), t, from, to - from);
        }

        private static float3 QuadOut(float3 from, float3 to, float t)
        {
            return Out(new Quad(), t, from, to - from);
        }

        private static Color32 QuadOut(Color32 from, Color32 to, float t)
        {
            return Out(new Quad(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuadInOut(float from, float to, float t)
        {
            return InOut(new Quad(), t, from, to - from);
        }

        private static float2 QuadInOut(float2 from, float2 to, float t)
        {
            return InOut(new Quad(), t, from, to - from);
        }

        private static float3 QuadInOut(float3 from, float3 to, float t)
        {
            return InOut(new Quad(), t, from, to - from);
        }

        private static Color32 QuadInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Quad(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuadOutIn(float from, float to, float t)
        {
            return OutIn(new Quad(), t, from, to - from);
        }

        private static float2 QuadOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Quad(), t, from, to - from);
        }

        private static float3 QuadOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Quad(), t, from, to - from);
        }

        private static Color32 QuadOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Quad(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float CubicIn(float from, float to, float t)
        {
            return In(new Cubic(), t, from, to - from);
        }

        private static float2 CubicIn(float2 from, float2 to, float t)
        {
            return In(new Cubic(), t, from, to - from);
        }

        private static float3 CubicIn(float3 from, float3 to, float t)
        {
            return In(new Cubic(), t, from, to - from);
        }

        private static Color32 CubicIn(Color32 from, Color32 to, float t)
        {
            return In(new Cubic(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float CubicOut(float from, float to, float t)
        {
            return Out(new Cubic(), t, from, to - from);
        }

        private static float2 CubicOut(float2 from, float2 to, float t)
        {
            return Out(new Cubic(), t, from, to - from);
        }

        private static float3 CubicOut(float3 from, float3 to, float t)
        {
            return Out(new Cubic(), t, from, to - from);
        }

        private static Color32 CubicOut(Color32 from, Color32 to, float t)
        {
            return Out(new Cubic(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float CubicInOut(float from, float to, float t)
        {
            return InOut(new Cubic(), t, from, to - from);
        }

        private static float2 CubicInOut(float2 from, float2 to, float t)
        {
            return InOut(new Cubic(), t, from, to - from);
        }

        private static float3 CubicInOut(float3 from, float3 to, float t)
        {
            return InOut(new Cubic(), t, from, to - from);
        }

        private static Color32 CubicInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Cubic(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float CubicOutIn(float from, float to, float t)
        {
            return OutIn(new Cubic(), t, from, to - from);
        }

        private static float2 CubicOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Cubic(), t, from, to - from);
        }

        private static float3 CubicOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Cubic(), t, from, to - from);
        }

        private static Color32 CubicOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Cubic(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuartIn(float from, float to, float t)
        {
            return In(new Quart(), t, from, to - from);
        }

        private static float2 QuartIn(float2 from, float2 to, float t)
        {
            return In(new Quart(), t, from, to - from);
        }

        private static float3 QuartIn(float3 from, float3 to, float t)
        {
            return In(new Quart(), t, from, to - from);
        }

        private static Color32 QuartIn(Color32 from, Color32 to, float t)
        {
            return In(new Quart(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuartOut(float from, float to, float t)
        {
            return Out(new Quart(), t, from, to - from);
        }

        private static float2 QuartOut(float2 from, float2 to, float t)
        {
            return Out(new Quart(), t, from, to - from);
        }

        private static float3 QuartOut(float3 from, float3 to, float t)
        {
            return Out(new Quart(), t, from, to - from);
        }

        private static Color32 QuartOut(Color32 from, Color32 to, float t)
        {
            return Out(new Quart(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuartInOut(float from, float to, float t)
        {
            return InOut(new Quart(), t, from, to - from);
        }

        private static float2 QuartInOut(float2 from, float2 to, float t)
        {
            return InOut(new Quart(), t, from, to - from);
        }

        private static float3 QuartInOut(float3 from, float3 to, float t)
        {
            return InOut(new Quart(), t, from, to - from);
        }

        private static Color32 QuartInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Quart(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuartOutIn(float from, float to, float t)
        {
            return OutIn(new Quart(), t, from, to - from);
        }

        private static float2 QuartOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Quart(), t, from, to - from);
        }

        private static float3 QuartOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Quart(), t, from, to - from);
        }

        private static Color32 QuartOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Quart(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuintIn(float from, float to, float t)
        {
            return In(new Quint(), t, from, to - from);
        }

        private static float2 QuintIn(float2 from, float2 to, float t)
        {
            return In(new Quint(), t, from, to - from);
        }

        private static float3 QuintIn(float3 from, float3 to, float t)
        {
            return In(new Quint(), t, from, to - from);
        }

        private static Color32 QuintIn(Color32 from, Color32 to, float t)
        {
            return In(new Quint(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuintOut(float from, float to, float t)
        {
            return Out(new Quint(), t, from, to - from);
        }

        private static float2 QuintOut(float2 from, float2 to, float t)
        {
            return Out(new Quint(), t, from, to - from);
        }

        private static float3 QuintOut(float3 from, float3 to, float t)
        {
            return Out(new Quint(), t, from, to - from);
        }

        private static Color32 QuintOut(Color32 from, Color32 to, float t)
        {
            return Out(new Quint(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuintInOut(float from, float to, float t)
        {
            return InOut(new Quint(), t, from, to - from);
        }

        private static float2 QuintInOut(float2 from, float2 to, float t)
        {
            return InOut(new Quint(), t, from, to - from);
        }

        private static float3 QuintInOut(float3 from, float3 to, float t)
        {
            return InOut(new Quint(), t, from, to - from);
        }

        private static Color32 QuintInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Quint(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float QuintOutIn(float from, float to, float t)
        {
            return OutIn(new Quint(), t, from, to - from);
        }

        private static float2 QuintOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Quint(), t, from, to - from);
        }

        private static float3 QuintOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Quint(), t, from, to - from);
        }

        private static Color32 QuintOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Quint(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float SineIn(float from, float to, float t)
        {
            return In(new Sine(), t, from, to - from);
        }

        private static float2 SineIn(float2 from, float2 to, float t)
        {
            return In(new Sine(), t, from, to - from);
        }

        private static float3 SineIn(float3 from, float3 to, float t)
        {
            return In(new Sine(), t, from, to - from);
        }

        private static Color32 SineIn(Color32 from, Color32 to, float t)
        {
            return In(new Sine(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float SineOut(float from, float to, float t)
        {
            return Out(new Sine(), t, from, to - from);
        }

        private static float2 SineOut(float2 from, float2 to, float t)
        {
            return Out(new Sine(), t, from, to - from);
        }

        private static float3 SineOut(float3 from, float3 to, float t)
        {
            return Out(new Sine(), t, from, to - from);
        }

        private static Color32 SineOut(Color32 from, Color32 to, float t)
        {
            return Out(new Sine(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float SineInOut(float from, float to, float t)
        {
            return InOut(new Sine(), t, from, to - from);
        }

        private static float2 SineInOut(float2 from, float2 to, float t)
        {
            return InOut(new Sine(), t, from, to - from);
        }

        private static float3 SineInOut(float3 from, float3 to, float t)
        {
            return InOut(new Sine(), t, from, to - from);
        }

        private static Color32 SineInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Sine(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float SineOutIn(float from, float to, float t)
        {
            return OutIn(new Sine(), t, from, to - from);
        }

        private static float2 SineOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Sine(), t, from, to - from);
        }

        private static float3 SineOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Sine(), t, from, to - from);
        }

        private static Color32 SineOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Sine(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float ExpoIn(float from, float to, float t)
        {
            return In(new Expo(), t, from, to - from);
        }

        private static float2 ExpoIn(float2 from, float2 to, float t)
        {
            return In(new Expo(), t, from, to - from);
        }

        private static float3 ExpoIn(float3 from, float3 to, float t)
        {
            return In(new Expo(), t, from, to - from);
        }

        private static Color32 ExpoIn(Color32 from, Color32 to, float t)
        {
            return In(new Expo(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float ExpoOut(float from, float to, float t)
        {
            return Out(new Expo(), t, from, to - from);
        }

        private static float2 ExpoOut(float2 from, float2 to, float t)
        {
            return Out(new Expo(), t, from, to - from);
        }

        private static float3 ExpoOut(float3 from, float3 to, float t)
        {
            return Out(new Expo(), t, from, to - from);
        }

        private static Color32 ExpoOut(Color32 from, Color32 to, float t)
        {
            return Out(new Expo(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float ExpoInOut(float from, float to, float t)
        {
            return InOut(new Expo(), t, from, to - from);
        }

        private static float2 ExpoInOut(float2 from, float2 to, float t)
        {
            return InOut(new Expo(), t, from, to - from);
        }

        private static float3 ExpoInOut(float3 from, float3 to, float t)
        {
            return InOut(new Expo(), t, from, to - from);
        }

        private static Color32 ExpoInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Expo(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float ExpoOutIn(float from, float to, float t)
        {
            return OutIn(new Expo(), t, from, to - from);
        }

        private static float2 ExpoOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Expo(), t, from, to - from);
        }

        private static float3 ExpoOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Expo(), t, from, to - from);
        }

        private static Color32 ExpoOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Expo(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float CircIn(float from, float to, float t)
        {
            return In(new Circ(), t, from, to - from);
        }

        private static float2 CircIn(float2 from, float2 to, float t)
        {
            return In(new Circ(), t, from, to - from);
        }

        private static float3 CircIn(float3 from, float3 to, float t)
        {
            return In(new Circ(), t, from, to - from);
        }

        private static Color32 CircIn(Color32 from, Color32 to, float t)
        {
            return In(new Circ(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float CircOut(float from, float to, float t)
        {
            return Out(new Circ(), t, from, to - from);
        }

        private static float2 CircOut(float2 from, float2 to, float t)
        {
            return Out(new Circ(), t, from, to - from);
        }

        private static float3 CircOut(float3 from, float3 to, float t)
        {
            return Out(new Circ(), t, from, to - from);
        }

        private static Color32 CircOut(Color32 from, Color32 to, float t)
        {
            return Out(new Circ(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float CircInOut(float from, float to, float t)
        {
            return InOut(new Circ(), t, from, to - from);
        }

        private static float2 CircInOut(float2 from, float2 to, float t)
        {
            return InOut(new Circ(), t, from, to - from);
        }

        private static float3 CircInOut(float3 from, float3 to, float t)
        {
            return InOut(new Circ(), t, from, to - from);
        }

        private static Color32 CircInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Circ(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float CircOutIn(float from, float to, float t)
        {
            return OutIn(new Circ(), t, from, to - from);
        }

        private static float2 CircOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Circ(), t, from, to - from);
        }

        private static float3 CircOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Circ(), t, from, to - from);
        }

        private static Color32 CircOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Circ(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float ElasticIn(float from, float to, float t)
        {
            return In(new Elastic(), t, from, to - from);
        }

        private static float2 ElasticIn(float2 from, float2 to, float t)
        {
            return In(new Elastic(), t, from, to - from);
        }

        private static float3 ElasticIn(float3 from, float3 to, float t)
        {
            return In(new Elastic(), t, from, to - from);
        }

        private static Color32 ElasticIn(Color32 from, Color32 to, float t)
        {
            return In(new Elastic(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float ElasticOut(float from, float to, float t)
        {
            return Out(new Elastic(), t, from, to - from);
        }

        private static float2 ElasticOut(float2 from, float2 to, float t)
        {
            return Out(new Elastic(), t, from, to - from);
        }

        private static float3 ElasticOut(float3 from, float3 to, float t)
        {
            return Out(new Elastic(), t, from, to - from);
        }

        private static Color32 ElasticOut(Color32 from, Color32 to, float t)
        {
            return Out(new Elastic(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float ElasticInOut(float from, float to, float t)
        {
            return InOut(new Elastic(), t, from, to - from);
        }

        private static float2 ElasticInOut(float2 from, float2 to, float t)
        {
            return InOut(new Elastic(), t, from, to - from);
        }

        private static float3 ElasticInOut(float3 from, float3 to, float t)
        {
            return InOut(new Elastic(), t, from, to - from);
        }

        private static Color32 ElasticInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Elastic(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float ElasticOutIn(float from, float to, float t)
        {
            return OutIn(new Elastic(), t, from, to - from);
        }

        private static float2 ElasticOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Elastic(), t, from, to - from);
        }

        private static float3 ElasticOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Elastic(), t, from, to - from);
        }

        private static Color32 ElasticOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Elastic(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float BackIn(float from, float to, float t)
        {
            return In(new Back(), t, from, to - from);
        }

        private static float2 BackIn(float2 from, float2 to, float t)
        {
            return In(new Back(), t, from, to - from);
        }

        private static float3 BackIn(float3 from, float3 to, float t)
        {
            return In(new Back(), t, from, to - from);
        }

        private static Color32 BackIn(Color32 from, Color32 to, float t)
        {
            return In(new Back(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float BackOut(float from, float to, float t)
        {
            return Out(new Back(), t, from, to - from);
        }

        private static float2 BackOut(float2 from, float2 to, float t)
        {
            return Out(new Back(), t, from, to - from);
        }

        private static float3 BackOut(float3 from, float3 to, float t)
        {
            return Out(new Back(), t, from, to - from);
        }

        private static Color32 BackOut(Color32 from, Color32 to, float t)
        {
            return Out(new Back(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float BackInOut(float from, float to, float t)
        {
            return InOut(new Back(), t, from, to - from);
        }

        private static float2 BackInOut(float2 from, float2 to, float t)
        {
            return InOut(new Back(), t, from, to - from);
        }

        private static float3 BackInOut(float3 from, float3 to, float t)
        {
            return InOut(new Back(), t, from, to - from);
        }

        private static Color32 BackInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Back(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float BackOutIn(float from, float to, float t)
        {
            return OutIn(new Back(), t, from, to - from);
        }

        private static float2 BackOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Back(), t, from, to - from);
        }

        private static float3 BackOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Back(), t, from, to - from);
        }

        private static Color32 BackOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Back(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float BounceIn(float from, float to, float t)
        {
            return In(new Bounce(), t, from, to - from);
        }

        private static float2 BounceIn(float2 from, float2 to, float t)
        {
            return In(new Bounce(), t, from, to - from);
        }

        private static float3 BounceIn(float3 from, float3 to, float t)
        {
            return In(new Bounce(), t, from, to - from);
        }

        private static Color32 BounceIn(Color32 from, Color32 to, float t)
        {
            return In(new Bounce(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float BounceOut(float from, float to, float t)
        {
            return Out(new Bounce(), t, from, to - from);
        }

        private static float2 BounceOut(float2 from, float2 to, float t)
        {
            return Out(new Bounce(), t, from, to - from);
        }

        private static float3 BounceOut(float3 from, float3 to, float t)
        {
            return Out(new Bounce(), t, from, to - from);
        }

        private static Color32 BounceOut(Color32 from, Color32 to, float t)
        {
            return Out(new Bounce(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float BounceInOut(float from, float to, float t)
        {
            return InOut(new Bounce(), t, from, to - from);
        }

        private static float2 BounceInOut(float2 from, float2 to, float t)
        {
            return InOut(new Bounce(), t, from, to - from);
        }

        private static float3 BounceInOut(float3 from, float3 to, float t)
        {
            return InOut(new Bounce(), t, from, to - from);
        }

        private static Color32 BounceInOut(Color32 from, Color32 to, float t)
        {
            return InOut(new Bounce(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float BounceOutIn(float from, float to, float t)
        {
            return OutIn(new Bounce(), t, from, to - from);
        }

        private static float2 BounceOutIn(float2 from, float2 to, float t)
        {
            return OutIn(new Bounce(), t, from, to - from);
        }

        private static float3 BounceOutIn(float3 from, float3 to, float t)
        {
            return OutIn(new Bounce(), t, from, to - from);
        }

        private static Color32 BounceOutIn(Color32 from, Color32 to, float t)
        {
            return OutIn(new Bounce(), t, from, new Color32 ((byte)(to.r - from.r), (byte)(to.g - from.g), (byte)(to.b - from.b), (byte)(to.a - from.a)));
        }

        private static float In<T>(T interpolator, float time, float a, float b, float delta = 1) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;

            return b * interpolator.Invoke(time, delta) + a;
        }

        private static float2 In<T>(T interpolator, float time, float2 a, float2 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;

            return b * interpolator.Invoke(time, delta) + a;
        }

        private static float3 In<T>(T interpolator, float time, float3 a, float3 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;

            return b * interpolator.Invoke(time, delta) + a;
        }

        private static Color32 In<T>(T interpolator, float time, Color32 a, Color32 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return new Color32 { r = (byte)(a.r + b.r), g = (byte)(a.g + b.g), b = (byte)(a.b + b.b), a = (byte)(a.a + b.a) };
            if (time <= 0)
                return a;

            var t = interpolator.Invoke(time, delta);

            return Color32.Lerp(a, b, t);
            //return new Color32( (byte)(b.r * t + a.r), (byte)(b.g * t + a.g), (byte)(b.b * t + a.b), (byte)(b.a * t + a.a) );
        }

        private static float Out<T>(T interpolator, float time, float a, float b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;

            return (a + b) - b * interpolator.Invoke(delta - time, delta);
        }

        private static float2 Out<T>(T interpolator, float time, float2 a, float2 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;

            return (a + b) - b * interpolator.Invoke(delta - time, delta);
        }

        private static float3 Out<T>(T interpolator, float time, float3 a, float3 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;

            return (a + b) - b * interpolator.Invoke(delta - time, delta);
        }

        private static Color32 Out<T>(T interpolator, float time, Color32 a, Color32 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return new Color32 { r = (byte)(a.r + b.r), g = (byte)(a.g + b.g), b = (byte)(a.b + b.b), a = (byte)(a.a + b.a) };
            if (time <= 0)
                return a;

            var t = interpolator.Invoke(delta - time, delta);
            return Color32.Lerp(b, a, t);
            //return new Color32( (byte)((b.r + a.r) - b.r * t), (byte)((b.g + a.g) - b.g * t), (byte)((b.b + a.b) - b.b * t), (byte)((b.a + a.a) - b.a * t) );
        }

        private static float InOut<T>(T interpolator, float time, float a, float b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;
            if (time < delta / 2)
                return In(interpolator, time * 2, a, b / 2, delta);

            return Out(interpolator, (time * 2) - delta, a + b / 2, b / 2, delta);
        }

        private static float2 InOut<T>(T interpolator, float time, float2 a, float2 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;
            if (time < delta / 2)
                return In(interpolator, time * 2, a, b / 2, delta);

            return Out(interpolator, (time * 2) - delta, a + b / 2, b / 2, delta);
        }

        private static float3 InOut<T>(T interpolator, float time, float3 a, float3 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;
            if (time < delta / 2)
                return In(interpolator, time * 2, a, b / 2, delta);

            return Out(interpolator, (time * 2) - delta, a + b / 2, b / 2, delta);
        }

        private static Color32 InOut<T>(T interpolator, float time, Color32 a, Color32 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return new Color32 { r = (byte)(a.r + b.r), g = (byte)(a.g + b.g), b = (byte)(a.b + b.b), a = (byte)(a.a + b.a) };
            if (time <= 0)
                return a;
            if (time < delta / 2)
                return In(interpolator, time * 2, a, new Color32((byte)(b.r / 2), (byte)(b.g / 2), (byte)(b.b / 2), (byte)(b.a / 2)), delta);

            Color32 halfB = new Color32((byte)(b.r / 2), (byte)(b.g / 2), (byte)(b.b / 2), (byte)(b.a / 2));

            return Out(interpolator, (time * 2) - delta, new Color32((byte)(a.r + halfB.r),(byte)(a.g + halfB.g),(byte)(a.b + halfB.b),(byte)(a.a + halfB.a)), halfB, delta);
        }

        private static float OutIn<T>(T interpolator, float time, float a, float b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;
            if (time < delta / 2)
                return Out(interpolator, time * 2, a, b / 2, delta);

            return In(interpolator, (time * 2) - delta, a + b / 2, b / 2, delta);
        }

        private static float2 OutIn<T>(T interpolator, float time, float2 a, float2 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;
            if (time < delta / 2)
                return Out(interpolator, time * 2, a, b / 2, delta);

            return In(interpolator, (time * 2) - delta, a + b / 2, b / 2, delta);
        }

        private static float3 OutIn<T>(T interpolator, float time, float3 a, float3 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return a + b;
            if (time <= 0)
                return a;
            if (time < delta / 2)
                return Out(interpolator, time * 2, a, b / 2, delta);

            return In(interpolator, (time * 2) - delta, a + b / 2, b / 2, delta);
        }

        private static Color32 OutIn<T>(T interpolator, float time, Color32 a, Color32 b, float delta = 1f) where T : unmanaged, IInterpolator
        {
            if (time >= delta)
                return new Color32 { r = (byte)(a.r + b.r), g = (byte)(a.g + b.g), b = (byte)(a.b + b.b), a = (byte)(a.a + b.a) };
            if (time <= 0)
                return a;
            if (time < delta / 2)
                return Out(interpolator, time * 2, a, new Color32((byte)(b.r / 2), (byte)(b.g / 2), (byte)(b.b / 2), (byte)(b.a / 2)), delta);

            Color32 halfB = new Color32((byte)(b.r / 2), (byte)(b.g / 2), (byte)(b.b / 2), (byte)(b.a / 2));

            return In(interpolator, (time * 2) - delta, new Color32((byte)(a.r + halfB.r),(byte)(a.g + halfB.g),(byte)(a.b + halfB.b),(byte)(a.a + halfB.a)), halfB, delta);
        }

        private interface IInterpolator
        {
            float Invoke(float t, float d = 1);
        }

        [Preserve]
        private struct Linear : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return t / d;
            }
        }

        [Preserve]
        private struct Quad : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return (t /= d) * t;
            }
        }

        [Preserve]
        private struct Cubic : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return (t /= d) * t * t;
            }
        }

        [Preserve]
        private struct Quart : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return (t /= d) * t * t * t;
            }
        }

        [Preserve]
        private struct Quint : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return (t /= d) * t * t * t * t;
            }
        }

        [Preserve]
        private struct Sine : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return 1 - math.cos(t / d * (math.PI / 2));
            }
        }

        [Preserve]
        private struct Expo : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return math.pow(2, 10 * (t / d - 1));
            }
        }

        [Preserve]
        private struct Circ : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return -(math.sqrt(1 - (t /= d) * t) - 1);
            }
        }

        [Preserve]
        private struct Elastic : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                t                            /= d;
                var p                         = d * .3f;
                var s                         = p / 4;
                return -(math.pow(2, 10 * (t -= 1)) * math.sin((t * d - s) * (2 * math.PI) / p));
            }
        }

        [Preserve]
        private struct Back : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                return (t /= d) * t * ((1.70158f + 1) * t - 1.70158f);
            }
        }

        [Preserve]
        private struct Bounce : IInterpolator
        {
            public float Invoke(float t, float d = 1)
            {
                t       = d - t;
                if ((t /= d) < (1 / 2.75f))
                    return 1 - (7.5625f * t * t);
                if (t < (2 / 2.75f))
                    return 1 - (7.5625f * (t -= (1.5f / 2.75f)) * t + .75f);
                if (t < (2.5f / 2.75f))
                    return 1 - (7.5625f * (t -= (2.25f / 2.75f)) * t + .9375f);
                return 1 - (7.5625f * (t     -= (2.625f / 2.75f)) * t + .984375f);
            }
        }
    }

    public enum InterpolationType : byte
    {
        Linear,
        QuadIn,
        QuadOut,
        QuadInOut,
        QuadOutIn,
        CubicIn,
        CubicOut,
        CubicInOut,
        CubicOutIn,
        QuartIn,
        QuartOut,
        QuartInOut,
        QuartOutIn,
        QuintIn,
        QuintOut,
        QuintInOut,
        QuintOutIn,
        SineIn,
        SineOut,
        SineInOut,
        SineOutIn,
        ExpoIn,
        ExpoOut,
        ExpoInOut,
        ExpoOutIn,
        CircIn,
        CircOut,
        CircInOut,
        CircOutIn,
        ElasticIn,
        ElasticOut,
        ElasticInOut,
        ElasticOutIn,
        BackIn,
        BackOut,
        BackInOut,
        BackOutIn,
        BounceIn,
        BounceOut,
        BounceInOut,
        BounceOutIn
    }
}

