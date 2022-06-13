using System;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;

using Latios.Systems;

namespace Latios
{
    /// <summary>
    /// A specialized runtime World which contains Latios Framework core functionality.
    /// </summary>
    public class LatiosWorld : World
    {
        /// <summary>
        /// The worldBlackboardEntity associated with this world
        /// </summary>
        public BlackboardEntity worldBlackboardEntity { get; private set; }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private BlackboardEntity m_sceneBlackboardEntity;
        private bool m_sceneBlackboardSafetyOverride;
        /// <summary>
        /// The current sceneBlackboardEntity associated with this world
        /// </summary>
        public BlackboardEntity sceneBlackboardEntity
        {
            get
            {
                if (m_sceneBlackboardEntity == Entity.Null && !m_sceneBlackboardSafetyOverride)
                {
                    throw new InvalidOperationException(
                        "The sceneBlackboardEntity has not been initialized yet. If you are trying to access this entity in OnCreate(), please use OnNewScene() or another callback instead.");
                }
                return m_sceneBlackboardEntity;
            }
            private set => m_sceneBlackboardEntity = value;
        }
#else
        public BlackboardEntity sceneBlackboardEntity { get; private set; }
#endif
        /// <summary>
        /// The main syncPoint system from which to get command buffers.
        /// Command buffers retrieved from this property will have dependencies managed automatically
        /// </summary>
        public SyncPointPlaybackSystem syncPoint
        {
            get
            {
                m_activeSystemAccessedSyncPoint = true;
                return m_syncPointPlaybackSystem;
            }
            set => m_syncPointPlaybackSystem = value;
        }
        /// <summary>
        /// The InitializationSystemGroup of this world for convenience
        /// </summary>
        public InitializationSystemGroup initializationSystemGroup => m_initializationSystemGroup;
        /// <summary>
        /// The SimulationSystemGroup of this world for convenience
        /// </summary>
        public SimulationSystemGroup simulationSystemGroup => m_simulationSystemGroup;
        /// <summary>
        /// The PresentationsystemGroup of this world for convenience. It is null for NetCode server worlds.
        /// </summary>
        public PresentationSystemGroup presentationSystemGroup => m_presentationSystemGroup;

        /// <summary>
        /// Specifies the default system ordering behavior for any newly created SuperSystems.
        /// If true, automatic system ordering will by default be disabled for those SuperSystems.
        /// </summary>
        public bool useExplicitSystemOrdering = false;

        internal ManagedStructComponentStorage ManagedStructStorage { get { return m_componentStorage; } }
        internal CollectionComponentStorage CollectionComponentStorage { get { return m_collectionsStorage; } }
        internal UnmanagedExtraInterfacesDispatcher UnmanagedExtraInterfacesDispatcher { get { return m_interfacesDispatcher; } }

        private List<CollectionDependency> m_collectionDependencies = new List<CollectionDependency>();

        //Todo: Disposal of storages is currently done in ManagedStructStorageCleanupSystems.cs.
        //This is because overriding World doesn't give us an opportunity to override the Dispose method.
        //In the future it may be worth creating a DisposeTool system that Dispose events can be registered to.
        private ManagedStructComponentStorage      m_componentStorage   = new ManagedStructComponentStorage();
        private CollectionComponentStorage         m_collectionsStorage = new CollectionComponentStorage();
        private UnmanagedExtraInterfacesDispatcher m_interfacesDispatcher;

        private InitializationSystemGroup               m_initializationSystemGroup;
        private SimulationSystemGroup                   m_simulationSystemGroup;
        private PresentationSystemGroup                 m_presentationSystemGroup;
        private SyncPointPlaybackSystem                 m_syncPointPlaybackSystem;
        private SystemHandle<BlackboardUnmanagedSystem> m_blackboardUnmanagedSystem;

        private bool m_paused          = false;
        private bool m_resumeNextFrame = false;

        public enum WorldRole
        {
            Default,
            Client,
            Server
        }

        /// <summary>
        /// Creates a LatiosWorld
        /// </summary>
        /// <param name="name">The name of the world</param>
        /// <param name="flags">Specifies world flags</param>
        /// <param name="role">The role of the world. Leave at default unless this is a NetCode project.</param>
        public LatiosWorld(string name, WorldFlags flags = WorldFlags.Simulation, WorldRole role = WorldRole.Default) : base(name, flags)
        {
            Authoring.ConversionBootstrapUtilities.RegisterConversionWorldAction();

            //BootstrapTools.PopulateTypeManagerWithGenerics(typeof(ManagedComponentTag<>),               typeof(IManagedComponent));
            BootstrapTools.PopulateTypeManagerWithGenerics(typeof(ManagedComponentSystemStateTag<>),    typeof(IManagedComponent));
            //BootstrapTools.PopulateTypeManagerWithGenerics(typeof(CollectionComponentTag<>),            typeof(ICollectionComponent));
            BootstrapTools.PopulateTypeManagerWithGenerics(typeof(CollectionComponentSystemStateTag<>), typeof(ICollectionComponent));
            m_interfacesDispatcher = new UnmanagedExtraInterfacesDispatcher();

            var bus                     = this.GetOrCreateSystem<BlackboardUnmanagedSystem>();
            m_blackboardUnmanagedSystem = bus.Handle;

            worldBlackboardEntity = new BlackboardEntity(EntityManager.CreateEntity(), EntityManager);
            worldBlackboardEntity.AddComponentData(new WorldBlackboardTag());
            EntityManager.SetName(worldBlackboardEntity, "World Blackboard Entity");
            bus.Struct.worldBlackboardEntity = (Entity)worldBlackboardEntity;
            bus.Struct.sceneBlackboardEntity = default;

            if (role == WorldRole.Default)
            {
                m_initializationSystemGroup = GetOrCreateSystem<LatiosInitializationSystemGroup>();
                m_simulationSystemGroup     = GetOrCreateSystem<LatiosSimulationSystemGroup>();
                m_presentationSystemGroup   = GetOrCreateSystem<LatiosPresentationSystemGroup>();
            }
            else if (role == WorldRole.Client)
            {
#if NETCODE_PROJECT
                m_initializationSystemGroup = GetOrCreateSystem<Compatibility.UnityNetCode.LatiosClientInitializationSystemGroup>();
                m_simulationSystemGroup     = GetOrCreateSystem<Compatibility.UnityNetCode.LatiosClientSimulationSystemGroup>();
                m_presentationSystemGroup   = GetOrCreateSystem<Compatibility.UnityNetCode.LatiosClientPresentationSystemGroup>();
#endif
            }
            else if (role == WorldRole.Server)
            {
#if NETCODE_PROJECT
                m_initializationSystemGroup = GetOrCreateSystem<Compatibility.UnityNetCode.LatiosServerInitializationSystemGroup>();
                m_simulationSystemGroup     = GetOrCreateSystem<Compatibility.UnityNetCode.LatiosServerSimulationSystemGroup>();
#endif
            }

            m_syncPointPlaybackSystem = GetExistingSystem<SyncPointPlaybackSystem>();
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
        internal bool autoGenerateSceneBlackboardEntity = true;

        internal void FrameStart()
        {
            if (m_resumeNextFrame)
            {
                m_paused          = false;
                m_resumeNextFrame = false;
            }

            if (autoGenerateSceneBlackboardEntity)
            {
                CreateNewSceneBlackboardEntity();
                autoGenerateSceneBlackboardEntity = false;
            }
        }

        internal void CreateNewSceneBlackboardEntity(bool forceRecreate = false)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_sceneBlackboardSafetyOverride = true;
#endif
            bool existsAndIsValid = EntityManager.Exists(sceneBlackboardEntity) && sceneBlackboardEntity.HasComponent<SceneBlackboardTag>();
            if (forceRecreate && existsAndIsValid)
            {
                EntityManager.DestroyEntity(sceneBlackboardEntity);
                existsAndIsValid = false;
            }

            if (!existsAndIsValid)
            {
                sceneBlackboardEntity = new BlackboardEntity(EntityManager.CreateEntity(), EntityManager);
                sceneBlackboardEntity.AddComponentData(new SceneBlackboardTag());
                Unmanaged.ResolveSystem(m_blackboardUnmanagedSystem).sceneBlackboardEntity = (Entity)sceneBlackboardEntity;
                EntityManager.SetName(sceneBlackboardEntity, "Scene Blackboard Entity");

                foreach (var system in Systems)
                {
                    if (system is ILatiosSystem latiosSystem)
                    {
                        latiosSystem.OnNewScene();
                    }
                }

                var unmanaged       = Unmanaged;
                var unmanagedStates = unmanaged.GetAllSystemStates(Allocator.TempJob);
                for (int i = 0; i < unmanagedStates.Length; i++)
                {
                    var dispatcher = m_interfacesDispatcher.GetDispatch(ref unmanagedStates.At(i));
                    if (dispatcher != null)
                        dispatcher.OnNewScene(ref unmanagedStates.At(i));
                }
                unmanagedStates.Dispose();
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_sceneBlackboardSafetyOverride = false;
#endif
        }

        #region AutoDependencies

        private SubSystemBase m_activeSystem;
        private bool          m_activeSystemAccessedSyncPoint;

        internal void UpdateOrCompleteDependency(JobHandle readHandle, JobHandle writeHandle)
        {
            if (m_activeSystem != null)
            {
                var jh                              = m_activeSystem.SystemBaseDependency;
                jh                                  = JobHandle.CombineDependencies(readHandle, writeHandle, jh);
                m_activeSystem.SystemBaseDependency = jh;
            }
            else
            {
                JobHandle.CombineDependencies(readHandle, writeHandle).Complete();
            }
        }

        internal void UpdateOrCompleteDependency(JobHandle jobHandle)
        {
            if (m_activeSystem != null)
            {
                var jh                              = m_activeSystem.SystemBaseDependency;
                jh                                  = JobHandle.CombineDependencies(jobHandle, jh);
                m_activeSystem.SystemBaseDependency = jh;
            }
            else
            {
                jobHandle.Complete();
            }
        }

        internal void MarkCollectionDirty<T>(Entity entity, bool isReadOnly) where T : struct, ICollectionComponent
        {
            m_collectionDependencies.Add(new CollectionDependency(entity, typeof(T), isReadOnly));
        }

        internal void MarkCollectionClean<T>(Entity entity, bool isReadOnly) where T : struct, ICollectionComponent
        {
            if (isReadOnly)
            {
                RemoveAllMatchingCollectionDependenciesReadOnly(entity, typeof(T));
            }
            else
            {
                RemoveAllMatchingCollectionDependencies(entity, typeof(T));
            }
        }

        internal void BeginDependencyTracking(SubSystemBase sys)
        {
            if (m_activeSystem != null)
            {
                throw new InvalidOperationException(
                    $"{sys.GetType().Name} has detected that the previously updated {m_activeSystem.GetType().Name} did not finish its update procedure properly. This is likely due to an exception thrown from within OnUpdate(), but please note that calling Update() on a SubSystem from within another SubSystem is not supported.");
            }
            m_activeSystem                  = sys;
            m_activeSystemAccessedSyncPoint = false;
        }

        internal void EndDependencyTracking(JobHandle outputDeps)
        {
            m_activeSystem = null;
            if (outputDeps.IsCompleted)
            {
                //Todo: Is this necessary? And what are the performance impacts? Is there a better way to figure out that all jobs were using .Run()?
                outputDeps.Complete();
            }
            else
            {
                UpdateCollectionDependencies(outputDeps);
                if (m_activeSystemAccessedSyncPoint)
                {
                    m_syncPointPlaybackSystem.AddJobHandleForProducer(outputDeps);
                }
            }
        }

        internal void CheckMissingDependenciesForCollections(SubSystemBase sys)
        {
            if (m_collectionDependencies.Count > 0)
                Debug.LogWarning(
                    $"{sys} finished its update but there are ICollectionComponent instances, one of which was of type {m_collectionDependencies[0].type}, that were accessed but did not have their dependencies updated.");
        }

        private struct CollectionDependency
        {
            public Entity entity;
            public Type   type;
            public bool   readOnly;

            public CollectionDependency(Entity entity, Type type, bool readOnly)
            {
                this.entity   = entity;
                this.type     = type;
                this.readOnly = readOnly;
            }
        }

        private void UpdateCollectionDependencies(JobHandle outputDeps)
        {
            foreach (var dep in m_collectionDependencies)
            {
                if (dep.readOnly)
                {
                    m_collectionsStorage.UpdateReadHandle(dep.entity, dep.type, outputDeps);
                }
                else
                {
                    m_collectionsStorage.UpdateWriteHandle(dep.entity, dep.type, outputDeps);
                }
            }
            m_collectionDependencies.Clear();
        }

        private void RemoveAllMatchingCollectionDependencies(Entity entity, Type type)
        {
            for (int i = 0; i < m_collectionDependencies.Count; i++)
            {
                if (m_collectionDependencies[i].entity == entity && m_collectionDependencies[i].type == type)
                {
                    m_collectionDependencies.RemoveAtSwapBack(i);
                    i--;
                }
            }
        }

        private void RemoveAllMatchingCollectionDependenciesReadOnly(Entity entity, Type type)
        {
            for (int i = 0; i < m_collectionDependencies.Count; i++)
            {
                if (m_collectionDependencies[i].entity == entity && m_collectionDependencies[i].type == type && m_collectionDependencies[i].readOnly == true)
                {
                    m_collectionDependencies.RemoveAtSwapBack(i);
                    i--;
                }
            }
        }
        #endregion
    }

namespace Systems
{
    /// <summary>
    /// The SimulationSystemGroup for a LatiosWorld created with WorldRole.Default
    /// </summary>
    [DisableAutoCreation, NoGroupInjection]
    public class LatiosSimulationSystemGroup : SimulationSystemGroup
    {
        SystemSortingTracker m_tracker;
        internal bool        skipInDeferred = false;

        protected override void OnUpdate()
        {
            if (!skipInDeferred)
                SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }

    /// <summary>
    /// The PresentationSystemGroup for a LatiosWorld created with WorldRole.Default
    /// </summary>
    [DisableAutoCreation, NoGroupInjection]
    public class LatiosPresentationSystemGroup : PresentationSystemGroup
    {
        SystemSortingTracker m_tracker;

        protected override void OnUpdate()
        {
            SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }
}
}

