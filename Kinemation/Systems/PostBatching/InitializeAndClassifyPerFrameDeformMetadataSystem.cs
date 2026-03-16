using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct InitializeAndClassifyPerFrameDeformMetadataSystem : ISystem
    {
        EntityQuery          m_query;
        LatiosWorldUnmanaged latiosWorld;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().With<ChunkHeader>(true).WithAnyEnabled<ChunkDeformPrefixSums>(true).WithAnyEnabled<ChunkCopyDeformTag>(true).Build();

            latiosWorld.worldBlackboardEntity.AddComponent<MaxRequiredDeformData>();
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new DeformClassificationMap());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            MaxRequiredDeformData maxes     = default;
            maxes.maxRequiredDeformVertices = 1;  // LegacyDotsDeformation treats index 0 as "no previous pose".
            latiosWorld.worldBlackboardEntity.SetComponentData(maxes);

            var map = new NativeParallelHashMap<ArchetypeChunk, DeformClassification>(m_query.CalculateEntityCountWithoutFiltering() * 2,
                                                                                      state.WorldUpdateAllocator);

            latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new DeformClassificationMap { deformClassificationMap = map });

            state.Dependency = new Job
            {
                chunkHeaderHandle       = GetComponentTypeHandle<ChunkHeader>(true),
                deformClassificationMap = map.AsParallelWriter(),
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkHeader> chunkHeaderHandle;

            public NativeParallelHashMap<ArchetypeChunk, DeformClassification>.ParallelWriter deformClassificationMap;

            HasChecker<LegacyLinearBlendSkinningShaderIndex> legacyLbsChecker;
            HasChecker<LegacyComputeDeformShaderIndex>       legacyComputeDeformChecker;
            HasChecker<LegacyDotsDeformParamsShaderIndex>    legacyDotsDeformChecker;

            HasChecker<CurrentMatrixVertexSkinningShaderIndex>  currentMatrixVertexChecker;
            HasChecker<PreviousMatrixVertexSkinningShaderIndex> previousMatrixVertexChecker;
            HasChecker<TwoAgoMatrixVertexSkinningShaderIndex>   twoAgoMatrixVertexChecker;
            HasChecker<CurrentDqsVertexSkinningShaderIndex>     currentDqsVertexChecker;
            HasChecker<PreviousDqsVertexSkinningShaderIndex>    previousDqsVertexChecker;
            HasChecker<TwoAgoDqsVertexSkinningShaderIndex>      twoAgoDqsVertexChecker;

            HasChecker<CurrentDeformShaderIndex>  currentDeformChecker;
            HasChecker<PreviousDeformShaderIndex> previousDeformChecker;
            HasChecker<TwoAgoDeformShaderIndex>   twoAgoDeformChecker;

            HasChecker<DisableComputeShaderProcessingTag> disableComputeShaderProcessingChecker;
            HasChecker<DualQuaternionSkinningDeformTag>   dqsDeformChecker;

            HasChecker<SkeletonDependent> skeletonDependentChecker;
            HasChecker<BlendShapeState>   blendShapeStateChecker;
            HasChecker<BlendShapeWeight>  blendShapeWeightChecker;
            HasChecker<DynamicMeshState>  dynamicMeshStateChecker;
            HasChecker<DynamicMeshVertex> dynamicMeshVertexChecker;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunks = metaChunk.GetNativeArray(ref chunkHeaderHandle);

                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var                  chunk          = chunks[i].ArchetypeChunk;
                    DeformClassification classification = default;

                    bool hasSkeleton    = skeletonDependentChecker[chunk];
                    bool hasBlendShapes = blendShapeStateChecker[chunk] && blendShapeWeightChecker[chunk];
                    bool hasDynamicMesh = dynamicMeshStateChecker[chunk] && dynamicMeshVertexChecker[chunk];

                    if (legacyLbsChecker[chunk])
                        classification |= DeformClassification.LegacyLbs;
                    if (legacyComputeDeformChecker[chunk])
                        classification |= DeformClassification.LegacyCompute;
                    if (legacyDotsDeformChecker[chunk])
                        classification |= DeformClassification.LegacyDotsDefom;
                    if (currentMatrixVertexChecker[chunk])
                        classification |= DeformClassification.CurrentVertexMatrix;
                    if (previousMatrixVertexChecker[chunk])
                        classification |= DeformClassification.PreviousVertexMatrix;
                    if (twoAgoMatrixVertexChecker[chunk])
                        classification |= DeformClassification.TwoAgoVertexMatrix;
                    if (currentDqsVertexChecker[chunk])
                        classification |= DeformClassification.CurrentVertexDqs;
                    if (previousDqsVertexChecker[chunk])
                        classification |= DeformClassification.PreviousVertexDqs;
                    if (twoAgoDqsVertexChecker[chunk])
                        classification |= DeformClassification.TwoAgoVertexDqs;
                    if (currentDeformChecker[chunk])
                        classification |= DeformClassification.CurrentDeform;
                    if (previousDeformChecker[chunk])
                        classification |= DeformClassification.PreviousDeform;
                    if (twoAgoDeformChecker[chunk])
                        classification |= DeformClassification.TwoAgoDeform;
                    var mask            = DeformClassification.LegacyCompute | DeformClassification.LegacyDotsDefom |
                                          DeformClassification.CurrentDeform | DeformClassification.PreviousDeform | DeformClassification.TwoAgoDeform;
                    bool hasDeformShader = (classification & mask) != DeformClassification.None;
                    if (hasDeformShader && hasDynamicMesh)
                        classification |= DeformClassification.RequiresUploadDynamicMesh;
                    if (hasDeformShader && hasBlendShapes)
                        classification |= DeformClassification.RequiresGpuComputeBlendShapes;
                    if (hasDeformShader && hasSkeleton)
                    {
                        if (dqsDeformChecker[chunk])
                            classification |= DeformClassification.RequiresGpuComputeDqsSkinning;
                        else
                            classification |= DeformClassification.RequiresGpuComputeMatrixSkinning;
                    }

                    if (disableComputeShaderProcessingChecker[chunk])
                    {
                        classification &= ~(DeformClassification.RequiresGpuComputeBlendShapes | DeformClassification.RequiresGpuComputeMatrixSkinning |
                                            DeformClassification.RequiresGpuComputeDqsSkinning);
                    }

                    deformClassificationMap.TryAdd(chunk, classification);
                }
            }
        }
    }
}

