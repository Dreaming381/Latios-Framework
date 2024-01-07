#if false
using Latios.Kinemation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Mimic.Shuriken
{
    public interface IShurikenModuleComponent<T> : IComponentData where T : unmanaged
    {
        T module { get; set; }
        BlobAssetReference<ParameterClip> clip { get; set; }
    }

    public struct ShurikenMainModule
    {
        public const short clipIndex                = 0;
        public const ushort moduleSeed              = 52486;
        public const ushort startDelaySeed          = 1;
        public const ushort startLifetimeSeed       = 1;
        public const ushort startSpeedSeed          = 1;
        public const ushort startSizeXSeed          = 1;
        public const ushort startSizeYSeed          = 1;
        public const ushort startSizeZSeed          = 1;
        public const ushort startRotationEulerXSeed = 1;
        public const ushort startRotationEulerYSeed = 1;
        public const ushort startRotationEulerZSeed = 1;
        public const ushort startColorRedSeed       = 1;
        public const ushort startColorGreenSeed     = 1;
        public const ushort startColorBlueSeed      = 1;
        public const ushort startColorAlphaSeed     = 1;
        public const ushort gravityModifierSeed     = 1;

        public float duration;
        public bool loop;
        public bool prewarm;

        private short packedEnums;
        public ParticleSystemGravitySource gravitySource
        {
            get => (ParticleSystemGravitySource)(packedEnums & 0x1);
            set => packedEnums = (short)((packedEnums & ~0x1) | ((int)value & 0x1));
        }

        //TODO:  Implement this
        public ParticleSystemSimulationSpace simulationSpace
        {
            get => (ParticleSystemSimulationSpace)((packedEnums >> 1) & 0x3);
            set => packedEnums = (short)((packedEnums & ~(0x3 << 1)) | (((int)value & 0x3) << 1));
        }

        public ParticleSystemScalingMode scalingMode
        {
            get => (ParticleSystemScalingMode)((packedEnums >> 3) & 0x3);
            set => packedEnums = (short)((packedEnums & ~(0x3 << 3)) | (((int)value & 0x3) << 3));
        }

        public ParticleSystemEmitterVelocityMode emitterVelocityMode
        {
            get => (ParticleSystemEmitterVelocityMode)((packedEnums >> 5) & 0x3);
            set => packedEnums = (short)((packedEnums & ~(0x3 << 5)) | (((int)value & 0x3) << 5));
        }

        public ParticleSystemStopAction stopAction
        {
            get => (ParticleSystemStopAction)((packedEnums >> 7) & 0x3);
            set => packedEnums = (short)((packedEnums & ~(0x3 << 7)) | (((int)value & 0x3) << 7));
        }

        public ParticleSystemCullingMode cullingMode
        {
            get => (ParticleSystemCullingMode)((packedEnums >> 9) & 0x3);
            set => packedEnums = (short)((packedEnums & ~(0x3 << 9)) | (((int)value & 0x3) << 9));
        }

        public ParticleSystemRingBufferMode ringBufferMode
        {
            get => (ParticleSystemRingBufferMode)((packedEnums >> 11) & 0x3);
            set => packedEnums = (short)((packedEnums & ~(0x3 << 11)) | (((int)value & 0x3) << 11));
        }

        //This only happens once per play, ignored during loop
        public MinMaxCurvePacked startDelay;
        public float startDelayMultiplier;
        public MinMaxCurvePacked startLifetime;
        public float startLifetimeMultiplier;
        public MinMaxCurvePacked startSpeed;
        public float startSpeedMultiplier;
        public bool startSize3D;
        public MinMaxCurvePacked startSizeX;
        public float startSizeXMultiplier;
        public MinMaxCurvePacked startSizeY;
        public float startSizeYMultiplier;
        public MinMaxCurvePacked startSizeZ;
        public float startSizeZMultiplier;
        public bool startRotation3D;
        public MinMaxCurvePacked startRotationEulerXRadians;
        public float startRotationEulerXMultiplier;
        public MinMaxCurvePacked startRotationEulerYRadians;
        public float startRotationEulerYMultiplier;
        public MinMaxCurvePacked startRotationEulerZRadians;
        public float startRotationEulerZMultiplier;
        public float flipRotation;
        public MinMaxCurvePacked startColorRed;
        public MinMaxCurvePacked startColorGreen;
        public MinMaxCurvePacked startColorBlue;
        public MinMaxCurvePacked startColorAlpha;
        public MinMaxCurvePacked gravityModifier;
        public float gravityModifierMultiplier;
        public Entity customSimulationSpaceTransformEntity;
        public float simulationSpeed;
        public bool useUnscaledTime;
        public bool playOnAwake;
        public int maxParticles;
    }

    public struct ShurikenMainModuleOverride : IShurikenModuleComponent<ShurikenMainModule>
    {
        private ShurikenMainModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenMainModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenEmissionModule
    {
        public const short clipIndex             = 1;
        public const ushort moduleSeed           = 2;
        public const ushort rateOverTimeSeed     = 2;
        public const ushort rateOverDistanceSeed = 2;
        public const ushort burstsSeed           = 2;

        public bool enabled;
        public MinMaxCurvePacked rateOverTime;
        public float rateOverTimeMultiplier;
        public MinMaxCurvePacked rateOverDistance;
        public float rateOverDistanceMultiplier;
    }

    public struct ShurikenEmissionModuleOverride : IShurikenModuleComponent<ShurikenEmissionModule>
    {
        private ShurikenEmissionModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenEmissionModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    [InternalBufferCapacity(0)]
    public struct EmissionBurst : IBufferElementData
    {
        public float time;
        public MinMaxCurvePacked count;
        public float cycleCount;
        public float repeatInterval;
        public float probability;
    }

    public struct ShurikenShapeModule
    {
        public bool enabled;
        public const short clipIndex           = 2;
        public const ushort moduleSeed         = 3;
        public const ushort radiusSpeedSeed    = 3;
        public const ushort meshSpawnSpeedSeed = 3;
        public const ushort arcSpeedSeed       = 3;
        public ParticleSystemShapeType shapeType;
        public float randomDirectionAmount;
        public float sphericalDirectionAmount;
        public float randomPositionAmount;
        public bool alignToDirection;
        public float radius;
        // TODO:  Verify these aren't actually used (even for circle, editor uses arc versions)
        // public ParticleSystemShapeMultiModeValue radiusMode;
        // public float radiusSpread;
        // public MinMaxCurvePacked radiusSpeed;
        public float radiusThickness;
        public float angleRadians;
        public float length;
        public float3 boxThickness;
        public ParticleSystemMeshShapeType meshShapeType;
        //public Mesh mesh
        //public MeshRenderer meshRenderer
        //public SkinnedMeshRenderer skinnedMeshRenderer
        //public Sprite sprite
        //public SpriteRenderer spriteRenderer
        public bool useMeshMaterialIndex;
        public int meshMaterialIndex;
        public bool useMeshColors;
        public float normalOffset;
        public ParticleSystemShapeMultiModeValue meshSpawnMode;
        public float meshSpawnSpread;
        public MinMaxCurvePacked meshSpawnSpeed;
        public float arcRadians;
        public ParticleSystemShapeMultiModeValue arcMode;
        public float arcSpreadRadians;
        public MinMaxCurvePacked arcSpeed;
        public float arcSpeedMultiplier;
        public float donutRadius;
        public float3 position;
        public float3 rotationEulerRadians;
        public float3 scale;
        //public Texture2D texture
        public ParticleSystemShapeTextureChannel textureClipChannel;
        public float textureClipThreshold;
        public bool textureColorAffectsParticles;
        public bool textureAlphaAffectsParticles;
        public bool textureBilinearFiltering;
        public int textureUVChannel;
    }

    public struct ShurikenShapeModuleOverride : IShurikenModuleComponent<ShurikenShapeModule>
    {
        private ShurikenShapeModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenShapeModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenVelocityOverLifetimeModule
    {
        public const short clipIndex          = 3;
        public const ushort moduleSeed        = 4;
        public const ushort velocitySeed      = 4;
        public const ushort orbitalSeed       = 4;
        public const ushort orbitalOffsetSeed = 4;
        public const ushort radialSeed        = 4;
        public const ushort speedModifierSeed = 4;

        public bool enabled;
        public MinMaxCurvePacked x;
        public MinMaxCurvePacked y;
        public MinMaxCurvePacked z;
        public float xMultiplier;
        public float yMultiplier;
        public float zMultiplier;
        public MinMaxCurvePacked orbitalX;
        public MinMaxCurvePacked orbitalY;
        public MinMaxCurvePacked orbitalZ;
        public float orbitalXMultiplier;
        public float orbitalYMultiplier;
        public float orbitalZMultiplier;
        public MinMaxCurvePacked orbitalOffsetX;
        public MinMaxCurvePacked orbitalOffsetY;
        public MinMaxCurvePacked orbitalOffsetZ;
        public float orbitalOffsetXMultiplier;
        public float orbitalOffsetYMultiplier;
        public float orbitalOffsetZMultiplier;
        public MinMaxCurvePacked radial;
        public float radialMultiplier;
        public MinMaxCurvePacked speedModifier;
        public float speedModifierMultiplier;
        public ParticleSystemSimulationSpace space;
    }

    public struct ShurikenVelocityOverLifetimeModuleOverride : IShurikenModuleComponent<ShurikenVelocityOverLifetimeModule>
    {
        private ShurikenVelocityOverLifetimeModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenVelocityOverLifetimeModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenLimitVelocityOverLifetimeModule
    {
        public const short clipIndex   = 4;
        public const ushort moduleSeed = 5;
        public const ushort limitSeed  = 4;
        public const ushort dragSeed   = 4;

        public bool enabled;
        public MinMaxCurvePacked limitX;
        public float limitXMultiplier;
        public MinMaxCurvePacked limitY;
        public float limitYMultiplier;
        public MinMaxCurvePacked limitZ;
        public float limitZMultiplier;
        public MinMaxCurvePacked limit;  //magnitude
        public float limitMultiplier;
        public float dampen;
        public bool separateAxes;
        public ParticleSystemSimulationSpace space;
        public MinMaxCurvePacked drag;
        public float dragMultiplier;
        public bool multiplyDragByParticleSize;
        public bool multiplyDragByParticleVelocity;
    }

    public struct ShurikenLimitVelocityOverLifetimeModuleOverride : IShurikenModuleComponent<ShurikenLimitVelocityOverLifetimeModule>
    {
        private ShurikenLimitVelocityOverLifetimeModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenLimitVelocityOverLifetimeModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenInheritVelocityModule
    {
        public const short clipIndex   = 5;
        public const ushort moduleSeed = 6;
        public const ushort curveSeed  = 6;

        public bool enabled;
        public ParticleSystemInheritVelocityMode mode;
        public MinMaxCurvePacked curve;
        public float curveMultiplier;
    }

    public struct ShurikenInheritVelocityModuleOverride : IShurikenModuleComponent<ShurikenInheritVelocityModule>
    {
        private ShurikenInheritVelocityModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenInheritVelocityModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenLifetimeByEmitterSpeedModule
    {
        public const short clipIndex   = 6;
        public const ushort moduleSeed = 7;
        public const ushort curveSeed  = 6;

        public bool enabled;
        public MinMaxCurvePacked curve;
        public float curveMultiplier;
        public float2 range;
    }

    public struct ShurikenLifetimeByEmitterSpeedModuleOverride : IShurikenModuleComponent<ShurikenLifetimeByEmitterSpeedModule>
    {
        private ShurikenLifetimeByEmitterSpeedModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenLifetimeByEmitterSpeedModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenForceOverLifetimeModule
    {
        public const short clipIndex   = 7;
        public const ushort moduleSeed = 8;
        public const ushort xSeed      = 6;
        public const ushort ySeed      = 6;
        public const ushort zSeed      = 6;

        public bool enabled;
        public MinMaxCurvePacked x;
        public MinMaxCurvePacked y;
        public MinMaxCurvePacked z;
        public float xMultiplier;
        public float yMultiplier;
        public float zMultiplier;
        public ParticleSystemSimulationSpace space;
        //randomized each frame if TwoCurves or TwoConstants mode
        public bool randomized;
    }

    public struct ShurikenForceOverLifetimeModuleOverride : IShurikenModuleComponent<ShurikenForceOverLifetimeModule>
    {
        private ShurikenForceOverLifetimeModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenForceOverLifetimeModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenColorOverLifetimeModule
    {
        public const short clipIndex   = 8;
        public const ushort moduleSeed = 9;
        public const ushort colorSeed  = 6;

        public bool enabled;
        public MinMaxCurvePacked colorRed;
        public MinMaxCurvePacked colorGreen;
        public MinMaxCurvePacked colorBlue;
        public MinMaxCurvePacked colorAlpha;
    }

    public struct ShurikenColorOverLifetimeModuleOverride : IShurikenModuleComponent<ShurikenColorOverLifetimeModule>
    {
        private ShurikenColorOverLifetimeModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenColorOverLifetimeModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenColorBySpeedModule
    {
        public const short clipIndex   = 9;
        public const ushort moduleSeed = 10;
        public const ushort colorSeed  = 6;

        public bool enabled;
        public MinMaxCurvePacked colorRed;
        public MinMaxCurvePacked colorGreen;
        public MinMaxCurvePacked colorBlue;
        public MinMaxCurvePacked colorAlpha;
        public float2 range;
    }

    public struct ShurikenColorBySpeedModuleOverride : IShurikenModuleComponent<ShurikenColorBySpeedModule>
    {
        private ShurikenColorBySpeedModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenColorBySpeedModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenSizeOverLifetimeModule
    {
        public const short clipIndex   = 10;
        public const ushort moduleSeed = 11;
        public const ushort xSeed      = 6;
        public const ushort ySeed      = 6;
        public const ushort zSeed      = 6;

        public bool enabled;
        public MinMaxCurvePacked x;  //doubles as "size"
        public float xMultiplier;  //doubles as "sizeMultiplier"
        public MinMaxCurvePacked y;
        public float yMultiplier;
        public MinMaxCurvePacked z;
        public float zMultiplier;
        public bool separateAxes;
    }

    public struct ShurikenSizeOverLifetimeModuleOverride : IShurikenModuleComponent<ShurikenSizeOverLifetimeModule>
    {
        private ShurikenSizeOverLifetimeModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenSizeOverLifetimeModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenSizeBySpeedModule
    {
        public const short clipIndex   = 11;
        public const ushort moduleSeed = 12;
        public const ushort xSeed      = 6;
        public const ushort ySeed      = 6;
        public const ushort zSeed      = 6;

        public bool enabled;
        public MinMaxCurvePacked x;  //doubles as "size"
        public float xMultiplier;  //doubles as "sizeMultiplier"
        public MinMaxCurvePacked y;
        public float yMultiplier;
        public MinMaxCurvePacked z;
        public float zMultiplier;
        public bool separateAxes;
        public float2 range;
    }

    public struct ShurikenSizeBySpeedModuleOverride : IShurikenModuleComponent<ShurikenSizeBySpeedModule>
    {
        private ShurikenSizeBySpeedModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenSizeBySpeedModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenRotationOverLifetimeModule
    {
        public const short clipIndex   = 12;
        public const ushort moduleSeed = 13;
        public const ushort xSeed      = 6;
        public const ushort ySeed      = 6;
        public const ushort zSeed      = 6;

        public bool enabled;
        public MinMaxCurvePacked x;
        public float xMultiplier;
        public MinMaxCurvePacked y;
        public float yMultiplier;
        public MinMaxCurvePacked z;
        public float zMultiplier;
        public bool separateAxes;
    }

    public struct ShurikenRotationOverLifetimeModuleOverride : IShurikenModuleComponent<ShurikenRotationOverLifetimeModule>
    {
        private ShurikenRotationOverLifetimeModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenRotationOverLifetimeModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenRotationBySpeedModule
    {
        public const short clipIndex   = 13;
        public const ushort moduleSeed = 14;
        public const ushort xSeed      = 6;
        public const ushort ySeed      = 6;
        public const ushort zSeed      = 6;

        public bool enabled;
        public MinMaxCurvePacked x;
        public float xMultiplier;
        public MinMaxCurvePacked y;
        public float yMultiplier;
        public MinMaxCurvePacked z;
        public float zMultiplier;
        public bool separateAxes;
        public float2 range;
    }

    public struct ShurikenRotationBySpeedModuleOverride : IShurikenModuleComponent<ShurikenRotationBySpeedModule>
    {
        private ShurikenRotationBySpeedModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenRotationBySpeedModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenExternalForcesModule
    {
        public const short clipIndex   = 14;
        public const ushort moduleSeed = 15;
        public const ushort curveSeed  = 6;

        public bool enabled;
        public float multiplier;
        public MinMaxCurvePacked multiplierCurve;
        public ParticleSystemGameObjectFilter influenceFilter;
        public uint influenceMask;
    }

    public struct ShurikenExternalForcesModuleOverride : IShurikenModuleComponent<ShurikenExternalForcesModule>
    {
        private ShurikenExternalForcesModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenExternalForcesModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenNoiseModule
    {
        public const short clipIndex           = 15;
        public const ushort moduleSeed         = 16;
        public const ushort noiseXSeed         = 6;
        public const ushort noiseYSeed         = 6;
        public const ushort noiseZSeed         = 6;
        public const ushort strengthXSeed      = 6;
        public const ushort strengthYSeed      = 6;
        public const ushort strengthZSeed      = 6;
        public const ushort scrollSpeedSeed    = 6;
        public const ushort remapXSeed         = 6;
        public const ushort remapYSeed         = 6;
        public const ushort remapZSeed         = 6;
        public const ushort positionAmountSeed = 6;
        public const ushort rotationAmountSeed = 6;
        public const ushort sizeAmountSeed     = 6;

        public bool enabled;
        public bool separateAxes;
        public MinMaxCurvePacked strengthX;  //doubles as "strength"
        public float strengthXMultiplier;  //doubles as "strengthMultiplier"
        public MinMaxCurvePacked strengthY;
        public float strengthYMultiplier;
        public MinMaxCurvePacked strengthZ;
        public float strengthZMultiplier;
        public float frequency;
        public bool damping;
        public int octaveCount;
        public float octaveMultiplier;
        public float octaveScale;
        public ParticleSystemNoiseQuality quality;
        public MinMaxCurvePacked scrollSpeed;
        public float scrollSpeedMultiplier;
        public bool remapEnabled;
        public MinMaxCurvePacked remapX;  //doubles as "remap"
        public float remapXMultiplier;  //doubles as "remapMultiplier"
        public MinMaxCurvePacked remapY;
        public float remapYMultiplier;
        public MinMaxCurvePacked remapZ;
        public float remapZMultiplier;
        public MinMaxCurvePacked positionAmount;
        public MinMaxCurvePacked rotationAmount;
        public MinMaxCurvePacked sizeAmount;
    }

    public struct ShurikenNoiseModuleOverride : IShurikenModuleComponent<ShurikenNoiseModule>
    {
        private ShurikenNoiseModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenNoiseModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenCollisionModule
    {
        public const short clipIndex   = 16;
        public const ushort moduleSeed = 17;

        public bool enabled;
        public ParticleSystemCollisionType type;
        public ParticleSystemCollisionMode mode;
        public MinMaxCurvePacked dampen;
        public float dampenMultiplier;
        public MinMaxCurvePacked bounce;
        public float bounceMultiplier;
        public MinMaxCurvePacked lifetimeLoss;
        public float lifetimeLossMultiplier;
        public float minKillSpeed;
        public float maxKillSpeed;
        public uint collidesWith;
        public bool enableDynamicColliders;
        //public bool                           enableInteriorCollisions; // Deprecated in Unity and has no effect.
        public int maxCollisionShapes;
        public ParticleSystemCollisionQuality quality;
        public float voxelSize;
        public float radiusScale;
        public bool sendCollisionMessages;
        public float colliderForce;
        public bool multiplyColliderForceByCollisionAngle;
        public bool multiplyColliderForceByParticleSpeed;
        public bool multiplyColliderForceByParticleSize;
    }

    public struct ShurikenCollisionModuleOverride : IShurikenModuleComponent<ShurikenCollisionModule>
    {
        private ShurikenCollisionModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenCollisionModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenTriggerModule
    {
        public bool enabled;
        public ParticleSystemOverlapAction inside;
        public ParticleSystemOverlapAction outside;
        public ParticleSystemOverlapAction enter;
        public ParticleSystemOverlapAction exit;
        public ParticleSystemColliderQueryMode colliderQueryMode;
        public float radiusScale;
    }

    public struct ShurikenTriggerModuleOverride : IShurikenModuleComponent<ShurikenTriggerModule>
    {
        private ShurikenTriggerModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenTriggerModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenSubEmittersModule
    {
        public bool enabled;
        public int subEmittersCount;
    }

    public struct ShurikenSubEmittersModuleOverride : IShurikenModuleComponent<ShurikenSubEmittersModule>
    {
        private ShurikenSubEmittersModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenSubEmittersModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenTextureSheetAnimationModule
    {
        public const short clipIndex   = 17;
        public const ushort moduleSeed = 18;

        public bool enabled;
        public ParticleSystemAnimationMode mode;
        public ParticleSystemAnimationTimeMode timeMode;
        public float fps;
        public int numTilesX;
        public int numTilesY;
        public ParticleSystemAnimationType animation;
        public ParticleSystemAnimationRowMode rowMode;
        //public bool                            useRandomRow; // Deprecated in Unity. Use rowMode instead.
        public MinMaxCurvePacked frameOverTime;
        public float frameOverTimeMultiplier;
        public MinMaxCurvePacked startFrame;
        public float startFrameMultiplier;
        public int cycleCount;
        public int rowIndex;
        public UVChannelFlags uvChannelMask;
        public int spriteCount;
        public float2 speedRange;
    }

    public struct ShurikenTextureSheetAnimationModuleOverride : IShurikenModuleComponent<ShurikenTextureSheetAnimationModule>
    {
        private ShurikenTextureSheetAnimationModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenTextureSheetAnimationModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenLightsModule
    {
        public const short clipIndex   = 18;
        public const ushort moduleSeed = 19;

        public bool enabled;
        public float ratio;
        public bool useRandomDistribution;
        //public Light light;
        public bool useParticleColor;
        public bool sizeAffectsRange;
        public bool alphaAffectsIntensity;
        public MinMaxCurvePacked range;
        public float rangeMultiplier;
        public MinMaxCurvePacked intensity;
        public float intensityMultiplier;
        public int maxLights;
    }

    public struct ShurikenLightsModuleOverride : IShurikenModuleComponent<ShurikenLightsModule>
    {
        private ShurikenLightsModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenLightsModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenTrailModule
    {
        public const short clipIndex   = 19;
        public const ushort moduleSeed = 20;

        public bool enabled;
        public ParticleSystemTrailMode mode;
        public float ratio;
        public MinMaxCurvePacked lifetime;
        public float lifetimeMultiplier;
        public float minVertexDistance;
        public ParticleSystemTrailTextureMode textureMode;
        public float2 textureScale;
        public bool worldSpace;
        public bool dieWithParticles;
        public bool sizeAffectsWidth;
        public bool sizeAffectsLifetime;
        public bool inheritParticleColor;
        public MinMaxCurvePacked colorOverLifetimeRed;
        public MinMaxCurvePacked colorOverLifetimeGreen;
        public MinMaxCurvePacked colorOverLifetimeBlue;
        public MinMaxCurvePacked colorOverLifetimeAlpha;
        public MinMaxCurvePacked widthOverTrail;
        public float widthOverTrailMultiplier;
        public MinMaxCurvePacked colorOverTrailRed;
        public MinMaxCurvePacked colorOverTrailGreen;
        public MinMaxCurvePacked colorOverTrailBlue;
        public MinMaxCurvePacked colorOverTrailAlpha;
        public bool generateLightingData;
        public int ribbonCount;
        public float shadowBias;
        public bool splitSubEmitterRibbons;
        public bool attachRibbonsToTransform;
    }

    public struct ShurikenTrailModuleOverride : IShurikenModuleComponent<ShurikenTrailModule>
    {
        private ShurikenTrailModule m_module;
        private BlobAssetReference<ParameterClip> m_clip;
        public ShurikenTrailModule module
        {
            get => m_module;
            set => m_module = value;
        }
        public BlobAssetReference<ParameterClip> clip
        {
            get => m_clip;
            set => m_clip = value;
        }
    }

    public struct ShurikenCustomDataModule : IComponentData
    {
        public bool enabled;
        //TODO:  CustomData
    }

    public struct ShurikenRendererModule : IComponentData
    {
    }
}
#endif

