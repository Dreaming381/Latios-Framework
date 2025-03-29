// This system is based on tertle's Entities fork which allows for disabling subscene reimport during play mode.
#if UNITY_EDITOR && !LATIOS_BL_FORK

using System;
using System.Diagnostics;
using Unity.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Unity.Scenes
{
    static class SubscenePlayModeEditorOptions
    {
        static string editorPrefName = "LatiosDisableSubsceneReimportDuringPlaymode";
        internal static bool disableSubsceneReimportDuringPlaymode
        {
            get => UnityEditor.EditorPrefs.GetBool(editorPrefName);
            private set => UnityEditor.EditorPrefs.SetBool(editorPrefName, value);
        }

        const string menuPath = "Edit/Latios/Disable Subscene Reimport During Playmode";

        [UnityEditor.InitializeOnLoadMethod]
        public static void InitToggle() => UnityEditor.Menu.SetChecked(menuPath, disableSubsceneReimportDuringPlaymode);

        [UnityEditor.MenuItem(menuPath)]
        public static void ToggleDriver()
        {
            var currentState = disableSubsceneReimportDuringPlaymode;
            currentState                          = !currentState;
            disableSubsceneReimportDuringPlaymode = currentState;
            UnityEditor.Menu.SetChecked(menuPath, currentState);
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.Streaming)]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(WeakAssetReferenceLoadingSystem))]
    [UpdateAfter(typeof(SceneSystem))]
    [UpdateBefore(typeof(ResolveSceneReferenceSystem))]
    partial class LatiosEditorResolveSceneReferenceSystem : SystemBase
    {
        struct AssetDependencyTrackerState : ICleanupComponentData
        {
            public UnityEditor.GUID SceneAndBuildConfigGUID;
        }

        EntityQuery m_AddScenes;
        EntityQuery m_RemoveScenesWithoutSceneReference;
        EntityQuery m_RemoveScenesWithDisableSceneResolveAndLoad;
        EntityQuery m_RemoveScenesWithDisabled;
        EntityQuery m_ValidSceneQuery;
        EntityQueryMask m_ValidSceneMask;
        ResolveSceneSectionArchetypes m_ResolveSceneSectionArchetypes;

        AssetDependencyTracker<Entity> _AssetDependencyTracker;
        NativeList<AssetDependencyTracker<Entity>.Completed> _Changed;

        SystemHandle _sceneSystem;

        SceneHeaderUtility _SceneHeaderUtility;

        [Conditional("LOG_RESOLVING")]
        void LogResolving(string type, Hash128 sceneGUID)
        {
            Debug.Log(type + ": " + sceneGUID);
        }

        [Conditional("LOG_RESOLVING")]
        void LogResolving(string log)
        {
            Debug.Log(log);
        }

        protected override unsafe void OnUpdate()
        {
            SceneWithBuildConfigurationGUIDs.ValidateBuildSettingsCache();

            var buildConfigurationGUID = EntityManager.GetComponentData<SceneSystemData>(_sceneSystem).BuildConfigurationGUID;

            // Add scene entities that haven't been encountered yet
            if (!m_AddScenes.IsEmptyIgnoreFilter)
            {
                //@TODO: Should use Entities.ForEach but we are missing Entities.ForEach support for explicit queries

                using (var addScenes = m_AddScenes.ToEntityArray(Allocator.TempJob))
                {
                    var trackerStates = new NativeArray<AssetDependencyTrackerState>(addScenes.Length, Allocator.Temp);
                    for (int i = 0; i != addScenes.Length; i++)
                    {
                        var sceneEntity        = addScenes[i];
                        var scene              = EntityManager.GetComponentData<SceneReference>(sceneEntity);
                        var requestSceneLoaded = EntityManager.GetComponentData<RequestSceneLoaded>(sceneEntity);

                        var guid = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(scene.SceneGUID,
                                                                                    buildConfigurationGUID, true, out var requireRefresh);
                        var async = (requestSceneLoaded.LoadFlags & SceneLoadFlags.BlockOnImport) == 0;

                        LogResolving(async ? "Adding Async" : "Adding Sync", guid);

                        _AssetDependencyTracker.Add(guid, sceneEntity, async);
                        if (requireRefresh)
                            _AssetDependencyTracker.RequestRefresh();

                        trackerStates[i] = new AssetDependencyTrackerState { SceneAndBuildConfigGUID = guid };
                    }

                    EntityManager.AddComponentData(m_AddScenes, trackerStates);
                    trackerStates.Dispose();
                }
            }

            // Remove scene entities that were added and should no longer be tracked
            RemoveSceneEntities(m_RemoveScenesWithoutSceneReference);
            RemoveSceneEntities(m_RemoveScenesWithDisableSceneResolveAndLoad);
            RemoveSceneEntities(m_RemoveScenesWithDisabled);

            // Process any scenes that have completed their asset import
            var isDone = _AssetDependencyTracker.GetCompleted(_Changed);
            foreach (var change in _Changed)
            {
                var sceneEntity = change.UserKey;
                LogResolving($"Resolving: {change.Asset} -> {change.ArtifactID}");

                // This happens when instantiating an already fully resolved scene entity,
                // AssetDependencyTrackerState will be added and results in a completed change request,
                // but since this scene is already fully resolved with the same hash we can simply skip it.
                if (SystemAPI.HasComponent<ResolvedSceneHash>(sceneEntity) &&
                    SystemAPI.GetComponent<ResolvedSceneHash>(sceneEntity).ArtifactHash == (Hash128)change.ArtifactID)
                {
                    if (!SystemAPI.HasBuffer<ResolvedSectionEntity>(sceneEntity))
                        throw new InvalidOperationException(
                            $"Entity {sceneEntity} used for a scene load has a {nameof(ResolvedSceneHash)} component but no {nameof(ResolvedSectionEntity)} buffer. " +
                            "This suggests that you copied a scene entity after loading it, but before its scene data had been fully resolved. " +
                            $"Please only copy it after resolving has finished, which will add {nameof(ResolvedSectionEntity)} to the entity.");
                    continue;
                }

                if (!m_ValidSceneMask.MatchesIgnoreFilter(sceneEntity))
                    throw new InvalidOperationException("entity should have been removed from tracker already");

                if (UnityEditor.EditorApplication.isPlaying && SubscenePlayModeEditorOptions.disableSubsceneReimportDuringPlaymode &&
                    SceneSystem.IsSceneLoaded(World.Unmanaged, sceneEntity))
                    continue;

                // Unload any previous state
                SceneSystem.UnloadSceneSectionMetaEntitiesOnly(World.Unmanaged, sceneEntity, false);

                // Resolve new state
                var scene   = EntityManager.GetComponentData<SceneReference>(change.UserKey);
                var request = EntityManager.GetComponentData<RequestSceneLoaded>(change.UserKey);

                SceneWithBuildConfigurationGUIDs.EnsureExistsFor(scene.SceneGUID,
                                                                 buildConfigurationGUID, true, out var requireRefresh);

                if (change.ArtifactID != default)
                {
                    LogResolving($"Schedule header load: {change.ArtifactID}");
                    SceneHeaderUtility.ScheduleHeaderLoadOnEntity(EntityManager, change.UserKey, scene.SceneGUID,
                                                                  request, change.ArtifactID, SceneSystem.SceneLoadDir);
                }
                else
                    Debug.LogError(
                        $"Failed to import entity scene because the automatically generated SceneAndBuildConfigGUID asset was not present: '{AssetDatabaseCompatibility.GuidToPath(scene.SceneGUID)}' -> '{AssetDatabaseCompatibility.GuidToPath(change.Asset)}'");
            }

            _SceneHeaderUtility.CleanupHeaders(EntityManager);

            bool headerLoadInProgress = false;
            Entities.WithStructuralChanges().WithNone<DisableSceneResolveAndLoad, ResolvedSectionEntity>().ForEach(
                (Entity sceneEntity, ref RequestSceneHeader requestHeader, ref SceneReference scene,
                 ref ResolvedSceneHash resolvedSceneHash, ref RequestSceneLoaded requestSceneLoaded) =>
            {
                if (!requestHeader.IsCompleted)
                {
                    if ((requestSceneLoaded.LoadFlags & SceneLoadFlags.BlockOnImport) == 0)
                    {
                        headerLoadInProgress = true;
                        return;
                    }
                    requestHeader.Complete();
                }

                var headerLoadResult = SceneHeaderUtility.FinishHeaderLoad(requestHeader, scene.SceneGUID, SceneSystem.SceneLoadDir);
                LogResolving($"Finished header load: {scene.SceneGUID}");
                if (!headerLoadResult.Success)
                {
                    requestHeader.Dispose();
                    EntityManager.AddBuffer<ResolvedSectionEntity>(sceneEntity);
                    EntityManager.RemoveComponent<RequestSceneHeader>(sceneEntity);
                    return;
                }

                ResolveSceneSectionUtility.ResolveSceneSections(EntityManager, sceneEntity, requestSceneLoaded, ref headerLoadResult.SceneMetaData.Value,
                                                                m_ResolveSceneSectionArchetypes, headerLoadResult.SectionPaths, headerLoadResult.HeaderBlobOwner);
                requestHeader.Dispose();
                EntityManager.RemoveComponent<RequestSceneHeader>(sceneEntity);
#if UNITY_EDITOR
                if (EntityManager.HasComponent<SubScene>(sceneEntity))
                {
                    var subScene = EntityManager.GetComponentObject<SubScene>(sceneEntity);
                    // Add SubScene component to section entities
                    using (var sectionEntities = EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity).ToNativeArray(Allocator.Temp))
                    {
                        for (int iSection = 0; iSection < sectionEntities.Length; ++iSection)
                            EntityManager.AddComponentObject(sectionEntities[iSection].SectionEntity, subScene);
                    }
                }
#endif
            }).Run();

            if (headerLoadInProgress)
                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();

            if (!isDone)
                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        private void RemoveSceneEntities(EntityQuery query)
        {
            if (!query.IsEmptyIgnoreFilter)
            {
                using (var removeEntities = query.ToEntityArray(Allocator.TempJob))
                    using (var removeGuids =
                               query.ToComponentDataArray<AssetDependencyTrackerState>(Allocator.TempJob))
                    {
                        for (int i = 0; i != removeEntities.Length; i++)
                        {
                            LogResolving("Removing", removeGuids[i].SceneAndBuildConfigGUID);
                            _AssetDependencyTracker.Remove(removeGuids[i].SceneAndBuildConfigGUID, removeEntities[i]);
                        }
                    }

                EntityManager.RemoveComponent<AssetDependencyTrackerState>(query);
            }
        }

        protected override void OnCreate()
        {
            World.GetExistingSystemManaged<ResolveSceneReferenceSystem>().Enabled = false;
            _sceneSystem                                                          = World.GetExistingSystem<SceneSystem>();
            _AssetDependencyTracker                                               =
                new AssetDependencyTracker<Entity>(EntityScenesPaths.SubSceneImporterType, "Import EntityScene");
            _Changed            = new NativeList<AssetDependencyTracker<Entity>.Completed>(32, Allocator.Persistent);
            _SceneHeaderUtility = new SceneHeaderUtility(this);
            m_ValidSceneQuery   = new EntityQueryBuilder(Allocator.Temp)
                                  .WithAll<SceneReference, RequestSceneLoaded, AssetDependencyTrackerState>()
                                  .WithNone<DisableSceneResolveAndLoad>()
                                  .Build(this);
            Assert.IsFalse(m_ValidSceneQuery.HasFilter(), "The use of EntityQueryMask in this system will not respect the query's active filter settings.");
            m_ValidSceneMask = m_ValidSceneQuery.GetEntityQueryMask();

            m_AddScenes = new EntityQueryBuilder(Allocator.Temp)
                          .WithAll<SceneReference, RequestSceneLoaded>()
                          .WithNone<DisableSceneResolveAndLoad, AssetDependencyTrackerState>()
                          .Build(this);
            m_RemoveScenesWithoutSceneReference = new EntityQueryBuilder(Allocator.Temp)
                                                  .WithAll<AssetDependencyTrackerState>().WithNone<SceneReference, RequestSceneLoaded>().Build(this);
            m_RemoveScenesWithDisableSceneResolveAndLoad = new EntityQueryBuilder(Allocator.Temp)
                                                           .WithAll<AssetDependencyTrackerState, DisableSceneResolveAndLoad>().Build(this);
            m_RemoveScenesWithDisabled = new EntityQueryBuilder(Allocator.Temp)
                                         .WithAll<AssetDependencyTrackerState, Disabled>().Build(this);

            m_ResolveSceneSectionArchetypes = ResolveSceneSectionUtility.CreateResolveSceneSectionArchetypes(EntityManager);
        }

        protected override void OnDestroy()
        {
            _AssetDependencyTracker.Dispose();
            _Changed.Dispose();
            _SceneHeaderUtility.Dispose(EntityManager);
        }
    }
}

#endif

