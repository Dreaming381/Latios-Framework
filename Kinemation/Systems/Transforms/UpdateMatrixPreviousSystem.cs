#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios.Transforms.Systems;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(MotionHistoryUpdateSuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateMatrixPreviousSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<PostProcessMatrix>(true).With<PreviousPostProcessMatrix>().IncludeDisabledEntities().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new UpdateMatricesJob
            {
                postProcessMatrixHandle         = GetComponentTypeHandle<PostProcessMatrix>(true),
                previousPostProcessMatrixHandle = GetComponentTypeHandle<PreviousPostProcessMatrix>(false),
                lastSystemVersion               = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct UpdateMatricesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix> postProcessMatrixHandle;
            public ComponentTypeHandle<PreviousPostProcessMatrix>    previousPostProcessMatrixHandle;
            public uint                                              lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidChange(ref postProcessMatrixHandle, lastSystemVersion))
                    return;

                var current  = chunk.GetNativeArray(ref postProcessMatrixHandle).Reinterpret<float3x4>();
                var previous = chunk.GetNativeArray(ref previousPostProcessMatrixHandle).Reinterpret<float3x4>();
                previous.CopyFrom(current);
            }
        }
    }
}

#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
using Latios.Transforms.Systems;
using static Unity.Entities.SystemAPI;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(MotionHistoryUpdateSuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateMatrixPreviousSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<LocalToWorld>(true).With<BuiltinMaterialPropertyUnity_MatrixPreviousM>().IncludeDisabledEntities().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new UpdateMatricesJob
            {
                postProcessMatrixHandle         = GetComponentTypeHandle<LocalToWorld>(true),
                previousPostProcessMatrixHandle = GetComponentTypeHandle<BuiltinMaterialPropertyUnity_MatrixPreviousM>(false),
                lastSystemVersion               = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct UpdateMatricesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> postProcessMatrixHandle;
            public ComponentTypeHandle<BuiltinMaterialPropertyUnity_MatrixPreviousM> previousPostProcessMatrixHandle;
            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidChange(ref postProcessMatrixHandle, lastSystemVersion))
                    return;

                var current  = chunk.GetNativeArray(ref postProcessMatrixHandle).Reinterpret<float4x4>();
                var previous = chunk.GetNativeArray(ref previousPostProcessMatrixHandle).Reinterpret<float4x4>();
                previous.CopyFrom(current);
            }
        }
    }
}
#endif

