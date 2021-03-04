# Math

The Math directory in Latios.Core contains several publicly accessible
mathematic utilities:

## Latios Math

`LatiosMath` contains random useful functions I purposely refactored because of
frequent usage. It is best to look directly at the code to see what they do.

## simdFloat3

Often when working in 3D projects, `float3` instances and Unity’s `math` class
operations are very useful. However, they don’t make full use of 4-lane simd
hardware. In addition, using these types automatically disables Burst’s
autovectorization of loops.

`simdFloat3` packs four `float3` instances together into simd registers so that
operations can make use of full simd hardware throughput. You can perform
operator arithmetic on these types just like normal `float3`s, and you can
access some of the useful `math` functions using the `simd` static class
instead. To access any individual `float3`, you can use the letter properties
`a`, `b`, `c`, and `d` to get or set them. There is also swizzling support for
shuffling the order of these `float3` instances. Accessing a component of all
`float3`s can be performed using the `x`, `y`, and `z` properties as usual,
although these use `float4` values as there are four of them.

Operations between `simdFloat3`s and vector types can be confusing. If a
`simdFloat3 p` were to be multiplied by a `float3 v`, the result will be the
equivalent of multiplying each `float3` in `p` by `v`. However, if `p` were
instead multiplied by `float4 s`, the result will be equivalent to performing
`p.a * s.x, p.b * s.y, p.c * s.z, and p.d * s.w`.
