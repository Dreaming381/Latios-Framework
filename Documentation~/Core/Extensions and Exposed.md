# Extensions and Exposed

Sometimes the Unity API is lacking for who knows why. As such, this page lists
all the APIs I have added either by extension methods or using asmrefs to access
the internals.

## Exposed by asmref

The following APIs were created using asmref files and the code is compiled as
part of the package code. Only APIs considered relatively safe for user use are
listed here.

### Unity.Mathematics

-   `math.select()` functions for bool types

### Unity.Entities

-   `UnityObjectRef<T>.GetInstanceID() – Returns the instance ID of the
    UnityObjectRef for use in hashmaps`
-   `ArchetypeChunk.GetChunkComponentRefRW<T>() – Returns the chunk component of
    a chunk by ref for situations where the chunk component is large`
-   `ArchetypeChunk.GetChunkComponentRefRO<T>() – Returns the chunk component of
    a chunk by RefRO for situations where the chunk component is large`
-   `BlobAssetReference<T>.GetLength()` – Returns the number of bytes associated
    with the blob asset starting from `GetUnsafePtr()`
-   `UnsafeUntypedBlobAssetReference.GetLength()` – Returns the number of bytes
    associated with the blob asset starting from `GetUnsafePtr()`
-   `BlobAssetReference<T>.GetHash64()` – Returns the internal hash of the blob
-   `UnsafeUntypedBlobAssetReference.GetHash64()` – Returns the internal hash of
    the blob
-   `ComponentSystemGroup.GetSystemGroupEnumerator()` – Returns an enumerator
    which can traverse both managed and unmanaged systems in order.
-   `SystemSortingTracker` – Struct type which can detect added or removed
    systems and resort the system groups.
-   `EntityManager.CopySharedComponent()` – Copies the value of a shared
    component from one entity to another given only the `ComponentType`. Will
    add the `ComponentType` to the destination entity if absent.
-   `World.AsManagedSystem()` – Resolves a `SystemHandle` into a
    `ComponentSystemBase` or `null`.

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
