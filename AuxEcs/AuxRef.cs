using System;
using System.Diagnostics;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    /// <summary>
    /// A reference to an arbitrary struct component attached to an entity. This reference remains valid
    /// for as long as the struct type is attached to the entity, regardless of any archetype changes.
    /// WARNING: Attempting to access an AuxRef after the AuxWorld has been destroyed can result in crashes!
    /// </summary>
    /// <typeparam name="T">The type of struct this AuxRef references</typeparam>
    public unsafe struct AuxRef<T> where T : unmanaged
    {
        internal T*   componentPtr;
        internal int* versionPtr;
        internal int  version;

        /// <summary>
        /// Retrieves the direct reference to the struct itself after validating the reference
        /// </summary>
        public ref T aux
        {
            get
            {
                CheckValid();
                return ref *componentPtr;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckValid()
        {
            if (componentPtr == null)
                throw new NullReferenceException("The AuxRef is not initialized.");
            if (version != *versionPtr)
                throw new InvalidOperationException("The AuxRef has been invalidated from when the component was removed from the entity");
        }
    }
}

