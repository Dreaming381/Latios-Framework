using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ResetPerFrameSkinningMetadataJob : ISystem
    {
        EntityQuery          m_skeletonQuery;
        LatiosWorldUnmanaged latiosWorld;

        ComponentTypeHandle<PerFrameSkeletonBufferMetadata> m_handle;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld     = state.GetLatiosWorldUnmanaged();
            m_skeletonQuery = state.Fluent().WithAll<PerFrameSkeletonBufferMetadata>().Build();

            latiosWorld.worldBlackboardEntity.AddManagedStructComponent(new BoneMatricesPerFrameBuffersManager
            {
                boneMatricesBuffers = new System.Collections.Generic.List<UnityEngine.ComputeBuffer>()
            });

            m_handle = state.GetComponentTypeHandle<PerFrameSkeletonBufferMetadata>(false);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        //[BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            latiosWorld.worldBlackboardEntity.SetComponentData(new MaxRequiredDeformVertices { verticesCount      = 0 });
            latiosWorld.worldBlackboardEntity.SetComponentData(new MaxRequiredLinearBlendMatrices { matricesCount = 0 });
            m_handle.Update(ref state);
            state.Dependency = new ResetPerFrameMetadataJob
            {
                handle            = m_handle,
                lastSystemVersion = state.LastSystemVersion
            }.ScheduleParallel(m_skeletonQuery, state.Dependency);

            latiosWorld.worldBlackboardEntity.GetManagedStructComponent<BoneMatricesPerFrameBuffersManager>().boneMatricesBuffers.Clear();
        }

        [BurstCompile]
        struct ResetPerFrameMetadataJob : IJobChunk
        {
            public ComponentTypeHandle<PerFrameSkeletonBufferMetadata> handle;
            public uint                                                lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.DidChange(ref handle, lastSystemVersion))
                {
                    var metadata = chunk.GetNativeArray(ref handle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        metadata[i] = new PerFrameSkeletonBufferMetadata { bufferId = -1, startIndexInBuffer = -1 };
                    }
                }
            }
        }
    }
}

