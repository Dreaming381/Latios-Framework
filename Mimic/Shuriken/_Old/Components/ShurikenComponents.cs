#if false
using System.Runtime.InteropServices;
using Latios.Kinemation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Mimic.Shuriken
{
    public struct ShurikenParticleSystemData : IComponentData
    {
        public bool isPlaying;
        public bool isEmitting;
        public bool isStopped;
        public bool isPaused;
        public int particleCount;
        public float time;
        public float delayTime;
        public float totalTime;
        public uint randomSeed;
        public float distanceTraveled;

        public float previousFrameTime;

        public float3 previousPosition;

        // Optional components
        public bool has3dRotation;
        public bool has3dScale;
        public bool hasVelocity;
        public bool hasColor;

        public BlobAssetReference<ShurikenModulesBlob> Modules;
    }

    public struct ShurikenModulesBlob
    {
        public ParameterClipSetBlob sampledClips;
        public ShurikenMainModule mainModule;
        public ShurikenEmissionModule emissionModule;
        public BlobArray<EmissionBurst> emissionBursts;
        public ShurikenShapeModule shapeModule;
        public ShurikenVelocityOverLifetimeModule velocityOverLifetimeModule;
        public ShurikenLimitVelocityOverLifetimeModule limitVelocityOverLifetimeModule;
        public ShurikenInheritVelocityModule inheritVelocityModule;
        public ShurikenLifetimeByEmitterSpeedModule lifetimeByEmitterSpeedModule;
        public ShurikenForceOverLifetimeModule forceOverLifetimeModule;
        public ShurikenColorOverLifetimeModule colorOverLifetimeModule;
        public ShurikenColorBySpeedModule colorBySpeedModule;
        public ShurikenSizeOverLifetimeModule sizeOverLifetimeModule;
        public ShurikenSizeBySpeedModule sizeBySpeedModule;
        public ShurikenRotationOverLifetimeModule rotationOverLifetimeModule;
        public ShurikenRotationBySpeedModule rotationBySpeedModule;
        public ShurikenExternalForcesModule externalForcesModule;
        public ShurikenNoiseModule noiseModule;
        public ShurikenCollisionModule collisionModule;
        public ShurikenTriggerModule triggerModule;
        public ShurikenSubEmittersModule subEmittersModule;
        public ShurikenTextureSheetAnimationModule textureSheetAnimationModule;
        public ShurikenLightsModule lightsModule;
        public ShurikenTrailModule trailsModule;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct MinMaxCurvePacked
    {
        [FieldOffset(0)] public ulong packedValue;

        public float Evaluate(ref ParameterClip sampledCurves, in Rng.RngSequence rngSequence, float time)
        {
            switch (mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return constant;
                case ParticleSystemCurveMode.Curve:
                    return sampledCurves.SampleParameter(curveMaxParameterIndex, time, KeyframeInterpolationMode.Interpolate);
                case ParticleSystemCurveMode.TwoCurves:
                    var min = sampledCurves.SampleParameter(curveMinParameterIndex, time, KeyframeInterpolationMode.Interpolate);
                    var max = sampledCurves.SampleParameter(curveMaxParameterIndex, time, KeyframeInterpolationMode.Interpolate);
                    return rngSequence.NextFloat(min, max);
                case ParticleSystemCurveMode.TwoConstants:
                    return rngSequence.NextFloat(constantMin, constantMax);
                default:
                    return 0f;
            }
        }

        // The last two bits in the packedValue are used to store the ParticleSystemCurveMode
        public ParticleSystemCurveMode mode
        {
            get => (ParticleSystemCurveMode)(packedValue & 0b11);  // Get the last 2 bits
            set
            {
                packedValue = (packedValue & ~0b11UL) | ((ulong)value & 0b11);
            }
        }

        //The first byte in the packedValue is used to store the curveMinParameterIndex
        public byte curveMinParameterIndex
        {
            get => (byte)(packedValue & 0xFF);  // Get the first 8 bits
            set { packedValue = (packedValue & ~0xFFUL) | value; }
        }

        //The second byte in the packedValue is used to store the curveMaxParameterIndex
        public byte curveMaxParameterIndex
        {
            get => (byte)((packedValue >> 8) & 0xFF);  // Get the next 8 bits
            set { packedValue = (packedValue & ~(0xFFUL << 8)) | ((ulong)value << 8); }
        }

        //The 16-48th bits in the packedValue are used to store the curveMultiplier
        public float curveMultiplier
        {
            get
            {
                // Extract 32 bits for curveMultiplier
                uint rawValue = (uint)((packedValue >> 16) & 0xFFFFFFFF);
                return new half(rawValue);
            }
            set
            {
                // Clear the relevant 32 bits and set the new stableSeed
                packedValue = (packedValue & ~(0xFFFFFFFFUL << 16)) | ((ulong)value & 0xFFFFFFFF) << 16;
            }
        }

        //The first 32 bits in the packedValue are used to store the constant
        public float constant
        {
            get
            {
                // Extracting the float stableSeed from the first 32 bits
                uint value = (uint)(packedValue & 0xFFFFFFFF);
                // Convert bytes to float
                return math.asfloat(value);
            }
            set
            {
                packedValue = (packedValue & 0xFFFFFFFE00000000UL) | ((ulong)value << 1);
            }
        }

        //The first 31 bits in the packedValue are used to store the constantMin
        public float constantMin
        {
            get => (packedValue >> 1) & 0x7FFFFFFF;  // Get the first 31 bits, shifted by 1
            set
            {
                // Ensure stableSeed fits in 31 bits and shift it by 1
                packedValue = (packedValue & 0xFFFFFFFF80000000UL) | (((ulong)value & 0x7FFFFFFF) << 1);
            }
        }

        //The 32nd-62nd bits in the packedValue are used to store the constantMax
        public float constantMax
        {
            get => (packedValue >> 33) & 0x7FFFFFFF;  // Get the next 31 bits, shifted by 1
            set
            {
                // Ensure stableSeed fits in 31 bits and shift it by 1
                packedValue = (packedValue & 0xFFFFFFFE00000001UL) | (((ulong)value & 0x7FFFFFFF) << 33);
            }
        }
    }
}
#endif

