using UnityEngine;

namespace Latios.Calligraphics.RichText
{
    internal struct HighlightState
    {
        public Color32     color;
        public RectOffsets padding;

        public HighlightState(Color32 color, RectOffsets padding)
        {
            this.color   = color;
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

