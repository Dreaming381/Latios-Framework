using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

// Currently disabled until a new optimization for Entities.Graphics 1.0 is decided upon.
// For now, no count caching is performed.
/*
   namespace Latios.Kinemation.Systems
   {
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateChunkLinearBlendMetadataSystem : ISystem
    {
        EntityQuery          m_query;
        LatiosWorldUnmanaged latiosWorld;

        ComponentTypeHandle<SkeletonDependent>                      m_skeletonDependentHandle;
        ComponentTypeHandle<ChunkLinearBlendSkinningMemoryMetadata> m_chunkLinearBlendSkinningMemoryMetadataHandle;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().WithAll<SkeletonDependent>(true).WithAll<ChunkLinearBlendSkinningMemoryMetadata>(false, true).Build();

            m_skeletonDependentHandle                      = state.GetComponentTypeHandle<SkeletonDependent>(true);
            m_chunkLinearBlendSkinningMemoryMetadataHandle = state.GetComponentTypeHandle<ChunkLinearBlendSkinningMemoryMetadata>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            latiosWorld.worldBlackboardEntity.SetComponentData(new MaxRequiredLinearBlendMatrices { matricesCount = 0 });

            var lastSystemVersion = state.LastSystemVersion;
            var blobHandle        = state.GetComponentTypeHandle<SkeletonDependent>(true);
            var metaHandle        = state.GetComponentTypeHandle<ChunkLinearBlendSkinningMemoryMetadata>(false);

            state.Dependency = new UpdateChunkMatrixCountsJob
            {
                blobHandle        = blobHandle,
                metaHandle        = metaHandle,
                lastSystemVersion = lastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state) {
        }

        [BurstCompile]
        struct UpdateChunkMatrixCountsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>           blobHandle;
            public ComponentTypeHandle<ChunkLinearBlendSkinningMemoryMetadata> metaHandle;
            public uint                                                        lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsUpdate  = chunk.DidChange(blobHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidOrderChange(lastSystemVersion);
                if (!needsUpdate)
                    return;

                var blobs       = chunk.GetNativeArray(blobHandle);
                int minMatrices = int.MaxValue;
                int maxMatrices = int.MinValue;

                for (int i = 0; i < chunk.Count; i++)
                {
                    int c       = blobs[i].skinningBlob.Value.bindPoses.Length;
                    minMatrices = math.min(minMatrices, c);
                    maxMatrices = math.max(maxMatrices, c);
                }

                //CheckMatrixCountMismatch(minMatrices, maxMatrices);

                var metadata = chunk.GetChunkComponentData(metaHandle);
                if (metadata.bonesPerMesh != maxMatrices || metadata.entitiesInChunk != chunk.Count)
                {
                    metadata.bonesPerMesh    = maxMatrices;
                    metadata.entitiesInChunk = chunk.Count;
                    chunk.SetChunkComponentData(metaHandle, metadata);
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckMatrixCountMismatch(int min, int max)
            {
                if (min != max)
                    UnityEngine.Debug.LogWarning(
                        "A chunk contains multiple Mesh Skinning Blobs with different matrix counts. Because Mesh Skinning Blobs are tied to their RenderMesh of which there is only one per chunk, this is likely a bug. Did you forget to change the Mesh Skinning Blob Reference when changing a Render Mesh?");
            }
        }
    }
   }
 */

