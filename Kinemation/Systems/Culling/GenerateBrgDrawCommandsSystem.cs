using Latios.Transforms;
using Unity.Assertions;
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
using UnityEngine;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

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
        ProfilerMarker           m_profilerEmitChunk;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_metaQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkPerCameraCullingMask>(true).With<ChunkPerCameraCullingSplitsMask>(true)
                          .With<ChunkPerFrameCullingMask>(false).With<EntitiesGraphicsChunkInfo>(true).Build();

            m_findJob = new FindChunksWithVisibleJob
            {
                perCameraCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkHeaderHandle          = state.GetComponentTypeHandle<ChunkHeader>(true),
                perFrameCullingMaskHandle  = state.GetComponentTypeHandle<ChunkPerFrameCullingMask>(false)
            };

            m_profilerEmitChunk = new ProfilerMarker("EmitChunk");
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

            var emitDrawCommandsJob = new EmitDrawCommandsJob
            {
                BRGRenderMeshArrays                   = brgCullingContext.brgRenderMeshArrays,
                CameraPosition                        = cullingContext.lodParameters.cameraPosition,
                chunkPerCameraCullingMaskHandle       = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkPerCameraCullingSplitsMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingSplitsMask>(true),
                chunksToProcess                       = chunkList.AsDeferredJobArray(),
                CullingLayerMask                      = cullingContext.cullingLayerMask,
                DepthSorted                           = GetComponentTypeHandle<DepthSorted_Tag>(true),
                DrawCommandOutput                     = chunkDrawCommandOutput,
#if UNITY_EDITOR
                EditorDataComponentHandle = GetSharedComponentTypeHandle<EditorRenderData>(),
#endif
                EntitiesGraphicsChunkInfo = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                LastSystemVersion         = state.LastSystemVersion,
                LightMaps                 = ManagedAPI.GetSharedComponentTypeHandle<LightMaps>(),
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                WorldTransform = GetComponentTypeHandle<WorldTransform>(true),
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                WorldTransform = GetComponentTypeHandle<Unity.Transforms.LocalToWorld>(true),
#endif
                PostProcessMatrix    = GetComponentTypeHandle<PostProcessMatrix>(true),
                MaterialMeshInfo     = GetComponentTypeHandle<MaterialMeshInfo>(true),
                ProfilerEmitChunk    = m_profilerEmitChunk,
                RenderFilterSettings = GetSharedComponentTypeHandle<RenderFilterSettings>(),
                RenderMeshArray      = ManagedAPI.GetSharedComponentTypeHandle<RenderMeshArray>(),
                SceneCullingMask     = cullingContext.sceneCullingMask,
                splitsAreValid       = cullingContext.viewType == BatchCullingViewType.Light,
            };

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

            var emitDrawCommandsDependency = emitDrawCommandsJob.ScheduleByRef(chunkList, 1, state.Dependency);

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

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
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
            [ReadOnly] public ComponentTypeHandle<WorldTransform>             WorldTransform;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>          PostProcessMatrix;
            [ReadOnly] public ComponentTypeHandle<DepthSorted_Tag>            DepthSorted;
            [ReadOnly] public SharedComponentTypeHandle<RenderMeshArray>      RenderMeshArray;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> RenderFilterSettings;
            [ReadOnly] public SharedComponentTypeHandle<LightMaps>            LightMaps;
            [ReadOnly] public NativeParallelHashMap<int, BRGRenderMeshArray>  BRGRenderMeshArrays;

            public ChunkDrawCommandOutput DrawCommandOutput;

            public ulong  SceneCullingMask;
            public float3 CameraPosition;
            public uint   LastSystemVersion;
            public uint   CullingLayerMask;

            public ProfilerMarker ProfilerEmitChunk;

#if UNITY_EDITOR
            [ReadOnly] public SharedComponentTypeHandle<EditorRenderData> EditorDataComponentHandle;
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

                    // If the chunk has a RenderMeshArray, get access to the corresponding registered
                    // Material and Mesh IDs
                    BRGRenderMeshArray brgRenderMeshArray = default;
                    if (!BRGRenderMeshArrays.IsEmpty)
                    {
                        int  renderMeshArrayIndex = chunk.GetSharedComponentIndex(RenderMeshArray);
                        bool hasRenderMeshArray   = renderMeshArrayIndex >= 0;
                        if (hasRenderMeshArray)
                            BRGRenderMeshArrays.TryGetValue(renderMeshArrayIndex, out brgRenderMeshArray);
                    }

                    ref var chunkCullingData = ref entitiesGraphicsChunkInfo.CullingData;

                    int batchIndex = entitiesGraphicsChunkInfo.BatchIndex;

                    var  materialMeshInfos   = chunk.GetNativeArray(ref MaterialMeshInfo);
                    var  worldTransforms     = chunk.GetNativeArray(ref WorldTransform);
                    var  postProcessMatrices = chunk.GetNativeArray(ref PostProcessMatrix);
                    bool hasPostProcess      = chunk.Has(ref PostProcessMatrix);
                    bool isDepthSorted       = chunk.Has(ref DepthSorted);
                    bool isLightMapped       = chunk.GetSharedComponentIndex(LightMaps) >= 0;

                    // Check if the chunk has statically disabled motion (i.e. never in motion pass)
                    // or enabled motion (i.e. in motion pass if there was actual motion or force-to-zero).
                    // We make sure to never set the motion flag if motion is statically disabled to improve batching
                    // in cases where the transform is changed.
                    bool hasMotion = (chunkCullingData.Flags & EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion) != 0;

                    if (hasMotion)
                    {
                        bool orderChanged     = chunk.DidOrderChange(LastSystemVersion);
                        bool transformChanged = chunk.DidChange(ref WorldTransform, LastSystemVersion);
                        if (hasPostProcess)
                            transformChanged |= chunk.DidChange(ref PostProcessMatrix, LastSystemVersion);
#if ENABLE_DOTS_DEFORMATION_MOTION_VECTORS
                        bool isDeformed = chunk.Has(ref DeformedMeshIndex);
#else
                        bool isDeformed = false;
#endif
                        hasMotion = orderChanged || transformChanged || isDeformed;
                    }

                    int chunkStartIndex = entitiesGraphicsChunkInfo.CullingData.ChunkOffsetInBatch;

                    var mask       = chunk.GetChunkComponentRefRO(ref chunkPerCameraCullingMaskHandle);
                    var splitsMask = chunk.GetChunkComponentRefRO(ref chunkPerCameraCullingSplitsMaskHandle);

                    TransformQvvs* depthSortingTransformsPtr = null;
                    if (isDepthSorted && hasPostProcess)
                    {
                        // In this case, we don't actually have a component that represents the rendered position.
                        // So we allocate a new array and compute the world positions. We store them in TransformQvvs
                        // so that the read pointer looks the same as our WorldTransforms.
                        // We compute them in the inner loop since only the visible instances are read from later,
                        // and it is a lot cheaper to only compute the visible instances.
                        var allocator             = DrawCommandOutput.ThreadLocalAllocator.ThreadAllocator(DrawCommandOutput.ThreadIndex)->Handle;
                        depthSortingTransformsPtr = AllocatorManager.Allocate<TransformQvvs>(allocator, chunk.Count);
                    }
                    else if (isDepthSorted)
                    {
                        depthSortingTransformsPtr = (TransformQvvs*)worldTransforms.GetUnsafeReadOnlyPtr();
                    }

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

                            MaterialMeshInfo materialMeshInfo = materialMeshInfos[entityIndex];
                            BatchID          batchID          = new BatchID { value = (uint)batchIndex };
                            ushort           splitMask        = splitsAreValid ? splitsMask.ValueRO.splitMasks[entityIndex] : (ushort)0;  // Todo: Should the default be 1 instead of 0?
                            bool             flipWinding      = (chunkCullingData.FlippedWinding[j] & entityMask) != 0;

                            BatchDrawCommandFlags drawCommandFlags = 0;

                            if (flipWinding)
                                drawCommandFlags |= BatchDrawCommandFlags.FlipWinding;

                            if (hasMotion)
                                drawCommandFlags |= BatchDrawCommandFlags.HasMotion;

                            if (isLightMapped)
                                drawCommandFlags |= BatchDrawCommandFlags.IsLightMapped;

                            // Depth sorted draws are emitted with access to entity transforms,
                            // so they can also be written out for sorting
                            if (isDepthSorted)
                            {
                                drawCommandFlags |= BatchDrawCommandFlags.HasSortingPosition;
                                // To maintain compatibility with most of the data structures, we pretend we have a LocalToWorld matrix pointer.
                                // We also customize the code where this pointer is read.
                                if (hasPostProcess)
                                {
                                    var index = j * 64 + bitIndex;
                                    var f4x4  = new float4x4(new float4(postProcessMatrices[index].postProcessMatrix.c0, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c1, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c2, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c3, 1f));
                                    depthSortingTransformsPtr[index].position = math.transform(f4x4, worldTransforms[index].position);
                                }
                            }

                            if (materialMeshInfo.HasMaterialMeshIndexRange)
                            {
                                RangeInt matMeshIndexRange = materialMeshInfo.MaterialMeshIndexRange;

                                for (int i = 0; i < matMeshIndexRange.length; i++)
                                {
                                    int matMeshSubMeshIndex = matMeshIndexRange.start + i;

                                    // Drop the draw command if OOB. Errors should have been reported already so no need to log anything
                                    if (matMeshSubMeshIndex >= brgRenderMeshArray.MaterialMeshSubMeshes.Length)
                                        continue;

                                    BatchMaterialMeshSubMesh matMeshSubMesh = brgRenderMeshArray.MaterialMeshSubMeshes[matMeshSubMeshIndex];

                                    DrawCommandSettings settings = new DrawCommandSettings
                                    {
                                        FilterIndex  = filterIndex,
                                        BatchID      = batchID,
                                        MaterialID   = matMeshSubMesh.Material,
                                        MeshID       = matMeshSubMesh.Mesh,
                                        SplitMask    = splitMask,
                                        SubMeshIndex = (ushort)matMeshSubMesh.SubMeshIndex,
                                        Flags        = drawCommandFlags
                                    };

                                    EmitDrawCommand(settings, j, bitIndex, chunkStartIndex, depthSortingTransformsPtr);
                                }
                            }
                            else
                            {
                                BatchMeshID meshID = materialMeshInfo.IsRuntimeMesh ?
                                                     materialMeshInfo.MeshID :
                                                     brgRenderMeshArray.GetMeshID(materialMeshInfo);

                                // Invalid meshes at this point will be skipped.
                                if (meshID == BatchMeshID.Null)
                                    continue;

                                // Null materials are handled internally by Unity using the error material if available.
                                BatchMaterialID materialID = materialMeshInfo.IsRuntimeMaterial ?
                                                             materialMeshInfo.MaterialID :
                                                             brgRenderMeshArray.GetMaterialID(materialMeshInfo);

                                var settings = new DrawCommandSettings
                                {
                                    FilterIndex  = filterIndex,
                                    BatchID      = batchID,
                                    MaterialID   = materialID,
                                    MeshID       = meshID,
                                    SplitMask    = splitMask,
                                    SubMeshIndex = (ushort)materialMeshInfo.SubMesh,
                                    Flags        = drawCommandFlags
                                };

                                EmitDrawCommand(settings, j, bitIndex, chunkStartIndex, depthSortingTransformsPtr);
                            }
                        }
                    }
                }
            }

            private void EmitDrawCommand(in DrawCommandSettings settings, int entityQword, int entityBit, int chunkStartIndex, void* depthSortingPtr)
            {
                // Depth sorted draws are emitted with access to entity transforms,
                // so they can also be written out for sorting
                if (settings.HasSortingPosition)
                {
                    DrawCommandOutput.EmitDepthSorted(settings, entityQword, entityBit, chunkStartIndex, (float4x4*)depthSortingPtr);
                }
                else
                {
                    DrawCommandOutput.Emit(settings, entityQword, entityBit, chunkStartIndex);
                }
            }
        }

        [BurstCompile]
        internal unsafe struct ExpandVisibleInstancesJob : IJobParallelForDefer
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            public void Execute(int index)
            {
                var workItem        = DrawCommandOutput.WorkItems.ElementAt(index);
                var header          = workItem.Arrays;
                var transformHeader = workItem.TransformArrays;
                int binIndex        = workItem.BinIndex;

                var bin                    = DrawCommandOutput.BinIndices.ElementAt(binIndex);
                int binInstanceOffset      = bin.InstanceOffset;
                int binPositionOffset      = bin.PositionOffset;
                int workItemInstanceOffset = workItem.PrefixSumNumInstances;
                int headerInstanceOffset   = 0;

                int*    visibleInstances = DrawCommandOutput.CullingOutputDrawCommands->visibleInstances;
                float3* sortingPositions = (float3*)DrawCommandOutput.CullingOutputDrawCommands->instanceSortingPositions;

                if (transformHeader == null)
                {
                    while (header != null)
                    {
                        ExpandArray(
                            visibleInstances,
                            header,
                            binInstanceOffset + workItemInstanceOffset + headerInstanceOffset);

                        headerInstanceOffset += header->NumInstances;
                        header                = header->Next;
                    }
                }
                else
                {
                    while (header != null)
                    {
                        Assert.IsTrue(transformHeader != null);

                        int instanceOffset = binInstanceOffset + workItemInstanceOffset + headerInstanceOffset;
                        int positionOffset = binPositionOffset + workItemInstanceOffset + headerInstanceOffset;

                        ExpandArrayWithPositions(
                            visibleInstances,
                            sortingPositions,
                            header,
                            transformHeader,
                            instanceOffset,
                            positionOffset);

                        headerInstanceOffset += header->NumInstances;
                        header                = header->Next;
                        transformHeader       = transformHeader->Next;
                    }
                }
            }

            private int ExpandArray(
                int*                                      visibleInstances,
                DrawStream<DrawCommandVisibility>.Header* header,
                int instanceOffset)
            {
                int numStructs = header->NumElements;

                for (int i = 0; i < numStructs; ++i)
                {
                    var visibility   = *header->Element(i);
                    int numInstances = ExpandVisibility(visibleInstances + instanceOffset, visibility);
                    Assert.IsTrue(numInstances > 0);
                    instanceOffset += numInstances;
                }

                return instanceOffset;
            }

            private int ExpandArrayWithPositions(
                int*                                      visibleInstances,
                float3*                                   sortingPositions,
                DrawStream<DrawCommandVisibility>.Header* header,
                DrawStream<System.IntPtr>.Header*         transformHeader,
                int instanceOffset,
                int positionOffset)
            {
                int numStructs = header->NumElements;

                for (int i = 0; i < numStructs; ++i)
                {
                    var visibility   = *header->Element(i);
                    var transforms   = (TransformQvvs*)(*transformHeader->Element(i));
                    int numInstances = ExpandVisibilityWithPositions(
                        visibleInstances + instanceOffset,
                        sortingPositions + positionOffset,
                        visibility,
                        transforms);
                    Assert.IsTrue(numInstances > 0);
                    instanceOffset += numInstances;
                    positionOffset += numInstances;
                }

                return instanceOffset;
            }

            private int ExpandVisibility(int* outputInstances, DrawCommandVisibility visibility)
            {
                int numInstances = 0;
                int startIndex   = visibility.ChunkStartIndex;

                for (int i = 0; i < 2; ++i)
                {
                    ulong qword = visibility.VisibleInstances[i];
                    while (qword != 0)
                    {
                        int   bitIndex                 = math.tzcnt(qword);
                        ulong mask                     = 1ul << bitIndex;
                        qword                         ^= mask;
                        int instanceIndex              = (i << 6) + bitIndex;
                        int visibilityIndex            = startIndex + instanceIndex;
                        outputInstances[numInstances]  = visibilityIndex;
                        ++numInstances;
                    }
                }

                return numInstances;
            }

            private int ExpandVisibilityWithPositions(
                int*                  outputInstances,
                float3*               outputSortingPosition,
                DrawCommandVisibility visibility,
                TransformQvvs*        transforms)
            {
                int numInstances = 0;
                int startIndex   = visibility.ChunkStartIndex;

                for (int i = 0; i < 2; ++i)
                {
                    ulong qword = visibility.VisibleInstances[i];
                    while (qword != 0)
                    {
                        int   bitIndex     = math.tzcnt(qword);
                        ulong mask         = 1ul << bitIndex;
                        qword             ^= mask;
                        int instanceIndex  = (i << 6) + bitIndex;

                        int visibilityIndex                 = startIndex + instanceIndex;
                        outputInstances[numInstances]       = visibilityIndex;
                        outputSortingPosition[numInstances] = transforms[instanceIndex].position;

                        ++numInstances;
                    }
                }

                return numInstances;
            }
        }
#endif

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        [BurstCompile]
        unsafe struct EmitDrawCommandsJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> chunkPerCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingSplitsMask> chunkPerCameraCullingSplitsMaskHandle;
            public bool splitsAreValid;

            //[ReadOnly] public IndirectList<ChunkVisibilityItem> VisibilityItems;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo> MaterialMeshInfo;
            [ReadOnly] public ComponentTypeHandle<Unity.Transforms.LocalToWorld> WorldTransform;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix> PostProcessMatrix;
            [ReadOnly] public ComponentTypeHandle<DepthSorted_Tag> DepthSorted;
            [ReadOnly] public SharedComponentTypeHandle<RenderMeshArray> RenderMeshArray;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> RenderFilterSettings;
            [ReadOnly] public SharedComponentTypeHandle<LightMaps> LightMaps;
            [ReadOnly] public NativeParallelHashMap<int, BRGRenderMeshArray> BRGRenderMeshArrays;

            public ChunkDrawCommandOutput DrawCommandOutput;

            public ulong SceneCullingMask;
            public float3 CameraPosition;
            public uint LastSystemVersion;
            public uint CullingLayerMask;

            public ProfilerMarker ProfilerEmitChunk;

#if UNITY_EDITOR
            [ReadOnly] public SharedComponentTypeHandle<EditorRenderData> EditorDataComponentHandle;
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

                    // If the chunk has a RenderMeshArray, get access to the corresponding registered
                    // Material and Mesh IDs
                    BRGRenderMeshArray brgRenderMeshArray = default;
                    if (!BRGRenderMeshArrays.IsEmpty)
                    {
                        int renderMeshArrayIndex = chunk.GetSharedComponentIndex(RenderMeshArray);
                        bool hasRenderMeshArray   = renderMeshArrayIndex >= 0;
                        if (hasRenderMeshArray)
                            BRGRenderMeshArrays.TryGetValue(renderMeshArrayIndex, out brgRenderMeshArray);
                    }

                    ref var chunkCullingData = ref entitiesGraphicsChunkInfo.CullingData;

                    int batchIndex = entitiesGraphicsChunkInfo.BatchIndex;

                    var materialMeshInfos   = chunk.GetNativeArray(ref MaterialMeshInfo);
                    var worldTransforms     = chunk.GetNativeArray(ref WorldTransform);
                    var postProcessMatrices = chunk.GetNativeArray(ref PostProcessMatrix);
                    bool hasPostProcess      = chunk.Has(ref PostProcessMatrix);
                    bool isDepthSorted       = chunk.Has(ref DepthSorted);
                    bool isLightMapped       = chunk.GetSharedComponentIndex(LightMaps) >= 0;

                    // Check if the chunk has statically disabled motion (i.e. never in motion pass)
                    // or enabled motion (i.e. in motion pass if there was actual motion or force-to-zero).
                    // We make sure to never set the motion flag if motion is statically disabled to improve batching
                    // in cases where the transform is changed.
                    bool hasMotion = (chunkCullingData.Flags & EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion) != 0;

                    if (hasMotion)
                    {
                        bool orderChanged     = chunk.DidOrderChange(LastSystemVersion);
                        bool transformChanged = chunk.DidChange(ref WorldTransform, LastSystemVersion);
                        if (hasPostProcess)
                            transformChanged |= chunk.DidChange(ref PostProcessMatrix, LastSystemVersion);
#if ENABLE_DOTS_DEFORMATION_MOTION_VECTORS
                        bool isDeformed = chunk.Has(ref DeformedMeshIndex);
#else
                        bool isDeformed = false;
#endif
                        hasMotion = orderChanged || transformChanged || isDeformed;
                    }

                    int chunkStartIndex = entitiesGraphicsChunkInfo.CullingData.ChunkOffsetInBatch;

                    var mask       = chunk.GetChunkComponentRefRO(ref chunkPerCameraCullingMaskHandle);
                    var splitsMask = chunk.GetChunkComponentRefRO(ref chunkPerCameraCullingSplitsMaskHandle);

                    float4x4* depthSortingTransformsPtr = null;
                    if (isDepthSorted && hasPostProcess)
                    {
                        // In this case, we don't actually have a component that represents the rendered position.
                        // So we allocate a new array and compute the world positions. We store them in TransformQvvs
                        // so that the read pointer looks the same as our WorldTransforms.
                        // We compute them in the inner loop since only the visible instances are read from later,
                        // and it is a lot cheaper to only compute the visible instances.
                        var allocator = DrawCommandOutput.ThreadLocalAllocator.ThreadAllocator(DrawCommandOutput.ThreadIndex)->Handle;
                        depthSortingTransformsPtr = AllocatorManager.Allocate<float4x4>(allocator, chunk.Count);
                    }
                    else if (isDepthSorted)
                    {
                        depthSortingTransformsPtr = (float4x4*)worldTransforms.GetUnsafeReadOnlyPtr();
                    }

                    for (int j = 0; j < 2; j++)
                    {
                        ulong visibleWord = mask.ValueRO.GetUlongFromIndex(j);

                        while (visibleWord != 0)
                        {
                            int bitIndex    = math.tzcnt(visibleWord);
                            int entityIndex = (j << 6) + bitIndex;
                            ulong entityMask  = 1ul << bitIndex;

                            // Clear the bit first in case we early out from the loop
                            visibleWord ^= entityMask;

                            MaterialMeshInfo materialMeshInfo = materialMeshInfos[entityIndex];
                            BatchID batchID          = new BatchID { value = (uint)batchIndex };
                            ushort splitMask        = splitsAreValid ? splitsMask.ValueRO.splitMasks[entityIndex] : (ushort)0;  // Todo: Should the default be 1 instead of 0?
                            bool flipWinding      = (chunkCullingData.FlippedWinding[j] & entityMask) != 0;

                            BatchDrawCommandFlags drawCommandFlags = 0;

                            if (flipWinding)
                                drawCommandFlags |= BatchDrawCommandFlags.FlipWinding;

                            if (hasMotion)
                                drawCommandFlags |= BatchDrawCommandFlags.HasMotion;

                            if (isLightMapped)
                                drawCommandFlags |= BatchDrawCommandFlags.IsLightMapped;

                            // Depth sorted draws are emitted with access to entity transforms,
                            // so they can also be written out for sorting
                            if (isDepthSorted)
                            {
                                drawCommandFlags |= BatchDrawCommandFlags.HasSortingPosition;
                                // To maintain compatibility with most of the data structures, we pretend we have a LocalToWorld matrix pointer.
                                // We also customize the code where this pointer is read.
                                if (hasPostProcess)
                                {
                                    var index = j * 64 + bitIndex;
                                    var f4x4  = new float4x4(new float4(postProcessMatrices[index].postProcessMatrix.c0, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c1, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c2, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c3, 1f));
                                    depthSortingTransformsPtr[index].c3.xyz = math.transform(f4x4, worldTransforms[index].Position);
                                }
                            }

                            if (materialMeshInfo.HasMaterialMeshIndexRange)
                            {
                                RangeInt matMeshIndexRange = materialMeshInfo.MaterialMeshIndexRange;

                                for (int i = 0; i < matMeshIndexRange.length; i++)
                                {
                                    int matMeshSubMeshIndex = matMeshIndexRange.start + i;

                                    // Drop the draw command if OOB. Errors should have been reported already so no need to log anything
                                    if (matMeshSubMeshIndex >= brgRenderMeshArray.MaterialMeshSubMeshes.Length)
                                        continue;

                                    BatchMaterialMeshSubMesh matMeshSubMesh = brgRenderMeshArray.MaterialMeshSubMeshes[matMeshSubMeshIndex];

                                    DrawCommandSettings settings = new DrawCommandSettings
                                    {
                                        FilterIndex  = filterIndex,
                                        BatchID      = batchID,
                                        MaterialID   = matMeshSubMesh.Material,
                                        MeshID       = matMeshSubMesh.Mesh,
                                        SplitMask    = splitMask,
                                        SubMeshIndex = (ushort)matMeshSubMesh.SubMeshIndex,
                                        Flags        = drawCommandFlags
                                    };

                                    EmitDrawCommand(settings, j, bitIndex, chunkStartIndex, depthSortingTransformsPtr);
                                }
                            }
                            else
                            {
                                BatchMeshID meshID = materialMeshInfo.IsRuntimeMesh ?
                                                     materialMeshInfo.MeshID :
                                                     brgRenderMeshArray.GetMeshID(materialMeshInfo);

                                // Invalid meshes at this point will be skipped.
                                if (meshID == BatchMeshID.Null)
                                    continue;

                                // Null materials are handled internally by Unity using the error material if available.
                                BatchMaterialID materialID = materialMeshInfo.IsRuntimeMaterial ?
                                                             materialMeshInfo.MaterialID :
                                                             brgRenderMeshArray.GetMaterialID(materialMeshInfo);

                                var settings = new DrawCommandSettings
                                {
                                    FilterIndex  = filterIndex,
                                    BatchID      = batchID,
                                    MaterialID   = materialID,
                                    MeshID       = meshID,
                                    SplitMask    = splitMask,
                                    SubMeshIndex = (ushort)materialMeshInfo.SubMesh,
                                    Flags        = drawCommandFlags
                                };

                                EmitDrawCommand(settings, j, bitIndex, chunkStartIndex, depthSortingTransformsPtr);
                            }
                        }
                    }
                }
            }

            private void EmitDrawCommand(in DrawCommandSettings settings, int entityQword, int entityBit, int chunkStartIndex, void* depthSortingPtr)
            {
                // Depth sorted draws are emitted with access to entity transforms,
                // so they can also be written out for sorting
                if (settings.HasSortingPosition)
                {
                    DrawCommandOutput.EmitDepthSorted(settings, entityQword, entityBit, chunkStartIndex, (float4x4*)depthSortingPtr);
                }
                else
                {
                    DrawCommandOutput.Emit(settings, entityQword, entityBit, chunkStartIndex);
                }
            }
        }
#endif

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
                        submeshIndex        = (ushort)settings.SubMeshIndex,
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

                Assert.IsTrue(rangeCount <= output->drawCommandCount);
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

