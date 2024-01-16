using System;
using System.ComponentModel;
using Unity.Entities;
using Unity.Entities.Exposed;

namespace Latios
{
    /// <summary>
    /// A SuperSystem that typically serves as a root when using Explicit System Ordering.
    /// These systems will be injected using attribute ordering, and explicitly specify
    /// child systems they update.
    /// </summary>
    public abstract partial class RootSuperSystem : SuperSystem
    {
    }

    /// <summary>
    /// A subclass of ComponentSystemGroup which provides Latios Framework Core features.
    /// </summary>
    public abstract partial class SuperSystem : ComponentSystemGroup, ILatiosSystem
    {
        /// <summary>
        /// The latiosWorld of this system
        /// </summary>
        public LatiosWorld latiosWorld { get; private set; }

        /// <summary>
        /// The unmanaged aspects of a latiosWorld this system belongs to, which is also valid in editor worlds
        /// </summary>
        public LatiosWorldUnmanaged latiosWorldUnmanaged { get; private set; }

        /// <summary>
        /// The scene blackboard entity for the LatiosWorld of this system
        /// </summary>
        public BlackboardEntity sceneBlackboardEntity => latiosWorld.sceneBlackboardEntity;
        /// <summary>
        /// The world blackboard entity for the LatiosWorld of this system
        /// </summary>
        public BlackboardEntity worldBlackboardEntity => latiosWorld.worldBlackboardEntity;

        /// <summary>
        /// Begins a Fluent query chain
        /// </summary>
        public FluentQuery Fluent => this.Fluent();

        /// <summary>
        /// Override this method to perform additional filtering to decide if this system should run.
        /// If it does not run, none of its child systems will run either.
        /// </summary>
        /// <returns>true if this system should run</returns>
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
                latiosWorld          = lWorld;
                latiosWorldUnmanaged = latiosWorld.latiosWorldUnmanaged;
            }
            else
            {
                latiosWorldUnmanaged = World.Unmanaged.GetLatiosWorldUnmanaged();
            }
            CreateSystems();

            if (!m_overrideSystemSortingPreference)
                base.EnableSystemSorting = !latiosWorld.useExplicitSystemOrdering;
        }

        public EntityQuery GetEntityQuery(EntityQueryDesc desc) => GetEntityQuery(new EntityQueryDesc[] { desc });
        public EntityQuery GetEntityQuery(EntityQueryBuilder desc) => GetEntityQuery(desc);

        #region API
        /// <summary>
        /// Define the systems to be added to this SuperSystem using GetOrCreateAndAddSystem<>()
        /// or GetOrCreateAndAddUnmanagedSystem<>(). Leave empty for injection workflows.
        /// </summary>
        protected abstract void CreateSystems();

        /// <summary>
        /// Override to get alerted whenever a new sceneBlackboardEntity is created
        /// </summary>
        public virtual void OnNewScene()
        {
        }

        /// <summary>
        /// Creates a new system from the given type and adds it to this SuperSystem's update list.
        /// If system sorting is disabled, the update order is based on the order the systems are added.
        /// If the system already exists in the world, that system is added to the update list and returned instead.
        /// </summary>
        /// <param name="type">The type of system to add. It can be managed or unmanaged.</param>
        /// <returns>A union object that contains the created managed or unmanaged system added</returns>
        public BootstrapTools.ComponentSystemBaseSystemHandleUnion GetOrCreateAndAddSystem(Type type)
        {
            if (typeof(ComponentSystemBase).IsAssignableFrom(type))
            {
                var system = World.GetOrCreateSystemManaged(type);
                AddSystemToUpdateList(system);
                return new BootstrapTools.ComponentSystemBaseSystemHandleUnion
                {
                    systemHandle  = system.SystemHandle,
                    systemManaged = system
                };
            }
            if (typeof(ISystem).IsAssignableFrom(type))
            {
                var system = World.GetOrCreateSystem(type);
                AddSystemToUpdateList(system);
                return new BootstrapTools.ComponentSystemBaseSystemHandleUnion
                {
                    systemHandle  = system,
                    systemManaged = null
                };
            }
            return default;
        }

        /// <summary>
        /// Creates a new managed system and adds it to this SuperSystem's update list.
        /// If system sorting is disabled, the update order is based on the order the systems are added.
        /// If the system already exists in the world, that system is added to the update list and returned instead.
        /// </summary>
        /// <typeparam name="T">The type of managed system to create</typeparam>
        /// <returns>The managed system added</returns>
        public T GetOrCreateAndAddManagedSystem<T>() where T : ComponentSystemBase
        {
            var system = World.GetOrCreateSystemManaged<T>();
            AddSystemToUpdateList(system);
            return system;
        }

        /// <summary>
        /// Creates a new unmanaged system and adds it to this SuperSystem's update list.
        /// If system sorting is disabled, the update order is based on the order the systems are added.
        /// If the system already exists in the world, that system is added to the update list and returned instead.
        /// </summary>
        /// <typeparam name="T">The type of unmanaged system to create</typeparam>
        /// <returns>The unmanaged system added</returns>
        public ref T GetOrCreateAndAddUnmanagedSystem<T>() where T : unmanaged, ISystem
        {
            var system = World.GetOrCreateSystem<T>();
            AddSystemToUpdateList(system);
            return ref World.Unmanaged.GetUnsafeSystemRef<T>(system);
        }

        private bool m_overrideSystemSortingPreference = false;

        public new bool EnableSystemSorting
        {
            get { return base.EnableSystemSorting; }
            set
            {
                m_overrideSystemSortingPreference = true;
                base.EnableSystemSorting          = value;
            }
        }

        public void SortSystemsUsingAttributes(bool enableSortingAlways = true)
        {
            base.EnableSystemSorting = true;
            SortSystems();
            EnableSystemSorting = enableSortingAlways;
        }

        /// <summary>
        /// Updates a system while supporting full Latios Framework features
        /// </summary>
        /// <param name="world">The world containing the system</param>
        /// <param name="system">The system's handle</param>
        public static unsafe void UpdateSystem(LatiosWorldUnmanaged world, SystemHandle system)
        {
            var managed = world.m_impl->m_worldUnmanaged.AsManagedSystem(system);
            if (managed != null)
            {
                UpdateManagedSystem(world.m_impl, managed);
            }
            else
            {
                UpdateUnmanagedSystem(world.m_impl, system);
            }
        }

        /// <summary>
        /// Updates an entire ComponentSystemGroup while supporting full Latios Framework features.
        /// This is assigned to the static ComponentSystemGroup delegate when a LatiosWorld is created.
        /// </summary>
        /// <param name="group">The ComponentSystemGroup to update</param>
        public static unsafe void DoLatiosFrameworkComponentSystemGroupUpdate(ComponentSystemGroup group)
        {
            if (group.isSystemListSortDirty)
                group.SortSystems();

            LatiosWorld lw = group.World as LatiosWorld;

            if (lw == null)
            {
                DoUnityUpdateAllSystems(group);
                return;
            }

            // Update all unmanaged and managed systems together, in the correct sort order.
            var world      = lw.latiosWorldUnmanaged;
            var enumerator = group.GetSystemEnumerator();
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
                        UpdateUnmanagedSystem(world.m_impl, handle);
                    }
                    else
                    {
                        // Update managed code.
                        UpdateManagedSystem(world.m_impl, enumerator.currentManaged);
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

                if (group.World.QuitUpdate)
                    break;
            }
        }

        #endregion API

        internal static void DoUnityUpdateAllSystems(ComponentSystemGroup group)
        {
            var worldUnmanaged = group.World.Unmanaged;
            var enumerator     = group.GetSystemEnumerator();
            while (enumerator.MoveNext())
            {
                try
                {
                    if (!enumerator.IsCurrentManaged)
                    {
                        // Update unmanaged (burstable) code.
                        enumerator.current.Update(worldUnmanaged);
                    }
                    else
                    {
                        enumerator.currentManaged.Update();
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }

                if (group.World.QuitUpdate)
                    break;
            }
        }

        internal static unsafe void UpdateUnmanagedSystem(LatiosWorldUnmanagedImpl* impl, SystemHandle system)
        {
            if (!impl->isAllowedToRun)
                return;
            try
            {
                impl->BeginDependencyTracking(system);
                var     wu    = impl->m_worldUnmanaged;
                ref var state = ref wu.ResolveSystemStateRef(system);
                if (state.World is LatiosWorld lw)
                {
                    var dispatcher = lw.UnmanagedExtraInterfacesDispatcher.GetDispatch(ref state);
                    if (dispatcher != null)
                    {
                        if (dispatcher.ShouldUpdateSystem(ref state))
                        {
                            state.Enabled = true;
                            system.Update(wu);
                        }
                        else if (state.Enabled)
                        {
                            state.Enabled = false;
                            system.Update(wu);
                        }
                        else
                        {
                            state.Enabled = false;
                        }
                    }
                    else
                        system.Update(wu);
                }
                else
                    system.Update(wu);
            }
            catch
            {
                impl->EndDependencyTracking(system, true);
                throw;
            }
            impl->EndDependencyTracking(system, false);
        }

        internal static unsafe void UpdateManagedSystem(LatiosWorldUnmanagedImpl* impl, ComponentSystemBase system)
        {
            if (!impl->isAllowedToRun)
                return;
            try
            {
                impl->BeginDependencyTracking(system.SystemHandle);
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
            catch
            {
                impl->EndDependencyTracking(system.SystemHandle, true);
                throw;
            }
            impl->EndDependencyTracking(system.SystemHandle, false);
        }
    }
}

