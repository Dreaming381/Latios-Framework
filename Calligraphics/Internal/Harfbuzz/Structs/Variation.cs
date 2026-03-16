using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Variation : IEquatable<Variation>
    {        
        public AxisTag axisTag;
        public float value;
        public Variation(AxisTag axisTag, float value)
        {
            this.axisTag = axisTag;
            this.value = value;
        }
        public override bool Equals(object obj) => obj is Variation other && Equals(other);

        public bool Equals(Variation other)
        {
            return GetHashCode() == other.GetHashCode();
        }

        public static bool operator ==(Variation e1, Variation e2)
        {
            return e1.GetHashCode() == e2.GetHashCode();
        }
        public static bool operator !=(Variation e1, Variation e2)
        {
            return e1.GetHashCode() != e2.GetHashCode();
        }
        public override int GetHashCode()
        {
            int hashCode = 2055808453;
            hashCode = hashCode * -1521134295 + axisTag.GetHashCode();
            hashCode = hashCode * -1521134295 + value.GetHashCode();            
            return hashCode;
        }
    }
}
