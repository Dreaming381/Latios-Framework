# Smart Blobbers

Smart Blobbers provide a powerful, streamlined, and user-friendly workflow for
generating data-intense Blob Assets during Baking. Any authoring logic can
request a Blob Asset from a Smart Blobber and receive a handle. That handle can
later be used to retrieve the Blob Asset after the Smart Blobber has executed.
The Smart Blobber generates blobs in parallel, using Burst if possible. This can
drastically speed up baking times while keeping the logic for generating blob
assets unified and consistent.

## Converting a Blob Asset using an Authoring MonoBehaviour

The following code demonstrates how to generate a
`BlobAssetReference<SkeletonClipSetBlob>` using Kinemation’s Smart Blobber.

```csharp
using Latios.Authoring;
using Latios.Kinemation;
using Latios.Kinemation.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Dragons.Authoring
{
    [DisallowMultipleComponent]
    public class SingleClipAuthoring : MonoBehaviour
    {
        public AnimationClip clip;
    }

    struct SingleClipSmartBakeItem : ISmartBakeItem<SingleClipAuthoring>
    {
        SmartBlobberHandle<SkeletonClipSetBlob> blob;

        public bool Bake(SingleClipAuthoring authoring, IBaker baker)
        {
            baker.AddComponent<SingleClip>();
            var clips = new NativeArray<SkeletonClipConfig>(1, Allocator.Temp);
            clips[0]  = new SkeletonClipConfig { clip = authoring.clip, settings = SkeletonClipCompressionSettings.kDefaultSettings };
            blob      = baker.RequestCreateBlobAsset(baker.GetComponent<Animator>(), clips);
            return true;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentData(entity, new SingleClip { blob = blob.Resolve(entityManager) });
        }
    }

    class SingleClipBaker : SmartBaker<SingleClipAuthoring, SingleClipSmartBakeItem>
    {
    }
}
```

The first thing you may notice is the new interface `ISmartBakeItem`. This
interface defines a method `Bake()` which is called when the `SmartBaker`
executes. At this point, the “bake item” is default-initialized.

In this example, the `SingleClipSmartBakeItem` has a member of type
`SmartBlobberHandle<>`. You can define as many of these as you want, pass them
around wherever, and store them in any component decorated with
`[TemporaryBakingType]`. There is also a `SmartBlobberHandleUntyped` if you need
it.

`RequestCreateBlobAsset<>` is a globally-defined extension method for `IBaker`
which can process a blob asset input request structure. However, most Smart
Blobbers provide custom extension methods of the same name but with explicit
arguments as a convenience. In this example, there is an overload that takes an
Animator and a `NativeArray<SkeletonClipConfig>`. The method returns a
`SmartBlobberHandle`.

`Bake()` returns a `bool`. If you return a value of false, the bake item will be
discarded from further operations. This is useful if your logic conditionally
decides whether it needs to request a blob asset at all. Other operations
performed with baker will still be applied.

The second method in the interface is `PostProcessBlobRequests()`, and is called
after all Smart Blobbers have updated, but before most other types of baking
systems update. The second parameter is the primary entity processed by the
Smart Baker. In this method, you can resolve any `SmartBlobberHandle` as a
`BlobAssetReference` and assign it to components or dynamic buffers. The
`BlobAssetReference` is deduplicated and tracked by the baking process at this
point.

While the `EntityManager` argument passed in lets you do many different things,
it is strongly recommended you only use `Get/Set(Shared)Component` and
`Get/SetBuffer` APIs on either the entity argument or additionally created
entities applied to the Smart Baker and only work with types directly added in
`Bake()`.

Lastly, you need to define the `SmartBaker` type by subclassing `SmartBaker` and
specifying both the authoring component and the `ISmartBakeItem` type as generic
arguments. You do not need to define any other details for this class.

### How does this Smart Baker thing work?

Baking goes through two distinct steps. First, the Bakers themselves are
executed. Then afterwards, baking systems execute. Smart Blobbers can only
receive requests from `Baker`s, and the results are only made available to
baking systems. To avoid making users write custom baking systems, Smart Bakers
automatically generate the code for each step, and provide a stateful “bake
item” to retain context between steps. The “bake item” is actually an
`IComponentData` decorated with [TemporaryBakingType] which is added to a
Baking-Only Entity created by the Smart Baker. By making temporary entities,
each authoring component can have its own bake item.

The Smart Baker will also generate a baking system which queries for the
custom-defined bake item type (plus some internal tracking types added by the
Smart Baker) and dispatch `PostProcessBlobRequests()`.

If you would rather use a custom baking system instead of a Smart Baker, you can
do that too. Simply store the `SmartBlobberHandle` in a custom-defined
`[TemproaryBakingType]` component type and resolve it in your baking system.

## Creating a Simple Smart Blobber

If you have your own custom blob asset types, you may want to create a Smart
Blobber for them. Let’s walk through a simple example.

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

Similar to Smart Bakers, every Smart Blobber blob request creates a baking-only
entity. Then, Smart Blobbers use baking systems to compute blob assets for each
of these blob baking entities. To prepare a request, you must define an
`ISmartBlobberRequestFilter<>`. This filter is responsible for performing
initial validation of inputs, gathering new inputs from the `IBaker`, and
customizing the `blobBakingEntity` for the baking systems.

We will need to define custom components to contain our inputs that we can
attach to our request entity.

```csharp
[TemporaryBakingType]
internal struct DigitsValueInput : IComponentData
{
    public int value;
}

[TemporaryBakingType]
internal struct DigitsElementInput : IBufferElementData
{
    public int digit;
}
```

Now we can define our filter struct.

```csharp
public struct DigitsSmartBlobberRequestFilter : ISmartBlobberRequestFilter<DigitsBlob>
{
    public int   value;
    public int[] digits;

    public bool Filter(IBaker baker, Entity blobBakingEntity)
    {
        if (digits == null)
            return false;

        baker.AddComponent(blobBakingEntity, new DigitsValueInput { value = value });
        var buffer = baker.AddBuffer<DigitsElementInput>(blobBakingEntity).Reinterpret<int>();
        foreach (var digit in digits)
            buffer.Add(digit);
        return true;
    }
}
```

Just like with `ISmartBakeItem.Bake()`, `Filter()` returns a `bool` that allows
for aborting the request.

We could stop here, as the general-purpose `RequestCreateBlobAsset<>()` method
takes an `ISmartBlobberRequestFilter` as input. However, we can supply our own
custom extension method to make the API easier to use.

```csharp
public static class DigitsSmartBlobberBakerExtensions
{
    public static SmartBlobberHandle<DigitsBlob> RequestCreateBlobAsset(this IBaker baker, int value, int[] digits)
    {
        return baker.RequestCreateBlobAsset<DigitsBlob, DigitsSmartBlobberRequestFilter>(new DigitsSmartBlobberRequestFilter { value = value, digits = digits });
    }
}
```

If we wanted to, we could define other filter types and extension methods that
take different sets of inputs for the same blob type, and perhaps even attach
different sets of components to the `blobBakingEntity`. Perhaps we could accept
a `NativeArray<int>` instead of a managed `int[]`. In such a scenario, it is
best practice to allow any native container inputs to be allocated with
`Allocator.Temp`.

At this point, we now have the code in place to receive a blob request, and set
up a special `blobBakingEntity` to hold our filtered inputs. Now we need to
actually generate our blob asset inside a baking system. Every
`blobBakingEntity` that passes the filter will have the following component
added where you should store your generated blob asset:

```csharp
[TemporaryBakingType]
public struct SmartBlobberResult : IComponentData
{
    public UnsafeUntypedBlobAssetReference blob;
}
```

For this simple example, we’ll use a Burst-compiled `ISystem` as our baking
system, and use `IJobEntity` for computing our blob assets.

```csharp
[UpdateInGroup(typeof(Latios.Authoring.Systems.SmartBlobberBakingGroup))]
[BurstCompile]
public partial struct DigitsSmartBlobberSystem : ISystem
{
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new Job().ScheduleParallel();
    }

    [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
    [BurstCompile]
    partial struct Job : IJobEntity
    {
        public void Execute(ref SmartBlobberResult result, in DigitsValueInput valueInput, in DynamicBuffer<DigitsElementInput> bufferInput)
        {
            var     builder = new BlobBuilder(Allocator.Temp);
            ref var root    = ref builder.ConstructRoot<DigitsBlob>();
            root.value      = valueInput.value;
            builder.ConstructFromNativeArray(ref root.digits, bufferInput.Reinterpret<int>().AsNativeArray());
            var typedBlob = builder.CreateBlobAssetReference<DigitsBlob>(Allocator.Persistent);
            result.blob   = Unity.Entities.LowLevel.Unsafe.UnsafeUntypedBlobAssetReference.Create(typedBlob);
        }
    }
}
```

First, notice the `[WithEntityQueryOptions]`. This is a general requirement of
all queries in baking systems, but is an easy thing to forget so it is worth
reiterating.

Second, notice that the system is updated in the `SmartBlobberBakingGroup`. This
is really important, because the magic happens shortly after this point. There
are other baking systems which will read the `SmartBlobberResult` as well as
some additional internal data on the `blobBakingEntity` and perform all of the
blob asset deduplication, incremental allocation tracking, and
`SmartBlobberHandle` resolution logic. The last step is to register the blob
type so that these systems are aware of it. This must be done inside
`OnCreate()` without Burst, by calling `Register()` on a temporary
`SmartBlobberTools<>` instance.

```csharp
    public void OnCreate(ref SystemState state)
    {
        new SmartBlobberTools<DigitsBlob>().Register(state.World);
    }
```

To summarize the steps:

1.  Define components to store the input data for blob asset generation
2.  Use an ISmartBlobberRequestFilter and the generic
    RequestCreateBlobAsset\<\>() extension to configure the blobBakingEntity
    with the input data
3.  Use baking systems to compute blobs and write the results to
    SmartBlobberResult
4.  Register the blob type

And that’s it. Here is the complete code:

```csharp
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

using Latios;
using Latios.Authoring;

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

public struct DigitsBakeItem : ISmartBakeItem<DigitsAuthoring>
{
    SmartBlobberHandle<DigitsBlob> blob;

    public bool Bake(DigitsAuthoring authoring, IBaker baker)
    {
        baker.AddComponent<DigitsBlobReference>();
        blob = baker.RequestCreateBlobAsset(authoring.value, authoring.digits);
        return true;
    }

    public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
    {
        entityManager.SetComponentData(entity, new DigitsBlobReference { blob = blob.Resolve(entityManager) });
    }
}

public class DigitsBaker : SmartBaker<DigitsAuthoring, DigitsBakeItem> { }

// Begin Custom Smart Blobber code

public static class DigitsSmartBlobberBakerExtensions
{
    public static SmartBlobberHandle<DigitsBlob> RequestCreateBlobAsset(this IBaker baker, int value, int[] digits)
    {
        return baker.RequestCreateBlobAsset<DigitsBlob, DigitsSmartBlobberRequestFilter>(new DigitsSmartBlobberRequestFilter { value = value, digits = digits });
    }
}

[TemporaryBakingType]
internal struct DigitsValueInput : IComponentData
{
    public int value;
}

[TemporaryBakingType]
internal struct DigitsElementInput : IBufferElementData
{
    public int digit;
}

public struct DigitsSmartBlobberRequestFilter : ISmartBlobberRequestFilter<DigitsBlob>
{
    public int   value;
    public int[] digits;

    public bool Filter(IBaker baker, Entity blobBakingEntity)
    {
        if (digits == null)
            return false;

        baker.AddComponent(blobBakingEntity, new DigitsValueInput { value = value });
        var buffer                                                        = baker.AddBuffer<DigitsElementInput>(blobBakingEntity).Reinterpret<int>();
        foreach (var digit in digits)
            buffer.Add(digit);
        return true;
    }
}

[UpdateInGroup(typeof(Latios.Authoring.Systems.SmartBlobberBakingGroup))]
[BurstCompile]
public partial struct DigitsSmartBlobberSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        new SmartBlobberTools<DigitsBlob>().Register(state.World);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new Job().ScheduleParallel();
    }

    [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
    [BurstCompile]
    partial struct Job : IJobEntity
    {
        public void Execute(ref SmartBlobberResult result, in DigitsValueInput valueInput, in DynamicBuffer<DigitsElementInput> bufferInput)
        {
            var     builder = new BlobBuilder(Allocator.Temp);
            ref var root    = ref builder.ConstructRoot<DigitsBlob>();
            root.value      = valueInput.value;
            builder.ConstructFromNativeArray(ref root.digits, bufferInput.Reinterpret<int>().AsNativeArray());
            var typedBlob = builder.CreateBlobAssetReference<DigitsBlob>(Allocator.Persistent);
            result.blob   = Unity.Entities.LowLevel.Unsafe.UnsafeUntypedBlobAssetReference.Create(typedBlob);
        }
    }
}
```

### Why not just use Baker.AddBlobAsset()?

For trivially computed blob assets (including our example), using a Smart
Blobber is likely overkill. The overhead of preparing the blobBakingEntity
outweighs the cost of creating the blob immediately. But often, blob assets are
not so trivial to compute. In such cases, Smart Blobbers offer several
advantages.

One advantage is that Blob Assets can be generated in parallel in Burst. There’s
more going on than just copying data to `BlobArray<>` fields.
`BlobBuilder.CreateBlobAssetReference()` does a non-trivial amount of work to
organize the relative offsets of the blob asset and put everything together.
This cost can become significant as the blob asset increases in complexity. The
mehtod also hashes the entire blob data near the end of generation.

The second advantage is that a Smart Blobber can reason about multiple blob
assets at once to further reduce the cost. This is especially useful when
populating a subscene with many copies of a prefab. But it can also be useful
when different types of blob assets can share resources. For example, many of
Kinemation’s Smart Blobbers leverage a “shadow hierarchy” which describes the
hierarchy information of an optimized Animator.

The third advantage is that input transformations can be reasoned about in an
ECS fashion. It may help break down the problem for extremely complex blob
assets.
