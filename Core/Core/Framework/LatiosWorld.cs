using System;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

using Latios.Systems;

namespace Latios
{
    public class LatiosWorld : World
    {
        public ManagedEntity worldGlobalEntity { get; private set; }
        public ManagedEntity sceneGlobalEntity { get; private set; }

        internal ManagedStructComponentStorage ManagedStructStorage { get { return m_componentStorage; } }
        internal CollectionComponentStorage CollectionComponentStorage { get { return m_collectionsStorage; } }

        private List<CollectionDependency> m_collectionDependencies = new List<CollectionDependency>();

        //Todo: Disposal of collection storage is currently done in ManagedStructStorageCleanupSystems.cs.
        //This is because overriding World doesn't give us an opportunity to override the Dispose method.
        //In the future it may be worth creating a DisposeTool system that Dispose events can be registered to.
        private ManagedStructComponentStorage m_componentStorage   = new ManagedStructComponentStorage();
        private CollectionComponentStorage    m_collectionsStorage = new CollectionComponentStorage();

        private InitializationSystemGroup m_initializationSystemGroup;
        private SimulationSystemGroup     m_simulationSystemGroup;
        private PresentationSystemGroup   m_presentationSystemGroup;

        private bool m_paused          = false;
        private bool m_resumeNextFrame = false;

        public LatiosWorld(string name) : base(name)
        {
            //BootstrapTools.PopulateTypeManagerWithGenerics(typeof(ManagedComponentTag<>),               typeof(IManagedComponent));
            BootstrapTools.PopulateTypeManagerWithGenerics(typeof(ManagedComponentSystemStateTag<>),    typeof(IManagedComponent));
            //BootstrapTools.PopulateTypeManagerWithGenerics(typeof(CollectionComponentTag<>),            typeof(ICollectionComponent));
            BootstrapTools.PopulateTypeManagerWithGenerics(typeof(CollectionComponentSystemStateTag<>), typeof(ICollectionComponent));

            worldGlobalEntity = new ManagedEntity(EntityManager.CreateEntity(), EntityManager);
            sceneGlobalEntity = new ManagedEntity(EntityManager.CreateEntity(), EntityManager);
            worldGlobalEntity.AddComponentData(new WorldGlobalTag());
            sceneGlobalEntity.AddComponentData(new SceneGlobalTag());

#if UNITY_EDITOR
            EntityManager.SetName(worldGlobalEntity, "World Global Entity");
            EntityManager.SetName(sceneGlobalEntity, "Scene Global Entity");
#endif

            m_initializationSystemGroup = GetOrCreateSystem<LatiosInitializationSystemGroup>();
            m_simulationSystemGroup     = GetOrCreateSystem<LatiosSimulationSystemGroup>();
            m_presentationSystemGroup   = GetOrCreateSystem<LatiosPresentationSystemGroup>();
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

        internal void CreateNewSceneGlobalEntity()
        {
            if (!EntityManager.Exists(sceneGlobalEntity) || !sceneGlobalEntity.HasComponentData<SceneGlobalTag>())
            {
                sceneGlobalEntity = new ManagedEntity(EntityManager.CreateEntity(), EntityManager);
                sceneGlobalEntity.AddComponentData(new SceneGlobalTag());
#if UNITY_EDITOR
                EntityManager.SetName(sceneGlobalEntity, "Scene Global Entity");
#endif
            }
        }

        #region CollectionDependencies
        private SubSystemBase m_activeSystem;

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

        internal void BeginCollectionTracking(SubSystemBase sys)
        {
            if (m_activeSystem != null)
            {
                throw new InvalidOperationException("Error: Calling Update on a SubSystem from within another SubSystem is not allowed!");
            }
            m_activeSystem = sys;
        }

        internal void EndCollectionTracking(JobHandle outputDeps)
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

    public class LatiosSimulationSystemGroup : SimulationSystemGroup
    {
        protected override void OnUpdate()
        {
            LatiosWorld lw = World as LatiosWorld;
            if (!lw.paused)
                base.OnUpdate();
        }
    }

    public class LatiosPresentationSystemGroup : PresentationSystemGroup
    {
        protected override void OnUpdate()
        {
            LatiosWorld lw = World as LatiosWorld;
            if (!lw.paused)
                base.OnUpdate();
        }
    }
}

