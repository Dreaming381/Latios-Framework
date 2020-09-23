# LatiosWorld in Detail

Latios.Core provides quite a few unique features, many of which depend on
`LatiosWorld` being the primary `World`. But why? What is actually going on?
While you could look at the code to find answers, that code is a little scary…

Ok, probably really scary. It’s been in a bit of flux with Unity’s DOTS updates.
Hopefully I will be able to clean it up in future releases.

Regardless, I will do my best to explain what `LatiosWorld` does and what
systems it automatically populates itself with and the roles they play.

## LatiosWorld Creation

When a `LatiosWorld` is created, it first creates instances of
`ManagedStructComponentStorage` and `CollectionComponentStorage`. As their
names imply, these objects store the collection components and managed struct
components that can be attached to entities. `CollectionComponentStorage` also
stores the `JobHandle`s and allocation flags with each collection component.

Second, it creates a cache of the collection component dependencies pending
update of an executing `SubSystem.Dependency`s final value.

Third, it injects generic types used by the reactive systems which track
collection and managed struct components into the `TypeManager` so that
Unity’s ECS recognizes them.

Fourth, it creates `sceneGlobalEntity` and `worldGlobalEntity`.

Finally, it creates the `LatiosInitializationSystemGroup`, the
`LatiosSimulationSystemGroup`, and the `LatiosPresentationSystemGroup` which
are subclasses of primary groups created by a non-`LatiosWorld`.

The `LatiosWorld` contains a couple of flags used for stopping and restarting
simulations of systems on scene changes. The `LatiosSimulationSystemGroup` and
`LatiosPresentationSystemGroup` simply check one of these flags in
`OnUpdate()` and conditionally execute the `base.OnUpdate()`. Otherwise,
they are identical to `SimulationSystemGroup` and `PresentationSystemGroup`
in functionality and behavior.

## System Creation in LatiosInitializationSystemGroup

The `LatiosInitializationSystemGroup` is the home to all Latios.Core systems.
It is by far the most frequently broken piece of code during DOTS updates.
Consequently, the system ordering and execution inside of
`LatiosInitializationSystemGroup` will likely change in future releases.

The following systems are created by the LatiosInitializationSystemGroup:

-   SceneManagerSystem – Triggers scene changes

-   MergeGlobalsSystem – Merges GlobalEntityData entities into the
    `sceneGlobalEntity` and `worldGlobalEntity`

-   DestroyEntitiesOnSceneChangeSystem – Destroys all entities whenever the
    scene changes

    -   This system does not use a normal OnUpdate method and may not show up in
        the Unity Editor

    -   This system creates a new sceneGlobalEntity instance after it destroys
        the old one

-   ManagedComponentsReactiveSystemGroup – This is a `SuperSystem` that
    creates multiple concrete instances of generic systems to react to each
    `IManagedComponent` and `ICollectionComponent` type. Due to `World`
    not properly implementing extendible `IDisposable`, this system group is
    also responsible for disposing of the `CollectionComponentStorage`
    belonging to the `LatiosWorld` inside of `OnDestroy()`. The generic
    systems this system creates are as follows:

    -   ManagedComponentCreateSystem\<T\>

    -   ManagedComponentDestroySystem\<T\>

    -   CollectionComponentCreateSystem\<T\>

    -   CollectionComponentDestroySystem\<T\>

-   LatiosSyncPointGroup – This is a temporary `ComponentSystemGroup` designed
    for application code. It will be removed in a future release.

## System Ordering in LatiosInitializationSystemGroup

Currently, the `LatiosInitializationSystemGroup orders itself as follows:

-   BeginInitializationEntityCommandBufferSystem

-   SceneManagerSystem

-   [First Injected System]

-   …

-   [Last Unity Injected System]

-   MergeGlobalsSystem

-   ManagedComponentReactiveSystemGroup

-   LatiosSyncPointGroup

-   [Remaining Injected Systems]

-   EndInitializationEntityCommandBufferSystem
