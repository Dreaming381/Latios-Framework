# Latios Framework Core

Latios.Core offers a toolbox of extensions to the Unity.Entities package. It
does not abstract away ECS nor provide an alternative ECS. Many Latios packages
depend on Latios.Core.

Check out the [Getting Started](Getting%20Started.md) page!

## Features

### Bootstrap Tools

The bootstrap API has improved quite a bit over time, but it still doesn’t let
you really customize the injection pipeline. That’s what the `BootstrapTools`
class is here for!

-   Inject systems based on namespaces

-   Build partial hierarchies

-   Build a playerloop with custom preset loops (like one with classical
    FixedUpdate or one with Rendering before Simulation)

-   Inject generic component types based on an interface constraint (Going away
    soonish)

See more: [Customizing the Bootstraps](Customizing%20the%20Bootstraps.md)

### Explicit Top-Down Hierarchical System Ordering

Do the `[UpdateBefore/After]` attributes confuse you? Would you rather just
see the systems ordered explicitly in your code within the group as they do in
the editor windows? Would you like to decouple the systems so that they don’t
reference each other? Well there’s a new way to order systems in the framework.
You can specify the system order explicitly using the `GetOrCreateAndAdd` API
in `SuperSystem.CreateSystems()`. It is opt-in so if you prefer the injection
approach you can do things that way too.

See more: [Super Systems](Super%20Systems.md)

### EntityDataCopyKit

Ever want to copy a component from one Entity to another using its
`ComponentType`, perhaps obtained by comparing two entities’ archetypes? I
did. So I wrote this. It uses evil reflection hacking magic to invade the ECS
internals. But it also caches what it does so hopefully no GC.

### Conditional System Updates

Unity does this thing where it tries to look at your EntityQueries and decide if
your system should update or not. While it’s certainly cute that Unity cares
about performance, you as the programmer can make much better decisions. Turn
off Unity’s logic with the [AlwaysUpdateSystem] attribute and turn on your own
by overriding ShouldUpdateSystem.

You can also use both mechanisms if Unity’s logic is not interfering but you
also want to further constrain the system to a specific scene or something. I do
this a lot.

You can also apply this logic to a `SuperSystem` (`ComponentSystemGroup`) to
enable or disable an entire group of systems.

See more: [Super Systems](Super%20Systems.md)

### Global Entities

Unity’s solution to singletons is to create an `EntityQuery` every time you
want a new one and also make every singleton component live in its own 16 kB
mansion. These singletons are so spoiled!

Well I am done spoiling singletons, so I asked myself:

Why do people write singletons? Is it because they only want one of something,
or is it really because they want most logic to find the same shared instance?

Well for me, it is the latter.

My solution is global entities. There are two of them: world and scene. The
`worldGlobalEntity` lives as long as the world does. The `sceneGlobalEntity`
dies and respawns when the scene changes. These two each get a 16 kB chunk for
all of their components, and they even have a special convenient API for getting
and setting components directly on them.

But the best part is that there’s no `EntityQuery` associated with them, so if
you want to make a hundred backups of these entities to track your state over
time, or have a component represent the “Active” option from a pool of entities
containing “viable” options, well you can do those things here.

Wait, no. That wasn’t the best part. The best part is the authoring workflow!
Simply attach a `GlobalEntityData` component to automatically have all the
components merged into one of the two global entities of your choice. You can
even set a merging strategy for them. This also works at runtime, so you can do
cool stuff like instantiate a new settings override from a prefab.

See more: [Global Entities](Global%20Entities.md)

*Feedback Request: I am looking for a better term than “global” to describe
these entities. “World” + “Global” sounds redundant and simultaneously confusing
when the “Scene” variant comes into play.*

### Scene Management

Remember the good-old-days in `MonoBehaviour` land where by default when you
loaded a new scene, all the `GameObject`s of the old scene went away for you?
That was kinda nice, wasn’t it? It’s here too!

The rule is simple. If you want stuff to disappear, use scenes. If you want
stuff to be additive, use subscenes. And yes, having multiple scenes each with a
bunch of subscenes is totally supported and works exactly as you would expect it
to *(hopefully)*.

You can request a new scene using the `RequestLoadScene` component, and you
can get useful scene info from the `CurrentScene` component attached to the
`worldGlobalEntity`.

If you want an entity to stick around (besides the `worldGlobalEntity` which
always sticks around), you can add the `DontDestroyOnSceneChangeTag`.

Now you can build your Mario Party clone, your multi-track racer, or your fancy
5-scene credits system in ECS with the ease you had in `MonoBehaviour`s. Oh,
and having most scenes be almost exclusively `MonoBehaviour`s while a couple
of scenes use ECS is totally a thing you can do, especially since you can check
`CurrentScene` inside `ShouldUpdateSystem`. For all you out there trying to
shoehorn ECS into your Mono game, shoehorn no more!

See more: [Scene Management](Scene%20Management.md)

### Collection Components

Why in the world does Unity think that collections only belong on systems? Did
they not watch the Overwatch ECS talks?

All joking aside, they support them in two different ways:

-   Class components implementing `IDisposable`, which works but allocates GC

-   Unsafe collections which you have to be extra careful using

I wasn’t really satisfied with either of these solutions, so I made my own. They
are structs that implement `ICollectionComponent`.

**Warning: Managed components and collection components are not real components.
There are some special ways you need to work with them.**

They do not affect the archetype. Instead, they “follow” an existing component
you can specify as the `AssociatedComponentType`. Whenever you add or remove
the `AssociatedComponentType`, a system will add or remove the collection
component in the `ManagedComponentReactiveSystemGroup` which runs in
`LatiosInitializationSystemGroup`.

The typical way to iterate through these collection components is to use

```csharp

Entities.WithAll\<{AssociatedComponentType}\>().ForEach((Entity)
=\>{}).WithoutBurst().Run();

```

You can then access the collection component using the `EntityManager`
extensions.

Collection components have this nice feature of automatically updating their
dependencies if you use them in a `SubSystem` (`SystemBase`).

See more: [Collection and Managed Struct Components](Collection%20and%20Managed%20Struct%20Components.md)

### Managed Struct Components

So you got some SOs or some Meshes and Materials or something that you want to
live on individual entities, but you don’t want to use Shared Components and
chop your memory and performance into gravel. You also don’t want to use class
`IComponentData` because that’s garbage every time you instantiate a new
entity and you know how to reference data on other entities using Entity fields.
Really, you just want GC-free structs that can store shared references. You
can’t use them in jobs, but that’s not the concern.

Meet `IManagedComponent`. It is a struct that can hold references. You can get
and set them using `EntityManager.Get/SetManagedComponent` and friends. They
work essentially the same as `ICollectionComponent` except without the
automatic dependency management because there’s no dependencies to manage.

See more: [Collection and Managed Struct Components](Collection%20and%20Managed%20Struct%20Components.md)

### Math

What framework would be complete without some math helpers? Not this one.
Overly-used algorithms and some SIMD stuff are here. Help yourself!

See more: [Math](Math.md)

### Fluent Queries

Fluent syntax for expressing EntityQueries was a big improvement. However, every
iteration so far has lacked a way to extend it. This implementation not only
provides clean Fluent syntax for building EntityQueries, but it is also
extensible so that library authors can write patch methods for their
dependencies.

Fluent Queries have a concept of “weak” requests, or requests that can be
overridden by other expressions. For example, an extension method may request a
weak readonly `Translation`. If a `Translation` request already exists
(readonly or readwrite), the weak request will be ignored.

There are similar mechanisms for handling “Any” requests and “Exclude” requests.

See more: [Fluent Queries](Fluent%20Queries.md)

### Burst Patcher for Generic Jobs

It is not perfect. It does not work with generic systems. But I have a mechanism
for getting Burst to compile generic jobs constrained by an interface when
performing an AOT build (building a player). It works for private interface
implementations as well as private generic jobs.

To make a generic job compile with Burst for AOT, add the
`BurstPatcherAttribute` and give it the name of the interface to generate jobs
for. You still need to add `[BurstCompile]`.

On any interface implementation, you can add `[IgnoreBurstPatcher]` to prevent
the job from being compiled with Burst.

## Known Issues

-   This package does not work with the DOTS Runtime. I rely on reflection in
    order to not fork the Entities package.

-   There’s a limit to how many generic components you can add at runtime before
    everything explodes. If you want to expand that limit, write a T4 script to
    generate hundreds of non-generic `IComponentData` types. Your compiler
    will hate you, but Unity might stop exploding. I’ll take dealing with one
    enemy over death any day.

-   `IManagedComponent` and `ICollectionComponent` are not true components.
    Under the hood, I use generic components to modify the Entity archetypes.
    Expect them to not work with a lot of query and archetype sugar. I do try to
    make them blend in where I can though.

-   `IManagedComponent` and `ICollectionComponent` do not save in subscenes.

-   Burst Patcher jobs do not always show up in the Burst Inspector.

## Near-Term Roadmap

-   Smart sync point management

    -   Fetch ECBs from “sync points” rather than `EntityCommandBufferSystems`

    -   Automatically provide the ECB its dependencies similar to
        `ICollectionComponent`

    -   Specialized CommandBuffer types which use fast paths when you know what
        you need to write in advance

-   Codegen generic components

-   Optimized transform hierarchy types and systems

    -   Static parents

    -   Partially static hierarchies

-   Burst-Patcher Burst Inspector support

-   World configuration settings

-   Improved collection components

    -   Default initialization interface

    -   Dependency backup/restore for Entities.ForEach

    -   Get as ref

    -   Conversion and serialization

-   Profiling tools

    -   Port and cleanup from Lsss

-   Reflection-free refactor

    -   For Tiny support

-   Safe blob management

-   Custom Lambda Code-gen

    -   If I am feeling really, really brave…

-   More Examples

-   More docs
