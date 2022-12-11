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
            public uint  NextUInt (uint min, uint max) => RngToolkit.AsUInt(NextState(), min, max);
            public uint2 NextUInt2(uint2 min, uint2 max) => RngToolkit.AsUInt2(NextUInt2(), min, max);
            public uint3 NextUInt3(uint3 min, uint3 max) => RngToolkit.AsUInt3(NextUInt3(), min, max);
            public uint4 NextUInt4(uint4 min, uint4 max) => RngToolkit.AsUInt4(NextUInt4(), min, max);

            public int NextInt() => RngToolkit.AsInt(NextState());
            public int2 NextInt2() => RngToolkit.AsInt2(NextUInt2());
            public int3 NextInt3() => RngToolkit.AsInt3(NextUInt3());
            public int4 NextInt4() => RngToolkit.AsInt4(NextUInt4());
            public int NextInt(int min, int max) => RngToolkit.AsInt(NextState(), min, max);
            public int2 NextInt2(int2 min, int2 max) => RngToolkit.AsInt2(NextUInt2(), min, max);
            public int3 NextInt3(int3 min, int3 max) => RngToolkit.AsInt3(NextUInt3(), min, max);
            public int4 NextInt4(int4 min, int4 max) => RngToolkit.AsInt4(NextUInt4(), min, max);

            public float NextFloat() => RngToolkit.AsFloat(NextState());
            public float2 NextFloat2() => RngToolkit.AsFloat2(NextUInt2());
            public float3 NextFloat3() => RngToolkit.AsFloat3(NextUInt3());
            public float4 NextFloat4() => RngToolkit.AsFloat4(NextUInt4());
            public float NextFloat(float min, float max) => RngToolkit.AsFloat(NextState(), min, max);
            public float2 NextFloat2(float2 min, float2 max) => RngToolkit.AsFloat2(NextUInt2(), min, max);
            public float3 NextFloat3(float3 min, float3 max) => RngToolkit.AsFloat3(NextUInt3(), min, max);
            public float4 NextFloat4(float4 min, float4 max) => RngToolkit.AsFloat4(NextUInt4(), min, max);

            public float2 NextFloat2Direction() => RngToolkit.AsFloat2Direction(NextState());
            public float3 NextFloat3Direction() => RngToolkit.AsFloat3Direction(NextUInt2());
            public quaternion NextQuaternionRotation() => RngToolkit.AsQuaternionRotation(NextUInt3());
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
        public uint NextUInt(uint min, uint max) => currentSequence.NextUInt(min, max);
        public uint2 NextUInt2(uint2 min, uint2 max) => currentSequence.NextUInt2( min, max);
        public uint3 NextUInt3(uint3 min, uint3 max) => currentSequence.NextUInt3( min, max);
        public uint4 NextUInt4(uint4 min, uint4 max) => currentSequence.NextUInt4( min, max);

        public int NextInt() => currentSequence.NextInt();
        public int2 NextInt2() => currentSequence.NextInt2();
        public int3 NextInt3() => currentSequence.NextInt3();
        public int4 NextInt4() => currentSequence.NextInt4();
        public int NextInt(int min, int max) => currentSequence.NextInt(min, max);
        public int2 NextInt2(int2 min, int2 max) => currentSequence.NextInt2( min, max);
        public int3 NextInt3(int3 min, int3 max) => currentSequence.NextInt3( min, max);
        public int4 NextInt4(int4 min, int4 max) => currentSequence.NextInt4(min, max);

        public float NextFloat() => currentSequence.NextFloat();
        public float2 NextFloat2() => currentSequence.NextFloat2();
        public float3 NextFloat3() => currentSequence.NextFloat3();
        public float4 NextFloat4() => currentSequence.NextFloat4();
        public float NextFloat(float min, float max) => currentSequence.NextFloat(min, max);
        public float2 NextFloat2(float2 min, float2 max) => currentSequence.NextFloat2( min, max);
        public float3 NextFloat3(float3 min, float3 max) => currentSequence.NextFloat3( min, max);
        public float4 NextFloat4(float4 min, float4 max) => currentSequence.NextFloat4( min, max);

        public float2 NextFloat2Direction() => currentSequence.NextFloat2Direction();
        public float3 NextFloat3Direction() => currentSequence.NextFloat3Direction();
        public quaternion NextQuaternionRotation() => currentSequence.NextQuaternionRotation();
    }
}

