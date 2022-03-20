# Fluent Queries

`FluentQuery` is a struct with a fluent builder API for constructing
`EntityQuery` instances. It streamlines the process of creating EntityQueries in
`OnCreate()` inside `Subsystem`s and `SuperSystem`s by accounting for strange
nuances in the Unity API and reducing boilerplate.

There is slightly more boilerplate using `FluentQuery` over storing an
`EntityQuery` from an `Entities.ForEach`. Use a `FluentQuery` when an
`Entities.ForEach` does not match the problem, such as in a system that relies
on `IJobChunk` instead.

## Creating EntityQueries using FluentQuery

You can create a base `FluentQuery` instance using the `Fluent` property on
`SubSystem` and `SuperSystem`, or you can use the `EntityManager` and
`SystemState` extension method `Fluent()`.

To convert a `FluentQuery` into an `EntityQuery`, call `Build()`.

While unnecessary for typical use cases, you can store a `FluentQuery` instance
in a local variable or pass it to functions. However, the base `FluentQuery`
instance allocates native containers on construction and those containers are
only disposed when `Build()` is called.

## Basic FluentQuery Operations

The primary methods for application code are provided as follows:

-   WithAll\<T\>(bool readOnly) – Adds a component to the “All” list of the
    `EntityQuery`
-   WithAny\<T\>(bool readOnly) – Adds a component to the “Any” list of the
    `EntityQuery`
    -   If any component is repeated in the “All” list, the “Any” list is
        dropped entirely
-   Without\<T\>() – Excludes the component from the resulting `EntityQuery`
-   WithSharedComponent\<T\>(T value) – Adds a shared component filter to the
    `EntityQuery`
    -   Up to two of these calls may be performed in a single `FluentQuery`
        chain
-   WithChangeFilter\<T\>() – Adds a change filter for the type to the
    `EntityQuery`
    -   There is no limit to the number of calls performed on the `FluentQuery`,
        but at the time of writing, `EntityQuery` only supports two filters of
        this type
-   IncludeDisabled() – Sets the `EntityQuery` to use disabled entities
-   IncludePrefabs() – Sets the `EntityQuery` to use prefab entities
-   UseWriteGroups() – Sets the `EntityQuery` to use write group filtering

## Extending FluentQuery

Unlike `Entities.ForEach`, `FluentQuery` is extendible. To extend it, simply
create an extension method which returns the result of the last element in the
chain continued inside the extension method.

One use case is to quickly add a group of components commonly used together
using a single method.

Example:

```csharp
public static FluentQuery WithCommonTransforms(this FluentQuery fluent, bool readOnly)
{
    return fluent.WithAll<Translation>(readOnly).WithAll<Rotation>(readOnly).WithAll<LocalToWorld>(readOnly);
}
```

Another use case is for a library providing an API that requires an
`EntityQuery` with a minimum set of ReadOnly components, but would be satisfied
if the user gave an `EntityQuery` with ReadWrite access instead.

When multiple extension methods are used, it can be very easy to create
ReadWrite vs ReadOnly conflicts. Likewise, it is easy for a user to try and
exclude a component an extension method calls `WithAny()` on. For this reason,
some advanced API exists so that extension methods can specify their minimum
requirements, allowing other extension methods or user code to override the
requests as long as the minimum requirements are met.

These advanced methods are as follows:

-   WithAllWeak\<T\>() – Adds a component to the “All” list as ReadOnly unless
    something else in the FluentQuery adds the component as ReadWrite
-   WithAnyWeak\<T\>() – Same as `WithAllWeak<T>()` except applied to the “Any”
    list
    -   If any component is repeated in the “All” list, the “Any” list is
        dropped entirely
-   WithAnyNotExcluded\<T\>(bool readOnly) – Adds the component to the “Any”
    list unless it is excluded using `Without<T>()`
-   WithAnyNotExcludedWeak\<T\>() – Applies both effects of `WithAnyWeak<T>()`
    and `WithAnyNotExcluded<T>(false)`
