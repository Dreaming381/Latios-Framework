using Unity.Collections;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    /// <summary>
    /// An enumerator that iterates all components of a specified type stored in the AuxWorld.
    /// This enumerator iterates in a cache-efficient manner, and is not invalidated by entity archetype changes.
    /// This enumerator can be retrieved by AuxWorld.AllOf<typeparamref name="T"/>()
    /// </summary>
    /// <typeparam name="T">The type of arbitrary struct component to iterate</typeparam>
    public unsafe struct AuxComponentEnumerator<T> where T : unmanaged
    {
        AuxRef<T>           auxRef;
        AllComponentsStore* store;
        int                 typeId;
        int                 currentIndex;

        internal AuxComponentEnumerator(AllComponentsStore* store, int typeId)
        {
            auxRef       = default;
            this.store   = store;
            this.typeId  = typeId;
            currentIndex = -1;
        }

        /// <summary>
        /// The number of components of this type present in the AuxWorld at the time of access
        /// </summary>
        public int count => store != null ? (*store)[typeId].instanceCount : 0;

        public AuxComponentEnumerator<T> GetEnumerator() => this;

        public AuxRef<T> Current => auxRef;

        public bool MoveNext()
        {
            if (store == null)
                return false;
            while (currentIndex + 1 < (*store)[typeId].maxIndex)
            {
                currentIndex++;
                auxRef = (*store)[typeId].GetRef<T>(currentIndex);
                if ((auxRef.version & 1) == 1)
                    return true;
            }
            return false;
        }
    }
}

