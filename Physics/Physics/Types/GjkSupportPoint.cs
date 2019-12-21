using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    public struct GjkSupportPoint
    {
        public float3 pos;
        public uint   id;

        public int ida => (int)(id >> 16);
        public int idb => (int)(id & 0xffff);
    }
}

