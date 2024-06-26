using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.InternalSourceGen
{
    // Everything in this class is used directly by source-generated code.
    // Be wary of making modifications.
    public static unsafe class StaticAPI
    {
        #region Collection Component
        /// <summary>
        /// Did you forget the `partial` keyword?
        /// This interface is automatically generated by source generators for every ICollectionComponent.
        /// </summary>
        public interface ICollectionComponentSourceGenerated
        {
            public ComponentType componentType { get; }
            public ComponentType cleanupType { get; }
        }

        public interface ICollectionComponentCleanup
        {
            // Must implement the following:
            // public static FunctionPointer<BurstDispatchCollectionComponentDelegate> GetBurstDispatchFunctionPtr();
            // That method should be generated by source generators which we grab via reflection.
        }

        public struct ContextPtr
        {
            public void* ptr;
        }

        public delegate void BurstDispatchCollectionComponentDelegate(ContextPtr context, int operation);

        public static void BurstDispatchCollectionComponent<T>(ContextPtr context, int operation) where T : unmanaged, ICollectionComponentSourceGenerated, ICollectionComponent
        {
            CollectionComponentOperations.DispatchCollectionComponentOperation<T>(context.ptr, operation);
        }
        #endregion

        #region Managed Struct Component
        /// <summary>
        /// Did you forget the `partial` keyword?
        /// This interface is automatically generated by source generators for every IManagedStructComponent.
        /// </summary>
        public interface IManagedStructComponentSourceGenerated
        {
            public ComponentType componentType { get; }
            public ComponentType cleanupType { get; }
        }

        public interface IManagedStructComponentCleanup
        {
            // Must implement the following:
            // public static System.Type GetManagedStructComponentType();
            // That method should be generated by source generators which we grab via reflection.
        }
        #endregion
    }
}

