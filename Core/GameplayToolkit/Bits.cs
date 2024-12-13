using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    public static class Bits
    {
        public static bool GetBit(int data, int bitIndex) => new BitField32(math.asuint(data)).IsSet(bitIndex);
        public static void SetBit(ref int data, int bitIndex, bool bitValue)
        {
            var bitfield = new BitField32(math.asuint(data));
            bitfield.SetBits(bitIndex, bitValue);
            data = math.asint(bitfield.Value);
        }
        public static bool GetBit(uint data, int bitIndex) => new BitField32(data).IsSet(bitIndex);
        public static void SetBit(ref uint data, int bitIndex, bool bitValue)
        {
            var bitfield = new BitField32(data);
            bitfield.SetBits(bitIndex, bitValue);
            data = bitfield.Value;
        }
        public static bool GetBit(long data, int bitIndex) => new BitField64(math.asulong(data)).IsSet(bitIndex);
        public static void SetBit(ref long data, int bitIndex, bool bitValue)
        {
            var bitfield = new BitField64(math.asulong(data));
            bitfield.SetBits(bitIndex, bitValue);
            data = math.aslong(bitfield.Value);
        }
        public static bool GetBit(ulong data, int bitIndex) => new BitField64(data).IsSet(bitIndex);
        public static void SetBit(ref ulong data, int bitIndex, bool bitValue)
        {
            var bitfield = new BitField64(data);
            bitfield.SetBits(bitIndex, bitValue);
            data = bitfield.Value;
        }
        public static bool GetBit(ushort data, int bitIndex) => GetBit((uint)data, bitIndex);
        public static void SetBit(ref ushort data, int bitIndex, bool bitValue)
        {
            uint intdata = data;
            SetBit(ref intdata, bitIndex, bitValue);
            data = (ushort)(intdata & 0xffff);
        }
        public static bool GetBit(byte data, int bitIndex) => GetBit((uint)data, bitIndex);
        public static void SetBit(ref byte data, int bitIndex, bool bitValue)
        {
            uint intdata = data;
            SetBit(ref intdata, bitIndex, bitValue);
            data = (byte)(intdata & 0xff);
        }

        public static int GetBits(int data, int firstBitIndex, int bitCount) 
            => math.asint(new BitField32(math.asuint(data)).GetBits(firstBitIndex, bitCount));
        public static void SetBits(ref int data, int firstBitIndex, int bitCount, int newValue)
        {
            uint udata = math.asuint(data);
            SetBits(ref udata, firstBitIndex, bitCount, math.asuint(newValue));
            data = math.asint(udata);
        }
        public static uint GetBits(uint data, int firstBitIndex, int bitCount) => new BitField32(data).GetBits(firstBitIndex, bitCount);
        public static void SetBits(ref uint data, int firstBitIndex, int bitCount, uint newValue)
        {
            var mask = 0xffffffffu >> (32 - bitCount);
            var newPart = (newValue & mask) << firstBitIndex;
            var oldPart = data & ~(mask << firstBitIndex);
            data = newPart | oldPart;
        }
        public static long GetBits(long data, int firstBitIndex, int bitCount)
            => math.aslong(new BitField64(math.asulong(data)).GetBits(firstBitIndex, bitCount));
        public static void SetBits(ref long data, int firstBitIndex, int bitCount,  long newValue)
        {
            ulong udata = math.asulong(data);
            SetBits(ref udata, firstBitIndex, bitCount, math.asulong(newValue));
            data = math.aslong(udata);
        }
        public static ulong GetBits(ulong data, int firstBitIndex, int bitCount) => new BitField64(data).GetBits(firstBitIndex, bitCount);
        public static void SetBits(ref ulong data, int firstBitIndex, int bitCount, ulong newValue)
        {
            var mask = (~0x0u) >> (64 - bitCount);
            var newPart = (newValue & mask) << firstBitIndex;
            var oldPart = data & ~(mask << firstBitIndex);
            data = newPart | oldPart;
        }
        public static ushort GetBits(ushort data, int firstBitIndex, int bitCount)
            => (ushort)(0xffff & GetBits((uint)data, firstBitIndex, bitCount));
        public static void SetBits(ref ushort data, int firstBitIndex, int bitCount,  ushort newValue)
        {
            uint intdata = data;
            SetBits(ref intdata, firstBitIndex, bitCount, newValue);
            data = (ushort)(intdata & 0xffff);
        }
        public static byte GetBits(byte data, int firstBitIndex, int bitCount)
            => (byte)(0xff & GetBits((uint)data, firstBitIndex, bitCount));
        public static void SetBits(ref byte data, int firstBitIndex, int bitCount,  byte newValue)
        {
            uint intdata = data;
            SetBits(ref intdata, firstBitIndex, bitCount, newValue);
            data = (byte)(intdata & 0xff);
        }
    }
}