#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

// Originally, this was supposed to be just a copy and paste with the WorldTransform
// instead of LocalToWorld, but I noticed that Unity's version (pre 15 at the time of writing)
// was awful. It was updating every moving entity on the main thread without Burst.
// I was looking at the Unity C# source to see if there was some internal API that was unmanaged
// that I could leverage. Instead, I found LightProbeQuery. Now this whole thing is jobified
// and Bursted!
// This system seems like a good candidate to move to PostBatching. So that's where it is now.

namespace Latios.Kinemation
{
    //[UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LatiosLightProbeUpdateSystem : ISystem, ISystemShouldUpdate
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_probeGridQuery;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_probeGridQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<BuiltinMaterialPropertyUnity_SHCoefficients>(),
                ComponentType.ReadOnly<WorldTransform>(),
                ComponentType.ReadOnly<BlendProbeTag>()
                );

            state.EntityManager.AddComponentData(state.SystemHandle, new RequiresFullRebuild { requiresFullRebuild = true });
            state.EntityManager.AddComponentObject(state.SystemHandle, new TetrahedralizationChangeCallbackReceiver(ref state));
        }

        public void OnDestroy(ref SystemState state)
        {
            state.EntityManager.RemoveComponent<TetrahedralizationChangeCallbackReceiver>(state.SystemHandle);
        }

        bool m_isValidLightProbesGrid;

        public bool ShouldUpdateSystem(ref SystemState state)
        {
            m_isValidLightProbesGrid = IsValidLightProbeGrid();
            return true;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!m_isValidLightProbesGrid)
            {
                state.EntityManager.SetComponentData(state.SystemHandle, new RequiresFullRebuild { requiresFullRebuild = true});
                return;
            }

            int shIndex = latiosWorld.worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>()
                          .AsNativeArray().IndexOf(ComponentType.ReadOnly<BuiltinMaterialPropertyUnity_SHCoefficients>());
            ulong shMaterialMaskLower = (ulong)shIndex >= 64UL ? 0UL : (1UL << shIndex);
            ulong shMaterialMaskUpper = (ulong)shIndex >= 64UL ? (1UL << (shIndex - 64)) : 0UL;

            var query               = new LightProbesQuery(Allocator.TempJob);
            var requiresFullRebuild = state.EntityManager.GetComponentData<RequiresFullRebuild>(state.SystemHandle);

            var job = new Job
            {
                blendProbeTagHandle     = GetComponentTypeHandle<BlendProbeTag>(true),
                chunkMaterialMaskHandle = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                lastSystemVersion       = state.LastSystemVersion,
                lightProbeQuery         = query,
                requiresFullRebuild     = requiresFullRebuild.requiresFullRebuild,
                shHandle                = GetComponentTypeHandle<BuiltinMaterialPropertyUnity_SHCoefficients>(false),
                shMaskLower             = shMaterialMaskLower,
                shMaskUpper             = shMaterialMaskUpper,
                worldTransformHandle    = GetComponentTypeHandle<WorldTransform>(true)
            };

            requiresFullRebuild.requiresFullRebuild = false;
            state.EntityManager.SetComponentData(state.SystemHandle, requiresFullRebuild);

            state.Dependency = job.ScheduleParallelByRef(m_probeGridQuery, state.Dependency);
            state.Dependency = query.Dispose(state.Dependency);
        }

        internal static bool IsValidLightProbeGrid()
        {
            var  probes    = LightmapSettings.lightProbes;
            bool validGrid = probes != null && probes.count > 0;
            return validGrid;
        }

        struct RequiresFullRebuild : IComponentData
        {
            public bool requiresFullRebuild;
        }

        class TetrahedralizationChangeCallbackReceiver : IComponentData, System.IDisposable
        {
            EntityManager entityManager;
            SystemHandle  systemHandle;

            public TetrahedralizationChangeCallbackReceiver()
            {
            }

            public TetrahedralizationChangeCallbackReceiver(ref SystemState state)
            {
                entityManager                   = state.EntityManager;
                systemHandle                    = state.SystemHandle;
                LightProbes.lightProbesUpdated += TriggerFullRebuild;
            }

            public void Dispose()
            {
                LightProbes.lightProbesUpdated -= TriggerFullRebuild;
            }

            void TriggerFullRebuild()
            {
                entityManager.SetComponentData(systemHandle, new RequiresFullRebuild { requiresFullRebuild = true });
            }
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<WorldTransform>                   worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<BlendProbeTag>                    blendProbeTagHandle;
            public ComponentTypeHandle<BuiltinMaterialPropertyUnity_SHCoefficients> shHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>              chunkMaterialMaskHandle;
            [ReadOnly] public LightProbesQuery                                      lightProbeQuery;
            public ulong                                                            shMaskLower;
            public ulong                                                            shMaskUpper;
            public uint                                                             lastSystemVersion;
            public bool                                                             requiresFullRebuild;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool changed  = chunk.DidChange(ref worldTransformHandle, lastSystemVersion);
                changed      |= chunk.DidChange(ref blendProbeTagHandle, lastSystemVersion);

                if (!(changed || requiresFullRebuild))
                    return;

                ref var mask      = ref chunk.GetChunkComponentRefRW(ref chunkMaterialMaskHandle);
                mask.lower.Value |= shMaskLower;
                mask.upper.Value |= shMaskUpper;

                var worldTransforms = chunk.GetNativeArray(ref worldTransformHandle);
                var shArray         = chunk.GetNativeArray(ref shHandle);
                if (requiresFullRebuild)
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        // The documentation specifies that this index should be initially set to 0,
                        // and then each frame it can be updated to the previous frame's values for faster queries.
                        // Since this is a full rebuild, we set the input index to 0.
                        int tetIndex = 0;
                        lightProbeQuery.CalculateInterpolatedLightAndOcclusionProbe(worldTransforms[i].position, ref tetIndex, out var shl2, out _);
                        var result = new BuiltinMaterialPropertyUnity_SHCoefficients()
                        {
                            Value = new SHCoefficients(shl2)
                        };
                        //result.Value.Padding.x = math.asfloat(tetIndex);
                        shArray[i] = result;
                    }
                }
                else
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        // The documentation specifies that this index should be initially set to 0,
                        // and then each frame it can be updated to the previous frame's values for faster queries.
                        // However, we don't have a component to store this, so for now we just set it to 0 every time.
                        //int tetIndex = math.asint(shArray[i].Value.Padding.x);
                        int tetIndex = 0;
                        lightProbeQuery.CalculateInterpolatedLightAndOcclusionProbe(worldTransforms[i].position, ref tetIndex, out var shl2, out _);
                        var result = new BuiltinMaterialPropertyUnity_SHCoefficients()
                        {
                            Value = new SHCoefficients(shl2)
                        };
                        //result.Value.Padding.x = math.asfloat(tetIndex);
                        shArray[i] = result;
                    }
                }
            }
        }
    }
}
#endif

