using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    [Serializable]
    public struct SphereCollider
    {
        public float3 center;
        public float  radius;

        public SphereCollider(float3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }
    }
}

