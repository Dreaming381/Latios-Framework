# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2020-9-22
Officially supports Entities [0.14.0]
### Added
* New Feature: Burst Patcher uses codegen at Build time to allow for compilation of generic jobs scheduled by generic methods even if the generic argument or the job is private
* Added "Bootstrap - Explicit Workflow" creation tool
* Added `BootstrapTools.InjectUnitySystems` which does exactly what it sounds like
* Added `BootstrapTools.UpdatePlayerLoopWithDelayedSimulation` which runs `SimulationSystemGroup` after rendering
* New Feature: Collection component dependency management now hooks into the `Dependency` field that lambda jobs use
* New Feature: Fluent Queries provide a fluent extendible shorthand for generating EntityQuery instances
* Added `LatiosSimulationSystemGroup` and `LatiosPresentationSystemGroup`
* Added `IManagedComponent` and `ICollectionComponent` API to `ManagedEntity`
* Added float2 variants for `SmoothStart` and `SmoothStop` in `LatiosMath`

### Changed
* The original Bootstrap creation is now labeled "Bootstrap - Injection Workflow"
* Renamed `IComponent` to `IManagedComponent`
* `IManagedComponent` and `ICollectionComponent` behave differently due to DOTS evolution. See the documentation for how to use them.
* `FixedString` is used instead of `NativeString`
* `BootstrapTools.UpdatePlayerLoopWithFixedUpdate` is now `BootstrapTools.AddWorldToCurrentPlayerLoopWithFixedUpdate` with updated API to mirror `ScriptBehaviorUpdateOrder`
* `EntityDataCopyKit` now ignores collection components by default
* `SubSystem` now subclasses `SystemBase` and should be used for all logical systems
* `LatiosInitializationSystemGroup` was reworked for compatibility with the latest Entities

### Removed
* `JobSubSystem` - use `SubSystem` instead which now subclasses `SystemBase`

### Fixed
* Fixed an issue with `EntityDataCopyKit` and dynamic buffers
* `SuperSystem` and `RootSuperSystem` should now properly invoke `OnStopRunning` of children systems on failure of `ShouldUpdateSystem`
* Fixed `IManagedComponent` and `ICollectionComponent` reactive systems not reacting aside from the first and last frames of a scene
* `SceneManagerSystem` now properly removes `RequestLoadScene` components after processing them

### Improved
* Many exceptions are now wrapped in a `[Conditional("ENABLE_UNITY_COLLECTION_CHECKS")]`
* Entity destruction on scene changes now works immediately instead of delayed unload

## [0.1.0] - 2019-12-21

### This is the first release of *Latios Core for DOTS*.
