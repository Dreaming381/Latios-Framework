using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// Comparer for sorting ColliderCastResults by distance
    /// </summary>
    public struct ColliderCastDistanceComparer : IComparer<ColliderCastResult>
    {
        public int Compare(ColliderCastResult x, ColliderCastResult y)
        {
            return x.distance.CompareTo(y.distance);
        }
    }
}

