# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic
Versioning](http://semver.org/spec/v2.0.0.html).

## [0.4.0] – 2021-8-9

Officially supports Entities [0.17.0]

### Added

-   Added argument `isInitialized` to
    `BlackboardEntity.AddCollectionComponent()`
-   *New Feature:* Added `OnNewScene()` callback to `SubSystem` and
    `SuperSystem`, which can be used to initialize components on the
    `sceneBlackboardEntity`.
-   *New Feature:* Added `EntityWith<T>` and `EntityWithBuffer<T>` for strongly
    typed entity references and left-to-right dereferencing chains.
-   Added `LatiosMath.ComplexMul()`.
-   *New Feature:* Added `Rng` and `RngToolkit` which provide a new framework
    for fast, deterministic, parallel random number generation.

### Improved

-   Added safety checks for improper usage of `sceneBlackboardEntity` that would
    only cause issues in builds.

## [0.3.1] – 2021-3-7

Officially supports Entities [0.17.0]

### Fixed

-   Fixed several compiler errors when creating builds
-   Removed dangling references to the Code Analysis package in
    Latios.Core.Editor.asmdef

## [0.3.0] – 2021-3-4

Officially supports Entities [0.17.0]

### Added

-   **Breaking**: Added `LatiosWorld.useExplicitSystemOrdering`. You must now
    set this value to true in an `ICustomBootstrap` before injecting additional
    systems.
-   Added the following convenience properties to `LatiosWorld`:
    `initializationSystemGroup`, `simulationsSystemGroup`,
    `presentationSystemGroup `
-   *New Feature:* Added `GameObjectConversionConfigurationSystem` which when
    subclassed allows for customizing the conversion world before any other
    systems run. Not all entities have been gathered when `OnUpdate()` is
    called. Note that this mechanism is likely temporary and may change after a
    Unity bug is fixed.
-   *New Feature:* Added the following containers: `EnableCommandBuffer`,
    `DisableCommandBuffer`, `DestroyCommandBuffer`, `InstantiateCommandBuffer`,
    `EntityOperationCommandBuffer`
-   *New Feature:* Added `SyncPointPlaybackSystem` which can play back
    `EntityCommandBuffers` as well as the new command buffer types
-   *New Feature:* Added `LatiosWorld.syncPoint` which provides fast access to
    the `SyncPointPlaybackSystem` and removes the need to call
    `AddJobHandleForProducer()` when invoked from a `SubSystem`
-   Added `BlackboardEntity.AddComponentDataIfMissing<T>(T data)` which systems
    can use to default-initialize `worldBlackboardEntity` components in
    `OnCreate()` without overriding custom settings
-   Added `PreSyncPointGroup` for scheduling non-ECS jobs to execute on worker
    threads during the sync point
-   Added extension `BlobBuilder.ConstructFromNativeArray<T>()` which allows
    initializing a `BlobArray<T>` from a `NativeArray<T>`
-   Added extension `BlobBuilder.AllocateFixedString<T>()` which allows
    initializing a `BlobString` from a `FixedString###` or `HeapString` inside a
    Burst job
-   Added extension `NativeList<T>.AddRangeFromBlob()` which appends the
    `BlobArray` data to the list
-   Added extension `StringBuilder.Append()` which works with `FixedString###`
    hi
-   Added new assembly `Entities.Exposed` which uses the namespace
    `Unity.Entities.Exposed` and extends the API with the following:
    -   `EntityLocationInChunk` – a new struct type
    -   Extension method `EntityManager.GetEntityLocationInChunk()` used for
        advanced algorithms
    -   Extension method `World.ExecutingSystemType()` used for profiling
-   Added new assembly `MathematicsExpansion` which extends the
    `Unity.Mathematics.math` API with `select()` functions for `bool` types
-   `Added lots of new API for simdFloat3`

### `Changed`

-   **Breaking:** Global Entities have been renamed to Blackboard Entities. See
    upgrade guide for details.
-   `TransfomUniformScalePatchConversionSystem` was moved from
    `Latios.Authoring` to `Latios.Authoring.Systems`
-   **Breaking:** Renamed `LatiosSyncPointGroup` to `LatiosWorldSyncGroup`
-   **Breaking:** `LatiosInitializationSystemGroup`,
    `LatiosSimulationSystemGroup`, `LatiosPresentationSystemGroup`,
    `LatiosFixedSimulationSystemGroup`, `PreSyncPointGroup`, and
    `LatiosWorldSyncGroup` all use `SuperSystem` updating behavior.
    `SuperSystem` and `SubSystem` types added to one of these groups will now
    have `ShouldUpdateSystem()` invoked. Unfortunately, this also means that
    `ISystemBase` systems added to these groups will not function. `ISystemBase`
    is advised against in the short-term. But if you must have it, put it inside
    a custom `ComponentSystemGroup` instead.

### Removed

-   BurstPatcher has been removed.

### Improved

-   Systems created in `LatiosWorld` now use attribute-based ordering which may
    improve compatibility with more DOTS projects
-   All authoring components have now been categorized under *Latios-\>Core* in
    the *Add Component* menu
-   All authoring components now have public access so that custom scripts can
    reference them
-   **Breaking:** `BlackboardEntity` methods have been revised to more closely
    resemble `EntityManager` methods
-   Added an additional argument to `BootstrapTools.InjectUnitySystems()` which
    silences warnings and made this silencing behavior default
-   Setting `DestroyEntitiesOnSceneChangeSystem.Enabled` to `false` disables its
    functionality

## [0.2.2] - 2021-1-24

Officially supports Entities [0.17.0]

### Changed

-   BurstPatcher has been deprecated. If you still need the functionality, add
    `BURST_PATCHER` to the Scripting Define Symbols

### Fixed

-   Fixed a `NullReferenceException` in the *Bootstrap – Injection Workflow*
    template

## [0.2.1] - 2020-10-31

Officially supports Entities [0.16.0]

### Changed

-   Due to Entities changes, modifying `SuperSystem`s or
    `LatiosInitializationSystemGroup` after bootstrap is no longer supported

### Fixed

-   Fixed some deprecation warnings

## [0.2.0] - 2020-9-22

Officially supports Entities [0.14.0]

### Added

-   *New Feature*: Burst Patcher uses codegen at Build time to allow for
    compilation of generic jobs scheduled by generic methods even if the generic
    argument or the job is private
-   Added "Bootstrap - Explicit Workflow" creation tool
-   Added `BootstrapTools.InjectUnitySystems` which does exactly what it sounds
    like
-   Added `BootstrapTools.UpdatePlayerLoopWithDelayedSimulation` which runs
    `SimulationSystemGroup` after rendering
-   *New Feature*: Collection component dependency management now hooks into the
    `Dependency` field that lambda jobs use
-   *New Feature*: Fluent Queries provide a fluent extendible shorthand for
    generating `EntityQuery` instances
-   Added `LatiosSimulationSystemGroup` and `LatiosPresentationSystemGroup`
-   Added `IManagedComponent` and `ICollectionComponent` API to `ManagedEntity`
-   Added float2 variants for `SmoothStart` and `SmoothStop` in `LatiosMath`

### Changed

-   The original Bootstrap creation is now labeled "Bootstrap - Injection
    Workflow"
-   Renamed `IComponent` to `IManagedComponent`
-   `IManagedComponent` and `ICollectionComponent` behave differently due to
    DOTS evolution. See the documentation for how to use them.
-   `FixedString` is used instead of `NativeString`
-   `BootstrapTools.UpdatePlayerLoopWithFixedUpdate` is now
    `BootstrapTools.AddWorldToCurrentPlayerLoopWithFixedUpdate` with updated API
    to mirror `ScriptBehaviorUpdateOrder`
-   `EntityDataCopyKit` now ignores collection components by default
-   `SubSystem` now subclasses `SystemBase` and should be used for all logical
    systems
-   `LatiosInitializationSystemGroup` was reworked for compatibility with the
    latest Entities

### Removed

-   `JobSubSystem` - use `SubSystem` instead which now subclasses `SystemBase`

### Fixed

-   Fixed an issue with `EntityDataCopyKit` and dynamic buffers
-   `SuperSystem` and `RootSuperSystem` should now properly invoke
    `OnStopRunning` of children systems on failure of `ShouldUpdateSystem`
-   Fixed `IManagedComponent` and `ICollectionComponent` reactive systems not
    reacting aside from the first and last frames of a scene
-   `SceneManagerSystem` now properly removes `RequestLoadScene` components
    after processing them

### Improved

-   Many exceptions are now wrapped in a
    `[Conditional("ENABLE_UNITY_COLLECTION_CHECKS")]`
-   Entity destruction on scene changes now works immediately instead of delayed
    unload

## [0.1.0] - 2019-12-21

### This is the first release of *Latios Core for DOTS*.
