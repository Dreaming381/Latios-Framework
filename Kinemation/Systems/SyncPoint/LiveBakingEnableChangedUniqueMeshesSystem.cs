using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LiveBakingEnableChangedUniqueMeshesSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().With<LiveBakedTag>(true).With<UniqueMeshConfig>(false).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                posHandle         = GetBufferTypeHandle<UniqueMeshPosition>(true),
                normHandle        = GetBufferTypeHandle<UniqueMeshNormal>(true),
                tanHandle         = GetBufferTypeHandle<UniqueMeshTangent>(true),
                colHandle         = GetBufferTypeHandle<UniqueMeshColor>(true),
                uv0Handle         = GetBufferTypeHandle<UniqueMeshUv0xy>(true),
                uv3Handle         = GetBufferTypeHandle<UniqueMeshUv3xyz>(true),
                indexHandle       = GetBufferTypeHandle<UniqueMeshIndex>(true),
                submeshHandle     = GetBufferTypeHandle<UniqueMeshSubmesh>(true),
                configHandle      = GetComponentTypeHandle<UniqueMeshConfig>(false),
                lastSystemVersion = latiosWorld.worldBlackboardEntity.GetComponentData<SystemVersionBeforeLiveBake>().version,
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<UniqueMeshPosition> posHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshNormal>   normHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshTangent>  tanHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshColor>    colHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshUv0xy>    uv0Handle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshUv3xyz>   uv3Handle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshIndex>    indexHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshSubmesh>  submeshHandle;
            public ComponentTypeHandle<UniqueMeshConfig>           configHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool anythingChanged = chunk.DidOrderChange(lastSystemVersion) ||
                                       chunk.DidChange(ref configHandle, lastSystemVersion) ||
                                       chunk.DidChange(ref posHandle, lastSystemVersion) ||
                                       chunk.DidChange(ref normHandle, lastSystemVersion) ||
                                       chunk.DidChange(ref tanHandle, lastSystemVersion) ||
                                       chunk.DidChange(ref colHandle, lastSystemVersion) ||
                                       chunk.DidChange(ref uv0Handle, lastSystemVersion) ||
                                       chunk.DidChange(ref uv3Handle, lastSystemVersion) ||
                                       chunk.DidChange(ref indexHandle, lastSystemVersion) ||
                                       chunk.DidChange(ref submeshHandle, lastSystemVersion);
                if (anythingChanged)
                    chunk.SetComponentEnabledForAll(ref configHandle, true);
            }
        }
    }
}

