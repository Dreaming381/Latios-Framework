Getting Started with Latios Physics
===================================

This is the first preview version I am releasing out to the public. It currently
only supports a small number of use cases. I am releasing it now to get early
feedback and learn of future users’ use cases, if any.

Authoring
---------

Currently Latios Physics uses the classical Physx components for authoring.
Attach a Sphere Collider and Capsule Collider to the entity you wish to convert.

Colliders in code
-----------------

```csharp
//How to create collider 
 
//Step 1: Create a sphere collider 
var sphere = new SphereCollider(float3.zero, 1f); 
 
//Step 2: Assign it to a collider 
Collider collider = sphere; 
 
//Step 3: Attach collider to an entity 
EntityManager.AddComponentData(sceneGlobalEntity, collider); 
 
//How to extract sphere collider 
 
//Step 1: Check type 
if (collider.type == ColliderType.Sphere) 
{ 
    //Step 2: Assign to specialized type 
    SphereCollider sphere2 = collider; 
 
    //Note: With safety checks enabled, you will get an exception if you cast to the wrong type. 
} 
 
//EZ PZ, right?
```

Simple Queries
--------------

-   Current

    -   Physics.Raycast

    -   Physics.DistanceBetween (Collider vs Collider only)

-   Future

    -   Physics.DistanceBetween (Point vs Collider)

    -   Physics.AreIntersecting

    -   Physics.ColliderCast

    -   Physics.ComputeContacts

    -   Physics.QuadraticCast

    -   Physics.QuadraticColliderCast

Building Collision Layers
-------------------------

*Warning: This API will likely undergo some significant revisions as I am not
happy with it and believe a fluent syntax will be better.*

The first thing you need to do is create an EntityQuery. The EntityQuery must
require Collider and LocalToWorld. (LocalToWorld requirement might be removed in
a future release. I might do an Any(Translation, Rotation, LocalToWorld) or I
might just drop the requirement altogether. Thoughts?)

The second thing you need to do is create a CollisionLayerSettings struct. This
struct has three fields:

-   worldAABB – The AABB from which to construct the multibox

-   worldBucketCountPerAxis – How many “buckets” AKA cells to divide the world
    into along each axis

-   layerType – AABB construction options (this variable should be renamed).
    Options are Discrete and Continuous. Currently are layers are built with the
    Discrete method regardless of this setting.

*Important: worldAABB and worldBucketCountPerAxis MUST match in order when
performing queries across multiple CollisionLayers. I will probably add a safety
check for this in a future release.*

The third thing you need to do is fetch a LayerChunkTypeGroup by calling
Physics.BuildLayerChunkTypeGroup. (This will bye-bye-a-go-go in a future
release)

Finally, call Physics.BuildCollisionLayer and pass in all the variables as well
as specify the CollisionLayer Allocator, input JobHandle, and output variable.
This method returns a JobHandle of the scheduled jobs. CollisionLayers are only
scheduled in parallel currently.

Using FindPairs
---------------

FindPairs is a broadphase algorithm that lets you immediately process pairs with
any narrow phase or other logic you desire. FindPairs has unique threading
properties that ensures the two Entities in the pair can be modified with
ComponentDataFromEntity.

FindPairs uses a fluent syntax. Currently, there are two steps required.

-   Step 1: Call Physics.FindPairs

    -   To find pairs within a layer, only pass in a single CollisionLayer.

    -   To find pairs between two layers (Bipartite), pass in both layers. No
        pairs are generated within each individual layer. It is up to you to
        ensure no entities exist in both layers or else scheduling a parallel
        job will not be safe.

    -   The final argument in FindPairs is an IFindPairsProcessor. Implement
        this interface as a struct containing any NativeContainers your
        algorithm relies upon.

-   Step 2: Call a scheduling method

    -   .RunImmediate – Run without scheduling a job. You can call this from
        inside a job (Not very useful yet, but will be once layer building is
        also supported from within a job).

    -   .Run – Run on the main thread with Burst.

    -   .ScheduleSingle – Run on a worker thread with Burst.

    -   .ScheduleParallel – Run on multiple worker threads with Burst.

Why is FindPairs so slow in a build?
------------------------------------

Great question! Latios Physics internally schedules multiple generic jobs for
FindPairs dynamically. The Burst Compiler currently does not support generic
jobs in builds without explicit declarations of their concrete types.

But fortunately, I have a solution!

Unfortunately, it is a little hacky and may break. It requires two things:

-   All IFindPairsProcessors must be accessible to the assembly named
    Latios.Physics.BurstPatch. You can do this by making them public or by
    making them internal and adding [assembly:
    InternalsVisibleTo("Latios.Physics.BurstPatch")] to your assembly.

-   Create a new BuildPipeline and add Latios-\>Patch Generic Physics Burst Jobs
    somewhere in the pipeline before the BuildPlayer step.

When building, a new folder will be added to your assets named LatiosGenerated
and will contain the generated code and assembly definition file. It is safe to
delete the folder if you run into issues.

If everything works correctly, FindPairs should be working much faster! In Mono
builds, I typically see over a 100x improvement. And on IL2CPP I see a 50x
improvement.
