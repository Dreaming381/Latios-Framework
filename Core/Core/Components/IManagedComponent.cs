using System;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    public interface IManagedComponent
    {
        Type AssociatedComponentType { get; }
    }

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

