# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic
Versioning](http://semver.org/spec/v2.0.0.html).

## [0.3.2] – 2021-5-25

Officially supports Entities [0.17.0]

### Added

-   Added constructor for `PhysicsScale` to auto-compute the state for a `float3
    scale`

### Fixed

-   Fixed compound collider scaling for AABBs and `DistanceBetween()`
-   Fixed `BuildCollisionLayer.ScheduleSingle()` using the wrong parallel job
    count
-   Fixed issue where a user could accidentally generate silent race-conditions
    when using `PhysicsComponentDataFromEntity` and `RunImmediate()` inside a
    parallel job. An error is thrown instead when safety checks are enabled.
-   Renamed some files so that Unity would not complain about them matching the
    names of `GameObject` components

## [0.3.0] – 2021-3-4

Officially supports Entities [0.17.0]

### Added

-   *New Feature:* Added support for Box Colliders
-   Added implicit conversion operators from `ComponentDataFromEntity` to
    `PhysicsComponentDataFromEntity` and from `BufferFromEntity` to
    `PhysicsBufferFromEntity`. You can now use `Get###FromEntity()` instead of
    `this.GetPhysics###FromEntity()`.
-   Added `subColliderIndexA` and `subColliderIndexB` to
    `ColliderDistanceResult`, which represent the indices of the collider hit in
    a `CompoundCollider`

### Changed

-   **Breaking:** `Latios.Physics` assembly was renamed to `Latios.Psyshock`
-   **Breaking:** The namespace `Latios.PhysicsEngine` was renamed to
    `Latios.Psyshock`
-   **Breaking:** `LatiosColliderConversionSystem` and `LatiosColliderAuthoring`
    have been renamed to `ColliderConversionSystem` and `ColliderAuthoring`
    respectively

### Fixed

-   Added missing `Conditional` attribute when building a collision layer from
    an `EntityQuery`

### Improved

-   All authoring components have now been categorized under *Latios-\>Physics
    (Psyshock)* in the *Add Component* menu

## [0.2.2] - 2021-1-24

Officially supports Entities [0.17.0]

### Added

-   *New Feature:* The FindPairs dispatcher from the upcoming [0.3.0] has been
    backported in order to better support 2020.2 users
-   `FindPairs.ScheduleParallel` now checks for entity aliasing when safety
    checks are enabled and throws when a conflict is discovered
-   `FindPairs.WithoutEntityAliasingChecks` disables entity aliasing checks
-   `FindPairs.ScheduleParallelUnsafe` breaks aliasing rules for increased
    parallelism
-   When safety checks are enabled, FindPairs bipartite mode validates that the
    two layers passed in are compatible

### Changed

-   `CompoundColliderBlob.colliders` is now a property instead of a field

### Fixed

-   FindPairs jobs now show up in the Burst Inspector and no longer require
    BurstPatcher
-   Fixed a bug when raycasting capsules
-   Properly declare dependencies during collider conversion
-   Fixed warning of empty tests assembly

## [0.2.0] - 2020-9-22

Officially supports Entities [0.14.0]

### Added

-   *New Feature:* Compound Colliders are here
-   *New Feature:* `BuildCollisionLayer` schedulers
-   Added `PhysicsComponentDataFromEntity` and `PhysicsBufferFromEntity` to help
    ensure correct usage of writing to components in an `IFindPairsProcessor`
-   Added `PatchQueryForBuildingCollisionLayer` extension method to
    `FluentQuery`
-   Added `Physics.TransformAabb` and `Physics.GetCenterExtents`

### Changed

-   Collider conversion has been almost completely rewritten
-   Renamed `AABB` to `Aabb`
-   `CalculateAabb` on colliders is now a static method of `Physics`
-   Renamed `worldBucketCountPerAxis` to `worldSubdivisionsPerAxis` in
    `CollisionLayerSettings`

### Removed

-   Removed `GetPointBySupportIndex`, `GetSupportPoint` and `GetSupportAabb`
    methods on colliders
-   Removed `ICollider`
-   Removed `CollisionLayerType` as it was unnecessary
-   Removed `Physics.DistanceBetween` for a point and a sphere collider as this
    API was not complete

### Fixed

-   Fixed issues with capsule collider conversion
-   Fixed issues with extracting transform data from entities in
    `BuildCollisionLayer`

### Improved

-   Many exceptions are now wrapped in a
    `[Conditional("ENABLE_UNITY_COLLECTION_CHECKS")]`
-   `Collider` now uses `StructLayout.Explicit` instead of `UnsafeUtility` for
    converting to and from specific collider types
-   `BuildCollisionLayer` using an `EntityQuery` no longer requires a
    `LocalToWorld` component

## [0.1.0] - 2019-12-21

### This is the first release of *Latios Physics for DOTS*.
