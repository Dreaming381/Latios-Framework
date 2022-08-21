# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic
Versioning](http://semver.org/spec/v2.0.0.html).

## [0.5.6] – 2022-8-21

Officially supports Entities [0.50.1] – [0.51.1]

### Added

-   Added `ClipEvents.TryGetEventsRange` which can find all events between two
    time points
-   Added `SkeletonBindingPathsBlob.StartsWith()` and`
    SkeletonBindingPathsBlob.TryGetFirstPathIndexThatStartWith()` for finding
    optimized bones by name.

### Fixed

-   Fixed multiple issues that caused errors when destroying exposed skeleton
    entities at runtime
-   Fixed Editor Mode Game Object Conversion when destroying shadow hierarchies
    used for capturing skeleton structures and animation clip samples

### Improved

-   Improved performance of the culling callbacks by \~15%

## [0.5.5] – 2022-8-14

Officially supports Entities [0.50.1] – [0.51.1]

### Added

-   Added `ClipEvents` which can be baked into `SkeletonClipSetBlob` and
    `ParameterClipSetBlob` instances. These are purely for user use and do not
    affect the Kinemation runtime.
-   Added `UnityEngine.AnimationClip.ExtractKinemationClipEvents()` which
    converts `AnimationEvent`s into a form which can be baked by the Smart
    Blobbers
-   Added `ParameterClipSetBlob` and an associated Smart Blobber. They can be
    used for baking general purpose animation curves and other scalar parameters
    into Burst-friendly compressed forms.
-   Added `BufferPoseBlender.ComputeBoneToRoot()` which can compute a
    `BoneToRoot` matrix for a single bone while the buffer remains in local
    space. This may be useful for IK solvers.
-   Added `SkeletonClipCompressionSettings.copyFirstKeyAtEnd` which can be used
    to fix looping animations which do not match start and end poses

### Fixed

-   Fixed `OptimizedBoneToRoot` construction using `ParentScaleInverse`, which
    was applying inverse scale to translation
-   Fixed the `duration` of clips being a sample longer than they actually are

## [0.5.4] – 2022-7-28

Officially supports Entities [0.50.1]

### Added

-   Added Mac OS support (including Apple Silicon)

### Fixed

-   Fixed an issue where multiple materials on a skinned mesh would not copy
    their skinning indices to the GPU, causing confusing rendering artifacts
-   Fixed crash with Intel GPUs that do not support large compute thread groups

## [0.5.3] – 2022-7-4

Officially supports Entities [0.50.1]

### Fixed

-   Fixed `ShaderEffectRadialBounds` crash caused by broken merge prior to 0.5.2
    release

## [0.5.2] – 2022-7-3

Officially supports Entities [0.50.1]

### Added

-   Added Linux support
-   Added `SkeletonClip.SamplePose()` overload which uses a `BufferPoseBlender`
    and performs optimal pose sampling
-   Added `BufferPoseBlender` which can temporarily repurpose a
    `DynamicBuffer<OptimizedBoneToRoot>` into a working buffer of
    `BoneTransform`s for additive pose sampling (blending) and IK and then
    convert the resulting `BoneTransform`s back into valid `OptimizedBoneToRoot`
    values

### Fixed

-   Fixed `ShaderEffectRadialBounds` which was ignored
-   Fixed issue where root motion animation was applied directly to the
    `OptimizedBoneToRoot` buffer when using pose sampling

### Improved

-   Fixed change filtering of exposed bone bounds
-   ACL Unity plugin binaries are now built using GitHub Actions and have a
    tagged release which includes artifacts for debug symbols as well as
    human-readable assembly text

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

-   Fixed a bug where only the first skeleton archetype was used for rendering
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
