# Rng and RngToolkit

`Rng` is a Burst-friendly type designed for providing deterministic random
numbers to parallel jobs. `Rng` utilizes `RngToolkit`, which is a collection of
functions for mapping `uint` values to other types and ranges.

## Using Rng

The simplest way to explain `Rng` is by example. So let’s assume we have a tag
component called `RandomizeTag`. Our goal is to write a system that randomizes
the `Translation` and `Rotation` of all entities with that tag.

The first step is to create our `Rng` instance as a private field in the system.

```csharp
Rng m_rng;
```

Next, we need to seed our instance. We could have seeded it when we declared it,
but for extra determinism we will seed it at the start of every seed. This seed
should be hardcoded, but we want to make sure it is different for every system.
To help detect copy and paste errors, a string can be passed in instead of a
`uint`. A good practice is to pass in the name of the system.

```csharp
public override void OnNewScene() => m_rng = new Rng("RandomizeTransformSystem");
```

In our `OnUpdate()`, we copy the instance into a local variable so it can be
captured in our `Entities.ForEach()` lambda.

```csharp
var rng = m_rng;
```

Our `Entities.ForEach()` will be a parallel job, so we need to ensure each
entity gets unique random results. We do this by calling `GetSequence()` which
returns an `RngSequence` instance.

```csharp
Entities.WithAll<RandomizeTag>().ForEach((int entityInQueryIndex, ref Translation translation) =>
{
    var random = rng.GetSequence(entityInQueryIndex);
```

With this sequence, we can make consecutive calls to its random functions. The
API matches `Unity.Mathematics.Random`.

```csharp
    var direction     = random.NextFloat3Direction();
    var magnitude     = random.NextFloat(0f, 100f);
    translation.Value = direction * magnitude;
}).ScheduleParallel();
```

`Rng` does not allocate any memory. That means if we were to pass it to another
job and make the same calls, we would get the same results. To avoid this, we
call `Shuffle()` to get new sequences for corresponding indices. For
convenience, this method also returns itself after the update.

```csharp
rng = m_rng.Shuffle();
```

Now we can use it in our second `Entities.ForEach()` job like so:

```csharp
Entities.WithAll<RandomizeTag>().ForEach((int entityInQueryIndex, ref Rotation rotation) =>
{
    var random     = rng.GetSequence(entityInQueryIndex);
    rotation.Value = random.NextQuaternionRotation();
}).ScheduleParallel();
```

Finally, we call `Shuffle()` one last time so that the next frame we get more
new random values.

```csharp
m_rng.Shuffle();
```

Altogether, the code looks like this:

```csharp
struct RandomizeTag : IComponentData { }

public class RandomizeTransformsSystem : SubSystem
{
    Rng m_rng;

    public override void OnNewScene() => m_rng = new Rng("RandomizeTransformSystem");

    protected override void OnUpdate()
    {
        var rng = m_rng;

        Entities.WithAll<RandomizeTag>().ForEach((int entityInQueryIndex, ref Translation translation) =>
        {
            var random        = rng.GetSequence(entityInQueryIndex);
            var direction     = random.NextFloat3Direction();
            var magnitude     = random.NextFloat(0f, 100f);
            translation.Value = direction * magnitude;
        }).ScheduleParallel();

        rng = m_rng.Shuffle();

        Entities.WithAll<RandomizeTag>().ForEach((int entityInQueryIndex, ref Rotation rotation) =>
        {
            var random     = rng.GetSequence(entityInQueryIndex);
            rotation.Value = random.NextQuaternionRotation();
        }).ScheduleParallel();

        m_rng.Shuffle();
    }
}
```

## Rng Randomness

You might be wondering if this new random number generator is prone to repeating
sequences or other flaws, especially since it promises deterministic parallel
unique random numbers every update. Some of that will be answered by this [GDC
Presentation](https://www.youtube.com/watch?v=LWFzPP8ZbdU), which was the source
of inspiration for this solution.

In that presentation, Squirrel Eiserloh presents a noise function which takes
both a `seed` and a `position` as arguments. Repeatedly incrementing the
`position` generates a sequence of random values. However, using the previous
raw random value as the next `position` value generates a completely different
sequence. These two principles are combined to generate separate sequences
corresponding to unique indices. Technically, these sequences are all part of
one long sequence starting at different points. But the likelihood of the
sequences overlapping in the common use case is quite small, since typically
only a small quantity of random numbers is needed for a given entity in a given
job. Also, `Unity.Mathematics.Random` has the same limitation.

But so far, we have only discussed modifying the `position`. All of those
sequences completely change when the `seed` is modified. And that’s what
`Shuffle()` does. While it is possible that two systems could get their `Rng`
seeds aligned, this is rare and often either transient or easily remedied by
changing one of the initial seeds.

Regarding the actual noise function, Squirrel Eiserloh has since released an
improved generator using the same interface as the one he presented at GDC. This
improved version called “SquirrelNoise5” is what powers `Rng`.

## RngToolkit

Most random number generators used in games generate `uint` values. However,
`uint` values are not commonly useful in simulations. These raw values need to
be converted into indices, floating point values, directions, and even
quaternion rotations. The math to remap `uint` values to these more useful forms
is always the same, but it isn’t always trivial. That’s why `RngToolkit` exists.
It contains a bunch of methods to perform these remappings.

Hopefully it will save you time and trouble when building out an API for any
random number or noise generator you may have.
