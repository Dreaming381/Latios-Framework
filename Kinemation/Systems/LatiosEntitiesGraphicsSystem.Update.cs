#region Header
#if (ENABLE_UNITY_COLLECTIONS_CHECKS || DEVELOPMENT_BUILD) && !DISABLE_MATERIALMESHINFO_BOUNDS_CHECKING
#define ENABLE_MATERIALMESHINFO_BOUNDS_CHECKING
#endif

using System.Text;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using MaterialPropertyType = Unity.Rendering.MaterialPropertyType;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
#endregion

namespace Latios.Kinemation.Systems
{
    public unsafe partial class LatiosEntitiesGraphicsSystem
    {
        protected override void OnUpdate()
        {
            if (m_unityEntitiesGraphicsSystem.Enabled == true)
            {
                UnityEngine.Debug.Log("Entities Graphics was enabled!");
                m_unityEntitiesGraphicsSystem.Enabled = false;
            }

            Profiler.BeginSample("UpdateFilterSettings");
            UpdateFilterSettings(ref CheckedStateRef);
            Profiler.EndSample();

            m_unmanaged.BeforeOnUpdate(ref CheckedStateRef);

            //m_unmanaged.OnUpdate(ref CheckedStateRef);
            fixed (Unmanaged* unmanaged = &m_unmanaged)
            DoOnUpdate(unmanaged, ref CheckedStateRef);

            try
            {
#if ENABLE_MATERIALMESHINFO_BOUNDS_CHECKING
                m_registerMaterialsAndMeshesSystem.LogBoundsCheckErrorMessages();
#endif
                m_unmanaged.m_brgRenderMeshArrays = m_registerMaterialsAndMeshesSystem.BRGRenderMeshArrays;
            }
            finally
            {
            }

            EntitiesGraphicsEditorTools.EndFrame();
        }

        [BurstCompile]
        static void DoOnUpdate(Unmanaged* unmanaged, ref SystemState state)
        {
            unmanaged->OnUpdate(ref state);
        }

        private void UpdateFilterSettings(ref SystemState state)
        {
            m_RenderFilterSettings.Clear();
            m_SharedComponentIndices.Clear();

            state.EntityManager.GetAllUniqueSharedComponentsManaged(m_RenderFilterSettings, m_SharedComponentIndices);

            m_unmanaged.m_FilterSettings.Clear();
            for (int i = 0; i < m_SharedComponentIndices.Count; ++i)
            {
                int sharedIndex                           = m_SharedComponentIndices[i];
                m_unmanaged.m_FilterSettings[sharedIndex] = MakeFilterSettings(m_RenderFilterSettings[i]);
            }

            m_RenderFilterSettings.Clear();
            m_SharedComponentIndices.Clear();
        }

        static BatchFilterSettings MakeFilterSettings(in RenderFilterSettings filterSettings)
        {
            return new BatchFilterSettings
            {
                layer              = (byte)filterSettings.Layer,
                renderingLayerMask = filterSettings.RenderingLayerMask,
                motionMode         = filterSettings.MotionMode,
                shadowCastingMode  = filterSettings.ShadowCastingMode,
                receiveShadows     = filterSettings.ReceiveShadows,
                staticShadowCaster = filterSettings.StaticShadowCaster,
                allDepthSorted     = false,  // set by culling
            };
        }

        partial struct Unmanaged
        {
            static readonly ProfilerMarker sCompleteJobsMarker                  = new ProfilerMarker("Complete Jobs");
            static readonly ProfilerMarker sUpdateEntitiesGraphicsBatchesMarker = new ProfilerMarker("UpdateEntitiesGraphicsBatches");

            public void BeforeOnUpdate(ref SystemState state)
            {
                m_reuploadAllData = EntitiesGraphicsEditorTools.DebugSettings.ForceInstanceDataUpload;

                if (!m_cullingCallbackFinalJobHandles.IsEmpty)
                    JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());
                m_cullingCallbackFinalJobHandles.Clear();

                // Todo: The implementation of this is not Burst-compatible.
                m_ThreadLocalAllocators.Rewind();
            }

            public void OnUpdate(ref SystemState state)
            {
                JobHandle inputDeps = state.Dependency;

                // Make sure any release culling jobs that have stored pointers in temp allocated
                // memory have finished before we rewind
                state.Dependency = default;
                latiosWorld.worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>(false);
                state.CompleteDependency();

                latiosWorld.worldBlackboardEntity.UpdateJobDependency<BrgCullingContext>(default, false);

                m_cullPassIndexThisFrame       = 0;
                m_dispatchPassIndexThisFrame   = 0;
                m_cullPassIndexForLastDispatch = -1;

                if (latiosWorld.worldBlackboardEntity.HasComponent<EnableCustomGraphicsTag>())
                {
                    m_dispatchPassIndexThisFrame = 1;
                    latiosWorld.worldBlackboardEntity.SetComponentData(new DispatchContext
                    {
                        dispatchIndexThisFrame                      = 0,
                        lastSystemVersionOfLatiosEntitiesGraphics   = state.LastSystemVersion,
                        globalSystemVersionOfLatiosEntitiesGraphics = state.GlobalSystemVersion
                    });
                }

                m_LastSystemVersionAtLastUpdate   = state.LastSystemVersion;
                m_globalSystemVersionAtLastUpdate = state.GlobalSystemVersion;

                sCompleteJobsMarker.Begin();
                inputDeps.Complete();  // #todo
                CompleteJobs();
                sCompleteJobsMarker.End();

                int renderersChunkCount = 0;
                var finalJh             = new JobHandle();
                try
                {
                    sUpdateEntitiesGraphicsBatchesMarker.Begin();
                    if (!m_EntitiesGraphicsRenderedQuery.IsEmptyIgnoreFilter)
                        finalJh = UpdateAllBatches(ref state, inputDeps, out renderersChunkCount);
                }
                finally
                {
                    sUpdateEntitiesGraphicsBatchesMarker.End();
                }

                latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new MaterialPropertiesUploadContext
                {
                    chunkProperties     = m_ChunkProperties,
                    valueBlits          = m_ValueBlits,
                    renderersChunkCount = renderersChunkCount,
                });

                state.Dependency   = finalJh;
                m_needsFirstUpdate = false;
            }

            private void EnsureHaveSpaceForNewBatch()
            {
                int currentCapacity = m_BatchInfos.Length;
                int neededCapacity  = BatchIndexRange;

                if (currentCapacity >= neededCapacity)
                    return;

                Assert.IsTrue(kMaxBatchGrowFactor >= 1f, "Grow factor should always be greater or equal to 1");

                var newCapacity = (int)(kMaxBatchGrowFactor * neededCapacity);

                m_BatchInfos.Resize(newCapacity, NativeArrayOptions.ClearMemory);
            }

            private void AddBatchIndex(int id)
            {
                Assert.IsTrue(!m_SortedBatchIds.Contains(id), "New batch ID already marked as used");
                m_SortedBatchIds.Add(id);
                m_ExistingBatchIndices.Add(id);
                EnsureHaveSpaceForNewBatch();
            }

            private void RemoveBatchIndex(int id)
            {
                if (!m_SortedBatchIds.Contains(id))
                    Assert.IsTrue(false, $"Attempted to release an unused id {id}");
                m_SortedBatchIds.Remove(id);
                m_ExistingBatchIndices.Remove(id);
            }

            private int BatchIndexRange => m_SortedBatchIds.Max + 1;

            static readonly ProfilerMarker sGarbageCollectUnreferencedBatchesMarker = new ProfilerMarker("GarbageCollectUnreferencedBatches");
            static readonly ProfilerMarker sAddNewChunksMarker                      = new ProfilerMarker("AddNewChunks");
            static readonly ProfilerMarker sStartUpdateMarker                       = new ProfilerMarker("StartUpdate");

            private JobHandle UpdateAllBatches(ref SystemState state, JobHandle inputDependencies, out int totalChunks)
            {
                var entitiesGraphicsRenderedChunkType   = state.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(false);
                var entitiesGraphicsRenderedChunkTypeRO = state.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true);
                var chunkHeadersRO                      = state.GetComponentTypeHandle<ChunkHeader>(true);
                var materialMeshInfosRO                 = state.GetComponentTypeHandle<MaterialMeshInfo>(true);
                var renderMeshArrays                    = state.GetSharedComponentTypeHandle<RenderMeshArray>();

                var numNewChunksArray          = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator);
                var totalChunksFromNormalQuery = m_EntitiesGraphicsRenderedQuery.CalculateChunkCountWithoutFiltering();
                var totalChunksFromMetaQuery   = m_MetaEntitiesForHybridRenderableChunksQuery.CalculateEntityCountWithoutFiltering();
                totalChunks                    = math.max(totalChunksFromNormalQuery, totalChunksFromMetaQuery);  // If these are different for some reason, reserve the larger.
                var newChunks                  =
                    CollectionHelper.CreateNativeArray<ArchetypeChunk>(totalChunks, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

                var classifyNewChunksJob = new ClassifyNewChunksJobLatiosVersion
                {
                    EntitiesGraphicsChunkInfo = entitiesGraphicsRenderedChunkTypeRO,
                    ChunkHeader               = chunkHeadersRO,
                    NumNewChunks              = numNewChunksArray,
                    NewChunks                 = newChunks,
                    chunkValidityMask         = m_entitiesGraphicsRenderedQueryMask
                }
                .ScheduleParallel(m_MetaEntitiesForHybridRenderableChunksQuery, inputDependencies);

                JobHandle entitiesGraphicsCompleted = new JobHandle();

                const int kNumBitsPerLong          = sizeof(long) * 8;
                var       unreferencedBatchIndices = CollectionHelper.CreateNativeArray<long>((BatchIndexRange + kNumBitsPerLong) / kNumBitsPerLong,
                                                                                        state.WorldUpdateAllocator,
                                                                                        NativeArrayOptions.ClearMemory);

                JobHandle initializedUnreferenced = default;
                var       existingKeys            = m_ExistingBatchIndices.ToNativeArray(state.WorldUpdateAllocator);
                initializedUnreferenced           = new InitializeUnreferencedIndicesScatterJob
                {
                    ExistingBatchIndices     = existingKeys,
                    UnreferencedBatchIndices = unreferencedBatchIndices,
                }.Schedule(existingKeys.Length, kNumScatteredIndicesPerThread);

                inputDependencies = JobHandle.CombineDependencies(inputDependencies, initializedUnreferenced);

                uint lastSystemVersion = state.LastSystemVersion;

                if (m_reuploadAllData)
                {
                    Debug.Log("Reuploading all Entities Graphics instance data to GPU");
                    lastSystemVersion = 0;
                }

                classifyNewChunksJob.Complete();
                int numNewChunks = numNewChunksArray[0];

                var maxBatchCount = math.max(kInitialMaxBatchCount, BatchIndexRange + numNewChunks);

                // Integer division with round up
                var maxBatchLongCount = (maxBatchCount + kNumBitsPerLong - 1) / kNumBitsPerLong;

                m_burstCompatibleTypeArray.Update(ref state);
                var entitiesGraphicsChunkUpdater = new EntitiesGraphicsChunkUpdater
                {
                    postProcessMatrixHandle         = state.GetComponentTypeHandle<PostProcessMatrix>(true),
                    previousPostProcessMatrixHandle = state.GetComponentTypeHandle<PreviousPostProcessMatrix>(true),

                    materialTypeHandleArray        = m_burstCompatibleTypeArray,
                    chunkMaterialPropertyDirtyMask = state.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                    unreferencedBatchIndices       = unreferencedBatchIndices,
                    chunkProperties                = m_ChunkProperties,
                    lastSystemVersion              = lastSystemVersion,

                    worldToLocalType     = TypeManager.GetTypeIndex<WorldToLocal_Tag>(),
                    prevWorldToLocalType = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousMI_Tag>(),

#if !LATIOS_TRANSFORMS_UNITY
                    worldTransformType    = TypeManager.GetTypeIndex<WorldTransform>(),
                    previousTransformType = TypeManager.GetTypeIndex<PreviousTransform>(),
#elif LATIOS_TRANSFORMS_UNITY
                    worldTransformType    = TypeManager.GetTypeIndex<LocalToWorld>(),
                    previousTransformType = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousM>(),
#endif
                };

                var updateOldJob = new UpdateOldEntitiesGraphicsChunksJob
                {
                    EntitiesGraphicsChunkInfo    = entitiesGraphicsRenderedChunkType,
                    ChunkHeader                  = chunkHeadersRO,
                    WorldTransform               = state.GetDynamicComponentTypeHandle(QueryExtensions.GetAbstractWorldTransformROComponentType()),
                    MaterialMeshInfo             = materialMeshInfosRO,
                    EntitiesGraphicsChunkUpdater = entitiesGraphicsChunkUpdater,
                };

                JobHandle updateOldDependencies = inputDependencies;

                // We need to wait for the job to complete here so we can process the new chunks
                updateOldJob.ScheduleParallel(m_MetaEntitiesForHybridRenderableChunksQuery, updateOldDependencies).Complete();

                // Garbage collect deleted batches before adding new ones to minimize peak memory use.
                sGarbageCollectUnreferencedBatchesMarker.Begin();
                int numRemoved = GarbageCollectUnreferencedBatches(unreferencedBatchIndices);
                sGarbageCollectUnreferencedBatchesMarker.End();

                if (numNewChunks > 0)
                {
                    sAddNewChunksMarker.Begin();
                    int numValidNewChunks = AddNewChunks(ref state, newChunks.GetSubArray(0, numNewChunks));
                    sAddNewChunksMarker.End();

                    var updateNewChunksJob = new UpdateNewEntitiesGraphicsChunksJob
                    {
                        NewChunks                    = newChunks,
                        EntitiesGraphicsChunkInfo    = entitiesGraphicsRenderedChunkTypeRO,
                        EntitiesGraphicsChunkUpdater = entitiesGraphicsChunkUpdater,
                    };

                    entitiesGraphicsCompleted = updateNewChunksJob.Schedule(numValidNewChunks, kNumNewChunksPerThread);
                }

                var drawCommandFlagsUpdated = new UpdateDrawCommandFlagsJob
                {
#if !LATIOS_TRANSFORMS_UNITY
                    WorldTransform    = state.GetComponentTypeHandle<WorldTransform>(true),
                    PostProcessMatrix = state.GetComponentTypeHandle<PostProcessMatrix>(true),
#elif LATIOS_TRANSFORMS_UNITY
                    WorldTransform = state.GetComponentTypeHandle<LocalToWorld>(true),
#endif
                    RenderFilterSettings      = state.GetSharedComponentTypeHandle<RenderFilterSettings>(),
                    EntitiesGraphicsChunkInfo = state.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(),
                    FilterSettings            = m_FilterSettings,
                    DefaultFilterSettings     = MakeFilterSettings(RenderFilterSettings.Default),
                    lastSystemVersion         = state.LastSystemVersion
                }.ScheduleParallel(m_ChangedTransformQuery, entitiesGraphicsCompleted);
                DidScheduleUpdateJob(drawCommandFlagsUpdated);

                // TODO: Need to wait for new chunk updating to complete, so there are no more jobs writing to the bitfields.
                entitiesGraphicsCompleted.Complete();

                sStartUpdateMarker.Begin();
                StartUpdate(ref state);
                sStartUpdateMarker.End();

                JobHandle outputDeps = drawCommandFlagsUpdated;

                return outputDeps;
            }

            private int GarbageCollectUnreferencedBatches(NativeArray<long> unreferencedBatchIndices)
            {
                int numRemoved = 0;

                int firstInQw = 0;
                for (int i = 0; i < unreferencedBatchIndices.Length; ++i)
                {
                    long qw = unreferencedBatchIndices[i];
                    while (qw != 0)
                    {
                        int  setBit     = math.tzcnt(qw);
                        long mask       = ~(1L << setBit);
                        int  batchIndex = firstInQw + setBit;

                        RemoveBatch(batchIndex);
                        ++numRemoved;

                        qw &= mask;
                    }

                    firstInQw += (int)AtomicHelpers.kNumBitsInLong;
                }

                return numRemoved;
            }

            private void RemoveBatch(int batchIndex)
            {
                var batchInfo            = m_BatchInfos[batchIndex];
                m_BatchInfos[batchIndex] = default;

                RemoveBatchIndex(batchIndex);

                if (!batchInfo.GPUMemoryAllocation.Empty)
                {
                    m_GPUPersistentAllocator.Release(batchInfo.GPUMemoryAllocation);
                }

                var metadataAllocation = batchInfo.ChunkMetadataAllocation;
                if (!metadataAllocation.Empty)
                {
                    for (ulong j = metadataAllocation.begin; j < metadataAllocation.end; ++j)
                        m_ChunkProperties[(int)j] = default;

                    m_ChunkMetadataAllocator.Release(metadataAllocation);
                }

                m_ThreadedBatchContext.RemoveBatch(new BatchID { value = (uint)batchIndex });
            }

            static int NumInstancesInChunk(ArchetypeChunk chunk) => chunk.Capacity;

            static void CreateBatchCreateInfo(
                ref BatchCreateInfoFactory batchCreateInfoFactory,
                ref NativeArray<ArchetypeChunk>  newChunks,
                ref NativeArray<BatchCreateInfo> sortedNewChunks,
                out MaterialPropertyType failureProperty
                )
            {
                failureProperty           = default;
                failureProperty.TypeIndex = -1;
                for (int i = 0; i < newChunks.Length; ++i)
                {
                    sortedNewChunks[i] = batchCreateInfoFactory.Create(newChunks[i], ref failureProperty);
                    if (failureProperty.TypeIndex >= 0)
                    {
                        return;
                    }
                }
                sortedNewChunks.Sort();
            }

            private int AddNewChunks(ref SystemState state, NativeArray<ArchetypeChunk> newChunks)
            {
                int numValidNewChunks = 0;

                Assert.IsTrue(newChunks.Length > 0, "Attempted to add new chunks, but list of new chunks was empty");

                var batchCreationTypeHandles = new BatchCreationTypeHandles
                {
                    perInstanceCullingHandle                       = state.GetComponentTypeHandle<PerInstanceCullingTag>(true),
                    lodHeightPercentagesHandle                     = state.GetComponentTypeHandle<LodHeightPercentages>(true),
                    lodHeightPercentagesWithCrossfadeMarginsHandle = state.GetComponentTypeHandle<LodHeightPercentagesWithCrossfadeMargins>(true),
                };

                // Sort new chunks by RenderMesh so we can put
                // all compatible chunks inside one batch.
                var batchCreateInfoFactory = new BatchCreateInfoFactory
                {
                    GraphicsArchetypes          = m_GraphicsArchetypes,
                    TypeIndexToMaterialProperty = m_TypeIndexToMaterialProperty,
                };

                var sortedNewChunks = new NativeArray<BatchCreateInfo>(newChunks.Length, Allocator.Temp);

                CreateBatchCreateInfo(ref batchCreateInfoFactory, ref newChunks, ref sortedNewChunks, out var failureProperty);
                if (failureProperty.TypeIndex >= 0)
                {
                    FixedString128Bytes debugName                                             = default;
                    debugName.CopyFromTruncated(TypeManager.GetTypeInfo(new TypeIndex { Value = failureProperty.TypeIndex }).DebugTypeName);
                    UnityEngine.Debug.Log($"TypeIndex mismatch between key and stored property. TypeIndex corresponds to type {debugName} ({failureProperty.TypeIndex:x8})");
                    PrintFailurePropertyDetails(failureProperty);
                }

                static int GetArchetypeMaxEntitiesPerBatch(GraphicsArchetype archetype, int maxBytesPerBatch)
                {
                    int fixedBytes     = 0;
                    int bytesPerEntity = 0;

                    for (int i = 0; i < archetype.PropertyComponents.Length; ++i)
                        bytesPerEntity += archetype.PropertyComponents[i].SizeBytesGPU;

                    int maxBytes            = maxBytesPerBatch;
                    int maxBytesForEntities = maxBytes - fixedBytes;

                    return maxBytesForEntities / math.max(1, bytesPerEntity);
                }

                int batchBegin          = 0;
                int numInstances        = NumInstancesInChunk(sortedNewChunks[0].Chunk);
                int maxEntitiesPerBatch = GetArchetypeMaxEntitiesPerBatch(m_GraphicsArchetypes.GetGraphicsArchetype(sortedNewChunks[0].GraphicsArchetypeIndex), m_maxBytesPerBatch);

                for (int i = 1; i <= sortedNewChunks.Length; ++i)
                {
                    int  instancesInChunk = 0;
                    bool breakBatch       = false;

                    if (i < sortedNewChunks.Length)
                    {
                        var cur          = sortedNewChunks[i];
                        breakBatch       = !sortedNewChunks[batchBegin].Equals(cur);
                        instancesInChunk = NumInstancesInChunk(cur.Chunk);
                    }
                    else
                    {
                        breakBatch = true;
                    }

                    if (numInstances + instancesInChunk > maxEntitiesPerBatch)
                        breakBatch = true;

                    if (breakBatch)
                    {
                        int numChunks = i - batchBegin;

                        bool valid = AddNewBatch(ref state,
                                                 batchCreationTypeHandles,
                                                 sortedNewChunks.GetSubArray(batchBegin, numChunks),
                                                 numInstances);

                        // As soon as we encounter an invalid chunk, we know that all the rest are invalid
                        // too.
                        if (valid)
                            numValidNewChunks += numChunks;
                        else
                            return numValidNewChunks;

                        batchBegin   = i;
                        numInstances = instancesInChunk;

                        if (batchBegin < sortedNewChunks.Length)
                            maxEntitiesPerBatch = GetArchetypeMaxEntitiesPerBatch(m_GraphicsArchetypes.GetGraphicsArchetype(sortedNewChunks[batchBegin].GraphicsArchetypeIndex),
                                                                                  m_maxBytesPerBatch);
                    }
                    else
                    {
                        numInstances += instancesInChunk;
                    }
                }

                sortedNewChunks.Dispose();

                return numValidNewChunks;
            }

            [BurstDiscard]
            void PrintFailurePropertyDetails(MaterialPropertyType failureProperty)
            {
                Assert.IsTrue(false,
                              $"TypeIndex mismatch between key and stored property, Type: {failureProperty.TypeName} ({failureProperty.TypeIndex:x8}), Property: {failureProperty.PropertyName} ({failureProperty.NameID:x8})");
            }

            private static int NextAlignedBy16(int size)
            {
                return ((size + 15) >> 4) << 4;
            }

            internal static MetadataValue CreateMetadataValue(int nameID, int gpuAddress, bool isOverridden)
            {
                const uint kPerInstanceDataBit = 0x80000000;

                return new MetadataValue
                {
                    NameID = nameID,
                    Value  = (uint)gpuAddress |
                             (isOverridden ? kPerInstanceDataBit : 0),
                };
            }

            private bool AddNewBatch(
                ref SystemState state,
                BatchCreationTypeHandles typeHandles,
                NativeArray<BatchCreateInfo> batchChunks,
                int numInstances)
            {
                var graphicsArchetype = m_GraphicsArchetypes.GetGraphicsArchetype(batchChunks[0].GraphicsArchetypeIndex);

                var overrides     = graphicsArchetype.PropertyComponents;
                var overrideSizes = new NativeArray<int>(overrides.Length, Allocator.Temp);

                int numProperties = overrides.Length;

                Assert.IsTrue(numProperties > 0,      "No overridden properties, expected at least one");
                Assert.IsTrue(numInstances > 0,       "No instances, expected at least one");
                Assert.IsTrue(batchChunks.Length > 0, "No chunks, expected at least one");

                int batchSizeBytes = 0;
                // Every chunk has the same graphics archetype, so each requires the same amount
                // of component metadata structs.
                int batchTotalChunkMetadata = numProperties * batchChunks.Length;

                for (int i = 0; i < overrides.Length; ++i)
                {
                    // For each component, allocate a contiguous range that's aligned by 16.
                    int sizeBytesComponent  = NextAlignedBy16(overrides[i].SizeBytesGPU * numInstances);
                    overrideSizes[i]        = sizeBytesComponent;
                    batchSizeBytes         += sizeBytesComponent;
                }

                BatchInfo batchInfo = default;

                // TODO: If allocations fail, bail out and stop spamming the log each frame.

                batchInfo.ChunkMetadataAllocation = m_ChunkMetadataAllocator.Allocate((ulong)batchTotalChunkMetadata);
                if (batchInfo.ChunkMetadataAllocation.Empty)
                    Assert.IsTrue(false,
                                  $"Out of memory in the Entities Graphics chunk metadata buffer. Attempted to allocate {batchTotalChunkMetadata} elements, buffer size: {m_ChunkMetadataAllocator.Size}, free size left: {m_ChunkMetadataAllocator.FreeSpace}.");

                batchInfo.GPUMemoryAllocation = m_GPUPersistentAllocator.Allocate((ulong)batchSizeBytes, m_batchAllocationAlignment);
                if (batchInfo.GPUMemoryAllocation.Empty)
                    Assert.IsTrue(false,
                                  $"Out of memory in the Entities Graphics GPU instance data buffer. Attempted to allocate {batchSizeBytes}, buffer size: {m_GPUPersistentAllocator.Size}, free size left: {m_GPUPersistentAllocator.FreeSpace}.");

                // Physical offset inside the buffer, always the same on all platforms.
                int allocationBegin = (int)batchInfo.GPUMemoryAllocation.begin;

                // Metadata offset depends on whether a raw buffer or cbuffer is used.
                // Raw buffers index from start of buffer, cbuffers index from start of allocation.
                uint bindOffset = m_useConstantBuffers ?
                                  (uint)allocationBegin :
                                  0;
                uint bindWindowSize = m_useConstantBuffers ?
                                      (uint)m_maxBytesPerBatch :
                                      0;

                // Compute where each individual property SoA stream starts
                var overrideStreamBegin = new NativeArray<int>(overrides.Length, Allocator.Temp);
                overrideStreamBegin[0]  = allocationBegin;
                for (int i = 1; i < numProperties; ++i)
                    overrideStreamBegin[i] = overrideStreamBegin[i - 1] + overrideSizes[i - 1];

                int numMetadata      = numProperties;
                var overrideMetadata = new NativeArray<MetadataValue>(numMetadata, Allocator.Temp);

                int metadataIndex = 0;
                for (int i = 0; i < numProperties; ++i)
                {
                    int gpuAddress                  = overrideStreamBegin[i] - (int)bindOffset;
                    overrideMetadata[metadataIndex] = CreateMetadataValue(overrides[i].NameID, gpuAddress, true);
                    ++metadataIndex;
                }

                var batchID = m_ThreadedBatchContext.AddBatch(overrideMetadata, m_GPUPersistentInstanceBufferHandle,
                                                              bindOffset, bindWindowSize);
                int batchIndex = (int)batchID.value;

                if (batchIndex == 0)
                    Assert.IsTrue(false, "Failed to add new BatchRendererGroup batch.");

                AddBatchIndex(batchIndex);
                m_BatchInfos[batchIndex] = batchInfo;

                // Configure chunk components for each chunk
                var args = new SetBatchChunkDataArgs
                {
                    BatchChunks         = batchChunks,
                    BatchIndex          = batchIndex,
                    ChunkProperties     = m_ChunkProperties,
                    EntityManager       = state.EntityManager,
                    NumProperties       = numProperties,
                    TypeHandles         = typeHandles,
                    ChunkMetadataBegin  = (int)batchInfo.ChunkMetadataAllocation.begin,
                    ChunkOffsetInBatch  = 0,
                    OverrideStreamBegin = overrideStreamBegin
                };
                SetBatchChunkData(ref args, ref overrides);

                Assert.IsTrue(args.ChunkOffsetInBatch == numInstances, "Batch instance count mismatch");

                return true;
            }

            static void SetBatchChunkData(ref SetBatchChunkDataArgs args, ref UnsafeList<ArchetypePropertyOverride> overrides)
            {
                var batchChunks         = args.BatchChunks;
                int numProperties       = args.NumProperties;
                var overrideStreamBegin = args.OverrideStreamBegin;
                int chunkOffsetInBatch  = args.ChunkOffsetInBatch;
                int chunkMetadataBegin  = args.ChunkMetadataBegin;
                for (int i = 0; i < batchChunks.Length; ++i)
                {
                    var chunk                     = batchChunks[i].Chunk;
                    var entitiesGraphicsChunkInfo = new EntitiesGraphicsChunkInfo
                    {
                        Valid           = true,
                        BatchIndex      = args.BatchIndex,
                        ChunkTypesBegin = chunkMetadataBegin,
                        ChunkTypesEnd   = chunkMetadataBegin + numProperties,
                        CullingData     = new EntitiesGraphicsChunkCullingData
                        {
                            Flags               = ComputeCullingFlags(chunk, args.TypeHandles),
                            InstanceLodEnableds = default,
                            ChunkOffsetInBatch  = chunkOffsetInBatch,
                        },
                    };

                    args.EntityManager.SetChunkComponentData(chunk, entitiesGraphicsChunkInfo);

                    for (int j = 0; j < numProperties; ++j)
                    {
                        var propertyOverride = overrides[j];
                        var chunkProperty    = new ChunkProperty
                        {
                            ComponentTypeIndex = propertyOverride.TypeIndex,
                            GPUDataBegin       = overrideStreamBegin[j] + chunkOffsetInBatch * propertyOverride.SizeBytesGPU,
                            ValueSizeBytesCPU  = propertyOverride.SizeBytesCPU,
                            ValueSizeBytesGPU  = propertyOverride.SizeBytesGPU,
                        };

                        args.ChunkProperties[chunkMetadataBegin + j] = chunkProperty;
                    }

                    chunkOffsetInBatch += NumInstancesInChunk(chunk);
                    chunkMetadataBegin += numProperties;
                }

                args.ChunkOffsetInBatch = chunkOffsetInBatch;
                args.ChunkMetadataBegin = chunkMetadataBegin;
            }

            static byte ComputeCullingFlags(ArchetypeChunk chunk, BatchCreationTypeHandles typeHandles)
            {
                bool hasLodData = chunk.Has(ref typeHandles.lodHeightPercentagesHandle) || chunk.Has(ref typeHandles.lodHeightPercentagesWithCrossfadeMarginsHandle);

                // TODO: Do we need non-per-instance culling anymore? It seems to always be added
                // for converted objects, and doesn't seem to be removed ever, so the only way to
                // not have it is to manually remove it or create entities from scratch.
                bool hasPerInstanceCulling = !hasLodData || chunk.Has(ref typeHandles.perInstanceCullingHandle);

                byte flags = 0;

                if (hasLodData)
                    flags |= EntitiesGraphicsChunkCullingData.kFlagHasLodData;
                if (hasPerInstanceCulling)
                    flags |= EntitiesGraphicsChunkCullingData.kFlagInstanceCulling;

                return flags;
            }

            private void CompleteJobs(bool completeEverything = false)
            {
                // TODO: This might not be necessary, remove?
                if (completeEverything)
                {
                    m_EntitiesGraphicsRenderedQuery.CompleteDependency();
                    m_LodSelectGroup.CompleteDependency();
                    m_ChangedTransformQuery.CompleteDependency();
                }

                m_UpdateJobDependency.Complete();
                m_UpdateJobDependency = new JobHandle();
            }

            private void DidScheduleUpdateJob(JobHandle job)
            {
                m_UpdateJobDependency = JobHandle.CombineDependencies(job, m_UpdateJobDependency);
            }

            private void StartUpdate(ref SystemState state)
            {
                var persistentBytes = m_GPUPersistentAllocator.OnePastHighestUsedAddress;
                if (persistentBytes > (ulong)m_PersistentInstanceDataSize)
                {
                    while ((ulong)m_PersistentInstanceDataSize < persistentBytes)
                    {
                        m_PersistentInstanceDataSize *= 2;
                    }

                    if (m_PersistentInstanceDataSize > kGPUBufferSizeMax)
                    {
                        m_PersistentInstanceDataSize = kGPUBufferSizeMax;  // Some backends fails at loading 1024 MiB, but 1023 is fine... This should ideally be a device cap.
                    }

                    if (persistentBytes > kGPUBufferSizeMax)
                        Debug.LogError(
                            "Entities Graphics: Current loaded scenes need more than 1GiB of persistent GPU memory. This is more than some GPU backends can allocate. Try to reduce amount of loaded data.");

                    ref var uploadSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<UploadMaterialPropertiesSystem>(
                        state.WorldUnmanaged.GetExistingUnmanagedSystem<UploadMaterialPropertiesSystem>());
                    if (uploadSystem.SetBufferSize(m_PersistentInstanceDataSize, out var newHandle))
                    {
                        m_GPUPersistentInstanceBufferHandle = newHandle;
                        UpdateBatchBufferHandles();
                    }
                }
            }

            private void UpdateBatchBufferHandles()
            {
                foreach (var b in m_ExistingBatchIndices)
                {
                    m_ThreadedBatchContext.SetBatchBuffer(new BatchID { value = (uint)b }, m_GPUPersistentInstanceBufferHandle);
                }
            }
        }
    }
}

