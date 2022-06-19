# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic
Versioning](http://semver.org/spec/v2.0.0.html).

## [0.5.1] – 2022-6-19

Officially supports Entities [0.50.1]

### Added

-   Added `SkeletonClip.SamplePose()` which uses a fast path to compute the
    entire `OptimizedBoneToRoot` buffer
-   Added bool `hasAnyParentScaleInverseBone` and bitmask array
    `hasChildWithParentScaleInverseBitmask` to `OptimizedSkeletonHierarchyBlob`
    which can be used to detect if an inverse scale value needs to be calculated
    in advance
-   Added `SkeletonClip.boneCount` for safety purposes
-   Added `SkeletonClip.sampleRate` for convenience when using other
    `KeyframeInterpolationMode`s than the default
-   Added `SkeletonClip.sizeUncompressed` and `SkeletonClip.sizeCompressed` to
    compare the efficacies of different compression levels

### Fixed

-   Fixed an bug where only the first skeleton archetype was used for rendering
-   Skinned Mesh Renderers are no longer treated as bones in the skeleton during
    conversion
-   Fixed a bug where the first clip in a `SkeletonClipSetBakeData` was
    duplicated for all of the clips in the resulting `SkeletonClipSetBlob`
-   Scene root transforms are no longer baked into `SkeletonClip`
-   `InstallKinemationConversion()` now works correctly when the conversion
    bootstrap returns `false`
-   Applied the Hybrid Renderer 0.51 specular reflections bugfix to Kinemation’s
    renderer

## [0.5.0] – 2022-6-13

Officially supports Entities [0.50.1]

### This is the first release of *Kinemation*.
