using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class CopySkinWithCullingSystem : SubSystem
    {
        EntityQuery m_metaQuery;

        protected override void OnCreate()
        {
            m_metaQuery = Fluent.WithAll<ChunkWorldRenderBounds>(true).WithAll<HybridChunkInfo>(true).WithAll<ChunkHeader>(true).WithAll<ChunkPerFrameCullingMask>(true)
                          .WithAll<ChunkCopySkinShaderData>(true).WithAll<ChunkPerCameraCullingMask>(false).UseWriteGroups().Build();
        }

        protected override void OnUpdate()
        {
            Dependency = new CopySkinJob
            {
                hybridChunkInfoHandle    = GetComponentTypeHandle<HybridChunkInfo>(true),
                chunkHeaderHandle        = GetComponentTypeHandle<ChunkHeader>(true),
                chunkPerFrameMaskHandle  = GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                referenceHandle          = GetComponentTypeHandle<ShareSkinFromEntity>(true),
                entityHandle             = GetEntityTypeHandle(),
                sife                     = GetStorageInfoFromEntity(),
                chunkPerCameraMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                computeCdfe              = GetComponentDataFromEntity<ComputeDeformShaderIndex>(false),
                linearBlendCdfe          = GetComponentDataFromEntity<LinearBlendSkinningShaderIndex>(false)
            }.ScheduleParallel(m_metaQuery, Dependency);
        }

        [BurstCompile]
        unsafe struct CopySkinJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<HybridChunkInfo>          hybridChunkInfoHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>              chunkHeaderHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask> chunkPerFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ShareSkinFromEntity>      referenceHandle;
            [ReadOnly] public EntityTypeHandle                              entityHandle;

            [ReadOnly] public StorageInfoFromEntity sife;

            public ComponentTypeHandle<ChunkPerCameraCullingMask> chunkPerCameraMaskHandle;

            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<ComputeDeformShaderIndex>       computeCdfe;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<LinearBlendSkinningShaderIndex> linearBlendCdfe;

            public void Execute(ArchetypeChunk archetypeChunk, int chunkIndex)
            {
                var hybridChunkInfos = archetypeChunk.GetNativeArray(hybridChunkInfoHandle);
                var chunkHeaders     = archetypeChunk.GetNativeArray(chunkHeaderHandle);
                var chunkCameraMasks = archetypeChunk.GetNativeArray(chunkPerCameraMaskHandle);
                var chunkFrameMasks  = archetypeChunk.GetNativeArray(chunkPerFrameMaskHandle);

                for (var metaIndex = 0; metaIndex < archetypeChunk.Count; metaIndex++)
                {
                    var hybridChunkInfo = hybridChunkInfos[metaIndex];
                    if (!hybridChunkInfo.Valid)
                        continue;

                    var chunkHeader = chunkHeaders[metaIndex];

                    ref var chunkCullingData = ref hybridChunkInfo.CullingData;

                    var chunkInstanceCount    = chunkHeader.ArchetypeChunk.Count;
                    var chunkEntityLodEnabled = chunkCullingData.InstanceLodEnableds;
                    var anyLodEnabled         = (chunkEntityLodEnabled.Enabled[0] | chunkEntityLodEnabled.Enabled[1]) != 0;

                    if (anyLodEnabled)
                    {
                        // Todo: Throw error if not per-instance?
                        //var perInstanceCull = 0 != (chunkCullingData.Flags & HybridChunkCullingData.kFlagInstanceCulling);

                        var chunk = chunkHeader.ArchetypeChunk;

                        var references                 = chunk.GetNativeArray(referenceHandle);
                        var entities                   = chunk.GetNativeArray(entityHandle);
                        var invertedFrameMasks         = chunkFrameMasks[metaIndex];
                        invertedFrameMasks.lower.Value = ~invertedFrameMasks.lower.Value;
                        invertedFrameMasks.upper.Value = ~invertedFrameMasks.upper.Value;

                        var        lodWord = chunkEntityLodEnabled.Enabled[0];
                        BitField64 maskWordLower;
                        maskWordLower.Value = 0;
                        for (int i = math.tzcnt(lodWord); i < 64; lodWord ^= 1ul << i, i = math.tzcnt(lodWord))
                        {
                            bool isIn            = IsReferenceVisible(references[i].sourceSkinnedEntity, invertedFrameMasks.lower.IsSet(i), entities[i]);
                            maskWordLower.Value |= math.select(0ul, 1ul, isIn) << i;
                        }
                        lodWord = chunkEntityLodEnabled.Enabled[1];
                        BitField64 maskWordUpper;
                        maskWordUpper.Value = 0;
                        for (int i = math.tzcnt(lodWord); i < 64; lodWord ^= 1ul << i, i = math.tzcnt(lodWord))
                        {
                            bool isIn            = IsReferenceVisible(references[i + 64].sourceSkinnedEntity, invertedFrameMasks.upper.IsSet(i), entities[i + 64]);
                            maskWordUpper.Value |= math.select(0ul, 1ul, isIn) << i;
                        }

                        chunkCameraMasks[metaIndex] = new ChunkPerCameraCullingMask { lower = maskWordLower, upper = maskWordUpper };
                    }
                }
            }

            bool IsReferenceVisible(Entity reference, bool needsCopy, Entity thisEntity)
            {
                if (reference == Entity.Null || !sife.Exists(reference))
                    return false;

                var  info          = sife[reference];
                var  referenceMask = info.Chunk.GetChunkComponentData(chunkPerCameraMaskHandle);
                bool result;
                if (info.IndexInChunk >= 64)
                    result = referenceMask.upper.IsSet(info.IndexInChunk - 64);
                else
                    result = referenceMask.lower.IsSet(info.IndexInChunk);
                if (result && needsCopy)
                {
                    if (computeCdfe.HasComponent(thisEntity))
                    {
                        computeCdfe[thisEntity] = computeCdfe[reference];
                    }
                    if (linearBlendCdfe.HasComponent(thisEntity))
                    {
                        linearBlendCdfe[thisEntity] = linearBlendCdfe[reference];
                    }
                }
                return result;
            }
        }
    }
}

