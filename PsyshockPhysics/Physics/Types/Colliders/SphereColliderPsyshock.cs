using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A sphere defined by a center and a radius
    /// </summary>
    [Serializable]
    public struct SphereCollider
    {
        /// <summary>
        /// Defines how transform stretch values should morph the shape, since this shape cannot be perfectly stretched in an efficient manner
        /// </summary>
        public enum StretchMode : byte
        {
            /// <summary>
            /// Stretches the center of the sphere relative to the local origin, without affecting the radius
            /// </summary>
            StretchCenter = 0,
            /// <summary>
            /// Ignore the stretch of the transform
            /// </summary>
            IgnoreStretch = 1,
        }

        /// <summary>
        /// The center of the sphere in the collider's local space
        /// </summary>
        public float3 center;
        /// <summary>
        /// The radius of the sphere around the center
        /// </summary>
        public float radius;
        /// <summary>
        /// The stretch mode which specifies how transform stretch values should morph the sphere shape, since spheres cannot be perfectly stretched in an efficent manner
        /// </summary>
        public StretchMode stretchMode;

        /// <summary>
        /// Creates a new SphereCollider
        /// </summary>
        /// <param name="center">The center of the sphere in the collider's local space</param>
        /// <param name="radius">The radius of the sphere around the center</param>
        /// <param name="stretchMode">The stretch mode which specifies how transform stretch values should morph the sphere shape,
        /// since spheres cannot be perfectly stretched in an efficent manner</param>
        public SphereCollider(float3 center, float radius, StretchMode stretchMode = StretchMode.StretchCenter)
        {
            this.center      = center;
            this.radius      = radius;
            this.stretchMode = stretchMode;
        }
    }
}

