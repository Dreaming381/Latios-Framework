# Smart Blobbers

Smart Blobbers provide a powerful, streamlined, and user-friendly workflow for
generating Blob Assets during Game Object Conversion. Any authoring logic can
request a Blob Asset from a Smart Blobber and receive a handle. That handle can
later be used to retrieve the Blob Asset after the Smart Blobber has executed.
The Smart Blobber generates blobs in parallel, using Burst if possible. This can
drastically speed up conversion times while keeping the logic for generating
blob assets unified and consistent.

## Converting a Blob Asset using an Authoring MonoBehaviour

The following code demonstrates how to generate a
`BlobAssetReference<SkeletonClipSetBlob>` using Kinemation’s Smart Blobber.

```csharp
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Dragons.Authoring
{
    [DisallowMultipleComponent]
    public class SingleClipAuthoring : MonoBehaviour, IConvertGameObjectToEntity, IRequestBlobAssets
    {
        public AnimationClip clip;

        SmartBlobberHandle<SkeletonClipSetBlob> blob;

        public void RequestBlobAssets(Entity entity, EntityManager dstEntityManager, GameObjectConversionSystem conversionSystem)
        {
            var config = new SkeletonClipConfig { clip = clip, settings = SkeletonClipCompressionSettings.kDefaultSettings };

            blob = conversionSystem.CreateBlob(gameObject, new SkeletonClipSetBakeData
            {
                animator = GetComponent<Animator>(),
                clips    = new SkeletonClipConfig[] { config }
            });
        }

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            var singleClip = new SingleClip { blob = blob.Resolve() };

            dstManager.AddComponentData(entity, singleClip);
        }
    }
}
```

The first thing you may notice is the new interface `IRequestBlobAssets`. This
interface defines a method `RequestBlobAssets()` which is called after the
`DeclareReferencedPrefabs` stage but before Convert gets called and before the
Smart Blobbers update.

This authoring class has a non-serialized member of type `SmartBlobberHandle<>`.
You can define as many of these as you want, pass them around wherever, and
store them in managed containers. There is also a `SmartBlobberHandleUntyped` if
you need it.

Some Smart Blobbers define extension methods for `GameObjectConversionSystem`
which allow you to request a blob asset without a reference to the Smart
Blobber. In this case, it is called `CreateBlob()`. However, if one does not
exist, you can still fetch a reference to the Smart Blobber (which is a system
belonging to the conversion world) and calling `AddToConvert()` or
`AddToConvertUntyped()`.

Finally, in `Convert()`, you can call `Resolve()` on the handle to get the
generated `BlobAssetReference<T>`. This `BlobAssetReference<T>` has already been
assigned to the `BlobAssetStore` and deduplicated across the conversion world.

### Why does CreateBlob() need the gameObject?

Unity’s advanced blob asset API requires every blob asset to be associated with
a `GameObject`. It will dispose all old blobs of a type the second time a
conversion system runs using that type with the same `BlobAssetStore` instance,
but only if those old blobs weren’t reproduced in the new run. I don’t actually
know how that can ever happen, but it is something to keep in mind.

## Creating a Simple Smart Blobber

If you have your own custom blob asset types, you may want to create a Smart
Blobber for them. Let’s walk through a simple example where the Smart Blobber
directly reads authoring components and applies the results directly to the
converted entities.

First, we need our blob type, component type, and authoring type.

```csharp
public struct DigitsBlob
{
    public int            value;
    public BlobArray<int> digits;
}

public struct DigitsBlobReference : IComponentData
{
    public BlobAssetReference<DigitsBlob> blob;
}

[DisallowMultipleComponent]
public class DigitsAuthoring : MonoBehaviour
{
    public int   value  = 381;
    public int[] digits = { 3, 8, 1 };
}
```

Note the `DigitsAuthoring` does not implement `IConvertGameObjectToEntity`. That
responsibility is now on the Smart Blobber.

Every Smart Blobber requires an input `struct`. This defines the inputs needed
for each blob asset request. This is considered public API for the Smart
Blobber.

```csharp
// This is where Smart Blobber logic starts
public struct DigitsBakeData
{
    public int   value;
    public int[] digits;
}
```

The Smart Blobber also requires a converter `struct` which implements
`ISmartBlobberSimpleBuilder<>`. This `struct` is considered an internal aspect
of the Smart Blobber and should not be interacted with externally. However, if
you make your Smart Blobber `public`, you will have to make this `public` as
well.

```csharp
// This struct represents a single element in a parallel Burst job
public struct DigitsConverter : ISmartBlobberSimpleBuilder<DigitsBlob>
{
    public int             value;
    public UnsafeList<int> digits;

    public unsafe BlobAssetReference<DigitsBlob> BuildBlob()
    {
        var     builder = new BlobBuilder(Allocator.Temp);
        ref var root    = ref builder.ConstructRoot<DigitsBlob>();
        root.value      = value;
        builder.ConstructFromNativeArray(ref root.digits, digits.Ptr, digits.Length);
        return builder.CreateBlobAssetReference<DigitsBlob>(Allocator.Persistent);
    }
}
```

The member fields are the inputs needed to construct a single blob asset. Native
Containers are not allowed, but unsafe containers are. The `BuildBlob()`
function is where the actual blob asset is built using a `BlobBuilder` like in
this example or the `BlobAssetReference<>` direct creation methods.
`BuildBlob()` is only called once per converter instance.

Now it is time to define our actual Smart Blobber class:

```csharp
public class DigitsSmartBlobberSystem : SmartBlobberConversionSystem<DigitsBlob, DigitsBakeData, DigitsConverter>
{
```

Simple Smart Blobbers have **3** generic arguments.

Since this Smart Blobber is processing the authoring components directly, it
needs a list to keep track of requests.

```csharp
// This is used to keep track of the inputs and apply them to the outputs.
// You only need this if you are generating runtime components directly in this system.
struct AuthoringHandlePair
{
    public DigitsAuthoring                authoring;
    public SmartBlobberHandle<DigitsBlob> blobHandle;
}

List<AuthoringHandlePair> m_sourceList = new List<AuthoringHandlePair>();
```

Requests can be generated by the overriding the `virtual` method
`GatherInputs()`. Note that a `SmartBlobberConversionSystem` subclasses
`GameObjectConversionSystem`. Things like `Entities.ForEach`, `World`, and
`DstEntityManager` are all accessible. `GatherInputs()` is called during the
system’s `OnUpdate()` but before blob asset processing.

```csharp
// This is where we gather inputs and feed requests to ourselves.
// Again, we are only doing this because this system generates runtime components directly.
protected override void GatherInputs()
{
    Entities.ForEach((DigitsAuthoring authoring) =>
    {
        var input      = new DigitsBakeData { value = authoring.value, digits = authoring.digits };
        var blobHandle = AddToConvert(authoring.gameObject, input);
        m_sourceList.Add(new AuthoringHandlePair { authoring = authoring, blobHandle = blobHandle });
    });
}
```

The requests can be resolved by overriding the virtual method
`FinalizeOutputs()`. This method is called during the system’s `OnUpdate()`
after blob asset processing.

```csharp
// And this is where we resolve blob handles and add them to entities
protected override void FinalizeOutputs()
{
    foreach (var pair in m_sourceList)
    {
        var entity       = GetPrimaryEntity(pair.authoring);
        var resolvedBlob = pair.blobHandle.Resolve();
        DstEntityManager.AddComponentData(entity, new DigitsBlobReference { blob = resolvedBlob });
    }
}
```

The final step is to implement the `Filter()` method. This is an abstract method
and must be implemented. It is called for each input, and it requires an
instance of a converter to be generated. If this method returns false, the input
will be skipped. The converter will not have `BuildBlob()` called and the handle
associated with the input will resolve to `BlobAssetReference<>.Null`.

```csharp
// And now we convert our inputs into something our job-friendly converter struct can handle.
protected override unsafe bool Filter(in DigitsBakeData input, GameObject gameObject, out DigitsConverter converter)
{
    if (input.digits == null)
    {
        converter = default;
        // Returning null here means that this input will be assigned a Null BlobAssetReference.
        return false;
    }

    // It is usually a good idea to use UpdateAllocator when allocating UnsafeLists like this
    var digits = new UnsafeList<int>(input.digits.Length, World.UpdateAllocator.ToAllocator);
    foreach (var digit in input.digits)
        digits.Add(digit);

    converter = new DigitsConverter
    {
        value  = input.value,
        digits = digits
    };
    return true;
}
```

When populating UnsafeLists and other containers stored inside converters, it is
currently best practice to use World.UpdateAllocator.

And that’s it. Here is the complete code:

```csharp
using System.Collections.Generic;
using Latios;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace Dragons
{
    public struct DigitsBlob
    {
        public int            value;
        public BlobArray<int> digits;
    }

    public struct DigitsBlobReference : IComponentData
    {
        public BlobAssetReference<DigitsBlob> blob;
    }

    [DisallowMultipleComponent]
    public class DigitsAuthoring : MonoBehaviour
    {
        public int   value  = 381;
        public int[] digits = { 3, 8, 1 };
    }

    // This is where Smart Blobber logic starts
    public struct DigitsBakeData
    {
        public int   value;
        public int[] digits;
    }

    // This struct represents a single element in a parallel Burst job
    public struct DigitsConverter : ISmartBlobberSimpleBuilder<DigitsBlob>
    {
        public int             value;
        public UnsafeList<int> digits;

        public unsafe BlobAssetReference<DigitsBlob> BuildBlob()
        {
            var     builder = new BlobBuilder(Allocator.Temp);
            ref var root    = ref builder.ConstructRoot<DigitsBlob>();
            root.value      = value;
            builder.ConstructFromNativeArray(ref root.digits, digits.Ptr, digits.Length);
            return builder.CreateBlobAssetReference<DigitsBlob>(Allocator.Persistent);
        }
    }

    public class DigitsSmartBlobberSystem : SmartBlobberConversionSystem<DigitsBlob, DigitsBakeData, DigitsConverter>
    {
        // This is used to keep track of the inputs and apply them to the outputs.
        // You only need this if you are generating runtime components directly in this system.
        struct AuthoringHandlePair
        {
            public DigitsAuthoring                authoring;
            public SmartBlobberHandle<DigitsBlob> blobHandle;
        }

        List<AuthoringHandlePair> m_sourceList = new List<AuthoringHandlePair>();

        // This is where we gather inputs and feed requests to ourselves.
        // Again, we are only doing this because this system generates runtime components directly.
        protected override void GatherInputs()
        {
            Entities.ForEach((DigitsAuthoring authoring) =>
            {
                var input      = new DigitsBakeData { value = authoring.value, digits = authoring.digits };
                var blobHandle = AddToConvert(authoring.gameObject, input);
                m_sourceList.Add(new AuthoringHandlePair { authoring = authoring, blobHandle = blobHandle });
            });
        }

        // And this is where we resolve blob handles and add them to entities
        protected override void FinalizeOutputs()
        {
            foreach (var pair in m_sourceList)
            {
                var entity       = GetPrimaryEntity(pair.authoring);
                var resolvedBlob = pair.blobHandle.Resolve();
                DstEntityManager.AddComponentData(entity, new DigitsBlobReference { blob = resolvedBlob });
            }
        }

        // And now we convert our inputs into something our job-friendly converter struct can handle.
        protected override unsafe bool Filter(in DigitsBakeData input, GameObject gameObject, out DigitsConverter converter)
        {
            if (input.digits == null)
            {
                converter = default;
                // Returning null here means that this input will be assigned a Null BlobAssetReference.
                return false;
            }

            // It is usually a good idea to use UpdateAllocator when allocating UnsafeLists like this
            var digits = new UnsafeList<int>(input.digits.Length, World.UpdateAllocator.ToAllocator);
            foreach (var digit in input.digits)
                digits.Add(digit);

            converter = new DigitsConverter
            {
                value  = input.value,
                digits = digits
            };
            return true;
        }
    }
}
```

### Why not just use BlobAssetStore.AddUniqueBlobAsset()?

In the case where the Smart Blobber is processing Game Objects directly and
using a simple variant, this may not have many advantages.

One advantage is that Blob Assets are generated in parallel in Burst. There’s
more going on than just copying data to `BlobArray<>` fields.
`BlobBuilder.CreateBlobAssetReference()` does a non-trivial amount of work to
organize the relative offsets of the blob asset and put everything together.
This cost can become significant as the blob asset increases in complexity. It
also hashes the entire blob data near the end of generation.

The second advantage is that a Smart Blobber can start receiving requests from
other conversion later on in development. Requirements change as projects
evolve, and so can whether or not only one type of component is allowed to have
a type of blob asset.

### What if I need main-thread access to build my blob?

It might be the blob is heavily based on polymorphic Scriptable Objects or some
other paradigm that makes it difficult to benefit from a parallel Burst job. In
that case, construct the blob asset inside of `Filter()` and assign it to the
converter. The converter simply has to return the blob asset in its
`BuildBlob()` method.

## Creating a Batching Smart Blobber

There is a more advanced type of Smart Blobber which is able to reason about all
blobs at once throughout most of the pipeline. This allows for input
deduplication and sharing Native Containers across converters via a context
object. This example will create a `TriangleSoupBlob` derived from `Mesh`
instances. It will also provide a streamlined request API.

This will be the blob and runtime component definitions:

```csharp
public struct TriangleSoupBlob
{
    public Aabb                        aabb;
    public BlobArray<Aabb>             batch32Aabbs;
    public BlobArray<Aabb>             triangleAabbs;
    public BlobArray<TriangleCollider> triangles;
    public FixedString128Bytes         originalMeshName;
}

public struct TriangleSoupBlobReference : IComponentData
{
    public BlobAssetReference<TriangleSoupBlob> blob;
}
```

`TriangleSoupBlob` has a 3-level AABB hierarchy in order to demonstrate more
complex precomputation in a Smart Blobber. The `Aabb` and `TriangleCollider`
types come from Psyshock.

This is an example authoring component that uses the desired streamlined API:

```csharp
// This is the consumer of the Smart Blobber
[DisallowMultipleComponent]
public class TriangleSoupAuthoring : MonoBehaviour, IRequestBlobAssets, IConvertGameObjectToEntity
{
    public Mesh mesh;

    SmartBlobberHandle<TriangleSoupBlob> blobHandle;

    public void RequestBlobAssets(Entity entity, EntityManager dstEntityManager, GameObjectConversionSystem conversionSystem)
    {
        blobHandle = conversionSystem.CreateBlob(gameObject, new TriangleSoupBakeData { mesh = mesh });
    }

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new TriangleSoupBlobReference { blob = blobHandle.Resolve() });
    }
}
```

In order to provide this streamlined API, write extension methods for any
`GameObjectConversionSystem` which fetches the Smart Blobber and calls the
appropriate method. Input for this Smart Blobber functions the same as the
simple Smart Blobber.

```csharp
// This is the Smart Blobber streamlined API
public struct TriangleSoupBakeData
{
    public Mesh mesh;
}

public static class TriangleSoupSmartBlobberApiExtensions
{
    public static SmartBlobberHandle<TriangleSoupBlob> CreateBlob(this GameObjectConversionSystem conversionSystem,
                                                                    GameObject gameObject,
                                                                    TriangleSoupBakeData bakeData)
    {
        return conversionSystem.World.GetExistingSystem<TriangleSoupSmartBlobberSystem>().AddToConvert(gameObject, bakeData);
    }

    public static SmartBlobberHandleUntyped CreateBlobUntyped(this GameObjectConversionSystem conversionSystem,
                                                                GameObject gameObject,
                                                                TriangleSoupBakeData bakeData)
    {
        return conversionSystem.World.GetExistingSystem<TriangleSoupSmartBlobberSystem>().AddToConvertUntyped(gameObject, bakeData);
    }
}
```

This Smart Blobber will need to read meshes inside the converters which run in a
parallel job. Therefore, this Smart Blobber will use a MeshDataArray accessible
to all converters. This is done through a context object.

```csharp
// This is where the Smart Blobber internal logic starts
public struct TriangleSoupContext : System.IDisposable
{
    [ReadOnly] public Mesh.MeshDataArray meshes;

    public void Dispose() => meshes.Dispose();
}
```

Note that context objects must implement `System.IDisposable`. There is only one
context object per Smart Blobber. The context object is a field in the parallel
blob asset generation job, so it is shallow-copied to every worker thread
instance. Normal job struct rules apply here.

The converter now implements the `ISmartBlobberContextBuilder<>` interface, and
receives three arguments. The first argument is an index which corresponds to
the `Filter()` method of the Smart Blobber. The second argument is an index
which corresponds to the `PostFilter()` method of the Smart Blobber. The final
argument is a reference to the shallow-copied thread-local context object.

```csharp
public struct TriangleSoupConverter : ISmartBlobberContextBuilder<TriangleSoupBlob, TriangleSoupContext>
{
    public FixedString128Bytes meshName;

    public BlobAssetReference<TriangleSoupBlob> BuildBlob(int prefilterIndex, int postfilterIndex, ref TriangleSoupContext context)
    {
        var mesh = context.meshes[postfilterIndex];

        NativeArray<int> indices;
        // We need to convert to int to get a common interface
        if (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
        {
            var meshIndices = mesh.GetIndexData<ushort>();
            indices         = new NativeArray<int>(meshIndices.Length, Allocator.Temp);
            for (int i = 0; i < meshIndices.Length; i++)
                indices[i] = meshIndices[i];
        }
        else
        {
            var meshIndices = mesh.GetIndexData<uint>();
            indices         = new NativeArray<int>(meshIndices.Length, Allocator.Temp);
            for (int i = 0; i < meshIndices.Length; i++)
                indices[i] = (int)meshIndices[i];
        }

        // If our index buffer uses triangle strips or lines or something, quit.
        // Smart Blobbers check for Null blobs and handle them appropriately.
        if (indices.Length % 3 != 0 || indices.Length == 0)
            return default;

        var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
        mesh.GetVertices(vertices);

        var     builder   = new BlobBuilder(Allocator.Temp);
        ref var root      = ref builder.ConstructRoot<TriangleSoupBlob>();
        var     triangles = builder.Allocate(ref root.triangles, indices.Length / 3);
        var     aabbs     = builder.Allocate(ref root.triangleAabbs, triangles.Length);

        for (int i = 0; i < triangles.Length; i++)
        {
            float3 a = vertices[indices[i * 3 + 0]];
            float3 b = vertices[indices[i * 3 + 1]];
            float3 c = vertices[indices[i * 3 + 2]];

            triangles[i] = new TriangleCollider(a, b, c);
            aabbs[i]     = Physics.AabbFrom(triangles[i], RigidTransform.identity);
        }

        int batchCount = aabbs.Length / 32;
        if (aabbs.Length % 32 != 0)
            batchCount++;

        var batchAabbs = builder.Allocate(ref root.batch32Aabbs, batchCount);
        var fullAabb   = aabbs[0];
        for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            int triangleBaseIndex    = batchIndex * 32;
            int triangleCountInBatch = math.min(aabbs.Length - triangleBaseIndex, 32);
            var currentBatchAabb     = aabbs[triangleBaseIndex];
            for (int triangleIndex = 1; triangleIndex < triangleCountInBatch; triangleIndex++)
            {
                currentBatchAabb = Physics.CombineAabb(currentBatchAabb, aabbs[triangleBaseIndex + triangleIndex]);
            }
            batchAabbs[batchIndex] = currentBatchAabb;
            fullAabb               = Physics.CombineAabb(fullAabb, currentBatchAabb);
        }

        root.aabb             = fullAabb;
        root.originalMeshName = meshName;

        return builder.CreateBlobAssetReference<TriangleSoupBlob>(Allocator.Persistent);
    }
}
```

There’s a lot of code here. Much of it is processing and constructing the actual
blob. Hopefully the benefit of generating blob assets in parallel has become
obvious. There are two other things to note. First, in some cases, the converter
will return a `default` (`Null`) blob asset. This is legal for converters in
Smart Blobbers. Second, the mesh is indexed using `postfilterIndex`. The reason
for this will become apparent soon.

The Smart Blobber class is defined like so:

```csharp
public class TriangleSoupSmartBlobberSystem : SmartBlobberConversionSystem<TriangleSoupBlob, TriangleSoupBakeData, TriangleSoupConverter, TriangleSoupContext>
{
```

This time, there are **4** generic arguments, with the fourth being the context
object type.

This Smart Blobber does not process authoring components directly, so it does
not need to override `GatherInputs()` or `FinalizeOutputs()`. It is legal for a
Smart Blobber to provide both a streamlined API and also process authoring
components directly for a specific type. For example, Myri’s Smart Blobber
converts Audio Source components directly. However, if user can still request a
list of audio clips to be converted into blobs so that the user can store them
in a user-defined dynamic buffer.

The Filter() method is a little different. The first argument,
FilterBlobberData, provides access to the inputs, associated GameObject
references, and uninitialized converters. The second argument is a
default-initialized context object. The final argument is a mapping
NativeArray\<int\> initialized with the values 0, 1, 2, 3, 4… up to the number
of inputs.

While the converters and context object can be initialized in this method, it is
not necessary to initialize them at this time. However, the mapping array must
be properly configured here, as it replaces the return value of the simple Smart
Blobber’s Filter() method.

To skip an input, set its corresponding element in the mapping array to a
negative value.

To specify that an input is a clone of another input, set its corresponding
element to the lowest-indexed element it duplicates.

The following is an implementation of this method which performs validation and
skipping in managed code, but uses a job to detect duplicate meshes.

```csharp
protected override void Filter(FilterBlobberData blobberData, ref TriangleSoupContext context, NativeArray<int> inputToFilteredMapping)
{
    var hashes = new NativeArray<int>(blobberData.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
    for (int i = 0; i < blobberData.Count; i++)
    {
        var input = blobberData.input[i];
        if (input.mesh == null || !input.mesh.isReadable)
        {
            if (input.mesh != null && !input.mesh.isReadable)
                Debug.LogError($"Failed to convert mesh {input.mesh.name}. The mesh was not marked as readable. Please correct this in the mesh asset's import settings.");

            hashes[i]                 = default;
            inputToFilteredMapping[i] = -1;
        }
        else
        {
            DeclareAssetDependency(blobberData.associatedObject[i], input.mesh);
            hashes[i] = input.mesh.GetInstanceID();
        }
    }

    new DeduplicateJob { hashes = hashes, inputToFilteredMapping = inputToFilteredMapping }.Run();
    hashes.Dispose();
}

[BurstCompile]
struct DeduplicateJob : IJob
{
    [ReadOnly] public NativeArray<int> hashes;
    public NativeArray<int>            inputToFilteredMapping;

    public void Execute()
    {
        var map = new NativeHashMap<int, int>(hashes.Length, Allocator.Temp);
        for (int i = 0; i < hashes.Length; i++)
        {
            if (inputToFilteredMapping[i] < 0)
                continue;

            if (map.TryGetValue(hashes[i], out int index))
                inputToFilteredMapping[i] = index;
            else
                map.Add(hashes[i], i);
        }
    }
} 
```

One aspect to point out is the presence of `DeclareAssetDependency()`.
Dependency declarations are still required, even in Smart Blobbers.

Because this Smart Blobber did not initialize the converters or context object
in `Filter()`, it needs to override `PostFilter()` and initialize them there.

The `PostFilter()` method provides similar arguments to the `Filter()` method,
except this time all the invalid and duplicated elements have been removed. To
recover the original input index for any element, use the provided
`filteredToInputMapping` `NativeArray<int>`.

The `PostFilter()` method for this Smart Blobber is implemented as follows:

```csharp
protected override void PostFilter(PostFilterBlobberData blobberData, ref TriangleSoupContext context)
{
    var meshList = new List<Mesh>();

    var converters = blobberData.converters;

    for (int i = 0; i < blobberData.Count; i++)
    {
        var mesh = blobberData.input[i].mesh;
        meshList.Add(mesh);

        converters[i] = new TriangleSoupConverter
        {
            meshName = mesh.name
        };
    }

    context.meshes = Mesh.AcquireReadOnlyMeshData(meshList);
}
```

After that, the base class Smart Blobber dispatches the parallel job and handles
the mappings such that all handles associated with the inputs resolve to the
correct blob assets.

Here is what it looks like in its complete form:

```csharp
using System.Collections.Generic;
using Latios;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Psyshock;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using Physics = Latios.Psyshock.Physics;

namespace Dragons
{
    public struct TriangleSoupBlob
    {
        public Aabb                        aabb;
        public BlobArray<Aabb>             batch32Aabbs;
        public BlobArray<Aabb>             triangleAabbs;
        public BlobArray<TriangleCollider> triangles;
        public FixedString128Bytes         originalMeshName;
    }

    public struct TriangleSoupBlobReference : IComponentData
    {
        public BlobAssetReference<TriangleSoupBlob> blob;
    }

    // This is the consumer of the Smart Blobber
    [DisallowMultipleComponent]
    public class TriangleSoupAuthoring : MonoBehaviour, IRequestBlobAssets, IConvertGameObjectToEntity
    {
        public Mesh mesh;

        SmartBlobberHandle<TriangleSoupBlob> blobHandle;

        public void RequestBlobAssets(Entity entity, EntityManager dstEntityManager, GameObjectConversionSystem conversionSystem)
        {
            blobHandle = conversionSystem.CreateBlob(gameObject, new TriangleSoupBakeData { mesh = mesh });
        }

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, new TriangleSoupBlobReference { blob = blobHandle.Resolve() });
        }
    }

    // This is the Smart Blobber streamlined API
    public struct TriangleSoupBakeData
    {
        public Mesh mesh;
    }

    public static class TriangleSoupSmartBlobberApiExtensions
    {
        public static SmartBlobberHandle<TriangleSoupBlob> CreateBlob(this GameObjectConversionSystem conversionSystem,
                                                                      GameObject gameObject,
                                                                      TriangleSoupBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<TriangleSoupSmartBlobberSystem>().AddToConvert(gameObject, bakeData);
        }

        public static SmartBlobberHandleUntyped CreateBlobUntyped(this GameObjectConversionSystem conversionSystem,
                                                                  GameObject gameObject,
                                                                  TriangleSoupBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<TriangleSoupSmartBlobberSystem>().AddToConvertUntyped(gameObject, bakeData);
        }
    }

    // This is where the Smart Blobber internal logic starts
    public struct TriangleSoupContext : System.IDisposable
    {
        [ReadOnly] public Mesh.MeshDataArray meshes;

        public void Dispose() => meshes.Dispose();
    }

    public struct TriangleSoupConverter : ISmartBlobberContextBuilder<TriangleSoupBlob, TriangleSoupContext>
    {
        public FixedString128Bytes meshName;

        public BlobAssetReference<TriangleSoupBlob> BuildBlob(int prefilterIndex, int postfilterIndex, ref TriangleSoupContext context)
        {
            var mesh = context.meshes[postfilterIndex];

            NativeArray<int> indices;
            // We need to convert to int to get a common interface
            if (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
            {
                var meshIndices = mesh.GetIndexData<ushort>();
                indices         = new NativeArray<int>(meshIndices.Length, Allocator.Temp);
                for (int i = 0; i < meshIndices.Length; i++)
                    indices[i] = meshIndices[i];
            }
            else
            {
                var meshIndices = mesh.GetIndexData<uint>();
                indices         = new NativeArray<int>(meshIndices.Length, Allocator.Temp);
                for (int i = 0; i < meshIndices.Length; i++)
                    indices[i] = (int)meshIndices[i];
            }

            // If our index buffer uses triangle strips or lines or something, quit.
            // Smart Blobbers check for Null blobs and handle them appropriately.
            if (indices.Length % 3 != 0 || indices.Length == 0)
                return default;

            var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
            mesh.GetVertices(vertices);

            var     builder   = new BlobBuilder(Allocator.Temp);
            ref var root      = ref builder.ConstructRoot<TriangleSoupBlob>();
            var     triangles = builder.Allocate(ref root.triangles, indices.Length / 3);
            var     aabbs     = builder.Allocate(ref root.triangleAabbs, triangles.Length);

            for (int i = 0; i < triangles.Length; i++)
            {
                float3 a = vertices[indices[i * 3 + 0]];
                float3 b = vertices[indices[i * 3 + 1]];
                float3 c = vertices[indices[i * 3 + 2]];

                triangles[i] = new TriangleCollider(a, b, c);
                aabbs[i]     = Physics.AabbFrom(triangles[i], RigidTransform.identity);
            }

            int batchCount = aabbs.Length / 32;
            if (aabbs.Length % 32 != 0)
                batchCount++;

            var batchAabbs = builder.Allocate(ref root.batch32Aabbs, batchCount);
            var fullAabb   = aabbs[0];
            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                int triangleBaseIndex    = batchIndex * 32;
                int triangleCountInBatch = math.min(aabbs.Length - triangleBaseIndex, 32);
                var currentBatchAabb     = aabbs[triangleBaseIndex];
                for (int triangleIndex = 1; triangleIndex < triangleCountInBatch; triangleIndex++)
                {
                    currentBatchAabb = Physics.CombineAabb(currentBatchAabb, aabbs[triangleBaseIndex + triangleIndex]);
                }
                batchAabbs[batchIndex] = currentBatchAabb;
                fullAabb               = Physics.CombineAabb(fullAabb, currentBatchAabb);
            }

            root.aabb             = fullAabb;
            root.originalMeshName = meshName;

            return builder.CreateBlobAssetReference<TriangleSoupBlob>(Allocator.Persistent);
        }
    }

    public class TriangleSoupSmartBlobberSystem : SmartBlobberConversionSystem<TriangleSoupBlob, TriangleSoupBakeData, TriangleSoupConverter, TriangleSoupContext>
    {
        protected override void Filter(FilterBlobberData blobberData, ref TriangleSoupContext context, NativeArray<int> inputToFilteredMapping)
        {
            var hashes = new NativeArray<int>(blobberData.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < blobberData.Count; i++)
            {
                var input = blobberData.input[i];
                if (input.mesh == null || !input.mesh.isReadable)
                {
                    if (input.mesh != null && !input.mesh.isReadable)
                        Debug.LogError($"Failed to convert mesh {input.mesh.name}. The mesh was not marked as readable. Please correct this in the mesh asset's import settings.");

                    hashes[i]                 = default;
                    inputToFilteredMapping[i] = -1;
                }
                else
                {
                    DeclareAssetDependency(blobberData.associatedObject[i], input.mesh);
                    hashes[i] = input.mesh.GetInstanceID();
                }
            }

            new DeduplicateJob { hashes = hashes, inputToFilteredMapping = inputToFilteredMapping }.Run();
            hashes.Dispose();
        }

        [BurstCompile]
        struct DeduplicateJob : IJob
        {
            [ReadOnly] public NativeArray<int> hashes;
            public NativeArray<int>            inputToFilteredMapping;

            public void Execute()
            {
                var map = new NativeHashMap<int, int>(hashes.Length, Allocator.Temp);
                for (int i = 0; i < hashes.Length; i++)
                {
                    if (inputToFilteredMapping[i] < 0)
                        continue;

                    if (map.TryGetValue(hashes[i], out int index))
                        inputToFilteredMapping[i] = index;
                    else
                        map.Add(hashes[i], i);
                }
            }
        }

        protected override void PostFilter(PostFilterBlobberData blobberData, ref TriangleSoupContext context)
        {
            var meshList = new List<Mesh>();

            var converters = blobberData.converters;

            for (int i = 0; i < blobberData.Count; i++)
            {
                var mesh = blobberData.input[i].mesh;
                meshList.Add(mesh);

                converters[i] = new TriangleSoupConverter
                {
                    meshName = mesh.name
                };
            }

            context.meshes = Mesh.AcquireReadOnlyMeshData(meshList);
        }
    }
}
```
