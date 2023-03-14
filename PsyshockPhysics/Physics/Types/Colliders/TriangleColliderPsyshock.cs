using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A triangle defined by three distinct points. This collider is "double-sided" in that neither side is considered "inside".
    /// In fact, it is impossible to be "inside" the triangle in 3D space, only on the surface.
    /// </summary>
    [Serializable]
    public struct TriangleCollider
    {
        /// <summary>
        /// The first point of the triangle in the collider's local space
        /// </summary>
        public float3 pointA;
        /// <summary>
        /// The second point of the triangle in the collider's local space
        /// </summary>
        public float3 pointB;
        /// <summary>
        /// The third point of the triangle in the collider's local space
        /// </summary>
        public float3 pointC;

        /// <summary>
        /// Creates a new TriangleCollider
        /// </summary>
        /// <param name="pointA">The first point of the triangle in the collider's local space</param>
        /// <param name="pointB">The second point of the triangle in the collider's local space</param>
        /// <param name="pointC">The third point of the triangle in the collider's local space</param>
        public TriangleCollider(float3 pointA, float3 pointB, float3 pointC)
        {
            this.pointA = pointA;
            this.pointB = pointB;
            this.pointC = pointC;
        }

        /// <summary>
        /// Packs the triangle points as a simdFloat3, in the sequence A, B, C, A.
        /// </summary>
        public simdFloat3 AsSimdFloat3()
        {
            return new simdFloat3(pointA, pointB, pointC, pointA);
        }
    }
}

