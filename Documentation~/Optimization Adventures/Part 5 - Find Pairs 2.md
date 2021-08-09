# Optimization Adventures: Part 5 – Find Pairs 2

Don’t get your hopes up. This is not the mega adventure where I fix the
single-threaded bottleneck and add all the remaining optimizations. I need Burst
1.5 for that, and Latios Framework 0.4 still supports Burst 1.4.1.

Actually, the 0.4 release didn’t feature any optimization effort. “Performance
by default” really is good enough in a lot of situations. And for the rest, I
want Burst 1.5. It has a ton of tools that I requested. But the DOTS team is
taking its time to force everyone to update. So instead, I guess now is as good
of a time as any to talk about data layout, and set up our simplified
`AabbLayer` for success.

## Where We Left Off

Last time, we employed SIMD to turn four comparisons into one. That got rid of a
bunch of branching logic and brought our inner loop instruction count down to
12\. But 8 of those were just loading the SIMD registers. Back then, I was using
Burst 1.2. Burst has come a long way since then, so I reran everything with the
newer Burst version. Here’s the new version of the inner loop:

```asm
#=== FindPairsSimplified.cs(188, 1)   float4 less = new float4(current.aabb.max.y, aabbs[j].aabb.max.y, current.aabb.max.z, aabbs[j].aabb.max.z);

vinsertps        xmm0, xmm7, dword ptr [rbp - 12], 16
vinsertps        xmm0, xmm0, xmm10,                32
vinsertps        xmm0, xmm0, dword ptr [rbp - 8],  48

#=== FindPairsSimplified.cs(189, 1)   float4 more = new float4(aabbs[j].aabb.min.y, current.aabb.min.y, aabbs[j].aabb.min.z, current.aabb.min.z);

vmovss           xmm1, dword ptr [rbp - 24]
vinsertps        xmm1, xmm1,                xmm8,                 16
vinsertps        xmm1, xmm1,                dword ptr [rbp - 20], 32
vinsertps        xmm1, xmm1,                xmm9,                 48

#=== FindPairsSimplified.cs(191, 1)   if (math.bitmask(less < more) == 0)

vcmpltps         xmm0, xmm0, xmm1
vmovmskps        eax,  xmm0
test             al,   al
jne              .LBB0_32
```

Look at that! One of the loading operations is gone! In this version of Burst,
we are down to 11 instructions.

And as far as performance goes, here’s what I got when profiling the full suite:

| Element Count (time units) | Naïve   | SIMD    | Speedup |
|----------------------------|---------|---------|---------|
| 10 (µs)                    | 4.9     | 3.5     | 1.4     |
| 20 (µs)                    | 13.2    | 5.5     | 2.4     |
| 50 (µs)                    | 5.6     | 3.7     | 1.5     |
| 100 (µs)                   | 27.1    | 14.2    | 1.9     |
| 200 (µs)                   | 34.9    | 13.4    | 2.6     |
| 500 (µs)                   | 373.2   | 194.7   | 1.9     |
| 1000 (ms)                  | 1.2486  | 0.6273  | 2.0     |
| 2000 (ms)                  | 1.8628  | 0.7985  | 2.3     |
| 5000 (ms)                  | 21.5    | 4.6555  | 4.6     |
| 10000 (ms)                 | 65.07   | 18.35   | 3.5     |
| 20000 (ms)                 | 177.82  | 74.9    | 2.4     |
| 50000 (s)                  | 1.29241 | 0.65342 | 2.0     |

Overall, that’s an average speedup of 2.4x. This isn’t the most robust test as
it is only single iteration after Burst is warmed up, but I’m too lazy to care
much more.

## Smells Like a Bad Data Layout

We have 8 values we need in our comparisons, and Burst is using 7 scalar loads
to populate them. There are SIMD load instructions, which means we could
theoretically load 8 values in just 2 instructions. We’d still have to rearrange
the values into our `less` and `more` SIMD registers, but that shouldn’t take 5
instructions. So is this possible? Well let’s look at our data layout and see if
we can’t find some instructions that make this work. I have bolded the data we
actually care about. Remember, we need to load two of these.

-   AabbEntity
    -   Aabb.min.x
    -   **Aabb.min.y**
    -   **Aabb.min.z**
    -   Aabb.max.x
    -   **Aabb.max.y**
    -   **Aabb.max.z**
    -   Entity.Index
    -   Entity.Version

So here’s something you need to know about SIMD loads. On most hardware, the
four values loaded have to be at adjacent memory addresses. And well, `max.x` is
kinda ruining that. I said in our first adventure that our data layout sucks,
and this is why. We now need 4 loads just to get the data into registers. And
then we still need to rearrange them. You could try and do that and maybe see if
you can get everything in the right spot with 6 instructions. But I would much
rather fix the problem at its source. Let’s change the data layout to something
like this:

-   AabbEntity
    -   Aabb.min.x
    -   Aabb.max.x
    -   **Aabb.min.y**
    -   **Aabb.min.z**
    -   **Aabb.max.y**
    -   **Aabb.max.z**
    -   Entity.Index
    -   Entity.Version

That looks like this in code:

```csharp
public struct AabbEntityRearranged : IComparable<AabbEntityRearranged>
{
    public float2 minXmaxX;
    public float4 minYZmaxYZ;
    public Entity entity;

    public int CompareTo(AabbEntityRearranged other)
    {
        return minXmaxX.x.CompareTo(other.minXmaxX.x);
    }
}
```

And here’s what the job now looks like. Some of the indexing might be a little
confusing because `xyzw` of a `float4` don’t correspond to `xyz` of two
`float3`s.

```csharp
[BurstCompile]
public struct RearrangedSweep : IJob
{
    [ReadOnly] public NativeArray<AabbEntityRearranged> aabbs;
    public NativeList<EntityPair>                       overlaps;

    public void Execute()
    {
        for (int i = 0; i < aabbs.Length - 1; i++)
        {
            AabbEntityRearranged current = aabbs[i];

            for (int j = i + 1; j < aabbs.Length && aabbs[j].minXmaxX.x <= current.minXmaxX.y; j++)
            {
                float4 less = new float4(current.minYZmaxYZ.z, aabbs[j].minYZmaxYZ.z, current.minYZmaxYZ.w, aabbs[j].minYZmaxYZ.w);
                float4 more = new float4(aabbs[j].minYZmaxYZ.x, current.minYZmaxYZ.x, aabbs[j].minYZmaxYZ.y, current.minYZmaxYZ.y);

                if (math.bitmask(less < more) == 0)
                {
                    overlaps.Add(new EntityPair(current.entity, aabbs[j].entity));
                }
            }
        }
    }
}
```

Let’s see what Burst does with this:

```csharp
#=== FindPairsSimplified.cs(214, 1)   float4 less = new float4(current.minYZmaxYZ.z, aabbs[j].minYZmaxYZ.z, current.minYZmaxYZ.w, aabbs[j].minYZmaxYZ.w);

vinsertps        xmm0, xmm7, dword ptr [rbp - 12], 16
vinsertps        xmm0, xmm0, xmm10,                32
vinsertps        xmm0, xmm0, dword ptr [rbp - 8],  48

#=== FindPairsSimplified.cs(215, 1)   float4 more = new float4(aabbs[j].minYZmaxYZ.x, current.minYZmaxYZ.x, aabbs[j].minYZmaxYZ.y, current.minYZmaxYZ.y);

vmovss           xmm1, dword ptr [rbp - 20]
vinsertps        xmm1, xmm1,                xmm8,                 16
vinsertps        xmm1, xmm1,                dword ptr [rbp - 16], 32
vinsertps        xmm1, xmm1,                xmm9,                 48

#=== FindPairsSimplified.cs(217, 1)   if (math.bitmask(less < more) == 0)

vcmpltps         xmm0, xmm0, xmm1
vmovmskps        eax,  xmm0
test             al,   al
jne              .LBB0_32
```

Dang it! It seems that wasn’t enough to convince Burst about what we are trying
to do.

## Everybody Do the Shuffle!

The issue we seem to be running into is that Burst is forgetting about our
inputs both being `float4` when we load them into `less` and `more`. I think I
know why this is, but it is likely a bug, which I will explain later. However,
there’s a solution to this problem. We can force Burst to treat our inputs as
`float4` types by using `math` intrinsics. Remember, our goal is to perform two
loads and then rearrange them into `less` and `more`, so let’s pick an intrinsic
that does that rearranging step. Our intrinsic of choice?

`math.shuffle()`

The new code for creating less and more looks like this:

```csharp
float4 less = math.shuffle(current.minYZmaxYZ,
                           aabbs[j].minYZmaxYZ,
                           math.ShuffleComponent.LeftZ,
                           math.ShuffleComponent.RightZ,
                           math.ShuffleComponent.LeftW,
                           math.ShuffleComponent.RightW);
float4 more = math.shuffle(current.minYZmaxYZ,
                           aabbs[j].minYZmaxYZ,
                           math.ShuffleComponent.RightX,
                           math.ShuffleComponent.LeftX,
                           math.ShuffleComponent.RightY,
                           math.ShuffleComponent.LeftY);
```

And apparently, that was the kick Burst needed, because it did this:

```asm
#=== FindPairsSimplified.cs(214, 1)  float4 less = math.shuffle(current.minYZmaxYZ,
vmovlps          xmm0, xmm7, qword ptr [rbp - 12] # xmm0 = mem[0,1],xmm7[2,3]
vpermilps        xmm0, xmm0, 114                  # xmm0 = xmm0[2,0,3,1]
vmovsd           xmm1, qword ptr [rbp - 20]       # xmm1 = mem[0],zero
vunpcklps        xmm1, xmm1, xmm8                 # xmm1 = xmm1[0],xmm8[0],xmm1[1],xmm8[1]
vcmpltps         xmm0, xmm0, xmm1
vmovmskps        eax,  xmm0
test             al,   al
jne              .LBB0_32
```

We have a couple new instructions here. Let’s break them down.

Reading from right to left, `vmovlps`, pronounced “move low packed singles”
takes the bottom half of the first argument, the top half of the second
argument, and mashes them together into `xmm0`.

The next instruction, `vpermilps`, pronounced “permute in lane packed singles”,
rearranges the elements in the register based on some constant code, which is
what that `114` means. At this point, `xmm0` contains `less`.

The third instruction, `vmovsd`, pronounced “move single double” is treating two
floats as a double and sticking them in the lower half of `xmm1`.

And lastly, `vunpcklps`, pronounced “unpack low packed singles” takes two SIMD
registers, grabs the elements in their lower halves, and interleaves them into
the resulting register, which is `xmm1` now containing `more`.

There’s a lot of this half-register loading shenanigans going on, which we never
described. It seems Burst is still disregarding the innate structure of `float4`
anyways and jumping to the raw data. In fact, normally I get this kind of result
out of Burst with just the `float4` constructors instead of using `shuffle()`.
Why `shuffle()` is needed here is something I can’t fully answer. Either way, we
are now down to 8 instructions. But are they fast instructions?

| Element Count (time units) | Naïve   | SIMD    | Rearrange | Naïve -\> SIMD | SIMD -\> Rearrange | Total Speedup |
|----------------------------|---------|---------|-----------|----------------|--------------------|---------------|
| 10 (µs)                    | 6.6     | 3       | 2.7       | 2.2            | 1.1                | 2.4           |
| 20 (µs)                    | 14.7    | 5.8     | 5.5       | 2.5            | 1.1                | 2.7           |
| 50 (µs)                    | 7.1     | 3.1     | 3.2       | 2.3            | 1.0                | 2.2           |
| 100 (µs)                   | 42.8    | 14.4    | 13        | 3.0            | 1.1                | 3.3           |
| 200 (µs)                   | 30.9    | 14.1    | 12.5      | 2.2            | 1.1                | 2.5           |
| 500 (µs)                   | 139     | 58.7    | 45.3      | 2.4            | 1.3                | 3.1           |
| 1000 (ms)                  | 1.6678  | 0.6313  | 0.6031    | 2.6            | 1.0                | 2.8           |
| 2000 (ms)                  | 1.9853  | 0.8166  | 0.5863    | 2.4            | 1.4                | 3.4           |
| 5000 (ms)                  | 29.51   | 5.1975  | 4.7296    | 5.7            | 1.1                | 6.2           |
| 10000 (ms)                 | 45.11   | 21.87   | 14.93     | 2.1            | 1.5                | 3.0           |
| 20000 (ms)                 | 193.38  | 79.54   | 61.42     | 2.4            | 1.3                | 3.1           |
| 50000 (s)                  | 1.18299 | 0.62556 | 0.5481    | 1.9            | 1.1                | 2.2           |

The average speedups were 2.6, 1.2 and 3.1 respectively. Yup, there’s definitely
some noise in there. But also, there’s a clear speedup with this new approach.

We broke the 3x threshold!

## Look What the Cache Dragged In

Just by rearranging the data, we were able to achieve noticeable speedups. But
those speedups came from helping Burst use better instructions. Memory bandwidth
may also be a concern, and we haven’t really considered it much here.

You see, our entire `AabbEntityRearranged` fits into a cache line. Actually, a
cache line can fit two of them. That means from a bandwidth standpoint, every
time we touch a single part of the `AabbEntityRearranged`, we end up loading the
whole thing. So are we being efficient with it?

Well, if any `less` is less than `more`, we don’t create an `EntityPair` and
consequently don’t touch the `entity`. That’s a quarter of our bandwidth wasted.

What if our current box doesn’t overlap with any box along the x-axis? In that
case, we don’t even need our `minYZmaxYZ` which is half the struct.

And while I won’t go into too much depth here, the bipartite version of the
algorithm has to do a catch-up step where it aligns the x-axis boxes between the
two groups. And during that step, it can even skip touching `max.x`.

So to avoid always loading unnecessary data, we need to pull those optional
pieces apart using a struct-of-arrays structure, or SOA for short. At this
point, our job becomes our “struct” and our new code looks like this:

```csharp
[BurstCompile]
public struct SoaSweep : IJob
{
    [ReadOnly] public NativeArray<float>  xmins;
    [ReadOnly] public NativeArray<float>  xmaxs;
    [ReadOnly] public NativeArray<float4> minYZmaxYZs;
    [ReadOnly] public NativeArray<Entity> entities;
    public NativeList<EntityPair>         overlaps;

    public void Execute()
    {
        for (int i = 0; i < xmins.Length - 1; i++)
        {
            float4 current = minYZmaxYZs[i];

            for (int j = i + 1; j < xmaxs.Length && xmins[j] <= xmaxs[i]; j++)
            {
                float4 less = new float4(current.z, minYZmaxYZs[j].z, current.w, minYZmaxYZs[j].w);
                float4 more = new float4(minYZmaxYZs[j].x, current.x, minYZmaxYZs[j].y, current.y);

                if (math.bitmask(less < more) == 0)
                {
                    overlaps.Add(new EntityPair(entities[i], entities[j]));
                }
            }
        }
    }
}
```

And then Burst decided to do this:

```asm
vmovups          xmm0, xmmword ptr [rdi + 4*r14 + 16]
vinsertps        xmm1, xmm7, xmm0, 156                # xmm1 = xmm7[0],xmm0[2],zero,zero
vinsertps        xmm1, xmm1, xmm6, 232                # xmm1 = xmm1[0,1],xmm6[3],zero
vblendps         xmm1, xmm1, xmm0, 8                  # xmm1 = xmm1[0,1,2],xmm0[3]
vunpcklps        xmm0, xmm0, xmm6                     # xmm0 = xmm0[0],xmm6[0],xmm0[1],xmm6[1]
vcmpltps         xmm0, xmm1, xmm0
vmovmskps        eax,  xmm0
test             al,   al
jne              .LBB0_32
```

## BURST! WHY ARE YOU LIKE THIS?

Seriously! What is Burst doing? It decided to use SIMD loads for part of the
data and scalar loads for the other. This is definitely a bug. I seem to be
hitting all the Burst bugs in this adventure. I promise you; I usually don’t
encounter this. Burst is often much better behaved.

So I profiled this version, just to see if maybe there was something Burst knew
that I didn’t. The results were slightly biased towards this new version, but
there were times our previous king still won, so it wasn’t very conclusive. But
what I did learn was that despite Burst slacking and leaving an extra
instruction in the loop, the SOA structure was compensating for it. There was
some promise. And so I gave Burst another kick with the shuffles.

```asm
vmovups          xmm0, xmmword ptr [rdi + 4*r14 + 16]
vunpckhps        xmm1, xmm6, xmm0                     # xmm1 = xmm6[2],xmm0[2],xmm6[3],xmm0[3]
vunpcklps        xmm0, xmm0, xmm6                     # xmm0 = xmm0[0],xmm6[0],xmm0[1],xmm6[1]
vcmpltps         xmm0, xmm1, xmm0
vmovmskps        eax,  xmm0
test             al,   al
jne              .LBB0_32
```

Whoah!

7 instructions!

That’s a new record!

That’s better than the official FindPairs!

No seriously, it is better in that regard. This version loads and keeps
`minYZmaxYZ[i]` in the outer loop, whereas the official FindPairs reloads it in
the inner loop every time. I think I know why, and I have a plan to fix it. But
for now, let’s see what this new version does to our timings!

| Element Count (time units) | Naïve  | Rearrange | SoaShuffle | Naïve -\> Rearrange | Rearrange -\> SoaShuffle | Total Speedup |
|----------------------------|--------|-----------|------------|---------------------|--------------------------|---------------|
| 10 (µs)                    | 11.4   | 5         | 5.1        | 2.3                 | 1.0                      | 2.2           |
| 20 (µs)                    | 6.1    | 2.5       | 3.1        | 2.4                 | 0.8                      | 2.0           |
| 50 (µs)                    | 17.9   | 7.4       | 7.9        | 2.4                 | 0.9                      | 2.3           |
| 100 (µs)                   | 13.5   | 5.5       | 5.9        | 2.5                 | 0.9                      | 2.3           |
| 200 (µs)                   | 78.6   | 27.4      | 25.9       | 2.9                 | 1.1                      | 3.0           |
| 500 (µs)                   | 349.7  | 109.9     | 97.7       | 3.2                 | 1.1                      | 3.6           |
| 1000 (ms)                  | 1.0108 | 0.3543    | 0.2971     | 2.9                 | 1.2                      | 3.4           |
| 2000 (ms)                  | 4.0707 | 1.1876    | 1.0107     | 3.4                 | 1.2                      | 4.0           |
| 5000 (ms)                  | 17.75  | 3.6338    | 3.2021     | 4.9                 | 1.1                      | 5.5           |
| 10000 (ms)                 | 53.75  | 13.36     | 12.06      | 4.0                 | 1.1                      | 4.5           |
| 20000 (ms)                 | 192.56 | 59.27     | 52.2       | 3.2                 | 1.1                      | 3.7           |
| 50000 (s)                  | 1.1898 | 0.52153   | 0.45988    | 2.3                 | 1.1                      | 2.6           |

The average speedups are 3.0, 1.1, and 3.3 respectively.

Sure enough, this version wins. It is hard to tell if the gain came from the
smarter memory or the instruction difference. But that doesn’t really matter.
We’re faster. In fact, if we only look at 500 elements and above, we are nearly
4x faster than the naïve version! If we keep going, we might have a real shot at
breaking that 5x barrier!

But don’t let that statistic fool you. Those lower element counts aren’t noise.
Notice how there’s a consistent speedup with the newer algorithms even at this
lower threshold?

One explanation for that is that these newer versions have less instructions to
load. The naïve version had a whole bunch of branches everywhere and was doing
everything with scalar instructions. Our SIMD operations take up less
instruction memory, and so the algorithm loads faster. This might also be why
our `SoaShuffle` doesn’t perform as well as previous algorithms. In order to
achieve a tight inner loop, the outer loop logic suffers.

Pierre Terdiman further optimizes his version by counting x86 micro-ops,
unrolling the inner loop, and optimizing the list adding function. Those ideas
work, and his algorithm’s speed is one to fear. However, I think I might have a
better idea, one that will play a little more friendly with Burst. But that’s
for a later adventure. I kicked around this old version of Burst enough for one
adventure.

## Try It Yourself!

You can find the code samples for this Optimization Adventure inside the
Optimization Adventures directory in this package. It includes performance tests
you can run in the Test Runner window. Please note that the performance
characteristics and Burst output will likely be different if using a different
Burst version.
