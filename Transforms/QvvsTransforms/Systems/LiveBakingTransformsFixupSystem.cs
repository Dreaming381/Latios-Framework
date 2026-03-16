#if !LATIOS_TRANSFORMS_UNITY
using Latios.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(AfterLiveBakingSuperSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LiveBakingTransformsFixupSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_rootsQuery;
        EntityQuery          m_childrenQuery;
        EntityQuery          m_worldTransformsQuery;
        EntityQuery          m_tickedTransformsQuery;
        EntityQuery          m_liveBakedQuery;

        bool m_editorWorldCorrupted;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld            = state.GetLatiosWorldUnmanaged();
            m_rootsQuery           = state.Fluent().With<LiveBakedTag, EntityInHierarchy>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_childrenQuery        = state.Fluent().With<LiveBakedTag, RootReference>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_worldTransformsQuery =
                state.Fluent().With<LiveBakedTag, WorldTransform>(true).WithAnyEnabled<EntityInHierarchy, RootReference>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_tickedTransformsQuery =
                state.Fluent().With<LiveBakedTag, TickedWorldTransform>(true).WithAnyEnabled<EntityInHierarchy,
                                                                                             RootReference>(true).IncludePrefabs().IncludeDisabledEntities().Build();
            m_liveBakedQuery = state.Fluent().With<LiveBakedTag>(true).IncludePrefabs().IncludeDisabledEntities().Build();

            m_editorWorldCorrupted = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var rootsOrderVersion                = state.EntityManager.GetComponentOrderVersion<EntityInHierarchy>();
            var childrenOrderVersion             = state.EntityManager.GetComponentOrderVersion<RootReference>();
            var worldTransformOrderVersion       = state.EntityManager.GetComponentOrderVersion<WorldTransform>();
            var tickedWorldTransformOrderVersion = state.EntityManager.GetComponentOrderVersion<TickedWorldTransform>();

            var capture = latiosWorld.worldBlackboardEntity.GetCollectionComponent<LiveTransformCapture>(false);

            // Uses WorldUpdateAllocator so Dispose doesn't do anything and does not invalidate our current capture.
            latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new LiveTransformCapture
            {
                changeVersion                    = state.GlobalSystemVersion,
                rootsOrderVersion                = rootsOrderVersion,
                childrenOrderVersion             = childrenOrderVersion,
                worldTransformOrderVersion       = worldTransformOrderVersion,
                tickedWorldTransformOrderVersion = tickedWorldTransformOrderVersion,
            });

            // If all subscenes are closed, reset the corruption.
            if (m_editorWorldCorrupted && m_liveBakedQuery.IsEmptyIgnoreFilter)
                m_editorWorldCorrupted = false;

            m_editorWorldCorrupted |= !capture.cleanEditorWorld;
            if (!m_editorWorldCorrupted)
                return;

            bool rootsChangedStructurally            = rootsOrderVersion != capture.rootsOrderVersion;
            bool childrenChangedStructurally         = childrenOrderVersion != capture.childrenOrderVersion;
            bool worldTransformsChangedStructurally  = worldTransformOrderVersion != capture.worldTransformOrderVersion;
            bool tickedTransformsChangedStructurally = tickedWorldTransformOrderVersion != capture.tickedWorldTransformOrderVersion;

            m_rootsQuery.SetChangedVersionFilter(ComponentType.ReadOnly<EntityInHierarchy>());
            m_childrenQuery.SetChangedVersionFilter(ComponentType.ReadOnly<RootReference>());
            m_worldTransformsQuery.SetChangedVersionFilter(ComponentType.ReadOnly<WorldTransform>());
            m_tickedTransformsQuery.SetChangedVersionFilter(ComponentType.ReadOnly<TickedWorldTransform>());

            m_rootsQuery.SetOverrideChangeFilterVersion(capture.changeVersion);
            m_childrenQuery.SetOverrideChangeFilterVersion(capture.changeVersion);
            m_worldTransformsQuery.SetOverrideChangeFilterVersion(capture.changeVersion);
            m_tickedTransformsQuery.SetOverrideChangeFilterVersion(capture.changeVersion);
            bool hierarchyBuffersChanged = !m_rootsQuery.IsEmpty;
            bool rootReferencesChanged   = !m_childrenQuery.IsEmpty;
            bool worldTransformsChanged  = !m_worldTransformsQuery.IsEmpty;
            bool tickedTransformsChanged = !m_tickedTransformsQuery.IsEmpty;

            if (!rootsChangedStructurally && !childrenChangedStructurally && !worldTransformsChangedStructurally && !tickedTransformsChangedStructurally)
            {
                if (!rootReferencesChanged)
                {
                    if (!hierarchyBuffersChanged)
                    {
                        if (!worldTransformsChanged && !tickedTransformsChanged)
                        {
                            // Nothing changed during baking. We are safe.
                            return;
                        }

                        // Only WorldTransforms changed, and not any LocalTransforms. This means either a root was moved, or a former root
                        // that was dynamically parented had its authoring transform changed.
                        //
                        // All non-root transforms should be reset to their old values. And then any changed root should cause propagation.
                        // In the editor world, propagation ignored all inheritance flags except CopyParent.
                        //
                        // Todo:
                    }
                }
            }
        }
    }
}
#endif

