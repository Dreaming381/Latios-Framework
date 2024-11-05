using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    internal struct ScriptHeader
    {
        const int          kTypeBitCount       = 13;
        const int          kByteOffsetBitCount = 18;
        const int          kInstanceIdBitCount = 54 - (kByteOffsetBitCount + kTypeBitCount);
        public const ulong kMaxTypeIndex       = (1ul << kTypeBitCount) - 1;
        public const ulong kMaxByteOffset      = (1ul << kByteOffsetBitCount) - 1;
        public const ulong kMaxInstanceId      = (1ul << kInstanceIdBitCount) - 1;

        // Master 54:
        //   instance count - 18 - 256k instances
        //   last-used index (script ID) - 23
        //   structure version
        // Instance 54:
        //   script type - 13 - 8192 types
        //   byte offset - 18 - 256kB - requires 256k instance
        //   script ID - 23

        // Cache 64:
        // script ID - 23
        // byte offset - 18
        // structure version
        // header offset (instance count) - 14

        public ulong bloomMask;
        public ulong packed;

        public byte userByte
        {
            get => (byte)(packed >> 56);
            set => packed = (packed & 0x00ffffffffffffff) | (((ulong)value) << 56);
        }

        public bool userFlagA
        {
            get => (packed & (1ul << 54)) != 0;
            set => packed = value ? (packed | (1ul << 54)) : (packed & (~(1ul << 54)));
        }

        public bool userFlagB
        {
            get => (packed & (1ul << 55)) != 0;
            set => packed = value ? (packed | (1ul << 55)) : (packed & (~(1ul << 55)));
        }

        // instance
        public int scriptType
        {
            get => (int)(packed & kMaxTypeIndex);
            set => packed = (packed & (~kMaxTypeIndex)) | (((ulong)value) & kMaxTypeIndex);
        }

        public int byteOffset
        {
            get => (int)((packed >> kTypeBitCount) & kMaxByteOffset);
            set => packed = (packed & (~(kMaxByteOffset << kTypeBitCount))) | ((((ulong)value) & kMaxByteOffset) << kTypeBitCount);
        }

        public int instanceId
        {
            get => (int)((packed >> (kTypeBitCount + kByteOffsetBitCount)) & kMaxInstanceId);
            set => packed = (packed & (~(kMaxInstanceId << (kTypeBitCount + kByteOffsetBitCount)))) | ((((ulong)value) & kMaxInstanceId) << (kTypeBitCount + kByteOffsetBitCount));
        }

        // master
        public int instanceCount
        {
            get => byteOffset;
            set => byteOffset = value;
        }

        public int lastUsedInstanceId
        {
            get => instanceId;
            set => instanceId = value;
        }
    }
}

