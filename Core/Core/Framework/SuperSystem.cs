using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Entities;

namespace Latios
{
    public abstract class RootSuperSystem : SuperSystem
    {
        bool m_recursiveContext = false;

        protected override void OnUpdate()
        {
            if (m_recursiveContext)
                return;

            bool shouldUpdate = ShouldUpdateSystem();
            if (!shouldUpdate)
            {
                Enabled            = false;
                m_recursiveContext = true;
                Update();
                m_recursiveContext = false;
                Enabled            = true;
            }
            else
            {
                base.OnUpdate();
            }
        }
    }

    public abstract class SuperSystem : ComponentSystemGroup, ILatiosSystem
    {
        public LatiosWorld latiosWorld { get; private set; }

        public BlackboardEntity sceneBlackboardEntity => latiosWorld.sceneBlackboardEntity;
        public BlackboardEntity worldBlackboardEntity => latiosWorld.worldBlackboardEntity;

        public FluentQuery Fluent => this.Fluent();

        public virtual bool ShouldUpdateSystem()
        {
            return Enabled;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected sealed override void OnCreate()
        {
            base.OnCreate();
            if (World is LatiosWorld lWorld)
            {
                latiosWorld = lWorld;
            }
            else
            {
                throw new InvalidOperationException("The current world is not of type LatiosWorld required for Latios framework functionality.");
            }
            CreateSystems();

            EnableSystemSorting &= !latiosWorld.useExplicitSystemOrdering;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override void OnUpdate()
        {
            UpdateAllManagedSystems(this);
        }

        public EntityQuery GetEntityQuery(EntityQueryDesc desc) => GetEntityQuery(new EntityQueryDesc[] { desc });

        #region API

        protected abstract void CreateSystems();

        public ComponentSystemBase GetOrCreateAndAddSystem(Type type)
        {
            var system = World.GetOrCreateSystem(type);
            AddSystemToUpdateList(system);
            return system;
        }

        public T GetOrCreateAndAddSystem<T>() where T : ComponentSystemBase
        {
            var system = World.GetOrCreateSystem<T>();
            AddSystemToUpdateList(system);
            return system;
        }

        public void SortSystemsUsingAttributes(bool enableSortingAlways = true)
        {
            EnableSystemSorting = true;
            SortSystems();
            EnableSystemSorting = enableSortingAlways;
        }

        #endregion API

        internal static void UpdateManagedSystem(ComponentSystemBase system)
        {
            try
            {
                if (system is ILatiosSystem latiosSys)
                {
                    if (latiosSys.ShouldUpdateSystem())
                    {
                        system.Enabled = true;
                        system.Update();
                    }
                    else if (system.Enabled)
                    {
                        system.Enabled = false;
                        //Update to invoke OnStopRunning().
                        system.Update();
                    }
                    else
                    {
                        system.Enabled = false;
                    }
                }
                else
                {
                    system.Update();
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        internal static void UpdateAllManagedSystems(ComponentSystemGroup group)
        {
            for (int i = 0; i < group.Systems.Count; i++)
            {
                UpdateManagedSystem(group.Systems[i]);

                if (group.World.QuitUpdate)
                    break;
            }
        }

        public virtual void OnNewScene()
        {
        }
    }
}

