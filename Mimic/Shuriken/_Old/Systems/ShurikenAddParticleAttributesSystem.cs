#if false
using Latios.LifeFX;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

using static Unity.Entities.SystemAPI;

namespace Latios.Mimic.Shuriken.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ShurikenAddParticleAttributesSystem : ISystem
    {
        private EntityQuery m_query;
        private EntityQuery m_ecbQuery;

        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                          .WithAllRW<ShurikenParticleSystemData>();

            m_query = builder.Build(ref state);
            builder.Dispose();

            m_ecbQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new Unity.Entities.ComponentType[]
                {
                    Unity.Entities.ComponentType.ReadOnly<Unity.Entities.BeginPresentationEntityCommandBufferSystem.Singleton>()
                },
                Any     = new Unity.Entities.ComponentType[] { },
                None    = new Unity.Entities.ComponentType[] { },
                Options = EntityQueryOptions.Default | EntityQueryOptions.IncludeSystems
            });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbParallelWriter = m_ecbQuery
                                    .GetSingleton<BeginPresentationEntityCommandBufferSystem.Singleton>()
                                    .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var job = new ShurikenParticleAttributesJob
            {
                ecbParallelWriter                     = ecbParallelWriter,
                particleSystemHandle                  = GetComponentTypeHandle<ShurikenParticleSystemData>(true),
                mainModuleHandle                      = GetComponentTypeHandle<ShurikenMainModuleOverride>(true),
                velocityOverLifetimeModuleHandle      = GetComponentTypeHandle<ShurikenVelocityOverLifetimeModuleOverride>(true),
                limitVelocityOverLifetimeModuleHandle = GetComponentTypeHandle<ShurikenLimitVelocityOverLifetimeModuleOverride>(true),
                inheritVelocityModuleHandle           = GetComponentTypeHandle<ShurikenInheritVelocityModuleOverride>(true),
                lifetimeByEmitterSpeedModuleHandle    = GetComponentTypeHandle<ShurikenLifetimeByEmitterSpeedModuleOverride>(true),
                forceOverLifetimeModuleHandle         = GetComponentTypeHandle<ShurikenForceOverLifetimeModuleOverride>(true),
                colorOverLifetimeModuleHandle         = GetComponentTypeHandle<ShurikenColorOverLifetimeModuleOverride>(true),
                colorBySpeedModuleHandle              = GetComponentTypeHandle<ShurikenColorBySpeedModuleOverride>(true),
                sizeOverLifetimeModuleHandle          = GetComponentTypeHandle<ShurikenSizeOverLifetimeModuleOverride>(true),
                sizeBySpeedModuleHandle               = GetComponentTypeHandle<ShurikenSizeBySpeedModuleOverride>(true),
                rotationOverLifetimeModuleHandle      = GetComponentTypeHandle<ShurikenRotationOverLifetimeModuleOverride>(true),
                rotationBySpeedModuleHandle           = GetComponentTypeHandle<ShurikenRotationBySpeedModuleOverride>(true),
                externalForcesModuleHandle            = GetComponentTypeHandle<ShurikenExternalForcesModuleOverride>(true),
                noiseModuleHandle                     = GetComponentTypeHandle<ShurikenNoiseModuleOverride>(true),
                particleSeedHandle                    = GetBufferTypeHandle<ParticleSeed>(false),
                particleRotation3dHandle              = GetBufferTypeHandle<ParticleRotation3d>(false),
                particleRotationSpeedHandle           = GetBufferTypeHandle<ParticleRotationSpeed>(false),
                particleScale3dHandle                 = GetBufferTypeHandle<ParticleScale3d>(false),
                particleColorHandle                   = GetBufferTypeHandle<ParticleColor>(false),
                particleVelocityHandle                = GetBufferTypeHandle<ParticleVelocity>(false),
            };

            state.Dependency = job.ScheduleParallel(m_query, state.Dependency);
        }
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        private struct ShurikenParticleAttributesJob : IJobChunk
        {
            [FormerlySerializedAs("ecb")] public EntityCommandBuffer.ParallelWriter ecbParallelWriter;
            public EntityTypeHandle entityHandle;
            public ComponentTypeHandle<ShurikenParticleSystemData> particleSystemHandle;

            //Modules
            public ComponentTypeHandle<ShurikenMainModuleOverride> mainModuleHandle;
            public ComponentTypeHandle<ShurikenVelocityOverLifetimeModuleOverride> velocityOverLifetimeModuleHandle;

            public ComponentTypeHandle<ShurikenLimitVelocityOverLifetimeModuleOverride> limitVelocityOverLifetimeModuleHandle;

            public ComponentTypeHandle<ShurikenInheritVelocityModuleOverride> inheritVelocityModuleHandle;
            public ComponentTypeHandle<ShurikenLifetimeByEmitterSpeedModuleOverride> lifetimeByEmitterSpeedModuleHandle;
            public ComponentTypeHandle<ShurikenForceOverLifetimeModuleOverride> forceOverLifetimeModuleHandle;
            public ComponentTypeHandle<ShurikenColorOverLifetimeModuleOverride> colorOverLifetimeModuleHandle;
            public ComponentTypeHandle<ShurikenColorBySpeedModuleOverride> colorBySpeedModuleHandle;
            public ComponentTypeHandle<ShurikenSizeOverLifetimeModuleOverride> sizeOverLifetimeModuleHandle;
            public ComponentTypeHandle<ShurikenSizeBySpeedModuleOverride> sizeBySpeedModuleHandle;
            public ComponentTypeHandle<ShurikenRotationOverLifetimeModuleOverride> rotationOverLifetimeModuleHandle;

            public ComponentTypeHandle<ShurikenRotationBySpeedModuleOverride> rotationBySpeedModuleHandle;

            public ComponentTypeHandle<ShurikenExternalForcesModuleOverride> externalForcesModuleHandle;
            public ComponentTypeHandle<ShurikenNoiseModuleOverride> noiseModuleHandle;

            //Particle Attributes
            public BufferTypeHandle<ParticleSeed> particleSeedHandle;
            public BufferTypeHandle<ParticleRotation> particleRotationHandle;
            public BufferTypeHandle<ParticleRotation3d> particleRotation3dHandle;
            public BufferTypeHandle<ParticleRotationSpeed> particleRotationSpeedHandle;
            public BufferTypeHandle<ParticleScale> particleScaleHandle;
            public BufferTypeHandle<ParticleScale3d> particleScale3dHandle;
            public BufferTypeHandle<ParticleColor> particleColorHandle;
            public BufferTypeHandle<ParticleVelocity> particleVelocityHandle;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                                in v128 chunkEnabledMask)
            {
                var entities                         = chunk.GetNativeArray(entityHandle);
                var particleSystems                  = chunk.GetNativeArray(ref particleSystemHandle);
                var particleSeedsAccessor            = chunk.GetBufferAccessor(ref particleSeedHandle);
                var mainModules                      = chunk.GetNativeArray(ref mainModuleHandle);
                var velocityOverLifetimeModules      = chunk.GetNativeArray(ref velocityOverLifetimeModuleHandle);
                var limitVelocityOverLifetimeModules = chunk.GetNativeArray(ref limitVelocityOverLifetimeModuleHandle);
                var inheritVelocityModules           = chunk.GetNativeArray(ref inheritVelocityModuleHandle);
                var forceOverLifetimeModules         = chunk.GetNativeArray(ref forceOverLifetimeModuleHandle);
                var colorOverLifetimeModules         = chunk.GetNativeArray(ref colorOverLifetimeModuleHandle);
                var colorBySpeedModules              = chunk.GetNativeArray(ref colorBySpeedModuleHandle);
                var sizeOverLifetimeModules          = chunk.GetNativeArray(ref sizeOverLifetimeModuleHandle);
                var sizeBySpeedModules               = chunk.GetNativeArray(ref sizeBySpeedModuleHandle);
                var rotationOverLifetimeModules      = chunk.GetNativeArray(ref rotationOverLifetimeModuleHandle);
                var rotationBySpeedModules           = chunk.GetNativeArray(ref rotationBySpeedModuleHandle);
                var externalForcesModules            = chunk.GetNativeArray(ref externalForcesModuleHandle);
                var noiseModules                     = chunk.GetNativeArray(ref noiseModuleHandle);
                var particleRotationsAccessor        = chunk.GetBufferAccessor(ref particleRotationHandle);
                var particleRotations3dAccessor      = chunk.GetBufferAccessor(ref particleRotation3dHandle);
                var particleRotationSpeedsAccessor   = chunk.GetBufferAccessor(ref particleRotationSpeedHandle);
                var particleScalesAccessor           = chunk.GetBufferAccessor(ref particleScaleHandle);
                var particleScales3dAccessor         = chunk.GetBufferAccessor(ref particleScale3dHandle);
                var particleColorsAccessor           = chunk.GetBufferAccessor(ref particleColorHandle);
                var particleVelocitiesAccessor       = chunk.GetBufferAccessor(ref particleVelocityHandle);

                for (int entityIndexInChunk = 0; entityIndexInChunk < chunk.Count; entityIndexInChunk++)
                {
                    var particleSystem = particleSystems[entityIndexInChunk];
                    var entity         = entities[entityIndexInChunk];
                    var particleCount  = particleSeedsAccessor[entityIndexInChunk].Length;

                    GetModule(chunk, entityIndexInChunk, mainModules, particleSystem.Modules.Value.mainModule, out var mainModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              velocityOverLifetimeModules,
                              particleSystem.Modules.Value.velocityOverLifetimeModule,
                              out var velocityOverLifetimeModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              limitVelocityOverLifetimeModules,
                              particleSystem.Modules.Value.limitVelocityOverLifetimeModule,
                              out var limitVelocityOverLifetimeModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              inheritVelocityModules,
                              particleSystem.Modules.Value.inheritVelocityModule,
                              out var inheritVelocityModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              forceOverLifetimeModules,
                              particleSystem.Modules.Value.forceOverLifetimeModule,
                              out var forceOverLifetimeModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              colorOverLifetimeModules,
                              particleSystem.Modules.Value.colorOverLifetimeModule,
                              out var colorOverLifetimeModule);
                    GetModule(chunk, entityIndexInChunk, colorBySpeedModules, particleSystem.Modules.Value.colorBySpeedModule,
                              out var colorBySpeedModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              sizeOverLifetimeModules,
                              particleSystem.Modules.Value.sizeOverLifetimeModule,
                              out var sizeOverLifetimeModule);
                    GetModule(chunk, entityIndexInChunk, sizeBySpeedModules, particleSystem.Modules.Value.sizeBySpeedModule, out var sizeBySpeedModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              rotationOverLifetimeModules,
                              particleSystem.Modules.Value.rotationOverLifetimeModule,
                              out var rotationOverLifetimeModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              rotationBySpeedModules,
                              particleSystem.Modules.Value.rotationBySpeedModule,
                              out var rotationBySpeedModule);
                    GetModule(chunk,
                              entityIndexInChunk,
                              externalForcesModules,
                              particleSystem.Modules.Value.externalForcesModule,
                              out var externalForcesModule);
                    GetModule(chunk, entityIndexInChunk, noiseModules, particleSystem.Modules.Value.noiseModule, out var noiseModule);

                    // Check modules for attributes
                    bool has3dRotation = mainModule.startRotation3D ||
                                         rotationOverLifetimeModule.enabled && rotationOverLifetimeModule.separateAxes ||
                                         rotationBySpeedModule.enabled && rotationBySpeedModule.separateAxes ||
                                         noiseModule.enabled && noiseModule.separateAxes;

                    bool has3dScale = mainModule.startSize3D ||
                                      sizeOverLifetimeModule.enabled && sizeOverLifetimeModule.separateAxes ||
                                      sizeBySpeedModule.enabled && sizeBySpeedModule.separateAxes ||
                                      noiseModule.enabled && noiseModule.separateAxes;

                    bool hasVelocity = mainModule.startSpeed.packedValue != 0 ||
                                       velocityOverLifetimeModule.enabled ||
                                       inheritVelocityModule.enabled ||
                                       limitVelocityOverLifetimeModule.enabled ||
                                       forceOverLifetimeModule.enabled ||
                                       externalForcesModule.enabled;

                    bool hasColor = (mainModule.startColorAlpha.mode != ParticleSystemCurveMode.Constant ||
                                     (mainModule.startColorAlpha.mode == ParticleSystemCurveMode.Constant &&
                                      mainModule.startColorAlpha.constant != 1f)) ||
                                    (mainModule.startColorRed.mode != ParticleSystemCurveMode.Constant ||
                                     (mainModule.startColorRed.mode == ParticleSystemCurveMode.Constant &&
                                      mainModule.startColorRed.constant != 1f)) ||
                                    (mainModule.startColorGreen.mode != ParticleSystemCurveMode.Constant ||
                                     (mainModule.startColorGreen.mode == ParticleSystemCurveMode.Constant &&
                                      mainModule.startColorGreen.constant != 1f)) ||
                                    (mainModule.startColorBlue.mode != ParticleSystemCurveMode.Constant ||
                                     (mainModule.startColorBlue.mode == ParticleSystemCurveMode.Constant &&
                                      mainModule.startColorBlue.constant != 1f)) ||
                                    colorOverLifetimeModule.enabled ||
                                    colorBySpeedModule.enabled;

                    if (has3dRotation)
                    {
                        AddRemoveParticleAttribute(unfilteredChunkIndex, entity, particleCount, particleRotations3dAccessor, particleRotationsAccessor, ref ecbParallelWriter);
                    }
                    else
                    {
                        AddRemoveParticleAttribute(unfilteredChunkIndex, entity, particleCount, particleRotationsAccessor, particleRotations3dAccessor, ref ecbParallelWriter);
                    }

                    if (has3dScale)
                    {
                        AddRemoveParticleAttribute(unfilteredChunkIndex, entity, particleCount, particleScales3dAccessor, particleScalesAccessor, ref ecbParallelWriter);
                    }
                    else
                    {
                        AddRemoveParticleAttribute(unfilteredChunkIndex, entity, particleCount, particleScalesAccessor, particleScales3dAccessor, ref ecbParallelWriter);
                    }

                    if (hasColor)
                    {
                        AddParticleAttribute(unfilteredChunkIndex, entity, particleCount, particleColorsAccessor, ref ecbParallelWriter);
                    }
                    else
                    {
                        RemoveParticleAttribute(unfilteredChunkIndex, entity, particleColorsAccessor, ref ecbParallelWriter);
                    }

                    if (hasVelocity)
                    {
                        AddParticleAttribute(unfilteredChunkIndex, entity, particleCount, particleVelocitiesAccessor, ref ecbParallelWriter);
                    }
                    else
                    {
                        RemoveParticleAttribute(unfilteredChunkIndex, entity, particleVelocitiesAccessor, ref ecbParallelWriter);
                    }
                }
            }

            private void GetModule<T, T2>(in ArchetypeChunk chunk, int entityIndexInChunk, in NativeArray<T2> components, in T defaultModule, out T resolvedModule)
                where T : unmanaged
                where T2 : unmanaged, IComponentData, IShurikenModuleComponent<T>
            {
                if (chunk.HasChunkComponent<T2>())
                {
                    resolvedModule = components[entityIndexInChunk].module;
                }
                else
                {
                    resolvedModule = defaultModule;
                }
            }

            private void AddRemoveParticleAttribute<T1, T2>(int sortKey,
                                                            Entity entity,
                                                            int particleCount,
                                                            in BufferAccessor<T1>                  addBufferAccessor,
                                                            in BufferAccessor<T2>                  removeBufferAccessor,
                                                            ref EntityCommandBuffer.ParallelWriter ecb) where T1 : unmanaged, IBufferElementData where T2 : unmanaged,
            IBufferElementData
            {
                AddParticleAttribute(sortKey, entity, particleCount, addBufferAccessor, ref ecb);
                RemoveParticleAttribute(sortKey, entity, removeBufferAccessor, ref ecb);
            }

            private void AddParticleAttribute<T>(int sortKey, Entity entity, int particleCount, in BufferAccessor<T> addBufferAccessor,
                                                 ref EntityCommandBuffer.ParallelWriter ecb) where T : unmanaged, IBufferElementData
            {
                if (addBufferAccessor.Length > 0)
                {
                    ecb.AddBuffer<T>(sortKey, entity);
                    for (int i = 0; i < particleCount; i++)
                    {
                        ecb.AppendToBuffer(sortKey, entity, new T());
                    }
                }
            }

            private void RemoveParticleAttribute<T>(int sortKey, Entity entity, in BufferAccessor<T> removeBufferAccessor,
                                                    ref EntityCommandBuffer.ParallelWriter ecb) where T : unmanaged, IBufferElementData
            {
                if (removeBufferAccessor.Length > 0)
                {
                    ecb.RemoveComponent<T>(sortKey, entity);
                }
            }
        }
    }
}
#endif

