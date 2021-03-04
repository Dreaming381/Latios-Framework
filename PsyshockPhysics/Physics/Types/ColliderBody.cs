using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public struct ColliderBody
    {
        public Collider       collider;
        public RigidTransform transform;
        public Entity         entity;
    }
}

