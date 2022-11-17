# FAQ – How Do I Choose Between Hybrid, Shared, and Managed Components for My Data?

Unity exposes lots of component types. And the Latios Framework adds its own to
the mix. This can be confusing for new users to pick the right one. This page
aims to answer those questions by going through each data storage type in
Entities and the Latios Framework.

## Struct IComponentData

Use this type for small, mutable, unmanaged data.

This should be your default workhorse, as it is compatible with Burst and
provides the mostly friendly memory access patterns. In general, try to keep the
size of one of these components under 128 bytes. But a few exceptions can be
fine in limited quantities.

## Class IComponentData

Use this sparingly for managed types which need to be serialized on entities.

Allocating one of these generates garbage, and the rules for entity
instantiation and destruction and how entities share references to a class
instance are shaky and inconsistent. There are usually better options when using
the Latios Framework.

These are one of the two types of “Component Objects”.

## Struct IBufferElementData

Use this when you need multiple mutable instances of unmanaged data on an Entity
where all instances for a given entity can be processed by a single thread.

Dynamic Buffers are less efficient to iterate than struct `IComponentData` and
offer less flexibility than `ICollectionComponent` for splitting elements across
a parallel job. However, they are fast when the entire buffer only needs to be
touched by a single thread. They are especially powerful in filtered operations
such as when used in change filters or within an `IFindPairsProcessor`. Their
greatest power is that multiple instances can be touched within a single job.

## Struct ISharedComponentData

Use this when you want to group entities into chunks by a specific value.

The name can be a little misleading. This isn’t a mechanism for entities to
share a value. It is a mechanism to group entities which share a value. If you
aren’t grouping things for query filters or to solve specific ordering issues
like the Hybrid Renderer’s instanced draw commands, don’t use them.

## Blob Asset

Use this for complex large immutable data.

Blob Assets are immutable, so the data they contain is usually authored in the
editor or generated in external applications. While they can be used to share
data across multiple entities to reduce memory usage, doing so for
`IComponentData` under 128 bytes in size is typically not worth the effort and
may have a detrimental performance impact. Most people do not know how to
generate Blob Assets correctly when using the Game Object Conversion workflow (I
have not seen a single YouTube video get it right). And runtime generation of
Blob Assets is currently very unsafe.

With that said, when applied to the right applications, Blob Assets are very
powerful. The Latios Framework utilizes them for several applications and hides
all the conversion intricacies so you don’t have to worry about them. These
applications are (including unreleased tech): convex and compound colliders in
Psyshock, Myri’s audio clips with all the audio samples, Myri’s listener profile
which describes spatial regions and the audio filters to apply to them,
Kinemation’s mesh skinning weights and bone bounds, Kinemation’s optimized bone
hierarchy and bindposes, and MachAxle’s axes and action maps.

## ICleanup {ComponentData / SharedComponentData / BufferElementData}

Use these when you need to detect an entity is destroyed or want to prevent the
component’s value from being copied when instantiating new entities.

`ICleanup*` types are weird. They are powerful, but also seem to be in conflict
with the direction Unity is steering (although I doubt they will be deprecated).
Their purpose is for runtime use, where a system can identify that an entity is
destroyed and still access whatever is stored in the `ICleanup*` (if any) for
cleanup purposes. Myri uses this to detect destroyed listeners and detach them
from the DSPGraph.

Another interesting characteristic is that the component type is not added to
instantiated entities even if it exists on the source entity. In Kinemation, a
skeleton internally keeps track of all meshes bound to it for performance
reasons. But if a skeleton is instantiated, that list of dependents should not
be copied, otherwise the meshes would be bound to two skeletons at once. Even
though nothing really cares once the skeleton is destroyed, the type still uses
`ICleanup*` to achieve the desired behavior.

## Struct IManagedStructComponent – Latios Framework

Use this to store managed Unity Assets created/loaded at runtime on Entities.

Unity Assets like `GameObject` prefabs and `ScriptableObject`s can be loaded at
runtime from Resources, Addressables, or a custom mechanism. A common pattern is
to store a hash or `FixedString` in a struct `IComponentData` to load these
assets. But once loaded, something at runtime needs to store a reference to
them.

`IManagedStructComponent` allows aggregating multiple references into a struct
with normal ECS struct-like rules for getting and setting the references
(although mutating the referenced objects is a different matter). In addition,
these structs are not boxed and are instead stored in strongly-typed managed
storage, drastically reducing GC pressure. Access is still slow and managed, so
avoid them if you don’t need the Hybrid functionality.

## Struct ICollectionComponent – Latios Framework

Use this when a NativeContainer is accessed by multiple systems or when
NativeContainers need to be associated with entities.

`ICollectionComponent` works similarly to `IManagedStructComponent`, except
instead of storing Hybrid Assets (it can do that too by the way), it stores
NativeContainers. In addition, it keeps track of the containers’ `JobHandle`s
for automatic job dependency management. Long story short, if your
NativeContainer is not temporary nor private to the creating system, it should
probably be in a `ICollectionComponent`. If you are not convinced, watch the
Overwatch Team’s presentation on ECS and keep an ear out for the “Replay System
Woes”.

Most of the time, you will want to attach these to a blackboard entity. But
sometimes you may prefer to use these over a `DynamicBuffer`. The reason you
would want to do that is if you want to process a single entity’s container in a
parallel job. Unlike with `DynamicBuffer`s, you can do this without sync points.
You also get access to other container types, plus you can store multiple
containers in a single struct for better encapsulation.

Lastly, unlike `IManagedStructComponent`, `ICollectionComponent` can be used
inside a Burst-compiled `ISystem`.
