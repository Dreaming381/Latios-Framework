using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

#if false
namespace Latios.LifeFX
{
    [InternalBufferCapacity(0)]
    public struct ParticleSeed : IBufferElementData
    {
        public uint stableSeed;
    }

    [InternalBufferCapacity(0)]
    public struct ParticleCenter : IBufferElementData
    {
        public float3 center;
    }

    [InternalBufferCapacity(0)]
    public struct ParticleRotation : IBufferElementData
    {
        /// <summary>
        /// Angle radians
        /// </summary>
        public float rotationCCW;
    }

    [InternalBufferCapacity(0)]
    public struct ParticleRotation3d : IBufferElementData
    {
        public quaternion rotation;
    }

    [InternalBufferCapacity(0)]
    public struct ParticleRotationSpeed : IBufferElementData
    {
        public float rotationSpeedCCW;
    }

    /// <summary>
    /// Akin to ParticleSystemVertexStream.SizeX
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct ParticleScale : IBufferElementData
    {
        /// <summary>
        /// Uniform scale
        /// </summary>
        public float scale;
    }

    /// <summary>
    /// Akin to ParticleSystemVertexStream.SizeXYZ
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct ParticleScale3d : IBufferElementData
    {
        public float3 scale;
    }

    [InternalBufferCapacity(0)]
    public struct ParticleColor : IBufferElementData
    {
        public half4 color;
    }

    [InternalBufferCapacity(0)]
    public struct ParticleVelocity : IBufferElementData
    {
        public float3 velocity;
    }

    /// <summary>
    /// Akin to ParticleSystemVertexStream.AgePercent
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct ParticleAgeFraction : IBufferElementData
    {
        public ushort fraction;
    }

    /// <summary>
    /// Akin to ParticleSystemVertexStream.InvStartLifetime.
    /// Multiplying a time fraction by this fraction remaps that time fraction
    /// into a normalized age fraction.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct ParticleInverseStartLifetime : IBufferElementData
    {
        public float inverseExpectedLifetime;
    }
}
#endif

