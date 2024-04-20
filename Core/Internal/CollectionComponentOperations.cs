using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.InternalSourceGen
{
    internal static unsafe class CollectionComponentOperations
    {
        static StaticAPI.ContextPtr Wrap(void* ptr) => new StaticAPI.ContextPtr {
            ptr = ptr
        };

        public static void CreateQueries(FunctionPointer<StaticAPI.BurstDispatchCollectionComponentDelegate> functionPtr,
                                         ref SystemState state,
                                         out EntityQuery addQuery,
                                         out EntityQuery removeQuery)
        {
            fixed (SystemState* ptr = &state)
            {
                var context = new CreateQueriesContext
                {
                    addQuery    = default,
                    removeQuery = default,
                    state       = ptr
                };

                functionPtr.Invoke(Wrap(UnsafeUtility.AddressOf(ref context)), (int)OperationType.CreateQueries);

                addQuery    = context.addQuery;
                removeQuery = context.removeQuery;
                return;
            }
        }

        public static void SyncQueries(FunctionPointer<StaticAPI.BurstDispatchCollectionComponentDelegate> functionPtr,
                                       LatiosWorldUnmanaged latiosWorld,
                                       EntityTypeHandle entityHandle,
                                       EntityQuery addQuery,
                                       EntityQuery removeQuery)
        {
            var context = new SyncQueriesContext
            {
                addQuery     = addQuery,
                removeQuery  = removeQuery,
                latiosWorld  = latiosWorld,
                entityHandle = entityHandle
            };

            functionPtr.Invoke(Wrap(UnsafeUtility.AddressOf(ref context)), (int)OperationType.SyncQueries);
        }

        public static void DisposeCollectionStorage(FunctionPointer<StaticAPI.BurstDispatchCollectionComponentDelegate> functionPtr,
                                                    CollectionComponentStorage*                                         storage,
                                                    int storageIndex)
        {
            var context = new DisposeCollectionStorageContext
            {
                storage      = storage,
                storageIndex = storageIndex
            };

            functionPtr.Invoke(Wrap(UnsafeUtility.AddressOf(ref context)), (int)OperationType.DisposeCollectionStorage);
        }

        enum OperationType : int
        {
            CreateQueries,
            SyncQueries,
            DisposeCollectionStorage,
        }

        struct CreateQueriesContext
        {
            public EntityQuery  addQuery;
            public EntityQuery  removeQuery;
            public SystemState* state;
        }

        struct SyncQueriesContext
        {
            public EntityQuery          addQuery;
            public EntityQuery          removeQuery;
            public LatiosWorldUnmanaged latiosWorld;
            public EntityTypeHandle     entityHandle;
        }

        struct DisposeCollectionStorageContext
        {
            public CollectionComponentStorage* storage;
            public int                         storageIndex;
        }

        public static void DispatchCollectionComponentOperation<T>(void* context, int operation) where T : unmanaged, ICollectionComponent,
        StaticAPI.ICollectionComponentSourceGenerated
        {
            var operationType = (OperationType)operation;
            switch (operationType)
            {
                case OperationType.CreateQueries:
                    CreateQueries<T>((CreateQueriesContext*)context);
                    break;
                case OperationType.SyncQueries:
                    SyncQueries<T>((SyncQueriesContext*)context);
                    break;
                case OperationType.DisposeCollectionStorage:
                    DisposeCollectionStorage<T>((DisposeCollectionStorageContext*)context);
                    break;
            }
        }

        static void CreateQueries<T>(CreateQueriesContext* context) where T : unmanaged, ICollectionComponent, StaticAPI.ICollectionComponentSourceGenerated
        {
            var t         = new T();
            var existType = new FixedList32Bytes<ComponentType>();
            existType.Add(t.componentType);
            var cleanupType = new FixedList32Bytes<ComponentType>();
            cleanupType.Add(t.cleanupType);

            var builder       = new EntityQueryBuilder(Allocator.Temp);
            context->addQuery = builder.WithAll(ref existType).WithNone(ref cleanupType).Build(ref *context->state);
            builder.Reset();
            context->removeQuery = builder.WithAll(ref cleanupType).WithNone(ref existType).Build(ref *context->state);
        }

        static void SyncQueries<T>(SyncQueriesContext* context) where T : unmanaged, ICollectionComponent, StaticAPI.ICollectionComponentSourceGenerated
        {
            if (!context->latiosWorld.isValid)
            {
                UnityEngine.Debug.LogError("LatiosWorldUnmanaged is invalid inside collection component processing. This may be a bug. Please report!");
                return;
            }

            ref var storage  = ref context->latiosWorld.m_impl->m_collectionComponentStorage;
            bool    needsAdd = !context->addQuery.IsEmptyIgnoreFilter;
            if (needsAdd)
            {
                var chunks = context->addQuery.ToArchetypeChunkArray(Allocator.Temp);

                foreach (var chunk in chunks)
                {
                    var entities = chunk.GetNativeArray(context->entityHandle);
                    foreach (var entity in entities)
                    {
                        storage.AddOrSetCollectionComponentAndDisposeOld<T>(entity, default, out _, out _);
                    }
                }
            }

            bool needsRemove = !context->removeQuery.IsEmptyIgnoreFilter;
            if (needsRemove)
            {
                var chunks = context->removeQuery.ToArchetypeChunkArray(Allocator.Temp);
                var jhs    = new NativeList<JobHandle>(Allocator.Temp);

                foreach (var chunk in chunks)
                {
                    var entities = chunk.GetNativeArray(context->entityHandle);
                    foreach (var entity in entities)
                    {
                        storage.RemoveIfPresentAndDisposeCollectionComponent<T>(entity, out var jh);
                        jhs.Add(jh);
                    }
                }

                JobHandle.CompleteAll(jhs.AsArray());
            }

            var t = new T().cleanupType;
            if (needsAdd)
                context->latiosWorld.m_impl->m_worldUnmanaged.EntityManager.AddComponent(context->addQuery, t);
            if (needsRemove)
                context->latiosWorld.m_impl->m_worldUnmanaged.EntityManager.RemoveComponent(context->addQuery, t);
        }

        static void DisposeCollectionStorage<T>(DisposeCollectionStorageContext* context) where T : unmanaged, ICollectionComponent, StaticAPI.ICollectionComponentSourceGenerated
        {
            context->storage->DisposeTypeUsingSourceGenDispatch<T>(context->storageIndex);
        }
    }
}

