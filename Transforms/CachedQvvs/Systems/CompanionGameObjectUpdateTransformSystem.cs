#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
#if !UNITY_DISABLE_MANAGED_COMPONENTS
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine.Jobs;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CompanionGameObjectUpdateTransformSystem : ISystem
    {
        static readonly ProfilerMarker s_profilerMarkerAddNew = new("AddNew");
        static readonly ProfilerMarker s_profilerMarkerRemove = new("Remove");
        static readonly ProfilerMarker s_profilerMarkerUpdate = new("Update");

        struct CompanionGameObjectUpdateTransformCleanup : ICleanupComponentData
        {
        }

        struct IndexAndInstance
        {
            public int transformAccessArrayIndex;
            public int instanceID;
        }

        TransformAccessArray                    m_transformAccessArray;
        NativeList<Entity>                      m_entities;
        NativeHashMap<Entity, IndexAndInstance> m_entitiesMap;

        EntityQuery m_createdQuery;
        EntityQuery m_destroyedQuery;
        EntityQuery m_modifiedQuery;
        EntityQuery m_existQuery;

        public void OnCreate(ref SystemState state)
        {
            m_transformAccessArray = new TransformAccessArray(0);
            m_entities             = new NativeList<Entity>(64, Allocator.Persistent);
            m_entitiesMap          = new NativeHashMap<Entity, IndexAndInstance>(64, Allocator.Persistent);
            m_createdQuery         = state.GetEntityQuery(
                new EntityQueryDesc
            {
                All  = new[] { ComponentType.ReadOnly<CompanionLink>() },
                None = new[] { ComponentType.ReadOnly<CompanionGameObjectUpdateTransformCleanup>() }
            }
                );
            m_destroyedQuery = state.GetEntityQuery(
                new EntityQueryDesc
            {
                All  = new[] { ComponentType.ReadOnly<CompanionGameObjectUpdateTransformCleanup>() },
                None = new[] { ComponentType.ReadOnly<CompanionLink>() }
            }
                );
            m_modifiedQuery = state.GetEntityQuery(
                new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CompanionLink>() },
            }
                );
            m_modifiedQuery.SetChangedVersionFilter(typeof(CompanionLink));
            m_existQuery = state.GetEntityQuery(ComponentType.ReadOnly<CompanionLink>(), ComponentType.ReadOnly<WorldTransform>());
        }

        public void OnDestroy(ref SystemState state)
        {
            m_transformAccessArray.Dispose();
            m_entities.Dispose();
            m_entitiesMap.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            using (s_profilerMarkerAddNew.Auto())
            {
                if (!m_createdQuery.IsEmpty)
                {
                    var entities = m_createdQuery.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        var link   = state.EntityManager.GetComponentData<CompanionLink>(entity);

                        // It is possible that an object is created and immediately destroyed, and then this shouldn't run.
                        if (link.Companion != null)
                        {
                            IndexAndInstance indexAndInstance          = default;
                            indexAndInstance.transformAccessArrayIndex = m_entities.Length;
                            indexAndInstance.instanceID                = link.Companion.GetInstanceID();
                            m_entitiesMap.Add(entity, indexAndInstance);
                            m_transformAccessArray.Add(link.Companion.transform);
                            m_entities.Add(entity);
                        }
                    }

                    state.EntityManager.AddComponent<CompanionGameObjectUpdateTransformCleanup>(m_createdQuery);
                }
            }

            using (s_profilerMarkerRemove.Auto())
            {
                if (!m_destroyedQuery.IsEmpty)
                {
                    var args = new RemoveDestroyedEntitiesArgs
                    {
                        Entities             = m_entities,
                        DestroyedQuery       = m_destroyedQuery,
                        EntitiesMap          = m_entitiesMap,
                        EntityManager        = state.EntityManager,
                        TransformAccessArray = m_transformAccessArray
                    };
                    RemoveDestroyedEntities(ref args);
                }
            }

            using (s_profilerMarkerUpdate.Auto())
            {
                foreach (var (link, entity) in Query<CompanionLink>().WithChangeFilter<CompanionLink>().WithEntityAccess())
                {
                    var cached    = m_entitiesMap[entity];
                    var currentID = link.Companion.GetInstanceID();
                    if (cached.instanceID != currentID)
                    {
                        // We avoid the need to update the indices and reorder the entities array by adding
                        // the new transform first, and removing the old one after with a RemoveAtSwapBack.
                        // Example, replacing B with X in ABCD:
                        // 1. ABCD + X = ABCDX
                        // 2. ABCDX - B = AXCD
                        // -> the transform is updated, but the index remains unchanged
                        m_transformAccessArray.Add(link.Companion.transform);
                        m_transformAccessArray.RemoveAtSwapBack(cached.transformAccessArrayIndex);
                        cached.instanceID     = currentID;
                        m_entitiesMap[entity] = cached;
                    }
                }
            }

            state.Dependency = new CopyTransformJob
            {
                worldTransformLookup = GetComponentLookup<WorldTransform>(),
                entities             = m_entities
            }.Schedule(m_transformAccessArray, state.Dependency);
        }

        struct RemoveDestroyedEntitiesArgs
        {
            public EntityQuery                             DestroyedQuery;
            public NativeList<Entity>                      Entities;
            public NativeHashMap<Entity, IndexAndInstance> EntitiesMap;
            public TransformAccessArray                    TransformAccessArray;
            public EntityManager                           EntityManager;
        }

        [BurstCompile]
        static void RemoveDestroyedEntities(ref RemoveDestroyedEntitiesArgs args)
        {
            var entities = args.DestroyedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                // This check is necessary because the code for adding entities is conditional and in edge-cases where
                // objects are quickly created-and-destroyed, we might not have the entity in the map.
                if (args.EntitiesMap.TryGetValue(entity, out var indexAndInstance))
                {
                    var index = indexAndInstance.transformAccessArrayIndex;
                    args.TransformAccessArray.RemoveAtSwapBack(index);
                    args.Entities.RemoveAtSwapBack(index);
                    args.EntitiesMap.Remove(entity);
                    if (index < args.Entities.Length)
                    {
                        var fixup                              = args.EntitiesMap[args.Entities[index]];
                        fixup.transformAccessArrayIndex        = index;
                        args.EntitiesMap[args.Entities[index]] = fixup;
                    }
                }
            }
            entities.Dispose();
            args.EntityManager.RemoveComponent<CompanionGameObjectUpdateTransformCleanup>(args.DestroyedQuery);
        }

        [BurstCompile]
        struct CopyTransformJob : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<WorldTransform> worldTransformLookup;
            [ReadOnly] public NativeList<Entity>                                         entities;

            public unsafe void Execute(int index, TransformAccess transform)
            {
                var wt                  = worldTransformLookup[entities[index]];
                transform.localPosition = wt.position;
                transform.localRotation = wt.rotation;
                transform.localScale    = wt.nonUniformScale;
            }
        }
    }
}
#endif
#endif

