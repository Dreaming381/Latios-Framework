using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct GenerateBrgDrawCommandsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_metaQuery;

        FindChunksWithVisibleJob m_findJob;
        EmitDrawCommandsJob      m_emitDrawCommandsJob;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_metaQuery = state.Fluent().WithAll<ChunkHeader>(true).WithAll<ChunkPerCameraCullingMask>(true).WithAll<ChunkPerCameraCullingSplitsMask>(true)
                          .WithAll<ChunkPerFrameCullingMask>(false).WithAll<EntitiesGraphicsChunkInfo>(true).Build();

            m_findJob = new FindChunksWithVisibleJob
            {
                perCameraCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkHeaderHandle          = state.GetComponentTypeHandle<ChunkHeader>(true),
                perFrameCullingMaskHandle  = state.GetComponentTypeHandle<ChunkPerFrameCullingMask>(false)
            };

            m_emitDrawCommandsJob = new EmitDrawCommandsJob
            {
                chunkPerCameraCullingMaskHandle       = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkPerCameraCullingSplitsMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingSplitsMask>(true),
                // = visibilityItems,
                EntitiesGraphicsChunkInfo = state.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                MaterialMeshInfo          = state.GetComponentTypeHandle<MaterialMeshInfo>(true),
                LocalToWorld              = state.GetComponentTypeHandle<LocalToWorld>(true),
                DepthSorted               = state.GetComponentTypeHandle<DepthSorted_Tag>(true),
                RenderFilterSettings      = state.GetSharedComponentTypeHandle<RenderFilterSettings>(),
                LightMaps                 = state.GetSharedComponentTypeHandle<LightMaps>(),
#if UNITY_EDITOR
                EditorDataComponentHandle = state.GetSharedComponentTypeHandle<EditorRenderData>(),
#endif
                ProfilerEmitChunk = new ProfilerMarker("EmitChunk"),
            };
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            var brgCullingContext = latiosWorld.worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>();
            var cullingContext    = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>();

            var chunkList = new NativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);

            m_findJob.chunkHeaderHandle.Update(ref state);
            m_findJob.chunksToProcess = chunkList.AsParallelWriter();
            m_findJob.perCameraCullingMaskHandle.Update(ref state);
            m_findJob.perFrameCullingMaskHandle.Update(ref state);
            state.Dependency = m_findJob.ScheduleParallelByRef(m_metaQuery, state.Dependency);

            // TODO: Dynamically estimate this based on past frames
            int binCountEstimate       = 1;
            var chunkDrawCommandOutput = new ChunkDrawCommandOutput(
                binCountEstimate,
                brgCullingContext.cullingThreadLocalAllocator,
                brgCullingContext.batchCullingOutput);

            m_emitDrawCommandsJob.chunkPerCameraCullingMaskHandle.Update(ref state);
            m_emitDrawCommandsJob.chunkPerCameraCullingSplitsMaskHandle.Update(ref state);
            m_emitDrawCommandsJob.EntitiesGraphicsChunkInfo.Update(ref state);
            m_emitDrawCommandsJob.MaterialMeshInfo.Update(ref state);
            m_emitDrawCommandsJob.LocalToWorld.Update(ref state);
            m_emitDrawCommandsJob.DepthSorted.Update(ref state);
            m_emitDrawCommandsJob.RenderFilterSettings.Update(ref state);
            m_emitDrawCommandsJob.LightMaps.Update(ref state);
#if UNITY_EDITOR
            m_emitDrawCommandsJob.EditorDataComponentHandle.Update(ref state);
            m_emitDrawCommandsJob.BatchEditorData = brgCullingContext.batchEditorSharedIndexToSceneMaskMap;
#endif
            m_emitDrawCommandsJob.CullingLayerMask  = cullingContext.cullingLayerMask;
            m_emitDrawCommandsJob.DrawCommandOutput = chunkDrawCommandOutput;
            m_emitDrawCommandsJob.SceneCullingMask  = cullingContext.sceneCullingMask;
            m_emitDrawCommandsJob.CameraPosition    = cullingContext.lodParameters.cameraPosition;
            m_emitDrawCommandsJob.LastSystemVersion = cullingContext.lastSystemVersionOfLatiosEntitiesGraphics;

            m_emitDrawCommandsJob.chunksToProcess = chunkList.AsDeferredJobArray();
            m_emitDrawCommandsJob.splitsAreValid  = cullingContext.viewType == BatchCullingViewType.Light;

            var allocateWorkItemsJob = new AllocateWorkItemsJob
            {
                DrawCommandOutput = chunkDrawCommandOutput,
            };

            var collectWorkItemsJob = new CollectWorkItemsJob
            {
                DrawCommandOutput = chunkDrawCommandOutput,
                ProfileCollect    = new ProfilerMarker("Collect"),
                ProfileWrite      = new ProfilerMarker("Write"),
            };

            var flushWorkItemsJob = new FlushWorkItemsJob
            {
                DrawCommandOutput = chunkDrawCommandOutput,
            };

            var allocateInstancesJob = new AllocateInstancesJob
            {
                DrawCommandOutput = chunkDrawCommandOutput,
            };

            var allocateDrawCommandsJob = new AllocateDrawCommandsJob
            {
                DrawCommandOutput = chunkDrawCommandOutput
            };

            var expandInstancesJob = new ExpandVisibleInstancesJob
            {
                DrawCommandOutput = chunkDrawCommandOutput,
            };

            var generateDrawCommandsJob = new GenerateDrawCommandsJob
            {
                DrawCommandOutput = chunkDrawCommandOutput,
            };

            var generateDrawRangesJob = new GenerateDrawRangesJob
            {
                DrawCommandOutput = chunkDrawCommandOutput,
                FilterSettings    = brgCullingContext.batchFilterSettingsByRenderFilterSettingsSharedIndex,
            };

            var emitDrawCommandsDependency = m_emitDrawCommandsJob.ScheduleByRef(chunkList, 1, state.Dependency);

            var collectGlobalBinsDependency =
                chunkDrawCommandOutput.BinCollector.ScheduleFinalize(emitDrawCommandsDependency);
            var sortBinsDependency = DrawBinSort.ScheduleBinSort(
                brgCullingContext.cullingThreadLocalAllocator.GeneralAllocator,
                chunkDrawCommandOutput.SortedBins,
                chunkDrawCommandOutput.UnsortedBins,
                collectGlobalBinsDependency);

            var allocateWorkItemsDependency = allocateWorkItemsJob.Schedule(collectGlobalBinsDependency);
            var collectWorkItemsDependency  = collectWorkItemsJob.ScheduleWithIndirectList(
                chunkDrawCommandOutput.UnsortedBins, 1, allocateWorkItemsDependency);

            var flushWorkItemsDependency =
                flushWorkItemsJob.Schedule(ChunkDrawCommandOutput.NumThreads, 1, collectWorkItemsDependency);

            var allocateInstancesDependency = allocateInstancesJob.Schedule(flushWorkItemsDependency);

            var allocateDrawCommandsDependency = allocateDrawCommandsJob.Schedule(
                JobHandle.CombineDependencies(sortBinsDependency, flushWorkItemsDependency));

            var allocationsDependency = JobHandle.CombineDependencies(
                allocateInstancesDependency,
                allocateDrawCommandsDependency);

            var expandInstancesDependency = expandInstancesJob.ScheduleWithIndirectList(
                chunkDrawCommandOutput.WorkItems,
                1,
                allocateInstancesDependency);
            var generateDrawCommandsDependency = generateDrawCommandsJob.ScheduleWithIndirectList(
                chunkDrawCommandOutput.SortedBins,
                1,
                allocationsDependency);
            var generateDrawRangesDependency = generateDrawRangesJob.Schedule(allocateDrawCommandsDependency);

            var expansionDependency = JobHandle.CombineDependencies(
                expandInstancesDependency,
                generateDrawCommandsDependency,
                generateDrawRangesDependency);

            state.Dependency = chunkDrawCommandOutput.Dispose(expansionDependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct FindChunksWithVisibleJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               chunkHeaderHandle;

            public ComponentTypeHandle<ChunkPerFrameCullingMask> perFrameCullingMaskHandle;

            public NativeList<ArchetypeChunk>.ParallelWriter chunksToProcess;

            [Unity.Burst.CompilerServices.SkipLocalsInit]
            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunksCache = stackalloc ArchetypeChunk[128];
                int chunksCount = 0;
                var masks       = metaChunk.GetNativeArray(ref perCameraCullingMaskHandle);
                var headers     = metaChunk.GetNativeArray(ref chunkHeaderHandle);
                var frameMask   = (ChunkPerFrameCullingMask*)metaChunk.GetComponentDataPtrRW(ref perFrameCullingMaskHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var mask = masks[i];
                    if ((mask.lower.Value | mask.upper.Value) != 0)
                    {
                        chunksCache[chunksCount] = headers[i].ArchetypeChunk;
                        chunksCount++;
                    }

                    frameMask[i].lower.Value |= mask.lower.Value;
                    frameMask[i].upper.Value |= mask.upper.Value;
                }

                if (chunksCount > 0)
                {
                    chunksToProcess.AddRangeNoResize(chunksCache, chunksCount);
                }
            }
        }

        [BurstCompile]
        unsafe struct EmitDrawCommandsJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>                          chunksToProcess;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>       chunkPerCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingSplitsMask> chunkPerCameraCullingSplitsMaskHandle;
            public bool                                                            splitsAreValid;

            //[ReadOnly] public IndirectList<ChunkVisibilityItem> VisibilityItems;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo>  EntitiesGraphicsChunkInfo;
            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo>           MaterialMeshInfo;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>               LocalToWorld;
            [ReadOnly] public ComponentTypeHandle<DepthSorted_Tag>            DepthSorted;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> RenderFilterSettings;
            [ReadOnly] public SharedComponentTypeHandle<LightMaps>            LightMaps;

            public ChunkDrawCommandOutput DrawCommandOutput;

            public ulong  SceneCullingMask;
            public float3 CameraPosition;
            public uint   LastSystemVersion;
            public uint   CullingLayerMask;

            public ProfilerMarker ProfilerEmitChunk;

#if UNITY_EDITOR
            [ReadOnly] public SharedComponentTypeHandle<EditorRenderData> EditorDataComponentHandle;
            [ReadOnly] public NativeParallelHashMap<int, BatchEditorRenderData> BatchEditorData;
#endif

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                //var visibilityItem = VisibilityItems.ElementAt(index);

                //var chunkVisibility = visibilityItem.Visibility;

                int filterIndex = chunk.GetSharedComponentIndex(RenderFilterSettings);

                DrawCommandOutput.InitializeForEmitThread();

                {
                    var entitiesGraphicsChunkInfo = chunk.GetChunkComponentData(ref EntitiesGraphicsChunkInfo);

                    if (!entitiesGraphicsChunkInfo.Valid)
                        return;

                    ref var chunkCullingData = ref entitiesGraphicsChunkInfo.CullingData;

                    int batchIndex = entitiesGraphicsChunkInfo.BatchIndex;

                    var  materialMeshInfos = chunk.GetNativeArray(ref MaterialMeshInfo);
                    var  localToWorlds     = chunk.GetNativeArray(ref LocalToWorld);
                    bool isDepthSorted     = chunk.Has(ref DepthSorted);
                    bool isLightMapped     = chunk.GetSharedComponentIndex(LightMaps) >= 0;

                    // Check if the chunk has statically disabled motion (i.e. never in motion pass)
                    // or enabled motion (i.e. in motion pass if there was actual motion or force-to-zero).
                    // We make sure to never set the motion flag if motion is statically disabled to improve batching
                    // in cases where the transform is changed.
                    bool hasMotion = (chunkCullingData.Flags & EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion) != 0;

                    if (hasMotion)
                    {
                        bool orderChanged     = chunk.DidOrderChange(LastSystemVersion);
                        bool transformChanged = chunk.DidChange(ref LocalToWorld, LastSystemVersion);
#if ENABLE_DOTS_DEFORMATION_MOTION_VECTORS
                        bool isDeformed = chunk.Has(ref DeformedMeshIndex);
#else
                        bool isDeformed = false;
#endif
                        hasMotion = orderChanged || transformChanged || isDeformed;
                    }

                    int chunkStartIndex = entitiesGraphicsChunkInfo.CullingData.ChunkOffsetInBatch;

                    var mask       = chunk.GetChunkComponentRefRO(in chunkPerCameraCullingMaskHandle);
                    var splitsMask = chunk.GetChunkComponentRefRO(in chunkPerCameraCullingSplitsMaskHandle);

                    for (int j = 0; j < 2; j++)
                    {
                        ulong visibleWord = mask.ValueRO.GetUlongFromIndex(j);

                        while (visibleWord != 0)
                        {
                            int   bitIndex    = math.tzcnt(visibleWord);
                            int   entityIndex = (j << 6) + bitIndex;
                            ulong entityMask  = 1ul << bitIndex;

                            // Clear the bit first in case we early out from the loop
                            visibleWord ^= entityMask;

                            var materialMeshInfo = materialMeshInfos[entityIndex];

                            // Null materials are handled internally by Unity using the error material if available.
                            // Invalid meshes at this point will be skipped.
                            if (materialMeshInfo.Mesh <= 0)
                                continue;

                            bool flipWinding = (chunkCullingData.FlippedWinding[j] & entityMask) != 0;

                            var settings = new DrawCommandSettings
                            {
                                FilterIndex  = filterIndex,
                                BatchID      = new BatchID { value = (uint)batchIndex },
                                MaterialID   = materialMeshInfo.MaterialID,
                                MeshID       = materialMeshInfo.MeshID,
                                SplitMask    = splitsAreValid ? splitsMask.ValueRO.splitMasks[entityIndex] : (ushort)0,  // Todo: Should the default be 1 instead of 0?
                                SubmeshIndex = (ushort)materialMeshInfo.Submesh,
                                Flags        = 0
                            };

                            if (flipWinding)
                                settings.Flags |= BatchDrawCommandFlags.FlipWinding;

                            if (hasMotion)
                                settings.Flags |= BatchDrawCommandFlags.HasMotion;

                            if (isLightMapped)
                                settings.Flags |= BatchDrawCommandFlags.IsLightMapped;

                            // Depth sorted draws are emitted with access to entity transforms,
                            // so they can also be written out for sorting
                            if (isDepthSorted)
                            {
                                settings.Flags |= BatchDrawCommandFlags.HasSortingPosition;
                                DrawCommandOutput.EmitDepthSorted(settings, j, bitIndex, chunkStartIndex,
                                                                  (float4x4*)localToWorlds.GetUnsafeReadOnlyPtr());
                            }
                            else
                            {
                                DrawCommandOutput.Emit(settings, j, bitIndex, chunkStartIndex);
                            }
                        }
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct GenerateDrawCommandsJob : IJobParallelForDefer
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            public void Execute(int index)
            {
                var sortedBin = DrawCommandOutput.SortedBins.ElementAt(index);
                var settings  = DrawCommandOutput.UnsortedBins.ElementAt(sortedBin);
                var bin       = DrawCommandOutput.BinIndices.ElementAt(sortedBin);

                bool hasSortingPosition = settings.HasSortingPosition;
                uint maxPerCommand      = hasSortingPosition ?
                                          1u :
                                          EntitiesGraphicsTuningConstants.kMaxInstancesPerDrawCommand;
                uint numInstances    = (uint)bin.NumInstances;
                int  numDrawCommands = bin.NumDrawCommands;

                uint drawInstanceOffset      = (uint)bin.InstanceOffset;
                uint drawPositionFloatOffset = (uint)bin.PositionOffset * 3;  // 3 floats per position

                var cullingOutput = DrawCommandOutput.CullingOutputDrawCommands;
                var draws         = cullingOutput->drawCommands;

                for (int i = 0; i < numDrawCommands; ++i)
                {
                    var draw = new BatchDrawCommand
                    {
                        visibleOffset       = drawInstanceOffset,
                        visibleCount        = math.min(maxPerCommand, numInstances),
                        batchID             = settings.BatchID,
                        materialID          = settings.MaterialID,
                        meshID              = settings.MeshID,
                        submeshIndex        = (ushort)settings.SubmeshIndex,
                        splitVisibilityMask = settings.SplitMask,
                        flags               = settings.Flags,
                        sortingPosition     = hasSortingPosition ?
                                              (int)drawPositionFloatOffset :
                                              0,
                    };

                    int drawCommandIndex    = bin.DrawCommandOffset + i;
                    draws[drawCommandIndex] = draw;

                    drawInstanceOffset      += draw.visibleCount;
                    drawPositionFloatOffset += draw.visibleCount * 3;
                    numInstances            -= draw.visibleCount;
                }
            }
        }

        [BurstCompile]
        internal unsafe struct GenerateDrawRangesJob : IJob
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            [ReadOnly] public NativeParallelHashMap<int, BatchFilterSettings> FilterSettings;

            private const int MaxInstances = EntitiesGraphicsTuningConstants.kMaxInstancesPerDrawRange;
            private const int MaxCommands  = EntitiesGraphicsTuningConstants.kMaxDrawCommandsPerDrawRange;

            private int m_PrevFilterIndex;
            private int m_CommandsInRange;
            private int m_InstancesInRange;

            public void Execute()
            {
                int numBins = DrawCommandOutput.SortedBins.Length;
                var output  = DrawCommandOutput.CullingOutputDrawCommands;

                ref int rangeCount = ref output->drawRangeCount;
                var     ranges     = output->drawRanges;

                rangeCount         = 0;
                m_PrevFilterIndex  = -1;
                m_CommandsInRange  = 0;
                m_InstancesInRange = 0;

                for (int i = 0; i < numBins; ++i)
                {
                    var sortedBin = DrawCommandOutput.SortedBins.ElementAt(i);
                    var settings  = DrawCommandOutput.UnsortedBins.ElementAt(sortedBin);
                    var bin       = DrawCommandOutput.BinIndices.ElementAt(sortedBin);

                    int  numInstances       = bin.NumInstances;
                    int  drawCommandOffset  = bin.DrawCommandOffset;
                    int  numDrawCommands    = bin.NumDrawCommands;
                    int  filterIndex        = settings.FilterIndex;
                    bool hasSortingPosition = settings.HasSortingPosition;

                    for (int j = 0; j < numDrawCommands; ++j)
                    {
                        int instancesInCommand = math.min(numInstances, DrawCommandBin.MaxInstancesPerCommand);

                        AccumulateDrawRange(
                            ref rangeCount,
                            ranges,
                            drawCommandOffset,
                            instancesInCommand,
                            filterIndex,
                            hasSortingPosition);

                        ++drawCommandOffset;
                        numInstances -= instancesInCommand;
                    }
                }

                UnityEngine.Debug.Assert(rangeCount <= output->drawCommandCount);
            }

            private void AccumulateDrawRange(
                ref int rangeCount,
                BatchDrawRange* ranges,
                int drawCommandOffset,
                int numInstances,
                int filterIndex,
                bool hasSortingPosition)
            {
                bool isFirst = rangeCount == 0;

                bool addNewCommand;

                if (isFirst)
                {
                    addNewCommand = true;
                }
                else
                {
                    int newInstanceCount = m_InstancesInRange + numInstances;
                    int newCommandCount  = m_CommandsInRange + 1;

                    bool sameFilter       = filterIndex == m_PrevFilterIndex;
                    bool tooManyInstances = newInstanceCount > MaxInstances;
                    bool tooManyCommands  = newCommandCount > MaxCommands;

                    addNewCommand = !sameFilter || tooManyInstances || tooManyCommands;
                }

                if (addNewCommand)
                {
                    ranges[rangeCount] = new BatchDrawRange
                    {
                        filterSettings    = FilterSettings[filterIndex],
                        drawCommandsBegin = (uint)drawCommandOffset,
                        drawCommandsCount = 1,
                    };

                    ranges[rangeCount].filterSettings.allDepthSorted = hasSortingPosition;

                    m_PrevFilterIndex  = filterIndex;
                    m_CommandsInRange  = 1;
                    m_InstancesInRange = numInstances;

                    ++rangeCount;
                }
                else
                {
                    ref var range = ref ranges[rangeCount - 1];

                    ++range.drawCommandsCount;
                    range.filterSettings.allDepthSorted &= hasSortingPosition;

                    ++m_CommandsInRange;
                    m_InstancesInRange += numInstances;
                }
            }
        }
    }
}

