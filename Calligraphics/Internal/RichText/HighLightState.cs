using Latios.Calligraphics.Extensions;
using UnityEngine;

namespace Latios.Calligraphics
{
    public struct HighlightState
    {
        public Color32 color;
        public Calli_Offset padding;

        public HighlightState(Color32 color, Calli_Offset padding)
        {
            this.color = color;
            this.padding = padding;
        }

        public static bool operator ==(HighlightState lhs, HighlightState rhs)
        {
            return lhs.color.Compare(rhs.color) && lhs.padding == rhs.padding;
        }

        public static bool operator !=(HighlightState lhs, HighlightState rhs)
        {
            return !(lhs == rhs);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public bool Equals(HighlightState other)
        {
            return base.Equals(other);
        }
    }
}

