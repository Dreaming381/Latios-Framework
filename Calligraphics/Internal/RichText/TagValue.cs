using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Latios.Calligraphics.RichText
{   
        
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct TagValue
    {
        [FieldOffset(0)]
        internal TagValueType type;

        [FieldOffset(1)]
        internal TagUnitType unit;

        [FieldOffset(2)]
        private float m_numericalValue;

        [FieldOffset(2)]
        private Color32 m_colorValue;

        //instead of storing String values in here (e.g. name of requested font),
        //we store the position in CalliBytesRaw and fetch it when needed
        [FieldOffset(2)]
        internal int valueStart;
        [FieldOffset(6)]
        internal int valueLength;
        [FieldOffset(10)]
        internal int valueHash;
        [FieldOffset(15)]
        internal StringValue stringValue;

        internal float NumericalValue
        {
            get
            {
                if (type != TagValueType.NumericalValue)
                    throw new InvalidOperationException("Not a numerical value");
                return m_numericalValue;
            }
            set
            {
                m_numericalValue = value;
            }
        }

        internal Color ColorValue
        {
            get
            {
                if (type != TagValueType.ColorValue)
                    throw new InvalidOperationException("Not a color value");
                return m_colorValue;
            }
            set
            {
                m_colorValue = value;
            }
        }
    }    
}
