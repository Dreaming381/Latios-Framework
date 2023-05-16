using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios
{
    public static unsafe class LatiosWorldUnmanagedRetrieveExtensions
    {
        /// <summary>
        /// Gets an instance of LatiosWorldUnmanaged for the world the SystemState belongs to. This method can be invoked from Burst.
        /// </summary>
        public static LatiosWorldUnmanaged GetLatiosWorldUnmanaged(this ref SystemState state)
        {
            return state.WorldUnmanaged.GetLatiosWorldUnmanaged();
        }

        /// <summary>
        /// Gets an instance of LatiosWorldUnmanaged for the world the EntityManager belongs to. This method can be invoked from Burst.
        /// </summary>
        public static LatiosWorldUnmanaged GetLatiosWorldUnmanaged(this EntityManager entityManager)
        {
            return entityManager.WorldUnmanaged.GetLatiosWorldUnmanaged();
        }

        /// <summary>
        /// Gets an instance of LatiosWorldUnmanaged for the world. This method can be invoked from Burst.
        /// </summary>
        public static LatiosWorldUnmanaged GetLatiosWorldUnmanaged(this WorldUnmanaged world)
        {
            var system = world.GetExistingUnmanagedSystem<Systems.LatiosWorldUnmanagedSystem>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (system == SystemHandle.Null)
                throw new System.InvalidOperationException("The current world is not a LatiosWorld. Did you forget to create a Bootstrap?");
#endif
            ref var systemRef = ref world.GetUnsafeSystemRef<Systems.LatiosWorldUnmanagedSystem>(system);
            if (systemRef.m_impl == null)
                systemRef.Initialize(world);
            return new LatiosWorldUnmanaged
            {
                m_impl    = systemRef.m_impl,
                m_index   = systemRef.m_trackingIndex,
                m_version = systemRef.m_trackingVersion
            };
        }
    }
}

namespace Latios.Systems
{
    /// <summary>
    /// This system stores and manages the lifecycle of the LatiosWorldUnmanaged implementation.
    /// This system is the first system created by LatiosWorld. When safety checks are enabled
    /// and this system is destroyed, all instances of LatiosWorldUnmanaged are invalidated
    /// and will throw execeptions if used.
    /// However, resolving of this system is only performed when creating a new LatiosWorldUnmanaged
    /// instance from a WorldUnmanaged. All LatiosWorldUnmanaged instances store a safety-protected pointer
    /// directly to the implementation object for optimal runtime performance.
    /// </summary>
    [BurstCompile, DisableAutoCreation]
    public unsafe partial struct LatiosWorldUnmanagedSystem : ISystem
    {
        internal LatiosWorldUnmanagedImpl* m_impl;
        internal int                       m_trackingIndex;
        internal int                       m_trackingVersion;

        // Burst is safe here because Initialize could be early-invoked from Burst code
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            if (m_impl == null)
                Initialize(state.WorldUnmanaged);
            state.Enabled = false;
        }

        public void Initialize(WorldUnmanaged world)
        {
            LatiosWorldUnmanagedTracking.CreateHandle(out m_trackingIndex, out m_trackingVersion);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_trackingVersion == 0)
                throw new System.InvalidOperationException("Failed to allocate a LatiosWorldUnmanaged. This is an internal bug. Please report!");
#endif

            m_impl                   = AllocatorManager.Allocate<LatiosWorldUnmanagedImpl>(Allocator.Persistent);
            *m_impl                  = default;
            m_impl->m_worldUnmanaged = world;

            m_impl->m_worldBlackboardEntity = new BlackboardEntity(world.EntityManager.CreateEntity(), new LatiosWorldUnmanaged
            {
                m_impl    = m_impl,
                m_index   = m_trackingIndex,
                m_version = m_trackingVersion
            });
            m_impl->m_worldBlackboardEntity.AddComponentData(new WorldBlackboardTag());
            world.EntityManager.SetName(m_impl->m_worldBlackboardEntity, "World Blackboard Entity");

            m_impl->m_collectionComponentStorage = new CollectionComponentStorage(Allocator.Persistent);
            m_impl->m_collectionDependencies     = new UnsafeList<LatiosWorldUnmanagedImpl.CollectionDependency>(16, Allocator.Persistent);

            m_impl->m_executingSystemStack = new UnsafeList<SystemHandle>(16, Allocator.Persistent);
        }

        // The storage types require classes to handle typed storage disposal properly
        // so Burst can't be used here.
        public void OnDestroy(ref SystemState state)
        {
            m_impl->m_worldUnmanaged.EntityManager.CompleteAllTrackedJobs();

            if (m_impl->m_unmanagedSystemInterfacesDispatcher.IsAllocated)
            {
                (m_impl->m_unmanagedSystemInterfacesDispatcher.Target as UnmanagedExtraInterfacesDispatcher)?.Dispose();
                m_impl->m_unmanagedSystemInterfacesDispatcher.Free();
            }
            if (m_impl->m_managedStructStorage.IsAllocated)
            {
                (m_impl->m_managedStructStorage.Target as ManagedStructComponentStorage)?.Dispose();
                m_impl->m_managedStructStorage.Free();
            }
            m_impl->m_collectionComponentStorage.Dispose();
            m_impl->m_collectionDependencies.Dispose();
            m_impl->m_executingSystemStack.Dispose();
            AllocatorManager.Free(Allocator.Persistent, m_impl);
            m_impl = null;
            LatiosWorldUnmanagedTracking.DestroyHandle(m_trackingIndex, m_trackingVersion);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;
        }
    }
}

