# Upgrade Guide [0.5.8] [0.6.0]

Please back up your project in a separate directory before upgrading! You will
likely need to reference the pre-upgraded project during the upgrade process.

## Core

### New Bootstrap and Installers

You will likely need to recreate your bootstrap, as the authoring paradigm has
completely changed.

### Scene Manager

Scene Manager now forces subscenes to load synchronously, and
`DontDestroyOnSceneChangeTag` will now protect entities from being destroyed by
subscene unload.

### Initialization System Order

This changed a bit. If you were relying on specific ordering, you might
encounter a few issues. However, such issues aren’t expected to be common.

### Smart Blobbers

With the transition from *conversion* to *baking*, Smart Blobbers had to be
completely redesigned. The new design has a steeper learning curve, but is much
more flexible. In 0.5.x, there was a 1-1-1-1 correspondence between the blob
type, the input type, the converter type, and the conversion system. In 0.6.x,
no such correspondence exists. You can define multiple inputs for a single blob
type, and have multiple baking systems work with a blob type, or make a single
baking system work with multiple blob types. The new paradigm uses bakers to add
input components to special baking-only entities, and then uses baking systems
to compute blob assets for those entities by writing to a special component.
Blob asset allocation, deduplication, and incremental tracking are handled in
separate baking systems, rather than a base class.

There is also a `SmartBaker` type, which serves to functionally replace
`IRequestBlobAssets`.

### Scripting Defines

Make sure to add `ENABLE_TRANSFORM_V1` to your scripting defines.

### IManagedComponent -\> IManagedStructComponent

Not only is there a rename, but `AssociatedComponentType` expects a
`ComponentType` now.

### ICollectionComponent

These have been redesigned to be fully Burst-compatible in `ISystem`. While the
general concept remains the same, expect breakages in many of the methods and
interfaces. In particular, it is now up to the user to track whether a
collection component is initialized.

## Psyshock Physics

### IFindPairsProcessor

`Execute()` now receives its argument as an `in` parameter.

### Collider Baking

Multiple colliders on a single Game Object are supported (excluding convex
meshes) and will be combined into a compound collider. The enabled checkbox will
exclude the component from being baked.

## Myri Audio

### ListenerProfileBuilder

This type now uses a context object passed by `ref` rather than protected member
methods. There is a new alternative way to specify a builder using
`IListenerProfileBuilder` if you need to construct a profile on the fly.

## Kinemation

### Skeleton and Skinned Mesh Authoring

Several options for defining your own custom skeleton have been removed. Some
features may be added back later. Also, the skeleton definitions have been
tweaked to remove unwanted bones more aggressively. A skeleton parented to a
bone in another skeleton should now be possible.

### ParameterClipSetBlob

There is no longer a Smart Blobber for this blob type. If you need this, please
reach out to me, as I am trying to better understand use cases to design a new
effective API.

## New Things to Try!

### Core

Collection Components and `SyncPointPlaybackSystem` and a whole bunch of other
goodies are Burst-compatible thanks to the new `LatiosWorldUnmanaged` type.

Also, try out the new `ICustomBakingBootstrap` and `ICustomEditorBootstrap`.

### Psyshock Physics

There’s a new FindObjects algorithm which works like FindPairs, but for a single
AABB query against a layer. It runs faster than O(n) time.

There are now new shortcut APIs for raycasting, collider-casting, and
distance-testing a layer. They all make use of the new FindObjects algorithm
under-the-hood.

`FindPairsResult` has been overhauled to provide shorter syntax for distance
queries, and expose the layers for additional FindObjects queries.

### Myri

Myri has a new API for procedurally generating audio at bake time for an
`AudioClipBlob` without the use of an `AudioClip` asset. The new API lets you
generate the samples in parallel Burst-compiled jobs.

### Kinemation

You can bake a Skinned Mesh Renderer with dynamic skeleton binding info without
baking its parent skeleton. To do this, inside a baker, call
`GetComponentsInChildren<SkinnedMeshRenderer>()` and then call `GetEntity()` on
one of the results. I want to see someone do character customization with this!
