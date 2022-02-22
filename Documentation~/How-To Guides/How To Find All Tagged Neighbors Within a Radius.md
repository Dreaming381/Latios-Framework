# How To Find All Tagged Neighbors Within a Radius

Even though DOTS is fast, O(n2) algorithms can still drag down performance. This
technique using Psyshock is much faster and runs in parallel. You can still use
it even if you use another physics solution.

## Prerequisites

For this example, we need a tag for our entities and some place to store the
results. These should do fine:

```csharp
public struct TheTag : IComponentData { }

public struct Neighbor : IBufferElementData
{
    public EntityWith<TheTag> prefabEntity;
}
```

## The Strategy

To make this fast, we’ll be using Psyshock’s FindPairs algorithm. We’ll use
temporary sphere colliders to represent the search radius.

## The Processor

The FindPairs algorithm requires an `IFindPairsProcessor` implementation to
handle generated neighboring pairs. The implementation must be a
Burst-compatible struct.

```csharp
struct FindNeighborsProcessor : IFindPairsProcessor
```

It can use `NativeContainers` with and without attributes just like a job. In
this case, we want write access to the Neighbor buffer. However, writing to a
`BufferFromEntity` in parallel is not allowed, so we use
`PhysicsBufferFromEntity` instead.

```csharp
public PhysicsBufferFromEntity<Neighbor> neighborBfe;
```

Finally, we need to implement the interface’s `Execute` method. FindPairs passes
in a `FindPairsResult` on each invocation, which is just a pair of entities
which might have overlapping sphere colliders.

```csharp
public void Execute(FindPairsResult result)
```

We still need to check if the colliders are actually overlapping to know that
the two neighbors are really within the desired radius of each other. We do that
with `Physics.DistanceBetween` like so:

```csharp
if (Physics.DistanceBetween(result.bodyA.collider, result.bodyA.transform, result.bodyB.collider, result.bodyB.transform, 0f, out _))
```

Lastly, we can add our neighbors like this:

```csharp
{
    neighborBfe[result.entityA].Add(new Neighbor { prefabEntity = new EntityWith<TheTag> { entity = result.entityB } });
    neighborBfe[result.entityB].Add(new Neighbor { prefabEntity = new EntityWith<TheTag> { entity = result.entityA } });
}
```

Overall, our processor looks like this:

```csharp
struct FindNeighborsProcessor : IFindPairsProcessor
{
    public PhysicsBufferFromEntity<Neighbor> neighborBfe;

    public void Execute(FindPairsResult result)
    {
        if (Physics.DistanceBetween(result.bodyA.collider, result.bodyA.transform, result.bodyB.collider, result.bodyB.transform, 0f, out _))
        {
            neighborBfe[result.entityA].Add(new Neighbor { prefabEntity = new EntityWith<TheTag> { entity = result.entityB } });
            neighborBfe[result.entityB].Add(new Neighbor { prefabEntity = new EntityWith<TheTag> { entity = result.entityA } });
        }
    }
}
```

## The System

First, we need to create a new system. Let’s call it `FindNeighborsSystem`. We
will also need a using `Latios.Psyshock;` at the top of our script file. We’ll
also define a constant for our search radius as a member of our system like
this:

```csharp
const float SEARCH_RADIUS = 10f;
```

### Generating Bodies

Now let’s create a `SphereCollider` which represents our search radius. Because
we are testing if two radii intersect rather than if a point is within a radius,
we actually need half this value. The center of the sphere is in local space so
it can be set to zero.

```csharp
var sphereCollider = new SphereCollider(float3.zero, SEARCH_RADIUS / 2f);
```

We need a `NativeArray<ColliderBody>` for our tagged entities. To size this
correctly, we will define an `EntityQuery` member of our system like so:
`EntityQuery m_query;`

Then we need to allocate an array based on the entity count in that query like
this:

```csharp
var bodies = new NativeArray<ColliderBody>(m_query.CalculateEntityCount(), Allocator.TempJob);
```

We need to clear out the `Neighbor` buffers every update. We also need to
populate a `NativeArray<ColliderBody>` with our tagged entities. And we need to
define our `EntityQuery`. All that can be done with this block of code:

```csharp
Entities.WithAll<TheTag>().ForEach((Entity entity, int entityInQueryIndex, ref DynamicBuffer<Neighbor> neighborBuffer, in Translation translation) =>
{
    neighborBuffer.Clear();

    bodies[entityInQueryIndex] = new ColliderBody
    {
        collider = sphereCollider,
        entity = entity,
        transform = new RigidTransform(quaternion.identity, translation.Value)
    };
}).WithStoreEntityQueryInField(ref m_query).ScheduleParallel();
```

### Generating the CollisionLayer

FindPairs requires our bodies array be converted into a `CollisionLayer`. We can
do so like this:

```csharp
Dependency = Physics.BuildCollisionLayer(bodies).ScheduleParallel(out var layer, Allocator.TempJob, Dependency);
Dependency = bodies.Dispose(Dependency);
```

However, for better performance, we can use a settings object to define a 3D
grid over our entities:

```csharp
var settings = new CollisionLayerSettings
{
    worldAABB                = new Aabb(float3.zero, new float3(500f, 500f, 500f)),
    worldSubdivisionsPerAxis = new int3(1, 8, 8)
};
Dependency = Physics.BuildCollisionLayer(bodies).WithSettings(settings).ScheduleParallel(out var layer, Allocator.TempJob, Dependency);
Dependency = bodies.Dispose(Dependency);
```

Entities outside the worldAABB will still be accounted for, so in this case the
settings won’t affect which pairs the algorithm finds. Only performance is
affected.

### Invoking FindPairs

The final step is to invoke FindPairs with our custom `FindNeighborsProcessor`.

```csharp
var processor = new FindNeighborsProcessor
{
    neighborBfe = GetBufferFromEntity<Neighbor>()
};
Dependency = Physics.FindPairs(layer, processor).ScheduleParallel(Dependency);
Dependency = layer.Dispose(Dependency);
```

## Final Result

The full system looks like this:

```csharp
public class FindNeighborsSystem : SystemBase
{
    EntityQuery m_query;

    const float SEARCH_RADIUS = 10f;

    protected override void OnUpdate()
    {
        var sphereCollider = new SphereCollider(float3.zero, SEARCH_RADIUS / 2f);
        var bodies         = new NativeArray<ColliderBody>(m_query.CalculateEntityCount(), Allocator.TempJob);

        Entities.WithAll<TheTag>().ForEach((Entity entity, int entityInQueryIndex, ref DynamicBuffer<Neighbor> neighborBuffer, in Translation translation) =>
        {
            neighborBuffer.Clear();

            bodies[entityInQueryIndex] = new ColliderBody
            {
                collider  = sphereCollider,
                entity    = entity,
                transform = new RigidTransform(quaternion.identity, translation.Value)
            };
        }).WithStoreEntityQueryInField(ref m_query).ScheduleParallel();

        Dependency = Physics.BuildCollisionLayer(bodies).ScheduleParallel(out var layer, Allocator.TempJob, Dependency);
        Dependency = bodies.Dispose(Dependency);

        var processor = new FindNeighborsProcessor
        {
            neighborBfe = GetBufferFromEntity<Neighbor>()
        };
        Dependency = Physics.FindPairs(layer, processor).ScheduleParallel(Dependency);
        Dependency = layer.Dispose(Dependency);
    }

    struct FindNeighborsProcessor : IFindPairsProcessor
    {
        public PhysicsBufferFromEntity<Neighbor> neighborBfe;

        public void Execute(FindPairsResult result)
        {
            if (Physics.DistanceBetween(result.bodyA.collider, result.bodyA.transform, result.bodyB.collider, result.bodyB.transform, 0f, out _))
            {
                neighborBfe[result.entityA].Add(new Neighbor { prefabEntity = new EntityWith<TheTag> { entity = result.entityB } });
                neighborBfe[result.entityB].Add(new Neighbor { prefabEntity = new EntityWith<TheTag> { entity = result.entityA } });
            }
        }
    }
}
```
