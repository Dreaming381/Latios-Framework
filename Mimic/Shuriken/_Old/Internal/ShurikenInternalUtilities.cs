#if false
using System.Runtime.CompilerServices;
using Latios.Kinemation;
using Latios.LifeFX;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Mimic.Shuriken.Internal
{
    internal static class ShurikenInternalUtilities
    {
        internal static void CreateParticleData(
            ref ParameterClip clip,
            uint particleSeed,
            in ShurikenParticleSystemData particleSystemData,
            in ShurikenMainModule mainModule,
            ref DynamicBuffer<ParticleSeed>                 seeds,
            ref DynamicBuffer<ParticleCenter>               centers,
            bool hasRotation3d,
            ref DynamicBuffer<ParticleRotation>             rotations,
            ref DynamicBuffer<ParticleRotation3d>           rotations3d,
            ref DynamicBuffer<ParticleRotationSpeed>        rotationSpeeds,
            bool hasScale3d,
            ref DynamicBuffer<ParticleScale>                scales,
            ref DynamicBuffer<ParticleScale3d>              scales3d,
            bool hasColor,
            ref DynamicBuffer<ParticleColor>                colors,
            bool hasVelocity,
            ref DynamicBuffer<ParticleVelocity>             velocities,
            ref DynamicBuffer<ParticleAgeFraction>          agePercents,
            ref DynamicBuffer<ParticleInverseStartLifetime> inverseStartLifetimes)
        {
            seeds.Add(new ParticleSeed { stableSeed = particleSeed });

            var rng = new Rng(particleSeed);

            var startLifetimeSequence       = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startLifetimeSeed);
            var startSpeedSequence          = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startSpeedSeed);
            var startSizeXSequence          = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startSizeXSeed);
            var startRotationEulerXSequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startRotationEulerXSeed);
            var startRotation               = quaternion.identity;

            var startLifetime       = mainModule.startLifetime.Evaluate(ref clip, startLifetimeSequence, particleSystemData.time) * mainModule.startLifetimeMultiplier;
            var startSpeed          = mainModule.startSpeed.Evaluate(ref clip, startSpeedSequence, particleSystemData.time) * mainModule.startSpeedMultiplier;
            var startSizeX          = mainModule.startSizeX.Evaluate(ref clip, startSizeXSequence, particleSystemData.time) * mainModule.startSizeXMultiplier;
            var startRotationEulerX =
                mainModule.startRotationEulerXRadians.Evaluate(ref clip, startRotationEulerXSequence, particleSystemData.time) * mainModule.startRotationEulerXMultiplier;

            centers.Add(new ParticleCenter {center = float3.zero});

            if (hasRotation3d)
            {
                var startRotationEulerYSequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startRotationEulerYSeed);
                var startRotationEulerZSequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startRotationEulerZSeed);

                var startRotationEulerY =
                    mainModule.startRotationEulerYRadians.Evaluate(ref clip, startRotationEulerYSequence, particleSystemData.time) * mainModule.startRotationEulerYMultiplier;
                var startRotationEulerZ =
                    mainModule.startRotationEulerZRadians.Evaluate(ref clip, startRotationEulerZSequence, particleSystemData.time) * mainModule.startRotationEulerZMultiplier;
                startRotation = quaternion.Euler(startRotationEulerX, startRotationEulerY, startRotationEulerZ);

                rotations3d.Add(new ParticleRotation3d {rotation = startRotation });
            }
            else
            {
                startRotation = quaternion.Euler(startRotationEulerX, 0f, 0f);

                rotations.Add(new ParticleRotation {rotationCCW = startRotationEulerX});
            }

            rotationSpeeds.Add(new ParticleRotationSpeed { rotationSpeedCCW = 0f });

            if (hasScale3d)
            {
                var startSizeYSequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startSizeYSeed);
                var startSizeZSequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startSizeZSeed);

                var startSizeY = mainModule.startSizeY.Evaluate(ref clip, startSizeYSequence, particleSystemData.time) * mainModule.startSizeYMultiplier;
                var startSizeZ = mainModule.startSizeZ.Evaluate(ref clip, startSizeZSequence, particleSystemData.time) * mainModule.startSizeZMultiplier;

                scales3d.Add(new ParticleScale3d { scale = new float3(startSizeX, startSizeY, startSizeZ) });
            }
            else
            {
                scales.Add(new ParticleScale { scale = startSizeX });
            }

            if (hasColor)
            {
                var startColorRedSequence   = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startColorRedSeed);
                var startColorGreenSequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startColorGreenSeed);
                var startColorBlueSequence  = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startColorBlueSeed);
                var startColorAlphaSequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startColorAlphaSeed);

                var startColorRed   = (half)mainModule.startColorRed.Evaluate(ref clip, startColorRedSequence, particleSystemData.time);
                var startColorGreen = (half)mainModule.startColorGreen.Evaluate(ref clip, startColorGreenSequence, particleSystemData.time);
                var startColorBlue  = (half)mainModule.startColorBlue.Evaluate(ref clip, startColorBlueSequence, particleSystemData.time);
                var startColorAlpha = (half)mainModule.startColorAlpha.Evaluate(ref clip, startColorAlphaSequence, particleSystemData.time);
                colors.Add(new ParticleColor { color = new half4(startColorRed, startColorGreen, startColorBlue, startColorAlpha)});
            }

            if (hasVelocity)
            {
                velocities.Add(new ParticleVelocity { velocity = math.mul(startRotation, math.forward()) * startSpeed });
            }

            agePercents.Add(new ParticleAgeFraction { fraction = 0 });
            inverseStartLifetimes.Add(new ParticleInverseStartLifetime {inverseExpectedLifetime = 1f / startLifetime});
        }

        // TODO: Adapt for 2d rotations and 3d rotations, and for 1d and 3d scales

        /// <summary>
        /// Handles particle system delay, gravity, and looping
        /// </summary>
        /// <param name="particleSystemData">The particle system whose time and delay to update</param>
        /// <param name="module">The main module to use for the update</param>
        /// <param name="gravity2d">The 2d gravity of the world</param>
        /// <param name="gravity3d">The 3d gravity of the world</param>
        /// <param name="particleVelocities">The velocity of an existing particle.  Gravity will be accumulated as a force over time</param>
        /// <param name="isDelayed">Whether the particle system is delayed</param>
        /// <param name="simulationDeltaTime">The simulation's delta time (determined by main module's time scaling)</param>
        internal static void DoMainModule(ref ParameterClip clip,
                                          ref ShurikenParticleSystemData particleSystemData,
                                          in ShurikenMainModule module,
                                          in float2 gravity2d,
                                          in float3 gravity3d,
                                          in float deltaTime,
                                          in float unscaledDeltaTime,
                                          in float previousDeltaTime,
                                          in float previousUnscaledDeltaTime,
                                          in DynamicBuffer<ParticleSeed>                 particleSeeds,
                                          in DynamicBuffer<ParticleInverseStartLifetime> particleInverseStartLifetimes,
                                          ref DynamicBuffer<ParticleAgeFraction>         particleAgePercents,
                                          bool hasRotation3d,
                                          ref DynamicBuffer<ParticleRotation>            particleRotations,
                                          ref DynamicBuffer<ParticleRotation3d>          particleRotations3d,
                                          bool hasScale3d,
                                          ref DynamicBuffer<ParticleScale>               particleScales,
                                          ref DynamicBuffer<ParticleScale3d>             particleScales3d,
                                          bool hasVelocity,
                                          ref DynamicBuffer<ParticleVelocity>            particleVelocities,
                                          bool hasColor,
                                          ref DynamicBuffer<ParticleColor>               particleColors,
                                          out bool isDelayed,
                                          out float simulationDeltaTime,
                                          out float previousSimulationDeltaTime)
        {
            isDelayed                   = false;
            simulationDeltaTime         = module.simulationSpeed * (module.useUnscaledTime ? unscaledDeltaTime : deltaTime);
            previousSimulationDeltaTime = module.simulationSpeed * (module.useUnscaledTime ? previousUnscaledDeltaTime : previousDeltaTime);

            var rng                = new Rng(particleSystemData.randomSeed);
            var startDelaySequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startDelaySeed);
            var startDelay         = module.startDelay.Evaluate(ref clip, startDelaySequence, 0) * module.startDelayMultiplier;
            if (startDelay >= particleSystemData.delayTime)
            {
                particleSystemData.delayTime += simulationDeltaTime;
                isDelayed                     = true;
            }
            else if (particleSystemData.isPlaying)
            {
                if (particleSystemData.time >= module.duration)
                {
                    if (module.loop)
                    {
                        particleSystemData.time -= module.duration;
                    }
                    else
                    {
                        particleSystemData.isPlaying = false;
                        particleSystemData.isStopped = true;
                    }
                }
            }

            if (particleSystemData.isPlaying)
            {
                var gravityModifierSequence = rng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.gravityModifierSeed);
                var gravityModifier         = module.gravityModifier.Evaluate(ref clip, gravityModifierSequence, particleSystemData.time) * module.gravityModifierMultiplier;
                var gravity                 =
                    (module.gravitySource == ParticleSystemGravitySource.Physics3D ? gravity3d : new float3(gravity2d.x, gravity2d.y, 0f)) * gravityModifier;

                //Age particles
                //Reset rotation, color, scale, velocity
                //Apply gravity
                for (int i = 0; i < particleSystemData.particleCount; i++)
                {
                    var particleSeed = particleSeeds[i].stableSeed;
                    var particleRng  = new Rng(particleSeed);

                    var agePercent = particleAgePercents[i];
                    agePercent.fraction   += (ushort)(simulationDeltaTime / particleInverseStartLifetimes[i].inverseExpectedLifetime);
                    particleAgePercents[i] = agePercent;

                    var startSpeedSequence          = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startSpeedSeed);
                    var startSizeXSequence          = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startSizeXSeed);
                    var startSizeYSequence          = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startSizeYSeed);
                    var startSizeZSequence          = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startSizeZSeed);
                    var startRotationEulerXSequence = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startRotationEulerXSeed);
                    var startRotationEulerYSequence = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startRotationEulerYSeed);
                    var startRotationEulerZSequence = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startRotationEulerZSeed);
                    var startColorRedSequence       = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startColorRedSeed);
                    var startColorGreenSequence     = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startColorGreenSeed);
                    var startColorBlueSequence      = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startColorBlueSeed);
                    var startColorAlphaSequence     = particleRng.GetSequence(ShurikenMainModule.moduleSeed + ShurikenMainModule.startColorAlphaSeed);

                    var particleLifeTime = GetParticleLifetime(particleAgePercents[i].fraction, particleInverseStartLifetimes[i].inverseExpectedLifetime);

                    var startSpeed     = module.startSpeed.Evaluate(ref clip, startSpeedSequence, particleSystemData.time - particleLifeTime) * module.startSpeedMultiplier;
                    var sizeX          = module.startSizeX.Evaluate(ref clip, startSizeXSequence, particleSystemData.time - particleLifeTime) * module.startSizeXMultiplier;
                    var sizeY          = module.startSizeY.Evaluate(ref clip, startSizeYSequence, particleSystemData.time - particleLifeTime) * module.startSizeYMultiplier;
                    var sizeZ          = module.startSizeZ.Evaluate(ref clip, startSizeZSequence, particleSystemData.time - particleLifeTime) * module.startSizeZMultiplier;
                    var rotationEulerX =
                        module.startRotationEulerXRadians.Evaluate(ref clip, startRotationEulerXSequence,
                                                                   particleSystemData.time - particleLifeTime) * module.startRotationEulerXMultiplier;
                    var rotationEulerY =
                        module.startRotationEulerYRadians.Evaluate(ref clip, startRotationEulerYSequence,
                                                                   particleSystemData.time - particleLifeTime) * module.startRotationEulerYMultiplier;
                    var rotationEulerZ =
                        module.startRotationEulerZRadians.Evaluate(ref clip, startRotationEulerZSequence,
                                                                   particleSystemData.time - particleLifeTime) * module.startRotationEulerZMultiplier;
                    var rotation   = quaternion.Euler(rotationEulerX, rotationEulerY, rotationEulerZ);
                    var colorRed   = (half)module.startColorRed.Evaluate(ref clip, startColorRedSequence, particleSystemData.time - particleLifeTime);
                    var colorGreen = (half)module.startColorGreen.Evaluate(ref clip, startColorGreenSequence, particleSystemData.time - particleLifeTime);
                    var colorBlue  = (half)module.startColorBlue.Evaluate(ref clip, startColorBlueSequence, particleSystemData.time - particleLifeTime);
                    var colorAlpha = (half)module.startColorAlpha.Evaluate(ref clip, startColorAlphaSequence, particleSystemData.time - particleLifeTime);

                    if (hasRotation3d)
                    {
                        var particleRotation3d = particleRotations3d[i];
                        particleRotation3d.rotation = rotation;
                        particleRotations3d[i]      = particleRotation3d;
                    }
                    else
                    {
                        var particleRotation = particleRotations[i];
                        particleRotation.rotationCCW = rotationEulerX;
                        particleRotations[i]         = particleRotation;
                    }

                    if (hasScale3d)
                    {
                        var particleScale3d = particleScales3d[i];
                        particleScale3d.scale = new float3(sizeX, sizeY, sizeZ);
                        particleScales3d[i]   = particleScale3d;
                    }
                    else
                    {
                        var particleScale = particleScales[i];
                        particleScale.scale = sizeX;
                        particleScales[i]   = particleScale;
                    }

                    if (hasColor)
                    {
                        var particleColor = particleColors[i];
                        particleColor.color = new half4(colorRed, colorGreen, colorBlue, colorAlpha);
                        particleColors[i]   = particleColor;
                    }

                    if (hasVelocity)
                    {
                        var particleVelocity = particleVelocities[i];
                        particleVelocity.velocity = math.mul(rotation, math.forward()) * startSpeed + gravity * particleLifeTime;
                        particleVelocities[i]     = particleVelocity;
                    }
                }
            }
        }

        /// <summary>
        /// Determines how many particles to emit this frame
        /// </summary>
        /// <param name="particleSystemData">The particle system whose time to use for the update</param>
        /// <param name="module">The emission module to use for the update</param>
        /// <param name="bursts">The bursts configuration for the emission module</param>
        /// <param name="simulationDeltaTime">The simulation's delta time (determined by main module's time scaling)</param>
        /// <param name="distanceTraveledDelta">The delta distance traveled by the particle system</param>
        /// <param name="newParticleCount">The number of particles to be emitted</param>
        internal static void DoEmissionModule(
            ref ParameterClip clip,
            in ShurikenParticleSystemData particleSystemData,
            in ShurikenEmissionModule module,
            in NativeArray<EmissionBurst> bursts,
            float simulationDeltaTime,
            float distanceTraveledDelta,
            out int newParticleCount)
        {
            newParticleCount = 0;
            if (module.enabled)
            {
                var rng = new Rng(particleSystemData.randomSeed);

                //Rate over time
                var rateOverTimeSequence = rng.GetSequence(ShurikenEmissionModule.moduleSeed + ShurikenEmissionModule.rateOverTimeSeed);
                var rateOverTime         = module.rateOverTime.Evaluate(ref clip, rateOverTimeSequence, particleSystemData.time) * module.rateOverTimeMultiplier;

                var rateOverTimeInterval = math.rcp(rateOverTime);

                if (particleSystemData.time % rateOverTimeInterval + simulationDeltaTime >= rateOverTimeInterval)
                {
                    newParticleCount++;
                }

                //Rate over distance
                var rateOverDistanceSequence = rng.GetSequence(ShurikenEmissionModule.moduleSeed + ShurikenEmissionModule.rateOverDistanceSeed);
                var rateOverDistance         = module.rateOverDistance.Evaluate(ref clip, rateOverDistanceSequence, particleSystemData.time) * module.rateOverDistanceMultiplier;

                var rateOverDistanceInterval = math.rcp(rateOverDistance);

                if (particleSystemData.distanceTraveled % rateOverDistanceInterval + distanceTraveledDelta >= rateOverDistanceInterval)
                {
                    newParticleCount++;
                }

                //Bursts
                var burstsSequence = rng.GetSequence(ShurikenEmissionModule.moduleSeed + ShurikenEmissionModule.burstsSeed);
                for (int i = 0; i < bursts.Length; i++)
                {
                    var burst           = bursts[i];
                    var burstActiveTime = particleSystemData.time - burst.time;
                    var totalCycles     = burstActiveTime / burst.repeatInterval;

                    if (totalCycles > burst.cycleCount)
                    {
                        continue;
                    }

                    var burstElementSequence = rng.GetSequence(ShurikenEmissionModule.moduleSeed + ShurikenEmissionModule.burstsSeed + i);

                    // Check if it's time for this burst
                    if (burstActiveTime % rateOverTimeInterval + simulationDeltaTime >= burst.repeatInterval)
                    {
                        // Determine the probability of this burst
                        if (burstsSequence.NextFloat(0, 1) < burst.probability)
                        {
                            // Calculate the number of particles to emit
                            int burstParticleCount = (int)burst.count.Evaluate(ref clip, burstElementSequence, particleSystemData.time);
                            newParticleCount += burstParticleCount;
                        }
                    }
                }
            }
        }

        //TODO:  The rest of the shapes
        //TODO:  Increase efficiency by determining non-particle-specific values outside of loop
        //TODO:  Reset rotations of every particle to initial rotation
        /// <summary>
        /// Determines the start positions, rotations, and velocities of newly spawned particles
        /// </summary>
        /// <param name="particleSystemData">The particle system whose time to use for the update</param>
        /// <param name="module">The shape module to use for the update</param>
        /// <param name="newParticleCount">The number of particles to be shaped</param>
        /// <param name="particleAgePercents">The age percents of created particles</param>
        /// <param name="particleInverseStartLifetimes">The inverse of the particles' start lifetimes</param>
        /// <param name="particleCenters">The position of the particles</param>
        /// <param name="particleRotations">The rotation of the particles</param>
        /// <param name="particleVelocities">The velocities of the particles, modified when rotations are affected</param>
        public static void DoShapeModule(
            ref ParameterClip clip,
            in ShurikenParticleSystemData particleSystemData,
            in ShurikenShapeModule module,
            int newParticleCount,
            in DynamicBuffer<ParticleSeed>                 particleSeeds,
            in DynamicBuffer<ParticleAgeFraction>          particleAgePercents,
            in DynamicBuffer<ParticleInverseStartLifetime> particleInverseStartLifetimes,
            ref DynamicBuffer<ParticleCenter>              particleCenters,
            bool hasRotation3d,
            ref DynamicBuffer<ParticleRotation>            particleRotations,
            ref DynamicBuffer<ParticleRotation3d>          particleRotations3d,
            bool hasVelocity,
            ref DynamicBuffer<ParticleVelocity>            particleVelocities)
        {
            if (module.enabled)
            {
                float3 shapePosition = float3.zero;
                quaternion shapeRotation = quaternion.identity;

                //TODO:  Change the rotations
                //Change spawned particle shapes and rotations according to shape
                var canExaminePrevious = newParticleCount < particleSeeds.Length;
                for (int i = particleSeeds.Length - newParticleCount; i < particleSeeds.Length; i++)
                {
                    var particleSeed        = particleSeeds[i].stableSeed;
                    var particleRng         = new Rng(particleSeed);
                    var shapeModuleSequence = particleRng.GetSequence(ShurikenShapeModule.moduleSeed);
                    var particlePosition    = particleCenters[i].center;

                    switch (module.shapeType)
                    {
                        case ParticleSystemShapeType.Box:
                        {
                            var min = -module.scale / 2f;
                            var max = module.scale / 2f;
                            shapePosition    = shapeModuleSequence.NextFloat3(min, max) + shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);
                            shapeRotation    = quaternion.Euler(module.rotationEulerRadians);
                            shapePosition    = math.mul(quaternion.Euler(module.rotationEulerRadians), shapePosition);
                            particlePosition = shapePosition + module.position;

                            break;
                        }
                        case ParticleSystemShapeType.Circle:
                        {
                            float timeFactor = 0;
                            if (canExaminePrevious)
                            {
                                var particleAgePercent           = particleAgePercents[i - 1];
                                var particleInverseStartLifetime = particleInverseStartLifetimes[i - 1];
                                timeFactor = particleAgePercent.fraction * particleInverseStartLifetime.inverseExpectedLifetime;
                            }

                            float arcPosition =
                                CalculateArcPosition(module.arcRadians,
                                                     module.arcMode,
                                                     module.arcSpreadRadians,
                                                     module.arcSpeed.Evaluate(ref clip, shapeModuleSequence, timeFactor) * module.arcSpeedMultiplier,
                                                     timeFactor);

                            // Calculate position within the circle considering Radius Thickness
                            float distanceFromCenter = module.radius * math.sqrt(shapeModuleSequence.NextFloat(0, module.radiusThickness));
                            float angle              = arcPosition;
                            shapePosition = new float3(distanceFromCenter * math.cos(angle), distanceFromCenter * math.sin(angle), 0);
                            shapeRotation = quaternion.Euler(module.rotationEulerRadians);

                            // Apply Randomize Position
                            shapePosition += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);

                            shapePosition    = math.mul(quaternion.EulerXYZ(module.rotationEulerRadians), shapePosition);
                            particlePosition = shapePosition + module.position;

                            break;
                        }

                        case ParticleSystemShapeType.Cone:
                        {
                            float timeFactor = 0;
                            if (canExaminePrevious)
                            {
                                var particleAgePercent           = particleAgePercents[i - 1];
                                var particleInverseStartLifetime = particleInverseStartLifetimes[i - 1];
                                timeFactor = particleAgePercent.fraction * particleInverseStartLifetime.inverseExpectedLifetime;
                            }

                            float arcPosition =
                                CalculateArcPosition(module.arcRadians,
                                                     module.arcMode,
                                                     module.arcSpreadRadians,
                                                     module.arcSpeed.Evaluate(ref clip, shapeModuleSequence, timeFactor) * module.arcSpeedMultiplier,
                                                     timeFactor);

                            // Calculate base position and direction for Cone
                            float angle              = arcPosition;
                            float distanceFromCenter = module.radius * math.sqrt(shapeModuleSequence.NextFloat(0, 1));  // Uniform distribution within a circle

                            // Apply Radius Thickness
                            if (module.radiusThickness < 1f)
                            {
                                // Emission closer to the edge based on radius thickness
                                float minDistance = module.radius * module.radiusThickness;
                                distanceFromCenter = math.lerp(minDistance, module.radius, distanceFromCenter);
                            }

                            shapePosition = new float3(distanceFromCenter * math.cos(angle), distanceFromCenter * math.sin(angle), 0);

                            // Apply Randomize Position
                            shapePosition += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);
                            shapeRotation  = quaternion.Euler(module.rotationEulerRadians);

                            particlePosition = shapePosition + module.position;
                            break;
                        }
                        case ParticleSystemShapeType.Donut:
                        {
                            float timeFactor = 0;
                            if (canExaminePrevious)
                            {
                                var particleAgePercent           = particleAgePercents[i - 1];
                                var particleInverseStartLifetime = particleInverseStartLifetimes[i - 1];
                                timeFactor = particleAgePercent.fraction * particleInverseStartLifetime.inverseExpectedLifetime;
                            }

                            float arcPosition =
                                CalculateArcPosition(module.arcRadians,
                                                     module.arcMode,
                                                     module.arcSpreadRadians,
                                                     module.arcSpeed.Evaluate(ref clip, shapeModuleSequence, timeFactor) * module.arcSpeedMultiplier,
                                                     timeFactor);

                            // Main ring radius and outer ring thickness
                            float R = module.radius;
                            float r = module.donutRadius * module.radiusThickness;

                            // Calculate position on the donut using arcPosition
                            float angle1 = arcPosition;
                            float angle2 = shapeModuleSequence.NextFloat(0, 2 * math.PI);
                            shapePosition = new float3((R + r * math.cos(angle2)) * math.cos(angle1), (R + r * math.cos(angle2)) * math.sin(angle1), r * math.sin(angle2));

                            // Apply Randomize Position
                            shapePosition += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);

                            shapeRotation = quaternion.Euler(module.rotationEulerRadians);
                            float3 donutPositionRotated = math.mul(shapeRotation, shapePosition);

                            particlePosition = donutPositionRotated + module.position;

                            break;
                        }
                        case ParticleSystemShapeType.Hemisphere:
                        {
                            float timeFactor = 0;
                            if (canExaminePrevious)
                            {
                                var particleAgePercent           = particleAgePercents[i - 1];
                                var particleInverseStartLifetime = particleInverseStartLifetimes[i - 1];
                                timeFactor = particleAgePercent.fraction * particleInverseStartLifetime.inverseExpectedLifetime;
                            }

                            // Calculate the arc position
                            float arcPosition =
                                CalculateArcPosition(module.arcRadians,
                                                     module.arcMode,
                                                     module.arcSpreadRadians,
                                                     module.arcSpeed.Evaluate(ref clip, shapeModuleSequence, timeFactor),
                                                     timeFactor);

                            // Convert arcPosition (angle around the hemisphere) to spherical coordinates
                            // For hemisphere, limit the vertical angle φ to the range [0, π/2] (upper hemisphere)
                            float theta = arcPosition;  // Horizontal angle
                            float phi   = shapeModuleSequence.NextFloat(0, math.PI);  // Vertical angle - randomized for full sphere coverage

                            // Convert spherical coordinates to Cartesian coordinates for 3D position
                            float x = module.radius * math.sin(phi) * math.cos(theta);
                            float y = module.radius * math.sin(phi) * math.sin(theta);
                            float z = module.radius * math.cos(phi);

                            shapePosition = new float3(x, y, z);

                            // Apply Randomize Position if necessary
                            shapePosition += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);
                            shapeRotation  = quaternion.Euler(module.rotationEulerRadians);

                            particlePosition = shapePosition + module.position;
                            break;
                        }
                        // case ParticleSystemShapeType.Mesh:
                        // {
                        //     break;
                        // }
                        case ParticleSystemShapeType.Rectangle:
                        {
                            // Rectangle dimensions based on module.scale
                            float width  = module.scale.x;
                            float height = module.scale.y;

                            // Random position within the rectangle
                            float x = shapeModuleSequence.NextFloat(-width / 2, width / 2);
                            float y = shapeModuleSequence.NextFloat(-height / 2, height / 2);

                            shapePosition  = new float3(x, y, 0);
                            shapePosition += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);  // Randomize Position

                            shapeRotation = quaternion.Euler(module.rotationEulerRadians);
                            shapePosition = math.mul(shapeRotation, shapePosition);

                            particlePosition = shapePosition + module.position;

                            break;
                        }
                        case ParticleSystemShapeType.Sphere:
                        {
                            float timeFactor = 0;
                            if (canExaminePrevious)
                            {
                                var particleAgePercent           = particleAgePercents[i - 1];
                                var particleInverseStartLifetime = particleInverseStartLifetimes[i - 1];
                                timeFactor = particleAgePercent.fraction * particleInverseStartLifetime.inverseExpectedLifetime;
                            }

                            // Calculate the arc position
                            float arcPosition =
                                CalculateArcPosition(module.arcRadians,
                                                     module.arcMode,
                                                     module.arcSpreadRadians,
                                                     module.arcSpeed.Evaluate(ref clip, shapeModuleSequence, timeFactor),
                                                     timeFactor);

                            // Convert arcPosition (angle around the sphere) to spherical coordinates
                            float theta = arcPosition;  // Horizontal angle
                            float phi   = shapeModuleSequence.NextFloat(0, math.PI);  // Vertical angle - randomized for full sphere coverage

                            // Convert spherical coordinates to Cartesian coordinates for 3D position
                            float x = module.radius * math.sin(phi) * math.cos(theta);
                            float y = module.radius * math.sin(phi) * math.sin(theta);
                            float z = module.radius * math.cos(phi);

                            shapePosition = new float3(x, y, z);

                            // Apply Randomize Position if necessary
                            shapePosition += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);
                            shapeRotation  = quaternion.Euler(module.rotationEulerRadians);
                            shapePosition  = math.mul(shapeRotation, shapePosition);

                            particlePosition = shapePosition + module.position;

                            break;
                        }
                        case ParticleSystemShapeType.BoxEdge:
                        {
                            shapePosition = GetPositionOnBox(ref shapeModuleSequence, module.scale, false, default);

                            shapePosition   += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);  // Randomize Position
                            shapeRotation    = quaternion.EulerXYZ(module.rotationEulerRadians);
                            shapePosition    = math.mul(shapeRotation, shapePosition);
                            particlePosition = shapePosition + module.position;

                            break;
                        }
                        case ParticleSystemShapeType.BoxShell:
                        {
                            // Calculate the thickness of the shell
                            var shellThickness = module.scale * module.boxThickness;

                            // Randomize position on the shell surface
                            shapePosition = GetPositionOnBox(ref shapeModuleSequence, module.scale, true, shellThickness);

                            // Apply Position and Rotation
                            shapeRotation    = quaternion.EulerXYZ(module.rotationEulerRadians);
                            shapePosition    = math.mul(shapeRotation, shapePosition);
                            particlePosition = shapePosition + module.position;

                            break;
                        }

                        case ParticleSystemShapeType.ConeVolume:
                        {
                            float timeFactor = 0;
                            if (canExaminePrevious)
                            {
                                var particleAgePercent           = particleAgePercents[i - 1];
                                var particleInverseStartLifetime = particleInverseStartLifetimes[i - 1];
                                timeFactor = particleAgePercent.fraction * particleInverseStartLifetime.inverseExpectedLifetime;
                            }

                            // Calculate the arc position based on the Cone settings
                            float arcPosition =
                                CalculateArcPosition(module.arcRadians,
                                                     module.arcMode,
                                                     module.arcSpreadRadians,
                                                     module.arcSpeed.Evaluate(ref clip, shapeModuleSequence, timeFactor) * module.arcSpeedMultiplier,
                                                     timeFactor);

                            // Calculate base position within the cone volume
                            float height             = shapeModuleSequence.NextFloat(0, module.length);
                            float radiusAtHeight     = height * math.tan(module.angleRadians);
                            float angle              = arcPosition;
                            float distanceFromCenter = shapeModuleSequence.NextFloat(0, radiusAtHeight);

                            // Apply Radius Thickness
                            if (module.radiusThickness < 1f)
                            {
                                float minDistance = radiusAtHeight * module.radiusThickness;
                                distanceFromCenter = math.lerp(minDistance, radiusAtHeight, distanceFromCenter);
                            }

                            shapePosition = new float3(distanceFromCenter * math.cos(angle), distanceFromCenter * math.sin(angle), height - module.length / 2);

                            // Apply Randomize Position
                            shapePosition   += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);
                            shapeRotation    = quaternion.Euler(module.rotationEulerRadians);
                            shapePosition    = math.mul(shapeRotation, shapePosition);
                            particlePosition = shapePosition + module.position;

                            break;
                        }
                        // case ParticleSystemShapeType.MeshRenderer:
                        // {
                        //     break;
                        // }
                        // case ParticleSystemShapeType.SpriteRenderer:
                        // {
                        //     break;
                        // }
                        case ParticleSystemShapeType.SingleSidedEdge:
                        {
                            float linePosition = shapeModuleSequence.NextFloat(-module.length / 2, module.length / 2);
                            shapePosition = new float3(linePosition, 0, 0);  // Assuming edge along x-axis
                            // Apply Randomize Position
                            shapePosition   += shapeModuleSequence.NextFloat3(-module.randomPositionAmount, module.randomPositionAmount);
                            shapeRotation    = quaternion.Euler(module.rotationEulerRadians);
                            shapePosition    = math.mul(shapeRotation, shapePosition);
                            particlePosition = shapePosition + module.position;

                            break;
                        }
                            // case ParticleSystemShapeType.SkinnedMeshRenderer:
                            // {
                            //     break;
                            // }
                    }

                    // Set rotation
                    quaternion particleRotation = quaternion.identity;
                    particleRotation = ApplySphericalAndRandomRotations(ref shapeModuleSequence, module, shapeRotation, shapePosition);
                    if (hasRotation3d)
                    {
                        var particleRotation3d = particleRotations3d[i];
                        particleRotation3d.rotation = particleRotation;
                        particleRotations3d[i]      = particleRotation3d;
                    }
                    else
                    {
                        var particleRotation2d = particleRotations[i];
                        particleRotation2d.rotationCCW = GetEulerX(particleRotation);
                        particleRotations[i]           = particleRotation2d;
                    }

                    if (hasVelocity)
                    {
                        // Reapply new rotation to velocity
                        var particleVelocity = particleVelocities[i];
                        particleVelocity.velocity = math.mul(particleRotation, math.forward() * math.length(particleVelocity.velocity));
                        particleVelocities[i]     = particleVelocity;
                    }

                    particleCenters[i] = new ParticleCenter { center = particlePosition };
                }
            }
        }

        #region ShapeModuleHelpers

        internal static float3 GetPositionOnBox(ref Rng.RngSequence shapeModuleSequence, float3 scale, bool isShell, float3 shellThickness = default)
        {
            float3 position = float3.zero;
            int component;

            if (isShell)
            {
                // Randomly select one of the six faces of the box for BoxShell
                int face = shapeModuleSequence.NextInt(1, 7);
                component = (face - 1) % 3;  // Determines which component (x, y, or z) is constant

                position[component] = (face <= 3 ? 1 : -1) * scale[component] / 2;
                for (int i = 0; i < 3; i++)
                {
                    if (i != component)
                    {
                        position[i] = shapeModuleSequence.NextFloat(-scale[i] / 2, scale[i] / 2);
                    }
                }
            }
            else
            {
                // BoxEdge logic
                int edge = shapeModuleSequence.NextInt(1, 13);
                component = (edge - 1) / 4;  // Determines the axis along which the edge lies

                // Position along the selected edge
                position[component]           = shapeModuleSequence.NextFloat(-scale[component] / 2, scale[component] / 2);
                position[(component + 1) % 3] = (edge % 4 <= 1 ? 1 : -1) * scale[(component + 1) % 3] / 2;
                position[(component + 2) % 3] = ((edge / 4) % 2 == 0 ? 1 : -1) * scale[(component + 2) % 3] / 2;
            }

            return position;
        }

        internal static quaternion ApplySphericalAndRandomRotations(ref Rng.RngSequence sequence,
                                                                    ShurikenShapeModule module,
                                                                    quaternion baseRotation,
                                                                    float3 shapePositionBeforeRotation)
        {
            if (module.alignToDirection)
            {
                var clampedSpherizeWeight  = math.clamp(module.sphericalDirectionAmount, 0f, 1f);
                var clampedRandomizeWeight = math.clamp(module.randomDirectionAmount, 0f, 1f);
                var totalWeight            = clampedSpherizeWeight + clampedRandomizeWeight;

                // Apply Spherize Direction
                var spherizedRotation = quaternion.LookRotation(math.normalize(shapePositionBeforeRotation), math.up());
                baseRotation = math.slerp(baseRotation, spherizedRotation, clampedSpherizeWeight / totalWeight);

                // Apply Randomize Direction
                var randomizedRotation = quaternion.LookRotation(math.normalize(sequence.NextFloat3(new float3(-1f, -1f, -1f), new float3(1f, 1f, 1f))), math.up());
                baseRotation = math.slerp(baseRotation, randomizedRotation, clampedRandomizeWeight / totalWeight);

                return baseRotation;
            }

            return quaternion.identity;
        }

        internal static float CalculateArcPosition(float arc, ParticleSystemShapeMultiModeValue arcMode, float arcSpread, float arcSpeed, float timeFactor)
        {
            float arcPosition = 0f;

            switch (arcMode)
            {
                case ParticleSystemShapeMultiModeValue.Random:
                    arcPosition = UnityEngine.Random.Range(0, arc);
                    break;
                case ParticleSystemShapeMultiModeValue.Loop:
                    arcPosition = (timeFactor * arcSpeed) % arc;
                    break;
                case ParticleSystemShapeMultiModeValue.PingPong:
                    float modulus = (timeFactor * arcSpeed) % (arc * 2);
                    arcPosition = arc - math.abs(modulus - arc);
                    break;
                case ParticleSystemShapeMultiModeValue.BurstSpread:
                    float segments       = math.ceil(1f / arcSpread);
                    float currentSegment = math.floor(timeFactor * segments) % segments;
                    arcPosition = (currentSegment / segments) * arc;
                    break;
            }

            return arcPosition;
        }

        #endregion

        /// <summary>
        /// Sets the velocity of each particle based on the velocity over lifetime module and the particle's lifetime
        /// </summary>
        /// <param name="particleSystemData"></param>
        /// <param name="module"></param>
        /// <param name="emitterRotation"></param>
        /// <param name="particleSeeds"></param>
        /// <param name="particleCenters"></param>
        /// <param name="particleRotations"></param>
        /// <param name="particleVelocities"></param>
        internal static void DoVelocityOverLifetimeModule(
            ref ParameterClip clip,
            in ShurikenParticleSystemData particleSystemData,
            in ShurikenVelocityOverLifetimeModule module,
            in quaternion emitterRotation,
            in DynamicBuffer<ParticleSeed>        particleSeeds,
            in DynamicBuffer<ParticleCenter>      particleCenters,
            ref DynamicBuffer<ParticleVelocity>   particleVelocities)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleVelocities.Length; i++)
            {
                var rng = new Rng(particleSeeds[i].stableSeed);

                var velocity         = particleVelocities[i];
                var additiveVelocity = float3.zero;
                float3 particlePosition = particleCenters[i].center;

                // Apply linear velocities
                var velocitySequence = rng.GetSequence(ShurikenVelocityOverLifetimeModule.moduleSeed + ShurikenVelocityOverLifetimeModule.velocitySeed);
                additiveVelocity.x += module.x.Evaluate(ref clip, velocitySequence, particleSystemData.time) * module.xMultiplier;
                additiveVelocity.y += module.y.Evaluate(ref clip, velocitySequence, particleSystemData.time) * module.yMultiplier;
                additiveVelocity.z += module.z.Evaluate(ref clip, velocitySequence, particleSystemData.time) * module.zMultiplier;

                // Orbital velocities
                var orbitalSequence = rng.GetSequence(ShurikenVelocityOverLifetimeModule.moduleSeed + ShurikenVelocityOverLifetimeModule.orbitalSeed);
                float3 orbitalVelocity = float3.zero;
                orbitalVelocity.x = module.orbitalX.Evaluate(ref clip, orbitalSequence, particleSystemData.time) * module.orbitalXMultiplier;
                orbitalVelocity.y = module.orbitalY.Evaluate(ref clip, orbitalSequence, particleSystemData.time) * module.orbitalYMultiplier;
                orbitalVelocity.z = module.orbitalZ.Evaluate(ref clip, orbitalSequence, particleSystemData.time) * module.orbitalZMultiplier;

                // Apply rotations
                quaternion rotationX = quaternion.AxisAngle(new float3(1, 0, 0), orbitalVelocity.x);
                quaternion rotationY = quaternion.AxisAngle(new float3(0, 1, 0), orbitalVelocity.y);
                quaternion rotationZ = quaternion.AxisAngle(new float3(0, 0, 1), orbitalVelocity.z);

                // Combine rotations
                quaternion combinedRotation = math.mul(math.mul(rotationX, rotationY), rotationZ);

                // Orbital offsets
                var orbitalOffsetSequence = rng.GetSequence(ShurikenVelocityOverLifetimeModule.moduleSeed + ShurikenVelocityOverLifetimeModule.orbitalOffsetSeed);
                float3 orbitalOffset         = float3.zero;
                orbitalOffset.x = module.orbitalOffsetX.Evaluate(ref clip, orbitalOffsetSequence, particleSystemData.time) * module.orbitalOffsetXMultiplier;
                orbitalOffset.y = module.orbitalOffsetY.Evaluate(ref clip, orbitalOffsetSequence, particleSystemData.time) * module.orbitalOffsetYMultiplier;
                orbitalOffset.z = module.orbitalOffsetZ.Evaluate(ref clip, orbitalOffsetSequence, particleSystemData.time) * module.orbitalOffsetZMultiplier;

                float3 orbitPosition = math.mul(combinedRotation, particlePosition + orbitalOffset);
                float3 deltaPosition = orbitPosition - particleCenters[i].center;

                //Apply orbit velocity
                additiveVelocity += deltaPosition;

                // Radial velocity
                if (module.radialMultiplier != 0)
                {
                    var radialSequence = rng.GetSequence(ShurikenVelocityOverLifetimeModule.moduleSeed + ShurikenVelocityOverLifetimeModule.radialSeed);
                    float radialValue    = module.radial.Evaluate(ref clip, radialSequence, particleSystemData.time);

                    float3 radialDirection = math.normalize(particlePosition);
                    float3 radialVelocity  = radialDirection * radialValue * module.radialMultiplier;

                    additiveVelocity += radialVelocity;
                }

                // Speed Modifier
                var speedSequence = rng.GetSequence(ShurikenVelocityOverLifetimeModule.moduleSeed + ShurikenVelocityOverLifetimeModule.speedModifierSeed);
                var speedModifier = module.speedModifier.Evaluate(ref clip, speedSequence, particleSystemData.time) * module.speedModifierMultiplier;
                additiveVelocity *= speedModifier;

                if (module.space == ParticleSystemSimulationSpace.World)
                {
                    // Apply emitter rotation
                    additiveVelocity = math.mul(emitterRotation, additiveVelocity);
                }

                // Apply the updated velocity back to the DynamicBuffer
                velocity.velocity    += additiveVelocity;
                particleVelocities[i] = velocity;
            }
        }

        internal static void DoLimitVelocityOverLifetimeModule(
            ref ParameterClip clip,
            in ShurikenParticleSystemData particleSystemData,
            in ShurikenLimitVelocityOverLifetimeModule module,
            in quaternion emitterRotation,
            in DynamicBuffer<ParticleSeed>             particleSeeds,
            in DynamicBuffer<ParticleAgeFraction>      particleAgePercents,
            in DynamicBuffer<ParticleScale3d>          particleScales,
            ref DynamicBuffer<ParticleVelocity>        particleVelocities)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleVelocities.Length; i++)
            {
                var rng = new Rng(particleSeeds[i].stableSeed);

                var velocity   = particleVelocities[i].velocity;
                var scale      = particleScales[i].scale;
                float limitValue = 0;
                float3 dragValue;

                // Limit velocity
                if (module.separateAxes)
                {
                    if (module.space == ParticleSystemSimulationSpace.World)
                    {
                        velocity = math.rotate(emitterRotation, velocity);
                    }

                    // Separate limit for each axis
                    limitValue =
                        module.limitX.Evaluate(ref clip,
                                               rng.GetSequence(ShurikenLimitVelocityOverLifetimeModule.moduleSeed + ShurikenLimitVelocityOverLifetimeModule.limitSeed),
                                               particleSystemData.time) * module.limitXMultiplier;
                    velocity.x = math.min(velocity.x, limitValue);
                    limitValue =
                        module.limitY.Evaluate(ref clip,
                                               rng.GetSequence(ShurikenLimitVelocityOverLifetimeModule.moduleSeed + ShurikenLimitVelocityOverLifetimeModule.limitSeed),
                                               particleSystemData.time) * module.limitYMultiplier;
                    velocity.y = math.min(velocity.y, limitValue);
                    limitValue =
                        module.limitZ.Evaluate(ref clip,
                                               rng.GetSequence(ShurikenLimitVelocityOverLifetimeModule.moduleSeed + ShurikenLimitVelocityOverLifetimeModule.limitSeed),
                                               particleSystemData.time) * module.limitZMultiplier;
                    velocity.z = math.min(velocity.z, limitValue);

                    if (module.space == ParticleSystemSimulationSpace.World)
                    {
                        velocity = math.rotate(math.inverse(emitterRotation), velocity);
                    }
                }
                else
                {
                    // Single limit for the magnitude of the velocity vector
                    limitValue =
                        module.limit.Evaluate(ref clip,
                                              rng.GetSequence(ShurikenLimitVelocityOverLifetimeModule.moduleSeed + ShurikenLimitVelocityOverLifetimeModule.limitSeed),
                                              particleSystemData.time) * module.limitMultiplier;

                    if (math.length(velocity) > limitValue)
                    {
                        velocity = math.normalize(velocity) * limitValue;
                    }
                }

                // Apply dampen (scaled with age percent)
                float dampenFactor = 1 - module.dampen * particleAgePercents[i].fraction;
                velocity *= dampenFactor;

                // Apply drag
                if (module.dragMultiplier != 0)
                {
                    dragValue = module.drag.Evaluate(ref clip,
                                                     rng.GetSequence(ShurikenLimitVelocityOverLifetimeModule.moduleSeed + ShurikenLimitVelocityOverLifetimeModule.dragSeed),
                                                     particleSystemData.time) * module.dragMultiplier;

                    // Conditional drag calculations
                    if (module.multiplyDragByParticleSize)
                    {
                        dragValue *= scale;
                    }

                    if (module.multiplyDragByParticleVelocity)
                    {
                        dragValue *= math.length(velocity);
                    }

                    velocity *= (1 - dragValue);
                }

                // Update particle velocity
                particleVelocities[i] = new ParticleVelocity { velocity = velocity };
            }
        }

        internal static void DoInheritVelocityModule(
            ref ParameterClip clip,
            in ShurikenParticleSystemData particleSystemData,
            in ShurikenInheritVelocityModule module,
            in float3 emitterVelocity,
            in DynamicBuffer<ParticleSeed>      particleSeeds,
            ref DynamicBuffer<ParticleVelocity> particleVelocities)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleVelocities.Length; i++)
            {
                var rng      = new Rng(particleSeeds[i].stableSeed);
                var velocity = particleVelocities[i].velocity;

                // Compute the inheritance factor
                float inheritFactor = module.curve.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenInheritVelocityModule.moduleSeed + ShurikenInheritVelocityModule.curveSeed),
                    particleSystemData.time) * module.curveMultiplier;

                // Apply the inherited velocity based on the mode
                switch (module.mode)
                {
                    case ParticleSystemInheritVelocityMode.Current:
                        // Apply current emitter velocity
                        velocity += emitterVelocity * inheritFactor;
                        break;
                    case ParticleSystemInheritVelocityMode.Initial:
                        // velocity += initialEmitterVelocity * inheritFactor;
                        break;
                }

                // Update particle velocity
                particleVelocities[i] = new ParticleVelocity { velocity = velocity };
            }
        }

        internal static void DoLifetimeByEmitterSpeedModule(
            ref ParameterClip clip,
            in ShurikenLifetimeByEmitterSpeedModule module,
            float emitterSpeed,
            int newParticleCount,
            in DynamicBuffer<ParticleSeed>                  particleSeeds,
            ref DynamicBuffer<ParticleAgeFraction>          particleAges,
            ref DynamicBuffer<ParticleInverseStartLifetime> particleInverseStartLifetimes)
        {
            if (!module.enabled || emitterSpeed < module.range.x || emitterSpeed > module.range.y)
                return;

            for (int i = particleAges.Length - newParticleCount; i < particleAges.Length; i++)
            {
                var rng = new Rng(particleSeeds[i].stableSeed);

                // Evaluate the curve at the current emitter speed
                float curveValue = module.curve.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenLifetimeByEmitterSpeedModule.moduleSeed + ShurikenLifetimeByEmitterSpeedModule.curveSeed),
                    emitterSpeed) * module.curveMultiplier;

                // Calculate the new inverse start lifetime based on the curve stableSeed
                float newInverseStartLifetime = 1f / curveValue;

                // Update the ParticleInverseStartLifetime for the current particle
                particleInverseStartLifetimes[i] = new ParticleInverseStartLifetime { inverseExpectedLifetime = newInverseStartLifetime };
            }
        }

        internal static void DoForceOverLifetimeModule(
            ref ParameterClip clip,
            in ShurikenForceOverLifetimeModule module,
            in quaternion emitterRotation,
            in DynamicBuffer<ParticleSeed>        particleSeeds,
            in DynamicBuffer<ParticleAgeFraction> particleAges,
            ref DynamicBuffer<ParticleVelocity>   particleVelocities)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleVelocities.Length; i++)
            {
                var rng = new Rng(particleSeeds[i].stableSeed);

                var velocity   = particleVelocities[i].velocity;
                float agePercent = particleAges[i].fraction;

                // Evaluate the force for each axis
                float forceX = module.x.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenForceOverLifetimeModule.moduleSeed + ShurikenForceOverLifetimeModule.xSeed),
                    agePercent) * module.xMultiplier;

                float forceY = module.y.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenForceOverLifetimeModule.moduleSeed + ShurikenForceOverLifetimeModule.ySeed),
                    agePercent) * module.yMultiplier;

                float forceZ = module.z.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenForceOverLifetimeModule.moduleSeed + ShurikenForceOverLifetimeModule.zSeed),
                    agePercent) * module.zMultiplier;

                // Add the force to the velocity
                float3 localForce = new float3(forceX, forceY, forceZ);

                // Convert force to world space if needed
                if (module.space == ParticleSystemSimulationSpace.World)
                {
                    math.rotate(emitterRotation, localForce);
                }
                velocity += localForce;

                particleVelocities[i] = new ParticleVelocity { velocity = velocity };
            }
        }

        internal static void DoColorOverLifetimeModule(
            ref ParameterClip clip,
            in ShurikenColorOverLifetimeModule module,
            in DynamicBuffer<ParticleSeed>        particleSeeds,
            in DynamicBuffer<ParticleAgeFraction> particleAges,  // Assuming you have this buffer to get the age percent of each particle
            ref DynamicBuffer<ParticleColor>      particleColors)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleColors.Length; i++)
            {
                var rng        = new Rng(particleSeeds[i].stableSeed);
                float agePercent = particleAges[i].fraction;

                // Evaluate the color for each channel
                var red = (half)module.colorRed.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenColorOverLifetimeModule.moduleSeed + ShurikenColorOverLifetimeModule.colorSeed),
                    agePercent);

                var green = (half)module.colorGreen.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenColorOverLifetimeModule.moduleSeed + ShurikenColorOverLifetimeModule.colorSeed),
                    agePercent);

                var blue = (half)module.colorBlue.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenColorOverLifetimeModule.moduleSeed + ShurikenColorOverLifetimeModule.colorSeed),
                    agePercent);

                var alpha = (half)module.colorAlpha.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenColorOverLifetimeModule.moduleSeed + ShurikenColorOverLifetimeModule.colorSeed),
                    agePercent);

                // Construct the new color and assign it to the particle
                particleColors[i] = new ParticleColor { color = new half4(red, green, blue, alpha) };
            }
        }

        internal static void DoColorBySpeedModule(
            ref ParameterClip clip,
            in ShurikenColorBySpeedModule module,
            in DynamicBuffer<ParticleSeed>     particleSeeds,
            in DynamicBuffer<ParticleVelocity> particleVelocities,  // Assuming you have this buffer for particle velocities
            ref DynamicBuffer<ParticleColor>   particleColors)
        {
            if (!module.enabled)
                return;

            float speedRange = module.range.y - module.range.x;

            for (int i = 0; i < particleColors.Length; i++)
            {
                var velocity      = particleVelocities[i].velocity;
                float particleSpeed = math.length(velocity);
                if (particleSpeed < module.range.x || particleSpeed > module.range.y)
                {
                    continue;
                }

                var rng = new Rng(particleSeeds[i].stableSeed);

                // Normalize the speed within the specified range
                float normalizedSpeed = (particleSpeed - module.range.x) / (module.range.y - module.range.x);

                // Evaluate the color for each channel
                var red = (half)module.colorRed.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenColorBySpeedModule.moduleSeed + ShurikenColorBySpeedModule.colorSeed),
                    normalizedSpeed);

                var green = (half)module.colorGreen.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenColorBySpeedModule.moduleSeed + ShurikenColorBySpeedModule.colorSeed),
                    normalizedSpeed);

                var blue = (half)module.colorBlue.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenColorBySpeedModule.moduleSeed + ShurikenColorBySpeedModule.colorSeed),
                    normalizedSpeed);

                var alpha = (half)module.colorAlpha.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenColorBySpeedModule.moduleSeed + ShurikenColorBySpeedModule.colorSeed),
                    normalizedSpeed);

                // Construct the new color and assign it to the particle
                particleColors[i] = new ParticleColor { color = new half4(red, green, blue, alpha) };
            }
        }

        internal static void DoSizeOverLifetimeModule(
            ref ParameterClip clip,
            in ShurikenSizeOverLifetimeModule module,
            in DynamicBuffer<ParticleSeed>        particleSeeds,
            in DynamicBuffer<ParticleAgeFraction> particleAges,
            ref DynamicBuffer<ParticleScale>      particleScales,
            ref DynamicBuffer<ParticleScale3d>    particleScales3d)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleSeeds.Length; i++)
            {
                var rng             = new Rng(particleSeeds[i].stableSeed);
                float agePercent      = particleAges[i].fraction;
                float3 scaleMultiplier = new float3();

                if (module.separateAxes)
                {
                    float x =
                        module.x.Evaluate(ref clip, rng.GetSequence(ShurikenSizeOverLifetimeModule.moduleSeed + ShurikenSizeOverLifetimeModule.xSeed),
                                          agePercent) * module.xMultiplier;
                    float y =
                        module.y.Evaluate(ref clip, rng.GetSequence(ShurikenSizeOverLifetimeModule.moduleSeed + ShurikenSizeOverLifetimeModule.ySeed),
                                          agePercent) * module.yMultiplier;
                    float z =
                        module.z.Evaluate(ref clip, rng.GetSequence(ShurikenSizeOverLifetimeModule.moduleSeed + ShurikenSizeOverLifetimeModule.zSeed),
                                          agePercent) * module.zMultiplier;

                    scaleMultiplier     = new float3(x, y, z);
                    particleScales3d[i] = new ParticleScale3d { scale = particleScales3d[i].scale * scaleMultiplier };
                }
                else
                {
                    // Apply uniform size change
                    float sizeFactor =
                        module.x.Evaluate(ref clip, rng.GetSequence(ShurikenSizeOverLifetimeModule.moduleSeed + ShurikenSizeOverLifetimeModule.xSeed),
                                          agePercent) * module.xMultiplier;
                    particleScales[i] = new ParticleScale { scale = particleScales[i].scale * sizeFactor };
                }
            }
        }

        internal static void DoSizeBySpeedModule(
            ref ParameterClip clip,
            in ShurikenSizeBySpeedModule module,
            in DynamicBuffer<ParticleSeed>     particleSeeds,
            in DynamicBuffer<ParticleVelocity> particleVelocities,
            ref DynamicBuffer<ParticleScale>   particleScales,
            ref DynamicBuffer<ParticleScale3d> particleScales3d)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleSeeds.Length; i++)
            {
                var rng           = new Rng(particleSeeds[i].stableSeed);
                float particleSpeed = math.length(particleVelocities[i].velocity);

                if (particleSpeed < module.range.x && particleSpeed > module.range.y)
                    continue;

                // Normalize the speed within the specified range
                float normalizedSpeed = (particleSpeed - module.range.x) / (module.range.y - module.range.x);
                normalizedSpeed = math.clamp(normalizedSpeed, 0, 1);  // Ensure it's within 0-1 range

                float scaleMultiplier;
                if (module.separateAxes)
                {
                    // If axes are separate, the x curve (and xMultiplier) is used for uniform scaling
                    scaleMultiplier = module.x.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenSizeBySpeedModule.moduleSeed + ShurikenSizeBySpeedModule.xSeed),
                        normalizedSpeed) * module.xMultiplier;
                    particleScales3d[i] = new ParticleScale3d { scale = particleScales3d[i].scale * scaleMultiplier };
                }
                else
                {
                    // For uniform scaling without separate axes
                    scaleMultiplier = module.x.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenSizeBySpeedModule.moduleSeed + ShurikenSizeBySpeedModule.xSeed),
                        normalizedSpeed) * module.xMultiplier;
                    particleScales[i] = new ParticleScale { scale = particleScales[i].scale + scaleMultiplier };
                }
            }
        }

        internal static void DoRotationOverLifetimeModule(
            ref ParameterClip clip,
            in ShurikenRotationOverLifetimeModule module,
            in DynamicBuffer<ParticleSeed>        particleSeeds,
            in DynamicBuffer<ParticleAgeFraction> particleAges,
            ref DynamicBuffer<ParticleRotation>   particleRotations,
            ref DynamicBuffer<ParticleRotation3d> particleRotations3d)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleSeeds.Length; i++)
            {
                var rng        = new Rng(particleSeeds[i].stableSeed);
                float agePercent = particleAges[i].fraction / (float)ushort.MaxValue;

                quaternion additionalRotation = quaternion.identity;
                if (module.separateAxes)
                {
                    // Apply rotation separately for each axis
                    float rotationX = module.x.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenRotationOverLifetimeModule.moduleSeed + ShurikenRotationOverLifetimeModule.xSeed),
                        agePercent) * module.xMultiplier;

                    float rotationY = module.y.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenRotationOverLifetimeModule.moduleSeed + ShurikenRotationOverLifetimeModule.ySeed),
                        agePercent) * module.yMultiplier;

                    float rotationZ = module.z.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenRotationOverLifetimeModule.moduleSeed + ShurikenRotationOverLifetimeModule.zSeed),
                        agePercent) * module.zMultiplier;

                    additionalRotation     = quaternion.EulerXYZ(rotationX, rotationY, rotationZ);
                    particleRotations3d[i] = new ParticleRotation3d { rotation = math.mul(particleRotations3d[i].rotation, additionalRotation) };
                }
                else
                {
                    // Apply angular rotation
                    float rotationUniform = module.x.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenRotationOverLifetimeModule.moduleSeed + ShurikenRotationOverLifetimeModule.xSeed),
                        agePercent) * module.xMultiplier;

                    particleRotations[i] = new ParticleRotation { rotationCCW = particleRotations[i].rotationCCW + rotationUniform };
                }
            }
        }

        internal static void DoRotationBySpeedModule(
            ref ParameterClip clip,
            in ShurikenRotationBySpeedModule module,
            in DynamicBuffer<ParticleSeed>        particleSeeds,
            in DynamicBuffer<ParticleVelocity>    particleVelocities,
            ref DynamicBuffer<ParticleRotation>   particleRotations,
            ref DynamicBuffer<ParticleRotation3d> particleRotations3d)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleSeeds.Length; i++)
            {
                var rng           = new Rng(particleSeeds[i].stableSeed);
                float particleSpeed = math.length(particleVelocities[i].velocity);

                // Check if the particle's speed is within the specified range
                if (particleSpeed < module.range.x && particleSpeed > module.range.y)
                    continue;

                // Normalize the speed within the specified range
                float normalizedSpeed = (particleSpeed - module.range.x) / (module.range.y - module.range.x);

                if (module.separateAxes)
                {
                    // Apply rotation separately for each axis
                    float rotationX = module.x.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenRotationBySpeedModule.moduleSeed + ShurikenRotationBySpeedModule.xSeed),
                        normalizedSpeed) * module.xMultiplier;

                    float rotationY = module.y.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenRotationBySpeedModule.moduleSeed + ShurikenRotationBySpeedModule.ySeed),
                        normalizedSpeed) * module.yMultiplier;

                    float rotationZ = module.z.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenRotationBySpeedModule.moduleSeed + ShurikenRotationBySpeedModule.zSeed),
                        normalizedSpeed) * module.zMultiplier;

                    quaternion additionalRotation = quaternion.EulerXYZ(rotationX, rotationY, rotationZ);
                    particleRotations3d[i] = new ParticleRotation3d { rotation = math.mul(particleRotations3d[i].rotation, additionalRotation) };
                }
                else
                {
                    // Apply uniform rotation
                    float rotationUniform = module.x.Evaluate(
                        ref clip,
                        rng.GetSequence(ShurikenRotationBySpeedModule.moduleSeed + ShurikenRotationBySpeedModule.xSeed),
                        normalizedSpeed) * module.xMultiplier;

                    particleRotations[i] = new ParticleRotation { rotationCCW = particleRotations[i].rotationCCW + rotationUniform };
                }
            }
        }

        internal static void DoNoiseModule(
            ref ParameterClip clip,
            in ShurikenNoiseModule module,
            float previousSimulationDeltaTime,
            in DynamicBuffer<ParticleSeed>                 particleSeeds,
            in DynamicBuffer<ParticleAgeFraction>          particleAgePercents,
            in DynamicBuffer<ParticleInverseStartLifetime> particleInverseStartLifetimes,
            ref DynamicBuffer<ParticleCenter>              particleCenters,
            bool hasRotation3d,
            ref DynamicBuffer<ParticleRotation>            particleRotations,
            ref DynamicBuffer<ParticleRotation3d>          particleRotations3d,
            bool hasScale3d,
            ref DynamicBuffer<ParticleScale>               particleScales,
            ref DynamicBuffer<ParticleScale3d>             particleScales3d)
        {
            if (!module.enabled)
                return;

            for (int i = 0; i < particleSeeds.Length; i++)
            {
                var rng              = new Rng(particleSeeds[i].stableSeed);
                var particleLifetime = GetParticleLifetime(particleAgePercents[i].fraction, particleInverseStartLifetimes[i].inverseExpectedLifetime);

                float scrollSpeed = module.scrollSpeed.Evaluate(
                    ref clip,
                    rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.scrollSpeedSeed),
                    particleLifetime) * module.scrollSpeedMultiplier;

                // Generate noise for each axis
                float3 noiseValue = GenerateNoiseValue(ref clip, module, rng, particleLifetime, scrollSpeed) *
                                    new float3(module.strengthX.Evaluate(
                                                   ref clip,
                                                   rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.strengthXSeed),
                                                   particleLifetime) * module.strengthXMultiplier,
                                               module.strengthY.Evaluate(
                                                   ref clip,
                                                   rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.strengthYSeed),
                                                   particleLifetime) * module.strengthYMultiplier,
                                               module.strengthZ.Evaluate(
                                                   ref clip,
                                                   rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.strengthZSeed),
                                                   particleLifetime) * module.strengthZMultiplier
                                               );

                var positionAmount = module.positionAmount.Evaluate(ref clip,
                                                                    rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.positionAmountSeed),
                                                                    particleLifetime);
                var noisePosition = noiseValue * positionAmount;

                // Undo previous positional noise since particle center is not re-simulated
                float previousParticleLifetime = math.max(particleLifetime - previousSimulationDeltaTime, 0);
                if (particleLifetime - previousParticleLifetime > 0)
                {
                    var previousPositionAmount = module.positionAmount.Evaluate(ref clip,
                                                                                rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.positionAmountSeed),
                                                                                previousParticleLifetime);
                    float3 previousNoiseValue = GenerateNoiseValue(ref clip, module, rng, particleLifetime, scrollSpeed) *
                                                new float3(module.strengthX.Evaluate(
                                                               ref clip,
                                                               rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.strengthXSeed),
                                                               previousParticleLifetime) * module.strengthXMultiplier,
                                                           module.strengthY.Evaluate(
                                                               ref clip,
                                                               rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.strengthYSeed),
                                                               previousParticleLifetime) * module.strengthYMultiplier,
                                                           module.strengthZ.Evaluate(
                                                               ref clip,
                                                               rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.strengthZSeed),
                                                               previousParticleLifetime) * module.strengthZMultiplier
                                                           );

                    noisePosition -= previousNoiseValue * previousPositionAmount;
                }
                particleCenters[i] = new ParticleCenter { center = particleCenters[i].center + noisePosition };

                // Apply noise to rotation
                var rotationAmount = module.rotationAmount.Evaluate(ref clip,
                                                                    rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.rotationAmountSeed),
                                                                    particleLifetime);
                if (rotationAmount > 0)
                {
                    if (hasRotation3d)
                    {
                        float3 angularChange = math.radians(noiseValue) * rotationAmount;

                        // Create quaternion rotations for each axis
                        quaternion rotationX = quaternion.AxisAngle(math.right(), angularChange.x);
                        quaternion rotationY = quaternion.AxisAngle(math.up(), angularChange.y);
                        quaternion rotationZ = quaternion.AxisAngle(math.forward(), angularChange.z);

                        // Combine axis rotations
                        quaternion combinedRotation = math.mul(math.mul(rotationX, rotationY), rotationZ);

                        // Apply the combined rotation to the particle's current rotation
                        var particleRotation = particleRotations3d[i];
                        particleRotation.rotation = math.mul(particleRotation.rotation, combinedRotation);
                        particleRotations3d[i]    = particleRotation;
                    }
                    else
                    {
                        // Apply angular change to the particle's current rotation
                        var particleRotation = particleRotations[i];
                        particleRotation.rotationCCW += math.radians(noiseValue.x) * rotationAmount;
                        particleRotations[i]          = particleRotation;
                    }
                }

                // Apply noise to size
                var sizeAmount = module.sizeAmount.Evaluate(ref clip, rng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.sizeAmountSeed), particleLifetime);
                if (sizeAmount > 0)
                {
                    if (hasScale3d)
                    {
                        var particleScale = particleScales3d[i];
                        particleScale.scale += noiseValue * sizeAmount;
                        particleScales3d[i]  = particleScale;
                    }
                    else
                    {
                        var particleScale = particleScales[i];
                        particleScale.scale += noiseValue.x * sizeAmount;
                        particleScales[i]    = particleScale;
                    }
                }
            }
        }

        internal static float3 GenerateNoiseValue(ref ParameterClip clip, ShurikenNoiseModule module, Rng particleRng, float timeFactor, float scrollSpeed)
        {
            float3 totalNoise   = float3.zero;
            float amplitude    = 1;
            float frequency    = module.frequency;
            float maxAmplitude = 0;  // Used for normalizing the result

            var xSequence = particleRng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.noiseXSeed);
            var ySequence = particleRng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.noiseYSeed);
            var zSequence = particleRng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.noiseZSeed);

            var scroll = timeFactor * scrollSpeed * frequency;

            //TODO:  Verify this is correct, that octaveCount = 0 means no noise
            //TODO:  Verify noise quality
            for (int octave = 0; octave < module.octaveCount; octave++)
            {
                float noiseXValue = 0f;
                float noiseYValue = 0f;
                float noiseZValue = 0f;

                switch (module.quality)
                {
                    case ParticleSystemNoiseQuality.Low:
                    {
                        noiseXValue = noise.snoise(new float2(scroll, xSequence.NextFloat()));
                        noiseYValue = noise.snoise(new float2(scroll, ySequence.NextFloat()));
                        noiseZValue = noise.snoise(new float2(scroll, zSequence.NextFloat()));
                        break;
                    }
                    case ParticleSystemNoiseQuality.Medium:
                    {
                        noiseXValue = noise.cnoise(new float2(scroll, xSequence.NextFloat()));
                        noiseYValue = noise.cnoise(new float2(scroll, ySequence.NextFloat()));
                        noiseZValue = noise.cnoise(new float2(scroll, zSequence.NextFloat()));
                        break;
                    }
                    case ParticleSystemNoiseQuality.High:
                    {
                        noiseXValue = noise.cnoise(new float3(scroll,scroll, xSequence.NextFloat()));
                        noiseYValue = noise.cnoise(new float3(scroll, scroll, ySequence.NextFloat()));
                        noiseZValue = noise.cnoise(new float3(scroll, scroll, zSequence.NextFloat()));
                        break;
                    }
                }

                float3 noiseValue = new float3(noiseXValue, noiseYValue, noiseZValue);

                totalNoise   += noiseValue * amplitude;
                maxAmplitude += amplitude;

                amplitude *= module.octaveMultiplier;
                frequency *= module.octaveScale;
            }

            // Normalizing the result
            totalNoise /= maxAmplitude;

            // Apply damping if enabled
            if (module.damping)
            {
                totalNoise *= (1 - timeFactor);  // Example damping effect, adjust as needed
            }

            //Remap
            if (module.remapEnabled)
            {
                float remappedXValue = module.remapX.Evaluate(ref clip,
                                                              particleRng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.remapXSeed),
                                                              totalNoise.x) * module.remapXMultiplier;
                float remappedYValue = module.remapY.Evaluate(ref clip,
                                                              particleRng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.remapYSeed),
                                                              totalNoise.y) * module.remapYMultiplier;
                float remappedZValue = module.remapZ.Evaluate(ref clip,
                                                              particleRng.GetSequence(ShurikenNoiseModule.moduleSeed + ShurikenNoiseModule.remapZSeed),
                                                              totalNoise.z) * module.remapZMultiplier;
                return new float3(remappedXValue, remappedYValue, remappedZValue);
            }

            return totalNoise;
        }

        internal static float GetEulerX(quaternion quaternion)
        {
            var q        = quaternion.value;
            double sinrCosp = +2.0 * (q.w * q.x + q.y * q.z);
            double cosrCosp = +1.0 - 2.0 * (q.x * q.x + q.y * q.y);
            return (float)math.atan2(sinrCosp, cosrCosp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float GetParticleLifetime(float agePercent, float inverseStartLifetime)
        {
            return agePercent * (1 / inverseStartLifetime);
        }
    }
}
#endif

