using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class ResetPerFrameSkinningMetadataJob : SubSystem
    {
        EntityQuery m_skeletonQuery;

        protected override void OnCreate()
        {
            m_skeletonQuery = Fluent.WithAll<PerFrameSkeletonBufferMetadata>().Build();

            worldBlackboardEntity.AddCollectionComponent(new BoneMatricesPerFrameBuffersManager
            {
                boneMatricesBuffers = new System.Collections.Generic.List<UnityEngine.ComputeBuffer>()
            });
        }

        protected override void OnUpdate()
        {
            Dependency = new ResetPerFrameMetadataJob
            {
                handle            = GetComponentTypeHandle<PerFrameSkeletonBufferMetadata>(false),
                lastSystemVersion = LastSystemVersion
            }.ScheduleParallel(m_skeletonQuery, Dependency);

            worldBlackboardEntity.GetCollectionComponent<BoneMatricesPerFrameBuffersManager>().boneMatricesBuffers.Clear();
        }

        [BurstCompile]
        struct ResetPerFrameMetadataJob : IJobEntityBatch
        {
            public ComponentTypeHandle<PerFrameSkeletonBufferMetadata> handle;
            public uint                                                lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (batchInChunk.DidChange(handle, lastSystemVersion))
                {
                    var metadata = batchInChunk.GetNativeArray(handle);
                    for (int i = 0; i < batchInChunk.Count; i++)
                    {
                        metadata[i] = new PerFrameSkeletonBufferMetadata { bufferId = -1, startIndexInBuffer = -1 };
                    }
                }
            }
        }
    }
}

