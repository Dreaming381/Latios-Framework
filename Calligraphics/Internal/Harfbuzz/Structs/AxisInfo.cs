using System.Runtime.InteropServices;
using Unity.Collections;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct AxisInfo
    {
        public uint axisIndex;
        public AxisTag axisTag;
        public NameID nameID;
        public uint flags;
        public float minValue;
        public float defaultValue;
        public float maxValue;
        uint reserved;
        public FixedString32Bytes AxisName => Harfbuzz.HB_TAG((uint)axisTag);

        public override string ToString()
        {
            return $"{axisIndex} {axisTag} {AxisName} {nameID} {flags} min:{minValue} default:{defaultValue} max:{maxValue}";
        }
    }
}
