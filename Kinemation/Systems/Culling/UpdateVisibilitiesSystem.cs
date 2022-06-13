using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

// Unlike the LOD system, Unity's culling implementation is much more sensible.
// There's a couple of oddities that I have corrected, such as using Intersect2NoPartial in opportune locations
// and using Temp memory for the ThreadLocalIndexLists.
// But otherwise, most modifications are for shoehorning the skinning flags.
namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class UpdateVisibilitiesSystem : SubSystem
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = Fluent.WithAll<ChunkWorldRenderBounds>(true).WithAll<HybridChunkInfo>(false).WithAll<ChunkHeader>(true).Build();
        }

        protected unsafe override void OnUpdate()
        {
            var brgCullingContext = worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>();

            Dependency = new ZeroVisibleCountsJob
            {
                Batches = brgCullingContext.cullingContext.batchVisibility
            }.ScheduleParallel(brgCullingContext.cullingContext.batchVisibility.Length, 16, Dependency);

            Dependency = new WriteVisibilitiesJob
            {
                internalToExternalRemappingTable = brgCullingContext.internalToExternalMappingIds,
                chunkHeaderHandle                = GetComponentTypeHandle<ChunkHeader>(true),
                hybridChunkInfoHandle            = GetComponentTypeHandle<HybridChunkInfo>(false),
                perCameraMaskHandle              = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                perFrameMaskHandle               = GetComponentTypeHandle<ChunkPerFrameCullingMask>(false),
                batches                          = brgCullingContext.cullingContext.batchVisibility,
                indexList                        = brgCullingContext.cullingContext.visibleIndices
            }.ScheduleParallel(m_query, Dependency);
        }

        [BurstCompile]
        unsafe struct ZeroVisibleCountsJob : IJobFor
        {
            public NativeArray<BatchVisibility> Batches;

            public void Execute(int index)
            {
                // Can't set individual fields of structs inside NativeArray, so do it via raw pointer
                ((BatchVisibility*)Batches.GetUnsafePtr())[index].visibleCount = 0;
            }
        }

        [BurstCompile]
        unsafe struct WriteVisibilitiesJob : IJobEntityBatch
        {
            [ReadOnly] public NativeArray<int> internalToExternalRemappingTable;

            [ReadOnly] public ComponentTypeHandle<ChunkHeader>    chunkHeaderHandle;
            public ComponentTypeHandle<HybridChunkInfo>           hybridChunkInfoHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraMaskHandle;
            public ComponentTypeHandle<ChunkPerFrameCullingMask>  perFrameMaskHandle;

            [NativeDisableParallelForRestriction] public NativeArray<int>             indexList;
            [NativeDisableParallelForRestriction] public NativeArray<BatchVisibility> batches;

            public void Execute(ArchetypeChunk archetypeChunk, int chunkIndex)
            {
                var hybridChunkInfoArray = archetypeChunk.GetNativeArray(hybridChunkInfoHandle);
                var chunkHeaderArray     = archetypeChunk.GetNativeArray(chunkHeaderHandle);
                var perCameraMaskArray   = archetypeChunk.GetNativeArray(perCameraMaskHandle);
                var perFrameMaskArray    = archetypeChunk.GetNativeArray(perFrameMaskHandle);

                for (var metaIndex = 0; metaIndex < archetypeChunk.Count; metaIndex++)
                {
                    var perCameraMask              = perCameraMaskArray[metaIndex];
                    var perFrameMask               = perFrameMaskArray[metaIndex];
                    perFrameMask.lower.Value      |= perCameraMask.lower.Value;
                    perFrameMask.upper.Value      |= perCameraMask.upper.Value;
                    perFrameMaskArray[metaIndex]   = perFrameMask;
                    perCameraMaskArray[metaIndex]  = default;

                    var hybridChunkInfo = hybridChunkInfoArray[metaIndex];
                    if (!hybridChunkInfo.Valid)
                        continue;

                    var chunkHeader = chunkHeaderArray[metaIndex];

                    int internalBatchIndex = hybridChunkInfo.InternalIndex;
                    int externalBatchIndex = internalToExternalRemappingTable[internalBatchIndex];

                    int chunkOutputOffset    = 0;
                    int instanceOutputOffset = 0;

                    ref var chunkCullingData = ref hybridChunkInfo.CullingData;

                    var pBatch = ((BatchVisibility*)batches.GetUnsafePtr()) + externalBatchIndex;

                    int batchOutputOffset      = pBatch->offset;
                    int processedInstanceCount = chunkCullingData.BatchOffset;

                    var chunkInstanceCount = chunkHeader.ArchetypeChunk.Count;
                    var anyVisible         = (perCameraMask.upper.Value | perCameraMask.lower.Value) != 0;

                    if (anyVisible)
                    {
                        // Since the chunk is fully in, we can easily count how many instances we will output
                        int chunkOutputCount = perCameraMask.lower.CountBits() + perCameraMask.upper.CountBits();

                        chunkOutputOffset = System.Threading.Interlocked.Add(
                            ref pBatch->visibleCount, chunkOutputCount) - chunkOutputCount;

                        chunkOutputOffset += batchOutputOffset;

                        for (int j = 0; j < 2; j++)
                        {
                            var lodWord = math.select(perCameraMask.lower.Value, perCameraMask.upper.Value, j == 1);

                            while (lodWord != 0)
                            {
                                var bitIndex                                        = math.tzcnt(lodWord);
                                var finalIndex                                      = (j << 6) + bitIndex;
                                indexList[chunkOutputOffset + instanceOutputOffset] =
                                    processedInstanceCount + finalIndex;

                                instanceOutputOffset += 1;
                                lodWord              ^= 1ul << bitIndex;
                            }
                        }
                    }
                    chunkCullingData.StartIndex     = (short)chunkOutputOffset;
                    chunkCullingData.Visible        = (short)instanceOutputOffset;
                    hybridChunkInfoArray[metaIndex] = hybridChunkInfo;
                }
            }
        }
    }
}

