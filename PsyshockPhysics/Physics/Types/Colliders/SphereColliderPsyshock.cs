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
        public float3 center;
        public float  radius;

        public SphereCollider(float3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }
    }
}

