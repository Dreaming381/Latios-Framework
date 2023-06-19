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

Currently, MatrixPrevious is updated in the motion history super system, which
exists as a shim to support Kinemation’s systems but does nothing itself.

## Skinned Mesh Culling with Scale and Shear

How do we detect that a skinned mesh requires the slow path for culling that in
QVVS world is handled by PostProcessMatrix? Currently, the user must manage
PostProcessMatrix as described by the XML documentation.

## Consequences

The following lists out the consequences of these unsolved problems as things
stand today in this compatibility mode:

1) Optimized skeletons may bake children of exported bones in the wrong spots
which won’t get corrected until animation plays (which means it will always look
wrong in the Editor).

2) The system that handles motion vectors for normal meshes will update in the
beginning of SimulationSystemGroup rather than in PresentationSystemGroup.

3) Motion vectors for skinned meshes attached to exposed skeletons won’t work.

4) Psyshock CollisionLayers generated from Entity Queries only consider position
and rotation.

5) Exposed skeletons only use position and rotation of bones for skinning.

6) Exported bones only receive position, rotation, and uniform scale from the
optimized skeleton and ignore stretch.

7) Skinned meshes must have identical LocalToWorld as their parent skeleton or a
properly specified PostProcessMatrix or else culling will be inaccurate.

8) Some components may exist but have no or altered effect compared to QVVS
Transforms.
