using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    [Serializable]
    public struct BoxCollider
    {
        public float3 center;
        public float3 halfSize;

        public BoxCollider(float3 center, float3 halfSize)
        {
            this.center   = center;
            this.halfSize = halfSize;
        }
    }
}

