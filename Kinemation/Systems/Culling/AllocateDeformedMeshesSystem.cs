using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// This system doesn't actually allocate the compute buffer.
// Doing so now would introduce a sync point.
// This system just calculates the required size and distributes instance shader properties.
namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class AllocateDeformedMeshesSystem : SubSystem
    {
        EntityQuery m_metaQuery;

        protected override void OnCreate()
        {
            m_metaQuery = Fluent.WithAll<ChunkHeader>(true).WithAll<ChunkComputeDeformMemoryMetadata>().Build();

            worldBlackboardEntity.AddComponent<MaxRequiredDeformVertices>();
        }

        protected override void OnUpdate()
        {
            int deformIndex = worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>().AsNativeArray()
                              .IndexOf(ComponentType.ReadOnly<ComputeDeformShaderIndex>());
            ulong deformMaterialMaskLower = (ulong)deformIndex >= 64UL ? 0UL : (1UL << deformIndex);
            ulong deformMaterialMaskUpper = (ulong)deformIndex >= 64UL ? (1UL << (deformIndex - 64)) : 0UL;

            var metaHandle          = GetComponentTypeHandle<ChunkComputeDeformMemoryMetadata>();
            var perCameraMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true);
            var perFrameMaskHandle  = GetComponentTypeHandle<ChunkPerFrameCullingMask>(true);

            var chunkList = World.UpdateAllocator.AllocateNativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering());

            Dependency = new ChunkPrefixSumJob
            {
                perCameraMaskHandle           = perCameraMaskHandle,
                perFrameMaskHandle            = perFrameMaskHandle,
                chunkHeaderHandle             = GetComponentTypeHandle<ChunkHeader>(true),
                metaHandle                    = metaHandle,
                materialMaskHandle            = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(),
                maxRequiredDeformVerticesCdfe = GetComponentDataFromEntity<MaxRequiredDeformVertices>(),
                worldBlackboardEntity         = worldBlackboardEntity,
                changedChunkList              = chunkList,
                deformMaterialMaskLower       = deformMaterialMaskLower,
                deformMaterialMaskUpper       = deformMaterialMaskUpper
            }.Schedule(m_metaQuery, Dependency);

            Dependency = new AssignComputeDeformMeshOffsetsJob
            {
                perCameraMaskHandle = perCameraMaskHandle,
                perFrameMaskHandle  = perFrameMaskHandle,
                metaHandle          = metaHandle,
                changedChunks       = chunkList.AsDeferredJobArray(),
                indicesHandle       = GetComponentTypeHandle<ComputeDeformShaderIndex>()
            }.Schedule(chunkList, 1, Dependency);
        }

        // Schedule single
        [BurstCompile]
        struct ChunkPrefixSumJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>  perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               chunkHeaderHandle;
            public ComponentTypeHandle<ChunkComputeDeformMemoryMetadata>     metaHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>       materialMaskHandle;
            public ComponentDataFromEntity<MaxRequiredDeformVertices>        maxRequiredDeformVerticesCdfe;
            public Entity                                                    worldBlackboardEntity;
            public NativeList<ArchetypeChunk>                                changedChunkList;
            public ulong                                                     deformMaterialMaskLower;
            public ulong                                                     deformMaterialMaskUpper;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var prefixSum = maxRequiredDeformVerticesCdfe[worldBlackboardEntity].verticesCount;

                var cameraMaskArray   = batchInChunk.GetNativeArray(perCameraMaskHandle);
                var frameMaskArray    = batchInChunk.GetNativeArray(perFrameMaskHandle);
                var headerArray       = batchInChunk.GetNativeArray(chunkHeaderHandle);
                var metaArray         = batchInChunk.GetNativeArray(metaHandle);
                var materialMaskArray = batchInChunk.GetNativeArray(materialMaskHandle);

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var cameraMask = cameraMaskArray[i];
                    var frameMask  = frameMaskArray[i];
                    var lower      = cameraMask.lower.Value & (~frameMask.lower.Value);
                    var upper      = cameraMask.upper.Value & (~frameMask.upper.Value);
                    if ((upper | lower) == 0)
                        continue;

                    changedChunkList.Add(headerArray[i].ArchetypeChunk);
                    var materialMask          = materialMaskArray[i];
                    materialMask.lower.Value |= deformMaterialMaskLower;
                    materialMask.upper.Value |= deformMaterialMaskUpper;
                    materialMaskArray[i]      = materialMask;

                    var meta = metaArray[i];

                    meta.vertexStartPrefixSum  = prefixSum;
                    var newEntities            = math.countbits(lower) + math.countbits(upper);
                    prefixSum                 += newEntities * meta.verticesPerMesh;
                    metaArray[i]               = meta;
                }
                maxRequiredDeformVerticesCdfe[worldBlackboardEntity] = new MaxRequiredDeformVertices { verticesCount = prefixSum };
            }
        }

        [BurstCompile]
        struct AssignComputeDeformMeshOffsetsJob : IJobParallelForDefer
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>        perCameraMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>         perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkComputeDeformMemoryMetadata> metaHandle;
            [ReadOnly] public NativeArray<ArchetypeChunk>                           changedChunks;

            public ComponentTypeHandle<ComputeDeformShaderIndex> indicesHandle;

            public void Execute(int index)
            {
                var batchInChunk = changedChunks[index];

                var metadata   = batchInChunk.GetChunkComponentData(metaHandle);
                var cameraMask = batchInChunk.GetChunkComponentData(perCameraMaskHandle);
                var frameMask  = batchInChunk.GetChunkComponentData(perFrameMaskHandle);
                var lower      = new BitField64(cameraMask.lower.Value & (~frameMask.lower.Value));
                var upper      = new BitField64(cameraMask.upper.Value & (~frameMask.upper.Value));

                var indices   = batchInChunk.GetNativeArray(indicesHandle).Reinterpret<uint>();
                int prefixSum = metadata.vertexStartPrefixSum;

                for (int i = lower.CountTrailingZeros(); i < 64; lower.SetBits(i, false), i = lower.CountTrailingZeros())
                {
                    indices[i]  = (uint)prefixSum;
                    prefixSum  += metadata.verticesPerMesh;
                }

                for (int i = upper.CountTrailingZeros(); i < 64; upper.SetBits(i, false), i = upper.CountTrailingZeros())
                {
                    indices[i + 64]  = (uint)prefixSum;
                    prefixSum       += metadata.verticesPerMesh;
                }
            }
        }
    }
}

