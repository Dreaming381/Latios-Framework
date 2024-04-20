using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Systems
{
    /// <summary>
    /// A system responsible for dispatching the SyncPointPlaybackSystem.
    /// This system captures exceptions from playback and reschedules the system until no command buffers are left.
    /// If all goes well, SyncPointPlaybackSystem is only updated once per frame.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(BeginInitializationEntityCommandBufferSystem))]
    public partial class SyncPointPlaybackSystemDispatch : SuperSystem
    {
        SystemHandle m_syncPointPlaybackSystem;

        protected override void CreateSystems()
        {
            m_syncPointPlaybackSystem = World.GetOrCreateSystem<SyncPointPlaybackSystem>();
        }

        protected override void OnUpdate()
        {
            ref var playbackSystemRef = ref World.Unmanaged.GetUnsafeSystemRef<SyncPointPlaybackSystem>(m_syncPointPlaybackSystem);
            var     unmanaged         = World.Unmanaged.GetLatiosWorldUnmanaged();
            do
            {
                try
                {
                    UpdateSystem(unmanaged, m_syncPointPlaybackSystem);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }
            while (playbackSystemRef.needsAnotherRun);
        }
    }

    /// <summary>
    /// A system registered to LatiosWorldUnmanaged.syncPoint and responsible for playing back command buffers.
    /// Command buffers are played back in the order they are requested across all systems in the World since last update.
    /// If this system is valid, it is safe to invoke any of its methods.
    /// </summary>
    [DisableAutoCreation]
    [BurstCompile]
    public unsafe partial struct SyncPointPlaybackSystem : ISystem
    {
        #region Impl
        enum PlaybackType
        {
            Entity,
            Enable,
            Disable,
            Destroy,
            InstantiateNoData,
            InstantiateUntyped,
            AddComponentsUntyped,
        }

        struct PlaybackInstance
        {
            public PlaybackType type;
            public SystemHandle requestingSystem;
        }

        NativeList<EntityCommandBuffer>               m_entityCommandBuffers;
        NativeList<EnableCommandBuffer>               m_enableCommandBuffers;
        NativeList<DisableCommandBuffer>              m_disableCommandBuffers;
        NativeList<DestroyCommandBuffer>              m_destroyCommandBuffers;
        NativeList<InstantiateCommandBuffer>          m_instantiateCommandBuffersWithoutData;
        NativeList<InstantiateCommandBufferUntyped>   m_instantiateCommandBuffersUntyped;
        NativeList<AddComponentsCommandBufferUntyped> m_addComponentsCommandBuffersUntyped;

        NativeList<JobHandle>                m_jobHandles;
        NativeList<PlaybackInstance>         m_playbackInstances;
        AllocatorHelper<RewindableAllocator> m_commandBufferAllocator;

        LatiosWorldUnmanaged m_world;
        bool                 m_hasPendingJobHandlesToAcquire;
        internal bool hasPendingJobHandlesToAquire => m_hasPendingJobHandlesToAcquire;

        int  m_nextPlaybackIndex;
        bool m_needsAnotherRun;
        internal bool needsAnotherRun => m_needsAnotherRun;

        int m_entityIndex;
        int m_enableIndex;
        int m_disableIndex;
        int m_destroyIndex;
        int m_instantiateNoDataIndex;
        int m_instantiateUntypedIndex;
        int m_addComponentsUntypedIndex;

        internal NativeText.ReadOnly m_requestSystemNameForCurrentBuffer;
        internal FixedString64Bytes  m_currentBufferTypeName;

        Unity.Profiling.ProfilerMarker m_currentMarker;
        bool                           m_needsMarkerResolve;
        NativeText                     m_externalSourceText;

        // Custom allocator is not Burst-Compatible during initialization
        //[BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_world                                   = state.GetLatiosWorldUnmanaged();
            fixed(SyncPointPlaybackSystem* ptr        = &this)
            m_world.m_impl->m_syncPointPlaybackSystem = ptr;

            m_commandBufferAllocator = new AllocatorHelper<RewindableAllocator>(Allocator.Persistent);
            m_commandBufferAllocator.Allocator.Initialize(16 * 1024);

            m_playbackInstances                    = new NativeList<PlaybackInstance>(Allocator.Persistent);
            m_entityCommandBuffers                 = new NativeList<EntityCommandBuffer>(Allocator.Persistent);
            m_enableCommandBuffers                 = new NativeList<EnableCommandBuffer>(Allocator.Persistent);
            m_disableCommandBuffers                = new NativeList<DisableCommandBuffer>(Allocator.Persistent);
            m_destroyCommandBuffers                = new NativeList<DestroyCommandBuffer>(Allocator.Persistent);
            m_instantiateCommandBuffersWithoutData = new NativeList<InstantiateCommandBuffer>(Allocator.Persistent);
            m_instantiateCommandBuffersUntyped     = new NativeList<InstantiateCommandBufferUntyped>(Allocator.Persistent);
            m_addComponentsCommandBuffersUntyped   = new NativeList<AddComponentsCommandBufferUntyped>(Allocator.Persistent);

            m_jobHandles = new NativeList<JobHandle>(Allocator.Persistent);

            m_externalSourceText = new NativeText("OtherWorldlySource", Allocator.Persistent);
        }

        // Custom allocator is not Burst-Compatible during disposal
        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            m_playbackInstances.Clear();
            JobHandle.CompleteAll(m_jobHandles.AsArray());
            m_jobHandles.Dispose();
            m_commandBufferAllocator.Allocator.Rewind();
            m_commandBufferAllocator.Allocator.Dispose();
            m_commandBufferAllocator.Dispose();

            m_playbackInstances.Dispose();
            m_entityCommandBuffers.Dispose();
            m_enableCommandBuffers.Dispose();
            m_disableCommandBuffers.Dispose();
            m_destroyCommandBuffers.Dispose();
            m_instantiateCommandBuffersWithoutData.Dispose();
            m_instantiateCommandBuffersUntyped.Dispose();
            m_addComponentsCommandBuffersUntyped.Dispose();

            m_externalSourceText.Dispose();
        }

        public bool ShouldUpdateSystem()
        {
            return !m_playbackInstances.IsEmpty;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            JobHandle.CompleteAll(m_jobHandles.AsArray());
            m_jobHandles.Clear();
            state.CompleteDependency();
            m_needsAnotherRun = true;

            while (m_nextPlaybackIndex < m_playbackInstances.Length)
            {
                var instance = m_playbackInstances[m_nextPlaybackIndex];
                m_nextPlaybackIndex++;
                //Todo: We don't fail as gracefully as EntityCommandBufferSystem, but I'm not sure what is exactly required to meet that. There's way too much magic there.
                //var systemName = instance.requestingSystemType != null? TypeManager.GetSystemName(instance.requestingSystemType) : "Unknown";
                //Profiler.BeginSample(systemName);
                // Todo: Support fetching profile marker for systems associated with other worlds.

                m_currentBufferTypeName = default;
                switch (instance.type)
                {
                    case PlaybackType.Entity: m_currentBufferTypeName               = "EntityCommandBuffer"; break;
                    case PlaybackType.Enable: m_currentBufferTypeName               = "EnableCommandBuffer"; break;
                    case PlaybackType.Disable: m_currentBufferTypeName              = "DisableCommandBuffer"; break;
                    case PlaybackType.Destroy: m_currentBufferTypeName              = "DestroyCommandBuffer"; break;
                    case PlaybackType.InstantiateNoData: m_currentBufferTypeName    = "InstantiateCommandBuffer"; break;
                    case PlaybackType.InstantiateUntyped: m_currentBufferTypeName   = "InstantiateCommandBuffer"; break;
                    case PlaybackType.AddComponentsUntyped: m_currentBufferTypeName = "AddComponentsCommandBuffer"; break;
                }
                if (state.WorldUnmanaged.IsSystemValid(instance.requestingSystem))
                    m_requestSystemNameForCurrentBuffer = state.WorldUnmanaged.ResolveSystemStateRef(instance.requestingSystem).DebugName;
                else
                    m_requestSystemNameForCurrentBuffer = m_externalSourceText.AsReadOnly();
#if ENABLE_PROFILER
                var fixedBuffer = new FixedString4096Bytes(m_requestSystemNameForCurrentBuffer);
                m_currentMarker = new Unity.Profiling.ProfilerMarker($"{m_currentBufferTypeName}::{fixedBuffer}");
                m_currentMarker.Begin();
                m_needsMarkerResolve = true;
#endif

                switch (instance.type)
                {
                    case PlaybackType.Entity:
                    {
                        var ecb = m_entityCommandBuffers[m_entityIndex];
                        m_entityIndex++;
                        ecb.Playback(state.EntityManager);
                        break;
                    }
                    case PlaybackType.Enable:
                    {
                        var ecb = m_enableCommandBuffers[m_enableIndex];
                        m_enableIndex++;
                        ecb.Playback(state.EntityManager, SystemAPI.GetBufferLookup<LinkedEntityGroup>(true));
                        break;
                    }
                    case PlaybackType.Disable:
                    {
                        var dcb = m_disableCommandBuffers[m_disableIndex];
                        m_disableIndex++;
                        dcb.Playback(state.EntityManager, SystemAPI.GetBufferLookup<LinkedEntityGroup>(true));
                        break;
                    }
                    case PlaybackType.Destroy:
                    {
                        var dcb = m_destroyCommandBuffers[m_destroyIndex];
                        m_destroyIndex++;
                        dcb.Playback(state.EntityManager);
                        break;
                    }
                    case PlaybackType.InstantiateNoData:
                    {
                        var icb = m_instantiateCommandBuffersWithoutData[m_instantiateNoDataIndex];
                        m_instantiateNoDataIndex++;
                        icb.Playback(state.EntityManager);
                        break;
                    }
                    case PlaybackType.InstantiateUntyped:
                    {
                        var icb = m_instantiateCommandBuffersUntyped[m_instantiateUntypedIndex];
                        m_instantiateUntypedIndex++;
                        icb.Playback(state.EntityManager);
                        break;
                    }
                    case PlaybackType.AddComponentsUntyped:
                    {
                        var accb = m_addComponentsCommandBuffersUntyped[m_addComponentsUntypedIndex];
                        m_addComponentsUntypedIndex++;
                        accb.Playback(state.EntityManager);
                        break;
                    }
                }
#if ENABLE_PROFILER
                m_currentMarker.End();
                m_needsMarkerResolve = false;
#endif
            }
#if ENABLE_PROFILER
            if (m_needsMarkerResolve)
            {
                m_currentMarker.End();
                m_needsMarkerResolve = false;
            }
#endif
            m_needsAnotherRun   = false;
            m_nextPlaybackIndex = 0;
            m_playbackInstances.Clear();
            m_entityCommandBuffers.Clear();
            m_enableCommandBuffers.Clear();
            m_disableCommandBuffers.Clear();
            m_destroyCommandBuffers.Clear();
            m_instantiateCommandBuffersWithoutData.Clear();
            m_instantiateCommandBuffersUntyped.Clear();
            m_addComponentsCommandBuffersUntyped.Clear();

            m_entityIndex               = 0;
            m_enableIndex               = 0;
            m_disableIndex              = 0;
            m_destroyIndex              = 0;
            m_instantiateNoDataIndex    = 0;
            m_instantiateUntypedIndex   = 0;
            m_addComponentsUntypedIndex = 0;

            m_commandBufferAllocator.Allocator.Rewind();
        }

        #endregion

        #region API
        /// <summary>
        /// Creates a new EntityCommandBuffer that will be played back by this system.
        /// </summary>
        public EntityCommandBuffer CreateEntityCommandBuffer()
        {
            m_hasPendingJobHandlesToAcquire = true;
            var ecb                         = new EntityCommandBuffer(m_commandBufferAllocator.Allocator.Handle.ToAllocator, PlaybackPolicy.SinglePlayback);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.Entity,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_entityCommandBuffers.Add(ecb);
            return ecb;
        }

        /// <summary>
        /// Creates a new EnableCommandBuffer that will be played back by this system.
        /// </summary>
        public EnableCommandBuffer CreateEnableCommandBuffer()
        {
            m_hasPendingJobHandlesToAcquire = true;
            var ecb                         = new EnableCommandBuffer(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.Enable,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_enableCommandBuffers.Add(ecb);
            return ecb;
        }

        /// <summary>
        /// Creates a new DisableCommandBuffer that will be played back by this system.
        /// </summary>
        public DisableCommandBuffer CreateDisableCommandBuffer()
        {
            m_hasPendingJobHandlesToAcquire = true;
            var dcb                         = new DisableCommandBuffer(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.Disable,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_disableCommandBuffers.Add(dcb);
            return dcb;
        }

        /// <summary>
        /// Creates a new DestroyCommandBuffer that will be played back by this system.
        /// </summary>
        public DestroyCommandBuffer CreateDestroyCommandBuffer()
        {
            m_hasPendingJobHandlesToAcquire = true;
            var dcb                         = new DestroyCommandBuffer(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.Destroy,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_destroyCommandBuffers.Add(dcb);
            return dcb;
        }

        /// <summary>
        /// Creates a new InstantiateCommandBuffer that will be played back by this system.
        /// </summary>
        public InstantiateCommandBuffer CreateInstantiateCommandBuffer()
        {
            m_hasPendingJobHandlesToAcquire = true;
            var icb                         = new InstantiateCommandBuffer(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.InstantiateNoData,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersWithoutData.Add(icb);
            return icb;
        }

        /// <summary>
        /// Creates a new InstantiateCommandBuffer that will be played back by this system.
        /// </summary>
        public InstantiateCommandBuffer<T0> CreateInstantiateCommandBuffer<T0>() where T0 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var icb                         = new InstantiateCommandBuffer<T0>(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.InstantiateUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        /// <summary>
        /// Creates a new InstantiateCommandBuffer that will be played back by this system.
        /// </summary>
        public InstantiateCommandBuffer<T0, T1> CreateInstantiateCommandBuffer<T0, T1>() where T0 : unmanaged, IComponentData where T1 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var icb                         = new InstantiateCommandBuffer<T0, T1>(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.InstantiateUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        /// <summary>
        /// Creates a new InstantiateCommandBuffer that will be played back by this system.
        /// </summary>
        public InstantiateCommandBuffer<T0, T1, T2> CreateInstantiateCommandBuffer<T0, T1, T2>() where T0 : unmanaged, IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var icb                         = new InstantiateCommandBuffer<T0, T1, T2>(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.InstantiateUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        /// <summary>
        /// Creates a new InstantiateCommandBuffer that will be played back by this system.
        /// </summary>
        public InstantiateCommandBuffer<T0, T1, T2, T3> CreateInstantiateCommandBuffer<T0, T1, T2, T3>() where T0 : unmanaged, IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData where T3 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var icb                         = new InstantiateCommandBuffer<T0, T1, T2, T3>(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.InstantiateUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        /// <summary>
        /// Creates a new InstantiateCommandBuffer that will be played back by this system.
        /// </summary>
        public InstantiateCommandBuffer<T0, T1, T2, T3, T4> CreateInstantiateCommandBuffer<T0, T1, T2, T3, T4>() where T0 : unmanaged, IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData where T3 : unmanaged, IComponentData where T4 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var icb                         = new InstantiateCommandBuffer<T0, T1, T2, T3, T4>(m_commandBufferAllocator.Allocator.Handle.ToAllocator);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.InstantiateUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        /// <summary>
        /// Creates a new AddComponentsCommandBuffer that will be played back by this system.
        /// </summary>
        public AddComponentsCommandBuffer<T0> CreateAddComponentsCommandBuffer<T0>(AddComponentsDestroyedEntityResolution destroyedEntityResolution) where T0 : unmanaged,
        IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var accb                        = new AddComponentsCommandBuffer<T0>(m_commandBufferAllocator.Allocator.Handle.ToAllocator, destroyedEntityResolution);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.AddComponentsUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_addComponentsCommandBuffersUntyped.Add(accb.m_addComponentsCommandBufferUntyped);
            return accb;
        }

        /// <summary>
        /// Creates a new AddComponentsCommandBuffer that will be played back by this system.
        /// </summary>
        public AddComponentsCommandBuffer<T0, T1> CreateAddComponentsCommandBuffer<T0, T1>(AddComponentsDestroyedEntityResolution destroyedEntityResolution) where T0 : unmanaged,
        IComponentData where T1 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var accb                        = new AddComponentsCommandBuffer<T0, T1>(m_commandBufferAllocator.Allocator.Handle.ToAllocator, destroyedEntityResolution);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.AddComponentsUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_addComponentsCommandBuffersUntyped.Add(accb.m_addComponentsCommandBufferUntyped);
            return accb;
        }

        /// <summary>
        /// Creates a new AddComponentsCommandBuffer that will be played back by this system.
        /// </summary>
        public AddComponentsCommandBuffer<T0, T1, T2> CreateAddComponentsCommandBuffer<T0, T1,
                                                                                       T2>(AddComponentsDestroyedEntityResolution destroyedEntityResolution) where T0 : unmanaged,
        IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var accb                        = new AddComponentsCommandBuffer<T0, T1, T2>(m_commandBufferAllocator.Allocator.Handle.ToAllocator, destroyedEntityResolution);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.AddComponentsUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_addComponentsCommandBuffersUntyped.Add(accb.m_addComponentsCommandBufferUntyped);
            return accb;
        }

        /// <summary>
        /// Creates a new AddComponentsCommandBuffer that will be played back by this system.
        /// </summary>
        public AddComponentsCommandBuffer<T0, T1, T2, T3> CreateAddComponentsCommandBuffer<T0, T1, T2,
                                                                                           T3>(AddComponentsDestroyedEntityResolution destroyedEntityResolution) where T0 :
        unmanaged, IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData where T3 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var accb                        = new AddComponentsCommandBuffer<T0, T1, T2, T3>(m_commandBufferAllocator.Allocator.Handle.ToAllocator, destroyedEntityResolution);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.AddComponentsUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_addComponentsCommandBuffersUntyped.Add(accb.m_addComponentsCommandBufferUntyped);
            return accb;
        }

        /// <summary>
        /// Creates a new AddComponentsCommandBuffer that will be played back by this system.
        /// </summary>
        public AddComponentsCommandBuffer<T0, T1, T2, T3, T4> CreateAddComponentsCommandBuffer<T0, T1, T2, T3,
                                                                                               T4>(AddComponentsDestroyedEntityResolution destroyedEntityResolution) where T0 :
        unmanaged, IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData where T3 : unmanaged, IComponentData where T4 : unmanaged, IComponentData
        {
            m_hasPendingJobHandlesToAcquire = true;
            var accb                        = new AddComponentsCommandBuffer<T0, T1, T2, T3, T4>(m_commandBufferAllocator.Allocator.Handle.ToAllocator, destroyedEntityResolution);
            var instance                    = new PlaybackInstance
            {
                type             = PlaybackType.AddComponentsUntyped,
                requestingSystem = m_world.m_impl->m_worldUnmanaged.GetCurrentlyExecutingSystem()
            };
            m_playbackInstances.Add(instance);
            m_addComponentsCommandBuffersUntyped.Add(accb.m_addComponentsCommandBufferUntyped);
            return accb;
        }

        /// <summary>
        /// Adds a JobHandle representing a job or set of jobs which write to a command buffer produced by this system.
        /// You do not need to call this method if the system is tracked by the LatiosWorld. However, calling this method
        /// inside a tracked system cancels automatic dependency propagation for that system for that update, which may be desirable.
        /// </summary>
        public void AddJobHandleForProducer(JobHandle handle)
        {
            m_jobHandles.Add(handle);
            m_hasPendingJobHandlesToAcquire = false;
        }

        /// <summary>
        /// Specify that any jobs (if any) using the command buffer produced by this system have already been completed.
        /// You do not need to call this method if the system is tracked by the LatiosWorld. However, calling this method
        /// insdie a tracked system cancels automatic dependency propagation for that system for that update, which may be desirable.
        /// </summary>
        public void AddMainThreadCompletionForProducer()
        {
            m_hasPendingJobHandlesToAcquire = false;
        }

        #endregion
    }
}

