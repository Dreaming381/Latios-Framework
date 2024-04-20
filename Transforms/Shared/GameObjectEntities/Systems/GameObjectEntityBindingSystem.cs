using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class GameObjectEntityBindingSystem : SubSystem
    {
        EntityQuery m_clientsQuery;
        EntityQuery m_hostsQuery;
        EntityQuery m_newTransformToEntityQuery;
        EntityQuery m_deadTransformToEntityQuery;
        EntityQuery m_newTransformFromEntityQuery;
        EntityQuery m_deadTransformFromEntityQuery;
        EntityQuery m_removeDontDestroyOnSceneChangeQuery;

        List<IInitializeGameObjectEntity> m_initCache = new List<IInitializeGameObjectEntity>();

        protected override void OnCreate()
        {
            m_clientsQuery              = Fluent.With<GameObjectEntity.ExistComponent>(true).With<GameObjectEntityBindClient>().IncludeDisabledEntities().Build();
            m_hostsQuery                = Fluent.With<GameObjectEntityHost>().IncludeDisabledEntities().Build();
            m_newTransformToEntityQuery =
                Fluent.With<GameObjectEntity.ExistComponent>(true).With<CopyTransformToEntity>(true).Without<CopyTransformToEntityCleanupTag>().Build();
            m_deadTransformToEntityQuery  = Fluent.With<CopyTransformToEntityCleanupTag>(true).Without<CopyTransformToEntity>().Build();
            m_newTransformFromEntityQuery =
                Fluent.With<GameObjectEntity.ExistComponent>(true).With<CopyTransformFromEntityTag>(true).Without<CopyTransformFromEntityCleanupTag>().Build();
            m_deadTransformFromEntityQuery        = Fluent.With<CopyTransformFromEntityCleanupTag>(true).Without<CopyTransformFromEntityTag>().Build();
            m_removeDontDestroyOnSceneChangeQuery = Fluent.With<DontDestroyOnSceneChangeTag>(true).With<RemoveDontDestroyOnSceneChangeTag>().Build();

            worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new CopyTransformToEntityMapping
            {
                entityToIndexMap     = new NativeHashMap<Entity, int>(128, Allocator.Persistent),
                indexToEntityMap     = new NativeHashMap<int, Entity>(128, Allocator.Persistent),
                transformAccessArray = new UnityEngine.Jobs.TransformAccessArray(128)
            });
            worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new CopyTransformFromEntityMapping
            {
                entityToIndexMap     = new NativeHashMap<Entity, int>(128, Allocator.Persistent),
                indexToEntityMap     = new NativeHashMap<int, Entity>(128, Allocator.Persistent),
                transformAccessArray = new UnityEngine.Jobs.TransformAccessArray(128)
            });
        }

        protected override void OnUpdate()
        {
            var copyTransformToEntityMapping   = worldBlackboardEntity.GetCollectionComponent<CopyTransformToEntityMapping>();
            var copyTransformFromEntityMapping = worldBlackboardEntity.GetCollectionComponent<CopyTransformFromEntityMapping>();

            CompleteDependency();

            EntityManager.RemoveComponent(m_removeDontDestroyOnSceneChangeQuery,
                                          new ComponentTypeSet(ComponentType.ReadWrite<DontDestroyOnSceneChangeTag>(),
                                                               ComponentType.ReadWrite<RemoveDontDestroyOnSceneChangeTag>()));

            if (!m_clientsQuery.IsEmptyIgnoreFilter && !m_hostsQuery.IsEmptyIgnoreFilter)
            {
                var clientCount = m_clientsQuery.CalculateEntityCount();
                var matches     = new NativeList<Match>(clientCount, WorldUpdateAllocator);
                new MatchJob
                {
                    hostChunks        = m_hostsQuery.ToArchetypeChunkArray(WorldUpdateAllocator),
                    clientChunks      = m_clientsQuery.ToArchetypeChunkArray(WorldUpdateAllocator),
                    entityHandle      = SystemAPI.GetEntityTypeHandle(),
                    hostHandle        = SystemAPI.GetComponentTypeHandle<GameObjectEntityHost>(true),
                    clientHandle      = SystemAPI.GetComponentTypeHandle<GameObjectEntityBindClient>(true),
                    matches           = matches,
                    clientEntityCount = clientCount,
                }.Run();

                foreach (var match in matches)
                {
                    var transform = latiosWorldUnmanaged.GetManagedStructComponent<GameObjectEntity>(match.client);
                    latiosWorldUnmanaged.AddManagedStructComponent( match.host, transform);
                    latiosWorldUnmanaged.SetManagedStructComponent<GameObjectEntity>(match.client, default);  // Prevent disposal when entity is destroyed
                    EntityManager.AddComponent<CopyTransformFromEntityTag>(match.host);

                    transform.gameObjectTransform.GetComponent<GameObjectEntityAuthoring>().entity = match.host;
                    m_initCache.Clear();
                    transform.gameObjectTransform.GetComponents(m_initCache);
                    foreach (var initializer in m_initCache)
                    {
                        initializer.Initialize(latiosWorld, match.host);
                    }
                    EntityManager.DestroyEntity(match.client);
                    EntityManager.RemoveComponent<GameObjectEntityHost>(match.host);
                }
            }

            if (!m_deadTransformToEntityQuery.IsEmptyIgnoreFilter)
            {
                var entities = m_deadTransformToEntityQuery.ToEntityArray(Allocator.Temp);
                foreach (var entity in entities)
                {
                    var index = copyTransformToEntityMapping.entityToIndexMap[entity];
                    copyTransformToEntityMapping.transformAccessArray.RemoveAtSwapBack(index);
                    var movedEntity =
                        copyTransformToEntityMapping.indexToEntityMap[copyTransformToEntityMapping.transformAccessArray.length];
                    copyTransformToEntityMapping.indexToEntityMap[index]       = movedEntity;
                    copyTransformToEntityMapping.entityToIndexMap[movedEntity] = index;
                    copyTransformToEntityMapping.entityToIndexMap.Remove(entity);
                    copyTransformToEntityMapping.indexToEntityMap.Remove(copyTransformToEntityMapping.transformAccessArray.length);
                }
                EntityManager.RemoveComponent<CopyTransformToEntityCleanupTag>(m_deadTransformToEntityQuery);
            }

            if (!m_newTransformToEntityQuery.IsEmptyIgnoreFilter)
            {
                var entities = m_newTransformToEntityQuery.ToEntityArray(Allocator.Temp);
                if (entities.Length > copyTransformFromEntityMapping.transformAccessArray.capacity - copyTransformFromEntityMapping.transformAccessArray.length)
                {
                    // Need to reallocate
                    var newCapacity              = math.ceilpow2(2 * (entities.Length + copyTransformFromEntityMapping.transformAccessArray.length));
                    var oldMapping               = copyTransformToEntityMapping;
                    copyTransformToEntityMapping = new CopyTransformToEntityMapping
                    {
                        entityToIndexMap     = new NativeHashMap<Entity, int>(newCapacity, Allocator.Persistent),
                        indexToEntityMap     = new NativeHashMap<int, Entity>(newCapacity, Allocator.Persistent),
                        transformAccessArray = new UnityEngine.Jobs.TransformAccessArray(newCapacity),
                    };

                    for (int i = 0; i < oldMapping.transformAccessArray.length; i++)
                        copyTransformFromEntityMapping.transformAccessArray.Add(oldMapping.transformAccessArray[i]);

                    new CopyMapsJob
                    {
                        srcEntityToIntMap = oldMapping.entityToIndexMap,
                        srcIntToEntityMap = oldMapping.indexToEntityMap,
                        dstEntityToIntMap = copyTransformToEntityMapping.entityToIndexMap,
                        dstIntToEntityMap = copyTransformToEntityMapping.indexToEntityMap
                    }.Run();
                    worldBlackboardEntity.SetCollectionComponentAndDisposeOld(copyTransformToEntityMapping);
                }
                foreach (var entity in entities)
                {
                    var index = copyTransformToEntityMapping.transformAccessArray.length;
                    copyTransformToEntityMapping.transformAccessArray.Add(latiosWorldUnmanaged.GetManagedStructComponent<GameObjectEntity>(entity).gameObjectTransform);
                    copyTransformToEntityMapping.indexToEntityMap.Add(index, entity);
                    copyTransformToEntityMapping.entityToIndexMap.Add(entity, index);
                }
                EntityManager.AddComponent<CopyTransformToEntityCleanupTag>(m_newTransformToEntityQuery);
            }

            if (!m_deadTransformFromEntityQuery.IsEmptyIgnoreFilter)
            {
                var entities = m_deadTransformFromEntityQuery.ToEntityArray(Allocator.Temp);
                foreach (var entity in entities)
                {
                    var index = copyTransformFromEntityMapping.entityToIndexMap[entity];
                    copyTransformFromEntityMapping.transformAccessArray.RemoveAtSwapBack(index);
                    var movedEntity =
                        copyTransformFromEntityMapping.indexToEntityMap[copyTransformFromEntityMapping.transformAccessArray.length];
                    copyTransformFromEntityMapping.indexToEntityMap[index]       = movedEntity;
                    copyTransformFromEntityMapping.entityToIndexMap[movedEntity] = index;
                    copyTransformFromEntityMapping.entityToIndexMap.Remove(entity);
                    copyTransformFromEntityMapping.indexToEntityMap.Remove(copyTransformFromEntityMapping.transformAccessArray.length);
                }
                EntityManager.RemoveComponent<CopyTransformFromEntityCleanupTag>(m_deadTransformFromEntityQuery);
            }

            if (!m_newTransformFromEntityQuery.IsEmptyIgnoreFilter)
            {
                var entities = m_newTransformFromEntityQuery.ToEntityArray(Allocator.Temp);
                if (entities.Length > copyTransformFromEntityMapping.transformAccessArray.capacity - copyTransformFromEntityMapping.transformAccessArray.length)
                {
                    // Need to reallocate
                    var newCapacity                = math.ceilpow2(2 * (entities.Length + copyTransformFromEntityMapping.transformAccessArray.length));
                    var oldMapping                 = copyTransformFromEntityMapping;
                    copyTransformFromEntityMapping = new CopyTransformFromEntityMapping
                    {
                        entityToIndexMap     = new NativeHashMap<Entity, int>(newCapacity, Allocator.Persistent),
                        indexToEntityMap     = new NativeHashMap<int, Entity>(newCapacity, Allocator.Persistent),
                        transformAccessArray = new UnityEngine.Jobs.TransformAccessArray(newCapacity),
                    };

                    for (int i = 0; i < oldMapping.transformAccessArray.length; i++)
                        copyTransformFromEntityMapping.transformAccessArray.Add(oldMapping.transformAccessArray[i]);

                    new CopyMapsJob
                    {
                        srcEntityToIntMap = oldMapping.entityToIndexMap,
                        srcIntToEntityMap = oldMapping.indexToEntityMap,
                        dstEntityToIntMap = copyTransformFromEntityMapping.entityToIndexMap,
                        dstIntToEntityMap = copyTransformFromEntityMapping.indexToEntityMap
                    }.Run();
                    worldBlackboardEntity.SetCollectionComponentAndDisposeOld(copyTransformFromEntityMapping);
                }
                foreach (var entity in entities)
                {
                    var index = copyTransformFromEntityMapping.transformAccessArray.length;
                    copyTransformFromEntityMapping.transformAccessArray.Add(latiosWorldUnmanaged.GetManagedStructComponent<GameObjectEntity>(entity).gameObjectTransform);
                    copyTransformFromEntityMapping.indexToEntityMap.Add(index, entity);
                    copyTransformFromEntityMapping.entityToIndexMap.Add(entity, index);
                }
                EntityManager.AddComponent<CopyTransformFromEntityCleanupTag>(m_newTransformFromEntityQuery);
            }
        }

        struct Match
        {
            public Entity host;
            public Entity client;
        }

        [BurstCompile]
        struct MatchJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>                     hostChunks;
            [ReadOnly] public NativeArray<ArchetypeChunk>                     clientChunks;
            [ReadOnly] public EntityTypeHandle                                entityHandle;
            [ReadOnly] public ComponentTypeHandle<GameObjectEntityHost>       hostHandle;
            [ReadOnly] public ComponentTypeHandle<GameObjectEntityBindClient> clientHandle;

            public NativeList<Match> matches;

            public int clientEntityCount;

            public void Execute()
            {
                var clientByGuid = new NativeHashMap<Unity.Entities.Hash128, Entity>(clientEntityCount, Allocator.Temp);
                foreach (var chunk in clientChunks)
                {
                    var entities = chunk.GetNativeArray(entityHandle);
                    var guids    = chunk.GetNativeArray(ref clientHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (!clientByGuid.TryAdd(guids[i].guid, entities[i]))
                            UnityEngine.Debug.LogError(
                                $"Multiple GameObjectEntities target the same GameObjectEntityHost. This can result in unpredictable behavior. The entities are {entities[i].ToFixedString()} and {clientByGuid[guids[i].guid].ToFixedString()}");
                    }
                }

                foreach (var chunk in hostChunks)
                {
                    var entities = chunk.GetNativeArray(entityHandle);
                    var guids    = chunk.GetNativeArray(ref hostHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (clientByGuid.TryGetValue(guids[i].guid, out var clientEntity))
                        {
                            matches.Add(new Match { client = clientEntity, host = entities[i] });
                            clientByGuid.Remove(guids[i].guid);
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct CopyMapsJob : IJob
        {
            public NativeHashMap<int, Entity> srcIntToEntityMap;
            public NativeHashMap<Entity, int> srcEntityToIntMap;
            public NativeHashMap<int, Entity> dstIntToEntityMap;
            public NativeHashMap<Entity, int> dstEntityToIntMap;

            public void Execute()
            {
                foreach (var pair in srcIntToEntityMap)
                    dstIntToEntityMap.Add(pair.Key, pair.Value);

                foreach (var pair in srcEntityToIntMap)
                    dstEntityToIntMap.Add(pair.Key, pair.Value);
            }
        }
    }
}

