using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace Latios
{
    public class LatiosWorld : World
    {
        public ManagedEntity worldGlobalEntity { get; private set; }
        public ManagedEntity sceneGlobalEntity { get; private set; }

        internal ManagedStructComponentStorage ManagedStructStorage { get { return m_componentStorage; } }
        internal CollectionComponentStorage CollectionComponentStorage { get { return m_collectionsStorage; } }

        private List<CollectionDependency> m_collectionDependencies = new List<CollectionDependency>();

        private ManagedStructComponentStorage m_componentStorage   = new ManagedStructComponentStorage();
        private CollectionComponentStorage    m_collectionsStorage = new CollectionComponentStorage();
        private InitializationSystemGroup     m_initializationSystemGroup;
        private SimulationSystemGroup         m_simulationSystemGroup;
        private PresentationSystemGroup       m_presentationSystemGroup;

        public LatiosWorld(string name) : base(name)
        {
            BootstrapTools.PopulateTypeManagerWithGenerics(typeof(ManagedComponentTag<>),               typeof(IComponent));
            BootstrapTools.PopulateTypeManagerWithGenerics(typeof(ManagedComponentSystemStateTag<>),    typeof(IComponent));
            BootstrapTools.PopulateTypeManagerWithGenerics(typeof(CollectionComponentTag<>),            typeof(ICollectionComponent));
            BootstrapTools.PopulateTypeManagerWithGenerics(typeof(CollectionComponentSystemStateTag<>), typeof(ICollectionComponent));

            worldGlobalEntity = new ManagedEntity(EntityManager.CreateEntity(), EntityManager);
            sceneGlobalEntity = new ManagedEntity(EntityManager.CreateEntity(), EntityManager);
            worldGlobalEntity.AddComponentData(new WorldGlobalTag());
            sceneGlobalEntity.AddComponentData(new SceneGlobalTag());

            m_initializationSystemGroup = GetOrCreateSystem<MyInitializationSystemGroup>();
            m_simulationSystemGroup     = GetOrCreateSystem<SimulationSystemGroup>();
            m_presentationSystemGroup   = GetOrCreateSystem<PresentationSystemGroup>();
        }

        internal void CreateNewSceneGlobalEntity()
        {
            if (!EntityManager.Exists(sceneGlobalEntity))
            {
                sceneGlobalEntity = new ManagedEntity(EntityManager.CreateEntity(), EntityManager);
                sceneGlobalEntity.AddComponentData(new SceneGlobalTag());
            }
        }

        #region CollectionDependencies
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

        internal void SetJobHandleForCollections(JobHandle handle) => UpdateCollectionDependencies(handle);

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
}

