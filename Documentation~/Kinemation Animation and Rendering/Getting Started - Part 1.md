# Getting Started with Kinemation – Part 1

This is the first preview version released publicly. As such, it only provides
foundational features. For simple use cases, it is easy to get started. But for
advanced production workflows, that burden may fall on the user.

It will be up to you, a member of the community, to help guide Kinemation’s
continual development to meet the needs of real-world productions. Please be
open and loud on your journey. Feedback is critical.

This Getting Started series is broken up into multiple parts. This first part
covers the terms and runtime structure. It is okay if you do not understand
everything in this part or choose to skim through it. The remaining parts will
guide you through the process of configuring and animating a character as an
entity.

## Minimum Requirements

-   Currently Supported Platforms
    -   Windows Standalone
-   GPU Minimum Requirements
    -   Supports Hybrid Renderer V2
    -   Supports 8 simultaneous compute buffers in compute shaders
    -   Has 32 kB of shared memory per thread group
    -   Supports thread group dimensions of (1024, 1, 1)
-   Content Requirements
    -   Skeleton size must be 341 or less (can be larger if bound meshes use
        small number of bones each)
    -   No limit to number of meshes bound to skeleton

## Skeletons and Bones – Not What You Think

Before we explore the different building blocks of Kinemation, we need to make
sure we are on the same page with these two terms. Kinemation has very peculiar
interpretations of the words “skeleton” and “bone”. These interpretations come
from Unity’s asset model. We’ll discuss this from the authoring perspective,
that is in terms of Game Objects.

A “bone” is simply a `Transform` that is included in a group of transforms for
animation purposes. Anything that originated from a `GameObject` can be a bone.
A foot can be a bone. A mesh can be a bone. A particle effect can be a bone.
Even the camera can be a bone. Some bones may influence skinned meshes. Some
won’t.

A “skeleton” is just a collection of bones. Those bones could be hierarchically
structured. But they also might not be. When evaluating skinning, bones are
transformed into the skeleton object’s local space. Skinned meshes bound to a
skeleton are forced to exist in this space. Because the bones and the meshes are
in the same coordinate space, skinning works.

The other aspect of a “skeleton” is that it is the bind target of skinned
meshes. Even if the skeleton contains multiple armatures from a DCC application,
the meshes get bound to the skeleton, not the armature `GameObjects`.

By default, Kinemation treats anything with an `Animator` as a skeleton. That
`GameObject` and all descendants become bones. This means the “skeleton” is
usually also a “bone”. For optimized hierarchies, Kinemation creates a
de-optimized shadow hierarchy and analyzes that instead.

However, which bones are part of a skeleton can be completely overridden by a
user.

## Anatomy of a Character at Runtime

There are seven main types of entities in Kinemation. Often, an entity may fall
under more than one type at once. The types are as follows:

-   Base skeleton
-   Exposed skeleton
-   Exposed bone
-   Optimized skeleton
-   Exported bone
-   Skinned mesh
-   Shared Submesh

Let’s break down each type.

### Base Skeleton

The *base skeleton* is an entity that can be a bind target for skinned meshes.
It provides a uniform interface for skinned meshes. It is composed of these
components:

-   SkeletonRootTag
    -   Required
    -   Zero-sized
    -   Added during conversion
-   ChunkPerCameraSkeletonCullingMask
    -   Added at runtime automatically
    -   Chunk component used for culling
-   SkeletonBindingPathsBlobReference
    -   Optional
    -   Added during conversion
    -   Used for binding skinned meshes which don’t use overrides
-   DynamicBuffer\<DependentSkinnedMesh\>
    -   Internal
    -   Added at runtime automatically
    -   SystemState
-   PerFrameSkeletonBufferMetadata
    -   Internal
    -   Added at runtime automatically

A skeleton is not valid if it is only a base skeleton. It needs to also be
either an *exposed skeleton* or an *optimized skeleton*. Unless you are building
a skeleton procedurally, you generally do not need to concern yourself with any
of these components.

### Exposed Skeleton

An *exposed skeleton* is a skeleton entity in which every one of its bones is an
entity, and bones rely on Unity’s transform system for skinning and animation.
In addition to the *base skeleton* components, it also has these components:

-   DynamicBuffer\<BoneReference\>
    -   Required
    -   Each element is a reference to an exposed bone entity
    -   Added during conversion
-   BoneReferenceIsDirtyFlag
    -   Optional
    -   Used to command Kinemation to resync the exposed bones with the *exposed
        skeleton*
    -   Must be manually added if needed
-   ExposedSkeletonCullingIndex
    -   Internal
    -   Added at runtime automatically
    -   SystemState

In nearly all cases, the only thing you will want to do is read the
`BoneReference` buffer. The index of each bone in this buffer corresponds to the
bone index in a `SkeletonClip`.

For procedural skeletons, the `BoneReference` buffer is the source of truth. Its
state must be synchronized to all *exposed bones*. That happens once the first
time Kinemation sees the skeleton. It can happen again by adding the
`BoneReferenceIsDirtyFlag` component and setting its value to `true`. Keeping
the `BoneReferenceIsDirtyFlag` component around will cause a fairly heavy
Kinemation system to run every frame. Avoid using it unless you need it.

If an entity in a `BoneReference` does not have the `LocalToWorld` component,
bad things will happen.

The *exposed skeleton* entity is typically also an *exposed bone* entity.

### Exposed Bone

An *exposed bone* is an entity whose transform components dictate its state of
animation. It has the following components:

-   LocalToWorld
    -   Required
    -   Added by Unity during conversion
-   BoneOwningSkeletonReference
    -   Optional
    -   References the *exposed skeleton* entity
    -   Added during conversion (and initialized)
    -   Modified by Kinemation during sync if present
-   BoneIndex
    -   Optional
    -   The bone’s index in the `BoneReference` buffer
    -   Added during conversion (and initialized)
    -   Modified by Kinemation during sync if present

Similar to `BoneReference`, you usually only want to read
`BoneOwningSkeletonReference` and `BoneIndex`. The latter is especially useful
when you need to sample a `SkeletonClip` while chunk-iterating bones.

In addition, an exposed bone may optionally have the following internal
components.

-   BoneCullingIndex
-   BoneBounds
-   BoneWorldBounds
-   ChunkBoneWorldBounds

These components are added automatically during conversion but can also be added
using `CullingUtilities.GetBoneCullingComponentTypes()`. Every bone that
influences a skinned mesh **must** have these components, unless the culling
behavior is overridden entirely.

### Optimized Skeleton

An *optimized skeleton* is an entity whose bones are not represented as entities
for the purposes of animation and skinning. Instead, the bone data is stored
directly in dynamic buffers attached to the optimized skeleton. This keeps the
transforms of the bones next to each other in memory, making hierarchical
updates, frustum culling, and full pose animation significantly faster.

In addition to the *base skeleton* components, an *optimized skeleton* has the
following components:

-   DynamicBuffer\<OptimizedBoneToRoot\>
    -   Required
    -   Each element is a `float4x4` matrix
    -   It is your responsibility to modify the values of this
    -   Added during conversion (and initialized with the pose at conversion
        time)
-   OptimizedSkeletonHierarchyBlobReference
    -   Optional
    -   Provides hierarchical information to convert local space sampled
        animation into the skeleton’s local space
    -   Added during conversion

Normally, you would not modify the length of `OptimizedBoneToRoot`. But you
almost always will want to write its elements. A future part will discuss how to
do that.

The hierarchy blob contains each bone’s parent index (-1 if the bone does not
have a parent). It also has an array of `BitField64` which indicate if that bone
should not inherit its parent’s scale. The first `BitField64` corresponds to the
first 64 bones, the next to the next 64 bones, and so on.

In addition, Kinemation automatically adds the following internal components at
runtime:

-   OptimizedSkeletonTag – SystemState
-   SkeletonShaderBoundsOffset
-   DynamicBuffer\<OptimizedBoneBounds\>
-   SkeletonWorldBounds
-   ChunkSkeletonWorldBounds

### Exported Bone

An *exported bone* is an entity that copies the `OptimizedBoneToRoot` matrix of
an *optimized skeleton* bone and assigns it to its own `LocalToParent` matrix.
When parented directly to the *optimized skeleton* entity, it effectively mimics
the transform of the optimized bone along with all the optimized bone’s
animations. This is often used for rigid character accessories like weapons or
hats.

An *exported bone* has the following components:

-   LocalToParent
-   BoneOwningSkeletonReference
-   CopyLocalToParentFromBone

In addition, an *exported bone* must **not have** any of these components:

-   Translation
-   Rotation
-   Scale
-   NonUniformScale
-   ParentScaleInverse
-   CompositeRotation
-   CompositeScale
-   Any other component that has `[WriteGroup(typeof(LocalToParent))]` besides
    `CopyLocalToParentFromBone`

Kinemation will generate an exported bone for each transform parented to an
*optimized skeleton* at conversion time. However, you can also freely create
these at runtime by meeting the above component requirements and specifying the
bone to track in the `CopyLocalToParentFromBone` component. The *optimized
skeleton* does not know about nor care about *exported bones*, which means you
can have multiple *exported bones* track the same optimized bone. Be careful
though, because *exported bones* can be relatively costly.

*Exported bones* update during the `TransformSystemGroup` update.

An *optimized skeleton* is **not** an *exported bone*.

### Skinned mesh

A skinned mesh is an entity that can be deformed by a skeleton. Skinned meshes
require the following components at all times:

-   BindSkeletonRoot
    -   Contains a reference to one of the following:
        -   *Base skeleton*
        -   *Exposed bone*
        -   *Exported bone*
        -   Already bound skinned mesh
    -   Added during conversion (only if descendent of converted skeleton)
-   MeshSkinningBlobReference
    -   Contains GPU skinning data
    -   Added during conversion

In addition, Kinemation requires one of these components to perform a binding:

-   MeshBindingPathsBlobReference
    -   Fails if the target skeleton does not have a
        `SkeletonBindingPathsBlobReference`
    -   Added during conversion
-   DynamicBuffer\<OverrideSkinningBoneIndex\>
    -   Directly maps each mesh bone (bindpose) to a skeleton bone
    -   Has priority

Skinned meshes can optionally have these components to facilitate binding:

-   NeedsBindingFlag
    -   Instructs Kinemation to rebind when `true`
    -   Presence incurs runtime cost like `BoneReferenceIsDirtyFlag`
-   ShaderEffectRadialBounds
    -   Adds an additional buffer around vertices when computing culling bounds
        to account for vertex shader effects
    -   Assumed zero when not present
    -   Requires rebind whenever added or changed

If Kinemation detects the correct components exist for binding, it will attempt
to bind. Every attempt will result in the following structural changes of
components:

-   Added
    -   Parent
    -   LocalToParent
    -   SkeletonDependent – Internal SystemState
-   Removed
    -   CopyLocalToParentFromBone
    -   Translation
    -   Rotation
    -   Scale
    -   NonUniformScale
    -   ParentScaleInverse
    -   CompositeRotation
    -   CompositeScale

A binding attempt can fail. In that case, Kinemation needs to parent the skinned
mesh to something valid or else the `ParentSystem` will crash. A special entity
with the `FailedBindingsRootTag` exists for this purpose. After a failed binding
attempt, you will need to add the `NeedsBindingFlag` and set its value to `true`
in order to reattempt a binding.

Finally, you may find these internal components added during conversion based on
the skinning methods used:

-   Linear Blend Skinning
    -   LinearBlendSkinningShaderIndex
    -   ChunkLinearBlendSkinningMemoryMetadata
-   Compute Deform
    -   ComputeDeformShaderIndex
    -   ChunkComputeDeformMemoryMetadata

### Shared Submesh

A *shared submesh* is an entity which shares the result of skinning with a
skinned mesh. This typically happens when a Skinned Mesh Renderer that uses
multiple materials is converted into an entity. It always has a
`ShareSkinFromEntity` component which points to the entity with the skin it
should borrow.

A *shared submesh* also has the following internal components added during
conversion:

-   ChunkCopySkinShaderData
    -   Also added at runtime if missing
-   LinearBlendSkinningShaderIndex
    -   If its material uses Linear Blend Skinning
-   ComputeDeformShaderIndex
    -   If its material uses Compute Deform

The biggest surprise with *shared submeshes* is that Kinemation converts the
hierarchy a little different from the rest of the Hybrid Renderer. It looks like
this:

-   Skeleton
    -   Skinned Mesh (Primary Entity) – Material 0
        -   Shared Submesh – Material 1
        -   Shared Submehs – Material 2
        -   …

## On to Part 2

Wow! You made it through. Or maybe you skimmed it. Either way, I hope it is
obvious that Kinemation has a lot going on to make everything work seamlessly.
Fortunately, conversion does most of the heavy lifting for you. So actually
getting started with simple skinned meshes couldn’t be easier!

[Continue to Part 2](Getting%20Started%20-%20Part%202.md)
