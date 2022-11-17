using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A Min-Max representation of an Axis-Aligned Bounding Box
    /// </summary>
    [Serializable]
    public struct Aabb
    {
        public float3 min;
        public float3 max;

        public Aabb(float3 min, float3 max)
        {
            this.min = min;
            this.max = max;
        }
    }
}

