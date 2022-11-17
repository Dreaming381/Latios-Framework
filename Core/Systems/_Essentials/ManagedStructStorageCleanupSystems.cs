using System;
using System.Diagnostics;
using System.Reflection;
using Debug = UnityEngine.Debug;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios.Systems
{
    internal interface ManagedComponentsReactiveSystem
    {
        EntityQuery Query { get; }
    }

    /// <summary>
    /// A system group responsible for creating and running all dynamically generated systems which synchronize the state
    /// of managed struct components and collection components with their respective AssociatedComponentType each.
    /// If you encounter errors with this system using IL2CPP, make sure you have Smaller Builds as the IL2CPP generation mode.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosWorldSyncGroup), OrderFirst = true)]
    [UpdateAfter(typeof(MergeBlackboardsSystem))]
    public class ManagedComponentsReactiveSystemGroup : RootSuperSystem
    {
        struct AssociatedTypeCleanupTagTypePair : IEquatable<AssociatedTypeCleanupTagTypePair>
        {
            public ComponentType associatedType;
            public ComponentType cleanupTagType;

            public bool Equals(AssociatedTypeCleanupTagTypePair other)
            {
                return associatedType == other.associatedType && cleanupTagType == other.cleanupTagType;
            }
        }

        private EntityQuery m_allSystemsQuery;

        public override bool ShouldUpdateSystem()
        {
            for (int i = 0; i < Systems.Count; i++)
            {
                var mcrs = Systems[i] as ManagedComponentsReactiveSystem;
                if (!mcrs.Query.IsEmptyIgnoreFilter)
                    return true;
            }
            return false;

            //return m_allSystemsQuery.IsEmptyIgnoreFilter == false;
        }

        //private struct ManagedDefeatStripTag : IComponentData { }
        //private struct ManagedDefeatStripComponent : IManagedComponent
        //{
        //    public Type AssociatedComponentType => typeof(ManagedDefeatStripTag);
        //}
        //
        //private struct CollectionDefeatStripTag : IComponentData { }
        //private struct CollectionDefeatStripComponent : ICollectionComponent
        //{
        //    public Type AssociatedComponentType => typeof(ManagedDefeatStripTag);
        //    public JobHandle Dispose(JobHandle inputDeps) => default;
        //}

        protected override void CreateSystems()
        {
            // Defeat stripping
            //World.DestroySystem(World.CreateSystem<ManagedComponentCreateSystem<ManagedDefeatStripComponent> >());
            //World.DestroySystem(World.CreateSystem<ManagedComponentDestroySystem<ManagedDefeatStripComponent> >());
            //World.DestroySystem(World.CreateSystem<CollectionComponentCreateSystem<CollectionDefeatStripComponent> >());
            //World.DestroySystem(World.CreateSystem<CollectionComponentDestroySystem<CollectionDefeatStripComponent> >());

            var managedCreateType         = typeof(ManagedComponentCreateSystem<>);
            var managedDestroyType        = typeof(ManagedComponentDestroySystem<>);
            var collectionCreateType      = typeof(CollectionComponentCreateSystem<>);
            var collectionDestroyType     = typeof(CollectionComponentDestroySystem<>);
            var managedSysStateTagType    = typeof(ManagedComponentCleanupTag<>);
            var collectionSysStateTagType = typeof(CollectionComponentCleanupTag<>);

            var typePairs = new NativeParallelHashSet<AssociatedTypeCleanupTagTypePair>(128, Allocator.TempJob);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (!BootstrapTools.IsAssemblyReferencingLatios(assembly))
                    continue;

                foreach (var type in assembly.GetTypes())
                {
                    if (type.GetCustomAttribute(typeof(DisableAutoTypeRegistrationAttribute)) != null)
                        continue;

                    if (type == typeof(IManagedStructComponent) || type == typeof(ICollectionComponent)
                        //|| type == typeof(ManagedDefeatStripComponent) ||
                        //type == typeof(CollectionDefeatStripComponent)
                        )
                        continue;

                    if (typeof(IManagedStructComponent).IsAssignableFrom(type))
                    {
                        GetOrCreateAndAddSystem(managedCreateType.MakeGenericType(type));
                        GetOrCreateAndAddSystem(managedDestroyType.MakeGenericType(type));
                        typePairs.Add(new AssociatedTypeCleanupTagTypePair
                        {
                            cleanupTagType = ComponentType.ReadOnly(managedSysStateTagType.MakeGenericType(type)),
                            associatedType = ComponentType.ReadOnly((Activator.CreateInstance(type) as IManagedStructComponent).AssociatedComponentType.GetManagedType())
                        });
                    }
                    else if (typeof(ICollectionComponent).IsAssignableFrom(type))
                    {
                        GetOrCreateAndAddSystem(collectionCreateType.MakeGenericType(type));
                        GetOrCreateAndAddSystem(collectionDestroyType.MakeGenericType(type));
                        typePairs.Add(new AssociatedTypeCleanupTagTypePair
                        {
                            cleanupTagType = ComponentType.ReadOnly(collectionSysStateTagType.MakeGenericType(type)),
                            associatedType = ComponentType.ReadOnly((Activator.CreateInstance(type) as ICollectionComponent).AssociatedComponentType.GetManagedType())
                        });
                    }
                }
            }

            //Todo: Bug in Unity prevents iterating over NativeHashSet (value is defaulted).
            var               typePairsArr = typePairs.ToNativeArray(Allocator.TempJob);
            EntityQueryDesc[] descs        = new EntityQueryDesc[typePairsArr.Length * 2];
            int               i            = 0;
            foreach (var pair in typePairsArr)
            {
                descs[i] = new EntityQueryDesc
                {
                    All  = new ComponentType[] { pair.associatedType },
                    None = new ComponentType[] { pair.cleanupTagType }
                };
                i++;
                descs[i] = new EntityQueryDesc
                {
                    All  = new ComponentType[] { pair.cleanupTagType },
                    None = new ComponentType[] { pair.associatedType }
                };
                i++;
            }
            //Bug in Unity prevents constructing this EntityQuery because the scratch buffer is hardcoded to a size of 1024 which is not enough.
            //m_allSystemsQuery = GetEntityQuery(descs);
            typePairsArr.Dispose();
            typePairs.Dispose();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }

    internal partial class ManagedComponentCreateSystem<T> : SubSystem, ManagedComponentsReactiveSystem where T : struct, IManagedStructComponent
    {
        private EntityQuery m_query;
        public EntityQuery Query => m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.Exclude<ManagedComponentCleanupTag<T> >(), ComponentType.ReadOnly(new T().AssociatedComponentType.GetManagedType()));
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Allocator.TempJob);
            var lw       = latiosWorldUnmanaged;
            foreach (var e in entities)
            {
                lw.AddManagedStructComponent<T>(e, default);
            }
            entities.Dispose();
        }
    }

    internal partial class ManagedComponentDestroySystem<T> : SubSystem, ManagedComponentsReactiveSystem where T : struct, IManagedStructComponent
    {
        private EntityQuery m_query;
        public EntityQuery Query => m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.ReadOnly<ManagedComponentCleanupTag<T> >(), ComponentType.Exclude(new T().AssociatedComponentType.GetManagedType()));
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Allocator.TempJob);
            var lw       = latiosWorldUnmanaged;
            foreach (var e in entities)
            {
                lw.RemoveManagedStructComponent<T>(e);
            }
            entities.Dispose();
        }
    }

    internal partial class CollectionComponentCreateSystem<T> : SubSystem, ManagedComponentsReactiveSystem where T : unmanaged, ICollectionComponent
    {
        private EntityQuery m_query;
        public EntityQuery Query => m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.Exclude<CollectionComponentCleanupTag<T> >(), ComponentType.ReadOnly(new T().AssociatedComponentType.GetManagedType()));
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Allocator.TempJob);
            var lw       = latiosWorldUnmanaged;
            foreach (var e in entities)
            {
                lw.AddOrSetCollectionComponentAndDisposeOld<T>(e, default);
            }
            entities.Dispose();
        }
    }

    internal partial class CollectionComponentDestroySystem<T> : SubSystem, ManagedComponentsReactiveSystem where T : unmanaged, ICollectionComponent
    {
        private EntityQuery m_query;
        public EntityQuery Query => m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.ReadOnly<CollectionComponentCleanupTag<T> >(), ComponentType.Exclude(new T().AssociatedComponentType.TypeIndex));
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Allocator.TempJob);
            var lw       = latiosWorldUnmanaged;
            foreach (var e in entities)
            {
                lw.RemoveCollectionComponentAndDispose<T>(e);
            }
            entities.Dispose();
        }
    }
}

