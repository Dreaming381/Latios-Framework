using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// A random number generator that can provide a unique sequence of values for a given unique index for each job scheduled.
    /// This random number generator is especially suited for systems which schedule parallel jobs. Only one instance is required
    /// per job and write-back is not required. It is deterministic.
    /// </summary>
    public struct Rng
    {
        uint m_state;

        /// <summary>
        /// Specify an initial state using a uint
        /// </summary>
        public Rng(uint seed)
        {
            m_state = seed;
        }

        /// <summary>
        /// Specify an initial state using a hashed string, such as the system name that creates this instance.
        /// </summary>
        public Rng(in FixedString128Bytes seedString)
        {
            m_state = math.asuint(seedString.GetHashCode());
        }

        /// <summary>
        /// Specify an initial state using a hashed string, such as the system name that creates this instance.
        /// </summary>
        public Rng(string seedString)
        {
            m_state = math.asuint(seedString.GetHashCode());
        }

        /// <summary>
        /// Alters the random number sequences that will be generated for a given index.
        /// You must call this method once before scheduling a job. The returned instance
        /// is a copy for convenience so you can inline the Shuffle() call with job assignment.
        /// The original instance is still mutated.
        /// </summary>
        /// <returns>A copy of the shuffled Rng for convenience.</returns>
        public Rng Shuffle()
        {
            var sequence = new RngSequence(new uint2(m_state, math.asuint(int.MinValue)));
            m_state      = sequence.NextUInt();
            return this;
        }

        /// <summary>
        /// Gets a random number generation sequence for a given index. Call this inside a job.
        /// </summary>
        /// <param name="index">The index to use for the sequence. Use a deterministic value (like an entityInQueryIndex) if you need deterministic behavior.</param>
        /// <returns></returns>
        public RngSequence GetSequence(int index)
        {
            return new RngSequence(new uint2(math.asuint(index), m_state));
        }

        /// <summary>
        /// A random number generation sequence which can provide a large number of unique random values for various use cases.
        /// </summary>
        public struct RngSequence
        {
            uint2 m_state;

            /// <summary>
            /// Construct a sequence given two seed state values. This method is invoked by Rng.
            /// </summary>
            /// <param name="initialState"></param>
            public RngSequence(uint2 initialState)
            {
                m_state = initialState;
            }

            uint NextState()
            {
                //   From https://www.youtube.com/watch?v=LWFzPP8ZbdU
                //   This version is SquirrelNoise5, which was posted on the author's Twitter: https://twitter.com/SquirrelTweets/status/1421251894274625536.
                //   This is a Unity C# adaptation of SquirrelNoise5 - Squirrel's Raw Noise utilities (version 5).
                //   The following code within this scope is licensed by Squirrel Eiserloh under the Creative Commons Attribution 3.0 license (CC-BY-3.0 US).
                var val    = m_state.x * 0xd2a80a3f;
                val       += m_state.y;
                val       ^= (val >> 9);
                val       += 0xa884f197;
                val       ^= (val >> 11);
                val       *= 0x6c736f4b;
                val       ^= (val >> 13);
                val       += 0xb79f3abb;
                val       ^= (val >> 15);
                val       += 0x1b56c4f5;
                val       ^= (val >> 17);
                m_state.x  = val;
                return val;
            }

            public bool NextBool() => RngToolkit.AsBool(NextState());
            public bool2 NextBool2() => RngToolkit.AsBool2(NextState());
            public bool3 NextBool3() => RngToolkit.AsBool3(NextState());
            public bool4 NextBool4() => RngToolkit.AsBool4(NextState());

            public uint NextUInt() => NextState();
            public uint2 NextUInt2() => new uint2(NextState(), NextState());
            public uint3 NextUInt3() => new uint3(NextState(), NextState(), NextState());
            public uint4 NextUInt4() => new uint4(NextState(), NextState(), NextState(), NextState());
            public uint  NextUInt (uint minInclusive, uint maxExclusive) => RngToolkit.AsUInt(NextState(), minInclusive, maxExclusive);
            public uint2 NextUInt2(uint2 minInclusive, uint2 maxExclusive) => RngToolkit.AsUInt2(NextUInt2(), minInclusive, maxExclusive);
            public uint3 NextUInt3(uint3 minInclusive, uint3 maxExclusive) => RngToolkit.AsUInt3(NextUInt3(), minInclusive, maxExclusive);
            public uint4 NextUInt4(uint4 minInclusive, uint4 maxExclusive) => RngToolkit.AsUInt4(NextUInt4(), minInclusive, maxExclusive);

            public int NextInt() => RngToolkit.AsInt(NextState());
            public int2 NextInt2() => RngToolkit.AsInt2(NextUInt2());
            public int3 NextInt3() => RngToolkit.AsInt3(NextUInt3());
            public int4 NextInt4() => RngToolkit.AsInt4(NextUInt4());
            public int NextInt(int minInclusive, int maxExclusive) => RngToolkit.AsInt(NextState(), minInclusive, maxExclusive);
            public int2 NextInt2(int2 minInclusive, int2 maxExclusive) => RngToolkit.AsInt2(NextUInt2(), minInclusive, maxExclusive);
            public int3 NextInt3(int3 minInclusive, int3 maxExclusive) => RngToolkit.AsInt3(NextUInt3(), minInclusive, maxExclusive);
            public int4 NextInt4(int4 minInclusive, int4 maxExclusive) => RngToolkit.AsInt4(NextUInt4(), minInclusive, maxExclusive);

            public float NextFloat() => RngToolkit.AsFloat(NextState());
            public float2 NextFloat2() => RngToolkit.AsFloat2(NextUInt2());
            public float3 NextFloat3() => RngToolkit.AsFloat3(NextUInt3());
            public float4 NextFloat4() => RngToolkit.AsFloat4(NextUInt4());
            public float NextFloat(float minInclusive, float maxExclusive) => RngToolkit.AsFloat(NextState(), minInclusive, maxExclusive);
            public float2 NextFloat2(float2 minInclusive, float2 maxExclusive) => RngToolkit.AsFloat2(NextUInt2(), minInclusive, maxExclusive);
            public float3 NextFloat3(float3 minInclusive, float3 maxExclusive) => RngToolkit.AsFloat3(NextUInt3(), minInclusive, maxExclusive);
            public float4 NextFloat4(float4 minInclusive, float4 maxExclusive) => RngToolkit.AsFloat4(NextUInt4(), minInclusive, maxExclusive);

            public float2 NextFloat2Direction() => RngToolkit.AsFloat2Direction(NextState());
            public float3 NextFloat3Direction() => RngToolkit.AsFloat3Direction(NextUInt2());
            public quaternion NextQuaternionRotation() => RngToolkit.AsQuaternionRotation(NextUInt3());

            public void ShuffleElements<T>(NativeArray<T> array) where T : unmanaged
            {
                for (int i = 0; i < array.Length - 1; i++)
                {
                    var swapTarget                = NextInt(i, array.Length);
                    (array[i], array[swapTarget]) = (array[swapTarget], array[i]);
                }
            }

            public void ShuffleElements<T, U>(T list) where T : unmanaged, INativeList<U> where U : unmanaged
            {
                for (int i = 0; i < list.Length - 1; i++)
                {
                    var swapTarget              = NextInt(i, list.Length);
                    (list[i], list[swapTarget]) = (list[swapTarget], list[i]);
                }
            }
        }
    }

    public static class SystemRngStateExtensions
    {
        public static void InitSystemRng(this ref SystemState state, uint seed)
        {
            state.EntityManager.AddComponentData(state.SystemHandle, new SystemRng(seed));
        }

        public static void InitSystemRng(this ref SystemState state, FixedString128Bytes seedString)
        {
            state.EntityManager.AddComponentData(state.SystemHandle, new SystemRng(seedString));
        }

        public static SystemRng GetJobRng(this ref SystemState state)
        {
            return state.EntityManager.GetComponentDataRW<SystemRng>(state.SystemHandle).ValueRW.Shuffle();
        }

        public static Rng.RngSequence GetMainThreadRng(this ref SystemState state)
        {
            var srng = GetJobRng(ref state);
            return srng.rng.GetSequence(int.MaxValue);  // Do something most people won't encounter in jobs for extra randomness.
        }
    }

    public struct SystemRng : IComponentData
    {
        public Rng             rng;
        public Rng.RngSequence currentSequence;

        /// <summary>
        /// Specify an initial state using a uint
        /// </summary>
        public SystemRng(uint seed)
        {
            rng             = new Rng(seed);
            currentSequence = default;
        }

        /// <summary>
        /// Specify an initial state using a hashed string, such as the system name that creates this instance.
        /// </summary>
        public SystemRng(in FixedString128Bytes seedString)
        {
            rng             = new Rng(seedString);
            currentSequence = default;
        }

        /// <summary>
        /// Alters the random number sequences that will be generated for a given index.
        /// You must call this method once before scheduling a job. The returned instance
        /// is a copy for convenience so you can inline the Shuffle() call with job assignment.
        /// The original instance is still mutated.
        /// </summary>
        /// <returns>A copy of the shuffled Rng for convenience.</returns>
        public SystemRng Shuffle()
        {
            rng.Shuffle();
            return this;
        }

        /// <summary>
        /// Gets a random number generation sequence for a given index. Call this inside a job, especially in IJobEntityChunkBeginEnd inside OnChunkBegin()
        /// </summary>
        /// <param name="unfilteredChunkIndex">The index to use for the sequence. Use a deterministic value (like unfilteredChunkIndex) if you need deterministic behavior.</param>
        public void BeginChunk(int unfilteredChunkIndex)
        {
            currentSequence = rng.GetSequence(unfilteredChunkIndex);
        }

        public bool  NextBool() => currentSequence.NextBool();
        public bool2 NextBool2() => currentSequence.NextBool2();
        public bool3 NextBool3() => currentSequence.NextBool3();
        public bool4 NextBool4() => currentSequence.NextBool4();

        public uint NextUInt() => currentSequence.NextUInt();
        public uint2 NextUInt2() => currentSequence.NextUInt2();
        public uint3 NextUInt3() => currentSequence.NextUInt3();
        public uint4 NextUInt4() => currentSequence.NextUInt4();
        public uint NextUInt(uint minInclusive, uint maxExclusive) => currentSequence.NextUInt(minInclusive, maxExclusive);
        public uint2 NextUInt2(uint2 minInclusive, uint2 maxExclusive) => currentSequence.NextUInt2( minInclusive, maxExclusive);
        public uint3 NextUInt3(uint3 minInclusive, uint3 maxExclusive) => currentSequence.NextUInt3( minInclusive, maxExclusive);
        public uint4 NextUInt4(uint4 minInclusive, uint4 maxExclusive) => currentSequence.NextUInt4( minInclusive, maxExclusive);

        public int NextInt() => currentSequence.NextInt();
        public int2 NextInt2() => currentSequence.NextInt2();
        public int3 NextInt3() => currentSequence.NextInt3();
        public int4 NextInt4() => currentSequence.NextInt4();
        public int NextInt(int minInclusive, int maxExclusive) => currentSequence.NextInt(minInclusive, maxExclusive);
        public int2 NextInt2(int2 minInclusive, int2 maxExclusive) => currentSequence.NextInt2( minInclusive, maxExclusive);
        public int3 NextInt3(int3 minInclusive, int3 maxExclusive) => currentSequence.NextInt3( minInclusive, maxExclusive);
        public int4 NextInt4(int4 minInclusive, int4 maxExclusive) => currentSequence.NextInt4(minInclusive, maxExclusive);

        public float NextFloat() => currentSequence.NextFloat();
        public float2 NextFloat2() => currentSequence.NextFloat2();
        public float3 NextFloat3() => currentSequence.NextFloat3();
        public float4 NextFloat4() => currentSequence.NextFloat4();
        public float NextFloat(float minInclusive, float maxExclusive) => currentSequence.NextFloat(minInclusive, maxExclusive);
        public float2 NextFloat2(float2 minInclusive, float2 maxExclusive) => currentSequence.NextFloat2( minInclusive, maxExclusive);
        public float3 NextFloat3(float3 minInclusive, float3 maxExclusive) => currentSequence.NextFloat3( minInclusive, maxExclusive);
        public float4 NextFloat4(float4 minInclusive, float4 maxExclusive) => currentSequence.NextFloat4( minInclusive, maxExclusive);

        public float2 NextFloat2Direction() => currentSequence.NextFloat2Direction();
        public float3 NextFloat3Direction() => currentSequence.NextFloat3Direction();
        public quaternion NextQuaternionRotation() => currentSequence.NextQuaternionRotation();

        public void ShuffleElements<T>(NativeArray<T> array) where T : unmanaged => currentSequence.ShuffleElements(array);
        public void ShuffleElements<T, U>(T list) where T : unmanaged, INativeList<U> where U : unmanaged => currentSequence.ShuffleElements<T, U>(list);
    }

    // Todo: In order for the below to work, Unity would need to change the source generators to invoke these interface methods
    // via interface-constrained generics, rather than calling them directly. Calling them directly doesn't export the symbols
    // to the struct type for easy call access.
    // An alternative would be to write a source generator that adds the IJobEntityChunkBeginEnd interface and implementation
    // if it isn't already present. That could even define the SystemRng property, though we'd probably need to validate
    // the SystemRng instance if we went that route.

    // /// <summary>
    // /// An interface to implement in IJobEntity jobs to automatically set up SystemRng.
    // /// You simply need to define a SystemRng autoproperty named "rng" in your job for everything
    // /// to function correctly within the job.
    // /// </summary>
    // public interface IJobEntityRng : IJobEntityChunkBeginEnd
    // {
    //     public SystemRng rng { get; set; }
    //
    //     new public bool OnChunkBegin(in ArchetypeChunk chunk,
    //                                  int unfilteredChunkIndex,
    //                                  bool useEnabledMask,
    //                                  in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
    //     {
    //         var instance = rng;
    //         instance.BeginChunk(unfilteredChunkIndex);
    //         rng = instance;
    //         return true;
    //     }
    //
    //     new public void OnChunkEnd(in ArchetypeChunk chunk,
    //                                int unfilteredChunkIndex,
    //                                bool useEnabledMask,
    //                                in Unity.Burst.Intrinsics.v128 chunkEnabledMask,
    //                                bool chunkWasExecuted)
    //     {
    //     }
    //
    //     bool IJobEntityChunkBeginEnd.OnChunkBegin(in ArchetypeChunk chunk,
    //                                               int unfilteredChunkIndex,
    //                                               bool useEnabledMask,
    //                                               in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
    //     {
    //         var instance = rng;
    //         instance.BeginChunk(unfilteredChunkIndex);
    //         rng = instance;
    //         return true;
    //     }
    //
    //     void IJobEntityChunkBeginEnd.OnChunkEnd(in ArchetypeChunk chunk,
    //                                             int unfilteredChunkIndex,
    //                                             bool useEnabledMask,
    //                                             in Unity.Burst.Intrinsics.v128 chunkEnabledMask,
    //                                             bool chunkWasExecuted)
    //     {
    //     }
    // }
}

