using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

// Even though this job only iterates chunks, it still ends up being expensive single-threaded.
// We capture the relevant data per chunk into an array in parallel, and then use that to populate the hashmap.

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
        public void OnUpdate(ref SystemState state)
        {
            var crossfadePtrMap = new NativeHashMap<LODCrossfadePtrMap.ChunkIdentifier, LODCrossfadePtrMap.CrossfadePtr>(
                1,
                state.WorldUpdateAllocator);
            latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new LODCrossfadePtrMap { chunkIdentifierToPtrMap = crossfadePtrMap });

            if (!m_lodCrossfadeQuery.IsEmptyIgnoreFilter)
            {
                int chunkCountWithoutFiltering = m_lodCrossfadeQuery.CalculateChunkCountWithoutFiltering();

                var chunkPtrsArray = CollectionHelper.CreateNativeArray<ChunkIdAndPointer>(chunkCountWithoutFiltering,
                                                                                           state.WorldUpdateAllocator,
                                                                                           NativeArrayOptions.UninitializedMemory);

                state.Dependency = new CaptureLodCrossfadePtrsJob
                {
                    lodCrossfadeHandle              = GetComponentTypeHandle<LodCrossfade>(true),
                    entitiesGraphicsChunkInfoHandle = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                    array                           = chunkPtrsArray,
                }.ScheduleParallel(m_lodCrossfadeQuery, state.Dependency);

                state.Dependency = new IndexLodCrossfadePtrsJob
                {
                    map   = crossfadePtrMap,
                    array = chunkPtrsArray,
                }.Schedule(state.Dependency);
            }
        }

        struct ChunkIdAndPointer
        {
            public LODCrossfadePtrMap.ChunkIdentifier id;
            public LODCrossfadePtrMap.CrossfadePtr    ptr;
        }

        [BurstCompile]
        struct CaptureLodCrossfadePtrsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LodCrossfade>              lodCrossfadeHandle;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> entitiesGraphicsChunkInfoHandle;

            public NativeArray<ChunkIdAndPointer> array;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var info       = chunk.GetChunkComponentRefRO(ref entitiesGraphicsChunkInfoHandle);
                var identifier = new LODCrossfadePtrMap.ChunkIdentifier
                {
                    batchID         = (uint)info.ValueRO.BatchIndex,
                    batchStartIndex = info.ValueRO.CullingData.ChunkOffsetInBatch
                };
                var ptr = new LODCrossfadePtrMap.CrossfadePtr { ptr = (LodCrossfade*)chunk.GetRequiredComponentDataPtrRO(ref lodCrossfadeHandle) };
                array[unfilteredChunkIndex]                         = new ChunkIdAndPointer { id = identifier, ptr = ptr };
            }
        }

        [BurstCompile]
        struct IndexLodCrossfadePtrsJob : IJob
        {
            [ReadOnly] public NativeArray<ChunkIdAndPointer>                                          array;
            public NativeHashMap<LODCrossfadePtrMap.ChunkIdentifier, LODCrossfadePtrMap.CrossfadePtr> map;

            public unsafe void Execute()
            {
                map.Capacity = array.Length;

                foreach (var chunkIdAndPointer in array)
                {
                    map.Add(chunkIdAndPointer.id, chunkIdAndPointer.ptr);
                }
            }
        }
    }
}

