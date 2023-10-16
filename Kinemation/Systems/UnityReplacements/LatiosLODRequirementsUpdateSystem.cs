using System.Diagnostics;
using Latios.Transforms;
using Latios.Transforms.Abstract;
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

        WorldTransformReadOnlyAspect.TypeHandle m_worldTransformHandle;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Change filter: LODGroupConversion add MeshLODComponent for all LOD children. When the MeshLODComponent is added/changed, we recalculate LOD ranges.
            m_UpdatedLODRanges = state.Fluent().WithWorldTransformReadOnly().With<MeshLODComponent>().With<RootLODRange>().With<LODRange>().Build();
            m_UpdatedLODRanges.SetChangedVersionFilter(ComponentType.ReadWrite<MeshLODComponent>());

            m_LODReferencePoints =
                state.Fluent().WithWorldTransformReadOnly().With<MeshLODComponent>(true).With<RootLODWorldReferencePoint>().With<LODWorldReferencePoint>().Build();

            // Change filter: LOD Group world reference points only change when MeshLODGroupComponent or LocalToWorld change
            m_LODGroupReferencePoints = state.Fluent().With<MeshLODGroupComponent>(true).WithWorldTransformReadOnly().With<LODGroupWorldReferencePoint>().Build();
            m_LODGroupReferencePoints.AddChangedVersionFilter(ComponentType.ReadWrite<MeshLODGroupComponent>());
            m_LODGroupReferencePoints.AddWorldTranformChangeFilter();

            m_worldTransformHandle = new WorldTransformReadOnlyAspect.TypeHandle(ref state);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_worldTransformHandle.Update(ref state);

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
                WorldTransform              = m_worldTransformHandle,
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
                        rootLod.LOD.LODMask = 0;
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
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle    WorldTransform;
            public ComponentTypeHandle<LODGroupWorldReferencePoint>      LODGroupWorldReferencePoint;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Unity.Assertions.Assert.IsFalse(useEnabledMask);

                var meshLODGroupComponent       = chunk.GetNativeArray(ref MeshLODGroupComponent);
                var worldTransform              = WorldTransform.Resolve(chunk);
                var lodGroupWorldReferencePoint = chunk.GetNativeArray(ref LODGroupWorldReferencePoint);
                var instanceCount               = chunk.Count;

                for (int i = 0; i < instanceCount; i++)
                {
                    lodGroupWorldReferencePoint[i] = new LODGroupWorldReferencePoint {
                        Value                      = qvvs.TransformPoint(worldTransform[i].worldTransformQvvs, meshLODGroupComponent[i].LocalReferencePoint)
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

