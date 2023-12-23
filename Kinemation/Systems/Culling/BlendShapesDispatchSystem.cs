using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class BlendShapesDispatchSystem : CullingComputeDispatchSubSystemBase
    {
        ComputeShader m_dispatchShader;

        EntityQuery m_query;

        // Shader bindings
        int _srcVertices;
        int _dstVertices;
        int _blendShapeDeltas;
        int _startOffset;
        int _metaBuffer;
        int _latiosDeformBuffer;
        int _DeformedMeshData;
        int _PreviousFrameDeformedMeshData;

        protected override void OnCreate()
        {
            m_query = Fluent.With<BlendShapeWeight>(true).With<BlendShapeState>(true).With<BoundMesh>(true)
                      .With<ChunkPerCameraCullingMask>(true, true).With<ChunkPerFrameCullingMask>(true, true)
                      .Without<DisableComputeShaderProcessingTag>().Build();

            m_dispatchShader = Resources.Load<ComputeShader>("ShapeBlending");

            _srcVertices                   = Shader.PropertyToID("_srcVertices");
            _dstVertices                   = Shader.PropertyToID("_dstVertices");
            _blendShapeDeltas              = Shader.PropertyToID("_blendShapeDeltas2");
            _startOffset                   = Shader.PropertyToID("_startOffset");
            _metaBuffer                    = Shader.PropertyToID("_metaBuffer");
            _latiosDeformBuffer            = Shader.PropertyToID("_latiosDeformBuffer");
            _DeformedMeshData              = Shader.PropertyToID("_DeformedMeshData");
            _PreviousFrameDeformedMeshData = Shader.PropertyToID("_PreviousFrameDeformedMeshData");
        }

        protected override IEnumerable<bool> UpdatePhase()
        {
            while (true)
            {
                if (!GetPhaseActions(CullingComputeDispatchState.Collect, out var terminate))
                {
                    yield return false;
                    continue;
                }
                if (terminate)
                    break;

                var streamCount       = CollectionHelper.CreateNativeArray<int>(1, WorldUpdateAllocator);
                streamCount[0]        = m_query.CalculateChunkCountWithoutFiltering();
                var streamConstructJh = NativeStream.ScheduleConstruct(out var stream, streamCount, default, WorldUpdateAllocator);
                var collectJh         = new GatherUploadOperationsJob
                {
                    meshHandle                           = SystemAPI.GetComponentTypeHandle<BoundMesh>(true),
                    currentDeformShaderIndexHandle       = SystemAPI.GetComponentTypeHandle<CurrentDeformShaderIndex>(true),
                    deformClassificationMap              = worldBlackboardEntity.GetCollectionComponent<DeformClassificationMap>(true).deformClassificationMap,
                    entityHandle                         = SystemAPI.GetEntityTypeHandle(),
                    legacyComputeDeformShaderIndexHandle = SystemAPI.GetComponentTypeHandle<LegacyComputeDeformShaderIndex>(true),
                    legacyDotsDeformShaderIndexHandle    = SystemAPI.GetComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>(true),
                    perCameraMaskHandle                  = SystemAPI.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                    perFrameMaskHandle                   = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                    previousDeformShaderIndexHandle      = SystemAPI.GetComponentTypeHandle<PreviousDeformShaderIndex>(true),
                    stateHandle                          = SystemAPI.GetComponentTypeHandle<BlendShapeState>(true),
                    streamWriter                         = stream.AsWriter(),
                    twoAgoDeformShaderIndexHandle        = SystemAPI.GetComponentTypeHandle<TwoAgoDeformShaderIndex>(true),
                    weightsHandle                        = SystemAPI.GetBufferTypeHandle<BlendShapeWeight>(true),
                }.ScheduleParallel(m_query, JobHandle.CombineDependencies(streamConstructJh, Dependency));

                var payloads                  = new NativeList<UploadPayload>(1, WorldUpdateAllocator);
                var requiredWeightsBufferSize = new NativeReference<uint>(WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
                Dependency                    = new MapPayloadsToWeightsBufferJob
                {
                    streamReader              = stream.AsReader(),
                    payloads                  = payloads,
                    requiredWeightsBufferSize = requiredWeightsBufferSize
                }.Schedule(collectJh);

                // Fetching this now because culling jobs are still running (hopefully).
                var graphicsBroker = worldBlackboardEntity.GetManagedStructComponent<GraphicsBufferBrokerReference>().graphicsBufferBroker;

                yield return true;

                if (!GetPhaseActions(CullingComputeDispatchState.Write, out terminate))
                    continue;
                if (terminate)
                    break;

                if (payloads.IsEmpty)
                {
                    // skip rest of loop.
                    yield return true;

                    if (!GetPhaseActions(CullingComputeDispatchState.Dispatch, out terminate))
                        continue;
                    if (terminate)
                        break;

                    yield return true;
                    continue;
                }

                var metaBufferSize = (uint)payloads.Length * 2 + requiredWeightsBufferSize.Value;
                var metaBuffer     = graphicsBroker.GetMetaUint4UploadBuffer(metaBufferSize);

                Dependency = new WriteMetaBufferJob
                {
                    payloads       = payloads.AsDeferredJobArray(),
                    meshGpuEntries = worldBlackboardEntity.GetCollectionComponent<MeshGpuManager>(true).entries.AsDeferredJobArray(),
                    metaBuffer     = metaBuffer.LockBufferForWrite<uint4>(0, (int)metaBufferSize)
                }.Schedule(payloads, 1, Dependency);

                yield return true;

                if (!GetPhaseActions(CullingComputeDispatchState.Dispatch, out terminate))
                    continue;

                metaBuffer.UnlockBufferAfterWrite<uint4>((int)metaBufferSize);

                if (terminate)
                    break;

                var persistentBuffer = graphicsBroker.GetDeformBuffer(worldBlackboardEntity.GetComponentData<MaxRequiredDeformData>().maxRequiredDeformVertices);
                m_dispatchShader.SetBuffer(0, _srcVertices,      graphicsBroker.GetMeshVerticesBuffer());
                m_dispatchShader.SetBuffer(0, _blendShapeDeltas, graphicsBroker.GetMeshBlendShapesBufferRO());
                m_dispatchShader.SetBuffer(0, _dstVertices,      persistentBuffer);
                m_dispatchShader.SetBuffer(0, _metaBuffer,       metaBuffer);

                for (uint dispatchesRemaining = (uint)payloads.Length, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_dispatchShader.SetInt(_startOffset, (int)offset);
                    //UnityEngine.Debug.Log("Dispatching");
                    m_dispatchShader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }

                Shader.SetGlobalBuffer(_DeformedMeshData,              persistentBuffer);
                Shader.SetGlobalBuffer(_PreviousFrameDeformedMeshData, persistentBuffer);
                Shader.SetGlobalBuffer(_latiosDeformBuffer,            persistentBuffer);

                yield return true;
            }
        }

        unsafe struct UploadPayload
        {
            public void* weightsPtr;
            public int   meshEntryIndex;
            public uint  nonzeroWeightsCount;
            public uint  persistentBufferStart;
            public uint  weightsBufferStart;
            public bool  requiresMeshUpload;
        }

        [BurstCompile]
        struct GatherUploadOperationsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>              perCameraMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>               perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<BlendShapeState>                        stateHandle;
            [ReadOnly] public BufferTypeHandle<BlendShapeWeight>                          weightsHandle;
            [ReadOnly] public ComponentTypeHandle<BoundMesh>                              meshHandle;
            [ReadOnly] public EntityTypeHandle                                            entityHandle;
            [ReadOnly] public ComponentTypeHandle<CurrentDeformShaderIndex>               currentDeformShaderIndexHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousDeformShaderIndex>              previousDeformShaderIndexHandle;
            [ReadOnly] public ComponentTypeHandle<TwoAgoDeformShaderIndex>                twoAgoDeformShaderIndexHandle;
            [ReadOnly] public ComponentTypeHandle<LegacyComputeDeformShaderIndex>         legacyComputeDeformShaderIndexHandle;
            [ReadOnly] public ComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>      legacyDotsDeformShaderIndexHandle;
            [ReadOnly] public NativeParallelHashMap<ArchetypeChunk, DeformClassification> deformClassificationMap;

            [NativeDisableParallelForRestriction] public NativeStream.Writer streamWriter;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                //UnityEngine.Debug.Log("Checking chunk for upload");
                var cameraMask = chunk.GetChunkComponentData(ref perCameraMaskHandle);
                var frameMask  = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower      = cameraMask.lower.Value & (~frameMask.lower.Value);
                var upper      = cameraMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return;

                streamWriter.BeginForEachIndex(unfilteredChunkIndex);
                var states                     = chunk.GetNativeArray(ref stateHandle);
                var weightsBuffers             = chunk.GetBufferAccessor(ref weightsHandle);
                var meshes                     = chunk.GetNativeArray(ref meshHandle);
                var entities                   = chunk.GetNativeArray(entityHandle);
                var currentShaderIndices       = chunk.GetNativeArray(ref currentDeformShaderIndexHandle);
                var previousShaderIndices      = chunk.GetNativeArray(ref previousDeformShaderIndexHandle);
                var twoAgoShaderIndices        = chunk.GetNativeArray(ref twoAgoDeformShaderIndexHandle);
                var legacyComputeShaderIndices = chunk.GetNativeArray(ref legacyComputeDeformShaderIndexHandle);
                var legacyDotsShaderIndices    = chunk.GetNativeArray(ref legacyDotsDeformShaderIndexHandle);
                var classification             = deformClassificationMap[chunk];

                bool needsCurrent  = (classification & DeformClassification.AnyCurrentDeform) != DeformClassification.None;
                bool needsPrevious = (classification & DeformClassification.AnyPreviousDeform) != DeformClassification.None;
                bool needsTwoAgo   = (classification & DeformClassification.TwoAgoDeform) != DeformClassification.None;

                var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var state              = states[i].state;
                    var mask               = state & BlendShapeState.Flags.RotationMask;
                    var currentRotation    = BlendShapeState.CurrentFromMask[(byte)mask];
                    var previousRotation   = BlendShapeState.PreviousFromMask[(byte)mask];
                    currentRotation        = (state & BlendShapeState.Flags.IsDirty) == BlendShapeState.Flags.IsDirty ? currentRotation : previousRotation;
                    var twoAgoRotation     = BlendShapeState.TwoAgoFromMask[(byte)mask];
                    var buffer             = weightsBuffers[i];
                    var blob               = meshes[i].meshBlob;
                    var weightsCount       = blob.Value.blendShapesData.shapes.Length;
                    var requiresMeshUpload = (classification & DeformClassification.RequiresUploadDynamicMesh) == DeformClassification.None;
                    var meshEntryIndex     = meshes[i].meshEntryIndex;

                    void* currentPtr, previousPtr, twoAgoPtr;

                    if (Unity.Burst.CompilerServices.Hint.Unlikely(weightsCount * 3 != buffer.Length))
                    {
                        currentPtr  = null;
                        previousPtr = currentPtr;
                        twoAgoPtr   = currentPtr;

                        UnityEngine.Debug.LogError(
                            $"Entity {entities[i]} has the wrong number of weights ({buffer.Length / 3} vs expected {weightsCount}) in DynamicBuffer<BlendShapeWeight>. Uploading zero weights instead.");
                    }
                    else
                    {
                        currentPtr  = buffer.AsNativeArray().GetSubArray(weightsCount * currentRotation, weightsCount).GetUnsafeReadOnlyPtr();
                        previousPtr = buffer.AsNativeArray().GetSubArray(weightsCount * previousRotation, weightsCount).GetUnsafeReadOnlyPtr();
                        twoAgoPtr   = buffer.AsNativeArray().GetSubArray(weightsCount * twoAgoRotation, weightsCount).GetUnsafeReadOnlyPtr();
                    }

                    if (needsCurrent)
                    {
                        var  floatWeightsBuffer  = (float*)currentPtr;
                        uint nonzeroWeightsCount = 0;
                        if (floatWeightsBuffer != null)
                        {
                            for (int j = 0; j < weightsCount; j++)
                                nonzeroWeightsCount += math.select(0u, 1u, floatWeightsBuffer[j] != 0f);
                        }

                        uint gpuTarget = 0;
                        if ((classification & DeformClassification.CurrentDeform) != DeformClassification.None)
                            gpuTarget = currentShaderIndices[i].firstVertexIndex;
                        else if ((classification & DeformClassification.LegacyCompute) != DeformClassification.None)
                            gpuTarget = legacyComputeShaderIndices[i].firstVertexIndex;
                        else
                            gpuTarget = legacyDotsShaderIndices[i].parameters.x;
                        //UnityEngine.Debug.Log("Wrote upload payload");
                        streamWriter.Write(new UploadPayload
                        {
                            weightsPtr            = currentPtr,
                            meshEntryIndex        = meshEntryIndex,
                            nonzeroWeightsCount   = nonzeroWeightsCount,
                            persistentBufferStart = gpuTarget,
                            requiresMeshUpload    = requiresMeshUpload
                        });
                    }
                    if (needsPrevious)
                    {
                        var  floatWeightsBuffer  = (float*)previousPtr;
                        uint nonzeroWeightsCount = 0;
                        if (floatWeightsBuffer != null)
                        {
                            for (int j = 0; j < weightsCount; j++)
                                nonzeroWeightsCount += math.select(0u, 1u, floatWeightsBuffer[j] != 0f);
                        }

                        uint gpuTarget = 0;
                        if ((classification & DeformClassification.PreviousDeform) != DeformClassification.None)
                            gpuTarget = previousShaderIndices[i].firstVertexIndex;
                        else
                            gpuTarget = legacyDotsShaderIndices[i].parameters.y;
                        streamWriter.Write(new UploadPayload
                        {
                            weightsPtr            = previousPtr,
                            meshEntryIndex        = meshEntryIndex,
                            nonzeroWeightsCount   = nonzeroWeightsCount,
                            persistentBufferStart = gpuTarget,
                            requiresMeshUpload    = requiresMeshUpload
                        });
                    }
                    if (needsTwoAgo)
                    {
                        var  floatWeightsBuffer  = (float*)twoAgoPtr;
                        uint nonzeroWeightsCount = 0;
                        if (floatWeightsBuffer != null)
                        {
                            for (int j = 0; j < weightsCount; j++)
                                nonzeroWeightsCount += math.select(0u, 1u, floatWeightsBuffer[j] != 0f);
                        }

                        streamWriter.Write(new UploadPayload
                        {
                            weightsPtr            = twoAgoPtr,
                            meshEntryIndex        = meshEntryIndex,
                            nonzeroWeightsCount   = nonzeroWeightsCount,
                            persistentBufferStart = twoAgoShaderIndices[i].firstVertexIndex,
                            requiresMeshUpload    = requiresMeshUpload
                        });
                    }
                }

                streamWriter.EndForEachIndex();
            }
        }

        [BurstCompile]
        struct MapPayloadsToWeightsBufferJob : IJob
        {
            [ReadOnly] public NativeStream.Reader streamReader;
            public NativeList<UploadPayload>      payloads;
            public NativeReference<uint>          requiredWeightsBufferSize;

            public void Execute()
            {
                var totalCount    = streamReader.Count();
                payloads.Capacity = totalCount;
                var  streamCount  = streamReader.ForEachCount;
                uint prefixSum    = 0;

                for (int streamIndex = 0; streamIndex < streamCount; streamIndex++)
                {
                    var count = streamReader.BeginForEachIndex(streamIndex);
                    for (int i = 0; i < count; i++)
                    {
                        var payload                 = streamReader.Read<UploadPayload>();
                        payload.weightsBufferStart  = prefixSum;
                        prefixSum                  += payload.nonzeroWeightsCount;
                        payloads.AddNoResize(payload);
                    }
                }

                requiredWeightsBufferSize.Value = prefixSum;
            }
        }

        [BurstCompile]
        struct WriteMetaBufferJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<UploadPayload>                    payloads;
            [ReadOnly] public NativeArray<MeshGpuEntry>                     meshGpuEntries;
            [NativeDisableParallelForRestriction] public NativeArray<uint4> metaBuffer;

            public unsafe void Execute(int index)
            {
                //UnityEngine.Debug.Log("Writing meta");
                var payload           = payloads[index];
                metaBuffer[2 * index] = new uint4(payload.nonzeroWeightsCount,
                                                  payload.weightsBufferStart + (uint)payloads.Length * 2,
                                                  0,
                                                  payload.persistentBufferStart);
                var entry                 = meshGpuEntries[payload.meshEntryIndex];
                metaBuffer[2 * index + 1] = new uint4(math.select(0u, 1u, payload.requiresMeshUpload),
                                                      entry.verticesStart,
                                                      entry.verticesCount,
                                                      0u);
                var weightsBuffer =
                    (uint4*)metaBuffer.GetSubArray((int)payload.weightsBufferStart + payloads.Length * 2, (int)payload.nonzeroWeightsCount).GetUnsafePtr();
                ref var blobShapes             = ref entry.blob.Value.blendShapesData;
                var     weightsPtr             = (float*)payload.weightsPtr;
                var     nextNonzeroWeightIndex = 0;
                if (weightsPtr != null)
                {
                    for (int i = 0; i < blobShapes.shapes.Length; i++)
                    {
                        if (weightsPtr[i] == 0f)
                            continue;

                        //float maxDisplacement = 0f;
                        //for (int j = 0; j < blobShapes.shapes[i].count; j++)
                        //{
                        //    var vert        = blobShapes.gpuData[(int)(blobShapes.shapes[i].start + j)];
                        //    maxDisplacement = math.max(math.length(vert.positionDisplacement), maxDisplacement);
                        //    if (vert.targetVertexIndex >= entry.blob.Value.undeformedVertices.Length)
                        //        UnityEngine.Debug.Log(
                        //            $"dispatch vertex delta at {j} references vertex {vert.targetVertexIndex} for mesh vertex count {entry.blob.Value.undeformedVertices.Length}");
                        //    UnityEngine.Debug.Log(
                        //        $"dispatch vertex: {j}, {vert.targetVertexIndex}, {vert.positionDisplacement}, {vert.normalDisplacement}, {vert.tangentDisplacement}");
                        //}
                        //UnityEngine.Debug.Log($"verticesCount: {blobShapes.shapes[i].count}, dispatch maxDisplacement: {maxDisplacement}");
                        weightsBuffer[nextNonzeroWeightIndex] = new uint4(blobShapes.shapes[i].permutationID,
                                                                          blobShapes.shapes[i].count,
                                                                          blobShapes.shapes[i].start + entry.blendShapesStart,
                                                                          math.asuint(weightsPtr[i]));
                        nextNonzeroWeightIndex++;
                    }
                }

                for (int i = 0; i < (int)payload.nonzeroWeightsCount; i++)
                {
                    uint runCount = 1;
                    for (int j = i + 1; j < (int)payload.nonzeroWeightsCount; j++)
                    {
                        if (weightsBuffer[i].x != weightsBuffer[j].x)
                            break;
                        runCount++;
                    }
                    weightsBuffer[i].x  = runCount;
                    i                  += (int)runCount;
                }
            }
        }
    }
}

