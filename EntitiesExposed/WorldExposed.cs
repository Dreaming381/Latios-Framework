using System;
using System.Collections.Generic;
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

        [NotBurstCompatible]
        public static unsafe NativeArray<SystemHandleUntyped> CreateUnmanagedSystems(this World world, IList<Type> unmanagedTypes, Allocator allocator)
        {
            int count  = unmanagedTypes.Count;
            var result = new NativeArray<SystemHandleUntyped>(count, allocator);

            var unmanaged = world.Unmanaged;

            for (int i = 0; i < count; ++i)
            {
                result[i] = unmanaged.CreateUnmanagedSystem(world, unmanagedTypes[i], false);
            }

            for (int i = 0; i < count; ++i)
            {
                var systemState = unmanaged.ResolveSystemState(result[i]);
                SystemBaseRegistry.CallOnCreate(systemState);
                SystemBaseRegistry.CallOnCreateForCompiler(systemState);
            }

            return result;
        }

        public static event Action<World> OnWorldCreated
        {
            add
            {
                World.WorldCreated += value;
            }
            remove
            {
                World.WorldCreated -= value;
            }
        }

        public static event Action<World, ComponentSystemBase> OnSystemCreated
        {
            add
            {
                World.SystemCreated += value;
            }
            remove
            {
                World.SystemCreated -= value;
            }
        }
    }
}

