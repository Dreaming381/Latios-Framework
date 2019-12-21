Getting Started with Latios.Core Framework
==========================================

Bootstrap
---------

Latios Framework requires a custom bootstrap to inject its custom features into
the ECS runtime.

You can create one from a template using the Project folder’s create menu and
selecting Latios-\>Bootstrap.

When you run your game, you may notice that only Unity’s systems and a couple
Latios systems are in the player loop. That’s because by default the Bootstrap
created from the template only injects Unity systems, and the LatiosWorld added
the other systems.

If you would like to inject all your pre-existing systems, you can add a call to
BootstrapTools.InjectSystemsFromNamespace() with your project’s root namespace.

However, the alternative approach to designing a loop is to create a
RootSuperSystem and add SuperSystems, SubSystems, and JobSubSystems in its
OnCreateSystems method. This lets you explicitly and hierarchically define your
player loop with respect to Unity’s systems as well as optional 3rd party
systems.

Common Types
------------

-   Latios World – a type of World which contains the extra data structures and
    systems for the framework to work.

-   SubSystem – a ComponentSystem subclass

-   JobSubSystem – a JobComponentSystem subclass

-   SuperSystem – a ComponentSystemGroup subclass

-   RootSuperSystem – a SuperSystem subclass designed to be auto-injected into
    non-Latios ComponentSystemGroups

-   IComponent – a struct type component that can store reference types

-   ICollectionComponent – a struct type component that can store
    NativeCollection types and keeps associated dependencies.

-   ManagedEntity – an Entity with extensions to apply EntityManager operations
    on it. It is used for the globalEntities.

Conventions
-----------

### Only one unique instance of a SubSystem or JobSubSystem

To understand why it is a bad idea to have multiple instances of the same type
of system (other than a SuperSystem), imagine you have a PositionClampSystem
that clamps the Translation component to a valid range. For performance reasons,
you specified a ChangeFilter on the Translation component so that you only clamp
values you did not clamp last frame.

Now what happens if you have two instances of PositionClampSystem? Well as soon
as the first instance sees a modified Translation, it writes to that
Translation. Then, the second instance sees that the Translation was modified
and also writes to it the clamped value. As the next frame rolls around, the
first PositionClampSystem sees that the very same Translation was modified since
the last time it ran. It was actually the second instance that modified it, but
the first instance doesn’t know that, so it modifies it yet again. These two
systems will ping-pong back and forth indefinitely, making the ChangeFilter
useless.

The correct solution to this problem is to instead have the same
PositionClampSystem update twice in the loop. That way, the second time the
instance runs, it only compares changes to the first time it ran in the frame
rather than the previous frame. Likewise, the first time it runs in the frame,
it only compares changes to the second time it ran in the previous frame.
Ultimately, the behavior is as expected.

### Only one scene active at a time

I know, I know. You’ve always had multiple scenes loaded at runtime because of
conflicts and editor performance and all that. But with subscenes, there’s no
need for that anymore. You can have as many subscenes as you like, but you
should keep scenes separate so that when you swap scenes, the slate can be wiped
clean.
