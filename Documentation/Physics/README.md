Latios Physics
==============

Well… really more like a spatial query engine at this point, but whatever.

Why?
----

TL;DR – Unity.Physics and I don’t get along. We have different goals, and I
decided I wanted a custom solution tailor-fit for my goals.

*Rant as follows:*

I get asked this a lot. Why? Is Unity.Physics not good enough? Is Havok’s
solution not good enough?

Trust me, when you hear my use case, you may find yourself wondering how the
heck Unity.Physics is a good general-purpose design. Spoiler alert: It’s not.
It’s very targeted at the use cases Unity is targeting.

First, let me introduce you to the decisions that make no sense to me:

-   Collider shapes exist as immutable shared data structures

-   A simulation requires all steps to be ticked rather than allowing each step
    to be ticked manually by the user

-   Simulation callbacks are single-threaded

-   Physics is very decoupled from ECS, trashing the Transform system and using
    layers rather than the superior ECS query system; yet simultaneously, it is
    tightly coupled as colliders use blobs, RigidBody has an Entity field, and
    all of its authoring uses GameObjectConversion

Now, to understand why this is terrible for me, let me present to you a game
idea a friend of mine and I came up with during a game jam:

The game was an RTS mixed with a TPS. You played as a witch or wizard defending
a magical forest from a military force that was seeking to harvest the forest’s
energy. You and your tower defense animals shot zany spells and spastic
projectiles at the enemy forces. You as the player needed to manage resources
and protect your land while simultaneously moving on foot to capture the enemy
military bases. Oh, and the spells could have all sorts of weird effects
including scaling things.

So let’s examine how Unity.Physics stacks up:

-   First and foremost, we need a Physics solution. The TPS mechanics of
    shooting and interacting with the world require the precision of real
    physics and not some weak position checks.

-   We need a mostly static environment for our characters and projectiles to
    traverse. Unity.Physics handles that perfectly well.

-   We need colliders on animated characters. Actually, we are still good here
    too. Even though Unity.Physics trashes the hierarchy, it doesn’t trash the
    LinkedEntityGroup, which means we can still instantiate a prefab with all of
    the individual colliders and reference them to drive them with bones.

-   We need morphing collider shapes. Most of this is just going to be scaling
    spheres based on an animation curve, but we need this. Currently this means
    we need to allocate, destroy, and reallocate a BlobAsset every frame while
    simultaneously making sure that the colliders are not shared. This is not
    performant.

-   Most of our game logic is going to be detecting if two colliders
    intersected. We want to know if our friendly projectiles hit the enemies, if
    the enemy fire hits us, if any projectile hits terrain, if two different
    spells hit each other, ect. Different collisions require different logical
    responses. And well, Unity.Physics at first looks like it should handle this
    fine, but this is actually where it falls flat on its face the hardest.
    Allow me to explain.

So the first issue is we need to tell Unity what collides with what. Right now
we know that based on EntityQueries, because we are using Tags and unique
component types for all of our other logic. But now we need to encode all of
that into a layer system. While annoying, it is doable.

So now we told Unity what collides with what, and it generates a NativeStream to
be processed in an ITriggerEventsJob. Except, all of our resulting pairs are
mixed together like a jar of jelly beans, which means that each unique event
handler needs to filter through the results and pick out the ones it cares for.
That’s a lot of iterating through the events. We can use the new EntityQueryMask
to speed this up, but still, it isn’t great. Instead, we might decide to bring
our iteration count back down by having one job do all the filtering for all the
different handlers to react to by creating a bunch of smaller collections of
pairs. This works, but now the global filterer needs to know about every kind of
pair interaction. You can try to generalize this, but it is just clunky. And
also, this mega-filter algorithm has to run single-threaded. So this is a pretty
bad bottleneck and simultaneously is tangling all our code together in stupid
ways.

So already this is not great, but it only gets worse. Let’s suppose that we
decide to drive our moving objects using physics, but we run into issues with
the contacts and some of the solvers and how they behave with the military
vehicles and the character controllers. Well guess what? Remember that song and
dance we had to play to sort all of your collision events to the proper handlers
from a single thread? We have to do the same thing again. Except here we don’t
care about most of the events, but we still have to sift through them all. It is
a totally unnecessary price to pay. And that applies to a lot more use cases
than the one I am presenting here.

What if I wanted to drive the colliders to spin in a circle around a character
but still be individually destroy-able (think boss shields or Mario Kart’s
triple shells in the newer games)? I have to set up everything with joints
rather than rely on the Transform System with a rotating parent.

What if I wanted to destroy an otherwise static rock from an explosive? Well now
the entire static world needs to be rebuilt.

What if I am trying to get sparks to bounce off a flash mob of vocaloids which
need a dynamically updated mesh collider every frame? That cooker is expensively
slow!

What if I am building a platformer and want to mimic the expanding and
contracting mushrooms from the New Super Mario Bros games? Do I create a
collider for every frame for those too? Or do I prebake the colliders for every
step in the cycle?

What if I am procedurally generating a world and want to apply scale variations
to the instantiated prefabs? More slow cooking.

More often than not, I find myself fighting with Unity.Physics (and
Havok.Physics) thinking backwards about my problems trying to not pay the cost
for things I don’t need.

So I presented my concerns on the forums, and then decided that if I wanted it
done right, I was going to have to do it myself. I’m definitely not there yet,
but I am comfortable and confident in the direction I am headed.

Features
--------

*Disclaimer: Some of the details of the features listed in this section are not
available yet but are planned for a near future release and have been heavily
accounted for in the design. If a particular feature shows promise in solving a
use case you are currently struggling with, please let me know so that I can
prioritize it. Solving other people’s problems seems to give me some
productivity buff. I don’t know. It’s probably some kind of fairy magic.*

### Mutable Colliders

Instead of making the Collider component a pointer to an immutable asset, I put
the mutable properties directly into the Collider component. Don’t worry. You
don’t have to query for every SphereCollider, CapsuleCollider, BoxCollider,
TriangleCollider, ect. I use a union to pack the different types together. The
primitive colliders fit entirely in the component, which makes sense because
those are the colliders you are going to want to mutate every which way possible
anyways. The more complex colliders may expose some readily tweakable parameters
but also rely on Blobs or Entity references to an Entity with dynamic buffers
and stuff. So per-frame animated (or simulated like cloth) mesh colliders are
not only more accessible but more intuitive in the DOTS ecosystem.

One might argue that copying large collider components (it is still currently
smaller than LocalToWorld) is quite a bit more expensive than copying pointers.
But given how many other performance-hogging decisions Unity.Physics makes, I
will take this trade any day.

As a bonus, Latios Colliders are constructable and copyable on the stack. That
makes them a lot more pleasant to work with.

### Transform Hierarchy Support

The physics algorithms apply a local to world space conversion when capturing
data from entities. They will also support world to local space conversion on
simulation write back when simulation is supported.

A PhysicsScale component is also present for handling scaled colliders. There is
an algorithm to update PhysicsScale from the transform hierarchy’s scale
components. (Future release, because the API is broken atm)

### Infinite Layers

So fun fact, Unity.Physics default BuildPhysicsWorldSystem doesn’t have one
broadphase structure. It has two. One for statics, and one for dynamics. It
performs a Bipartite check between the two. So if Bipartite checks are cheaper
than building the static world broadphase every frame, is there a reason why we
don’t just build a broadphase structure for each group of colliders we care
about?

That was the question I asked myself, and then I said, “no, there is not”.

So instead of building two large broadphase structures with layer masks and then
untangling the results, you can build a CollisionLayer per unique EntityQuery
(or from arbitrary data from a job). Then you can ask for all collisions within
a single layer or for the collisions between layers.

### Multibox Broadphase

What is so good about a multibox broadphase? Well for one, there’s no
single-threaded initial step, which is one of Unity.Physics bottlenecks. But
second, it’s a cheap pseudo-islanding solution.

And I know what you are wondering. How is that useful?

In Latios.Physics, you call Physics.FindPairs and pass in one or two collision
layers as arguments. However, there is an additional generic argument requesting
an IFindPairsProcessor. This is the struct that will have your NativeContainers
and stuff to handle the results.

Physics.FindPairs can be scheduled as a parallel job, and when you do, I
guarantee that both entities passed into the IFindPairsProcessor.Execute are
**thread-safe writable** to any of their components!

Do you know how many people have asked about how to write in parallel to nearby
enemies that should be damaged by an attack or some similar problem? Well here
you just have to create a trigger collider with a large radius and run its layer
against the layer with your enemies. Damage will stack as expected. Or you can
set a bool that says future interactions with that enemy can’t happen anymore.
And at the same time you can do stuff on the attack trigger too, like record all
the enemies it touched into a dynamic buffer.

It’s thread-safe. It’s parallel. It’s magic!

Ok, not entirely. You can still shoot yourself in the foot by trying to access
an entity that isn’t one of the pair’s entities. That includes trying to access
a parent entity, which is a common use case. You’ll have to be smart about how
you go about that problem, or just avoid it.

But even more importantly, you must ensure that **no entity can appear twice in
a participating CollisionLayer!** I have no automatic way to check for this, so
if you violate this rule you will silently generate race conditions. If that’s a
problem for you, use ScheduleSingle instead.

There’s still one more awesome thing about FindPairs. It is a broadphase. It
reports AABB intersection pairs, not true collider pairs. Why is that a good
thing? It means you get to choose what algorithm to follow up with.

-   Need all the contact manifold info?

    -   Cool!

-   Just need the penetration and normal to separate them?

    -   You can do that too.

-   Do you want a cheap intersection check to keep the cost down?

    -   That’s a possibility as well.

-   Do you want to check if the entities have some other component values before
    you do the expensive intersection checks?

    -   Also viable.

-   How about firing a bunch of raycasts between the two colliders to build a
    bunch of buoyancy force vectors?

    -   Actually, yes. It is pretty similar to an IJobParallelFor in that
        regard.

And remember, you can use a different IFindPairsProcessor for each FindPairs
call.

### Static Queries

No OOP-like API here. All the queries are static methods in the static Physics
class. Combined with the stack-creatable colliders, these methods can be quite
useful for “What if?” logic.

Also, you might see some more non-traditional queries pop up at some point if I
find myself want parabolic and smoothstep trajectories again.

### Immediate Mode Design

Nearly the entirety of Latios Physics is composed of data, data structures, and
stateless static methods. There’s no state. There’s no “StepTheSimulation”
method (and if there ever becomes one, it will just be a convenience method
using public API). There’s no systems (other than authoring).

Even the debug tools are static!

Unity.Physics first and foremost tries to be an out-of-the-box solution and then
slowly is working on exposing flexibility.

In constrast, Latios Physics tries to be a “Build your own solution” framework
and is being designed from inside-out. Over time, it will eventually achieve an
out-of-the-box status, but that is not the focus. The focus is flexibility with
the luxury of drop-in optimized and accurate algorithms and authoring tools.

Known Issues
------------

-   The first release is missing quite a few collider shapes, CollisionLayer
    construction methods, and FindPairs features. These are coming soon!

-   CollisionLayer construction API is clunky. I plan to revisit it after the
    Core’s fluent query builder is complete.

-   PhysicsScale is not added when authoring a scaled collider. Instead, the
    collider’s scale is baked in during conversion. This will be fixed with the
    custom authoring components.

-   By default, FindPairs jobs will not run with Burst in a built player. A
    temporary workaround is provided in the Getting Started page. A more
    streamlined solution will arrive once the Core’s general BurstPatcher is
    complete.

-   Composite Transform components are not currently supported.

-   FindPairs is not fully optimized. I’m using a single-axis pruner because of
    both familiarity and wanting to see how far I can push the algorithm with
    Burst and easily iterate-able data structures. I still need to implement
    inner loop single load, sign-flip safe compares, and sentinel loops. I may
    then experiment with a dual-axis pruner or a port of Unity’s BVH to multibox
    and the alternate data representation.

-   Authoring is weak right now. That stuff takes me a while to get working.

-   This Readme has too many words and not enough pictures.

Near-Term Roadmap
-----------------

-   Collision Layer Builders Overhaul

    -   Build single-threaded and from in a job

    -   Build from arrays directly

    -   Generate remap array (so that custom containers can be sorted too)

    -   Use Core’s fluent query builder

    -   Make CollisionLayerSettings more fluent

-   Optimize FindPairs more

-   Support Streams in FindPairs

    -   Cache and replay pairs

    -   Apply custom pair filtering

    -   Custom streams

    -   Indexing items in CollisionLayers

-   More Collider Shapes

    -   Box, Triangle, and Quad just need a fast EPA

    -   Develop the fastest Capsule vs Box algorithm ever (seriously, I haven’t
        seen any one try the approach I am attempting which I expect to only be
        slightly more expensive than capsule vs capsule)

    -   Terrain, Convex Hull, Static Mesh, and Dynamic Mesh

-   Raycasting CollisionLayers

    -   Any hit, first hit, and multi-hit with IRaycastLayerProcessor

    -   Batch structures

-   Point Queries

-   Collider Casts

    -   Using optimized CCD polynomial rootfinding algorithms

-   Simplified Overlap Queries

-   Make Collider type a true union with LayoutKind.Explicit

    -   Allow in and ref argument passing optimizations

    -   Allow manipulating the Collider data directly using the specialized type
        as a ref

    -   Hopefully help Burst be more aggressive at optimizing

-   New Authoring Components

    -   Autofitting

    -   Scale baking options

-   More Debug Tools

    -   Support checks with multiple Collision Layers

    -   Sanity check unique Entities

    -   Got any more debug view ideas? Let me know!

Not-So-Near-Term
----------------

-   Simulations

-   Dual axis broadphase

-   BVH broadphase

-   2D
