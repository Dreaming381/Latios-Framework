using System;
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct BBox: IEquatable<BBox>
    {
        public float2 min;
        public float2 max;
        public readonly bool IsValid => math.all(min <= max);
        public readonly float width => max.x - min.x;
        public readonly float height => max.y - min.y;

        public readonly int intWidth => (int)math.ceil(max.x - min.x);
        public readonly int intHeight => (int)math.ceil(max.y - min.y);

        /// <summary>  Create an empty, invalid BBox. </summary>
        public static readonly BBox Empty = new BBox { min = float.MaxValue, max = float.MinValue };
        /// <summary>   Create a union of two BBox. </summary>
        public static BBox Union(BBox a, BBox b)
        {
            var min = math.min(a.min, b.min);
            var max = math.max(a.max, b.max);

            return new BBox(min, max);
        }
        /// <summary>   Expands the BBox by the provided padding. </summary>
        public void Expand(int padding)
        {
            min -= padding;
            max += padding;
        }
        /// <summary>   Query if this aabb contains the given point. </summary>
        public bool Contains(float2 point)
        {
            return math.all(point >= min & point <= max);
        }

        public BBox(float2 min, float2 max)
        {
            this.min = min;
            this.max = max;
        }
        public BBox(float xmin, float ymin, float xmax, float ymax)
        {
            min = new float2(xmin, ymin);
            max = new float2(xmax, ymax);
        }

        public bool Equals(BBox other)
        {
            return this == other;
        }
        public static bool operator ==(BBox x, BBox y)
        {
            return math.all(x.min == y.min & x.max == y.max);
        }

        public static bool operator !=(BBox x, BBox y)
        {
            return !(x == y);
        }
        public override bool Equals(object obj) => obj is BBox other && Equals(other);
        public override int GetHashCode()
        {
            //return HashCode.Combine(c0, c1);
            int hash = 17;
            hash = hash * 29 + min.GetHashCode();
            hash = hash * 29 + max.GetHashCode();
            return hash;
        }
        public override string ToString()
        {
            return $"x {min.x:F1} y {min.y:F1} width {width:F1} height {height:F1}";
            //return $"min {min} max {max}";
        }
    }
}
