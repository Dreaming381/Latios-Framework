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
        /// <summary>
        /// The lower bounds along each axis
        /// </summary>
        public float3 min;
        /// <summary>
        /// The upper bounds along each axis
        /// </summary>
        public float3 max;

        /// <summary>
        /// Create a new Aabb
        /// </summary>
        /// <param name="min">The lower bounds along each axis</param>
        /// <param name="max">The upper bounds along each axis</param>
        public Aabb(float3 min, float3 max)
        {
            this.min = min;
            this.max = max;
        }
    }
}

