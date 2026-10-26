using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

// This system doesn't actually allocate the graphics buffers.
// Doing so now would introduce a sync point.
// This system just calculates the required size and distributes instance shader properties.
namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AllocateDeformMaterialPropertiesSystem : ISystem, ILatiosApi, ISystemShouldUpdate
    {
        EntityQuery m_query;
        EntityQuery m_metaQuery;

        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);

            m_query     = state.Fluent().With<ChunkDeformPrefixSums>(false, true).With<BoundMesh>(true).Without<ChunkCopyDeformTag>(true).Build();
            m_metaQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkDeformPrefixSums>().Without<ChunkCopyDeformTag>().Build();
        }

        public unsafe bool ShouldUpdateSystem(ref SystemState state)
        {
            fixed (AllocateDeformMaterialPropertiesSystem* system = &this)
            return ShouldUpdate(ref state, system);
        }

        [BurstCompile]
        static unsafe bool ShouldUpdate(ref SystemState state, AllocateDeformMaterialPropertiesSystem* system)
        {
            var api          = system->GetApi(ref state);
            var dispatchData = api.worldBlackboardEntity.GetComponentData<DispatchContext>();
            if (dispatchData.isCustomGraphicsDispatch)
            {
                var features = api.worldBlackboardEntity.GetComponentData<EnableUpdatingInCustomGraphics>();
                if (!features.dynamicMeshes && !features.blendShapes && !features.skinning)
                    return false;
            }
            return true;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api = this.GetApi(ref state);

            var map            = api.worldBlackboardEntity.GetCollectionComponent<DeformClassificationMap>(true).deformClassificationMap;
            var meshGpuEntries = api.worldBlackboardEntity.GetCollectionComponent<MeshGpuManager>(true).entries.AsDeferredJobArray();

            var prefixesJh = new GatherChunkSumsJob
            {
                deformClassificationMap = map,
            }.Inject(api).ScheduleParallel(m_query, state.Dependency);

            prefixesJh = new ChunkPrefixSumJob
            {
                worldBlackboardEntity = api.worldBlackboardEntity
            }.Inject(api).Schedule(m_metaQuery, prefixesJh);

            prefixesJh = new AssignMaterialPropertiesJob
            {
                deformClassificationMap = map,
                meshGpuEnties           = meshGpuEntries,
            }.Inject(api).ScheduleParallel(m_query, prefixesJh);

            // The idea behind scheduling this separately is that it may be able to run alongside the single-threaded ChunkPrefixSumJob.
            var dirtyJh = new MarkMaterialPropertiesDirtyJob
            {
                deformClassificationMap = map,
                worldBlackboardEntity   = api.worldBlackboardEntity
            }.Inject(api).ScheduleParallel(m_metaQuery, state.Dependency);

            state.Dependency = JobHandle.CombineDependencies(prefixesJh, dirtyJh);
        }

        [BurstCompile]
        partial struct GatherChunkSumsJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchMaskHandle;
            [ReadOnly, Inject] ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [ReadOnly, Inject] ComponentTypeHandle<BoundMesh>                   meshHandle;
            [Inject] ComponentTypeHandle<ChunkDeformPrefixSums>                 metaHandle;

            [ReadOnly] public NativeParallelHashMap<ArchetypeChunk, DeformClassification> deformClassificationMap;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var dispatchMask = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                var frameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return; // The masks get re-checked in ChunkPrefixSumJob so we can quit now.

                var classification = deformClassificationMap[chunk];

                var                   meshArray  = chunk.GetNativeArray(ref meshHandle);
                ChunkDeformPrefixSums counts     = default;
                var                   enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                {
                    for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                    {
                        ref var blob = ref meshArray[i].meshBlob.Value;
                        if ((classification & (DeformClassification.CurrentVertexMatrix | DeformClassification.LegacyLbs)) != DeformClassification.None)
                            counts.currentMatrix += (uint)blob.skinningData.bindPoses.Length;
                        if ((classification & DeformClassification.PreviousVertexMatrix) != DeformClassification.None)
                            counts.previousMatrix += (uint)blob.skinningData.bindPoses.Length;
                        if ((classification & DeformClassification.TwoAgoVertexMatrix) != DeformClassification.None)
                            counts.twoAgoMatrix += (uint)blob.skinningData.bindPoses.Length;
                        if ((classification & DeformClassification.CurrentVertexDqs) != DeformClassification.None)
                            counts.currentDqs += (uint)blob.skinningData.bindPosesDQ.Length;
                        if ((classification & DeformClassification.PreviousVertexDqs) != DeformClassification.None)
                            counts.previousDqs += (uint)blob.skinningData.bindPosesDQ.Length;
                        if ((classification & DeformClassification.TwoAgoVertexDqs) != DeformClassification.None)
                            counts.twoAgoDqs += (uint)blob.skinningData.bindPosesDQ.Length;

                        if ((classification & DeformClassification.AnyCurrentDeform) != DeformClassification.None)
                            counts.currentDeform += (uint)blob.undeformedVertices.Length;
                        if ((classification & DeformClassification.AnyPreviousDeform) != DeformClassification.None)
                            counts.previousDeform += (uint)blob.undeformedVertices.Length;
                        if ((classification & DeformClassification.TwoAgoDeform) != DeformClassification.None)
                            counts.twoAgoDeform += (uint)blob.undeformedVertices.Length;
                    }
                }
                chunk.SetChunkComponentData(ref metaHandle, counts);
            }
        }

        // Schedule single
        [BurstCompile]
        partial struct ChunkPrefixSumJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchMaskHandle;
            [ReadOnly, Inject] ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [Inject] ComponentTypeHandle<ChunkDeformPrefixSums>                 metaHandle;
            [Inject] ComponentLookup<MaxRequiredDeformData>                     maxRequiredDeformDataLookup;
            public Entity                                                       worldBlackboardEntity;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var maxes = maxRequiredDeformDataLookup[worldBlackboardEntity];

                var dispatchMaskArray = chunk.GetNativeArray(ref perDispatchMaskHandle);
                var frameMaskArray    = chunk.GetNativeArray(ref perFrameMaskHandle);
                var metaArray         = chunk.GetNativeArray(ref metaHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var dispatchMask = dispatchMaskArray[i];
                    var frameMask    = frameMaskArray[i];
                    var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                    var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
                    if ((upper | lower) == 0)
                        continue;

                    var                   counts                      = metaArray[i];
                    ChunkDeformPrefixSums prefixSums                  = default;
                    prefixSums.currentMatrix                          = maxes.maxRequiredBoneTransformsForVertexSkinning;
                    maxes.maxRequiredBoneTransformsForVertexSkinning += counts.currentMatrix;
                    prefixSums.previousMatrix                         = maxes.maxRequiredBoneTransformsForVertexSkinning;
                    maxes.maxRequiredBoneTransformsForVertexSkinning += counts.previousMatrix;
                    prefixSums.twoAgoMatrix                           = maxes.maxRequiredBoneTransformsForVertexSkinning;
                    maxes.maxRequiredBoneTransformsForVertexSkinning += counts.twoAgoMatrix;
                    prefixSums.currentDqs                             = maxes.maxRequiredBoneTransformsForVertexSkinning;
                    maxes.maxRequiredBoneTransformsForVertexSkinning += counts.currentDqs;
                    prefixSums.previousDqs                            = maxes.maxRequiredBoneTransformsForVertexSkinning;
                    maxes.maxRequiredBoneTransformsForVertexSkinning += counts.previousDqs;
                    prefixSums.twoAgoDqs                              = maxes.maxRequiredBoneTransformsForVertexSkinning;
                    maxes.maxRequiredBoneTransformsForVertexSkinning += counts.twoAgoDqs;

                    prefixSums.currentDeform         = maxes.maxRequiredDeformVertices;
                    maxes.maxRequiredDeformVertices += counts.currentDeform;
                    prefixSums.previousDeform        = maxes.maxRequiredDeformVertices;
                    maxes.maxRequiredDeformVertices += counts.previousDeform;
                    prefixSums.twoAgoDeform          = maxes.maxRequiredDeformVertices;
                    maxes.maxRequiredDeformVertices += counts.twoAgoDeform;

                    metaArray[i] = prefixSums;
                }
                maxRequiredDeformDataLookup[worldBlackboardEntity] = maxes;
            }
        }

        [BurstCompile]
        partial struct AssignMaterialPropertiesJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchMaskHandle;
            [ReadOnly, Inject] ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [ReadOnly, Inject] ComponentTypeHandle<BoundMesh>                   meshHandle;
            [ReadOnly, Inject] ComponentTypeHandle<ChunkDeformPrefixSums>       metaHandle;

            [ReadOnly] public NativeParallelHashMap<ArchetypeChunk, DeformClassification> deformClassificationMap;
            [ReadOnly] public NativeArray<MeshGpuEntry>                                   meshGpuEnties;

            [Inject] ComponentTypeHandle<LegacyLinearBlendSkinningShaderIndex> legacyLbsHandle;
            [Inject] ComponentTypeHandle<LegacyComputeDeformShaderIndex>       legacyComputeDeformHandle;
            [Inject] ComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>    legacyDotsDeformHandle;

            [Inject] ComponentTypeHandle<CurrentMatrixVertexSkinningShaderIndex>  currentMatrixVertexHandle;
            [Inject] ComponentTypeHandle<PreviousMatrixVertexSkinningShaderIndex> previousMatrixVertexHandle;
            [Inject] ComponentTypeHandle<TwoAgoMatrixVertexSkinningShaderIndex>   twoAgoMatrixVertexHandle;
            [Inject] ComponentTypeHandle<CurrentDqsVertexSkinningShaderIndex>     currentDqsVertexHandle;
            [Inject] ComponentTypeHandle<PreviousDqsVertexSkinningShaderIndex>    previousDqsVertexHandle;
            [Inject] ComponentTypeHandle<TwoAgoDqsVertexSkinningShaderIndex>      twoAgoDqsVertexHandle;

            [Inject] ComponentTypeHandle<CurrentDeformShaderIndex>  currentDeformHandle;
            [Inject] ComponentTypeHandle<PreviousDeformShaderIndex> previousDeformHandle;
            [Inject] ComponentTypeHandle<TwoAgoDeformShaderIndex>   twoAgoDeformHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var dispatchMask = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                var frameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return;

                var prefixSums = chunk.GetChunkComponentData(ref metaHandle);

                var meshArray      = chunk.GetNativeArray(ref meshHandle);
                var classification = deformClassificationMap[chunk];

                if ((classification & (DeformClassification.CurrentVertexMatrix | DeformClassification.LegacyLbs)) != DeformClassification.None)
                {
                    bool hasCurrent = (classification & DeformClassification.CurrentVertexMatrix) != DeformClassification.None;
                    bool hasLegacy  = (classification & DeformClassification.LegacyLbs) != DeformClassification.None;

                    NativeArray<uint> current = hasCurrent ? chunk.GetNativeArray(ref currentMatrixVertexHandle).Reinterpret<uint>() : default;
                    NativeArray<uint> legacy  = hasLegacy ? chunk.GetNativeArray(ref legacyLbsHandle).Reinterpret<uint>() : default;

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            if (hasCurrent)
                                current[i] = prefixSums.currentMatrix;
                            if (hasLegacy)
                                legacy[i]             = prefixSums.currentMatrix;
                            prefixSums.currentMatrix += (uint)meshArray[i].meshBlob.Value.skinningData.bindPoses.Length;
                        }
                    }
                }

                if ((classification & DeformClassification.PreviousVertexMatrix) != DeformClassification.None)
                {
                    NativeArray<uint> indices = chunk.GetNativeArray(ref previousMatrixVertexHandle).Reinterpret<uint>();

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            indices[i]                 = prefixSums.previousMatrix;
                            prefixSums.previousMatrix += (uint)meshArray[i].meshBlob.Value.skinningData.bindPoses.Length;
                        }
                    }
                }

                if ((classification & DeformClassification.TwoAgoVertexMatrix) != DeformClassification.None)
                {
                    NativeArray<uint> indices = chunk.GetNativeArray(ref twoAgoMatrixVertexHandle).Reinterpret<uint>();

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            indices[i]               = prefixSums.twoAgoMatrix;
                            prefixSums.twoAgoMatrix += (uint)meshArray[i].meshBlob.Value.skinningData.bindPoses.Length;
                        }
                    }
                }

                if ((classification & DeformClassification.CurrentVertexDqs) != DeformClassification.None)
                {
                    NativeArray<uint2> indices = chunk.GetNativeArray(ref currentDqsVertexHandle).Reinterpret<uint2>();

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            uint bindposeDq        = meshGpuEnties[meshArray[i].meshEntryIndex].bindPosesStart + (uint)meshArray[i].meshBlob.Value.skinningData.bindPoses.Length;
                            indices[i]             = new uint2(prefixSums.currentDqs, bindposeDq);
                            prefixSums.currentDqs += (uint)meshArray[i].meshBlob.Value.skinningData.bindPosesDQ.Length;
                        }
                    }
                }

                if ((classification & DeformClassification.PreviousVertexDqs) != DeformClassification.None)
                {
                    NativeArray<uint2> indices = chunk.GetNativeArray(ref previousDqsVertexHandle).Reinterpret<uint2>();

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            uint bindposeDq         = meshGpuEnties[meshArray[i].meshEntryIndex].bindPosesStart + (uint)meshArray[i].meshBlob.Value.skinningData.bindPoses.Length;
                            indices[i]              = new uint2(prefixSums.previousDqs, bindposeDq);
                            prefixSums.previousDqs += (uint)meshArray[i].meshBlob.Value.skinningData.bindPosesDQ.Length;
                        }
                    }
                }

                if ((classification & DeformClassification.TwoAgoVertexDqs) != DeformClassification.None)
                {
                    NativeArray<uint2> indices = chunk.GetNativeArray(ref twoAgoDqsVertexHandle).Reinterpret<uint2>();

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            uint bindposeDq       = meshGpuEnties[meshArray[i].meshEntryIndex].bindPosesStart + (uint)meshArray[i].meshBlob.Value.skinningData.bindPoses.Length;
                            indices[i]            = new uint2(prefixSums.twoAgoDqs, bindposeDq);
                            prefixSums.twoAgoDqs += (uint)meshArray[i].meshBlob.Value.skinningData.bindPosesDQ.Length;
                        }
                    }
                }

                if ((classification & DeformClassification.AnyCurrentDeform) != DeformClassification.None)
                {
                    bool hasCurrent       = (classification & DeformClassification.CurrentDeform) != DeformClassification.None;
                    bool hasLegacyCompute = (classification & DeformClassification.LegacyCompute) != DeformClassification.None;
                    bool hasLegacyDots    = (classification & DeformClassification.LegacyDotsDeform) != DeformClassification.None;

                    NativeArray<uint>  current       = hasCurrent ? chunk.GetNativeArray(ref currentDeformHandle).Reinterpret<uint>() : default;
                    NativeArray<uint>  legacyCompute = hasLegacyCompute ? chunk.GetNativeArray(ref legacyComputeDeformHandle).Reinterpret<uint>() : default;
                    NativeArray<uint4> legacyDots    = hasLegacyDots ? chunk.GetNativeArray(ref legacyDotsDeformHandle).Reinterpret<uint4>() : default;

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            if (hasCurrent)
                                current[i] = prefixSums.currentDeform;
                            if (hasLegacyCompute)
                                legacyCompute[i] = prefixSums.currentDeform;
                            if (hasLegacyDots)
                                legacyDots[i]         = new uint4(prefixSums.currentDeform, 0, 0, 0);
                            prefixSums.currentDeform += (uint)meshArray[i].meshBlob.Value.undeformedVertices.Length;
                        }
                    }
                }

                if ((classification & DeformClassification.AnyPreviousDeform) != DeformClassification.None)
                {
                    bool hasPrevious   = (classification & DeformClassification.PreviousDeform) != DeformClassification.None;
                    bool hasLegacyDots = (classification & DeformClassification.LegacyDotsDeform) != DeformClassification.None;

                    NativeArray<uint>  previous   = hasPrevious ? chunk.GetNativeArray(ref previousDeformHandle).Reinterpret<uint>() : default;
                    NativeArray<uint4> legacyDots = hasLegacyDots ? chunk.GetNativeArray(ref legacyDotsDeformHandle).Reinterpret<uint4>() : default;

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            if (hasPrevious)
                                previous[i] = prefixSums.previousDeform;
                            if (hasLegacyDots)
                                legacyDots[i]         += new uint4(0, prefixSums.previousDeform, 0, 0);
                            prefixSums.previousDeform += (uint)meshArray[i].meshBlob.Value.undeformedVertices.Length;
                        }
                    }
                }

                if ((classification & DeformClassification.TwoAgoDeform) != DeformClassification.None)
                {
                    NativeArray<uint> indices = chunk.GetNativeArray(ref twoAgoDeformHandle).Reinterpret<uint>();

                    var enumerator = new ChunkEntityBatchEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextRange(out var rangeStart, out var rangeCount))
                    {
                        for (int i = rangeStart, rangeEnd = rangeStart + rangeCount; i < rangeEnd; i++)
                        {
                            indices[i]               = prefixSums.twoAgoDeform;
                            prefixSums.twoAgoDeform += (uint)meshArray[i].meshBlob.Value.undeformedVertices.Length;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        partial struct MarkMaterialPropertiesDirtyJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<ChunkPerDispatchCullingMask>           perDispatchMaskHandle;
            [ReadOnly, Inject] ComponentTypeHandle<ChunkPerFrameCullingMask>              perFrameMaskHandle;
            [ReadOnly, Inject] ComponentTypeHandle<ChunkHeader>                           chunkHeaderHandle;
            [ReadOnly] public NativeParallelHashMap<ArchetypeChunk, DeformClassification> deformClassificationMap;
            [Inject] ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>                  materialMaskHandle;
            [ReadOnly, Inject] BufferLookup<MaterialPropertyComponentType>                materialPropertyTypeLookup;
            public Entity                                                                 worldBlackboardEntity;

            [NativeDisableContainerSafetyRestriction, NoAlias] NativeArray<ulong> propertyTypeMasks;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var dispatchMaskArray = metaChunk.GetNativeArray(ref perDispatchMaskHandle);
                var frameMaskArray    = metaChunk.GetNativeArray(ref perFrameMaskHandle);
                var chunkHeaderArray  = metaChunk.GetNativeArray(ref chunkHeaderHandle);
                var materialMaskArray = metaChunk.GetNativeArray(ref materialMaskHandle);

                if (!propertyTypeMasks.IsCreated)
                {
                    var materialProperties = materialPropertyTypeLookup[worldBlackboardEntity].AsNativeArray().Reinterpret<ComponentType>();
                    propertyTypeMasks      = new NativeArray<ulong>(2 * 12, Allocator.Temp);

                    var index            = materialProperties.IndexOf(ComponentType.ReadOnly<CurrentDeformShaderIndex>());
                    propertyTypeMasks[0] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[1] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                = materialProperties.IndexOf(ComponentType.ReadOnly<PreviousDeformShaderIndex>());
                    propertyTypeMasks[2] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[3] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                = materialProperties.IndexOf(ComponentType.ReadOnly<TwoAgoDeformShaderIndex>());
                    propertyTypeMasks[4] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[5] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                = materialProperties.IndexOf(ComponentType.ReadOnly<CurrentMatrixVertexSkinningShaderIndex>());
                    propertyTypeMasks[6] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[7] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                = materialProperties.IndexOf(ComponentType.ReadOnly<PreviousMatrixVertexSkinningShaderIndex>());
                    propertyTypeMasks[8] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[9] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                 = materialProperties.IndexOf(ComponentType.ReadOnly<TwoAgoMatrixVertexSkinningShaderIndex>());
                    propertyTypeMasks[10] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[11] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                 = materialProperties.IndexOf(ComponentType.ReadOnly<CurrentDqsVertexSkinningShaderIndex>());
                    propertyTypeMasks[12] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[13] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                 = materialProperties.IndexOf(ComponentType.ReadOnly<PreviousDqsVertexSkinningShaderIndex>());
                    propertyTypeMasks[14] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[15] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                 = materialProperties.IndexOf(ComponentType.ReadOnly<TwoAgoDqsVertexSkinningShaderIndex>());
                    propertyTypeMasks[16] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[17] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                 = materialProperties.IndexOf(ComponentType.ReadOnly<LegacyLinearBlendSkinningShaderIndex>());
                    propertyTypeMasks[18] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[19] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                 = materialProperties.IndexOf(ComponentType.ReadOnly<LegacyComputeDeformShaderIndex>());
                    propertyTypeMasks[20] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[21] = index >= 64 ? (1ul << (index - 64)) : 0;

                    index                 = materialProperties.IndexOf(ComponentType.ReadOnly<LegacyDotsDeformParamsShaderIndex>());
                    propertyTypeMasks[22] = index >= 64 ? 0ul : (1ul << index);
                    propertyTypeMasks[23] = index >= 64 ? (1ul << (index - 64)) : 0;
                }

                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var dispatchMask = dispatchMaskArray[i];
                    var frameMask    = frameMaskArray[i];
                    var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                    var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
                    if ((upper | lower) == 0)
                        continue;

                    var classification = deformClassificationMap[chunkHeaderArray[i].ArchetypeChunk];

                    lower = 0ul;
                    upper = 0ul;

                    if ((classification & DeformClassification.CurrentDeform) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[0];
                        upper |= propertyTypeMasks[1];
                    }
                    if ((classification & DeformClassification.PreviousDeform) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[2];
                        upper |= propertyTypeMasks[3];
                    }
                    if ((classification & DeformClassification.TwoAgoDeform) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[4];
                        upper |= propertyTypeMasks[5];
                    }
                    if ((classification & DeformClassification.CurrentVertexMatrix) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[6];
                        upper |= propertyTypeMasks[7];
                    }
                    if ((classification & DeformClassification.PreviousVertexMatrix) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[8];
                        upper |= propertyTypeMasks[9];
                    }
                    if ((classification & DeformClassification.TwoAgoVertexMatrix) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[10];
                        upper |= propertyTypeMasks[11];
                    }
                    if ((classification & DeformClassification.CurrentVertexDqs) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[12];
                        upper |= propertyTypeMasks[13];
                    }
                    if ((classification & DeformClassification.PreviousVertexDqs) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[14];
                        upper |= propertyTypeMasks[15];
                    }
                    if ((classification & DeformClassification.TwoAgoVertexDqs) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[16];
                        upper |= propertyTypeMasks[17];
                    }
                    if ((classification & DeformClassification.LegacyLbs) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[18];
                        upper |= propertyTypeMasks[19];
                    }
                    if ((classification & DeformClassification.LegacyCompute) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[20];
                        upper |= propertyTypeMasks[21];
                    }
                    if ((classification & DeformClassification.LegacyDotsDeform) != DeformClassification.None)
                    {
                        lower |= propertyTypeMasks[22];
                        upper |= propertyTypeMasks[23];
                    }

                    var mask              = materialMaskArray[i];
                    mask.lower.Value     |= lower;
                    mask.upper.Value     |= upper;
                    materialMaskArray[i]  = mask;
                }
            }
        }
    }
}

