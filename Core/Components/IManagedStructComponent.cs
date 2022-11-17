using System;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    /// <summary>
    /// A pseudo-component that can be attached to entities.
    /// It does not allocate GC but can store managed references.
    /// </summary>
    public interface IManagedStructComponent
    {
        /// <summary>
        /// A component type that should accompany the managed struct component.
        /// The associated component is automatically added when a managed struct component is added,
        /// and likewise for removal. You can use the associated component in queries.
        /// You can also add or remove the associated component type via a syncPoint command buffer
        /// to add or remove the managed struct component.
        /// A custom tag component specific to each IManagedStructComponent implementation is recommended.
        /// </summary>
        ComponentType AssociatedComponentType { get; }
    }

    /// <summary>
    /// A Pseduo-component that can be attached to entities.
    /// It can store NativeContainers and automatically tracks their dependencies.
    /// </summary>
    public interface ICollectionComponent
    {
        /// <summary>
        /// Attempt to Dispose the collection component. Note that user code could add not-fully-allocated collection components
        /// or a collection component may be default-initialized by the presence of an AssociatedComponentType.
        /// </summary>
        /// <param name="inputDeps"></param>
        /// <returns></returns>
        JobHandle TryDispose(JobHandle inputDeps);
        /// <summary>
        /// A component type that should accompany the collection component.
        /// The associated component is automatically added when a collection component is added,
        /// and likewise for removal. You can use the associated component in queries.
        /// You can also add or remove the associated component type via a syncPoint command buffer
        /// to add or remove the collection component.
        /// A custom tag component specific to each ICollectionComponent implementation is recommended.
        /// </summary>
        ComponentType AssociatedComponentType { get; }
    }

    //public struct ManagedComponentTag<T> : IComponentData where T : struct, IManagedComponent { }

    internal struct ManagedComponentCleanupTag<T> : ICleanupComponentData where T : struct, IManagedStructComponent { }

    //public struct CollectionComponentTag<T> : IComponentData where T : struct, ICollectionComponent { }

    internal struct CollectionComponentCleanupTag<T> : ICleanupComponentData where T : struct, ICollectionComponent { }
}

