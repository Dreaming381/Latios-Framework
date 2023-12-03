using Latios.Kinemation.InternalSourceGen;
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
        public void OnDestroy(ref SystemState state)
        {
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
                blendShapeStateHandle                   = GetComponentTypeHandle<BlendShapeState>(true),
                blendShapeWeightHandle                  = GetBufferTypeHandle<BlendShapeWeight>(true),
                chunkHeaderHandle                       = GetComponentTypeHandle<ChunkHeader>(true),
                currentDeformHandle                     = GetComponentTypeHandle<CurrentDeformShaderIndex>(true),
                currentDqsVertexHandle                  = GetComponentTypeHandle<CurrentDqsVertexSkinningShaderIndex>(true),
                currentMatrixVertexHandle               = GetComponentTypeHandle<CurrentMatrixVertexSkinningShaderIndex>(true),
                deformClassificationMap                 = map.AsParallelWriter(),
                disableComputeShaderProcessingTagHandle = GetComponentTypeHandle<DisableComputeShaderProcessingTag>(true),
                dqsDeformTagHandle                      = GetComponentTypeHandle<DualQuaternionSkinningDeformTag>(true),
                dynamicMeshStateHandle                  = GetComponentTypeHandle<DynamicMeshState>(true),
                dynamicMeshVertexHandle                 = GetBufferTypeHandle<DynamicMeshVertex>(true),
                legacyComputeDeformHandle               = GetComponentTypeHandle<LegacyComputeDeformShaderIndex>(true),
                legacyDotsDeformHandle                  = GetComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>(true),
                legacyLbsHandle                         = GetComponentTypeHandle<LegacyLinearBlendSkinningShaderIndex>(true),
                previousDeformHandle                    = GetComponentTypeHandle<PreviousDeformShaderIndex>(true),
                previousDqsVertexHandle                 = GetComponentTypeHandle<PreviousDqsVertexSkinningShaderIndex>(true),
                previousMatrixVertexHandle              = GetComponentTypeHandle<PreviousMatrixVertexSkinningShaderIndex>(true),
                skeletonDependentHandle                 = GetComponentTypeHandle<SkeletonDependent>(true),
                twoAgoDeformHandle                      = GetComponentTypeHandle<TwoAgoDeformShaderIndex>(true),
                twoAgoDqsVertexHandle                   = GetComponentTypeHandle<TwoAgoDqsVertexSkinningShaderIndex>(true),
                twoAgoMatrixVertexHandle                = GetComponentTypeHandle<TwoAgoMatrixVertexSkinningShaderIndex>(true),
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkHeader> chunkHeaderHandle;

            [ReadOnly] public ComponentTypeHandle<LegacyLinearBlendSkinningShaderIndex> legacyLbsHandle;
            [ReadOnly] public ComponentTypeHandle<LegacyComputeDeformShaderIndex>       legacyComputeDeformHandle;
            [ReadOnly] public ComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>    legacyDotsDeformHandle;

            [ReadOnly] public ComponentTypeHandle<CurrentMatrixVertexSkinningShaderIndex>  currentMatrixVertexHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousMatrixVertexSkinningShaderIndex> previousMatrixVertexHandle;
            [ReadOnly] public ComponentTypeHandle<TwoAgoMatrixVertexSkinningShaderIndex>   twoAgoMatrixVertexHandle;
            [ReadOnly] public ComponentTypeHandle<CurrentDqsVertexSkinningShaderIndex>     currentDqsVertexHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousDqsVertexSkinningShaderIndex>    previousDqsVertexHandle;
            [ReadOnly] public ComponentTypeHandle<TwoAgoDqsVertexSkinningShaderIndex>      twoAgoDqsVertexHandle;

            [ReadOnly] public ComponentTypeHandle<CurrentDeformShaderIndex>  currentDeformHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousDeformShaderIndex> previousDeformHandle;
            [ReadOnly] public ComponentTypeHandle<TwoAgoDeformShaderIndex>   twoAgoDeformHandle;

            [ReadOnly] public ComponentTypeHandle<SkeletonDependent> skeletonDependentHandle;
            [ReadOnly] public ComponentTypeHandle<BlendShapeState>   blendShapeStateHandle;
            [ReadOnly] public BufferTypeHandle<BlendShapeWeight>     blendShapeWeightHandle;
            [ReadOnly] public ComponentTypeHandle<DynamicMeshState>  dynamicMeshStateHandle;
            [ReadOnly] public BufferTypeHandle<DynamicMeshVertex>    dynamicMeshVertexHandle;

            [ReadOnly] public ComponentTypeHandle<DisableComputeShaderProcessingTag> disableComputeShaderProcessingTagHandle;

            [ReadOnly] public ComponentTypeHandle<DualQuaternionSkinningDeformTag> dqsDeformTagHandle;

            public NativeParallelHashMap<ArchetypeChunk, DeformClassification>.ParallelWriter deformClassificationMap;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunks = metaChunk.GetNativeArray(ref chunkHeaderHandle);

                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var                  chunk          = chunks[i].ArchetypeChunk;
                    DeformClassification classification = default;

                    bool hasSkeleton    = chunk.Has(ref skeletonDependentHandle);
                    bool hasBlendShapes = chunk.Has(ref blendShapeStateHandle) && chunk.Has(ref blendShapeWeightHandle);
                    bool hasDynamicMesh = chunk.Has(ref dynamicMeshStateHandle) && chunk.Has(ref dynamicMeshVertexHandle);

                    if (chunk.Has(ref legacyLbsHandle))
                        classification |= DeformClassification.LegacyLbs;
                    if (chunk.Has(ref legacyComputeDeformHandle))
                        classification |= DeformClassification.LegacyCompute;
                    if (chunk.Has(ref legacyDotsDeformHandle))
                        classification |= DeformClassification.LegacyDotsDefom;
                    if (chunk.Has(ref currentMatrixVertexHandle))
                        classification |= DeformClassification.CurrentVertexMatrix;
                    if (chunk.Has(ref previousMatrixVertexHandle))
                        classification |= DeformClassification.PreviousVertexMatrix;
                    if (chunk.Has(ref twoAgoMatrixVertexHandle))
                        classification |= DeformClassification.TwoAgoVertexMatrix;
                    if (chunk.Has(ref currentDqsVertexHandle))
                        classification |= DeformClassification.CurrentVertexDqs;
                    if (chunk.Has(ref previousDqsVertexHandle))
                        classification |= DeformClassification.PreviousVertexDqs;
                    if (chunk.Has(ref twoAgoDqsVertexHandle))
                        classification |= DeformClassification.TwoAgoVertexDqs;
                    if (chunk.Has(ref currentDeformHandle))
                        classification |= DeformClassification.CurrentDeform;
                    if (chunk.Has(ref previousDeformHandle))
                        classification |= DeformClassification.PreviousDeform;
                    if (chunk.Has(ref twoAgoDeformHandle))
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
                        if (chunk.Has(ref dqsDeformTagHandle))
                            classification |= DeformClassification.RequiresGpuComputeDqsSkinning;
                        else
                            classification |= DeformClassification.RequiresGpuComputeMatrixSkinning;
                    }

                    if (chunk.Has(ref disableComputeShaderProcessingTagHandle))
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

