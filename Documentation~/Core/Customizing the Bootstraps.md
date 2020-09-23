# Customizing the Bootstraps

Latios.Core requires using an `ICustomBootstrap` in order to instantiate a
`LatiosWorld` as the default `World` instance. However, aside from this
requirement, the framework does not impose any restrictions on the
`ICustomBootstrap` implementation. It is possible to modify the generated
bootstrap from the templates or write a custom one from scratch. Additional
utilities are provided in the static class `BootstrapTools`.

A `LatiosWorld` populates itself with a `LatiosInitializationSystemGroup`, a
`LatiosSimulationSystemGroup`, and a `LatiosPresentationSystemGroup`. It
further populates `LatiosInitializationSystemGroup` with necessary framework
systems. For more details, see [LatiosWorld in detail](LatiosWorld%20in%20Detail.md).

## Customizing Explicit System Ordering

When using the Explicit System Ordering workflow, you can further customize
which systems are injected at the top-level before top-down system ordering
takes over. Either before or after calling
`BootstrapTools.InjectRootSuperSystems()`, you may call one or more of these
useful functions:

-   `InjectUnitySystems()` - injects the systems from the type list which are
    Unity systems

-   `InjectSystemsFromNamespace()` - injects the systems from the type list
    whose namespace contains the passed-in string

    -   This is especially useful for third-party systems

-   `InjectSystem()` - injects a single system

## Customizing the PlayerLoop

Regardless of workflow, you may choose to customize the `PlayerLoop` to meet
your needs. While this is relatively straightforward with the recently added
`ScriptBehaviourUpdateOrder` API, some additional utilities are provided for
common use-cases.

-   `AddWorldToCurrentPlayerLoopWithFixedUpdate()` - The FixedUpdate in this
    context is the Unity Engine’s FixedUpdate and not the Entities FixedUpdate.

-   `AddWorldToCurrentPlayerLoopWithDelayedSimulation()` - This runs the
    `SimulationSystemGroup` after rendering. This may be useful in removing a
    sync point or a `TransformSystemGroup` update depending on how your logic
    is structured.
