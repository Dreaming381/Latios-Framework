# Collection and Managed Struct Components

Collection components and managed struct components are special types which can
be “attached” to entities just like any other component. They can contain native
containers and reference types but can only be accessed from the main thread.

**Due to Unity API limitations introduced with Entities.ForEach, these
components are not real components and are not part of the archetype. Instead,
they “follow” a component of your choice.**

**These components are not supported in GameObject Conversion.**

## Creating Component types

Creating a custom collection component or managed struct component requires
first declaring a struct that implements one of the following interfaces:
`ICollectionComponent` or `IManagedComponent`.

The next step is to create the fields of the struct. These are created just like
any normal struct.

The third step is to implement the interface’s `AssociatedComponentType`
property. You can either declare a unique tag `IComponentData` or use another
component that you always want to pair your custom `ICollectionComponent` or
`IManagedComponent` with.

For `IManagedComponent` types, your custom component is ready to go. But for
`ICollectionComponent` types, there’s still one final step. You must implement
the `JobHandle Dispose(JobHandle inputDeps)` method.

Examples:

```csharp
public struct PlanetGenerationWorld : IManagedComponent
{
    public LatiosWorld world;
    public Type AssociatedComponentType => typeof(PlanetGenerationWorldTag);
}

public struct PlanetGenerationWorldTag : IComponentData { }

public struct Pipe : IComponentData
{
    public float timeUntilNextEmission;
}

public struct PipeEmissionQueue : ICollectionComponent
{
    public NativeQueue<Entity> disabledEntityQueue;
    public NativeList<Entity> entitiesToEnable;
	
    public Type AssociatedComponentType => typeof(Pipe);
	
    public JobHandle Dispose(JobHandle inputDeps) => 
	    JobHandle.CombineDependencies(disabledEntityQueue.Dispose(inputDeps), entitiesToEnable.Dispose(inputDeps));
}

```

## Component Lifecycles

There are two ways to affect the lifecycle of an instance of a
`ICollectionComponent` or `IManagedComponent`.

### Direct Mode – EntityManager and ManagedEntity

In direct mode, you operate on the `ICollectionComponent` and
`IManagedComponent` types directly. Adding a collection component with
`AddCollectionComponent` will automatically add the
`AssociatedComponentType` as well. Removing the collection component with
`RemoveCollectionComponentAndDispose` will remove the
`AssociatedComponentType` as well as invoke `Dispose()` on the stored
collection component.

When using the direct mode API, the contents of a collection component are
assumed to be allocated. If this is not the case, you must check for this in the
`Dispose()` method.

### Indirect Mode – AssociatedComponentType

Sometimes you need to use an EntityCommandBuffer to intantiate entities or add
components that you wish to have a collection component. Or sometimes you want
to author a component that needs a collection component at runtime. For these
cases, you can rely on the `AssociatedComponentType` to add or remove the
components you need.

When adding the `AssociatedComponentType` to an entity, the collection
component will not exist immediately. Instead, it will be added on the next
frame. The exact timing of this will be after the `SceneManagerSystem` and
`MergeGlobalsSystem` but before other custom systems in
`LatiosInitializationSystemGroup`. The systems which perform this live in a
`ManagedComponentsReactiveSystemGroup`. Checking ‘HasComponent()’ on the
entity will return `false` until this happens.

A collection component added in this matter will be default-initialized, meaning
none of its NativeContainers will be allocated. The component will be flagged
appropriately, so that the reactive systems do not try to dispose the container
later.

When the `AssociatedComponentType` is removed, the collection component will
not be removed immediately. Instead, it will be removed and disposed within the
`ManagedComponentsReactiveSystemGroup`. Checking `HasComponent()` on the
entity immediately after the `AssociatedComponentType` is removed will return
`false`.

## Getting and Setting Components

Collection components and managed struct components can only be fetched or set
through EntityManager extension methods or using a ManagedEntity like the
`sceneGlobalEntity` or `worldGlobalEntity`.

The following are API methods exposed for manipulating these components:

-   Managed Components

    -   AddManagedComponent\<T\> - Adds the managed struct component to the
        entity

    -   RemoveManagedComponent\<T\> - Removes the managed struct component from
        the entity

    -   GetManagedComponent\<T\> - Gets a copy of the managed struct component
        from the entity, copying references to managed types rather than their
        underlying objects

    -   SetManagedComponent\<T\> - Replaces the stored managed struct component
        with a copy of the passed in struct, copying references to managed types

    -   HasManagedComponent\<T\> - Checks if a stored managed struct component
        exists on the entity and is not pending removal

-   Collection Components

    -   AddCollectionComponent\<T\> - Adds the collection component to the
        entity, marking it unallocated if `isInitialized` is manually set to
        `false`

    -   RemoveCollectionComponentAndDispose\<T\> - Removes the collection
        component and disposes it if it was flagged as allocated

    -   GetCollectionComponent\<T\> - Gets the collection component from the
        entity and marks it as readonly if `readOnly` is manually set to
        `true`

    -   SetCollectionComponentAndDisposeOld\<T\> - Replaces the stored
        collection component from the entity with the passed in collection
        component and disposes the replaced collection component if it was
        flagged as allocated

    -   HasCollectionComponent\<T\> - Checks if a stored collection component
        exists on the entity and is not pending removal

## Collection Component Dependency Management

Collection components have an intrinsic understanding of the `Dependency`
property of `SubSystem`s, similar to lambda jobs. This means that **by
default**, dependency management is **automatic**!

### ReadOnly and ReadWrite per Instance

The automatic dependency management tracks separate ReadOnly and ReadWrite
JobHandles per instance of a collection component. The rules for this follow the
same rules as `ComponentType`s in Unity’s ECS:

-   Multiple independent jobs may read from a collection component
    simultaneously

-   When a job has write-access to a collection component, no other job may read
    nor write to that collection component simultaneously

The automatic dependency management will do its best to keep track of this for
you, but requires that you correctly specify whether you are accessing a
collection component as `readOnly` similar to requesting a
`ComponentDataFromEntity`.

However, unlike Unity’s ECS, each instance is tracked per entity. Taking from
the example above, this means a job writing to EntityA’s PipeEmissionQueue can
run simultaneously with a job writing to EntityB’s PipeEmissionQueue.

### How the Dependencies are Updated

Before the `OnUpdate` of the `SubSystem` executes, the system registers
itself with the `LatiosWorld` as the active running system. From there, the
`LatiosWorld` forwards all `Dependency` updates to that system and also
records a list of all Entity-ICollectionComponent pairs which have been accessed
and need their internal `JobHandle`s updated. After the `OnUpdate` finishes,
the `SubSystem` passes the final `Dependency` to the `LatiosWorld` which
then commits the `Dependency` to the internal storage.

**Warning:** Be careful when iterating through collection components in an
`Entities.ForEach` loop like the following:

```csharp
Entities.WithAll<FactionTag>().ForEach((Entity entity) =>
{
    var shipLayer = EntityManager.GetCollectionComponent<FactionShipsCollisionLayer>(entity, true).layer;
    Dependency = Physics.FindPairs(bulletLayer, shipLayer, processor).ScheduleParallel(Dependency);
}).WithoutBurst().Run();
```

This will complete Dependency at the start of the Entities.ForEach, which may
include jobs for components you may be trying to run new jobs on inside your
loop that interact with your collection components.

To avoid such a sync point, you can do something like this:

```csharp
var backup = Dependency;
Dependency = default;

Entities.WithAll<FactionTag>().ForEach((Entity entity, int entityInQueryIndex) =>
{
    if (entityInQueryIndex == 0)
        Dependency = backup;

    var shipLayer = EntityManager.GetCollectionComponent<FactionShipsCollisionLayer>(entity, true).layer;

    Dependency = Physics.FindPairs(bulletLayer, shipLayer, processor).ScheduleParallel(Dependency);
}).WithoutBurst().Run();
```

API to more conveniently handle this scenario will come in a future release.

### Fine-Grained Dependency Control

If for some reason, you wish to have more fine-grained control over the
dependency management, there are overloads of the EntityManager methods which
you can use instead. The following table shows which functions modify which
JobHandle values:

| Method                                                 | Modifies Dependency | Queues internal update from Dependency | Removes internal update from queue |
|--------------------------------------------------------|---------------------|----------------------------------------|------------------------------------|
| AddCollectionComponent\<T\>                            |                     | x                                      |                                    |
| RemoveCollectionComponentAndDispose\<T\>               | x                   |                                        | x                                  |
| RemoveCollectionComponentAndDispose\<T\> out JobHandle |                     |                                        | x                                  |
| GetCollectionComponent\<T\>                            | x                   | x                                      |                                    |
| GetCollectionComponent\<T\> out JobHandle              |                     | x                                      |                                    |
| SetCollectionComponentAndDisposeOld\<T\>               | x                   | x                                      |                                    |
| UpdateCollectionComponentDependency\<T\>               |                     |                                        | x                                  |

One reason why you may wish to manually control dependencies is to allow jobs to
run during a sync point. By scheduling a job which only interacts with
collection components attached to `worldGlobalEntity` and calling
`UpdateCollectionComponentDependency<T>()` on those collection components
while not touching `Dependency`, the `JobHandle`s will be automatically
managed by the Latios automatic dependency management system but not Unity ECS’s
automatic dependency management system.
