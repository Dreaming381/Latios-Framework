using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public struct CollisionWorldIndex : IComponentData
    {
        internal int packed;
        public int bodyIndex => packed & 0xffffff;
        public byte worldIndex => (byte)(packed >> 24);
    }

    public struct CollisionWorldAabb : IComponentData
    {
        public Aabb aabb;
    }
}

