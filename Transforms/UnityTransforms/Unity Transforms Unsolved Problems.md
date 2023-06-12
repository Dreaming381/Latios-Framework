# Unity Transforms Unsolved Problems

This document contains known issues and other unsolved problems with Unity
Transforms support in the Latios Framework.

## Parsing TransformQvvs from LocalToWorld

There are many instances where the Latios Framework wants the world-space
TransformQvvs. Currently, the code ignores all scaling and shearing inside
LocalToWorld and only fetches position and rotation and sets scale and stretch
to 1f all around. Is there something better to do here? How would we account for
PostTransformMatrix?

## CopyParentWorldTransformTag

This is currently ignored. But how should we implement it? Continue to ignore
it? Force LocalTransform to identity? Implement a custom LocalToWorldSystem?

## PreviousTransform

The equivalent for this in Unity is BuiltinMaterialPropertyUnity_MatrixPreviousM
which is in the rendering namespace and could cause problems if we try to treat
it as broadly as the motion history transforms. Do we use it directly for motion
history? Or do we ignore it and define our own PreviousTransform?

Currently, there is no implementation of Motion History for Unity Transforms and
no motion history is considered for skinning for exposed skeletons.

## Exported Bones Children Sync During Baking

How do we ensure children of exported bones end up with the right transforms
during baking? Do we just hope that the GameObject hierarchy is sufficient?

## Change Filters on WorldTransformReadOnlyAspect

How do we get the change filter? Do we type-pun TypeHandle? Should we rewrite
the aspect manually? Currently we are relying on extension methods for
EntityQuery, but it would be nice to filter directly in a job.

## Writing to Exported Bones

Exported bones may only have a LocalTransform and don’t have anything for
stretch. What should happen? Currently, stretch is discarded.

## When Should MatrixPrevious Update?

In Entities Graphics, material properties are uploaded before culling. So after
uploading MatrixPrevious, Unity would run a system to update it for the next
frame. This doesn’t work with Kinemation due to the latter material property
uploads. In 0.5, a proxy component was added, but this was expensive. In 0.7,
QVVS Transforms have motion history which handle this responsibility. So what
should this new Unity Transforms compatibility mode do?

Currently, MatrixPrevious is updated in the motion history super system.

## Skinned Mesh Culling with Scale and Shear

How do we detect that a skinned mesh requires the slow path for culling that in
QVVS world is handled by PostProcessMatrix? Currently, this is ignored.

## IAspect as an Abstraction

Currently, IAspect is used to generate abstractions in various places. However,
the TypeHandle and Lookup types are not very flexible and make the abstractions
not be sufficient in various places. Perhaps there should be manually-written
abstractions for these rather than using IAspect-generated version? Then they
could be customized for advanced usages.
