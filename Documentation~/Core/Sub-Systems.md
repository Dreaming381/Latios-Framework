# Sub-Systems

A `SubSystem` is a subclass of `SystemBase` which exposes additional API and
boilerplate reduction mechanisms within the framework. You can use them exactly
as you would a `SystemBase`, and frequently you will want to do just that.
However, there are some additional features to take advantage of.

## Fluent Queries

[Fluent query](Fluent%20Queries.md) expressions can be initiated using the
member property `Fluent`. You will likely want to use this inside of
`OnCreate().`

## Blackboard Entities

The two [blackboard entities](Blackboard%20Entities.md) can be accessed through
the member properties `sceneBlackboardEntity` and `worldBlackboardEntity`.
However, you must assign them to local `Entity` variables before using them in a
Bursted lambda job.

## Collection Component Dependency Management

When fetching a [collection
component](Collection%20and%20Managed%20Struct%20Components.md), rather than
complete the `JobHandle`s associated with that component, the `JobHandle`s will
be combined with `Dependency`. Additionally, by default `Dependency` will
automatically be used to update the `JobHandle`s of the collection component
after the `SubSystem` finishes `OnUpdate()`. When combined with lambda jobs, you
may never have to explicitly touch `Dependency` nor any other `JobHandle`.

## LatiosWorld Sync Point Dependency Management

The [LatiosWorld](LatiosWorld%20in%20Detail.md) instance can be accessed through
the `latiosWorld` member property without casting. From there, the `latiosWorld`
provides access to the
[SyncPointPlaybackSystem](Custom%20Command%20Buffers%20and%20SyncPointPlaybackSystem.md)
via the `syncPoint` property. Once you do this, `Dependency` will automatically
be sent to `SyncPointPlaybackSystem` after the `SubSystem` finishes
`OnUpdate()`. When combined with lambda jobs, you may never have to explicitly
touch `Dependency` nor any other `JobHandle`.

## Custom Update Criteria

`ShouldUpdateSystem()` can be overridden if a [custom
criteria](Super%20Systems.md) should dictate whether the `SubSystem` should
execute `OnUpdate()`. This does not disable Unity’s `EntityQuery` checks. To
disable those, add `[AlwaysUpdateSystem]` to the `SubSystem`.

*Caution: Dependency and version numbers have not been updated yet when this
method is invoked. Only EntityManager, EntityQuery, and BlackboardEntity
operations are recommended.*

*Caution 2: Only SubSystems directly inside a SuperSystem or RootSuperSystem
will have ShouldUpdateSystem() invoked.*
