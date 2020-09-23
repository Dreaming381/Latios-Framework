# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2020-9-22
Officially supports Entities [0.14.0]
### Added
* New Feature: Compound Colliders are here
* New Feature: `BuildCollisionLayer` schedulers
* Added `PhysicsComponentDataFromEntity` and `PhysicsBufferFromEntity` to help ensure correct usage of writing to components in an `IFindPairsProcessor`
* Added `PatchQueryForBuildingCollisionLayer` extension method to `FluentQuery`
* Added `Physics.TransformAabb` and `Physics.GetCenterExtents`

### Changed
* Collider conversion has been almost completely rewritten
* Renamed `AABB` to `Aabb`
* `CalculateAabb` on colliders is now a static method of `Physics`
* Renamed `worldBucketCountPerAxis` to `worldSubdivisionsPerAxis` in `CollisionLayerSettings`

### Removed
* Removed `GetPointBySupportIndex`, `GetSupportPoint` and `GetSupportAabb` methods on colliders
* Removed `ICollider`
* Removed `CollisionLayerType` as it was unnecessary
* Removed `Physics.DistanceBetween` for a point and a sphere collider as this API was not complete

### Fixed
* Fixed issues with capsule collider conversion
* Fixed issues with extracting transform data from entities in `BuildCollisionLayer`

### Improved
* Many exceptions are now wrapped in a `[Conditional("ENABLE_UNITY_COLLECTION_CHECKS")]`
* `Collider` now uses `StructLayout.Explicit` instead of `UnsafeUtility` for converting to and from specific collider types
* `BuildCollisionLayer` using an `EntityQuery` no longer requires a `LocalToWorld` component

## [0.1.0] - 2019-12-21

### This is the first release of *Latios Physics for DOTS*.
