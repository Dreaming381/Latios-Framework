# ISystem Support

The Latios Framework has ever-growing support for `ISystem` beginning with
version 0.4.5. This page lists out the various features which are expected to
work in `ISystem` and how to utilize them.

## OnNewScene and ShouldUpdateSystem

These exist via the interfaces `ISystemNewScene` and `ISystemShouldUpdate`. A
struct implementing these should also implement `ISystem`. Currently these
functions do not support Burst compilation or lambdas.

## Fluent Queries

Fluent query API can be accessed via the `SystemState.Fluent()` extension
method. Currently they do not work in Burst, but will soon.

## Blackboard Entities

Blackboard entities can be acquired via `SystemState.GetWorldBlackboardEntity()`
and `SystemState.GetSceneBlackboardEntity()`. These methods work in Burst.
`BlackboardEntity` methods have the same Burst support as the `EntityManager`
methods they mirror.

## Custom Command Buffers

Custom command buffers can be created, written to, and played back all within a
single Burst method.

## Collection Components, Sync Point, and Dependency Management

These features are not supported. The issue is not dependency management, but
rather the inability to get `NativeContainers` in and out of unmanaged systems
from dynamically-sized storage. It seems like `EntityCommandBufferSystem` might
have the same problem. If you have a solution for this, please reach out to me!

## RNG

The `Rng` type works very well in Burst systems. The only pitfall is that it
can’t be seeded with a `string` in Burst.
