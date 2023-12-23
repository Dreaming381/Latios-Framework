using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.Exposed.Dangerous;

namespace Latios.Systems
{
    /// <summary>
    /// A group that runs after blackboard entities, collection components, and managed struct components are processed.
    /// Add systems to this group that perform structural changes to avoid synchronizing jobs, as jobs are usually synchronized at this point.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(Unity.Scenes.SceneSystemGroup))]
    public partial class LatiosWorldSyncGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// A group that runs before syncPoint and the following structural-change systems.
    /// Systems that have heavy jobs which do not depend on ECS structures can be scheduled here.
    /// The jobs may run alongside the sync point, utilizing worker threads which would normally be idle.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(BeginInitializationEntityCommandBufferSystem))]
    [UpdateBefore(typeof(SyncPointPlaybackSystemDispatch))]
    public partial class PreSyncPointGroup : ComponentSystemGroup
    {
    }

    // This used to be a subclass of InitializationSystemGroup, but due to a bug with subclassing and system sorting,
    // it was altered to be an IRateManager instead.
    internal class LatiosInitializationSystemGroupManager : IRateManager
    {
        LatiosWorld m_latiosWorld;

        public LatiosInitializationSystemGroupManager(LatiosWorld world, InitializationSystemGroup initializationGroup)
        {
            m_latiosWorld             = world;
            var autoDestroyExpired    = world.CreateSystem<AutoDestroyExpirablesSystem>();
            var syncPlayback          = world.CreateSystemManaged<SyncPointPlaybackSystemDispatch>();
            var mergeGlobals          = world.CreateSystemManaged<MergeBlackboardsSystem>();
            var collectionReactive    = world.CreateSystem<CollectionComponentsReactiveSystem>();
            var managedStructReactive = world.CreateSystem<ManagedStructComponentsReactiveSystem>();
            var syncGroup             = world.GetOrCreateSystemManaged<LatiosWorldSyncGroup>();
            var preSyncGroup          = world.GetOrCreateSystemManaged<PreSyncPointGroup>();
            initializationGroup.AddSystemToUpdateList(autoDestroyExpired);
            initializationGroup.AddSystemToUpdateList(syncPlayback);
            initializationGroup.AddSystemToUpdateList(syncGroup);
            initializationGroup.AddSystemToUpdateList(preSyncGroup);
            syncGroup.AddSystemToUpdateList(mergeGlobals);
            syncGroup.AddSystemToUpdateList(collectionReactive);
            syncGroup.AddSystemToUpdateList(managedStructReactive);
        }

        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME
            group.ClearSystemIds();
#endif

            m_latiosWorld.FrameStart();
            SuperSystem.DoLatiosFrameworkComponentSystemGroupUpdate(group);
            return false;
        }

        public float Timestep { get; set; }
    }
}

