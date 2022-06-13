using System;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    /// <summary>
    /// A pseudo-component that can be attached to entities.
    /// It does not allocate GC but can store managed references.
    /// </summary>
    public interface IManagedComponent
    {
        Type AssociatedComponentType { get; }
    }

    /// <summary>
    /// A Pseduo-component that can be attached to entities.
    /// It can store NativeContainers and automatically tracks their dependencies.
    /// </summary>
    public interface ICollectionComponent
    {
        JobHandle Dispose(JobHandle inputDeps);
        Type AssociatedComponentType { get; }
    }

    //public struct ManagedComponentTag<T> : IComponentData where T : struct, IManagedComponent { }

    internal struct ManagedComponentSystemStateTag<T> : ISystemStateComponentData where T : struct, IManagedComponent { }

    //public struct CollectionComponentTag<T> : IComponentData where T : struct, ICollectionComponent { }

    internal struct CollectionComponentSystemStateTag<T> : ISystemStateComponentData where T : struct, ICollectionComponent { }
}

