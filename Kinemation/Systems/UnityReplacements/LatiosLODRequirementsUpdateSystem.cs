#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using System.Diagnostics;
using Latios;
using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation
{
    [RequireMatchingQueriesForUpdate]
    [UpdateBefore(typeof(FreezeStaticLODObjects))]  // FreezeStaticLODObjects system has an UpdateAfter dependency on this
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.EntitySceneOptimizations | WorldSystemFilterFlags.Editor)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LatiosLODRequirementsUpdateSystem : ISystem
    {
        EntityQuery m_UpdatedLODRanges;
        EntityQuery m_LODReferencePoints;
        EntityQuery m_LODGroupReferencePoints;

        public void OnCreate(ref SystemState state)
        {
            // Change filter: LODGroupConversion add MeshLODComponent for all LOD children. When the MeshLODComponent is added/changed, we recalculate LOD ranges.
            m_UpdatedLODRanges = state.GetEntityQuery(ComponentType.ReadOnly<WorldTransform>(), typeof(MeshLODComponent), typeof(RootLODRange), typeof(LODRange));
            m_UpdatedLODRanges.SetChangedVersionFilter(ComponentType.ReadWrite<MeshLODComponent>());

            m_LODReferencePoints = state.GetEntityQuery(ComponentType.ReadOnly<WorldTransform>(),
                                                        ComponentType.ReadOnly<MeshLODComponent>(),
                                                        typeof(RootLODWorldReferencePoint),
                                                        typeof(LODWorldReferencePoint));

            // Change filter: LOD Group world reference points only change when MeshLODGroupComponent or LocalToWorld change
            m_LODGroupReferencePoints =
                state.GetEntityQuery(ComponentType.ReadOnly<MeshLODGroupComponent>(), ComponentType.ReadOnly<WorldTransform>(), typeof(LODGroupWorldReferencePoint));
            m_LODGroupReferencePoints.SetChangedVersionFilter(new[] { ComponentType.ReadWrite<MeshLODGroupComponent>(), ComponentType.ReadWrite<WorldTransform>() });
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var updateLODRangesJob = new UpdateLODRangesJob
            {
                MeshLODGroupComponent = GetComponentLookup<MeshLODGroupComponent>(true),
                MeshLODComponent      = GetComponentTypeHandle<MeshLODComponent>(),
                RootLODRange          = GetComponentTypeHandle<RootLODRange>(),
                LODRange              = GetComponentTypeHandle<LODRange>(),
            };

            var updateGroupReferencePointJob = new UpdateLODGroupWorldReferencePointsJob
            {
                MeshLODGroupComponent       = GetComponentTypeHandle<MeshLODGroupComponent>(true),
                WorldTransform              = GetComponentTypeHandle<WorldTransform>(true),
                LODGroupWorldReferencePoint = GetComponentTypeHandle<LODGroupWorldReferencePoint>(),
            };

            var updateReferencePointJob = new UpdateLODWorldReferencePointsJob
            {
                //MeshLODGroupComponent = GetComponentLookup<MeshLODGroupComponent>(true),
                MeshLODComponent            = GetComponentTypeHandle<MeshLODComponent>(true),
                LODGroupWorldReferencePoint = GetComponentLookup<LODGroupWorldReferencePoint>(true),
                RootLODWorldReferencePoint  = GetComponentTypeHandle<RootLODWorldReferencePoint>(),
                LODWorldReferencePoint      = GetComponentTypeHandle<LODWorldReferencePoint>(),
            };

            var depLODRanges            = updateLODRangesJob.ScheduleParallelByRef(m_UpdatedLODRanges, state.Dependency);
            var depGroupReferencePoints = updateGroupReferencePointJob.ScheduleParallelByRef(m_LODGroupReferencePoints, state.Dependency);
            var depCombined             = JobHandle.CombineDependencies(depLODRanges, depGroupReferencePoints);
            var depReferencePoints      = updateReferencePointJob.ScheduleParallelByRef(m_LODReferencePoints, depCombined);

            state.Dependency = JobHandle.CombineDependencies(depReferencePoints, depReferencePoints);
        }

        [BurstCompile]
        struct UpdateLODRangesJob : IJobChunk
        {
            [ReadOnly] public ComponentLookup<MeshLODGroupComponent> MeshLODGroupComponent;

            public ComponentTypeHandle<MeshLODComponent> MeshLODComponent;
            public ComponentTypeHandle<RootLODRange>     RootLODRange;
            public ComponentTypeHandle<LODRange>         LODRange;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void CheckDeepHLODSupport(Entity entity)
            {
                if (entity != Entity.Null)
                    throw new System.NotImplementedException("Deep HLOD is not supported yet");
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Unity.Assertions.Assert.IsFalse(useEnabledMask);

                var rootLODRange  = chunk.GetNativeArray(ref RootLODRange);
                var lodRange      = chunk.GetNativeArray(ref LODRange);
                var meshLods      = chunk.GetNativeArray(ref MeshLODComponent);
                var instanceCount = chunk.Count;

                for (int i = 0; i < instanceCount; i++)
                {
                    var meshLod        = meshLods[i];
                    var lodGroupEntity = meshLod.Group;
                    var lodMask        = meshLod.LODMask;
                    var lodGroup       = MeshLODGroupComponent[lodGroupEntity];

                    lodRange[i] = new LODRange(lodGroup, lodMask);
                }

                for (int i = 0; i < instanceCount; i++)
                {
                    var meshLod           = meshLods[i];
                    var lodGroupEntity    = meshLod.Group;
                    var lodGroup          = MeshLODGroupComponent[lodGroupEntity];
                    var parentMask        = lodGroup.ParentMask;
                    var parentGroupEntity = lodGroup.ParentGroup;

                    // Store LOD parent group in MeshLODComponent to avoid double indirection for every entity
                    meshLod.ParentGroup = parentGroupEntity;
                    meshLods[i]         = meshLod;

                    RootLODRange rootLod;

                    if (parentGroupEntity == Entity.Null)
                    {
                        rootLod.LOD.MinDist = 0;
                        rootLod.LOD.MaxDist = 1048576.0f;
                    }
                    else
                    {
                        var parentLodGroup = MeshLODGroupComponent[parentGroupEntity];
                        rootLod.LOD        = new LODRange(parentLodGroup, parentMask);
                        CheckDeepHLODSupport(parentLodGroup.ParentGroup);
                    }

                    rootLODRange[i] = rootLod;
                }
            }
        }

        [BurstCompile]
        struct UpdateLODGroupWorldReferencePointsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<MeshLODGroupComponent> MeshLODGroupComponent;
            [ReadOnly] public ComponentTypeHandle<WorldTransform>        WorldTransform;
            public ComponentTypeHandle<LODGroupWorldReferencePoint>      LODGroupWorldReferencePoint;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Unity.Assertions.Assert.IsFalse(useEnabledMask);

                var meshLODGroupComponent       = chunk.GetNativeArray(ref MeshLODGroupComponent);
                var worldTransform              = chunk.GetNativeArray(ref WorldTransform);
                var lodGroupWorldReferencePoint = chunk.GetNativeArray(ref LODGroupWorldReferencePoint);
                var instanceCount               = chunk.Count;

                for (int i = 0; i < instanceCount; i++)
                {
                    lodGroupWorldReferencePoint[i] = new LODGroupWorldReferencePoint {
                        Value                      = qvvs.TransformPoint(worldTransform[i].worldTransform, meshLODGroupComponent[i].LocalReferencePoint)
                    };
                }
            }
        }

        [BurstCompile]
        struct UpdateLODWorldReferencePointsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<MeshLODComponent>        MeshLODComponent;
            [ReadOnly] public ComponentLookup<LODGroupWorldReferencePoint> LODGroupWorldReferencePoint;
            public ComponentTypeHandle<RootLODWorldReferencePoint>         RootLODWorldReferencePoint;
            public ComponentTypeHandle<LODWorldReferencePoint>             LODWorldReferencePoint;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Unity.Assertions.Assert.IsFalse(useEnabledMask);

                var rootLODWorldReferencePoint = chunk.GetNativeArray(ref RootLODWorldReferencePoint);
                var lodWorldReferencePoint     = chunk.GetNativeArray(ref LODWorldReferencePoint);
                var meshLods                   = chunk.GetNativeArray(ref MeshLODComponent);
                var instanceCount              = chunk.Count;

                for (int i = 0; i < instanceCount; i++)
                {
                    var meshLod                     = meshLods[i];
                    var lodGroupEntity              = meshLod.Group;
                    var lodGroupWorldReferencePoint = LODGroupWorldReferencePoint[lodGroupEntity].Value;

                    lodWorldReferencePoint[i] = new LODWorldReferencePoint { Value = lodGroupWorldReferencePoint };
                }

                for (int i = 0; i < instanceCount; i++)
                {
                    var meshLod           = meshLods[i];
                    var parentGroupEntity = meshLod.ParentGroup;

                    RootLODWorldReferencePoint rootPoint;

                    if (parentGroupEntity == Entity.Null)
                    {
                        rootPoint.Value = new float3(0, 0, 0);
                    }
                    else
                    {
                        var parentGroupWorldReferencePoint = LODGroupWorldReferencePoint[parentGroupEntity].Value;
                        rootPoint.Value                    = parentGroupWorldReferencePoint;
                    }

                    rootLODWorldReferencePoint[i] = rootPoint;
                }
            }
        }
    }
}
#endif

