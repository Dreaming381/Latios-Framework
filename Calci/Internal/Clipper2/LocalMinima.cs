using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Latios.Calci.Clipper2
{
    internal struct LocalMinima : IEquatable<LocalMinima>
    {
        public readonly int      vertex;
        public readonly PathType polytype;
        public readonly bool     isOpen;

        public LocalMinima(int vertex, PathType polytype, bool isOpen = false)
        {
            this.vertex   = vertex;
            this.polytype = polytype;
            this.isOpen   = isOpen;
        }
        public bool Equals(LocalMinima other)
        {
            return this == other;
        }
        public static bool operator ==(LocalMinima lm1, LocalMinima lm2)
        {
            return lm1.vertex == lm2.vertex &&
                   lm1.polytype == lm2.polytype &&
                   lm1.isOpen == lm2.isOpen;
        }

        public static bool operator !=(LocalMinima lm1, LocalMinima lm2)
        {
            return !(lm1 == lm2);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is LocalMinima minima && this == minima;
        }
        public override int GetHashCode()
        {
            int hash = 17;
            hash     = hash * 29 + vertex.GetHashCode();
            return hash;
        }
    };
    unsafe struct LocMinSorter : IComparer<LocalMinima>
    {
        NativeList<Vertex> m_list;
        public LocMinSorter(NativeList<Vertex> list)
        {
            m_list = list;
        }
        public readonly int Compare(LocalMinima locMin1, LocalMinima locMin2)
        {
            return m_list[locMin2.vertex].pt.y.CompareTo(m_list[locMin1.vertex].pt.y);
        }
    }
}  //namespace

