using System;
using System.Collections.Generic;
using Latios.Transforms;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.Graphics;
using Unity.Entities.UniversalDelegates;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine.Rendering;

using RangeInt = UnityEngine.RangeInt;

namespace Latios.Kinemation.Systems
{
    public partial struct GenerateBrgDrawCommandsSystem
    {
        [BurstCompile]
        struct FindChunksWithVisibleJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               chunkHeaderHandle;

            public ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchCullingMaskHandle;

            public NativeList<ArchetypeChunk>.ParallelWriter chunksToProcess;

            [Unity.Burst.CompilerServices.SkipLocalsInit]
            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunksCache = stackalloc ArchetypeChunk[128];
                int chunksCount = 0;
                var masks       = metaChunk.GetNativeArray(ref perCameraCullingMaskHandle);
                var headers     = metaChunk.GetNativeArray(ref chunkHeaderHandle);
                var mergeMask   = (ChunkPerDispatchCullingMask*)metaChunk.GetComponentDataPtrRW(ref perDispatchCullingMaskHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var mask = masks[i];
                    if ((mask.lower.Value | mask.upper.Value) != 0)
                    {
                        chunksCache[chunksCount] = headers[i].ArchetypeChunk;
                        chunksCount++;
                    }

                    mergeMask[i].lower.Value |= mask.lower.Value;
                    mergeMask[i].upper.Value |= mask.upper.Value;
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
            [ReadOnly] public NativeArray<ArchetypeChunk>                                              chunksToProcess;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>                           chunkPerCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingSplitsMask>                     chunkPerCameraCullingSplitsMaskHandle;
            [ReadOnly] public ComponentTypeHandle<LodCrossfade>                                        lodCrossfadeHandle;
            [ReadOnly] public ComponentTypeHandle<SpeedTreeCrossfadeTag>                               speedTreeCrossfadeTagHandle;
            [ReadOnly] public ComponentTypeHandle<UseMmiRangeLodTag>                                   useMmiRangeLodTagHandle;
            [ReadOnly] public ComponentTypeHandle<OverrideMeshInRangeTag>                              overrideMeshInRangeTagHandle;
            [ReadOnly] public ComponentTypeHandle<RendererPriority>                                    rendererPriorityHandle;
            [ReadOnly] public ComponentTypeHandle<MeshLod>                                             meshLodHandle;
            [ReadOnly] public ComponentTypeHandle<PromiseAllEntitiesInChunkUseSameMaterialMeshInfoTag> promiseHandle;
            [ReadOnly] public EntityQueryMask                                                          motionVectorDeformQueryMask;
            public bool                                                                                splitsAreValid;

            //[ReadOnly] public IndirectList<ChunkVisibilityItem> VisibilityItems;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo>          MaterialMeshInfo;
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            [ReadOnly] public ComponentTypeHandle<WorldTransform> WorldTransform;
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            [ReadOnly] public ComponentTypeHandle<Unity.Transforms.LocalToWorld> WorldTransform;
#endif
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>          PostProcessMatrix;
            [ReadOnly] public ComponentTypeHandle<DepthSorted_Tag>            DepthSorted;
            [ReadOnly] public ComponentTypeHandle<PerVertexMotionVectors_Tag> ProceduralMotion;
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

                    var  materialMeshInfos   = chunk.GetComponentDataPtrRO(ref MaterialMeshInfo);
                    var  worldTransforms     = chunk.GetComponentDataPtrRO(ref WorldTransform);
                    var  postProcessMatrices = chunk.GetComponentDataPtrRO(ref PostProcessMatrix);
                    var  lodCrossfades       = chunk.GetComponentDataPtrRO(ref lodCrossfadeHandle);
                    var  rendererPriorities  = chunk.GetComponentDataPtrRO(ref rendererPriorityHandle);
                    var  meshLods            = chunk.GetComponentDataPtrRO(ref meshLodHandle);
                    var  meshLodCrossfades   = chunk.GetEnabledMask(ref meshLodHandle);
                    bool hasPostProcess      = postProcessMatrices != null;
                    bool isDepthSorted       = chunk.Has(ref DepthSorted);
                    bool isLightMapped       = chunk.GetSharedComponentIndex(LightMaps) >= 0;
                    bool hasLodCrossfade     = lodCrossfades != null;
                    bool useMmiRangeLod      = chunk.Has(ref useMmiRangeLodTagHandle);
                    bool hasOverrideMesh     = chunk.Has(ref overrideMeshInRangeTagHandle);

                    BatchDrawCommandFlags chunkFlags = default;

                    if (isLightMapped)
                        chunkFlags |= BatchDrawCommandFlags.IsLightMapped;

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
                            transformChanged     |= chunk.DidChange(ref PostProcessMatrix, LastSystemVersion);
                        bool isDeformed           = motionVectorDeformQueryMask.MatchesIgnoreFilter(chunk);
                        bool hasProceduralMotion  = chunk.Has(ref ProceduralMotion);
                        hasMotion                 = orderChanged || transformChanged || isDeformed || hasProceduralMotion;
                        if (hasMotion)
                            chunkFlags |= BatchDrawCommandFlags.HasMotion;
                    }

                    int chunkStartIndex = entitiesGraphicsChunkInfo.CullingData.ChunkOffsetInBatch;

                    var mask              = chunk.GetChunkComponentRefRO(ref chunkPerCameraCullingMaskHandle).ValueRO;
                    var splitsMask        = chunk.GetChunkComponentRefRO(ref chunkPerCameraCullingSplitsMaskHandle);
                    var crossFadeEnableds = hasLodCrossfade ? chunk.GetEnabledMask(ref lodCrossfadeHandle) : default;
                    var isSpeedTree       = hasLodCrossfade && chunk.Has(ref speedTreeCrossfadeTagHandle);

                    float* depthSortingTransformsPtr = null;
                    int    transformStrideInFloats   = 0;
                    int    positionOffsetInFloats    = 0;

                    if (isDepthSorted)
                    {
                        chunkFlags |= BatchDrawCommandFlags.HasSortingPosition;
                        if (hasPostProcess)
                        {
                            // In this case, we don't actually have a component that represents the rendered position.
                            // So we allocate a new array and compute the world positions.
                            // We compute them in the inner loop since only the visible instances are read from later,
                            // and it is a lot cheaper to only compute the visible instances.
                            var allocator             = DrawCommandOutput.ThreadLocalAllocator.ThreadAllocator(DrawCommandOutput.ThreadIndex)->Handle;
                            depthSortingTransformsPtr = (float*)AllocatorManager.Allocate<float3>(allocator, chunk.Count);
                            transformStrideInFloats   = 3;
                            positionOffsetInFloats    = 0;

                            for (int j = 0; j < 2; j++)
                            {
                                ulong visibleWord = mask.GetUlongFromIndex(j);
                                while (visibleWord != 0)
                                {
                                    int   bitIndex     = math.tzcnt(visibleWord);
                                    int   entityIndex  = (j << 6) + bitIndex;
                                    ulong entityMask   = 1ul << bitIndex;
                                    visibleWord       ^= entityMask;

                                    var index = j * 64 + bitIndex;
                                    var f4x4  = new float4x4(new float4(postProcessMatrices[index].postProcessMatrix.c0, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c1, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c2, 0f),
                                                             new float4(postProcessMatrices[index].postProcessMatrix.c3, 1f));
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                                    var position = math.transform(f4x4, worldTransforms[index].position);
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                                    var position = math.transform(f4x4, worldTransforms[index].Position);
#endif
                                    depthSortingTransformsPtr[3 * index]     = position.x;
                                    depthSortingTransformsPtr[3 * index + 1] = position.y;
                                    depthSortingTransformsPtr[3 * index + 2] = position.z;
                                }
                            }
                        }
                        else if (isDepthSorted)
                        {
                            depthSortingTransformsPtr = (float*)worldTransforms;
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                            transformStrideInFloats = 12;
                            positionOffsetInFloats  = 4;

#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                            transformStrideInFloats = 16;
                            positionOffsetInFloats  = 12;
#endif
                        }
                    }

                    var drawCommandSettings = new DrawCommandSettings
                    {
                        batch       = new BatchID { value = (uint)batchIndex },
                        filterIndex                       = filterIndex,
                    };

                    var entityDrawSettings = new EntityDrawSettings
                    {
                        chunkStartIndex         = chunkStartIndex,
                        chunkTransforms         = depthSortingTransformsPtr,
                        lodCrossfades           = lodCrossfades,
                        transformStrideInFloats = transformStrideInFloats,
                        positionOffsetInFloats  = positionOffsetInFloats,
                    };

                    if (chunk.Has(ref promiseHandle))
                    {
                        if (ExecuteBatched(in chunk, ref mask, in splitsMask.ValueRO, ref drawCommandSettings, in entityDrawSettings, useMmiRangeLod, meshLods, rendererPriorities,
                                           in entitiesGraphicsChunkInfo, materialMeshInfos, in brgRenderMeshArray, chunkFlags))
                            return;
                    }

                    for (int j = 0; j < 2; j++)
                    {
                        ulong visibleWord              = mask.GetUlongFromIndex(j);
                        entityDrawSettings.entityQword = j;

                        while (visibleWord != 0)
                        {
                            int   bitIndex               = math.tzcnt(visibleWord);
                            int   entityIndex            = (j << 6) + bitIndex;
                            ulong entityMask             = 1ul << bitIndex;
                            entityDrawSettings.entityBit = bitIndex;

                            // Clear the bit first in case we early out from the loop
                            visibleWord ^= entityMask;

                            MaterialMeshInfo materialMeshInfo = materialMeshInfos[entityIndex];
                            drawCommandSettings.splitMask     = splitsAreValid ? splitsMask.ValueRO.splitMasks[entityIndex] : (ushort)0;  // Todo: Should the default be 1 instead of 0?
                            bool flipWinding                  = (chunkCullingData.FlippedWinding[j] & entityMask) != 0;

                            BatchDrawCommandFlags drawCommandFlags = chunkFlags;

                            if (flipWinding)
                                drawCommandFlags |= BatchDrawCommandFlags.FlipWinding;

                            var  drawCommandFlagsWithoutCrossfade = drawCommandFlags;
                            bool isCrossfadeReady                 = hasLodCrossfade && crossFadeEnableds[entityIndex];
                            bool isMeshLodCrossfade               = meshLods != null && isCrossfadeReady && meshLodCrossfades[entityIndex];
                            if (isCrossfadeReady)
                            {
                                if (!isSpeedTree)
                                    drawCommandFlags |= BatchDrawCommandFlags.LODCrossFadeKeyword;
                                drawCommandFlags     |= BatchDrawCommandFlags.LODCrossFadeValuePacked;
                            }

                            ushort meshLod                        = meshLods != null ? meshLods[entityIndex].lodLevel : (ushort)0;
                            drawCommandSettings.renderingPriority = rendererPriorities == null ? 0 : rendererPriorities[entityIndex].priority;

                            if (materialMeshInfo.HasMaterialMeshIndexRange)
                            {
                                RangeInt matMeshIndexRange = materialMeshInfo.MaterialMeshIndexRange;
                                if (matMeshIndexRange.length == 127)
                                {
                                    int newLength             = (brgRenderMeshArray.MaterialMeshSubMeshes[matMeshIndexRange.start + 1].SubMeshIndex >> 16) & 0xff;
                                    newLength                |= (brgRenderMeshArray.MaterialMeshSubMeshes[matMeshIndexRange.start + 2].SubMeshIndex >> 8) & 0xff00;
                                    newLength                |= brgRenderMeshArray.MaterialMeshSubMeshes[matMeshIndexRange.start + 3].SubMeshIndex & 0xff0000;
                                    matMeshIndexRange.length  = newLength;
                                }

                                int hiResMask  = 0;
                                int lowResMask = 0;
                                if (useMmiRangeLod)
                                {
                                    materialMeshInfo.GetCurrentLodRegion(out var hiResLodIndex, out var isMmiCrossfading);
                                    hiResMask = 1 << hiResLodIndex;
                                    if (hiResMask > 1)
                                    {
                                        meshLod            = 0;
                                        isMeshLodCrossfade = false;
                                    }
                                    if (isMmiCrossfading && isCrossfadeReady)
                                        lowResMask = hiResMask << 1;

                                    // Late check if any of the elements are in the LOD. We'd prefer to filter these out sooner, but it is still good to check here.
                                    if (matMeshIndexRange.length > 0)
                                    {
                                        var combinedMask = (brgRenderMeshArray.MaterialMeshSubMeshes[matMeshIndexRange.start].SubMeshIndex >> 16) & 0xff;
                                        if ((combinedMask & (hiResMask | lowResMask)) == 0)
                                            continue;
                                    }
                                }

                                BatchMeshID overrideMesh = default;
                                if (hasOverrideMesh)
                                    overrideMesh = materialMeshInfo.IsRuntimeMesh ? materialMeshInfo.MeshID : brgRenderMeshArray.GetMeshID(materialMeshInfo);

                                for (int i = 0; i < matMeshIndexRange.length; i++)
                                {
                                    int matMeshSubMeshIndex = matMeshIndexRange.start + i;

                                    // Drop the draw command if OOB. Errors should have been reported already so no need to log anything
                                    if (matMeshSubMeshIndex >= brgRenderMeshArray.MaterialMeshSubMeshes.Length)
                                        continue;

                                    BatchMaterialMeshSubMesh matMeshSubMesh = brgRenderMeshArray.MaterialMeshSubMeshes[matMeshSubMeshIndex];

                                    var  drawCommandFlagsToUse = drawCommandFlags;
                                    bool complementLod         = false;
                                    if (useMmiRangeLod)
                                    {
                                        var  mmsmMask = matMeshSubMesh.SubMeshIndex >> 24;
                                        bool isHi     = (mmsmMask & hiResMask) != 0;
                                        bool isLow    = (mmsmMask & lowResMask) != 0;
                                        if (!isHi && !isLow)
                                            continue;
                                        if (isHi && isLow)
                                            drawCommandFlagsToUse = drawCommandFlagsWithoutCrossfade;
                                        else if (isLow)
                                            complementLod = true;
                                    }

                                    if (hasOverrideMesh)
                                        matMeshSubMesh.Mesh = overrideMesh;

                                    drawCommandSettings.mesh     = matMeshSubMesh.Mesh;
                                    drawCommandSettings.material = matMeshSubMesh.Material;
                                    drawCommandSettings.submesh  = (ushort)(matMeshSubMesh.SubMeshIndex & 0xffff);
                                    drawCommandSettings.meshLod  = complementLod ? (ushort)0 : meshLod;
                                    drawCommandSettings.flags    = drawCommandFlagsToUse;

                                    entityDrawSettings.complementLodCrossfade = complementLod;

                                    DrawCommandOutput.Emit(ref drawCommandSettings, in entityDrawSettings);

                                    if (isMeshLodCrossfade)
                                    {
                                        drawCommandSettings.meshLod++;
                                        DrawCommandOutput.Emit(ref drawCommandSettings, in entityDrawSettings);
                                    }
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

                                if (materialID == BatchMaterialID.Null)
                                    continue;

                                drawCommandSettings.mesh     = meshID;
                                drawCommandSettings.material = materialID;
                                drawCommandSettings.submesh  = GetSubMesh16(ref materialMeshInfo);
                                drawCommandSettings.meshLod  = meshLod;
                                drawCommandSettings.flags    = drawCommandFlags;

                                entityDrawSettings.complementLodCrossfade = false;

                                DrawCommandOutput.Emit(ref drawCommandSettings, in entityDrawSettings);

                                if (isMeshLodCrossfade)
                                {
                                    drawCommandSettings.meshLod++;
                                    entityDrawSettings.complementLodCrossfade = true;
                                    DrawCommandOutput.Emit(ref drawCommandSettings, in entityDrawSettings);
                                }
                            }
                        }
                    }
                }
            }

            // Return true if all entities handled. False if there are leftovers.
            bool ExecuteBatched(in ArchetypeChunk chunk,
                                ref ChunkPerCameraCullingMask mask,
                                in ChunkPerCameraCullingSplitsMask splitsMask,
                                ref DrawCommandSettings drawCommandSettings,
                                in EntityDrawSettings entityDrawSettings,
                                bool useMmiRangeLod,
                                MeshLod*                           meshLods,
                                RendererPriority*                  rendererPriorities,
                                in EntitiesGraphicsChunkInfo entitiesGraphicsChunkInfo,
                                MaterialMeshInfo*                  materialMeshInfos,
                                in BRGRenderMeshArray brgRenderMeshArray,
                                BatchDrawCommandFlags chunkDrawFlags)
            {
                if (useMmiRangeLod || meshLods != null || entityDrawSettings.lodCrossfades != null || rendererPriorities != null)
                    return false;

                if ((entitiesGraphicsChunkInfo.CullingData.FlippedWinding[0] | entitiesGraphicsChunkInfo.CullingData.FlippedWinding[1]) != 0)
                    return false;

                var materialMeshInfo = materialMeshInfos[0];

                if (materialMeshInfo.HasMaterialMeshIndexRange)
                    return false;

                BatchMeshID meshID = materialMeshInfo.IsRuntimeMesh ?
                                     materialMeshInfo.MeshID :
                                     brgRenderMeshArray.GetMeshID(materialMeshInfo);

                // Invalid meshes at this point will be skipped.
                if (meshID == BatchMeshID.Null)
                    return true;

                // Null materials are handled internally by Unity using the error material if available.
                BatchMaterialID materialID = materialMeshInfo.IsRuntimeMaterial ?
                                             materialMeshInfo.MaterialID :
                                             brgRenderMeshArray.GetMaterialID(materialMeshInfo);

                if (materialID == BatchMaterialID.Null)
                    return true;

                drawCommandSettings.mesh     = meshID;
                drawCommandSettings.material = materialID;
                drawCommandSettings.submesh  = GetSubMesh16(ref materialMeshInfo);
                drawCommandSettings.meshLod  = 0;
                drawCommandSettings.flags    = chunkDrawFlags;

                if (!splitsAreValid)
                {
                    var visibleCount              = mask.lower.CountBits() + mask.upper.CountBits();
                    var entityDrawSettingsBatched = new EntityDrawSettingsBatched
                    {
                        chunkStartIndex         = entityDrawSettings.chunkStartIndex,
                        chunkTransforms         = entityDrawSettings.chunkTransforms,
                        instancesCount          = visibleCount,
                        lower                   = mask.lower.Value,
                        upper                   = mask.upper.Value,
                        positionOffsetInFloats  = entityDrawSettings.positionOffsetInFloats,
                        transformStrideInFloats = entityDrawSettings.transformStrideInFloats
                    };

                    DrawCommandOutput.Emit(ref drawCommandSettings, in entityDrawSettingsBatched);
                }
                else
                {
                    Span<ChunkPerCameraCullingMask> drawMasksBySplitCombination = stackalloc ChunkPerCameraCullingMask[64];
                    drawMasksBySplitCombination.Clear();
                    ulong usedSplitsCombinations = 0;

                    ulong visibleWord = mask.lower.Value;
                    while (visibleWord != 0)
                    {
                        int   bitIndex     = math.tzcnt(visibleWord);
                        int   entityIndex  = bitIndex;
                        ulong bitMask      = 1ul << bitIndex;
                        visibleWord       ^= bitMask;

                        var splitMask                                       = splitsMask.splitMasks[entityIndex];
                        drawMasksBySplitCombination[splitMask].lower.Value |= bitMask;
                        usedSplitsCombinations                             |= 1ul << splitMask;
                    }
                    if (mask.upper.Value != 0)
                    {
                        visibleWord = mask.upper.Value;
                        while (visibleWord != 0)
                        {
                            int   bitIndex     = math.tzcnt(visibleWord);
                            int   entityIndex  = 64 + bitIndex;
                            ulong bitMask      = 1ul << bitIndex;
                            visibleWord       ^= bitMask;

                            var splitMask                                       = splitsMask.splitMasks[entityIndex];
                            drawMasksBySplitCombination[splitMask].upper.Value |= bitMask;
                            usedSplitsCombinations                             |= 1ul << splitMask;
                        }
                    }
                    var entityDrawSettingsBatched = new EntityDrawSettingsBatched
                    {
                        chunkStartIndex         = entityDrawSettings.chunkStartIndex,
                        chunkTransforms         = entityDrawSettings.chunkTransforms,
                        positionOffsetInFloats  = entityDrawSettings.positionOffsetInFloats,
                        transformStrideInFloats = entityDrawSettings.transformStrideInFloats
                    };
                    while (usedSplitsCombinations != 0)
                    {
                        var splitsCombination   = math.tzcnt(usedSplitsCombinations);
                        usedSplitsCombinations ^= 1ul << splitsCombination;

                        drawCommandSettings.splitMask            = (ushort)splitsCombination;
                        var visibleEntities                      = drawMasksBySplitCombination[splitsCombination];
                        entityDrawSettingsBatched.lower          = visibleEntities.lower.Value;
                        entityDrawSettingsBatched.upper          = visibleEntities.upper.Value;
                        entityDrawSettingsBatched.instancesCount = visibleEntities.lower.CountBits() + visibleEntities.upper.CountBits();

                        DrawCommandOutput.Emit(ref drawCommandSettings, in entityDrawSettingsBatched);
                    }
                }

                return true;
            }

            static unsafe ushort GetSubMesh16(ref MaterialMeshInfo mmi)
            {
                fixed (MaterialMeshInfo* mmiPtr = &mmi)
                {
                    var uintPtr    = (uint*)mmiPtr;
                    var packedData = uintPtr[2];
                    return (ushort)(packedData & 0xffff);
                }
            }
        }

        unsafe struct DrawBinSort
        {
            public const int kNumSlices = 4;

            [BurstCompile]
            internal unsafe struct SortArrays
            {
                public IndirectList<int> SortedBins;
                public IndirectList<int> SortTemp;

                public int ValuesPerIndex => (SortedBins.Length + kNumSlices - 1) / kNumSlices;

                [return : NoAlias] public int* ValuesTemp(int i = 0) => SortTemp.List->Ptr + i;
                [return : NoAlias] public int* ValuesDst(int i  = 0) => SortedBins.List->Ptr + i;

                public void GetBeginEnd(int index, out int begin, out int end)
                {
                    begin = index * ValuesPerIndex;
                    end   = math.min(begin + ValuesPerIndex, SortedBins.Length);
                }
            }

            internal unsafe struct BinSortComparer : IComparer<int>
            {
                [NoAlias]
                public DrawCommandSettings* Bins;

                public BinSortComparer(IndirectList<DrawCommandSettings> bins)
                {
                    Bins = bins.List->Ptr;
                }

                public int Compare(int x, int y) => Key(x).CompareTo(Key(y));

                private DrawCommandSettings Key(int bin) => Bins[bin];
            }

            [BurstCompile]
            internal unsafe struct AllocateForSortJob : IJob
            {
                public IndirectList<DrawCommandSettings> UnsortedBins;
                public SortArrays                        Arrays;

                public void Execute()
                {
                    int numBins = UnsortedBins.Length;
                    Arrays.SortedBins.Resize(numBins, NativeArrayOptions.UninitializedMemory);
                    Arrays.SortTemp.Resize(numBins, NativeArrayOptions.UninitializedMemory);
                }
            }

            [BurstCompile]
            internal unsafe struct SortSlicesJob : IJobParallelFor
            {
                public SortArrays                        Arrays;
                public IndirectList<DrawCommandSettings> UnsortedBins;

                public void Execute(int index)
                {
                    Arrays.GetBeginEnd(index, out int begin, out int end);

                    var valuesFromZero = Arrays.ValuesTemp();
                    int N              = end - begin;

                    for (int i = begin; i < end; ++i)
                        valuesFromZero[i] = i;

                    NativeSortExtension.Sort(Arrays.ValuesTemp(begin), N, new BinSortComparer(UnsortedBins));
                }
            }

            [BurstCompile]
            internal unsafe struct MergeSlicesJob : IJob
            {
                public SortArrays                        Arrays;
                public IndirectList<DrawCommandSettings> UnsortedBins;
                public int NumSlices => kNumSlices;

                public void Execute()
                {
                    var sliceRead = stackalloc int[NumSlices];
                    var sliceEnd  = stackalloc int[NumSlices];

                    int sliceMask = 0;

                    for (int i = 0; i < NumSlices; ++i)
                    {
                        Arrays.GetBeginEnd(i, out sliceRead[i], out sliceEnd[i]);
                        if (sliceRead[i] < sliceEnd[i])
                            sliceMask |= 1 << i;
                    }

                    int N        = Arrays.SortedBins.Length;
                    var dst      = Arrays.ValuesDst();
                    var src      = Arrays.ValuesTemp();
                    var comparer = new BinSortComparer(UnsortedBins);

                    for (int i = 0; i < N; ++i)
                    {
                        int iterMask           = sliceMask;
                        int firstNonEmptySlice = math.tzcnt(iterMask);

                        int bestSlice  = firstNonEmptySlice;
                        int bestValue  = src[sliceRead[firstNonEmptySlice]];
                        iterMask      ^= 1 << firstNonEmptySlice;

                        while (iterMask != 0)
                        {
                            int slice = math.tzcnt(iterMask);
                            int value = src[sliceRead[slice]];

                            if (comparer.Compare(value, bestValue) < 0)
                            {
                                bestSlice = slice;
                                bestValue = value;
                            }

                            iterMask ^= 1 << slice;
                        }

                        dst[i] = bestValue;

                        int  nextValue       = sliceRead[bestSlice] + 1;
                        bool sliceExhausted  = nextValue >= sliceEnd[bestSlice];
                        sliceRead[bestSlice] = nextValue;

                        int mask   = 1 << bestSlice;
                        mask       = sliceExhausted ? mask : 0;
                        sliceMask ^= mask;
                    }
                }
            }

            public static JobHandle ScheduleBinSort(
                RewindableAllocator*              allocator,
                IndirectList<int>                 sortedBins,
                IndirectList<DrawCommandSettings> unsortedBins,
                JobHandle dependency = default)
            {
                var sortArrays = new SortArrays
                {
                    SortedBins = sortedBins,
                    SortTemp   = new IndirectList<int>(0, allocator),
                };

                var alloc = new AllocateForSortJob
                {
                    Arrays       = sortArrays,
                    UnsortedBins = unsortedBins,
                }.Schedule(dependency);

                var sortSlices = new SortSlicesJob
                {
                    Arrays       = sortArrays,
                    UnsortedBins = unsortedBins,
                }.Schedule(kNumSlices, 1, alloc);

                var mergeSlices = new MergeSlicesJob
                {
                    Arrays       = sortArrays,
                    UnsortedBins = unsortedBins,
                }.Schedule(sortSlices);

                return mergeSlices;
            }

            public static void RunBinSortImmediate(RewindableAllocator* allocator, IndirectList<int> sortedBins, IndirectList<DrawCommandSettings> unsortedBins)
            {
                var sortArrays = new SortArrays
                {
                    SortedBins = sortedBins,
                    SortTemp   = new IndirectList<int>(0, allocator),
                };

                new AllocateForSortJob
                {
                    Arrays       = sortArrays,
                    UnsortedBins = unsortedBins,
                }.Execute();

                var sortSlicesJob = new SortSlicesJob
                {
                    Arrays       = sortArrays,
                    UnsortedBins = unsortedBins,
                };
                for (int i = 0; i < kNumSlices; i++)
                    sortSlicesJob.Execute(i);

                new MergeSlicesJob
                {
                    Arrays       = sortArrays,
                    UnsortedBins = unsortedBins,
                }.Execute();
            }
        }

        [BurstCompile]
        unsafe struct AllocateWorkItemsJob : IJob
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            public void Execute()
            {
                int numBins = DrawCommandOutput.UnsortedBins.Length;

                DrawCommandOutput.BinIndices.Resize(numBins, NativeArrayOptions.UninitializedMemory);

                // Each thread can have one item per bin, but likely not all threads will.
                int workItemsUpperBound = ChunkDrawCommandOutput.NumThreads * numBins;
                DrawCommandOutput.WorkItems.SetCapacity(workItemsUpperBound);
            }
        }

        [BurstCompile]
        unsafe struct CollectWorkItemsJob : IJobParallelForDefer
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            public void Execute(int index)
            {
                var  settings           = DrawCommandOutput.UnsortedBins.ElementAt(index);
                bool hasSortingPosition = settings.hasSortingPosition;

                long* binPresentFilter = DrawCommandOutput.BinPresentFilterForSettings(in settings);

                int maxWorkItems = 0;
                for (int qwIndex = 0; qwIndex < ChunkDrawCommandOutput.kNumThreadsBitfieldLength; ++qwIndex)
                    maxWorkItems += math.countbits(binPresentFilter[qwIndex]);

                // Since we collect at most one item per thread, we will have N = thread count at most
                var workItems     = DrawCommandOutput.WorkItems.List->AsParallelWriter();
                var collectBuffer = DrawCommandOutput.CollectBuffer;
                collectBuffer->EnsureCapacity(workItems, maxWorkItems, DrawCommandOutput.ThreadLocalAllocator, DrawCommandOutput.ThreadIndex);

                int numInstancesPrefixSum = 0;

                for (int qwIndex = 0; qwIndex < ChunkDrawCommandOutput.kNumThreadsBitfieldLength; ++qwIndex)
                {
                    // Load a filter bitfield which has a 1 bit for every thread index that might contain
                    // draws for a given DrawCommandSettings. The filter is exact if there are no hash
                    // collisions, but might contain false positives if hash collisions happened.
                    ulong qword = (ulong)binPresentFilter[qwIndex];

                    while (qword != 0)
                    {
                        int   bitIndex  = math.tzcnt(qword);
                        ulong mask      = 1ul << bitIndex;
                        qword          ^= mask;

                        int i = (qwIndex << 6) + bitIndex;

                        var threadDraws = DrawCommandOutput.ThreadLocalDrawCommands[i];

                        if (!threadDraws.DrawCommandStreamIndices.IsCreated)
                            continue;

                        if (threadDraws.DrawCommandStreamIndices.TryGetValue(settings, out int streamIndex))
                        {
                            var stream = threadDraws.DrawCommands[streamIndex].Stream;

                            collectBuffer->Add(new DrawCommandWorkItem
                            {
                                Arrays                = stream.Head,
                                BinIndex              = index,
                                PrefixSumNumInstances = numInstancesPrefixSum,
                            });

                            numInstancesPrefixSum += stream.TotalInstanceCount;
                        }
                    }
                }

                DrawCommandOutput.BinIndices.ElementAt(index) = new DrawCommandBin
                {
                    NumInstances   = numInstancesPrefixSum,
                    InstanceOffset = 0,
                    PositionOffset = hasSortingPosition ? 0 : DrawCommandBin.kNoSortingPosition,
                };
            }
        }

        [BurstCompile]
        unsafe struct FlushWorkItemsJob : IJobParallelFor
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            public void Execute(int index)
            {
                var dst = DrawCommandOutput.WorkItems.List->AsParallelWriter();
                DrawCommandOutput.ThreadLocalCollectBuffers[index].Flush(dst);
            }
        }

        [BurstCompile]
        unsafe struct AllocateInstancesJob : IJob
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            public void Execute()
            {
                int numBins = DrawCommandOutput.BinIndices.Length;

                int instancePrefixSum        = 0;
                int sortingPositionPrefixSum = 0;

                for (int i = 0; i < numBins; ++i)
                {
                    ref var bin                = ref DrawCommandOutput.BinIndices.ElementAt(i);
                    bool    hasSortingPosition = bin.HasSortingPosition;

                    bin.InstanceOffset = instancePrefixSum;

                    // Keep kNoSortingPosition in the PositionOffset if no sorting
                    // positions, so draw command jobs can reliably check it to
                    // to know whether there are positions without needing access to flags
                    bin.PositionOffset = hasSortingPosition ?
                                         sortingPositionPrefixSum :
                                         DrawCommandBin.kNoSortingPosition;

                    int numInstances = bin.NumInstances;
                    int numPositions = hasSortingPosition ? numInstances : 0;

                    instancePrefixSum        += numInstances;
                    sortingPositionPrefixSum += numPositions;
                }

                var output                   = DrawCommandOutput.CullingOutputDrawCommands;
                output->visibleInstanceCount = instancePrefixSum;
                output->visibleInstances     = ChunkDrawCommandOutput.Malloc<int>(instancePrefixSum);

                int numSortingPositionFloats              = sortingPositionPrefixSum * 3;
                output->instanceSortingPositionFloatCount = numSortingPositionFloats;
                output->instanceSortingPositions          = (sortingPositionPrefixSum == 0) ?
                                                            null :
                                                            ChunkDrawCommandOutput.Malloc<float>(numSortingPositionFloats);
            }
        }

        [BurstCompile]
        unsafe struct AllocateDrawCommandsJob : IJob
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            public void Execute()
            {
                int numBins = DrawCommandOutput.SortedBins.Length;

                int drawCommandPrefixSum = 0;

                for (int i = 0; i < numBins; ++i)
                {
                    var     sortedBin     = DrawCommandOutput.SortedBins.ElementAt(i);
                    ref var bin           = ref DrawCommandOutput.BinIndices.ElementAt(sortedBin);
                    bin.DrawCommandOffset = drawCommandPrefixSum;

                    // Bins with sorting positions will be expanded to one draw command
                    // per instance, whereas other bins will be expanded to contain
                    // many instances per command.
                    int numDrawCommands   = bin.NumDrawCommands;
                    drawCommandPrefixSum += numDrawCommands;
                }

                var output = DrawCommandOutput.CullingOutputDrawCommands;

                // Draw command count is exact at this point, we can set it up front
                int drawCommandCount = drawCommandPrefixSum;

                output->drawCommandCount              = drawCommandCount;
                output->drawCommands                  = ChunkDrawCommandOutput.Malloc<BatchDrawCommand>(drawCommandCount);
                output->drawCommandPickingInstanceIDs = null;

                // Worst case is one range per draw command, so this is an upper bound estimate.
                // The real count could be less.
                output->drawRangeCount = 0;
                output->drawRanges     = ChunkDrawCommandOutput.Malloc<BatchDrawRange>(drawCommandCount);
            }
        }

        [BurstCompile]
        unsafe struct ExpandVisibleInstancesJob : IJobParallelForDefer
        {
            public ChunkDrawCommandOutput DrawCommandOutput;

            public void Execute(int index)
            {
                var workItem = DrawCommandOutput.WorkItems.ElementAt(index);
                var header   = workItem.Arrays;
                int binIndex = workItem.BinIndex;

                ref var settings               = ref DrawCommandOutput.UnsortedBins.ElementAt(binIndex);
                var     bin                    = DrawCommandOutput.BinIndices.ElementAt(binIndex);
                int     binInstanceOffset      = bin.InstanceOffset;
                int     binPositionOffset      = bin.PositionOffset;
                int     workItemInstanceOffset = workItem.PrefixSumNumInstances;
                int     headerInstanceOffset   = 0;

                int*    visibleInstances = DrawCommandOutput.CullingOutputDrawCommands->visibleInstances;
                float3* sortingPositions = (float3*)DrawCommandOutput.CullingOutputDrawCommands->instanceSortingPositions;

                if (!settings.hasSortingPosition)
                {
                    while (header != null)
                    {
                        ExpandArray(visibleInstances,
                                    header,
                                    binInstanceOffset + workItemInstanceOffset + headerInstanceOffset,
                                    UseCrossfades(settings.flags));

                        headerInstanceOffset += header->NumInstances;
                        header                = header->Next;
                    }
                }
                else
                {
                    while (header != null)
                    {
                        int instanceOffset = binInstanceOffset + workItemInstanceOffset + headerInstanceOffset;
                        int positionOffset = binPositionOffset + workItemInstanceOffset + headerInstanceOffset;

                        ExpandArrayWithPositions(visibleInstances,
                                                 sortingPositions,
                                                 header,
                                                 instanceOffset,
                                                 positionOffset,
                                                 UseCrossfades(settings.flags));

                        headerInstanceOffset += header->NumInstances;
                        header                = header->Next;
                    }
                }
            }

            private int ExpandArray(
                int*                                      visibleInstances,
                DrawStream<DrawCommandVisibility>.Header* header,
                int instanceOffset,
                bool usesCrossfades)
            {
                int numStructs = header->NumElements;

                for (int i = 0; i < numStructs; ++i)
                {
                    var visibility = *header->Element(i);
                    int numInstances;
                    if (usesCrossfades)
                        numInstances = ExpandVisibilityCrossfade(visibleInstances + instanceOffset, visibility);
                    else
                        numInstances = ExpandVisibility(visibleInstances + instanceOffset, visibility);
                    Assert.IsTrue(numInstances > 0);
                    instanceOffset += numInstances;
                }

                return instanceOffset;
            }

            private int ExpandArrayWithPositions(
                int*                                      visibleInstances,
                float3*                                   sortingPositions,
                DrawStream<DrawCommandVisibility>.Header* header,
                int instanceOffset,
                int positionOffset,
                bool usesCrossfades)
            {
                int numStructs = header->NumElements;

                for (int i = 0; i < numStructs; ++i)
                {
                    var visibility = *header->Element(i);
                    int numInstances;
                    if (usesCrossfades)
                        numInstances = ExpandVisibilityWithPositionsCrossfade(visibleInstances + instanceOffset, sortingPositions + positionOffset, visibility);
                    else
                        numInstances = ExpandVisibilityWithPositions(visibleInstances + instanceOffset, sortingPositions + positionOffset, visibility);
                    Assert.IsTrue(numInstances > 0);
                    instanceOffset += numInstances;
                    positionOffset += numInstances;
                }

                return instanceOffset;
            }

            private int ExpandVisibility(int* outputInstances, DrawCommandVisibility visibility)
            {
                int numInstances = 0;
                int startIndex   = visibility.chunkStartIndex;

                for (int i = 0; i < 2; ++i)
                {
                    ulong qword = visibility.visibleInstances[i];
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

            private int ExpandVisibilityCrossfade(int* outputInstances, DrawCommandVisibility visibility)
            {
                int numInstances = 0;
                int startIndex   = visibility.chunkStartIndex;

                for (int i = 0; i < 2; ++i)
                {
                    ulong qword         = visibility.visibleInstances[i];
                    ulong crossfadeBits = visibility.crossfadeComplements[i];
                    while (qword != 0)
                    {
                        int   bitIndex                 = math.tzcnt(qword);
                        ulong mask                     = 1ul << bitIndex;
                        qword                         ^= mask;
                        var complementCrossfade        = (crossfadeBits & mask) != 0;
                        int instanceIndex              = (i << 6) + bitIndex;
                        var crossfade                  = complementCrossfade ? visibility.crossfadesPtr[instanceIndex].ToComplement() : visibility.crossfadesPtr[instanceIndex];
                        int visibilityIndex            = ((startIndex + instanceIndex) & 0x00ffffff) | (crossfade.raw << 24);
                        outputInstances[numInstances]  = visibilityIndex;
                        ++numInstances;
                    }
                }

                return numInstances;
            }

            private int ExpandVisibilityWithPositions(
                int*                  outputInstances,
                float3*               outputSortingPosition,
                DrawCommandVisibility visibility)
            {
                int numInstances = 0;
                int startIndex   = visibility.chunkStartIndex;

                for (int i = 0; i < 2; ++i)
                {
                    ulong qword = visibility.visibleInstances[i];
                    while (qword != 0)
                    {
                        int   bitIndex     = math.tzcnt(qword);
                        ulong mask         = 1ul << bitIndex;
                        qword             ^= mask;
                        int instanceIndex  = (i << 6) + bitIndex;

                        int visibilityIndex                 = startIndex + instanceIndex;
                        outputInstances[numInstances]       = visibilityIndex;
                        var positionPtr                     = visibility.transformsPtr + instanceIndex * visibility.transformStrideInFloats + visibility.positionOffsetInFloats;
                        outputSortingPosition[numInstances] = new float3(positionPtr[0], positionPtr[1], positionPtr[2]);
                        ++numInstances;
                    }
                }

                return numInstances;
            }

            private int ExpandVisibilityWithPositionsCrossfade(
                int*                  outputInstances,
                float3*               outputSortingPosition,
                DrawCommandVisibility visibility)
            {
                int numInstances = 0;
                int startIndex   = visibility.chunkStartIndex;

                for (int i = 0; i < 2; ++i)
                {
                    ulong qword         = visibility.visibleInstances[i];
                    ulong crossfadeBits = visibility.crossfadeComplements[i];
                    while (qword != 0)
                    {
                        int   bitIndex           = math.tzcnt(qword);
                        ulong mask               = 1ul << bitIndex;
                        qword                   ^= mask;
                        var complementCrossfade  = (crossfadeBits & mask) != 0;
                        int instanceIndex        = (i << 6) + bitIndex;

                        int visibilityIndex = startIndex + instanceIndex;
                        var crossfade       =
                            complementCrossfade ? visibility.crossfadesPtr[instanceIndex].ToComplement() : visibility.crossfadesPtr[instanceIndex];
                        outputInstances[numInstances]       = (visibilityIndex & 0x00ffffff) | (crossfade.raw << 24);
                        var positionPtr                     = visibility.transformsPtr + instanceIndex * visibility.transformStrideInFloats + visibility.positionOffsetInFloats;
                        outputSortingPosition[numInstances] = new float3(positionPtr[0], positionPtr[1], positionPtr[2]);

                        ++numInstances;
                    }
                }

                return numInstances;
            }

            private static bool UseCrossfades(BatchDrawCommandFlags flags)
            {
                return (flags & BatchDrawCommandFlags.LODCrossFadeValuePacked) == BatchDrawCommandFlags.LODCrossFadeValuePacked;
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

                bool hasSortingPosition = settings.hasSortingPosition;
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
                        visibleOffset = drawInstanceOffset,
                        visibleCount  = math.min(maxPerCommand, numInstances),
                        batchID       = settings.batch,
                        materialID    = settings.material,
                        meshID        = settings.mesh,
                        submeshIndex  = settings.submesh,
#if UNITY_6000_2_OR_NEWER
                        activeMeshLod = settings.meshLod,
#endif
                        splitVisibilityMask = settings.splitMask,
                        flags               = settings.flags,
                        sortingPosition     = hasSortingPosition ? (int)drawPositionFloatOffset : 0,
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
        unsafe struct GenerateDrawRangesJob : IJob
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
                    int  filterIndex        = settings.filterIndex;
                    bool hasSortingPosition = settings.hasSortingPosition;

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

