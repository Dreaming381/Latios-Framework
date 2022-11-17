using System;
using Unity.Entities;

namespace Latios
{
    /// <summary>
    /// A tag component automatically added to the worldBlackboardEntity. Use it in queries if necessary.
    /// </summary>
    public struct WorldBlackboardTag : IComponentData { }

    /// <summary>
    /// A tag component automatically added to the sceneBlackboardEntity. Use it in queries if necessary.
    /// </summary>
    public struct SceneBlackboardTag : IComponentData { }

    public enum BlackboardScope
    {
        /// <summary>
        /// Apply to the worldBlackboardEntity
        /// </summary>
        World,
        /// <summary>
        /// Apply to the sceneBlackboardEntity
        /// </summary>
        Scene
    }
    public enum MergeMethod
    {
        /// <summary>
        /// Any already existing components on the blackboard entity can have their data overwritten
        /// by the new component values
        /// </summary>
        Overwrite,
        /// <summary>
        /// If the blackboard entity already has a given component, the new component value will be discarded
        /// </summary>
        KeepExisting,
        /// <summary>
        /// An exception is thrown if the blackboard entity already has a given component
        /// </summary>
        ErrorOnConflict
    }

    /// <summary>
    /// Attach this to an entity to have all its components copied over to a blackboard entity.
    /// The entity will then be destroyed.
    /// </summary>
    public struct BlackboardEntityData : IComponentData
    {
        public BlackboardScope blackboardScope;
        public MergeMethod     mergeMethod;
    }
}

