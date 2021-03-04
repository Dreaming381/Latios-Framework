# Extensions and Exposed

Sometimes the Unity API is lacking for who knows why. As such, this page lists
all the APIs I have added either by extension methods or using asmrefs to access
the internals.

## Exposed by asmref

The following APIs were created using asmref files and the code is compiled as
part of the package code:

### Unity.Mathematics

-   math.select() functions for bool types

### Unity.Entities

-   EntityLocationInChunk – Provides an ArchetypeChunk as well as an index into
    that chunk where an entity resides. Implements IEquatable and IComparable to
    help order entities to their chunk layout.
-   EntityManager.GetEntityLocationInChunk(Entity) – Returns the
    EntityLocationInChunk of the passed in Entity.
-   World.ExecutingSystemType() – Returns the currently executing system for the
    given world. This should not be used for gameplay, but may be useful for
    profiling.

## Extensions

The following methods did not require internal access to implement and act as
extension methods to Unity’s types.

### Unity.Entities

-   BlobBuilder.ConstructFromNativeArray\<T\>(ref BlobArray\<T\>,
    NativeArray\<T\>) – A convenience method for initializing a BlobArray from a
    NativeArray.
-   BlobBuilder.AllocateFixedString\<T\>(ref BlobString, T) – Allocates a
    BlobString from a FixedString or HeapString, allowing BlobStrings to be
    constructed in Burst.
-   NativeList\<T\>.AddRangeFromBlob(ref BlobArray\<T\>) – Copies the BlobArray
    data to the NativeList.

### Unity.Collections

-   StringBuilder.Append(FixedString) – Appends a FixedString to the
    StringBuilder
