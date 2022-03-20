# LatiosWorld

Latios.Core provides quite a few unique features, many of which depend on
`LatiosWorld` being the primary `World`. But why? What is actually going on?
While you could look at the code to find answers, that code is a little scary…

Ok, probably really scary. It’s been in a bit of flux with Unity’s DOTS updates.
Hopefully it will continue to improve with each release.

Regardless, I will do my best to explain what `LatiosWorld` does and what
systems it automatically populates itself with and the roles they play.

## Default Systems

`LatiosWorld` automatically creates several top-level systems from within its
constructor:

-   LatiosInitializationSystemGroup – subclasses `InitializationSystemGroup`
-   LatiosSimulationSystemGroup – subclasses `SimulationSystemGroup`
-   LatiosPresentationSystemGroup – subclasses `PresentationSystemGroup`

## System Creation in LatiosInitializationSystemGroup

The `LatiosInitializationSystemGroup` is the home to all Latios.Core systems.

The following systems are created by the `LatiosInitializationSystemGroup`:

-   [PreSyncPointGroup](Custom%20Command%20Buffers%20and%20SyncPointPlaybackSystem.md)
    – This is a `ComponentSystemGroup` designed for systems which schedule jobs
    asynchronous to the sync point.
-   [SyncPointPlaybackSystem](Custom%20Command%20Buffers%20and%20SyncPointPlaybackSystem.md)
    – This is a system capable of playing back command buffers which perform ECS
    structural changes
-   [SceneManagerSystem](Scene%20Management.md) – Triggers scene changes and
    initializes `sceneBlackboardEntity` in play mode.
-   [MergeBlackboardsSystem](Blackboard%20Entities.md) – Merges
    `BlackboardEntityData` entities into the `sceneBlackboardEntity` and
    `worldBlackboardEntity`
-   [DestroyEntitiesOnSceneChangeSystem](Scene%20Management.md) – Destroys all
    entities whenever the scene changes
    -   This system does not use a normal `OnUpdate()` method and may not show
        up in the Unity Editor
    -   This system creates a new `sceneBlackboardEntity` instance after it
        destroys the old one
-   [ManagedComponentsReactiveSystemGroup](Collection%20and%20Managed%20Struct%20Components.md)
    – This is a `RootSuperSystem` that creates multiple concrete instances of
    generic systems to react to each `IManagedComponent` and
    `ICollectionComponent` type. Due to `World` not properly implementing
    extendible `IDisposable`, this system group is also responsible for
    disposing of the `CollectionComponentStorage` belonging to the `LatiosWorld`
    inside of `OnDestroy()`. The generic systems this system creates are as
    follows:
    -   ManagedComponentCreateSystem\<T\>
    -   ManagedComponentDestroySystem\<T\>
    -   CollectionComponentCreateSystem\<T\>
    -   CollectionComponentDestroySystem\<T\>
-   LatiosWorldSyncGroup – This is a `ComponentSystemGroup` designed for
    application code. Its purpose is to provide a safe location in
    `LatiosInitializationSystemGroup` for application code to execute. Since
    many systems in `LatiosInitializationSystemGroup` generate sync points, it
    is common to place systems that induce sync points here and treat
    `LatiosInitializationSystemGroup` as a mega sync point.

## System Ordering in LatiosInitializationSystemGroup

Currently, the `LatiosInitializationSystemGroup` orders itself as follows:

-   PreSyncPointGroup
-   BeginInitializationEntityCommandBufferSystem
-   SyncPointPlaybackSystem
-   SceneManagerSystem
-   [End OrderFirst region]
-   …
-   [Unity SceneSystemGroup or ConvertToEntitySystem, whichever is latter]
-   LatiosWorldSyncGroup
    -   MergeBlackboardsSystem
    -   ManagedComponentReactiveSystemGroup
    -   [End OrderFirst region]
-   [Remaining Injected Systems]
-   EndInitializationEntityCommandBufferSystem

## LatiosWorld Creation in Detail

When a `LatiosWorld` is created, it first creates instances of
`ManagedStructComponentStorage` and `CollectionComponentStorage`. As their names
imply, these objects store the collection components and managed struct
components that can be attached to entities. `CollectionComponentStorage` also
stores the `JobHandle`s and allocation flags with each collection component.

Second, it scans the list of unmanaged systems and generates generic classes for
`ISystemShouldUpdate` and `ISystemNewScene`. It also creates an unmanaged system
providing access to the blackboard entities from a Burst `ISystem`.

Third, it creates a cache of the collection component dependencies pending
update of an executing `SubSystem.Dependency`’s final value.

Fourth, it injects generic types used by the reactive systems which track
collection and managed struct components into the `TypeManager` so that Unity’s
ECS recognizes them.

Fifth, it creates `sceneBlackboardEntity` and `worldBlackboardEntity`.

Finally, it creates the `LatiosInitializationSystemGroup`, the
`LatiosSimulationSystemGroup`, and the `LatiosPresentationSystemGroup` which are
subclasses of primary groups created by a non-`LatiosWorld`.

The `LatiosWorld` contains a couple of flags used for stopping and restarting
simulations of systems on scene changes. The `LatiosSimulationSystemGroup` and
`LatiosPresentationSystemGroup` simply check one of these flags in `OnUpdate()`
and conditionally execute the `base.OnUpdate()`. Otherwise, they are identical
to `SimulationSystemGroup` and `PresentationSystemGroup` in functionality and
behavior.

The `LatiosWorld` also contains the public `useExplicitSystemOrdering` flag
which tells `SuperSystem`s if they should enable system sorting by default. This
is used by the bootstrap templates to set the appropriate workflow. However, a
`SuperSystem` may override this setting for itself in `CreateSystems()` by
setting the `EnableSystemSorting` flag.
