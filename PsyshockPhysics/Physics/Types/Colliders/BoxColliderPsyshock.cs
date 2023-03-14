using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A local coordinate space axis-alinged box using center & extents representation
    /// </summary>
    [Serializable]
    public struct BoxCollider
    {
        /// <summary>
        /// The center of the box in its local space
        /// </summary>
        public float3 center;
        /// <summary>
        /// The extents, or unsigned distance from the center to each side of the box and is equal to half the length, width, or height
        /// </summary>
        public float3 halfSize;

        /// <summary>
        /// Constructs a new BoxCollider
        /// </summary>
        /// <param name="center">The center of the box in its local space</param>
        /// <param name="halfSize">The extents, or unsigned distance from the center to each side of the box, and is equal to half the length, width, or height</param>
        public BoxCollider(float3 center, float3 halfSize)
        {
            this.center   = center;
            this.halfSize = halfSize;
        }
    }
}

