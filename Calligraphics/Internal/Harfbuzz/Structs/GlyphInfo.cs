using System.Runtime.InteropServices;
using Unity.Entities;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphInfo : IBufferElementData
    {
        public uint codepoint;
        private uint mask;
        public uint cluster;
        private uint var1;
        private uint var2;
    }
}
