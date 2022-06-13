# Getting Started with Kinemation – Part 4

Optimized skeletons. They are fast. But their `OptimizedBoneToRoot` buffers can
be quite raw. In this part, we’ll write some code to work with them.

## Making an Optimized Skeleton via Import

Before we write any code though, we need to change our skeleton to be optimized.
We can do so by checking this box.

![](media/9b9ac7015305136fcf73d455ff2cd183.png)

Of course, if we wanted to have additional exported bones to attach weapons or
accessories, we could do that with the dropdown below it.

## Writing to OptimizedBoneToRoot without ParentScaleInverse

We’re going to assume our character does not use `ParentScaleInverse`, as that
complicates the code quite a bit in Kinemation’s current state.

Fortunately, our authoring component is already set up for us. We don’t have to
worry about that.

In order to compute our optimized bone matrices, we’ll need the hierarchy
information provided by the `OptimizedSkeletonHierarchyBlobReference`. Our code
now looks like this:

```csharp
Entities.ForEach((ref DynamicBuffer<OptimizedBoneToRoot> btrBuffer, in OptimizedSkeletonHierarchyBlobReference hierarchyRef, in SingleClip singleClip) =>
{
    ref var clip     = ref singleClip.blob.Value.clips[0];
    var     clipTime = clip.LoopToClipTime(t);

                
}).ScheduleParallel();
```

With optimized skeletons, we don’t have to worry about modifying the world-space
transforms anymore. We still need to watch out for root motion animation, but if
we don’t have that, we can start our loop at 0. However, there’s no point since
the first matrix should always be the identity matrix, which conversion kindly
set for us. Don’t believe me? Well let’s temporarily remove our Single Clip
Authoring and look at the buffer in the inspector.

![](media/723f64ab1435586644a2f46c11c58007.png)

And now you have probably seen that even optimized characters keep their pose
during conversion. So go ahead and explore the rest of the components.

Alright, before we write our loop, let’s convert our buffer into its underlying
type so it is easier to work with:

```csharp
var bones = btrBuffer.Reinterpret<float4x4>().AsNativeArray();

for (int i = 1; i < bones.Length; i++)
{
    var boneTransform = clip.SampleBone(i, clipTime);


}
```

Next, we need to convert our `boneTransform` into a matrix. We can do that using
the `TRS()` method.

```csharp
var mat = float4x4.TRS(boneTransform.translation, boneTransform.rotation, boneTransform.scale);
```

Finally, we need to get the matrix into the skeleton’s coordinate space. To do
that, we need to multiply the matrix by bone’s parent matrix, and then by that
bone’s parent matrix, and then by that bone’s parent matrix, all the way up to
the root.

But there’s a trick. If the parent has a fully computed `OptimizedBoneToRoot`
matrix, we only need to multiply our matrix with that. And if we iterated
through our buffer from start to end, that will always be the case. Kinemation
conversion orders the bones such that each parent is always before its children.
That means we just have to do this:

```csharp
var parentIndex = hierarchyRef.blob.Value.parentIndices[i];
bones[i]        = math.mul(bones[parentIndex], mat);
```

Here’s what it looks like in its final form:

```csharp
using Latios;
using Latios.Kinemation;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Dragons
{
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class SingleClipPlayerSystem : SubSystem
    {
        protected override void OnUpdate()
        {
            float t = (float)Time.ElapsedTime;

            Entities.ForEach((ref DynamicBuffer<OptimizedBoneToRoot> btrBuffer, in OptimizedSkeletonHierarchyBlobReference hierarchyRef, in SingleClip singleClip) =>
            {
                ref var clip     = ref singleClip.blob.Value.clips[0];
                var     clipTime = clip.LoopToClipTime(t);

                var bones = btrBuffer.Reinterpret<float4x4>().AsNativeArray();

                for (int i = 1; i < bones.Length; i++)
                {
                    var boneTransform = clip.SampleBone(i, clipTime);

                    var mat         = float4x4.TRS(boneTransform.translation, boneTransform.rotation, boneTransform.scale);
                    var parentIndex = hierarchyRef.blob.Value.parentIndices[i];
                    bones[i]        = math.mul(bones[parentIndex], mat);
                }
            }).ScheduleParallel();
        }
    }
}
```

## What’s Next

Kinemation is new, and its potential is still being explored. Let me know what
you would like to see showcased next!
