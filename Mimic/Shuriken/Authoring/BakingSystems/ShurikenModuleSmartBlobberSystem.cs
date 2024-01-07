#if false
using System;
using AclUnity;
using Latios.Authoring;
using Latios.Kinemation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Mimic.Shuriken.Authoring
{
    public static class ShurikenBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of an MecanimControllerLayerBlob Blob Asset
        /// </summary>
        /// <param name="animatorController">An animatorController whose layer to bake.</param>
        /// <param name="layerIndex">The index of the layer to bake.</param>
        public static SmartBlobberHandle<ShurikenModulesBlob> RequestCreateBlobAsset(this IBaker baker, ParticleSystem particleSystem)
        {
            return baker.RequestCreateBlobAsset<ShurikenModulesBlob, ParticleSystemBakeData>(new ParticleSystemBakeData
            {
                particleSystem = particleSystem
            });
        }
    }

    /// <summary>
    /// Input for the AnimatorController Smart Blobber
    /// </summary>
    public struct ParticleSystemBakeData : ISmartBlobberRequestFilter<ShurikenModulesBlob>
    {
        /// <summary>
        /// The UnityEngine.Animator to bake into a blob asset reference.
        /// </summary>
        public ParticleSystem particleSystem;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            baker.AddComponent(blobBakingEntity, new ShurikenModuleBlobRequest
            {
                particleSystem = new UnityObjectRef<ParticleSystem> { Value = particleSystem },
            });

            return true;
        }
    }

    [TemporaryBakingType]
    internal struct ShurikenModuleBlobRequest : IComponentData
    {
        public UnityObjectRef<ParticleSystem> particleSystem;
    }
}

namespace Latios.Mimic.Shuriken.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class ShurikenModuleSmartBlobberSystem : SystemBase
    {
        protected override void OnCreate()
        {
            new SmartBlobberTools<ShurikenModulesBlob>().Register(World);
        }

        protected override void OnUpdate()
        {
            int count = SystemAPI.QueryBuilder().WithAll<ShurikenModuleBlobRequest>().WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                        .Build().CalculateEntityCountWithoutFiltering();
            var hashmap = new NativeParallelHashMap<UnityObjectRef<ParticleSystem>, BlobAssetReference<ShurikenModulesBlob> >(count * 2, Allocator.TempJob);

            new AddToMapJob { hashmap = hashmap.AsParallelWriter() }.ScheduleParallel();
            CompleteDependency();

            foreach (var pair in hashmap)
            {
                pair.Value = BakeParticleSystemModules(pair.Key.Value);
            }

            Entities.WithReadOnly(hashmap).ForEach((ref SmartBlobberResult result, in ShurikenModuleBlobRequest request) =>
            {
                var shurikenModulesBlob = hashmap[request.particleSystem];
                result.blob             = UnsafeUntypedBlobAssetReference.Create(shurikenModulesBlob);
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ScheduleParallel();

            Dependency = hashmap.Dispose(Dependency);
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct AddToMapJob : IJobEntity
        {
            public NativeParallelHashMap<UnityObjectRef<ParticleSystem>, BlobAssetReference<ShurikenModulesBlob> >.ParallelWriter hashmap;

            public void Execute(in ShurikenModuleBlobRequest request)
            {
                hashmap.TryAdd(request.particleSystem, default);
            }
        }

        private unsafe BlobAssetReference<ShurikenModulesBlob> BakeParticleSystemModules(ParticleSystem particleSystem)
        {
            const int moduleCount = 20;  //Number of modules to process
            var builder     = new BlobBuilder(Allocator.Temp);
            ref var root        = ref builder.ConstructRoot<ShurikenModulesBlob>();

            var clipNameList     = new NativeList<FixedString64Bytes>(Allocator.Temp);
            var clipNameHashList = new NativeList<int>(Allocator.Temp);

            //Create main module
            var mainModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.mainModule.duration                      = particleSystem.main.duration;
            root.mainModule.loop                          = particleSystem.main.loop;
            root.mainModule.prewarm                       = particleSystem.main.prewarm;
            root.mainModule.startDelay                    = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startDelay);
            root.mainModule.startDelayMultiplier          = particleSystem.main.startDelayMultiplier;
            root.mainModule.startLifetime                 = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startLifetime);
            root.mainModule.startLifetimeMultiplier       = particleSystem.main.startLifetimeMultiplier;
            root.mainModule.startSpeed                    = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startSpeed);
            root.mainModule.startSpeedMultiplier          = particleSystem.main.startSpeedMultiplier;
            root.mainModule.startSize3D                   = particleSystem.main.startSize3D;
            root.mainModule.startSizeX                    = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startSizeX);
            root.mainModule.startSizeXMultiplier          = particleSystem.main.startSizeXMultiplier;
            root.mainModule.startSizeY                    = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startSizeY);
            root.mainModule.startSizeYMultiplier          = particleSystem.main.startSizeYMultiplier;
            root.mainModule.startSizeZ                    = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startSizeZ);
            root.mainModule.startSizeZMultiplier          = particleSystem.main.startSizeZMultiplier;
            root.mainModule.startRotation3D               = particleSystem.main.startRotation3D;
            root.mainModule.startRotationEulerXRadians    = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startRotationX, math.radians);
            root.mainModule.startRotationEulerXMultiplier = particleSystem.main.startRotationXMultiplier;
            root.mainModule.startRotationEulerYRadians    = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startRotationY, math.radians);
            root.mainModule.startRotationEulerYMultiplier = particleSystem.main.startRotationYMultiplier;
            root.mainModule.startRotationEulerZRadians    = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.startRotationZ, math.radians);
            root.mainModule.startRotationEulerZMultiplier = particleSystem.main.startRotationZMultiplier;
            root.mainModule.flipRotation                  = particleSystem.main.flipRotation;
            //Start Color
            CreateMinMaxGradient(ref mainModuleClips,
                                 particleSystem.main.startColor,
                                 out root.mainModule.startColorRed,
                                 out root.mainModule.startColorGreen,
                                 out root.mainModule.startColorBlue,
                                 out root.mainModule.startColorAlpha);
            root.mainModule.gravitySource             = particleSystem.main.gravitySource;
            root.mainModule.gravityModifier           = CreateMinMaxCurve(ref mainModuleClips, particleSystem.main.gravityModifier);
            root.mainModule.gravityModifierMultiplier = particleSystem.main.gravityModifierMultiplier;
            root.mainModule.simulationSpace           = particleSystem.main.simulationSpace;

            //TODO:  Remove and place in system
            //root.mainModule.customSimulationSpaceTransformEntity = particleSystem.main.customSimulationSpace
            root.mainModule.simulationSpeed     = particleSystem.main.simulationSpeed;
            root.mainModule.useUnscaledTime     = particleSystem.main.useUnscaledTime;
            root.mainModule.scalingMode         = particleSystem.main.scalingMode;
            root.mainModule.playOnAwake         = particleSystem.main.playOnAwake;
            root.mainModule.maxParticles        = particleSystem.main.maxParticles;
            root.mainModule.emitterVelocityMode = particleSystem.main.emitterVelocityMode;
            root.mainModule.stopAction          = particleSystem.main.stopAction;
            root.mainModule.cullingMode         = particleSystem.main.cullingMode;
            root.mainModule.ringBufferMode      = particleSystem.main.ringBufferMode;

            //Create emission module
            var emissionModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.emissionModule.enabled                    = particleSystem.emission.enabled;
            root.emissionModule.rateOverTime               = CreateMinMaxCurve(ref emissionModuleClips, particleSystem.emission.rateOverTime);
            root.emissionModule.rateOverTimeMultiplier     = particleSystem.emission.rateOverTimeMultiplier;
            root.emissionModule.rateOverDistance           = CreateMinMaxCurve(ref emissionModuleClips, particleSystem.emission.rateOverDistance);
            root.emissionModule.rateOverDistanceMultiplier = particleSystem.emission.rateOverDistanceMultiplier;
            builder.Allocate(ref root.emissionBursts, particleSystem.emission.burstCount);
            for (int i = 0; i < particleSystem.emission.burstCount; i++)
            {
                var burst         = particleSystem.emission.GetBurst(i);
                var emissionBurst = new EmissionBurst();
                emissionBurst.time           = burst.time;
                emissionBurst.count          = CreateMinMaxCurve(ref emissionModuleClips, burst.count);
                emissionBurst.cycleCount     = burst.cycleCount;
                emissionBurst.repeatInterval = burst.repeatInterval;
                emissionBurst.probability    = burst.probability;
                root.emissionBursts[i]       = emissionBurst;
            }

            //Create shape module
            var shapeModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.shapeModule.enabled                  = particleSystem.shape.enabled;
            root.shapeModule.shapeType                = particleSystem.shape.shapeType;
            root.shapeModule.randomDirectionAmount    = particleSystem.shape.randomDirectionAmount;
            root.shapeModule.sphericalDirectionAmount = particleSystem.shape.sphericalDirectionAmount;
            root.shapeModule.randomPositionAmount     = particleSystem.shape.randomPositionAmount;
            root.shapeModule.alignToDirection         = particleSystem.shape.alignToDirection;
            root.shapeModule.radius                   = particleSystem.shape.radius;
            // root.shapeModule.radiusMode = particleSystem.shape.radiusMode;
            // root.shapeModule.radiusSpread = particleSystem.shape.radiusSpread;
            // root.shapeModule.radiusSpeed = CreateMinMaxCurve(ref shapeModuleClips, particleSystem.shape.radiusSpeed);
            root.shapeModule.radiusThickness = particleSystem.shape.radiusThickness;
            root.shapeModule.angleRadians    = math.radians(particleSystem.shape.angle);
            root.shapeModule.length          = particleSystem.shape.length;
            root.shapeModule.boxThickness    = particleSystem.shape.boxThickness;
            //TODO:  Meshes
            root.shapeModule.arcRadians           = math.radians(particleSystem.shape.arc);
            root.shapeModule.arcMode              = particleSystem.shape.arcMode;
            root.shapeModule.arcSpreadRadians     = math.radians(particleSystem.shape.arcSpread);
            root.shapeModule.arcSpeed             = CreateMinMaxCurve(ref shapeModuleClips, particleSystem.shape.arcSpeed);
            root.shapeModule.arcSpeedMultiplier   = particleSystem.shape.arcSpeedMultiplier;
            root.shapeModule.donutRadius          = particleSystem.shape.donutRadius;
            root.shapeModule.position             = particleSystem.shape.position;
            root.shapeModule.rotationEulerRadians = math.radians(particleSystem.shape.rotation);
            root.shapeModule.scale                = particleSystem.shape.scale;
            //TODO:  Textures

            //Create velocity over lifetime module
            var velocityOverLifetimeModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.velocityOverLifetimeModule.enabled                  = particleSystem.velocityOverLifetime.enabled;
            root.velocityOverLifetimeModule.x                        = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.x);
            root.velocityOverLifetimeModule.xMultiplier              = particleSystem.velocityOverLifetime.xMultiplier;
            root.velocityOverLifetimeModule.y                        = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.y);
            root.velocityOverLifetimeModule.yMultiplier              = particleSystem.velocityOverLifetime.yMultiplier;
            root.velocityOverLifetimeModule.z                        = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.z);
            root.velocityOverLifetimeModule.zMultiplier              = particleSystem.velocityOverLifetime.zMultiplier;
            root.velocityOverLifetimeModule.orbitalX                 = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.orbitalX);
            root.velocityOverLifetimeModule.orbitalXMultiplier       = particleSystem.velocityOverLifetime.orbitalXMultiplier;
            root.velocityOverLifetimeModule.orbitalY                 = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.orbitalY);
            root.velocityOverLifetimeModule.orbitalYMultiplier       = particleSystem.velocityOverLifetime.orbitalYMultiplier;
            root.velocityOverLifetimeModule.orbitalZ                 = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.orbitalZ);
            root.velocityOverLifetimeModule.orbitalZMultiplier       = particleSystem.velocityOverLifetime.orbitalZMultiplier;
            root.velocityOverLifetimeModule.orbitalOffsetX           = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.orbitalOffsetX);
            root.velocityOverLifetimeModule.orbitalOffsetXMultiplier = particleSystem.velocityOverLifetime.orbitalOffsetXMultiplier;
            root.velocityOverLifetimeModule.orbitalOffsetY           = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.orbitalOffsetY);
            root.velocityOverLifetimeModule.orbitalOffsetYMultiplier = particleSystem.velocityOverLifetime.orbitalOffsetYMultiplier;
            root.velocityOverLifetimeModule.orbitalOffsetZ           = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.orbitalOffsetZ);
            root.velocityOverLifetimeModule.orbitalOffsetZMultiplier = particleSystem.velocityOverLifetime.orbitalOffsetZMultiplier;
            root.velocityOverLifetimeModule.radial                   = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.radial);
            root.velocityOverLifetimeModule.radialMultiplier         = particleSystem.velocityOverLifetime.radialMultiplier;
            root.velocityOverLifetimeModule.speedModifier            = CreateMinMaxCurve(ref velocityOverLifetimeModuleClips, particleSystem.velocityOverLifetime.speedModifier);
            root.velocityOverLifetimeModule.speedModifierMultiplier  = particleSystem.velocityOverLifetime.speedModifierMultiplier;
            root.velocityOverLifetimeModule.space                    = particleSystem.velocityOverLifetime.space;

            //Create limit velocity over lifetime module
            var limitVelocityOverLifetimeModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.limitVelocityOverLifetimeModule.enabled = particleSystem.limitVelocityOverLifetime.enabled;
            root.limitVelocityOverLifetimeModule.limitX  = CreateMinMaxCurve(ref limitVelocityOverLifetimeModuleClips,
                                                                             particleSystem.limitVelocityOverLifetime.limitX);
            root.limitVelocityOverLifetimeModule.limitXMultiplier = particleSystem.limitVelocityOverLifetime.limitXMultiplier;
            root.limitVelocityOverLifetimeModule.limitY           = CreateMinMaxCurve(ref limitVelocityOverLifetimeModuleClips,
                                                                                      particleSystem.limitVelocityOverLifetime.limitY);
            root.limitVelocityOverLifetimeModule.limitYMultiplier = particleSystem.limitVelocityOverLifetime.limitYMultiplier;
            root.limitVelocityOverLifetimeModule.limitZ           = CreateMinMaxCurve(ref limitVelocityOverLifetimeModuleClips,
                                                                                      particleSystem.limitVelocityOverLifetime.limitZ);
            root.limitVelocityOverLifetimeModule.limitZMultiplier = particleSystem.limitVelocityOverLifetime.limitZMultiplier;
            root.limitVelocityOverLifetimeModule.limit            = CreateMinMaxCurve(ref limitVelocityOverLifetimeModuleClips,
                                                                                      particleSystem.limitVelocityOverLifetime.limit);
            root.limitVelocityOverLifetimeModule.limitMultiplier = particleSystem.limitVelocityOverLifetime.limitMultiplier;
            root.limitVelocityOverLifetimeModule.dampen          = particleSystem.limitVelocityOverLifetime.dampen;
            root.limitVelocityOverLifetimeModule.separateAxes    = particleSystem.limitVelocityOverLifetime.separateAxes;
            root.limitVelocityOverLifetimeModule.space           = particleSystem.limitVelocityOverLifetime.space;
            root.limitVelocityOverLifetimeModule.drag            = CreateMinMaxCurve(ref limitVelocityOverLifetimeModuleClips,
                                                                                     particleSystem.limitVelocityOverLifetime.drag);
            root.limitVelocityOverLifetimeModule.dragMultiplier                 = particleSystem.limitVelocityOverLifetime.dragMultiplier;
            root.limitVelocityOverLifetimeModule.multiplyDragByParticleVelocity = particleSystem.limitVelocityOverLifetime.multiplyDragByParticleVelocity;
            root.limitVelocityOverLifetimeModule.multiplyDragByParticleSize     = particleSystem.limitVelocityOverLifetime.multiplyDragByParticleSize;

            //Create inherit velocity module
            var inheritVelocityModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.inheritVelocityModule.enabled         = particleSystem.inheritVelocity.enabled;
            root.inheritVelocityModule.mode            = particleSystem.inheritVelocity.mode;
            root.inheritVelocityModule.curve           = CreateMinMaxCurve(ref inheritVelocityModuleClips, particleSystem.inheritVelocity.curve);
            root.inheritVelocityModule.curveMultiplier = particleSystem.inheritVelocity.curveMultiplier;

            //Create lifetime by emitter speed module
            var lifetimeByEmitterSpeedModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.lifetimeByEmitterSpeedModule.enabled         = particleSystem.lifetimeByEmitterSpeed.enabled;
            root.lifetimeByEmitterSpeedModule.curve           = CreateMinMaxCurve(ref lifetimeByEmitterSpeedModuleClips, particleSystem.lifetimeByEmitterSpeed.curve);
            root.lifetimeByEmitterSpeedModule.curveMultiplier = particleSystem.lifetimeByEmitterSpeed.curveMultiplier;
            root.lifetimeByEmitterSpeedModule.range           = particleSystem.lifetimeByEmitterSpeed.range;

            //Create force over lifetime module
            var forceOverLifetimeModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.forceOverLifetimeModule.enabled     = particleSystem.forceOverLifetime.enabled;
            root.forceOverLifetimeModule.x           = CreateMinMaxCurve(ref forceOverLifetimeModuleClips, particleSystem.forceOverLifetime.x);
            root.forceOverLifetimeModule.xMultiplier = particleSystem.forceOverLifetime.xMultiplier;
            root.forceOverLifetimeModule.y           = CreateMinMaxCurve(ref forceOverLifetimeModuleClips, particleSystem.forceOverLifetime.y);
            root.forceOverLifetimeModule.yMultiplier = particleSystem.forceOverLifetime.yMultiplier;
            root.forceOverLifetimeModule.z           = CreateMinMaxCurve(ref forceOverLifetimeModuleClips, particleSystem.forceOverLifetime.z);
            root.forceOverLifetimeModule.zMultiplier = particleSystem.forceOverLifetime.zMultiplier;
            root.forceOverLifetimeModule.space       = particleSystem.forceOverLifetime.space;
            root.forceOverLifetimeModule.randomized  = particleSystem.forceOverLifetime.randomized;

            //Create color over lifetime module
            var colorOverLifetimeModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.colorOverLifetimeModule.enabled = particleSystem.colorOverLifetime.enabled;
            CreateMinMaxGradient(ref colorOverLifetimeModuleClips,
                                 particleSystem.colorOverLifetime.color,
                                 out root.colorOverLifetimeModule.colorRed,
                                 out root.colorOverLifetimeModule.colorGreen,
                                 out root.colorOverLifetimeModule.colorBlue,
                                 out root.colorOverLifetimeModule.colorAlpha);

            //Create color by speed module
            var colorBySpeedModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.colorBySpeedModule.enabled = particleSystem.colorBySpeed.enabled;
            CreateMinMaxGradient(ref colorBySpeedModuleClips,
                                 particleSystem.colorBySpeed.color,
                                 out root.colorBySpeedModule.colorRed,
                                 out root.colorBySpeedModule.colorGreen,
                                 out root.colorBySpeedModule.colorBlue,
                                 out root.colorBySpeedModule.colorAlpha);
            root.colorBySpeedModule.range = particleSystem.colorBySpeed.range;

            //Create size over lifetime module
            var sizeOverLifetimeModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.sizeOverLifetimeModule.enabled      = particleSystem.sizeOverLifetime.enabled;
            root.sizeOverLifetimeModule.x            = CreateMinMaxCurve(ref sizeOverLifetimeModuleClips, particleSystem.sizeOverLifetime.x);
            root.sizeOverLifetimeModule.xMultiplier  = particleSystem.sizeOverLifetime.xMultiplier;
            root.sizeOverLifetimeModule.y            = CreateMinMaxCurve(ref sizeOverLifetimeModuleClips, particleSystem.sizeOverLifetime.y);
            root.sizeOverLifetimeModule.yMultiplier  = particleSystem.sizeOverLifetime.yMultiplier;
            root.sizeOverLifetimeModule.z            = CreateMinMaxCurve(ref sizeOverLifetimeModuleClips, particleSystem.sizeOverLifetime.z);
            root.sizeOverLifetimeModule.zMultiplier  = particleSystem.sizeOverLifetime.zMultiplier;
            root.sizeOverLifetimeModule.separateAxes = particleSystem.sizeOverLifetime.separateAxes;

            //Create size by speed module
            var sizeBySpeedModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.sizeBySpeedModule.enabled      = particleSystem.sizeBySpeed.enabled;
            root.sizeBySpeedModule.x            = CreateMinMaxCurve(ref sizeBySpeedModuleClips, particleSystem.sizeBySpeed.x);
            root.sizeBySpeedModule.xMultiplier  = particleSystem.sizeBySpeed.xMultiplier;
            root.sizeBySpeedModule.y            = CreateMinMaxCurve(ref sizeBySpeedModuleClips, particleSystem.sizeBySpeed.y);
            root.sizeBySpeedModule.yMultiplier  = particleSystem.sizeBySpeed.yMultiplier;
            root.sizeBySpeedModule.z            = CreateMinMaxCurve(ref sizeBySpeedModuleClips, particleSystem.sizeBySpeed.z);
            root.sizeBySpeedModule.zMultiplier  = particleSystem.sizeBySpeed.zMultiplier;
            root.sizeBySpeedModule.separateAxes = particleSystem.sizeBySpeed.separateAxes;
            root.sizeBySpeedModule.range        = particleSystem.sizeBySpeed.range;

            //Create rotation over lifetime module
            var rotationOverLifetimeModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.rotationOverLifetimeModule.enabled      = particleSystem.rotationOverLifetime.enabled;
            root.rotationOverLifetimeModule.x            = CreateMinMaxCurve(ref rotationOverLifetimeModuleClips, particleSystem.rotationOverLifetime.x, math.radians);
            root.rotationOverLifetimeModule.xMultiplier  = particleSystem.rotationOverLifetime.xMultiplier;
            root.rotationOverLifetimeModule.y            = CreateMinMaxCurve(ref rotationOverLifetimeModuleClips, particleSystem.rotationOverLifetime.y, math.radians);
            root.rotationOverLifetimeModule.yMultiplier  = particleSystem.rotationOverLifetime.yMultiplier;
            root.rotationOverLifetimeModule.z            = CreateMinMaxCurve(ref rotationOverLifetimeModuleClips, particleSystem.rotationOverLifetime.z, math.radians);
            root.rotationOverLifetimeModule.zMultiplier  = particleSystem.rotationOverLifetime.zMultiplier;
            root.rotationOverLifetimeModule.separateAxes = particleSystem.rotationOverLifetime.separateAxes;

            //Create rotation by speed module
            var rotationBySpeedModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.rotationBySpeedModule.enabled      = particleSystem.rotationBySpeed.enabled;
            root.rotationBySpeedModule.x            = CreateMinMaxCurve(ref rotationBySpeedModuleClips, particleSystem.rotationBySpeed.x, math.radians);
            root.rotationBySpeedModule.xMultiplier  = particleSystem.rotationBySpeed.xMultiplier;
            root.rotationBySpeedModule.y            = CreateMinMaxCurve(ref rotationBySpeedModuleClips, particleSystem.rotationBySpeed.y, math.radians);
            root.rotationBySpeedModule.yMultiplier  = particleSystem.rotationBySpeed.yMultiplier;
            root.rotationBySpeedModule.z            = CreateMinMaxCurve(ref rotationBySpeedModuleClips, particleSystem.rotationBySpeed.z, math.radians);
            root.rotationBySpeedModule.zMultiplier  = particleSystem.rotationBySpeed.zMultiplier;
            root.rotationBySpeedModule.separateAxes = particleSystem.rotationBySpeed.separateAxes;
            root.rotationBySpeedModule.range        = particleSystem.rotationBySpeed.range;

            //Create external forces module
            var externalForcesModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.externalForcesModule.enabled         = particleSystem.externalForces.enabled;
            root.externalForcesModule.multiplier      = particleSystem.externalForces.multiplier;
            root.externalForcesModule.multiplierCurve = CreateMinMaxCurve(ref externalForcesModuleClips, particleSystem.externalForces.multiplierCurve);
            root.externalForcesModule.influenceFilter = particleSystem.externalForces.influenceFilter;
            root.externalForcesModule.influenceMask   = (uint)particleSystem.externalForces.influenceMask.value;

            //Create noise module
            var noiseModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.noiseModule.enabled               = particleSystem.noise.enabled;
            root.noiseModule.separateAxes          = particleSystem.noise.separateAxes;
            root.noiseModule.strengthX             = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.strengthX);
            root.noiseModule.strengthXMultiplier   = particleSystem.noise.strengthXMultiplier;
            root.noiseModule.strengthY             = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.strengthY);
            root.noiseModule.strengthYMultiplier   = particleSystem.noise.strengthYMultiplier;
            root.noiseModule.strengthZ             = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.strengthZ);
            root.noiseModule.strengthZMultiplier   = particleSystem.noise.strengthZMultiplier;
            root.noiseModule.frequency             = particleSystem.noise.frequency;
            root.noiseModule.scrollSpeed           = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.scrollSpeed);
            root.noiseModule.scrollSpeedMultiplier = particleSystem.noise.scrollSpeedMultiplier;
            root.noiseModule.damping               = particleSystem.noise.damping;
            root.noiseModule.octaveCount           = particleSystem.noise.octaveCount;
            root.noiseModule.octaveMultiplier      = particleSystem.noise.octaveMultiplier;
            root.noiseModule.octaveScale           = particleSystem.noise.octaveScale;
            root.noiseModule.quality               = particleSystem.noise.quality;
            root.noiseModule.remapEnabled          = particleSystem.noise.remapEnabled;
            root.noiseModule.remapX                = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.remapX);
            root.noiseModule.remapXMultiplier      = particleSystem.noise.remapXMultiplier;
            root.noiseModule.remapY                = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.remapY);
            root.noiseModule.remapYMultiplier      = particleSystem.noise.remapYMultiplier;
            root.noiseModule.remapZ                = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.remapZ);
            root.noiseModule.remapZMultiplier      = particleSystem.noise.remapZMultiplier;
            root.noiseModule.positionAmount        = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.positionAmount);
            root.noiseModule.rotationAmount        = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.rotationAmount);
            root.noiseModule.sizeAmount            = CreateMinMaxCurve(ref noiseModuleClips, particleSystem.noise.sizeAmount);

            //Create collision module
            var collisionModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.collisionModule.enabled                               = particleSystem.collision.enabled;
            root.collisionModule.type                                  = particleSystem.collision.type;
            root.collisionModule.mode                                  = particleSystem.collision.mode;
            root.collisionModule.quality                               = particleSystem.collision.quality;
            root.collisionModule.maxCollisionShapes                    = particleSystem.collision.maxCollisionShapes;
            root.collisionModule.collidesWith                          = (uint)particleSystem.collision.collidesWith.value;
            root.collisionModule.enableDynamicColliders                = particleSystem.collision.enableDynamicColliders;
            root.collisionModule.bounce                                = CreateMinMaxCurve(ref collisionModuleClips, particleSystem.collision.bounce);
            root.collisionModule.bounceMultiplier                      = particleSystem.collision.bounceMultiplier;
            root.collisionModule.dampen                                = CreateMinMaxCurve(ref collisionModuleClips, particleSystem.collision.dampen);
            root.collisionModule.dampenMultiplier                      = particleSystem.collision.dampenMultiplier;
            root.collisionModule.lifetimeLoss                          = CreateMinMaxCurve(ref collisionModuleClips, particleSystem.collision.lifetimeLoss);
            root.collisionModule.lifetimeLossMultiplier                = particleSystem.collision.lifetimeLossMultiplier;
            root.collisionModule.minKillSpeed                          = particleSystem.collision.minKillSpeed;
            root.collisionModule.maxKillSpeed                          = particleSystem.collision.maxKillSpeed;
            root.collisionModule.voxelSize                             = particleSystem.collision.voxelSize;
            root.collisionModule.radiusScale                           = particleSystem.collision.radiusScale;
            root.collisionModule.sendCollisionMessages                 = particleSystem.collision.sendCollisionMessages;
            root.collisionModule.colliderForce                         = particleSystem.collision.colliderForce;
            root.collisionModule.multiplyColliderForceByCollisionAngle = particleSystem.collision.multiplyColliderForceByCollisionAngle;
            root.collisionModule.multiplyColliderForceByParticleSpeed  = particleSystem.collision.multiplyColliderForceByParticleSpeed;
            root.collisionModule.multiplyColliderForceByParticleSize   = particleSystem.collision.multiplyColliderForceByParticleSize;

            //Create trigger module
            root.triggerModule.enabled           = particleSystem.trigger.enabled;
            root.triggerModule.inside            = particleSystem.trigger.inside;
            root.triggerModule.outside           = particleSystem.trigger.outside;
            root.triggerModule.enter             = particleSystem.trigger.enter;
            root.triggerModule.exit              = particleSystem.trigger.exit;
            root.triggerModule.colliderQueryMode = particleSystem.trigger.colliderQueryMode;
            root.triggerModule.radiusScale       = particleSystem.trigger.radiusScale;

            //Create sub emitters module
            //TODO:  Sub emitters
            root.subEmittersModule.enabled          = particleSystem.subEmitters.enabled;
            root.subEmittersModule.subEmittersCount = particleSystem.subEmitters.subEmittersCount;

            //Create texture sheet animation module
            var textureSheetAnimationModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.textureSheetAnimationModule.enabled                 = particleSystem.textureSheetAnimation.enabled;
            root.textureSheetAnimationModule.mode                    = particleSystem.textureSheetAnimation.mode;
            root.textureSheetAnimationModule.numTilesX               = particleSystem.textureSheetAnimation.numTilesX;
            root.textureSheetAnimationModule.numTilesY               = particleSystem.textureSheetAnimation.numTilesY;
            root.textureSheetAnimationModule.animation               = particleSystem.textureSheetAnimation.animation;
            root.textureSheetAnimationModule.rowMode                 = particleSystem.textureSheetAnimation.rowMode;
            root.textureSheetAnimationModule.frameOverTime           = CreateMinMaxCurve(ref textureSheetAnimationModuleClips, particleSystem.textureSheetAnimation.frameOverTime);
            root.textureSheetAnimationModule.frameOverTimeMultiplier = particleSystem.textureSheetAnimation.frameOverTimeMultiplier;
            root.textureSheetAnimationModule.startFrame              = CreateMinMaxCurve(ref textureSheetAnimationModuleClips, particleSystem.textureSheetAnimation.startFrame);
            root.textureSheetAnimationModule.startFrameMultiplier    = particleSystem.textureSheetAnimation.startFrameMultiplier;
            root.textureSheetAnimationModule.cycleCount              = particleSystem.textureSheetAnimation.cycleCount;
            root.textureSheetAnimationModule.rowIndex                = particleSystem.textureSheetAnimation.rowIndex;
            root.textureSheetAnimationModule.uvChannelMask           = (UVChannelFlags)particleSystem.textureSheetAnimation.uvChannelMask;
            root.textureSheetAnimationModule.spriteCount             = particleSystem.textureSheetAnimation.spriteCount;
            root.textureSheetAnimationModule.speedRange              = particleSystem.textureSheetAnimation.speedRange;

            //Create lights module
            //TODO: Lights
            var lightsModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.lightsModule.enabled               = particleSystem.lights.enabled;
            root.lightsModule.ratio                 = particleSystem.lights.ratio;
            root.lightsModule.useRandomDistribution = particleSystem.lights.useRandomDistribution;
            //root.lightsModule.light = particleSystem.lights.light;
            root.lightsModule.useParticleColor      = particleSystem.lights.useParticleColor;
            root.lightsModule.sizeAffectsRange      = particleSystem.lights.sizeAffectsRange;
            root.lightsModule.alphaAffectsIntensity = particleSystem.lights.alphaAffectsIntensity;
            root.lightsModule.range                 = CreateMinMaxCurve(ref lightsModuleClips, particleSystem.lights.range);
            root.lightsModule.rangeMultiplier       = particleSystem.lights.rangeMultiplier;
            root.lightsModule.intensity             = CreateMinMaxCurve(ref lightsModuleClips, particleSystem.lights.intensity);
            root.lightsModule.intensityMultiplier   = particleSystem.lights.intensityMultiplier;
            root.lightsModule.maxLights             = particleSystem.lights.maxLights;

            //Create trails module
            var trailsModuleClips = new NativeList<Compression.AclCompressedClipResult>(Allocator.Temp);
            root.trailsModule.enabled              = particleSystem.trails.enabled;
            root.trailsModule.mode                 = particleSystem.trails.mode;
            root.trailsModule.ratio                = particleSystem.trails.ratio;
            root.trailsModule.lifetime             = CreateMinMaxCurve(ref trailsModuleClips, particleSystem.trails.lifetime);
            root.trailsModule.lifetimeMultiplier   = particleSystem.trails.lifetimeMultiplier;
            root.trailsModule.minVertexDistance    = particleSystem.trails.minVertexDistance;
            root.trailsModule.textureMode          = particleSystem.trails.textureMode;
            root.trailsModule.textureScale         = particleSystem.trails.textureScale;
            root.trailsModule.worldSpace           = particleSystem.trails.worldSpace;
            root.trailsModule.dieWithParticles     = particleSystem.trails.dieWithParticles;
            root.trailsModule.sizeAffectsWidth     = particleSystem.trails.sizeAffectsWidth;
            root.trailsModule.sizeAffectsLifetime  = particleSystem.trails.sizeAffectsLifetime;
            root.trailsModule.inheritParticleColor = particleSystem.trails.inheritParticleColor;
            CreateMinMaxGradient(ref trailsModuleClips,
                                 particleSystem.trails.colorOverLifetime.color,
                                 out root.trailsModule.colorOverLifetimeRed,
                                 out root.trailsModule.colorOverLifetimeGreen,
                                 out root.trailsModule.colorOverLifetimeBlue,
                                 out root.trailsModule.colorOverLifetimeAlpha);
            root.trailsModule.widthOverTrail           = CreateMinMaxCurve(ref trailsModuleClips, particleSystem.trails.widthOverTrail);
            root.trailsModule.widthOverTrailMultiplier = particleSystem.trails.widthOverTrailMultiplier;
            CreateMinMaxGradient(ref trailsModuleClips,
                                 particleSystem.trails.colorOverTrail.color,
                                 out root.trailsModule.colorOverTrailRed,
                                 out root.trailsModule.colorOverTrailGreen,
                                 out root.trailsModule.colorOverTrailBlue,
                                 out root.trailsModule.colorOverTrailAlpha);
            root.trailsModule.generateLightingData     = particleSystem.trails.generateLightingData;
            root.trailsModule.ribbonCount              = particleSystem.trails.ribbonCount;
            root.trailsModule.shadowBias               = particleSystem.trails.shadowBias;
            root.trailsModule.splitSubEmitterRibbons   = particleSystem.trails.splitSubEmitterRibbons;
            root.trailsModule.attachRibbonsToTransform = particleSystem.trails.attachRibbonsToTransform;

            //Create custom data module

            //Create renderer module

            var moduleClips = builder.Allocate(ref root.sampledClips.clips, moduleCount);

            //Write clip data
            short parameterCount = 0;
            ref var mainModuleClip = ref moduleClips[0];
            WriteParameterClips(ref builder,
                                ref mainModuleClip,
                                ref mainModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var emissionModuleClip = ref moduleClips[1];
            WriteParameterClips(ref builder,
                                ref emissionModuleClip,
                                ref emissionModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var shapeModuleClip = ref moduleClips[2];
            WriteParameterClips(ref builder,
                                ref shapeModuleClip,
                                ref shapeModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var velocityOverLifetimeModuleClip = ref moduleClips[3];
            WriteParameterClips(ref builder,
                                ref velocityOverLifetimeModuleClip,
                                ref velocityOverLifetimeModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var limitVelocityOverLifetimeModuleClip = ref moduleClips[4];
            WriteParameterClips(ref builder,
                                ref limitVelocityOverLifetimeModuleClip,
                                ref limitVelocityOverLifetimeModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var inheritVelocityModuleClip = ref moduleClips[5];
            WriteParameterClips(ref builder,
                                ref inheritVelocityModuleClip,
                                ref inheritVelocityModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var lifetimeByEmitterSpeedModuleClip = ref moduleClips[6];
            WriteParameterClips(ref builder,
                                ref lifetimeByEmitterSpeedModuleClip,
                                ref lifetimeByEmitterSpeedModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var forceOverLifetimeModuleClip = ref moduleClips[7];
            WriteParameterClips(ref builder,
                                ref forceOverLifetimeModuleClip,
                                ref forceOverLifetimeModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var colorOverLifetimeModuleClip = ref moduleClips[8];
            WriteParameterClips(ref builder,
                                ref colorOverLifetimeModuleClip,
                                ref colorOverLifetimeModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var colorBySpeedModuleClip = ref moduleClips[9];
            WriteParameterClips(ref builder,
                                ref colorBySpeedModuleClip,
                                ref colorBySpeedModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var sizeOverLifetimeModuleClip = ref moduleClips[10];
            WriteParameterClips(ref builder,
                                ref sizeOverLifetimeModuleClip,
                                ref sizeOverLifetimeModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var sizeBySpeedModuleClip = ref moduleClips[11];
            WriteParameterClips(ref builder,
                                ref sizeBySpeedModuleClip,
                                ref sizeBySpeedModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var rotationOverLifetimeModuleClip = ref moduleClips[12];
            WriteParameterClips(ref builder,
                                ref rotationOverLifetimeModuleClip,
                                ref rotationOverLifetimeModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var rotationBySpeedModuleClip = ref moduleClips[13];
            WriteParameterClips(ref builder,
                                ref rotationBySpeedModuleClip,
                                ref rotationBySpeedModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var externalForcesModuleClip = ref moduleClips[14];
            WriteParameterClips(ref builder,
                                ref externalForcesModuleClip,
                                ref externalForcesModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var noiseModuleClip = ref moduleClips[15];
            WriteParameterClips(ref builder,
                                ref noiseModuleClip,
                                ref noiseModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var collisionModuleClip = ref moduleClips[16];
            WriteParameterClips(ref builder,
                                ref collisionModuleClip,
                                ref collisionModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var textureSheetAnimationModuleClip = ref moduleClips[17];
            WriteParameterClips(ref builder,
                                ref textureSheetAnimationModuleClip,
                                ref textureSheetAnimationModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var lightsModuleClip = ref moduleClips[18];
            WriteParameterClips(ref builder,
                                ref lightsModuleClip,
                                ref lightsModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);
            ref var trailsModuleClip = ref moduleClips[19];
            WriteParameterClips(ref builder,
                                ref trailsModuleClip,
                                ref trailsModuleClips,
                                ref clipNameList,
                                ref clipNameHashList,
                                ref parameterCount);

            root.sampledClips.parameterCount = parameterCount;
            var parameterNames      = builder.Allocate(ref root.sampledClips.parameterNames, parameterCount);
            var parameterNameHashes = builder.Allocate(ref root.sampledClips.parameterNameHashes, parameterCount);
            for (int i = 0; i < parameterCount; i++)
            {
                parameterNames[i]      = clipNameList[i];
                parameterNameHashes[i] = clipNameHashList[i];
            }

            var result = builder.CreateBlobAssetReference<ShurikenModulesBlob>(Allocator.Persistent);

            return result;
        }

        private unsafe void WriteParameterClips(ref BlobBuilder builder, ref ParameterClip moduleClip, ref NativeList<Compression.AclCompressedClipResult> moduleParameterClips,
                                                ref NativeList<FixedString64Bytes> parameterNameList, ref NativeList<int> parameterNameHashList, ref short parameterCount)
        {
            for (int i = 0; i < moduleParameterClips.Length; i++)
            {
                var compressedClip = moduleParameterClips[i];
                // Dreaming -> Sovogal: I'm not going to expose compressedClipDataAligned16 here. Misuse can hard-crash Unity.
                // Besides, you can't pack multiple AclCompressedClipResult instances into a single ParameterClip.
                // I'll add a proper API for building embedded ParameterClip and ParameterClipSetBlob instances inside of other blob types.
                //var compressedData = builder.Allocate(ref moduleClip.compressedClipDataAligned16, compressedClip.sizeInBytes, 16);
                //compressedClip.CopyTo((byte*)compressedData.GetUnsafePtr());
                compressedClip.Dispose();

                var clipName = $"Clip{i}";
                parameterNameList.Add(clipName);
                parameterNameHashList.Add(clipName.GetHashCode());
                parameterCount++;
            }
        }

        private void CreateMinMaxGradient(ref NativeList<Compression.AclCompressedClipResult> parameterClips,
                                          in ParticleSystem.MinMaxGradient gradient,
                                          out MinMaxCurvePacked minMaxCurveRed,
                                          out MinMaxCurvePacked minMaxCurveGreen,
                                          out MinMaxCurvePacked minMaxCurveBlue,
                                          out MinMaxCurvePacked minMaxCurveAlpha)
        {
            var gradientMin = gradient.gradientMin;

            var minRedIndex   = parameterClips.Length;
            var minGreenIndex = parameterClips.Length + 1;
            var minBlueIndex  = parameterClips.Length + 2;
            var minAlphaIndex = parameterClips.Length + 3;
            GetClipsFromGradient(ref parameterClips, gradientMin);

            var gradientMax   = gradient.gradientMax;
            var maxRedIndex   = parameterClips.Length;
            var maxGreenIndex = parameterClips.Length + 1;
            var maxBlueIndex  = parameterClips.Length + 2;
            var maxAlphaIndex = parameterClips.Length + 3;
            GetClipsFromGradient(ref parameterClips, gradientMax);

            minMaxCurveRed                          = new MinMaxCurvePacked();
            minMaxCurveRed.mode                     = ParticleSystemCurveMode.TwoCurves;
            minMaxCurveRed.curveMinParameterIndex   = (byte)minRedIndex;
            minMaxCurveRed.curveMaxParameterIndex   = (byte)maxRedIndex;
            minMaxCurveGreen                        = new MinMaxCurvePacked();
            minMaxCurveGreen.mode                   = ParticleSystemCurveMode.TwoCurves;
            minMaxCurveGreen.curveMinParameterIndex = (byte)minGreenIndex;
            minMaxCurveGreen.curveMaxParameterIndex = (byte)maxGreenIndex;
            minMaxCurveBlue                         = new MinMaxCurvePacked();
            minMaxCurveBlue.mode                    = ParticleSystemCurveMode.TwoCurves;
            minMaxCurveBlue.curveMinParameterIndex  = (byte)minBlueIndex;
            minMaxCurveBlue.curveMaxParameterIndex  = (byte)maxBlueIndex;
            minMaxCurveAlpha                        = new MinMaxCurvePacked();
            minMaxCurveAlpha.mode                   = ParticleSystemCurveMode.TwoCurves;
            minMaxCurveAlpha.curveMinParameterIndex = (byte)minAlphaIndex;
            minMaxCurveAlpha.curveMaxParameterIndex = (byte)maxAlphaIndex;
        }

        private MinMaxCurvePacked CreateMinMaxCurve(ref NativeList<Compression.AclCompressedClipResult> parameterClips,
                                                    in ParticleSystem.MinMaxCurve minMaxCurve, Func<float, float> valueModifier = null)
        {
            MinMaxCurvePacked minMaxCurvePacked = new MinMaxCurvePacked();
            minMaxCurvePacked.mode = minMaxCurve.mode;
            switch (minMaxCurvePacked.mode)
            {
                case ParticleSystemCurveMode.Constant:
                {
                    minMaxCurvePacked.constant = minMaxCurve.constant;
                    break;
                }
                case ParticleSystemCurveMode.TwoConstants:
                {
                    minMaxCurvePacked.constantMin = minMaxCurve.constantMin;
                    minMaxCurvePacked.constantMax = minMaxCurve.constantMax;
                    break;
                }
                case ParticleSystemCurveMode.Curve:
                {
                    var compressedClip = GetClipFromCurve(minMaxCurve.curve, valueModifier);
                    var parameterIndex = parameterClips.Length;

                    minMaxCurvePacked.curveMinParameterIndex = (byte)parameterIndex;
                    parameterClips.Add(compressedClip);
                    break;
                }
                case ParticleSystemCurveMode.TwoCurves:
                {
                    break;
                }
            }

            return minMaxCurvePacked;
        }

        private Compression.AclCompressedClipResult GetClipFromCurve(AnimationCurve curve, Func<float, float> valueModifier = null)
        {
            const int sampleRate       = 60;
            const short compressionLevel = 0;
            var curveTime        = curve.keys[^ 1].time;
            var sampleInterval   = curveTime / sampleRate;
            float totalTime        = 0f;

            NativeList<float> samples = new NativeList<float>(Allocator.Temp);
            while (totalTime <= curveTime)
            {
                float value = valueModifier == null? curve.Evaluate(totalTime) : valueModifier(curve.Evaluate(totalTime));
                samples.Add(value);
                totalTime += sampleInterval;
            }

            NativeArray<float> errors = new NativeArray<float>(1, Allocator.Temp);
            errors[0] = 10f;  //??

            return AclUnity.Compression.CompressScalarsClip(samples.AsArray(), errors, sampleRate, compressionLevel);
        }

        private void GetClipsFromGradient(ref NativeList<Compression.AclCompressedClipResult> parameterClips, Gradient gradient)
        {
            const int sampleRate       = 60;
            const short compressionLevel = 0;
            var curveTime        = 1f;
            var sampleInterval   = curveTime / sampleRate;

            NativeArray<float> redSamples   = new NativeArray<float>(sampleRate, Allocator.Temp);
            NativeArray<float> greenSamples = new NativeArray<float>(sampleRate, Allocator.Temp);
            NativeArray<float> blueSamples  = new NativeArray<float>(sampleRate, Allocator.Temp);
            NativeArray<float> alphaSamples = new NativeArray<float>(sampleRate, Allocator.Temp);

            for (int i = 0; i < sampleRate; i++)
            {
                var time  = i * sampleInterval;
                var color = gradient.Evaluate(time);

                redSamples[i]   = color.r;
                greenSamples[i] = color.g;
                blueSamples[i]  = color.b;
                alphaSamples[i] = color.a;
            }

            NativeArray<float> errors = new NativeArray<float>(4, Allocator.Temp);
            errors[0] = 10f;  //??

            parameterClips.Add(AclUnity.Compression.CompressScalarsClip(redSamples, errors, sampleRate, compressionLevel));
            parameterClips.Add(AclUnity.Compression.CompressScalarsClip(greenSamples, errors, sampleRate, compressionLevel));
            parameterClips.Add(AclUnity.Compression.CompressScalarsClip(blueSamples, errors, sampleRate, compressionLevel));
            parameterClips.Add(AclUnity.Compression.CompressScalarsClip(alphaSamples, errors, sampleRate, compressionLevel));
        }
    }
}
#endif

