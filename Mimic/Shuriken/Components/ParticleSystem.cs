using Latios.Kinemation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

#if !LATIOS_TRANSFORMS_UNITY
using Transform = Latios.Transforms.WorldTransform;
#else
using Transform = Unity.Transforms.LocalToWorld;
#endif

namespace Latios.Mimic.Shuriken
{
    public struct ParticleSystem : IComponentData, IEnableableComponent
    {
        public BlobAssetReference<ParticleSystemBlob> blob;
        internal uint                                 seed;
        internal float                                playTime;
        internal float                                spawnAccumulator;
        // Todo: Put below in IEnableable?
        internal bool isSeedInitialized;
    }

    public struct CustomSimulationSpace : IComponentData
    {
        public EntityWith<Transform> customSpaceTransform;
    }

    public struct ImpartedVelocity : IComponentData
    {
        public float3 velocity;
    }

    public struct ImpartVelocityFromTransformTag : IComponentData { }

    public struct DestroyOnFinished : IComponentData, IAutoDestroyExpirable { }

    public struct ParticleSystemBlob
    {
        public BlobArray<half> halfConstants;
        public ParameterClip   systemLifetimeCurves;
        public BlobArray<half> systemLifetimeCurveMultipliers;
        public ParameterClip   particleLifetimeCurves;
        public BlobArray<half> particleLifetimeCurveMultipliers;
        // public BlobArray<half4> colorConstants;
        // public BlobArray<Gradient> gradients;
        public MainModule mainModule;
    }

    public struct MainModule
    {
        // Nomenclature: Half or Float,
        // k = constant, r = random, c = curve, and consequently rk = random between two constants,
        internal int    maxParticles;
        internal ushort packedMeta16;
        internal half   duration;
        internal half   flipRotation;
        internal half   simulationSpeed;
        internal byte   startDelay;  // half k, rk
        internal byte   startLifetime;  // half k, rk, c, rc
        internal byte   startSpeed;  // half k, rk, c, rc
        internal byte   startSizeX;  // half k, rk, c, rc
        internal byte   startSizeY;
        internal byte   startSizeZ;
        internal byte   startRotationX;  // half k, rk, c, rc
        internal byte   startRotationY;
        internal byte   startRotationZ;
        internal byte   startColor;  // color, gradient over lifetime, random between two colors, random between two gradients over lifetime, random color from gradient
        internal byte   gravityModifier;  // half k, rk, c, rc

        internal int packedMeta
        {
            get => packedMeta16;
            set => packedMeta16 = (ushort)(value & 0xffff);
        }

        internal bool looping
        {
            get => (packedMeta & 0x1) != 0;
            set => packedMeta = (packedMeta & ~0x1) | math.select(0, 0x1, value);
        }
        internal bool prewarm
        {
            get => (packedMeta & 0x2) != 0;
            set => packedMeta = (packedMeta & ~0x2) | math.select(0, 0x2, value);
        }
        internal bool startSize3D
        {
            get => (packedMeta & 0x4) != 0;
            set => packedMeta = (packedMeta & ~0x4) | math.select(0, 0x4, value);
        }
        internal bool startRotation3D
        {
            get => (packedMeta & 0x8) != 0;
            set => packedMeta = (packedMeta & ~0x8) | math.select(0, 0x8, value);
        }
        internal bool simulationWorldSpace  // False for local space, custom space if CustomSimulationSpace is present on the entity
        {
            get => (packedMeta & 0x10) != 0;
            set => packedMeta = (packedMeta & ~0x10) | math.select(0, 0x10, value);
        }
        internal bool useUnscaledDeltaTime  // False for scaled delta time (normal)
        {
            get => (packedMeta & 0x20) != 0;
            set => packedMeta = (packedMeta & ~0x20) | math.select(0, 0x20, value);
        }

        internal enum ScalingMode
        {
            Hierarchy = 0,
            Local = 1,
            Shape = 2,
        }
        internal ScalingMode scalingMode
        {
            get => (ScalingMode)((packedMeta >> 6) & 0x3);
            set => packedMeta = (packedMeta & ~0xc0) | ((int)value << 6);
        }
        internal bool playOnAwake
        {
            get => (packedMeta & 0x100) != 0;
            set => packedMeta = (packedMeta & ~0x100) | math.select(0, 0x100, value);
        }
        internal bool disableOnFinish
        {
            get => (packedMeta & 0x200) != 0;
            set => packedMeta = (packedMeta & ~0x200) | math.select(0, 0x200, value);
        }

        internal enum CullingMode
        {
            Automatic = 0,
            PauseAndCatchUp = 1,
            Pause = 2,
            AlwaysSimulate = 3
        }
        internal CullingMode cullingMode
        {
            get => (CullingMode)((packedMeta >> 10) & 0x3);
            set => packedMeta = (packedMeta & ~0xc00) | ((int)value << 10);
        }

        internal enum RingBufferMode
        {
            Disable = 0,
            PauseUntilReplaced = 1,
            LoopUntilReplaced = 2
        }
        internal RingBufferMode ringBufferMode
        {
            get => (RingBufferMode)((packedMeta >> 12) & 0x3);
            set => packedMeta = (packedMeta & ~0x3000) | ((int)value << 12);
        }
    }
}

