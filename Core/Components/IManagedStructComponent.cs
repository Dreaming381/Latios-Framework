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
}

