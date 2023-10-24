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
        /// This method is only called on removal. It is not called when setting the managed struct with a new value.
        /// It's primary purpose is to act as a user callback when then entity holding it is destroyed.
        /// </summary>
        void Dispose()
        {
        }
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
    }

    /// <summary>
    /// A struct that may wrap multiple collection components and other data to provide a more convenient API
    /// or protect the underlying containers from misuse.
    /// </summary>
    /// <typeparam name="T">The type of this ICollectionAspect, (yes, it is self-referencing)</typeparam>
    public interface ICollectionAspect<T> where T : unmanaged, ICollectionAspect<T>
    {
        /// <summary>
        /// The method used by the framework to create a collection aspect instance.
        /// Implement this method with whatever you need to construct the aspect.
        /// You should never have to call this method directly.
        /// </summary>
        /// <param name="latiosWorld">The Latios World for accessing collection components</param>
        /// <param name="entityManager">The EntityManager for accessing additional components</param>
        /// <param name="entity">The Entity this collection aspect should be derived from</param>
        /// <returns>A collection aspect instance</returns>
        T CreateCollectionAspect(LatiosWorldUnmanaged latiosWorld, EntityManager entityManager, Entity entity);
        /// <summary>
        /// The method used by the framework to query for a collection aspect.
        /// Implement this method to add new component types such as collection component Exist types.
        /// You should never have to call this method directly.
        /// </summary>
        /// <param name="query">A fluent query to apply additional query operations to</param>
        /// <returns>The last fluent query in the chain of appended operations.</returns>
        FluentQuery AppendToQuery(FluentQuery query);
    }
}

