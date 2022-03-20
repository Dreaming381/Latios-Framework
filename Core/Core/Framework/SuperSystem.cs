using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.Exposed.Dangerous;

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
            DoSuperSystemUpdate(this, ref m_systemSortingTracker);
        }

        public EntityQuery GetEntityQuery(EntityQueryDesc desc) => GetEntityQuery(new EntityQueryDesc[] { desc });

        #region API

        protected abstract void CreateSystems();

        public virtual void OnNewScene()
        {
        }

        public BootstrapTools.ComponentSystemBaseSystemHandleUntypedUnion GetOrCreateAndAddSystem(Type type)
        {
            if (typeof(ComponentSystemBase).IsAssignableFrom(type))
            {
                var system = World.GetOrCreateSystem(type);
                AddSystemToUpdateList(system);
                return new BootstrapTools.ComponentSystemBaseSystemHandleUntypedUnion
                {
                    systemHandle  = system.SystemHandleUntyped,
                    systemManaged = system
                };
            }
            if (typeof(ISystem).IsAssignableFrom(type))
            {
                var system = World.GetOrCreateUnmanagedSystem(type);
                AddUnmanagedSystemToUpdateList(system);
                return new BootstrapTools.ComponentSystemBaseSystemHandleUntypedUnion
                {
                    systemHandle  = system,
                    systemManaged = null
                };
            }
            return default;
        }

        public T GetOrCreateAndAddSystem<T>() where T : ComponentSystemBase
        {
            var system = World.GetOrCreateSystem<T>();
            AddSystemToUpdateList(system);
            return system;
        }

        public SystemRef<T> GetOrCreateAndAddUnmanagedSystem<T>() where T : unmanaged, ISystem
        {
            var system = World.GetOrCreateSystem<T>();
            AddUnmanagedSystemToUpdateList(system.Handle);
            return system;
        }

        public void SortSystemsUsingAttributes(bool enableSortingAlways = true)
        {
            EnableSystemSorting = true;
            SortSystems();
            EnableSystemSorting = enableSortingAlways;
        }

        new public static unsafe void UpdateSystem(ref WorldUnmanaged world, SystemHandleUntyped system)
        {
            var managed = world.AsManagedSystem(system);
            if (managed != null)
            {
                UpdateManagedSystem(managed);
            }
            else
            {
                var     wu    = world;
                ref var state = ref UnsafeUtility.AsRef<SystemState>(wu.ResolveSystemState(system));
                if(state.World is LatiosWorld lw)
                {
                    var dispatcher = lw.UnmanagedExtraInterfacesDispatcher.GetDispatch(ref state);
                    if (dispatcher != null)
                    {
                        if (!dispatcher.ShouldUpdateSystem(ref state))
                        {
                            state.Enabled = false;
                            return;
                        }
                        else
                            state.Enabled = true;
                    }
                }

                ComponentSystemGroup.UpdateSystem(ref wu, system);
            }
        }

        #endregion API

        internal static void UpdateManagedSystem(ComponentSystemBase system, bool propagateError = false)
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
                if (propagateError)
                    throw e;

                UnityEngine.Debug.LogException(e);
            }
        }

        SystemSortingTracker m_systemSortingTracker;

        internal static void UpdateAllSystems(ComponentSystemGroup group, ref SystemSortingTracker tracker)
        {
            tracker.CheckAndSortSystems(group);

            LatiosWorld lw = group.World as LatiosWorld;

            // Update all unmanaged and managed systems together, in the correct sort order.
            var world                     = group.World.Unmanaged;
            var previouslyExecutingSystem = world.ExecutingSystemHandle();
            var enumerator                = group.GetSystemEnumerator();
            while (enumerator.MoveNext())
            {
                if (lw.paused)
                    break;

                try
                {
                    if (!enumerator.IsCurrentManaged)
                    {
                        // Update unmanaged (burstable) code.
                        var handle = enumerator.current;
                        group.SetExecutingSystem(ref world, handle);
                        UpdateSystem(ref world, handle);
                    }
                    else
                    {
                        // Update managed code.
                        UpdateManagedSystem(enumerator.currentManaged, true);
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
#if UNITY_DOTSRUNTIME
                    // When in a DOTS Runtime build, throw this upstream -- continuing after silently eating an exception
                    // is not what you'll want, except maybe once we have LiveLink.  If you're looking at this code
                    // because your LiveLink dots runtime build is exiting when you don't want it to, feel free
                    // to remove this block, or guard it with something to indicate the player is not for live link.
                    throw;
#endif
                }
                finally
                {
                    group.SetExecutingSystem(ref world, previouslyExecutingSystem);
                }

                if (group.World.QuitUpdate)
                    break;
            }

            group.DestroyPendingSystemsInWorld(ref world);
        }

        internal static void DoSuperSystemUpdate(ComponentSystemGroup group, ref SystemSortingTracker tracker)
        {
            if (!group.Created)
                throw new InvalidOperationException(
                    $"Group of type {group.GetType()} has not been created, either the derived class forgot to call base.OnCreate(), or it has been destroyed");

            if (group.RateManager == null)
            {
                UpdateAllSystems(group, ref tracker);
            }
            else
            {
                while (group.RateManager.ShouldGroupUpdate(group))
                {
                    UpdateAllSystems(group, ref tracker);
                }
            }
        }
    }
}

