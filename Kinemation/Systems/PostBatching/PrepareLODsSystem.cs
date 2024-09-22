using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct PrepareLODsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_lodCrossfadeQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_lodCrossfadeQuery = state.Fluent().With<LodCrossfade>(true).With<EntitiesGraphicsChunkInfo>(true, true).Build();
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld<LODCrossfadePtrMap>(default);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var crossfadePtrMap = new NativeHashMap<LODCrossfadePtrMap.ChunkIdentifier, LODCrossfadePtrMap.CrossfadePtr>(
                1,
                state.WorldUpdateAllocator);
            latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new LODCrossfadePtrMap { chunkIdentifierToPtrMap = crossfadePtrMap });

            if (!m_lodCrossfadeQuery.IsEmptyIgnoreFilter)
            {
                state.Dependency = new CaptureLodCrossfadePtrsJob
                {
                    lodCrossfadeHandle              = GetComponentTypeHandle<LodCrossfade>(true),
                    entitiesGraphicsChunkInfoHandle = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                    map                             = crossfadePtrMap,
                    chunkCount                      = m_lodCrossfadeQuery.CalculateChunkCountWithoutFiltering()
                }.Schedule(m_lodCrossfadeQuery, state.Dependency);
            }
        }

        // Schedule single-threaded
        [BurstCompile]
        struct CaptureLodCrossfadePtrsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LodCrossfade>              lodCrossfadeHandle;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> entitiesGraphicsChunkInfoHandle;

            public NativeHashMap<LODCrossfadePtrMap.ChunkIdentifier, LODCrossfadePtrMap.CrossfadePtr> map;
            public int                                                                                chunkCount;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (map.IsEmpty)
                    map.Capacity = chunkCount;

                var info       = chunk.GetChunkComponentRefRO(ref entitiesGraphicsChunkInfoHandle);
                var identifier = new LODCrossfadePtrMap.ChunkIdentifier { batchID = (uint)info.ValueRO.BatchIndex, batchStartIndex = info.ValueRO.CullingData.ChunkOffsetInBatch };
                var ptr        = new LODCrossfadePtrMap.CrossfadePtr { ptr = (LodCrossfade*)chunk.GetRequiredComponentDataPtrRO(ref lodCrossfadeHandle) };
                map.Add(identifier, ptr);
            }
        }
    }
}

