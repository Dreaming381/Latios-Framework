using Latios.Transforms;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A struct which represents the data associated with each AABB in a CollisionLayer
    /// </summary>
    public struct ColliderBody
    {
        /// <summary>
        /// The Collider associated with the AABB
        /// </summary>
        public Collider collider;
        /// <summary>
        /// The transform of the collider
        /// </summary>
        public TransformQvvs transform;
        /// <summary>
        /// The entity associated with the collider
        /// </summary>
        public Entity entity;
    }
}

