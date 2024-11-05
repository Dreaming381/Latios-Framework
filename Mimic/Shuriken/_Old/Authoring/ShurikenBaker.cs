#if false
#if UNITY_EDITOR
using Latios.Authoring;
using Latios.LifeFX;
using Unity.Entities;
using UnityEngine;

namespace Latios.Mimic.Shuriken.Authoring
{
    [TemporaryBakingType]
    internal struct ShurikenSmartBakeItem : ISmartBakeItem<ParticleSystem>
    {
        private SmartBlobberHandle<ShurikenModulesBlob> m_modulesBlobHandle;

        public bool Bake(ParticleSystem authoring, IBaker baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.Dynamic);

            var particleSystemData = new ShurikenParticleSystemData
            {
                isPlaying        = authoring.isPlaying,
                isEmitting       = authoring.isEmitting,
                isStopped        = authoring.isStopped,
                isPaused         = authoring.isPaused,
                particleCount    = authoring.particleCount,
                time             = authoring.time,
                randomSeed       = authoring.randomSeed,
                previousPosition = authoring.transform.position,
            };

            baker.AddComponent(entity, particleSystemData);

            // Buffers for all particle
            baker.AddBuffer<ParticleSeed>(                entity);
            baker.AddBuffer<ParticleCenter>(              entity);
            baker.AddBuffer<ParticleRotationSpeed>(       entity);
            baker.AddBuffer<ParticleAgeFraction>(         entity);
            baker.AddBuffer<ParticleInverseStartLifetime>(entity);

            // Buffers based on module configuration
            // Rotation
            if (authoring.main.startRotation3D ||
                authoring.rotationOverLifetime is { enabled : true, separateAxes : true } ||
                authoring.rotationBySpeed is { enabled : true, separateAxes : true } ||
                authoring.noise is { enabled : true, separateAxes : true })
            {
                baker.AddBuffer<ParticleRotation3d>(entity);
            }
            else
            {
                baker.AddBuffer<ParticleRotation>(entity);
            }

            //Scale
            if (authoring.main.startSize3D ||
                authoring.sizeOverLifetime is { enabled : true, separateAxes : true } ||
                authoring.sizeBySpeed is { enabled : true, separateAxes : true } ||
                authoring.noise is { enabled : true, separateAxes : true })
            {
                baker.AddBuffer<ParticleScale3d>(entity);
            }
            else
            {
                baker.AddBuffer<ParticleScale>(entity);
            }

            // Color
            if ((authoring.main.startColor.mode != ParticleSystemGradientMode.Color ||
                 authoring.main.startColor.color != Color.white) ||
                (authoring.colorOverLifetime.enabled &&
                 authoring.colorOverLifetime.color.mode == ParticleSystemGradientMode.Color &&
                 authoring.colorOverLifetime.color.color == Color.white) ||
                (authoring.colorBySpeed.enabled &&
                 authoring.colorBySpeed.color.mode == ParticleSystemGradientMode.Color &&
                 authoring.colorBySpeed.color.color == Color.white))
            {
                baker.AddBuffer<ParticleColor>(entity);
            }

            // Velocity
            if (authoring.main.startSpeed.constant != 0 ||
                authoring.main.startSpeed.constantMin != 0 ||
                authoring.main.startSpeed.constantMax != 0 ||
                authoring.main.startSpeed.curveMin.keys.Length > 0 ||
                authoring.main.startSpeed.curveMax.keys.Length > 0 ||
                authoring.velocityOverLifetime.enabled ||
                authoring.inheritVelocity.enabled ||
                authoring.limitVelocityOverLifetime.enabled ||
                authoring.forceOverLifetime.enabled ||
                authoring.externalForces.enabled)
            {
                baker.AddBuffer<ParticleVelocity>(entity);
            }

            m_modulesBlobHandle = baker.RequestCreateBlobAsset(authoring);

            return true;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            var particleSystemData = entityManager.GetComponentData<ShurikenParticleSystemData>(entity);
            particleSystemData.Modules = m_modulesBlobHandle.Resolve(entityManager);
            entityManager.SetComponentData(entity, particleSystemData);
        }
    }

    [DisableAutoCreation]
    internal class ShurikenSmartBaker : SmartBaker<ParticleSystem, ShurikenSmartBakeItem>
    {
    }
}
#endif
#endif

