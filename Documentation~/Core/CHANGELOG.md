# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic
Versioning](http://semver.org/spec/v2.0.0.html).

## [0.5.8] – 2022-11-10

Officially supports Entities [0.51.1]

### Fixed

-   Fixed incorrect input locking behavior for batching Smart Blobbers that
    caused exceptions when used with subscenes

## [0.5.7] – 2022-8-28

Officially supports Entities [0.51.1]

### Changed

-   Explicitly applied Script Updater changes for hashmap renaming

## [0.5.6] – 2022-8-21

Officially supports Entities [0.50.1] – [0.51.1]

### Added

-   Added `ObjectAuthoringExtensions.DestroyDuringConversion()` to facilitate
    destroying temporary `UnityEngine.Objects` during conversion that works in
    Editor Mode, Play Mode, and Runtime.

## [0.5.5] – 2022-8-14

Officially supports Entities [0.50.1] – [0.51.1]

### Fixed

-   Fixed Extreme Transforms crashing on startup in builds

## [0.5.2] – 2022-7-3

Officially supports Entities [0.50.1]

### Fixed

-   Fixed Smart Blobbers hiding exception stack traces

## [0.5.1] – 2022-6-19

Officially supports Entities [0.50.1]

### Added

-   Added
    `CustomConversionSettings.OnPostCreateConversionWorldEventWrapper.OnPostCreateConversionWorld`
    which can be used to post-process the conversion world after all systems are
    created

### Fixed

-   Applied the Entities 0.51 `ParentSystem` bugfix to `ImprovedParentSystem`
    and `ExtremeParentSystem`
-   Made the `Child` buffer order deterministic for `ImprovedParentSystem` and
    `ExtremeParentSystem`

## [0.5.0] – 2022-6-13

Officially supports Entities [0.50.1]

### Added

-   *New Feature:* Added experimental NetCode support
-   *New Feature:* Added Smart Blobbers which provide a significantly better
    workflow for Blob Asset conversion
-   *New Feature:* Added `ICustomConversionBootstrap` which allows modifying the
    setup of a conversion world similar to `ICustomBootstrap`
-   *New Feature:* Added Improved Transforms and Extreme Transforms which
    provide faster and more bug-free transform system replacements while still
    preserving the user API
-   Added new bootstraps and templates
-   Exposed `UnsafeParallelBlockList` which is a fast container that can be
    written to in parallel
-   Added `[NoGroupInjection]` attribute to prevent a system from being injected
    in a default group
-   Added `CoreBootstrap` which provides installers for Scene Management and the
    new Transform systems
-   Added chunk component options to Fluent queries
-   Added Fluent method `WithDelegate()` which allows using a custom delegate in
    a Fluent query rather than an extension method
-   Added `LatiosWorld.ForceCreateNewSceneBlackboardEntityAndCallOnNewScene()`
    which provides an alternative to use that functionality without Scene
    Manager installed
-   Added `LatiosMath.RotateExtents()` overload that uses a scalar `float` for
    `extents`
-   Added `BlobBuilderArray.ConstructFromNativeArray<T>()` overload which uses
    raw pointer and length as arguments
-   Added `GetLength()` extension for Blob Assets

### Changed

-   Scene Manager is no longer automatically created by
    `LatiosInitializationSystemGroup` and instead has a dedicated installer
-   All systems inject into the base `InitializationSystemGroup` instead of the
    Latios-prefixed version
-   The ordering of systems in `InitializationSystemGroup` relative to Unity
    systems have been altered

### Fixed

-   Added missing `readOnly` argument to `BlackboardEntity.GetBuffer()`
-   Removed GC allocations in `EntityManager` Collection Component and Managed
    Struct Component implementations
-   Fixed a bug where `SuperSystem` was consuming exceptions incorrectly and
    hiding stack traces
-   Added missing namespace to StringBuilderExtensions

### Improved

-   Added significantly more XML documentation
-   `InstantiateCommandBuffer` can now have up to 15 tags added incrementally

### Removed

-   Removed `[BurstPatcher]` and `[IgnoreBurstPatcher]` attributes

## [0.4.5] – 2022-3-20

Officially supports Entities [0.50.0]

### Added

-   Added `Fluent()` extension method for `SystemState` for use of Fluent in
    `ISystem`.
-   Added `ISystemNewScene` and `ISystemShouldUpdate` which provides additional
    callbacks for `ISystem` matching that of `SubSystem` and `SuperSystem`.
-   Added `GetWorldBlackboardEntity()` and `GetSceneBlackboardEntity()`
    extension methods for `SystemState` which are compatible with Burst.
-   Added `SortingSystemTracker` which can detect systems added or removed from
    a `ComponentSystemGroup`.
-   Added `CopyComponentData()` and `CopyDynamicBuffer()` extension methods for
    `EntityManager` which are compatible with Burst.
-   Added `CopySharedComponent()` extension method for `EntityManager`.
-   Added `GetSystemEnumerator()` extension method for `ComponentSystemGroup`
    which can step through all managed and unmanaged systems in update order.
-   Added `ExecutingSystemHandle()` extension method for `WorldUnmanaged` which
    returns a handle to the system currently in its `OnUpdate()` routine. This
    is compatible with Burst.
-   Added `AsManagedSystem()` extension method for `World` and `WorldUnmanaged`
    which extract the class reference of a managed system from a
    `SystemHandleUntyped`.
-   Added `GetAllSystemStates()` extension method for `WorldUnmanaged` which can
    gather all `SystemState` instances and consequently all systems in a World
    including unmanaged systems.
-   Added `WorldExposedExtensions.GetMetaIdForType()` which can generate an ID
    for a `System.Type` which can be compared against
    `SystemState.UnmanagedMetaIndex`.

### Changed

-   `BootstrapTools.InjectSystem` can now inject unmanaged systems. Many other
    `BootstrapTools` functions now handle unmanaged systems too.

### Fixed

-   Fixed an issue where custom command buffers could not be played back from
    within a Burst context.
-   All custom `ComponentSystemGroup` types including `SuperSystem` and
    `RootSuperSystem` support unmanaged systems.
-   All custom `ComponentSystemGroup` types including `SuperSystem` and
    `RootSuperSystem` support `IRateManager`.
-   All custom `ComponentSystemGroup` types including `SuperSystem` and
    `RootSuperSystem` support automatic system sorting when systems are added
    and removed.
-   Fixed an issue where the managed and collection component reactive systems
    were being stripped in builds.
-   Fixed an issue with `EntityDataCopyKit` not handling default-valued shared
    components correctly.

### Improved

-   `EntityDataCopyKit` no longer uses reflection.
-   All custom `ComponentSystemGroup` types including `SuperSystem` and
    `RootSuperSystem` behave much more like Unity’s systems regarding error
    handling.

## [0.4.3] – 2022-2-21

Officially supports Entities [0.17.0]

### Fixed

-   Fixed GC allocations caused by using a foreach on `IReadOnlyList` type
-   Removed a reference to the Input System package in Latios.Core.asmdef

## [0.4.2] – 2021-10-5

Officially supports Entities [0.17.0]

### Added

-   Added `flags` parameter to `LatiosWorld` constructor which is required for
    NetCode projects.

### Improved

-   Improved the error message for when a `SubSystem` begins its `OnUpdate()`
    procedure but the previous `SubSystem` did not clean up the automatic
    dependency stack. The error now identifies both systems and suggests the
    more likely cause being an exception in the previous `SubSystem`.

## [0.4.1] – 2021-9-16

Officially supports Entities [0.17.0]

### Added

-   Added `simd.length()`.
-   Added `simd.select()` which allows for selecting the x, y, and z components
    using separate masks.
-   Added `simd.cminxyz()` and `cmaxxyz()` which computes the min/max between x,
    y, and z for each of the four float3 values.
-   Added `simd.abs()`.
-   Added `simd.project()`.
-   Added `simd.isfiniteallxyz()` which returns true for each float3 if the x,
    y, and z components are all finite values.

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
