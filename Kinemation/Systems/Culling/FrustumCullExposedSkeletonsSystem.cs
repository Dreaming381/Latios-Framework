using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public class FrustumCullExposedSkeletonsSystem : SubSystem
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = Fluent.WithAll<DependentSkinnedMesh>(true).WithAll<ExposedSkeletonCullingIndex>(true)
                      .WithAll<ChunkPerCameraSkeletonCullingMask>(false, true).Build();
        }

        protected override void OnUpdate()
        {
            // Todo: We only need the max index, so we may want to store that in an ICD instead.
            var exposedCullingIndexManager = worldBlackboardEntity.GetCollectionComponent<ExposedCullingIndexManager>(true, out var cullingIndexJH);
            var boundsArrays               = worldBlackboardEntity.GetCollectionComponent<ExposedSkeletonBoundsArrays>(true);
            cullingIndexJH.Complete();

            var planesBuffer = worldBlackboardEntity.GetBuffer<CullingPlane>(true);
            var unmanaged    = World.Unmanaged;
            var planes       = CullingUtilities.BuildSOAPlanePackets(planesBuffer.Reinterpret<UnityEngine.Plane>().AsNativeArray(), ref unmanaged);

            var perThreadBitArrays = unmanaged.UpdateAllocator.AllocateNativeArray<UnsafeBitArray>(JobsUtility.MaxJobThreadCount);
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
                perThreadBitArrays[i] = default;

            Dependency = new CullExposedBoundsJob
            {
                aabbs              = boundsArrays.allAabbs.AsDeferredJobArray(),
                batchAabbs         = boundsArrays.batchedAabbs.AsDeferredJobArray(),
                planePackets       = planes,
                maxBitIndex        = exposedCullingIndexManager.maxIndex,
                perThreadBitArrays = perThreadBitArrays,
                allocator          = unmanaged.UpdateAllocator.ToAllocator
            }.ScheduleBatch(exposedCullingIndexManager.maxIndex.Value + 1, 32, Dependency);

            Dependency = new CollapseBitsJob
            {
                perThreadBitArrays = perThreadBitArrays,
            }.Schedule(Dependency);

            Dependency = new SkeletonCullingJob
            {
                chunkMaskHandle    = GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(false),
                cullingIndexHandle = GetComponentTypeHandle<ExposedSkeletonCullingIndex>(true),
                perThreadBitArrays = perThreadBitArrays
            }.ScheduleParallel(m_query, Dependency);
        }

        // Todo: Is it worth iterating over meta chunks?
        [BurstCompile]
        struct CullExposedBoundsJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<AABB>                                      aabbs;
            [ReadOnly] public NativeArray<AABB>                                      batchAabbs;
            [ReadOnly] public NativeArray<FrustumPlanes.PlanePacket4>                planePackets;
            [ReadOnly] public NativeReference<int>                                   maxBitIndex;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeBitArray> perThreadBitArrays;
            public Allocator                                                         allocator;

            [NativeSetThreadIndex] int m_NativeThreadIndex;

            public void Execute(int startIndex, int count)
            {
                var cullType = FrustumPlanes.Intersect2(planePackets, batchAabbs[startIndex / 32]);
                if (cullType == FrustumPlanes.IntersectResult.Out)
                {
                    return;
                }

                var perThreadBitArray = perThreadBitArrays[m_NativeThreadIndex];
                if (!perThreadBitArray.IsCreated)
                {
                    perThreadBitArray = new UnsafeBitArray(CollectionHelper.Align(maxBitIndex.Value + 1, 64),
                                                           allocator,
                                                           NativeArrayOptions.ClearMemory);
                    perThreadBitArrays[m_NativeThreadIndex] = perThreadBitArray;
                }

                if (cullType == FrustumPlanes.IntersectResult.In)
                {
                    for (int i = 0; i < count; i++)
                    {
                        perThreadBitArray.Set(startIndex + i, true);
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        bool bit  = perThreadBitArray.IsSet(startIndex + i);
                        bit      |= FrustumPlanes.Intersect2NoPartial(planePackets, aabbs[startIndex + i]) == FrustumPlanes.IntersectResult.In;
                        perThreadBitArray.Set(startIndex + i, bit);
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct CollapseBitsJob : IJob
        {
            public NativeArray<UnsafeBitArray> perThreadBitArrays;

            public void Execute()
            {
                int startFrom = -1;
                for (int i = 0; i < perThreadBitArrays.Length; i++)
                {
                    if (perThreadBitArrays[i].IsCreated)
                    {
                        startFrom             = i + 1;
                        perThreadBitArrays[0] = perThreadBitArrays[i];
                        perThreadBitArrays[i] = default;
                        break;
                    }
                }

                if (startFrom == -1)
                {
                    // This happens if chunk culling removes all bones. Unlikely but possible.
                    // In this case, we will need to check for this in future jobs.
                    return;
                }

                for (int arrayIndex = startFrom; arrayIndex < perThreadBitArrays.Length; arrayIndex++)
                {
                    if (!perThreadBitArrays[arrayIndex].IsCreated)
                        continue;
                    var dstArray    = perThreadBitArrays[0];
                    var dstArrayPtr = dstArray.Ptr;
                    var srcArrayPtr = perThreadBitArrays[arrayIndex].Ptr;

                    for (int i = 0, bitCount = 0; bitCount < dstArray.Length; i++, bitCount += 64)
                    {
                        dstArrayPtr[i] |= srcArrayPtr[i];
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct SkeletonCullingJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<ExposedSkeletonCullingIndex> cullingIndexHandle;
            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>      chunkMaskHandle;

            [ReadOnly] public NativeArray<UnsafeBitArray> perThreadBitArrays;

            public void Execute(ArchetypeChunk chunk, int chunkIndex)
            {
                if (!perThreadBitArrays[0].IsCreated)
                {
                    chunk.SetChunkComponentData(chunkMaskHandle, default);
                    return;
                }

                var cullingIndices = chunk.GetNativeArray(cullingIndexHandle);

                BitField64 maskWordLower;
                maskWordLower.Value = 0;
                for (int i = 0; i < math.min(64, chunk.Count); i++)
                {
                    bool isIn            = perThreadBitArrays[0].IsSet(cullingIndices[i].cullingIndex);
                    maskWordLower.Value |= math.select(0ul, 1ul, isIn) << i;
                }
                BitField64 maskWordUpper;
                maskWordUpper.Value = 0;
                for (int i = 0; i < math.max(0, chunk.Count - 64); i++)
                {
                    bool isIn            = perThreadBitArrays[0].IsSet(cullingIndices[i + 64].cullingIndex);
                    maskWordUpper.Value |= math.select(0ul, 1ul, isIn) << i;
                }

                chunk.SetChunkComponentData(chunkMaskHandle, new ChunkPerCameraSkeletonCullingMask { lower = maskWordLower, upper = maskWordUpper });
            }
        }
    }
}

