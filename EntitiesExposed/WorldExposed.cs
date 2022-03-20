using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Exposed
{
    public static class WorldExposedExtensions
    {
        /// <summary>
        /// Returns the managed executing system type if one is executing.
        /// </summary>
        /// <param name="world"></param>
        /// <returns></returns>
        [NotBurstCompatible]
        public static Type ExecutingSystemType(this World world)
        {
            return world.Unmanaged.GetTypeOfSystem(world.Unmanaged.ExecutingSystem);
        }

        public static SystemHandleUntyped ExecutingSystemHandle(this ref WorldUnmanaged world) => world.ExecutingSystem;

        [NotBurstCompatible]
        public static unsafe ComponentSystemBase AsManagedSystem(this World world, SystemHandleUntyped system)
        {
            return world.Unmanaged.ResolveSystemState(system)->ManagedSystem;
        }

        [NotBurstCompatible]
        public static unsafe ComponentSystemBase AsManagedSystem(this ref WorldUnmanaged world, SystemHandleUntyped system)
        {
            return world.ResolveSystemState(system)->ManagedSystem;
        }

        public unsafe struct UnmanagedSystemStateArray
        {
            internal NativeArray<IntPtr> m_systemStatePtrs;

            public bool IsCreated => m_systemStatePtrs.IsCreated;
            public int Length => m_systemStatePtrs.Length;

            public ref SystemState At(int index)
            {
                SystemState* ssPtr = (SystemState*)m_systemStatePtrs[index];
                return ref UnsafeUtility.AsRef<SystemState>(ssPtr);
            }

            public void Dispose() => m_systemStatePtrs.Dispose();
        }

        public static unsafe UnmanagedSystemStateArray GetAllSystemStates(this ref WorldUnmanaged world, Allocator allocator)
        {
            return new UnmanagedSystemStateArray
            {
                m_systemStatePtrs = world.GetAllUnmanagedSystemStates(allocator)
            };
        }

        [NotBurstCompatible]
        public static unsafe int GetMetaIdForType(Type t)
        {
            return SystemBaseRegistry.GetSystemTypeMetaIndex(BurstRuntime.GetHashCode64(t));
        }
    }
}

