using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A triangle defined by three distinct points
    /// </summary>
    [Serializable]
    public struct TriangleCollider
    {
        public float3 pointA;
        public float3 pointB;
        public float3 pointC;

        public TriangleCollider(float3 pointA, float3 pointB, float3 pointC)
        {
            this.pointA = pointA;
            this.pointB = pointB;
            this.pointC = pointC;
        }

        public simdFloat3 AsSimdFloat3()
        {
            return new simdFloat3(pointA, pointB, pointC, pointA);
        }
    }
}

