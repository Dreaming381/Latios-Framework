# Extensions and Exposed

Sometimes the Unity API is lacking for who knows why. As such, this page lists
all the APIs I have added either by extension methods or using asmrefs to access
the internals.

## Exposed by asmref

The following APIs were created using asmref files and the code is compiled as
part of the package code:

### Unity.Mathematics

-   `math.select()` functions for bool types

### Unity.Entities

-   `World.ExecutingSystemType()` – Returns the currently executing system for
    the given world. This should not be used for gameplay, but may be useful for
    profiling.
-   `World.ExecutingSystemHandle()` – Returns the `SystemHandleUntyped` of the
    executing system for the given world.
-   `World.AsManagedSystem()` – Resolves a `SystemHandleUntyped` into a
    `ComponentSystemBase` or `null`.
-   `WorldUnmanaged.GetAllSystemStates()` – Returns an array of all
    `SystemState` instances stored in the world. The `SystemState` instances can
    be fetched by ref from the array object.
-   `WorldExposedExtensions.GetMetaIdForType()` – Computes the meta ID for a
    system type so that an unmanaged type with a resolved `SystemState` can be
    compared against the meta ID.
-   `ComponentSystemGroup.GetSystemGroupEnumerator()` – Returns an enumerator
    which can traverse both managed and unmanaged systems in order.
-   `SystemSortingTracker` – Struct type which can detect added or removed
    systems and resort the system groups.
-   `EntityManager.CopySharedComponent()` – Copies the value of a shared
    component from one entity to another given only the `ComponentType`. Will
    add the `ComponentType` to the destination entity if absent.

## Extensions

The following methods did not require internal access to implement and act as
extension methods to Unity’s types.

### Unity.Entities

-   `BlobBuilder.ConstructFromNativeArray<T>(ref BlobArray<T>, NativeArray<T>)`
    – A convenience method for initializing a `BlobArray` from a `NativeArray`.
-   `BlobBuilder.AllocateFixedString<T>(ref BlobString, T)` – Allocates a
    `BlobString` from a `FixedString` or `HeapString`, allowing `BlobStrings` to
    be constructed in Burst.
-   `NativeList<T>.AddRangeFromBlob(ref BlobArray<T>)` – Copies the `BlobArray`
    data to the `NativeList`.
-   `EntityManager.CopyComponentData(Entity src, Entity dst, ComponentType)` –
    Copies an unmanaged `IComponentData` from one entity to the other. Will add
    the `ComponentType` to the destination entity if absent.
-   `EntityManager.CopyDynamicBuffer(Entity src, Entity dst, ComponentType)` –
    Copies a `DynamicBuffer` from one entity to the other. Will add the
    `ComponentType` to the destination entity if absent.

### Unity.Collections

-   `StringBuilder.Append(FixedString)` – Appends a `FixedString` to the
    StringBuilder
