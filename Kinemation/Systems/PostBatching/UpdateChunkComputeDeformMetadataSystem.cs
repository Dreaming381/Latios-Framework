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
    public partial struct UpdateChunkComputeDeformMetadataSystem : ISystem
    {
        EntityQuery          m_query;
        LatiosWorldUnmanaged latiosWorld;

        ComponentTypeHandle<SkeletonDependent>                m_skeletonDependentHandle;
        ComponentTypeHandle<ChunkComputeDeformMemoryMetadata> m_chunkComputeDeformMemoryMetadataHandle;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().WithAll<SkeletonDependent>(true).WithAll<ChunkComputeDeformMemoryMetadata>(false, true).Build();

            m_skeletonDependentHandle                = state.GetComponentTypeHandle<SkeletonDependent>(true);
            m_chunkComputeDeformMemoryMetadataHandle = state.GetComponentTypeHandle<ChunkComputeDeformMemoryMetadata>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            latiosWorld.worldBlackboardEntity.SetComponentData(new MaxRequiredDeformVertices { verticesCount = 0 });

            var lastSystemVersion = state.LastSystemVersion;

            m_skeletonDependentHandle.Update(ref state);
            m_chunkComputeDeformMemoryMetadataHandle.Update(ref state);

            state.Dependency = new UpdateChunkVertexCountsJob
            {
                blobHandle        = m_skeletonDependentHandle,
                metaHandle        = m_chunkComputeDeformMemoryMetadataHandle,
                lastSystemVersion = lastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct UpdateChunkVertexCountsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>     blobHandle;
            public ComponentTypeHandle<ChunkComputeDeformMemoryMetadata> metaHandle;
            public uint                                                  lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsUpdate  = chunk.DidChange(blobHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidOrderChange(lastSystemVersion);
                if (!needsUpdate)
                    return;

                var blobs       = chunk.GetNativeArray(blobHandle);
                int minVertices = int.MaxValue;
                int maxVertices = int.MinValue;

                for (int i = 0; i < chunk.Count; i++)
                {
                    int c       = blobs[i].skinningBlob.Value.verticesToSkin.Length;
                    minVertices = math.min(minVertices, c);
                    maxVertices = math.max(maxVertices, c);
                }

                //CheckVertexCountMismatch(minVertices, maxVertices);

                var metadata = chunk.GetChunkComponentData(metaHandle);
                if (metadata.verticesPerMesh != maxVertices || metadata.entitiesInChunk != chunk.Count)
                {
                    metadata.verticesPerMesh = maxVertices;
                    metadata.entitiesInChunk = chunk.Count;
                    chunk.SetChunkComponentData(metaHandle, metadata);
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckVertexCountMismatch(int min, int max)
            {
                if (min != max)
                    UnityEngine.Debug.LogWarning(
                        "A chunk contains multiple Mesh Skinning Blobs with different vertex counts. Because Mesh Skinning Blobs are tied to their RenderMesh of which there is only one per chunk, this is likely a bug. Did you forget to change the Mesh Skinning Blob Reference when changing a Render Mesh?");
            }
        }
    }
   }
 */

