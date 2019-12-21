Latios Framework Core
=====================

Latios Core is the base framework of which nearly all of the Latios packages are
built upon. It provides generally useful extensions to the Unity.Entities
package.

Features
--------

### Bootstrap Tools

The bootstrap API got a lot better in Entities 0.2.0, but it still doesn’t let
you really customize the injection pipeline. That’s what the BootstrapTools
class is here for!

-   Inject systems based on namespaces

-   Build partial hierarchies

-   Build a playerloop with FixedUpdate (and set the FixedSimulationSystemGroup
    to the default injection group if you want)

-   Inject generic component types based on an interface constraint

### EntityDataCopyKit

Ever want to copy a component from one Entity to another using its
ComponentType, perhaps obtained by comparing two entities’ archetypes? I did. So
I wrote this. It uses evil reflection hacking magic to invade the ECS internals.
But it also caches what it does so hopefully no GC.

### Conditional System Updates

Unity does this thing where it tries to look at your EntityQueries and decide if
your system should update or not. While its certainly cute that Unity cares
about performance, you as the programmer can make much better decisions. Turn
off Unity’s logic with the [AlwaysUpdateSystem] attribute and turn on your own
by overriding ShouldUpdateSystem.

You can also use both mechanisms if Unity’s logic is not interfering but you
also want to further constrain the system to a specific scene or something. I do
this a lot.

You can also apply this logic to a SuperSystem (ComponentSystemGroup) to enable
or disable an entire group of systems.

### Global Entities

Unity’s solution to singletons is to create an EntityQuery every time you want a
new one (and ultimate create a new GC object) and also make every singleton
component live in its own 16 kB mansion. These singletons are so spoiled!

Well I am done spoiling singletons, so I asked myself:

Why do people write singletons? Is it because they only want one of something,
or is it really because they want most logic to find the same shared instance?

Well for me, it is the latter.

My solution is global entities. There are two of them: world and scene. The
worldGlobalEntity lives as long as the world does. The sceneGlobalEntity dies
and respawns when the scene changes. These two each get a 16 kB chunk for all of
their components, and they even have a special convenient API for getting and
setting components directly on them.

But the best part is that there’s no EntityQuery associated with them, so if you
want to make a hundred backups of these entities to track your state over time,
or have a component represent the “Active” option from a pool of entities
containing “viable” options, well you can do those things here.

Wait, no. That wasn’t the best part. The best part is the authoring workflow.
Simply attach a GlobalEntityData component to automatically have all the
components merged into one of the two global entities of your choice. You can
even set a merging strategy for them. This also works at runtime, so you can do
cool stuff like instantiate a new settings override from a prefab.

### Scene Management

Remember the good-old-days in MonoBehaviour land where by default when you
loaded a new scene, all the GameObjects of the old scene went away for you? That
was kinda nice, wasn’t it? It’s here too!

The rule is simple. If you want stuff to disappear, use scenes. If you want
stuff to be additive, use subscenes. And yes, having multiple scenes each with a
bunch of subscenes is totally supported and works exactly as you would expect it
to.

You can request a new scene using the RequestLoadScene component, and you can
get useful scene info from the CurrentScene component attached to the
worldGlobalEntity.

And if you want an entity to stick around (besides the worldGlobalEntity which
always sticks around), you can add the DontDestroyOnSceneChangeTag.

So now you can build your Mario Party clone, your multi-track racer, or your
fancy 5-scene credits system in ECS with the ease you had in MonoBehaviours. Oh,
and having most scenes be almost exclusively MonoBehaviours while a couple of
scenes are ECS is totally a thing you can do. For all you out there trying to
shoehorn ECS into your Mono game, shoehorn no more!

### Managed Struct Components

So you got some SOs or some Meshes and Materials or something that you want to
live on individual entities, but you don’t want to use Shared Components and
chop your memory and performance into gravel. You also don’t want to use class
IComponentData because that’s garbage every time you instantiate a new entity
and you know how to reference data on other entities using entities. Really, you
just want GC-free structs that can store shared references. You can’t use them
in jobs, but that’s not the concern.

Meet IComponent. It is a struct that can hold references. You can get and set
them using EntityManager.Get/SetManagedComponent and friends.

Just be careful with IComponent and their ICollectionComponent cousins. They are
aliens in disguise!

### Collection Components

Why in the world does Unity think that collections only belong on systems? Did
they not watch the Overwatch ECS talks?

Regardless, I added support for them. And you are going to need them if you want
to make use of all the crazy acceleration structures the other packages provide.

They do have this nice feature of automatically updating their dependencies if
you use them in a JobSubSystem (JobComponentSystem).

### Math

What framework would be complete without some math helpers? Not this one. Overly
used algorithms and some simd stuff are here. Help yourself!

Known Issues
------------

-   This stuff does not work with the DOTS Runtime. I rely on reflection in
    order to not fork the Entities package.

-   There’s a limit to how many generic components you can add at runtime before
    everything explodes. If you want to expand that limit, write a T4 script to
    generate hundreds of non-generic IComponentData types. Your compiler will
    hate you, but Unity might stop exploding. I’ll take dealing with one enemy
    over death any day.

-   IComponent and ICollectionComponent are not true components. Under the hood,
    I use generic components to modify the Entity archetypes. Expect them to not
    work with a lot of query and archetype sugar. I do try to make them blend in
    where I can though.

Near-Term Roadmap
-----------------

-   Example Scenes

-   General Purpose BurstPatcher

    -   Instead of generating code in the project, generate the assembly in
        IPostProcessPlayerScriptDLLs using [IgnoreAccessChecksTo] compatible
        compiler.

    -   Biggest risk is getting Burst to recognize this new assembly.

-   Fluent EntityQuery builder

    -   One that supports extensions and filters
