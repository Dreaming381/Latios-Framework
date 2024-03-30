using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal struct Plane
    {
        private float4 m_normalAndDistance;

        public float3 normal
        {
            get => m_normalAndDistance.xyz;
            set => m_normalAndDistance.xyz = value;
        }

        // Distance to the origin along the normal
        public float distanceToOrigin
        {
            get => m_normalAndDistance.w;
            set => m_normalAndDistance.w = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Plane(float3 normal, float distance)
        {
            m_normalAndDistance = new float4(normal, distance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float4(Plane plane) => plane.m_normalAndDistance;
    }
}

