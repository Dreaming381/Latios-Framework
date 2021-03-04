# Getting Started with Latios.Core Framework

## Bootstrap

Latios Framework requires a custom bootstrap to inject its custom features into
the ECS runtime.

You can create one from one of the two templates using the Project folder’s
create menu and selecting *Latios-\>Bootstrap*.

For beginners, it is recommended to choose the *Injection Workflow* variant, as
this matches the default Unity behavior.

For those who prefer explicit ordering of systems, you may find the *Explicit
Workflow* more enticing. This workflow will automatically inject Unity systems,
but then allow you to inject only top-level systems which set up children
systems in a top-down manner. See [Super Systems](Super%20Systems.md) for more
info on this workflow.

## Common Types

-   [LatiosWorld](LatiosWorld%20in%20Detail.md) – a `World` subclass which
    contains the extra data structures and systems required for the framework’s
    features to work
-   [SubSystem](Sub-Systems.md) – a `SystemBase` subclass
-   [SuperSystem](Super%20Systems.md) – a `ComponentSystemGroup` subclass
-   [RootSuperSystem](Super%20Systems.md) – a `SuperSystem` subclass designed to
    be auto-injected into non-Latios `ComponentSystemGroup`s
-   [IManagedComponent](Collection%20and%20Managed%20Struct%20Components.md) – a
    struct type component that can store reference types
-   [ICollectionComponent](Collection%20and%20Managed%20Struct%20Components.md)
    – a struct type component that can store NativeCollection types and keeps
    associated dependencies
-   [BlackboardEntity](Blackboard%20Entities.md) – an Entity with extensions to
    apply `EntityManager` operations on it

### Components

-   [WorldBlackboardTag](Blackboard%20Entities.md) – A tag component attached
    exclusively to the `worldBlackboardEntity`. You may choose to exclude this
    tag in an `EntityQuery`. Do not add or remove this component!
-   [SceneBlackboardTag](Blackboard%20Entities.md) – Same as
    `WorldBlackboardTag` but for the `sceneBlackboardEntity`. Do not add or
    remove this component!
-   [BlackboardEntityData](Blackboard%20Entities.md) – A configuration component
    which tells the Latios Framework to merge the entity this component is
    attached to into one of the two global entities. You are free to add and
    modify these components in your code.
-   [DontDestroyOnSceneChangeTag](Scene%20Management.md) – A tag component which
    preserves the Entity when the scene changes (actual scene, not just a
    subscene). You may add and remove these at will.
-   [RequestLoadScene](Scene%20Management.md) – Request a scene (true scene, not
    a subscene) to be loaded. Add or replace these to your heart’s content.
-   [CurrentScene](Scene%20Management.md) – This component is attached to the
    `worldBlackboardEntity` and contains the current scene, previous scene, and
    whether the scene just changed. Do not add, set, or remove this component!

## Conventions

### Only one unique instance of a SubSystem

To understand why it is a bad idea to have multiple instances of the same type
of system (other than a `SuperSystem`), imagine you have a `PositionClampSystem`
that clamps the Translation component to a valid range. For performance reasons,
you specified a `ChangeFilter` on the `Translation` component so that you only
clamp values you did not clamp last frame.

Now what happens if you have two instances of `PositionClampSystem`? Well as
soon as the first instance sees a modified `Translation`, it writes to that
`Translation`. Then, the second instance sees that the `Translation` was
modified and also writes to it the clamped value. As the next frame rolls
around, the first `PositionClampSystem` sees that the very same `Translation`
was modified since the last time it ran. It was actually the second instance
that modified it, but the first instance doesn’t know that, so it modifies it
yet again. These two systems will ping-pong back and forth indefinitely, making
the `ChangeFilter` useless.

The correct solution to this problem is to instead have the same
`PositionClampSystem` update twice in the loop. That way, when the instance runs
the second time, it only compares changes to the first time it ran in the frame
rather than the previous frame. Likewise, the first time it runs in the frame,
it only compares changes to the second time it ran in the previous frame.
Ultimately, the behavior is as expected.

If using the *Bootstrap – Explicit Workflow*, you’ll find that this works
correctly out-of-the-box.

### Only one scene active at a time

I know, I know. You’ve always had multiple scenes loaded at runtime because of
conflicts and editor performance and all that. But with subscenes, there’s no
need for that anymore. You can have as many subscenes as you like, but you
should keep scenes separate so that when you swap scenes, the slate can be wiped
clean.
