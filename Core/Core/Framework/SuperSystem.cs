using System;
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

        public ManagedEntity sceneGlobalEntity => latiosWorld.sceneGlobalEntity;
        public ManagedEntity worldGlobalEntity => latiosWorld.worldGlobalEntity;

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
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override void OnUpdate()
        {
            foreach (var sys in Systems)
            {
                try
                {
                    if (sys is ILatiosSystem latiosSys)
                    {
                        if (latiosSys.ShouldUpdateSystem())
                        {
                            sys.Enabled = true;
                            sys.Update();
                        }
                        else if (sys.Enabled)
                        {
                            sys.Enabled = false;
                            //Update to invoke OnStopRunning().
                            sys.Update();
                        }
                        else
                        {
                            sys.Enabled = false;
                        }
                    }
                    else
                    {
                        sys.Update();
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }

                if (World.QuitUpdate)
                    break;
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public sealed override void SortSystemUpdateList()
        {
            // Do nothing.
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

        public void SortSystemsUsingAttributes()
        {
            base.SortSystemUpdateList();
        }

        #endregion API
    }
}

