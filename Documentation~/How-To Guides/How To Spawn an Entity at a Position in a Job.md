# How To Spawn an Entity at a Position in a Job

Spawning an entity by itself usually isn’t sufficient. Usually, you need to
initialize some components which make the entity distinct. While this could be
done in a job using `EntityCommandBuffer`, the following approach will typically
result in better sync point performance, especially with larger spawn counts.

## Prerequisites

For this example, we will need some way to identify what prefab to spawn and
when. So for that, we will assume this component exists:

```csharp
public struct Spawner : IComponentData
{
    public EntityWith<Prefab> prefabEntity;
    public bool shouldSpawn;
}
```

The exact details of this component don’t matter much. The important thing is
that we have a prefab entity (`EntityWith<Prefab>` is just a wrapper around the
`Entity` type with an implicit cast to `Entity`) and a condition for spawning.

## The System

Create a new system called `Spawner` using the ECS system script template and
add a `using Latios;` line to the top of the file. Change the base class from
`SystemBase` to `SubSystem` and remove all the code inside of `OnUpdate()`. The
system should now look like this:

```csharp
using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms; 
public class Spawner : SubSystem
{
    protected override void OnUpdate()
    {
        
    }
}
```

Now inside `OnUpdate()`, add the following line:

```csharp
var icb = latiosWorld.syncPoint.CreateInstantiateCommandBuffer<Translation>().AsParallelWriter();
```

This line does three separate things. First, it creates a command buffer that
can instantiate entities as well as set their Translation components. Second, it
fetches the `ParallelWriter` accessor of this command buffer and assigns it to
the local variable `icb`. And third, it registers with the world that this
system created a command buffer, so that any dependencies are automatically
handled.

Next, create the lambda job skeleton. The `entityInQueryIndex` is a required
value.

```csharp
Entities.ForEach((int entityInQueryIndex, in Translation translation, in Spawner spawner) =>
{
    if (spawner.shouldSpawn)
    {

    }
}).ScheduleParallel();
```

Finally, add an instantiate command. In this example, the `translation` is
copied directly from the `spawner`, but this could be any value. Use the
`entityInQueryIndex` as the final `sortKey` argument.

```csharp
icb.Add(spawner.prefabEntity, translation, entityInQueryIndex);
```

And that’s it!

Here’s the full system, short and sweet:

```csharp
using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms; 
public class Spawner : SubSystem
{
    protected override void OnUpdate()
    {
        var icb = latiosWorld.syncPoint.CreateInstantiateCommandBuffer<Translation>().AsParallelWriter();

        Entities.ForEach((int entityInQueryIndex, in Translation translation, in Spawner spawner) =>
        {
            if (spawner.shouldSpawn)
            {
                icb.Add(spawner.prefabEntity, translation, entityInQueryIndex);
            }
        }).ScheduleParallel();
    }
}
```
