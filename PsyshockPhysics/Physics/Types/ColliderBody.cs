using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A struct which represents the data associated with each AABB in a CollisionLayer
    /// </summary>
    public struct ColliderBody
    {
        public Collider       collider;
        public RigidTransform transform;
        public Entity         entity;
    }
}

