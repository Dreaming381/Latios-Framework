using System;
using System.Diagnostics;
using System.Reflection;
using Debug = UnityEngine.Debug;
using Unity.Collections;
using Unity.Entities;

namespace Latios.Systems
{
    internal interface ManagedComponentsReactiveSystem
    {
        EntityQuery Query { get; }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosWorldSyncGroup), OrderFirst = true)]
    [UpdateAfter(typeof(MergeBlackboardsSystem))]
    public class ManagedComponentsReactiveSystemGroup : RootSuperSystem
    {
        struct AssociatedTypeSysStateTagTypePair : IEquatable<AssociatedTypeSysStateTagTypePair>
        {
            public ComponentType associatedType;
            public ComponentType sysStateTagType;

            public bool Equals(AssociatedTypeSysStateTagTypePair other)
            {
                return associatedType == other.associatedType && sysStateTagType == other.sysStateTagType;
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

        protected override void CreateSystems()
        {
            var managedCreateType         = typeof(ManagedComponentCreateSystem<>);
            var managedDestroyType        = typeof(ManagedComponentDestroySystem<>);
            var collectionCreateType      = typeof(CollectionComponentCreateSystem<>);
            var collectionDestroyType     = typeof(CollectionComponentDestroySystem<>);
            var managedSysStateTagType    = typeof(ManagedComponentSystemStateTag<>);
            var collectionSysStateTagType = typeof(CollectionComponentSystemStateTag<>);

            var typePairs = new NativeHashSet<AssociatedTypeSysStateTagTypePair>(128, Allocator.TempJob);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (!BootstrapTools.IsAssemblyReferencingLatios(assembly))
                    continue;

                foreach (var type in assembly.GetTypes())
                {
                    if (type.GetCustomAttribute(typeof(DisableAutoTypeRegistration)) != null)
                        continue;

                    if (type == typeof(IManagedComponent) || type == typeof(ICollectionComponent))
                        continue;

                    if (typeof(IManagedComponent).IsAssignableFrom(type))
                    {
                        GetOrCreateAndAddSystem(managedCreateType.MakeGenericType(type));
                        GetOrCreateAndAddSystem(managedDestroyType.MakeGenericType(type));
                        typePairs.Add(new AssociatedTypeSysStateTagTypePair
                        {
                            sysStateTagType = ComponentType.ReadOnly(managedSysStateTagType.MakeGenericType(type)),
                            associatedType  = ComponentType.ReadOnly((Activator.CreateInstance(type) as IManagedComponent).AssociatedComponentType)
                        });
                    }
                    else if (typeof(ICollectionComponent).IsAssignableFrom(type))
                    {
                        GetOrCreateAndAddSystem(collectionCreateType.MakeGenericType(type));
                        GetOrCreateAndAddSystem(collectionDestroyType.MakeGenericType(type));
                        typePairs.Add(new AssociatedTypeSysStateTagTypePair
                        {
                            sysStateTagType = ComponentType.ReadOnly(collectionSysStateTagType.MakeGenericType(type)),
                            associatedType  = ComponentType.ReadOnly((Activator.CreateInstance(type) as ICollectionComponent).AssociatedComponentType)
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
                    None = new ComponentType[] { pair.sysStateTagType }
                };
                i++;
                descs[i] = new EntityQueryDesc
                {
                    All  = new ComponentType[] { pair.sysStateTagType },
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
            latiosWorld.CollectionComponentStorage.Dispose();
        }
    }

    internal class ManagedComponentCreateSystem<T> : SubSystem, ManagedComponentsReactiveSystem where T : struct, IManagedComponent
    {
        private EntityQuery m_query;
        public EntityQuery Query => m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.Exclude<ManagedComponentSystemStateTag<T> >(), ComponentType.ReadOnly(new T().AssociatedComponentType));
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Allocator.TempJob);
            foreach (var e in entities)
            {
                EntityManager.AddManagedComponent(e, new T());
            }
            entities.Dispose();
        }
    }

    internal class ManagedComponentDestroySystem<T> : SubSystem, ManagedComponentsReactiveSystem where T : struct, IManagedComponent
    {
        private EntityQuery m_query;
        public EntityQuery Query => m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.ReadOnly<ManagedComponentSystemStateTag<T> >(), ComponentType.Exclude(new T().AssociatedComponentType));
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Allocator.TempJob);
            foreach(var e in entities)
            {
                EntityManager.RemoveManagedComponent<T>(e);
            }
            entities.Dispose();
        }
    }

    internal class CollectionComponentCreateSystem<T> : SubSystem, ManagedComponentsReactiveSystem where T : struct, ICollectionComponent
    {
        private EntityQuery m_query;
        public EntityQuery Query => m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.Exclude<CollectionComponentSystemStateTag<T> >(), ComponentType.ReadOnly(new T().AssociatedComponentType));
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Allocator.TempJob);
            foreach (var e in entities)
            {
                EntityManager.AddCollectionComponent(e, new T(), false);
            }
            entities.Dispose();
        }
    }

    internal class CollectionComponentDestroySystem<T> : SubSystem, ManagedComponentsReactiveSystem where T : struct, ICollectionComponent
    {
        private EntityQuery m_query;
        public EntityQuery Query => m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.ReadOnly<CollectionComponentSystemStateTag<T> >(), ComponentType.Exclude(new T().AssociatedComponentType));
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Allocator.TempJob);
            foreach (var e in entities)
            {
                EntityManager.RemoveCollectionComponentAndDispose<T>(e);
            }
            entities.Dispose();
        }
    }
}

