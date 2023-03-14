using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;

namespace AclUnity
{
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct Qvvs
    {
        [FieldOffset(0)] public quaternion rotation;
        [FieldOffset(16)] public float4    translation;
        [FieldOffset(32)] public float4    stretchScale;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct ClipHeader
    {
        public enum ClipType : byte
        {
            Skeleton,
            SkeletonWithUniformScales,
            Scalars
        }

        [FieldOffset(0)] public ClipType clipType;
        [FieldOffset(2)] public short    trackCount;
        [FieldOffset(4)] public float    sampleRate;
        [FieldOffset(8)] public float    duration;
        [FieldOffset(12)] public uint    offsetToUniformScalesStartInBytes;

        public static unsafe ClipHeader Read(void* compressedClip)
        {
            Decompression.CheckCompressedClipIsValid(compressedClip);
            return *(ClipHeader*)compressedClip;
        }
    }

    public struct SemanticVersion
    {
        public short major;
        public short minor;
        public short patch;
        public bool  isPreview;

        public bool IsValid => major > 0 || minor > 0 || patch > 0;
        public bool IsUnrecognized => major == -1 && minor == -1 && patch == -1;
    }

    [BurstCompile]
    public static class AclUnityCommon
    {
        public static SemanticVersion GetVersion()
        {
            int version = 0;
            if (X86.Avx2.IsAvx2Supported)
                version = AVX.getVersion();
            else
            {
                //UnityEngine.Debug.Log("Fetched without AVX");
                version = NoExtensions.getVersion();
            }

            if (version == -1)
                return new SemanticVersion { major = -1, minor = -1, patch = -1 };

            short patch                        = (short)(version & 0x3ff);
            short minor                        = (short)((version >> 10) & 0x3ff);
            short major                        = (short)((version >> 20) & 0x3ff);
            bool  isPreview                    = patch > 500;
            patch                              = isPreview ? (short)(patch - 500) : patch;
            return new SemanticVersion { major = major, minor = minor, patch = patch, isPreview = isPreview };
        }

        public static SemanticVersion GetUnityVersion()
        {
            int version = 0;
            if (X86.Avx2.IsAvx2Supported)
                version = AVX.getVersion();
            else
            {
                //UnityEngine.Debug.Log("Fetched without AVX");
                version = NoExtensions.getVersion();
            }

            if (version == -1)
                return new SemanticVersion { major = -1, minor = -1, patch = -1 };

            short patch                        = (short)(version & 0x3ff);
            short minor                        = (short)((version >> 10) & 0x3ff);
            short major                        = (short)((version >> 20) & 0x3ff);
            bool  isPreview                    = patch > 500;
            patch                              = isPreview ? (short)(patch - 500) : patch;
            return new SemanticVersion { major = major, minor = minor, patch = patch, isPreview = isPreview };
        }

        public static string GetPluginName()
        {
            if (X86.Avx2.IsAvx2Supported)
                return dllNameAVX;
            return dllName;
        }

        internal const string dllName    = "AclUnity";
        internal const string dllNameAVX = "AclUnity_AVX";

        static class NoExtensions
        {
            [DllImport(dllName)]
            public static extern int getVersion();
            [DllImport(dllName)]
            public static extern int getUnityVersion();
        }

        static class AVX
        {
            [DllImport(dllNameAVX)]
            public static extern int getVersion();
            [DllImport(dllNameAVX)]
            public static extern int getUnityVersion();
        }
    }
}

