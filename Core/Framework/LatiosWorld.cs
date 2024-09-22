using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Latios.Systems;
using Unity.Collections;
using Unity.Entities;

#if !ENTITY_STORE_V1
#error The Latios Framework requires the scripting define ENTITY_STORE_V1 for your safety and wellbeing.
#endif

namespace Latios
{
    /// <summary>
    /// A specialized runtime World which contains Latios Framework core functionality.
    /// </summary>
    public unsafe class LatiosWorld : World
    {
        /// <summary>
        /// The unmanaged state of the Latios World
        /// </summary>
        public LatiosWorldUnmanaged latiosWorldUnmanaged => m_unmanaged;

        /// <summary>
        /// The worldBlackboardEntity associated with this world
        /// </summary>
        public BlackboardEntity worldBlackboardEntity => m_unmanaged.worldBlackboardEntity;

        /// <summary>
        /// The current sceneBlackboardEntity associated with this world
        /// </summary>
        public BlackboardEntity sceneBlackboardEntity => m_unmanaged.sceneBlackboardEntity;
        /// <summary>
        /// The main syncPoint system from which to get command buffers.
        /// Command buffers retrieved from this property from within a system will have dependencies managed automatically
        /// </summary>
        public ref SyncPointPlaybackSystem syncPoint => ref m_unmanaged.syncPoint;
        /// <summary>
        /// The InitializationSystemGroup of this world for convenience
        /// </summary>
        public InitializationSystemGroup initializationSystemGroup => m_initializationSystemGroup;
        /// <summary>
        /// The SimulationSystemGroup of this world for convenience
        /// </summary>
        public SimulationSystemGroup simulationSystemGroup => m_simulationSystemGroup;
        /// <summary>
        /// The PresentationsystemGroup of this world for convenience. It is null for NetCode server and thin client worlds.
        /// </summary>
        public PresentationSystemGroup presentationSystemGroup => m_presentationSystemGroup;

        /// <summary>
        /// Specifies the default system ordering behavior for any newly created SuperSystems.
        /// If true, automatic system ordering will by default be disabled for those SuperSystems.
        /// </summary>
        public bool useExplicitSystemOrdering = false;

        /// <summary>
        /// If set to true, the World will stop updating systems if an exception is caught by the LatiosWorld.
        /// </summary>
        public bool zeroToleranceForExceptions { get => m_unmanaged.zeroToleranceForExceptions; set => m_unmanaged.zeroToleranceForExceptions = value; }

        /// <summary>
        /// Loads an asset from Resources and preserves it in a managed store. This is a workaround for Unity engine bugs.
        /// </summary>
        /// <typeparam name="T">The type of asset to load</typeparam>
        /// <param name="asset">The name of the asset</param>
        /// <returns>An unmanaged wrapper around the asset</returns>
        public UnityObjectRef<T> LoadFromResourcesAndPreserve<T>(string asset) where T : UnityEngine.Object
        {
            var loaded = UnityEngine.Resources.Load<T>(asset);
            m_defeatGCObjects.Add(loaded);
            return loaded;
        }

        private LatiosWorldUnmanaged m_unmanaged;

        private HashSet<UnityEngine.Object> m_defeatGCObjects;

        internal UnmanagedExtraInterfacesDispatcher UnmanagedExtraInterfacesDispatcher { get { return m_interfacesDispatcher; } }
        private UnmanagedExtraInterfacesDispatcher m_interfacesDispatcher;

        private InitializationSystemGroup m_initializationSystemGroup;
        private SimulationSystemGroup     m_simulationSystemGroup;
        private PresentationSystemGroup   m_presentationSystemGroup;
        private SystemHandle              m_latiosWorldUnmanagedSystem;

        private bool m_paused          = false;
        private bool m_resumeNextFrame = false;

        public enum WorldRole
        {
            Default,
            Client,
            Server,
            ThinClient
        }

        /// <summary>
        /// Creates a LatiosWorld
        /// </summary>
        /// <param name="name">The name of the world</param>
        /// <param name="flags">Specifies world flags</param>
        /// <param name="role">The role of the world. Leave at default unless this is a NetCode project.</param>
        public unsafe LatiosWorld(string name, WorldFlags flags = WorldFlags.Simulation, WorldRole role = WorldRole.Default) : base(name, flags)
        {
            ComponentSystemGroup.s_globalUpdateDelegate = SuperSystem.DoLatiosFrameworkComponentSystemGroupUpdate;

            m_defeatGCObjects = new HashSet<UnityEngine.Object>();

            m_interfacesDispatcher = new UnmanagedExtraInterfacesDispatcher();

            m_latiosWorldUnmanagedSystem = this.GetOrCreateSystem<LatiosWorldUnmanagedSystem>();

            m_unmanaged                                               = Unmanaged.GetLatiosWorldUnmanaged();
            m_unmanaged.m_impl->m_unmanagedSystemInterfacesDispatcher = GCHandle.Alloc(m_interfacesDispatcher, GCHandleType.Normal);

            if (role == WorldRole.Default || role == WorldRole.Client)
            {
                m_initializationSystemGroup = GetOrCreateSystemManaged<InitializationSystemGroup>();
                m_simulationSystemGroup     = GetOrCreateSystemManaged<SimulationSystemGroup>();
                m_presentationSystemGroup   = GetOrCreateSystemManaged<PresentationSystemGroup>();
            }
            else if (role == WorldRole.Server || role == WorldRole.ThinClient)
            {
                m_initializationSystemGroup = GetOrCreateSystemManaged<InitializationSystemGroup>();
                m_simulationSystemGroup     = GetOrCreateSystemManaged<SimulationSystemGroup>();
            }
            m_initializationSystemGroup.RateManager = new LatiosInitializationSystemGroupManager(this, m_initializationSystemGroup);
        }

        /// <summary>
        /// When the Scene Manager is not installed, call this function to destroy the old sceneBlackboardEntity,
        /// create a new one, and call the OnNewScene() method for all systems which have it.
        /// </summary>
        /// <returns></returns>
        public BlackboardEntity ForceCreateNewSceneBlackboardEntityAndCallOnNewScene()
        {
            CreateNewSceneBlackboardEntity(true);
            return sceneBlackboardEntity;
        }

        //Todo: Make this API public in the future.
        internal void Pause() => m_paused                    = true;
        internal void ResumeNextFrame() => m_resumeNextFrame = true;
        internal bool paused => m_paused;
        internal bool willResumeNextFrame => m_resumeNextFrame;

        internal void FrameStart()
        {
            if (m_resumeNextFrame)
            {
                m_paused          = false;
                m_resumeNextFrame = false;
            }
        }

        internal unsafe void CreateNewSceneBlackboardEntity(bool forceRecreate = false)
        {
            bool existsAndIsValid = m_unmanaged.isSceneBlackboardEntityCreated;
            if (forceRecreate && existsAndIsValid)
            {
                EntityManager.DestroyEntity(sceneBlackboardEntity);
                existsAndIsValid = false;
            }

            if (!existsAndIsValid)
            {
                m_unmanaged.CreateSceneBlackboardEntity();
            }
        }

        internal unsafe void DispatchNewSceneCallbacks()
        {
            foreach (var system in Systems)
            {
                if (system is ILatiosSystem latiosSystem)
                {
                    m_unmanaged.m_impl->BeginDependencyTracking(system.SystemHandle);
                    bool hadError = false;
                    try
                    {
                        latiosSystem.OnNewScene();
                    }
                    catch (Exception e)
                    {
                        hadError = true;
                        UnityEngine.Debug.LogException(e);
                    }
                    finally
                    {
                        m_unmanaged.m_impl->EndDependencyTracking(system.SystemHandle, hadError);
                    }
                }
            }

            var unmanaged        = Unmanaged;
            var unmanagedSystems = unmanaged.GetAllUnmanagedSystems(Allocator.TempJob);
            for (int i = 0; i < unmanagedSystems.Length; i++)
            {
                ref var systemState = ref unmanaged.ResolveSystemStateRef(unmanagedSystems[i]);
                var     dispatcher  = m_interfacesDispatcher.GetDispatch(ref systemState);
                if (dispatcher != null)
                {
                    m_unmanaged.m_impl->BeginDependencyTracking(systemState.SystemHandle);
                    bool hadError = false;
                    try
                    {
                        dispatcher.OnNewScene(ref systemState);
                    }
                    catch (Exception e)
                    {
                        hadError = true;
                        UnityEngine.Debug.LogException(e);
                    }
                    finally
                    {
                        m_unmanaged.m_impl->EndDependencyTracking(systemState.SystemHandle, hadError);
                    }
                }
            }
            unmanagedSystems.Dispose();
        }
    }
}

