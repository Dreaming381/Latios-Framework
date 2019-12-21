using System;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;

namespace Latios
{
    public class ManagedStructStorageCleanupSystemGroup : SuperSystem
    {
        private EntityQuery m_anythingNeedsUpdateQuery;

        public override bool ShouldUpdateSystem()
        {
            return m_anythingNeedsUpdateQuery.CalculateChunkCount() > 0;
        }

        protected override void CreateSystems()
        {
            var managedType            = typeof(ManagedComponentCleanupSystem<>);
            var collectionType         = typeof(CollectionComponentCleanupSystem<>);
            var managedTagType         = typeof(ManagedComponentTag<>);
            var managedSysStateType    = typeof(ManagedComponentSystemStateTag<>);
            var collectionTagType      = typeof(CollectionComponentTag<>);
            var collectionSysStateType = typeof(CollectionComponentSystemStateTag<>);

            var componentTypesAny  = new NativeList<ComponentType>(Allocator.TempJob);
            var componentTypesNone = new NativeList<ComponentType>(Allocator.TempJob);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (!BootstrapTools.IsAssemblyReferencingLatios(assembly))
                    continue;

                foreach (var type in assembly.GetTypes())
                {
                    if (type.GetCustomAttribute(typeof(DisableAutoTypeRegistration)) != null)
                        continue;

                    if (type == typeof(IComponent) || type == typeof(ICollectionComponent))
                        continue;

                    if (typeof(IComponent).IsAssignableFrom(type))
                    {
                        GetOrCreateAndAddSystem(managedType.MakeGenericType(type));
                        componentTypesAny.Add(ComponentType.ReadOnly(managedSysStateType.MakeGenericType(type)));
                        componentTypesNone.Add(ComponentType.ReadOnly(managedTagType.MakeGenericType(type)));
                    }
                    else if (typeof(ICollectionComponent).IsAssignableFrom(type))
                    {
                        GetOrCreateAndAddSystem(collectionType.MakeGenericType(type));
                        componentTypesAny.Add(ComponentType.ReadOnly(collectionSysStateType.MakeGenericType(type)));
                        componentTypesNone.Add(ComponentType.ReadOnly(collectionTagType.MakeGenericType(type)));
                    }
                }
            }

            EntityQueryDesc desc = new EntityQueryDesc
            {
                Any  = componentTypesAny.ToArray(),
                None = componentTypesNone.ToArray()
            };
            m_anythingNeedsUpdateQuery = GetEntityQuery(desc);
            componentTypesAny.Dispose();
            componentTypesNone.Dispose();
        }
    }

    public class ManagedComponentCleanupSystem<T> : SubSystem where T : struct, IComponent
    {
        private EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.ReadOnly<ManagedComponentSystemStateTag<T> >(), ComponentType.Exclude<ManagedComponentTag<T> >());
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Unity.Collections.Allocator.TempJob);
            foreach(var e in entities)
            {
                EntityManager.RemoveManagedComponent<T>(e);
            }
            entities.Dispose();
        }
    }

    public class CollectionComponentCleanupSystem<T> : SubSystem where T : struct, ICollectionComponent
    {
        private EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = GetEntityQuery(ComponentType.ReadOnly<CollectionComponentSystemStateTag<T> >(), ComponentType.Exclude<CollectionComponentTag<T> >());
        }

        protected override void OnUpdate()
        {
            var entities = m_query.ToEntityArray(Unity.Collections.Allocator.TempJob);
            foreach (var e in entities)
            {
                EntityManager.RemoveCollectionComponentAndDispose<T>(e);
            }
            entities.Dispose();
        }
    }
}

