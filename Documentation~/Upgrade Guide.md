# Upgrade Guide [0.2.2] [0.3.0]

Please back up your project in a separate directory before upgrading! You will
likely need to reference the pre-upgraded project during the upgrade process.

## Core

### Bootstrap

The bootstrap requirements have been changed, and it is recommended you delete
your old bootstraps and create new ones.

If using a modified *Bootstrap – Explicit Workflow*, you must add the following
to your bootstrap:

~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
var world                             = new LatiosWorld(defaultWorldName);
World.DefaultGameObjectInjectionWorld = world;
world.useExplicitSystemOrdering       = true;  // Add this line! 
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

If using a modified *Bootstrap- Injection Workflow*, you no longer must remove
all systems in the `Latios` namespace. You only need to remove the following
from the list:

-   LatiosInitializationSystemGroup
-   LatiosSimulationSystemGroup
-   LatiosPresentationSystemGroup
-   InitializationSystemGroup
-   SimulationSystemGroup
-   PresentationSystemGroup

In either workflow, systems generated during the construction of `LatiosWorld`
now use attribute-based ordering rather than an explicit ordering. This change
was made to improve compatibility with more of the DOTS ecosystem.

### Global Entities Blackboard Entities

Global Entities have been renamed to Blackboard Entities. All references to
“GlobalEntity” or “ManagedEntity” must be renamed to “BlackboardEntity”.

This name change broke all authoring components (`[GenerateAuthoringComponent]`
sucks and I am no longer using it to avoid issues like this in the future). You
will have to reconfigure them in the Editor. I am very, very sorry about this.

The `BlackboardEntity` API (formerly `ManagedEntity`) has been reworked to more
closely mirror `EntityManager` API.

### LatiosSyncPointGroup LatiosWorldSyncGroup

With the exception of `OrderFirst`, systems in this are guaranteed to execute
after all built-in systems in `Core`. This is an excellent location for systems
which interact with the `EntityManager` if your game loop treats
`LatiosInitializationSystemGroup` as a mega sync point.

### Minor Things

`TransfomUniformScalePatchConversionSystem` was moved from `Latios.Authoring` to
`Latios.Authoring.Systems`.

## Psyshock Physics

### asmdef rename

The assembly definition was renamed. You may need to update your Assembly
Definition Files to reference the new assembly name.

### Namespace Latios.PhysicsEngine Latios.Psyshock

This namespace was renamed. A simple project-wide text replace will likely
resolve this.

### Removed “Latios” prefixes

The following types had the prefix removed

-   LatiosColliderConversionSystem -\> ColliderConversionSystem
-   LatiosColliderAuthoring -\> ColliderAuthoring

## New Things to Try!

### Core

`GameObjectConversionConfigurationSystem` allows for customizing the conversion
world before all other systems process. Unlike typical
`DeclareReferencePrefabSystemGroup` systems, these systems only update once per
conversion.

Use
`worldBlackboardEntity.AddComponentDataIfMissing(someDefaultComponentDataValue)`
in a system’s `OnCreate()` to ensure `worldBlackboardEntity` always has required
settings. This method will not overwrite existing values.

Use `latiosWorld.syncPoint.Create###CommandBuffer()` inside a `SubSystem`.
`JobHandle` management is fully automatic after this point. There are some new
types you probably haven’t seen before. Some add new functionality. Some add
speed. More will come in a future release.

There’s a new `PreSyncPointGroup` which runs right before the sync point at the
beginning of the frame. Its purpose is for scheduling non-ECS jobs that execute
on worker threads during the sync point.

### Psyshock Physics

Box colliders are new. Try them out!

You can get rid of all `this.GetPhysicsComponentDataFromEntity()` and replace
them with `GetComponentDataFromEntity()`. Same for buffers.

### Myri Audio

This is a new package which does pure DOTS audio. It is still young, but already
includes a powerful and customizable spatializer. The API is also very simple.
