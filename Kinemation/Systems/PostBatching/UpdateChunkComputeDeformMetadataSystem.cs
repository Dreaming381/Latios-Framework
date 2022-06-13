using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class UpdateChunkComputeDeformMetadataSystem : SubSystem
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = Fluent.WithAll<SkeletonDependent>(true).WithAll<ChunkComputeDeformMemoryMetadata>(false, true).Build();
        }

        protected override void OnUpdate()
        {
            worldBlackboardEntity.SetComponentData(new MaxRequiredDeformVertices { verticesCount = 0 });

            var lastSystemVersion = LastSystemVersion;
            var blobHandle        = GetComponentTypeHandle<SkeletonDependent>(true);
            var metaHandle        = GetComponentTypeHandle<ChunkComputeDeformMemoryMetadata>(false);

            Dependency = new UpdateChunkVertexCountsJob
            {
                blobHandle        = blobHandle,
                metaHandle        = metaHandle,
                lastSystemVersion = lastSystemVersion
            }.ScheduleParallel(m_query, Dependency);
        }

        [BurstCompile]
        struct UpdateChunkVertexCountsJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>     blobHandle;
            public ComponentTypeHandle<ChunkComputeDeformMemoryMetadata> metaHandle;
            public uint                                                  lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                bool needsUpdate  = batchInChunk.DidChange(blobHandle, lastSystemVersion);
                needsUpdate      |= batchInChunk.DidOrderChange(lastSystemVersion);
                if (!needsUpdate)
                    return;

                var blobs       = batchInChunk.GetNativeArray(blobHandle);
                int minVertices = int.MaxValue;
                int maxVertices = int.MinValue;

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    int c       = blobs[i].skinningBlob.Value.verticesToSkin.Length;
                    minVertices = math.min(minVertices, c);
                    maxVertices = math.max(maxVertices, c);
                }

                CheckVertexCountMismatch(minVertices, maxVertices);

                var metadata = batchInChunk.GetChunkComponentData(metaHandle);
                if (metadata.verticesPerMesh != maxVertices || metadata.entitiesInChunk != batchInChunk.Count)
                {
                    metadata.verticesPerMesh = maxVertices;
                    metadata.entitiesInChunk = batchInChunk.Count;
                    batchInChunk.SetChunkComponentData(metaHandle, metadata);
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckVertexCountMismatch(int min, int max)
            {
                if (min != max)
                    UnityEngine.Debug.LogWarning(
                        "A chunk contains multiple Mesh Skinning Blobs. Because Mesh Skinning Blobs are tied to their RenderMesh of which there is only one per chunk, this is likely a bug. Did you forget to change the Mesh Skinning Blob Reference when changing a Render Mesh?");
            }
        }
    }
}

