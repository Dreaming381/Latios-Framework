using System;
using Unity.Mathematics;

//Note: A == B seems to work with SegmentSegmentDistance
namespace Latios.Psyshock
{
    /// <summary>
    /// A capsule composed of a segment and an inflated radius around the segment
    /// </summary>
    [Serializable]
    public struct CapsuleCollider
    {
        /// <summary>
        /// Defines how transform stretch values should morph the shape, since this shape cannot be perfectly stretched in an efficient manner
        /// </summary>
        public enum StretchMode : byte
        {
            /// <summary>
            /// Stretch the points of the interior segment relative to the local origin, without affecting the radius
            /// </summary>
            StretchPoints = 0,
            /// <summary>
            /// Ignore the stretch of the transform
            /// </summary>
            IgnoreStretch = 1,
            //StretchHeight = 2,
        }

        /// <summary>
        /// The first endpoint of the interior segment in the collider's local space
        /// </summary>
        public float3 pointA;
        /// <summary>
        /// The radius around the segment
        /// </summary>
        public float radius;
        /// <summary>
        /// The second endpoint of the interior segment in the collider's local space
        /// </summary>
        public float3 pointB;
        /// <summary>
        /// The stretch mode which specifies how transform stretch values should morph the capsule shape, since capsules cannot be perfectly stretched in an efficent manner
        /// </summary>
        public StretchMode stretchMode;

        /// <summary>
        /// Constructs a new CapsuleCollider
        /// </summary>
        /// <param name="pointA">The first endpoint of the interior segment in the collider's local space</param>
        /// <param name="pointB">The second endpoint of the interior segment in the collider's local space</param>
        /// <param name="radius">The radius around the segment</param>
        /// <param name="stretchMode">The stretch mode which specifies how transform stretch values should morph the capsule shape,
        /// since capsules cannot be perfectly stretched in an efficent manner</param>
        public CapsuleCollider(float3 pointA, float3 pointB, float radius, StretchMode stretchMode = StretchMode.StretchPoints)
        {
            this.pointA      = pointA;
            this.pointB      = pointB;
            this.radius      = radius;
            this.stretchMode = stretchMode;
        }
    }
}

