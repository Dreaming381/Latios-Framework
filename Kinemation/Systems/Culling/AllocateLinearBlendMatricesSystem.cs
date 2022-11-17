using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

// This system doesn't actually allocate the compute buffer.
// Doing so now would introduce a sync point.
// This system just calculates the required size and distributes instance shader properties.
namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AllocateLinearBlendMatricesSystem : ISystem
    {
        EntityQuery          m_query;
        EntityQuery          m_metaQuery;
        LatiosWorldUnmanaged latiosWorld;

        GatherChunkSumsJob                m_gatherJob;
        ChunkPrefixSumJob                 m_metaJob;
        AssignLinearBlendMatrixOffsetsJob m_assignJob;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_query = state.Fluent().WithAll<LinearBlendSkinningShaderIndex>().WithAll<MeshSkinningBlobReference>(true)
                      .WithAll<ChunkLinearBlendSkinningMemoryMetadata>(false, true).Build();
            m_metaQuery = state.Fluent().WithAll<ChunkHeader>(true).WithAll<ChunkLinearBlendSkinningMemoryMetadata>().Without<ChunkCopySkinShaderData>().Build();

            latiosWorld.worldBlackboardEntity.AddComponent<MaxRequiredLinearBlendMatrices>();

            m_gatherJob = new GatherChunkSumsJob
            {
                perCameraMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                perFrameMaskHandle  = state.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                metaHandle          = state.GetComponentTypeHandle<ChunkLinearBlendSkinningMemoryMetadata>(),
                meshHandle          = state.GetComponentTypeHandle<MeshSkinningBlobReference>(true)
            };

            m_metaJob = new ChunkPrefixSumJob
            {
                perCameraMaskHandle                  = m_gatherJob.perCameraMaskHandle,
                perFrameMaskHandle                   = m_gatherJob.perFrameMaskHandle,
                chunkHeaderHandle                    = state.GetComponentTypeHandle<ChunkHeader>(true),
                metaHandle                           = m_gatherJob.metaHandle,
                materialMaskHandle                   = state.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(),
                maxRequiredLinearBlendMatricesLookup = state.GetComponentLookup<MaxRequiredLinearBlendMatrices>(),
                worldBlackboardEntity                = latiosWorld.worldBlackboardEntity
            };

            m_assignJob = new AssignLinearBlendMatrixOffsetsJob
            {
                perCameraMaskHandle = m_metaJob.perCameraMaskHandle,
                perFrameMaskHandle  = m_metaJob.perFrameMaskHandle,
                metaHandle          = m_metaJob.metaHandle,
                meshHandle          = m_gatherJob.meshHandle,
                indicesHandle       = state.GetComponentTypeHandle<LinearBlendSkinningShaderIndex>()
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int linearBlendIndex = latiosWorld.worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>().AsNativeArray()
                                   .IndexOf(ComponentType.ReadOnly<LinearBlendSkinningShaderIndex>());
            ulong linearBlendMaterialMaskLower = (ulong)linearBlendIndex >= 64UL ? 0UL : (1UL << linearBlendIndex);
            ulong linearBlendMaterialMaskUpper = (ulong)linearBlendIndex >= 64UL ? (1UL << (linearBlendIndex - 64)) : 0UL;

            m_gatherJob.perCameraMaskHandle.Update(ref state);
            m_gatherJob.perFrameMaskHandle.Update(ref state);
            m_gatherJob.meshHandle.Update(ref state);
            m_gatherJob.metaHandle.Update(ref state);

            state.Dependency = m_gatherJob.ScheduleParallelByRef(m_query, state.Dependency);

            m_metaJob.metaHandle.Update(ref state);
            m_metaJob.perCameraMaskHandle.Update(ref state);
            m_metaJob.perFrameMaskHandle.Update(ref state);
            m_metaJob.chunkHeaderHandle.Update(ref state);
            m_metaJob.materialMaskHandle.Update(ref state);
            m_metaJob.maxRequiredLinearBlendMatricesLookup.Update(ref state);

            m_metaJob.changedChunkList             = state.WorldUnmanaged.UpdateAllocator.AllocateNativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering());
            m_metaJob.linearBlendMaterialMaskLower = linearBlendMaterialMaskLower;
            m_metaJob.linearBlendMaterialMaskUpper = linearBlendMaterialMaskUpper;

            state.Dependency = m_metaJob.Schedule(m_metaQuery, state.Dependency);

            m_assignJob.perCameraMaskHandle.Update(ref state);
            m_assignJob.perFrameMaskHandle.Update(ref state);
            m_assignJob.metaHandle.Update(ref state);
            m_assignJob.meshHandle.Update(ref state);
            m_assignJob.indicesHandle.Update(ref state);

            m_assignJob.changedChunks = m_metaJob.changedChunkList.AsDeferredJobArray();

            state.Dependency = m_assignJob.ScheduleByRef(m_metaJob.changedChunkList, 1, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct GatherChunkSumsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>   perCameraMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<MeshSkinningBlobReference>   meshHandle;
            public ComponentTypeHandle<ChunkLinearBlendSkinningMemoryMetadata> metaHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var cameraMask = chunk.GetChunkComponentData(perCameraMaskHandle);
                var frameMask  = chunk.GetChunkComponentData(perFrameMaskHandle);
                var lower      = cameraMask.lower.Value & (~frameMask.lower.Value);
                var upper      = cameraMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return; // The masks get re-checked in ChunkPrefixSumJob so we can quit now.

                var meshArray  = chunk.GetNativeArray(meshHandle);
                int count      = 0;
                var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.ChunkEntityCount);
                while (enumerator.NextEntityIndex(out int i))
                {
                    count += meshArray[i].blob.Value.bindPoses.Length;
                }
                chunk.SetChunkComponentData(metaHandle, new ChunkLinearBlendSkinningMemoryMetadata { bonesStartPrefixSum = count });
            }
        }

        // Schedule single
        [BurstCompile]
        struct ChunkPrefixSumJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>   perCameraMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>                 chunkHeaderHandle;
            public ComponentTypeHandle<ChunkLinearBlendSkinningMemoryMetadata> metaHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>         materialMaskHandle;
            public ComponentLookup<MaxRequiredLinearBlendMatrices>             maxRequiredLinearBlendMatricesLookup;
            public Entity                                                      worldBlackboardEntity;
            public NativeList<ArchetypeChunk>                                  changedChunkList;
            public ulong                                                       linearBlendMaterialMaskLower;
            public ulong                                                       linearBlendMaterialMaskUpper;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var prefixSum = maxRequiredLinearBlendMatricesLookup[worldBlackboardEntity].matricesCount;

                var cameraMaskArray   = chunk.GetNativeArray(perCameraMaskHandle);
                var frameMaskArray    = chunk.GetNativeArray(perFrameMaskHandle);
                var headerArray       = chunk.GetNativeArray(chunkHeaderHandle);
                var metaArray         = chunk.GetNativeArray(metaHandle);
                var materialMaskArray = chunk.GetNativeArray(materialMaskHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var cameraMask = cameraMaskArray[i];
                    var frameMask  = frameMaskArray[i];
                    var lower      = cameraMask.lower.Value & (~frameMask.lower.Value);
                    var upper      = cameraMask.upper.Value & (~frameMask.upper.Value);
                    if ((upper | lower) == 0)
                        continue;

                    changedChunkList.Add(headerArray[i].ArchetypeChunk);
                    var materialMask          = materialMaskArray[i];
                    materialMask.lower.Value |= linearBlendMaterialMaskLower;
                    materialMask.upper.Value |= linearBlendMaterialMaskUpper;
                    materialMaskArray[i]      = materialMask;

                    var meta                  = metaArray[i];
                    var chunkCount            = meta.bonesStartPrefixSum;
                    meta.bonesStartPrefixSum  = prefixSum;
                    prefixSum                += chunkCount;
                    metaArray[i]              = meta;
                }
                maxRequiredLinearBlendMatricesLookup[worldBlackboardEntity] = new MaxRequiredLinearBlendMatrices { matricesCount = prefixSum };
            }
        }

        [BurstCompile]
        struct AssignLinearBlendMatrixOffsetsJob : IJobParallelForDefer
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>              perCameraMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>               perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkLinearBlendSkinningMemoryMetadata> metaHandle;
            [ReadOnly] public ComponentTypeHandle<MeshSkinningBlobReference>              meshHandle;
            [ReadOnly] public NativeArray<ArchetypeChunk>                                 changedChunks;

            public ComponentTypeHandle<LinearBlendSkinningShaderIndex> indicesHandle;

            public void Execute(int index)
            {
                var chunk = changedChunks[index];

                var metadata   = chunk.GetChunkComponentData(metaHandle);
                var cameraMask = chunk.GetChunkComponentData(perCameraMaskHandle);
                var frameMask  = chunk.GetChunkComponentData(perFrameMaskHandle);
                var lower      = new BitField64(cameraMask.lower.Value & (~frameMask.lower.Value));
                var upper      = new BitField64(cameraMask.upper.Value & (~frameMask.upper.Value));

                var meshArray = chunk.GetNativeArray(meshHandle);
                var indices   = chunk.GetNativeArray(indicesHandle).Reinterpret<uint>();
                int prefixSum = metadata.bonesStartPrefixSum;

                for (int i = lower.CountTrailingZeros(); i < 64; lower.SetBits(i, false), i = lower.CountTrailingZeros())
                {
                    indices[i]  = (uint)prefixSum;
                    prefixSum  += meshArray[i].blob.Value.bindPoses.Length;
                }

                for (int i = upper.CountTrailingZeros(); i < 64; upper.SetBits(i, false), i = upper.CountTrailingZeros())
                {
                    indices[i + 64]  = (uint)prefixSum;
                    prefixSum       += meshArray[i + 64].blob.Value.bindPoses.Length;
                }
            }
        }
    }
}

