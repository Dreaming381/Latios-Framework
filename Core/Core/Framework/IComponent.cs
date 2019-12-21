using System;
using Unity.Entities;

namespace Latios
{
    public interface IComponent
    {
    }

    //Todo: Require DisposeJob()?
    public interface ICollectionComponent : IDisposable
    {
    }

    internal struct ManagedComponentTag<T> : IComponentData where T : struct, IComponent { }

    internal struct ManagedComponentSystemStateTag<T> : ISystemStateComponentData where T : struct, IComponent { }

    internal struct CollectionComponentTag<T> : IComponentData where T : struct, ICollectionComponent { }

    internal struct CollectionComponentSystemStateTag<T> : ISystemStateComponentData where T : struct, ICollectionComponent { }

    public static class ComponentSystemBaseIComponentExtensions
    {
        public static Type IComponentType<T>(this ComponentSystemBase b) where T : struct, IComponent => typeof(ManagedComponentTag<>).MakeGenericType(typeof(T));
        public static Type ICollectionComponentType<T>(this ComponentSystemBase b) where T : struct, IComponent => typeof(CollectionComponentTag<>).MakeGenericType(typeof(T));
    }
}

