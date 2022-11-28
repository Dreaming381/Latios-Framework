# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic
Versioning](http://semver.org/spec/v2.0.0.html).

## [0.6.1] – 2022-11-28

Officially supports Entities [1.0.0 prerelease 15]

### Added

-   Added `OverrideMeshRendererBase` which if present on a `GameObject` will
    disable default `MeshRenderer` baking for that `GameObject`
-   Added `OverrideMeshRendererBakerBase<T>` which allows baking a `Renderer`
    using a custom mesh and materials that might be generated at bake time

### Fixed

-   Fixed issue where the runtime mechanism for ensuring Kinemation culling
    components were present queried `RenderMesh` instead of `MaterialMeshInfo`
-   Fixed issue where `SkinnedMeshRenderers` might be excluded from
    `RenderMeshArray` post-processing during baking

### Improved

-   Kinemation renderer was updated with the latest material error handling
    improvements of Entities Graphics

## [0.6.0] – 2022-11-16

Officially supports Entities [1.0.0 experimental]

### Added

-   *New Feature:* The Kinemation renderer can now be used in the Editor World,
    providing previews of skinned mesh entities
-   Added `ChunkPerCameraCullingSplitsMask` for shadow map culling
-   Added `CullingSplitElement` for shadow map culling
-   Added many new fields to `CullingContext` which come from the new
    `BatchRendererGroup` API
-   Added `ChunkPerCameraSkeletonCullingSplitsMask` for shadow map culling of
    skeletons

### Changed

-   **Breaking:** Authoring has been completely rewritten to use baking workflow
-   **Breaking:** `KinemationConversionBootstrap.InstallKinemationConversion()`
    has been replaced with
    `KinemationBakingBootstrap.InstallKinemationBakersAndSystems()`
-   **Breaking:** `AnimationClip.ExtractKienamtionClipEvents()` now returns a
    `NativeArray` and expects an `Allocator` argument
-   **Breaking:** Renamed `PhysicsComponentDataFromEntity` to
    `PhysicsComponentLookup` and `PhysicsBufferFromEntity` to
    `PhysicsBufferLookup`
-   Exposed skeleton baking will not include a descendent Game Object with an
    Animator as part of the skeleton
-   Optimized skeleton baking only considers the first level children without
    Skinned Mesh Renderers or Animators to be exported bones, meaning exported
    bones can now have dynamic children
-   *Skeleton Authoring* has been replaced with *Skeleton Settings Authoring*
-   The new Culling algorithms work differently, so custom culling code should
    be re-evaluated as there may be opportunities to leverage more of the system

### Fixed

-   Fixed component conflicts between bound skinned meshes and exported bones
    during baking
-   Fixed bug where exposed bone culling arrays were allocated to double the
    amount of memory they actually required

### Improved

-   Added property inspectors in the editor for `MeshBindingPathsBlob`,
    `SkeletonBindingPathsBlob`, and `OptimizedSkeletonHierarchyBlob` which can
    be used to debug binding issues
-   Many systems are now Burst-compiled `ISystems` using the much-improved
    Collection Component manager and consequently run much faster on the main
    thread, especially during culling with improvements as much as 5 times
    better performance
-   Material Property Component Type Handles are fully cached
-   Meshes no longer need to be marked Read/Write enabled

### Removed

-   Removed `BindingMode`, `ImportStatus`, and `BoneTransformData`

## [0.5.8] – 2022-11-10

Officially supports Entities [0.51.1]

### Fixed

-   Fixed a crash where a `MeshSkinningBlob` is accessed after it has been
    unloaded by a subscene
-   Fixed `LocalToParent` being uninitialized if it was not already present on a
    skinned mesh entity prior to binding
-   Fixed bone bounds not being rebound correctly when a skinned mesh is removed
-   Fixed several caching issues that resulted in bindings reading the wrong
    cached binding data after all entities referencing a cached binding are
    destroyed and new bindings are later created

## [0.5.7] – 2022-8-28

Officially supports Entities [0.51.1]

### Changed

-   Explicitly applied Script Updater changes for hashmap renaming

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
