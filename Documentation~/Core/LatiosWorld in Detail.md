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

For NetCode projects, it instead creates the appropriate Client and Server
variants. This is dictated by the `WorldRole` argument in the `LatiosWorld`
constructor.

These systems have slightly modified behavior to support additional features.

## System Creation in LatiosInitializationSystemGroup

The `LatiosInitializationSystemGroup` is the home to all Latios.Core systems.

The following systems are created by the `LatiosInitializationSystemGroup`:

-   [PreSyncPointGroup](Custom%20Command%20Buffers%20and%20SyncPointPlaybackSystem.md)
    – This is a `ComponentSystemGroup` designed for systems which schedule jobs
    asynchronous to the sync point.
-   [SyncPointPlaybackSystem](Custom%20Command%20Buffers%20and%20SyncPointPlaybackSystem.md)
    – This is a system capable of playing back command buffers which perform ECS
    structural changes
-   [MergeBlackboardsSystem](Blackboard%20Entities.md) – Merges
    `BlackboardEntityData` entities into the `sceneBlackboardEntity` and
    `worldBlackboardEntity`
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
-   SyncPointPlaybackSystem
-   BeginInitializationEntityCommandBufferSystem
-   SceneManagerSystem (if installed)
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

When a `LatiosWorld` is created, it first scans the list of unmanaged systems
and generates generic classes for `ISystemShouldUpdate` and `ISystemNewScene`.
It also injects generic types used by the reactive systems which track
collection and managed struct components into the `TypeManager` so that Unity’s
ECS recognizes them, assuming they haven’t been injected already.

Second, it creates a `LatiosWorldUnmanagedSystem`, which is an unmanaged system
that does not update but rather governs the lifecycle of the
`LatiosWorldUnmanagedImpl`. The impl creates instances of
`ManagedStructComponentStorage` and `CollectionComponentStorage`. As their names
imply, these objects store the collection components and managed struct
components that can be attached to entities. `CollectionComponentStorage` also
stores the `JobHandle`s with each collection component.
`ManagedStructComponentStorage` is lazily initialized to ensure it is
initialized in a non-Burst-compiled context. `CollectionComponentStorage` can be
initialized fully within Burst.

Third, it creates `worldBlackboardEntity`.

Fourth, it creates a cache of the collection component dependencies pending
update of an executing `SystemState.Dependency`’s final value. The cache is
stored in the impl.

Finally, it creates the `LatiosInitializationSystemGroup`, the
`LatiosSimulationSystemGroup`, and the `LatiosPresentationSystemGroup` which are
subclasses of primary groups created by a non-`LatiosWorld`.

The `LatiosWorld` contains a couple of flags used for stopping and restarting
simulations of systems on scene changes. The `LatiosSimulationSystemGroup` and
`LatiosPresentationSystemGroup` check one of these flags in `OnUpdate()` and
conditionally execute the `base.OnUpdate()`. This behavior is only used when the
Scene Manager is installed. Otherwise, the `sceneBlackboardEntity` is created on
the first run of `LatiosInitializeSystemGroup`.

The `LatiosWorld` also contains the public `useExplicitSystemOrdering` flag
which tells `SuperSystem`s if they should enable system sorting by default. This
is used by the bootstrap templates to set the appropriate workflow. However, a
`SuperSystem` may override this setting for itself in `CreateSystems()` by
setting the `EnableSystemSorting` flag.

And lastly, The `LatiosWorld` contains a public `zeroToleranceForExceptions`
flag which will automatically stop all system execution when an exception is
caught by one of the Latios Framework system dispatchers.
