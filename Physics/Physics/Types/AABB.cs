using System;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    [Serializable]
    public struct AABB
    {
        public float3 min;
        public float3 max;

        public AABB(float3 min, float3 max)
        {
            this.min = min;
            this.max = max;
        }
    }
}

