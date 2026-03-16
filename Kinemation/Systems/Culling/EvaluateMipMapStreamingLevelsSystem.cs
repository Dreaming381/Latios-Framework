using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

using static Latios.Kinemation.StreamingMipMapArray;
using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct EvaluateMipMapStreamingLevelsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_query;

        NativeHashMap<int, UnmanagedStreamingMipMapArray> m_sharedIndexToUnmanagedArrayMap;
        NativeList<TextureState>                          m_textureStates;
        NativeList<int>                                   m_textureStatesFreelist;

        static List<StreamingMipMapArray> s_sharedValuesCache   = new List<StreamingMipMapArray>();
        static List<int>                  s_sharedIndicesCache  = new List<int>();
        static List<int>                  s_sharedVersionsCache = new List<int>();

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_query = state.Fluent().With<MaterialMeshInfo, StreamingMipMapArray, WorldRenderBounds>(true).With<RenderMeshArray>(true)
                      .With<ChunkPerFrameCullingMask>(true, true).Build();

            m_sharedIndexToUnmanagedArrayMap = new NativeHashMap<int, UnmanagedStreamingMipMapArray>(32, Allocator.Persistent);
            m_textureStates                  = new NativeList<TextureState>(128, Allocator.Persistent);
            m_textureStatesFreelist          = new NativeList<int>(128, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            foreach (var kvp in m_sharedIndexToUnmanagedArrayMap)
                kvp.Value.Dispose();
            m_sharedIndexToUnmanagedArrayMap.Dispose();
            m_textureStates.Dispose();
            m_textureStatesFreelist.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            s_sharedValuesCache.Clear();
            s_sharedIndicesCache.Clear();
            s_sharedVersionsCache.Clear();
            state.EntityManager.GetAllUniqueSharedComponentsManaged(s_sharedValuesCache, s_sharedIndicesCache, s_sharedVersionsCache);
            var sharedMetadataArray = CollectionHelper.CreateNativeArray<SharedValueMetadata>(s_sharedValuesCache.Count,
                                                                                              state.WorldUpdateAllocator,
                                                                                              NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < sharedMetadataArray.Length; i++)
            {
                var val                = s_sharedValuesCache[i];
                sharedMetadataArray[i] = new SharedValueMetadata
                {
                    rmaHash  = val.renderMeshArrayHash,
                    metaHash = val.metadataHash,
                    index    = s_sharedIndicesCache[i],
                    version  = s_sharedVersionsCache[i],
                };
            }
            CompareOldAndNewArrays(ref state, ref m_sharedIndexToUnmanagedArrayMap, ref sharedMetadataArray, out var indicesToConstruct);

            var newArrays = CollectionHelper.CreateNativeArray<UnmanagedStreamingMipMapArray>(indicesToConstruct.Length,
                                                                                              state.WorldUpdateAllocator,
                                                                                              NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < newArrays.Length; i++)
            {
                var metaIndex   = indicesToConstruct[i];
                var meta        = sharedMetadataArray[metaIndex];
                var sharedValue = s_sharedValuesCache[metaIndex];

                newArrays[i] = new UnmanagedStreamingMipMapArray(sharedValue, meta);
            }

            DoOnUpdateBurst(ref this, ref state, ref newArrays);
        }

        [BurstCompile]
        static void DoOnUpdateBurst(ref EvaluateMipMapStreamingLevelsSystem system, ref SystemState state, ref NativeArray<UnmanagedStreamingMipMapArray> newArrays)
        {
            system.OnUpdateBurst(ref state, ref newArrays);
        }

        void OnUpdateBurst(ref SystemState state, ref NativeArray<UnmanagedStreamingMipMapArray> newArrays)
        {
            foreach (var array in newArrays)
                m_sharedIndexToUnmanagedArrayMap.Add(array.sharedComponentIndex, array);

            var brgRmaMap    = latiosWorld.worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>(true).brgRenderMeshArrays;
            state.Dependency = new RebuildBatchToIndexMapsJob
            {
                sharedIndexToArrayMap              = m_sharedIndexToUnmanagedArrayMap,
                sharedIndexToBrgRenderMeshArrayMap = brgRmaMap
            }.Schedule(state.Dependency);

            var textureToStateIndexMap = new NativeHashMap<UnityObjectRef<Texture2D>, int>(256, state.WorldUpdateAllocator);
            var dispatchContext        = latiosWorld.worldBlackboardEntity.GetComponentData<DispatchContext>();
            state.Dependency           = new PreprocessTexturesJob
            {
                textureStates          = m_textureStates,
                freeList               = m_textureStatesFreelist,
                textureToStateIndexMap = textureToStateIndexMap,
                sharedIndexToArrayMap  = m_sharedIndexToUnmanagedArrayMap,
                extraDispatch          = dispatchContext.dispatchIndexThisFrame > 1
            }.Schedule(state.Dependency);

            var newLevelsPerThread = CollectionHelper.CreateNativeArray<UnsafeList<int> >(Unity.Jobs.LowLevel.Unsafe.JobsUtility.ThreadIndexCount,
                                                                                          state.WorldUpdateAllocator,
                                                                                          NativeArrayOptions.ClearMemory);
            state.Dependency = new EvaluateEntitiesJob
            {
                cameraParametersLookup             = GetBufferLookup<MipMapCameraParameters>(true),
                materialMeshInfoHandle             = GetComponentTypeHandle<MaterialMeshInfo>(true),
                newLevelsPerThread                 = newLevelsPerThread,
                perFrameMaskHandle                 = GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                renderMeshArrayHandle              = ManagedAPI.GetSharedComponentTypeHandle<RenderMeshArray>(),
                sharedIndexToArrayMap              = m_sharedIndexToUnmanagedArrayMap,
                sharedIndexToBrgRenderMeshArrayMap = brgRmaMap,
                streamingMipMapArrayHandle         = ManagedAPI.GetSharedComponentTypeHandle<StreamingMipMapArray>(),
                textureStates                      = m_textureStates,
                textureToStateIndexMap             = textureToStateIndexMap,
                worldBlackboardEntity              = latiosWorld.worldBlackboardEntity,
                worldRenderBoundsHandle            = GetComponentTypeHandle<WorldRenderBounds>(true),
                worldUpdateAllocator               = state.WorldUpdateAllocator
            }.ScheduleParallel(m_query, state.Dependency);

            state.Dependency = new MergeThreadLevelsJob
            {
                newLevelsPerThread = newLevelsPerThread,
                textureStates      = m_textureStates,
            }.Schedule(state.Dependency);

            state.Dependency = new PostProcessTexturesJob
            {
                mipMapsStreamingAssignmentLookup = GetBufferLookup<MipMapStreamingAssignment>(false),
                textureStates                    = m_textureStates,
                worldBlackbaordEntity            = latiosWorld.worldBlackboardEntity
            }.Schedule();
        }

        [BurstCompile]
        static void CompareOldAndNewArrays(ref SystemState state,
                                           ref NativeHashMap<int, UnmanagedStreamingMipMapArray> map,
                                           ref NativeArray<SharedValueMetadata> current,
                                           out NativeArray<int> indicesToConstruct)
        {
            var newIndices      = new NativeList<int>(current.Length, state.WorldUpdateAllocator);
            var unreferencedSet = new NativeHashSet<int>(map.Count + 1, state.WorldUpdateAllocator);
            foreach (var kvp in map)
                unreferencedSet.Add(kvp.Key);

            for (int i = 0; i < current.Length; i++)
            {
                var meta = current[i];
                if (map.TryGetValue(meta.index, out var unmanagedArray))
                {
                    if (meta.version == unmanagedArray.version && meta.metaHash.Equals(unmanagedArray.metadataHash) && meta.rmaHash.Equals(unmanagedArray.renderMeshArrayHash))
                    {
                        unreferencedSet.Remove(meta.index);
                        continue;
                    }
                }
                newIndices.Add(i);
            }

            foreach (var unreferenced in unreferencedSet)
            {
                map[unreferenced].Dispose();
                map.Remove(unreferenced);
            }

            indicesToConstruct = newIndices.AsArray();
        }

        struct SharedValueMetadata
        {
            public uint4 rmaHash;
            public uint4 metaHash;
            public int   index;
            public int   version;
        }

        struct UnmanagedStreamingMipMapArray : IDisposable
        {
            public UnsafeList<StreamingTextureInMaterial> streamingTextures;
            public UnsafeList<RangeByMaterial>            ranges;
            public UnsafeList<MeshMetric>                 meshMetrics;
            public UnsafeHashMap<BatchMeshID, int>        batchMeshToRmaIndexMap;
            public UnsafeHashMap<BatchMaterialID, int>    batchMaterialToRmaIndexMap;
            public uint4                                  renderMeshArrayHash;
            public uint4                                  metadataHash;
            public int                                    sharedComponentIndex;
            public int                                    version;

            public void Dispose()
            {
                streamingTextures.Dispose();
                ranges.Dispose();
                meshMetrics.Dispose();
                batchMeshToRmaIndexMap.Dispose();
                batchMaterialToRmaIndexMap.Dispose();
            }

            public unsafe UnmanagedStreamingMipMapArray(StreamingMipMapArray managed, SharedValueMetadata metadata)
            {
                streamingTextures = new UnsafeList<StreamingTextureInMaterial>(managed.streamingTextures.Length, Allocator.Persistent);
                streamingTextures.Resize(managed.streamingTextures.Length);
                managed.streamingTextures.AsSpan().CopyTo(new Span<StreamingTextureInMaterial>(streamingTextures.Ptr, streamingTextures.Length));

                ranges = new UnsafeList<RangeByMaterial>(managed.ranges.Length, Allocator.Persistent);
                ranges.Resize(managed.ranges.Length);
                managed.ranges.AsSpan().CopyTo(new Span<RangeByMaterial> (ranges.Ptr, ranges.Length));

                meshMetrics = new UnsafeList<MeshMetric>(managed.meshMetrics.Length, Allocator.Persistent);
                meshMetrics.Resize(managed.meshMetrics.Length);
                managed.meshMetrics.AsSpan().CopyTo(new Span<MeshMetric>(meshMetrics.Ptr, meshMetrics.Length));

                renderMeshArrayHash  = managed.renderMeshArrayHash;
                metadataHash         = managed.metadataHash;
                sharedComponentIndex = metadata.index;
                version              = metadata.version;

                batchMaterialToRmaIndexMap = default;
                batchMeshToRmaIndexMap     = default;
            }
        }

        struct TextureState
        {
            public UnityObjectRef<Texture2D> texture;
            public int                       mipmapCount;
            public int                       assignedLevel;
            public int                       evaluatedLevel;
            public int4                      oneTwoThreeFourAgo;
        }

        [BurstCompile]
        struct RebuildBatchToIndexMapsJob : IJob
        {
            public NativeHashMap<int, UnmanagedStreamingMipMapArray>         sharedIndexToArrayMap;
            [ReadOnly] public NativeParallelHashMap<int, BRGRenderMeshArray> sharedIndexToBrgRenderMeshArrayMap;

            public void Execute()
            {
                foreach (var pair in sharedIndexToArrayMap)
                {
                    ref var array = ref pair.Value;
                    if (array.batchMeshToRmaIndexMap.IsEmpty && !array.meshMetrics.IsEmpty)
                    {
                        var rma = GetRmaForSmma(in array);

                        array.batchMeshToRmaIndexMap = new UnsafeHashMap<BatchMeshID, int>(array.meshMetrics.Length, Allocator.Persistent);
                        for (int i = 0; i < rma.UniqueMeshes.Length; i++)
                            array.batchMeshToRmaIndexMap.Add(rma.UniqueMeshes[i], i);
                        array.batchMaterialToRmaIndexMap = new UnsafeHashMap<BatchMaterialID, int>(array.ranges.Length, Allocator.Persistent);
                        for (int i = 0; i < rma.UniqueMaterials.Length; i++)
                            array.batchMaterialToRmaIndexMap.Add(rma.UniqueMaterials[i], i);
                    }
                }
            }

            BRGRenderMeshArray GetRmaForSmma(in UnmanagedStreamingMipMapArray smma)
            {
                foreach (var pair in sharedIndexToBrgRenderMeshArrayMap)
                {
                    if (pair.Value.Hash128.Equals(smma.renderMeshArrayHash))
                        return pair.Value;
                }
                return default;
            }
        }

        [BurstCompile]
        struct PreprocessTexturesJob : IJob
        {
            public NativeList<TextureState>                                     textureStates;
            public NativeList<int>                                              freeList;
            public NativeHashMap<UnityObjectRef<Texture2D>, int>                textureToStateIndexMap;
            [ReadOnly] public NativeHashMap<int, UnmanagedStreamingMipMapArray> sharedIndexToArrayMap;

            public bool extraDispatch;

            public void Execute()
            {
                var referencedBits = new UnsafeBitArray(textureStates.Length, Allocator.Temp);
                var newTextures    = new UnsafeList<NewTexture>(128, Allocator.Temp);

                for (int i = 0; i < textureStates.Length; i++)
                {
                    if (textureStates[i].texture != default)
                        textureToStateIndexMap.Add(textureStates[i].texture, i);
                }

                foreach (var pair in sharedIndexToArrayMap)
                {
                    var array = pair.Value;
                    foreach (var arrayTexture in array.streamingTextures)
                    {
                        if (textureToStateIndexMap.TryGetValue(arrayTexture.streamingTexture, out var foundIndex))
                            referencedBits.Set(foundIndex, true);
                        else
                            newTextures.Add(new NewTexture { texture = arrayTexture.streamingTexture, mipmapCount = arrayTexture.mipmapCount });
                    }
                }

                for (int i = 0; i < textureStates.Length; i++)
                {
                    if (!referencedBits.IsSet(i) && textureStates[i].texture != default)
                    {
                        textureToStateIndexMap.Remove(textureStates[i].texture);
                        textureStates.ElementAt(i).texture = default;
                        freeList.Add(i);
                    }
                    else
                    {
                        // Update the state for the new evaluation
                        ref var state = ref textureStates.ElementAt(i);
                        if (!extraDispatch)
                        {
                            // Roll history for new frame
                            state.oneTwoThreeFourAgo   = state.oneTwoThreeFourAgo.wxyz;
                            state.oneTwoThreeFourAgo.x = state.evaluatedLevel;
                        }
                        state.evaluatedLevel = state.mipmapCount - 1;
                    }
                }

                foreach (var newTexture in newTextures)
                {
                    // We might have detected the texture multiple times
                    if (textureToStateIndexMap.ContainsKey(newTexture.texture))
                        continue;
                    if (freeList.IsEmpty)
                    {
                        int newIndex = textureStates.Length;
                        textureStates.Add(new TextureState
                        {
                            texture            = newTexture.texture,
                            mipmapCount        = newTexture.mipmapCount,
                            assignedLevel      = int.MaxValue,  // This value is different from our base level, which means our difference checker at the end will update it even if no entity was visible.
                            oneTwoThreeFourAgo = newTexture.mipmapCount - 1,
                            evaluatedLevel     = newTexture.mipmapCount - 1
                        });
                        textureToStateIndexMap.Add(newTexture.texture, newIndex);
                    }
                    else
                    {
                        var newIndex = freeList[freeList.Length - 1];
                        freeList.RemoveAt(freeList.Length - 1);
                        textureStates[newIndex] = new TextureState
                        {
                            texture            = newTexture.texture,
                            mipmapCount        = newTexture.mipmapCount,
                            assignedLevel      = int.MaxValue,
                            oneTwoThreeFourAgo = newTexture.mipmapCount - 1,
                            evaluatedLevel     = newTexture.mipmapCount - 1
                        };
                        textureToStateIndexMap.Add(newTexture.texture, newIndex);
                    }
                }
            }

            struct NewTexture
            {
                public UnityObjectRef<Texture2D> texture;
                public int                       mipmapCount;
            }
        }

        [BurstCompile]
        struct EvaluateEntitiesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>     perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo>             materialMeshInfoHandle;
            [ReadOnly] public ComponentTypeHandle<WorldRenderBounds>            worldRenderBoundsHandle;
            [ReadOnly] public SharedComponentTypeHandle<RenderMeshArray>        renderMeshArrayHandle;
            [ReadOnly] public SharedComponentTypeHandle<StreamingMipMapArray>   streamingMipMapArrayHandle;
            [ReadOnly] public NativeHashMap<int, UnmanagedStreamingMipMapArray> sharedIndexToArrayMap;
            [ReadOnly] public NativeParallelHashMap<int, BRGRenderMeshArray>    sharedIndexToBrgRenderMeshArrayMap;
            [ReadOnly] public NativeHashMap<UnityObjectRef<Texture2D>, int>     textureToStateIndexMap;
            [ReadOnly] public BufferLookup<MipMapCameraParameters>              cameraParametersLookup;
            [ReadOnly] public NativeList<TextureState>                          textureStates;

            [NativeDisableParallelForRestriction] public NativeArray<UnsafeList<int> > newLevelsPerThread;

            public Entity                           worldBlackboardEntity;
            public AllocatorManager.AllocatorHandle worldUpdateAllocator;

            [NativeSetThreadIndex] int threadIndex;

            HasChecker<UseMmiRangeLodTag>      useMmiRangeLodChecker;
            HasChecker<OverrideMeshInRangeTag> overrideMeshInRangeChecker;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (sharedIndexToBrgRenderMeshArrayMap.IsEmpty || sharedIndexToArrayMap.IsEmpty)
                    return;

                var mask = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                if ((mask.lower.Value | mask.upper.Value) == 0)
                    return;

                bool useMmiRangeLod   = useMmiRangeLodChecker[chunk];
                bool hasOverrideMesh  = overrideMeshInRangeChecker[chunk];
                var  mmiArray         = chunk.GetComponentDataPtrRO(ref materialMeshInfoHandle);
                var  worldBoundsArray = chunk.GetComponentDataPtrRO(ref worldRenderBoundsHandle);
                var  rmaIndex         = chunk.GetSharedComponentIndex(renderMeshArrayHandle);
                sharedIndexToBrgRenderMeshArrayMap.TryGetValue(rmaIndex, out var rma);
                var smmaIndex = chunk.GetSharedComponentIndex(streamingMipMapArrayHandle);
                sharedIndexToArrayMap.TryGetValue(smmaIndex, out var smma);

                var cameraParametersArray = cameraParametersLookup[worldBlackboardEntity].AsNativeArray();

                var entityEnumerator = new ChunkEntityEnumerator(true, new v128(mask.lower.Value, mask.upper.Value), chunk.Count);
                while (entityEnumerator.NextEntityIndex(out var entityIndex))
                {
                    var mmi         = mmiArray[entityIndex];
                    var worldBounds = worldBoundsArray[entityIndex];

                    if (mmi.HasMaterialMeshIndexRange)
                    {
                        RangeInt matMeshIndexRange = mmi.MaterialMeshIndexRange;
                        if (matMeshIndexRange.length == 127)
                        {
                            int newLength             = (rma.MaterialMeshSubMeshes[matMeshIndexRange.start + 1].SubMeshIndex >> 16) & 0xff;
                            newLength                |= (rma.MaterialMeshSubMeshes[matMeshIndexRange.start + 2].SubMeshIndex >> 8) & 0xff00;
                            newLength                |= rma.MaterialMeshSubMeshes[matMeshIndexRange.start + 3].SubMeshIndex & 0xff0000;
                            matMeshIndexRange.length  = newLength;
                        }

                        // Todo: We could potentially run the LOD Pack evaluation algorithm here against all captured cameras to cull unused meshes and materials from evaluation.

                        int overrideMeshIndex = 0;
                        if (hasOverrideMesh)
                        {
                            if (mmi.IsRuntimeMesh)
                            {
                                if (!smma.batchMeshToRmaIndexMap.TryGetValue(mmi.MeshID, out overrideMeshIndex))
                                    continue; // Runtime meshes are not supported
                            }
                            else
                                overrideMeshIndex = mmi.MeshArrayIndex;
                        }

                        for (int i = 0; i < matMeshIndexRange.length; i++)
                        {
                            int matMeshSubMeshIndex = matMeshIndexRange.start + i;

                            // Drop if OOB. Errors should have been reported already so no need to log anything
                            if (matMeshSubMeshIndex >= rma.MaterialMeshSubMeshes.Length)
                                continue;

                            BatchMaterialMeshSubMesh matMeshSubMesh = rma.MaterialMeshSubMeshes[matMeshSubMeshIndex];
                            var                      meshIndex      = hasOverrideMesh ? overrideMeshIndex : smma.batchMeshToRmaIndexMap[matMeshSubMesh.Mesh];
                            var                      materialIndex  = smma.batchMaterialToRmaIndexMap[matMeshSubMesh.Material];

                            EvaluateDrawInstance(ref smma, cameraParametersArray, in worldBounds, meshIndex, materialIndex);
                        }
                    }
                    else
                    {
                        var meshIndex = 0;
                        if (mmi.IsRuntimeMesh)
                        {
                            if (!smma.batchMeshToRmaIndexMap.TryGetValue(mmi.MeshID, out meshIndex))
                                continue; // Runtime meshes are not supported
                        }
                        else
                            meshIndex = mmi.MeshArrayIndex;

                        var materialIndex = 0;
                        if (mmi.IsRuntimeMaterial)
                        {
                            if (!smma.batchMaterialToRmaIndexMap.TryGetValue(mmi.MaterialID, out materialIndex))
                                continue; // Runtime materials are not supported
                        }
                        else
                            materialIndex = mmi.MaterialArrayIndex;

                        EvaluateDrawInstance(ref smma, cameraParametersArray, in worldBounds, meshIndex, materialIndex);
                    }
                }
            }

            void EvaluateDrawInstance(ref UnmanagedStreamingMipMapArray smma,
                                      in ReadOnlySpan<MipMapCameraParameters> cameraParametersArray,
                                      in WorldRenderBounds worldBounds,
                                      int meshIndex,
                                      int materialIndex)
            {
                var range = smma.ranges[materialIndex];
                if (range.count == 0)
                    return;

                var meshMetric = smma.meshMetrics[meshIndex];
                if (meshMetric.uv0Metric == 0f && meshMetric.meshLocalBounds.Equals(float3.zero))
                    return; // There's no metric for this mesh.

                if (!newLevelsPerThread[threadIndex].IsCreated)
                {
                    var newLevels = new UnsafeList<int>(textureStates.Length, worldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
                    newLevels.AddReplicate(int.MaxValue, textureStates.Length);
                    newLevelsPerThread[threadIndex] = newLevels;
                }

                var levelsArray = newLevelsPerThread[threadIndex];

                for (int i = 0; i < range.count; i++)
                {
                    var textureInfo = smma.streamingTextures[range.start + i];
                    int bestLevel   = int.MaxValue;
                    foreach (var camera in cameraParametersArray)
                    {
                        var level = LodUtilities.DesiredMipMapLevelFrom(in worldBounds,
                                                                        in meshMetric,
                                                                        textureInfo.textureScale,
                                                                        textureInfo.texelCount,
                                                                        camera.position,
                                                                        camera.cameraFactor,
                                                                        camera.isPerspective);
                        bestLevel = math.min(bestLevel, level);
                    }
                    var textureIndex          = textureToStateIndexMap[textureInfo.streamingTexture];
                    levelsArray[textureIndex] = math.min(levelsArray[textureIndex], bestLevel);
                }
            }
        }

        [BurstCompile]
        struct MergeThreadLevelsJob : IJob
        {
            public NativeArray<UnsafeList<int> > newLevelsPerThread;
            public NativeList<TextureState>      textureStates;

            public void Execute()
            {
                var usedThreads = new UnsafeList<UnsafeList<int> >(newLevelsPerThread.Length, Allocator.Temp);
                foreach (var thread in newLevelsPerThread)
                {
                    if (thread.IsCreated)
                        usedThreads.Add(thread);
                }

                if (usedThreads.IsEmpty)
                    return;

                var zero = usedThreads[0];
                for (int threadIndex = 1; threadIndex < usedThreads.Length; threadIndex++)
                {
                    var thread = usedThreads[threadIndex];
                    for (int i = 0; i < zero.Length; i++)
                        zero[i] = math.min(zero[i], thread[i]);
                }

                for (int i = 0; i < textureStates.Length; i++)
                {
                    ref var state        = ref textureStates.ElementAt(i);
                    state.evaluatedLevel = math.min(state.evaluatedLevel, zero[i]);
                }
            }
        }

        [BurstCompile]
        struct PostProcessTexturesJob : IJob
        {
            public NativeList<TextureState>                textureStates;
            public BufferLookup<MipMapStreamingAssignment> mipMapsStreamingAssignmentLookup;
            public Entity                                  worldBlackbaordEntity;

            public void Execute()
            {
                var assignments = mipMapsStreamingAssignmentLookup[worldBlackbaordEntity];
                assignments.Clear();

                for (int i = 0; i < textureStates.Length; i++)
                {
                    ref var state = ref textureStates.ElementAt(i);
                    if (state.texture == default)
                        continue;

                    var bestLevel = math.min(state.evaluatedLevel, math.cmin(state.oneTwoThreeFourAgo));
                    if (bestLevel != state.assignedLevel)
                    {
                        state.assignedLevel = bestLevel;
                        assignments.Add(new MipMapStreamingAssignment
                        {
                            texture = state.texture,
                            level   = bestLevel,
                        });
                    }
                }
            }
        }
    }
}

