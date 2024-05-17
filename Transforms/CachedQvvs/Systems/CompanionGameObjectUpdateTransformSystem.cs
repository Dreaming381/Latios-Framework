#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
#if !UNITY_DISABLE_MANAGED_COMPONENTS
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
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
        static readonly string         s_ProfilerMarkerAddNewString = "AddNew";
        static readonly string         s_ProfilerMarkerRemoveString = "Remove";
        static readonly string         s_ProfilerMarkerUpdateString = "Update";
        static readonly ProfilerMarker s_ProfilerMarkerAddNew       = new(s_ProfilerMarkerAddNewString);
        static readonly ProfilerMarker s_ProfilerMarkerRemove       = new(s_ProfilerMarkerRemoveString);
        static readonly ProfilerMarker s_ProfilerMarkerUpdate       = new(s_ProfilerMarkerUpdateString);

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
        EntityQuery m_existQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_transformAccessArray = new TransformAccessArray(0);
            m_entities             = new NativeList<Entity>(64, Allocator.Persistent);
            m_entitiesMap          = new NativeHashMap<Entity, IndexAndInstance>(64, Allocator.Persistent);
            m_createdQuery         = state.Fluent().With<CompanionLink, CompanionLinkTransform, WorldTransform>(true).Without<CompanionGameObjectUpdateTransformCleanup>().Build();
            m_destroyedQuery       = state.Fluent().With<CompanionGameObjectUpdateTransformCleanup>(true).Without<CompanionLink>().Build();
            m_existQuery           = state.Fluent().With<CompanionLink, CompanionLinkTransform, WorldTransform>(true).Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            m_transformAccessArray.Dispose();
            m_entities.Dispose();
            m_entitiesMap.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            using (s_ProfilerMarkerAddNew.Auto())
            {
                if (!m_createdQuery.IsEmpty)
                {
                    var entities = m_createdQuery.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        var link   = state.EntityManager.GetComponentData<CompanionLinkTransform>(entity);

                        // It is possible that an object is created and immediately destroyed, and then this shouldn't run.
                        if (link.CompanionTransform.IsValid())
                        {
                            IndexAndInstance indexAndInstance          = default;
                            indexAndInstance.transformAccessArrayIndex = m_entities.Length;
                            indexAndInstance.instanceID                = link.CompanionTransform.InstanceID();
                            m_entitiesMap.Add(entity, indexAndInstance);
                            m_transformAccessArray.Add(link.CompanionTransform.InstanceID());
                            m_entities.Add(entity);
                        }
                    }

                    state.EntityManager.AddComponent<CompanionGameObjectUpdateTransformCleanup>(m_createdQuery);
                }
            }

            using (s_ProfilerMarkerRemove.Auto())
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

            using (s_ProfilerMarkerUpdate.Auto())
            {
                foreach (var (link, entity) in Query<CompanionLinkTransform>().WithChangeFilter<CompanionLink>().WithEntityAccess())
                {
                    var cached    = m_entitiesMap[entity];
                    var currentID = link.CompanionTransform.InstanceID();
                    if (cached.instanceID != currentID)
                    {
                        // We avoid the need to update the indices and reorder the entities array by adding
                        // the new transform first, and removing the old one after with a RemoveAtSwapBack.
                        // Example, replacing B with X in ABCD:
                        // 1. ABCD + X = ABCDX
                        // 2. ABCDX - B = AXCD
                        // -> the transform is updated, but the index remains unchanged
                        m_transformAccessArray.Add(link.CompanionTransform.InstanceID());
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

