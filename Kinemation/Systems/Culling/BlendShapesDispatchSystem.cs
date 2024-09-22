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
    public partial struct BlendShapesDispatchSystem : ISystem, ICullingComputeDispatchSystem<BlendShapesDispatchSystem.CollectState, BlendShapesDispatchSystem.WriteState>
    {
        LatiosWorldUnmanaged latiosWorld;

        UnityObjectRef<ComputeShader>                        m_dispatchShader;
        EntityQuery                                          m_query;
        CullingComputeDispatchData<CollectState, WriteState> m_data;

        // Shader bindings
        int _srcVertices;
        int _dstVertices;
        int _blendShapeDeltas;
        int _startOffset;
        int _metaBuffer;
        int _latiosDeformBuffer;
        int _DeformedMeshData;
        int _PreviousFrameDeformedMeshData;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_data = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorld);

            m_query = state.Fluent().With<BlendShapeWeight>(true).With<BlendShapeState>(true).With<BoundMesh>(true)
                      .With<ChunkPerDispatchCullingMask>(true, true).With<ChunkPerFrameCullingMask>(true, true)
                      .Without<DisableComputeShaderProcessingTag>().Build();

            m_dispatchShader = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("ShapeBlending");

            _srcVertices                   = Shader.PropertyToID("_srcVertices");
            _dstVertices                   = Shader.PropertyToID("_dstVertices");
            _blendShapeDeltas              = Shader.PropertyToID("_blendShapeDeltas2");
            _startOffset                   = Shader.PropertyToID("_startOffset");
            _metaBuffer                    = Shader.PropertyToID("_metaBuffer");
            _latiosDeformBuffer            = Shader.PropertyToID("_latiosDeformBuffer");
            _DeformedMeshData              = Shader.PropertyToID("_DeformedMeshData");
            _PreviousFrameDeformedMeshData = Shader.PropertyToID("_PreviousFrameDeformedMeshData");
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state) => m_data.DoUpdate(ref state, ref this);

        public CollectState Collect(ref SystemState state)
        {
            var streamCount       = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator);
            streamCount[0]        = m_query.CalculateChunkCountWithoutFiltering();
            var streamConstructJh = NativeStream.ScheduleConstruct(out var stream, streamCount, default, state.WorldUpdateAllocator);
            var collectJh         = new GatherUploadOperationsJob
            {
                meshHandle                           = SystemAPI.GetComponentTypeHandle<BoundMesh>(true),
                currentDeformShaderIndexHandle       = SystemAPI.GetComponentTypeHandle<CurrentDeformShaderIndex>(true),
                deformClassificationMap              = latiosWorld.worldBlackboardEntity.GetCollectionComponent<DeformClassificationMap>(true).deformClassificationMap,
                entityHandle                         = SystemAPI.GetEntityTypeHandle(),
                legacyComputeDeformShaderIndexHandle = SystemAPI.GetComponentTypeHandle<LegacyComputeDeformShaderIndex>(true),
                legacyDotsDeformShaderIndexHandle    = SystemAPI.GetComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>(true),
                perDispatchMaskHandle                = SystemAPI.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true),
                perFrameMaskHandle                   = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                previousDeformShaderIndexHandle      = SystemAPI.GetComponentTypeHandle<PreviousDeformShaderIndex>(true),
                stateHandle                          = SystemAPI.GetComponentTypeHandle<BlendShapeState>(true),
                streamWriter                         = stream.AsWriter(),
                twoAgoDeformShaderIndexHandle        = SystemAPI.GetComponentTypeHandle<TwoAgoDeformShaderIndex>(true),
                weightsHandle                        = SystemAPI.GetBufferTypeHandle<BlendShapeWeight>(true),
            }.ScheduleParallel(m_query, JobHandle.CombineDependencies(streamConstructJh, state.Dependency));

            var payloads                  = new NativeList<UploadPayload>(1, state.WorldUpdateAllocator);
            var requiredWeightsBufferSize = new NativeReference<uint>(state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            state.Dependency              = new MapPayloadsToWeightsBufferJob
            {
                streamReader              = stream.AsReader(),
                payloads                  = payloads,
                requiredWeightsBufferSize = requiredWeightsBufferSize
            }.Schedule(collectJh);

            // Fetching this now because culling jobs are still running (hopefully).
            var graphicsBroker = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();

            return new CollectState
            {
                broker                    = graphicsBroker,
                payloads                  = payloads,
                requiredWeightsBufferSize = requiredWeightsBufferSize
            };
        }

        public WriteState Write(ref SystemState state, ref CollectState collectState)
        {
            if (collectState.payloads.IsEmpty)
            {
                // skip rest of loop.
                return default;
            }

            var graphicsBroker            = collectState.broker;
            var payloads                  = collectState.payloads;
            var requiredWeightsBufferSize = collectState.requiredWeightsBufferSize.Value;

            var metaBufferSize = (uint)payloads.Length * 2 + requiredWeightsBufferSize;
            var metaBuffer     = graphicsBroker.GetMetaUint4UploadBuffer(metaBufferSize);

            state.Dependency = new WriteMetaBufferJob
            {
                payloads       = payloads.AsDeferredJobArray(),
                meshGpuEntries = latiosWorld.worldBlackboardEntity.GetCollectionComponent<MeshGpuManager>(true).entries.AsDeferredJobArray(),
                metaBuffer     = metaBuffer.LockBufferForWrite<uint4>(0, (int)metaBufferSize)
            }.Schedule(payloads, 1, state.Dependency);

            return new WriteState
            {
                broker         = graphicsBroker,
                payloads       = payloads,
                metaBuffer     = metaBuffer,
                metaBufferSize = metaBufferSize
            };
        }

        public void Dispatch(ref SystemState state, ref WriteState writeState)
        {
            if (!writeState.broker.isCreated)
                return;

            var graphicsBroker = writeState.broker;
            var metaBuffer     = writeState.metaBuffer;
            var payloads       = writeState.payloads;
            var metaBufferSize = writeState.metaBufferSize;

            metaBuffer.UnlockBufferAfterWrite<uint4>((int)metaBufferSize);

            var persistentBuffer = graphicsBroker.GetDeformBuffer(latiosWorld.worldBlackboardEntity.GetComponentData<MaxRequiredDeformData>().maxRequiredDeformVertices);
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

            GraphicsUnmanaged.SetGlobalBuffer(_DeformedMeshData,              persistentBuffer);
            GraphicsUnmanaged.SetGlobalBuffer(_PreviousFrameDeformedMeshData, persistentBuffer);
            GraphicsUnmanaged.SetGlobalBuffer(_latiosDeformBuffer,            persistentBuffer);
        }

        public struct CollectState
        {
            internal GraphicsBufferBroker      broker;
            internal NativeList<UploadPayload> payloads;
            internal NativeReference<uint>     requiredWeightsBufferSize;
        }

        public struct WriteState
        {
            internal GraphicsBufferBroker      broker;
            internal GraphicsBufferUnmanaged   metaBuffer;
            internal NativeList<UploadPayload> payloads;
            internal uint                      metaBufferSize;
        }

        internal unsafe struct UploadPayload
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
            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask>            perDispatchMaskHandle;
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
                var dispatchMask = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                var frameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
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
                        if (weightsCount == buffer.Length)
                        {
                            currentPtr  = buffer.AsNativeArray().GetSubArray(weightsCount * currentRotation, weightsCount).GetUnsafeReadOnlyPtr();
                            previousPtr = currentPtr;
                            twoAgoPtr   = currentPtr;
                        }
                        else
                        {
                            currentPtr  = null;
                            previousPtr = currentPtr;
                            twoAgoPtr   = currentPtr;

                            UnityEngine.Debug.LogError(
                                $"Entity {entities[i]} has the wrong number of weights ({buffer.Length / 3} vs expected {weightsCount}) in DynamicBuffer<BlendShapeWeight>. Uploading zero weights instead.");
                        }
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

