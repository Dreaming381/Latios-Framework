using System;
using System.Diagnostics;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    /// <summary>
    /// An Auxilliary single-threaded ECS World which can add arbitrary structs to already existing entities (or even destroyed entities).
    /// This particular ECS keeps component values pinned at stable memory addresses for their entire lifecycle. This type has no job safety tracking.
    /// </summary>
    public unsafe struct AuxWorld : IDisposable
    {
        #region Construct/Destruct
        /// <summary>
        /// Construct the AuxWorld with the specified allocator. The allocator will be used continuously for all internal state.
        /// </summary>
        /// <param name="allocator">The allocator to use</param>
        public AuxWorld(AllocatorManager.AllocatorHandle allocator)
        {
            impl = AllocatorManager.Allocate<AuxWorldImpl>(allocator);
        }

        /// <summary>
        /// Disposes all state in the AuxWorld, invoking any IAuxDisposables in the process
        /// </summary>
        public void Dispose()
        {
            CheckIsValid();
            var allocator = impl->allocator;
            impl->Dispose();
            AllocatorManager.Free(allocator, impl);
            impl = null;
        }

        /// <summary>
        /// Gets the allocator used by this AuxWorld.
        /// </summary>
        public AllocatorManager.AllocatorHandle allocator
        {
            get
            {
                CheckIsValid();
                return impl->allocator;
            }
        }
        #endregion

        #region Component API
        /// <summary>
        /// Adds or replaces an arbitrary struct attached to the entity. If an IAuxDisposable is being replaced,
        /// the Dispose() method will be invoked on the prior instance.
        /// </summary>
        /// <typeparam name="T">The type of arbitrary struct to attach</typeparam>
        /// <param name="entity">The entity to attach the struct to</param>
        /// <param name="component">The struct's initial value</param>
        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            CheckIsValid();
            impl->AddComponent(entity, in component);
        }

        /// <summary>
        /// Removes an arbitrary struct attached to the entity. If the struct is an IAuxDisposable,
        /// the Dispose() method will be invoked on the removed instance.
        /// If the entity does not have the struct attached, this method does nothing.
        /// </summary>
        /// <typeparam name="T">The type of arbitrary struct to unattach</typeparam>
        /// <param name="entity">The entity to unattach the struct from</param>
        public void RemoveComponent<T>(Entity entity) where T : unmanaged
        {
            CheckIsValid();
            impl->RemoveComponent<T>(entity);
        }

        /// <summary>
        /// Removes all arbitrary structs attached to the entity. For each struct that is an IAuxDisposable,
        /// the Dispose() method will be invoked on it.
        /// </summary>
        /// <param name="entity">The entity to remove all structs from</param>
        public void RemoveAllComponents(Entity entity)
        {
            CheckIsValid();
        }

        /// <summary>
        /// Retrieves a reference to the arbitrary struct attached to the entity, if present.
        /// </summary>
        /// <typeparam name="T">The type of arbitrary struct to retrieve</typeparam>
        /// <param name="entity">The entity to retrive the struct from</param>
        /// <param name="componentRef">The reference to the struct, only valid if the struct was present</param>
        /// <returns>True if the struct was present on the entity, false otherwise</returns>
        public bool TryGetComponent<T>(Entity entity, out AuxRef<T> componentRef) where T : unmanaged
        {
            CheckIsValid();
            return impl->TryGetComponent(entity, out componentRef);
        }
        #endregion

        #region Query API
        /// <summary>
        /// Returns an enumerator that iterates all attached structs of the specified type.
        /// This particular enumerator iterates in a cache-efficient manner, and is not invalidated
        /// by archetype changes. Attempting to add this specific component type during iteration
        /// may or may not result in that component being iterated. And attempting to access a returned
        /// AuxRef after it has been removed will throw an exception.
        /// </summary>
        /// <typeparam name="T">The type of arbitrary struct to iterate</typeparam>
        /// <returns>An enumerator that can be used in a foreach expression to iterate elements directly</returns>
        public AuxComponentEnumerator<T> AllOf<T>() where T : unmanaged
        {
            CheckIsValid();
            return impl->AllOf<T>();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all entities which have all of the specified
        /// attached structs. This enumerator is not invalidated by structural changes, but may miss
        /// entities if an entity is removed from this archetype during iteration.
        /// </summary>
        /// <returns>An enumerator which can be used in a foreach expression, whichcan be deconstructed as
        /// (var entity, var type0, var type1, ...)</returns>
        public AuxQueryEnumerator<T0> AllWith<T0>()
            where T0 : unmanaged
        {
            return impl->AllWith<T0>();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all entities which have all of the specified
        /// attached structs. This enumerator is not invalidated by structural changes, but may miss
        /// entities if an entity is removed from this archetype during iteration.
        /// </summary>
        /// <returns>An enumerator which can be used in a foreach expression, whichcan be deconstructed as
        /// (var entity, var type0, var type1, ...)</returns>
        public AuxQueryEnumerator<T0, T1> AllWith<T0, T1>()
            where T0 : unmanaged
            where T1 : unmanaged
        {
            return impl->AllWith<T0, T1>();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all entities which have all of the specified
        /// attached structs. This enumerator is not invalidated by structural changes, but may miss
        /// entities if an entity is removed from this archetype during iteration.
        /// </summary>
        /// <returns>An enumerator which can be used in a foreach expression, whichcan be deconstructed as
        /// (var entity, var type0, var type1, ...)</returns>
        public AuxQueryEnumerator<T0, T1, T2> AllWith<T0, T1, T2>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
        {
            return impl->AllWith<T0, T1, T2>();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all entities which have all of the specified
        /// attached structs. This enumerator is not invalidated by structural changes, but may miss
        /// entities if an entity is removed from this archetype during iteration.
        /// </summary>
        /// <returns>An enumerator which can be used in a foreach expression, whichcan be deconstructed as
        /// (var entity, var type0, var type1, ...)</returns>
        public AuxQueryEnumerator<T0, T1, T2, T3> AllWith<T0, T1, T2, T3>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
        {
            return impl->AllWith<T0, T1, T2, T3>();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all entities which have all of the specified
        /// attached structs. This enumerator is not invalidated by structural changes, but may miss
        /// entities if an entity is removed from this archetype during iteration.
        /// </summary>
        /// <returns>An enumerator which can be used in a foreach expression, whichcan be deconstructed as
        /// (var entity, var type0, var type1, ...)</returns>
        public AuxQueryEnumerator<T0, T1, T2, T3, T4> AllWith<T0, T1, T2, T3, T4>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
        {
            return impl->AllWith<T0, T1, T2, T3, T4>();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all entities which have all of the specified
        /// attached structs. This enumerator is not invalidated by structural changes, but may miss
        /// entities if an entity is removed from this archetype during iteration.
        /// </summary>
        /// <returns>An enumerator which can be used in a foreach expression, whichcan be deconstructed as
        /// (var entity, var type0, var type1, ...)</returns>
        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5> AllWith<T0, T1, T2, T3, T4, T5>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
        {
            return impl->AllWith<T0, T1, T2, T3, T4, T5>();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all entities which have all of the specified
        /// attached structs. This enumerator is not invalidated by structural changes, but may miss
        /// entities if an entity is removed from this archetype during iteration.
        /// </summary>
        /// <returns>An enumerator which can be used in a foreach expression, whichcan be deconstructed as
        /// (var entity, var type0, var type1, ...)</returns>
        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6> AllWith<T0, T1, T2, T3, T4, T5, T6>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
            where T6 : unmanaged
        {
            return impl->AllWith<T0, T1, T2, T3, T4, T5, T6>();
        }

        /// <summary>
        /// Returns an enumerator that iterates through all entities which have all of the specified
        /// attached structs. This enumerator is not invalidated by structural changes, but may miss
        /// entities if an entity is removed from this archetype during iteration.
        /// </summary>
        /// <returns>An enumerator which can be used in a foreach expression, whichcan be deconstructed as
        /// (var entity, var type0, var type1, ...)</returns>
        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6, T7> AllWith<T0, T1, T2, T3, T4, T5, T6, T7>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
            where T6 : unmanaged
            where T7 : unmanaged
        {
            return impl->AllWith<T0, T1, T2, T3, T4, T5, T6, T7>();
        }
        #endregion

        #region State
        internal AuxWorldImpl* impl;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckIsValid()
        {
            if (impl == null)
                throw new NullReferenceException("The AuxWorld has not been initialized.");
        }
        #endregion
    }
}

