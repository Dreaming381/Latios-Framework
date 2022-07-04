# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic
Versioning](http://semver.org/spec/v2.0.0.html).

## [0.5.2] – 2022-7-3

Officially supports Entities [0.50.1]

### Fixed

-   Fixed `FindPairsResult` `bodyAIndex` and `bodyBIndex` which did not generate
    correct indices

### Improved

-   FindPairs performance has been improved after a regression introduced in
    Burst 1.6

## [0.5.0] – 2022-6-13

Officially supports Entities [0.50.1]

### Added

-   *New Feature:* Added convex colliders which use a blob asset for their hull
    but can be non-uniformly scaled at runtime
-   *New Feature:* Added experimental triangle colliders which consist of three
    points
-   Added `PhysicsDebug.DrawCollider()` which draws out the collider shape using
    a configurable resolution

### Fixed

-   Renamed `normalOnSweep` to `normalOnCaster` for `ColliderCastResult` which
    was the intended name

### Improved

-   Legacy collider conversion is now controlled by an installer

## [0.4.5] – 2022-3-20

Officially supports Entities [0.50.0]

### Fixed

-   Fixed an issue where FindPairs caused a Burst internal error when safety
    checks are enabled using Burst 1.6 or higher. A harmless workaround to the
    bug was discovered.

## [0.4.1] – 2021-9-16

Officially supports Entities [0.17.0]

### Fixed

-   Fixed `DistanceBetween()` queries for nearly touching box colliders where
    the edges were incorrectly reported as the closest points.
-   Fixed `ColliderCast()` queries for sphere vs box where negative local axes
    faces could not be hit.
-   Fixed `ColliderCast()` queries for sphere vs box where the wrong edge
    fraction was reported.
-   Fixed `ColliderCast()` queries for capsule vs capsule where a hit at the
    very end of the cast would report a zero distance hit.
-   Fixed `ColliderCast()` queries involving compound colliders incorrectly
    reporting a hit if the colliders start in an overlapping state.
-   Fixed `ColliderCast()` queries for capsule casters vs sphere targets where
    the wrong transforms were used.
-   Fixed `ColliderCast()` queries for capsule vs capsule where the query was
    executed in the wrong coordinate space.
-   Fixed `ColliderCast()` queries for box casters vs sphere targets where the
    start and end points were flipped, causing incorrect results to be
    generated.
-   Fixed argument names in `ColliderCast()` queries involving compound
    colliders.

### Improved

-   `ColliderCast()` queries for capsule vs box use a new more accurate
    algorithm.
-   `ColliderCast()` queries for box vs box use a new algorithm which is both
    faster and more accurate.

## [0.4.0] – 2021-8-9

Officially supports Entities [0.17.0]

### Added

-   Added `StepVelocityWithInput()`. It is the input-driven smooth motion logic
    I have been using for several projects.
-   Added several methods and utilities to aid in ballistics simulations. This
    API is subject to change as I continue to explore and develop this aspect of
    Psyshock. The new stuff can be found in
    *Physics/Dynamics/Physics.BallisticUtils.cs*.
-   *New Feature:* Added `Physics.ColliderCast()` queries.
-   *New Feature:* Added `DistanceBetween()` queries that take a single point as
    an input.
-   Added `subColliderIndex` to `PointDistanceResult` and `RaycastResult`.
-   Added `CombineAabb()`.

### Changed

-   **Breaking:** Renamed `CalculateAabb()` to `AabbFrom()`.

### Fixed

-   Fixed an issue where `DistanceBetween()` queries did not properly account
    for the center point of `SphereColliders`.

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
