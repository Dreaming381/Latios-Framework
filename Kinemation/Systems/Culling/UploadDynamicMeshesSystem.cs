using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class UploadDynamicMeshesSystem : CullingComputeDispatchSubSystemBase
    {
        ComputeShader m_uploadShader;

        EntityQuery m_query;

        // Shader bindings
        int _src;
        int _dst;
        int _startOffset;
        int _meta;
        int _latiosDeformBuffer;
        int _DeformedMeshData;
        int _PreviousFrameDeformedMeshData;

        protected override void OnCreate()
        {
            m_query = Fluent.With<DynamicMeshVertex>(true).With<DynamicMeshState>(true).With<BoundMesh>(true)
                      .With<ChunkPerCameraCullingMask>(true, true).With<ChunkPerFrameCullingMask>(true, true).Build();

            m_uploadShader                 = Resources.Load<ComputeShader>("UploadVertices");
            _src                           = Shader.PropertyToID("_src");
            _dst                           = Shader.PropertyToID("_dst");
            _startOffset                   = Shader.PropertyToID("_startOffset");
            _meta                          = Shader.PropertyToID("_meta");
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
                    blobHandle                           = SystemAPI.GetComponentTypeHandle<BoundMesh>(true),
                    currentDeformShaderIndexHandle       = SystemAPI.GetComponentTypeHandle<CurrentDeformShaderIndex>(true),
                    deformClassificationMap              = worldBlackboardEntity.GetCollectionComponent<DeformClassificationMap>(true).deformClassificationMap,
                    entityHandle                         = SystemAPI.GetEntityTypeHandle(),
                    legacyComputeDeformShaderIndexHandle = SystemAPI.GetComponentTypeHandle<LegacyComputeDeformShaderIndex>(true),
                    legacyDotsDeformShaderIndexHandle    = SystemAPI.GetComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>(true),
                    perCameraMaskHandle                  = SystemAPI.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                    perFrameMaskHandle                   = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                    previousDeformShaderIndexHandle      = SystemAPI.GetComponentTypeHandle<PreviousDeformShaderIndex>(true),
                    stateHandle                          = SystemAPI.GetComponentTypeHandle<DynamicMeshState>(true),
                    streamWriter                         = stream.AsWriter(),
                    twoAgoDeformShaderIndexHandle        = SystemAPI.GetComponentTypeHandle<TwoAgoDeformShaderIndex>(true),
                    verticesHandle                       = SystemAPI.GetBufferTypeHandle<DynamicMeshVertex>(true)
                }.ScheduleParallel(m_query, JobHandle.CombineDependencies(streamConstructJh, Dependency));

                var payloads                 = new NativeList<UploadPayload>(1, WorldUpdateAllocator);
                var requiredUploadBufferSize = new NativeReference<uint>(WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
                Dependency                   = new MapPayloadsToUploadBufferJob
                {
                    streamReader             = stream.AsReader(),
                    payloads                 = payloads,
                    requiredUploadBufferSize = requiredUploadBufferSize
                }.Schedule(collectJh);

                // Fetching this now because culling jobs are still running (hopefully).
                var graphicsPool = worldBlackboardEntity.GetManagedStructComponent<GraphicsBufferManager>().pool;

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

                var uploadBuffer = graphicsPool.GetMeshVerticesUploadBuffer(requiredUploadBufferSize.Value);
                var metaBuffer   = graphicsPool.GetUploadMetaBuffer((uint)payloads.Length);

                Dependency = new WriteUploadsToBuffersJob
                {
                    payloads             = payloads.AsDeferredJobArray(),
                    verticesUploadBuffer = uploadBuffer.LockBufferForWrite<DynamicMeshVertex>(0, (int)requiredUploadBufferSize.Value),
                    metaUploadBuffer     = metaBuffer.LockBufferForWrite<uint3>(0, payloads.Length)
                }.Schedule(payloads, 1, Dependency);

                yield return true;

                if (!GetPhaseActions(CullingComputeDispatchState.Dispatch, out terminate))
                    continue;

                uploadBuffer.UnlockBufferAfterWrite<DynamicMeshVertex>((int)requiredUploadBufferSize.Value);
                metaBuffer.UnlockBufferAfterWrite<uint3>(payloads.Length);

                if (terminate)
                    break;

                var persistentBuffer = graphicsPool.GetDeformBuffer(worldBlackboardEntity.GetComponentData<MaxRequiredDeformData>().maxRequiredDeformVertices);
                m_uploadShader.SetBuffer(0, _dst,  persistentBuffer);
                m_uploadShader.SetBuffer(0, _src,  uploadBuffer);
                m_uploadShader.SetBuffer(0, _meta, metaBuffer);

                for (uint dispatchesRemaining = (uint)payloads.Length, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_uploadShader.SetInt(_startOffset, (int)offset);
                    m_uploadShader.Dispatch(0, (int)dispatchCount, 1, 1);
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
            public void* ptr;
            public uint  length;
            public uint  persistentBufferStart;
            public uint  uploadBufferStart;
        }

        [BurstCompile]
        struct GatherUploadOperationsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>              perCameraMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>               perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<DynamicMeshState>                       stateHandle;
            [ReadOnly] public BufferTypeHandle<DynamicMeshVertex>                         verticesHandle;
            [ReadOnly] public ComponentTypeHandle<BoundMesh>                              blobHandle;
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
                var cameraMask = chunk.GetChunkComponentData(ref perCameraMaskHandle);
                var frameMask  = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower      = cameraMask.lower.Value & (~frameMask.lower.Value);
                var upper      = cameraMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return;

                streamWriter.BeginForEachIndex(unfilteredChunkIndex);
                var states                     = chunk.GetNativeArray(ref stateHandle);
                var verticesBuffers            = chunk.GetBufferAccessor(ref verticesHandle);
                var blobs                      = chunk.GetNativeArray(ref blobHandle);
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
                    var state            = states[i].state;
                    var mask             = state & DynamicMeshState.Flags.RotationMask;
                    var currentRotation  = DynamicMeshState.CurrentFromMask[(byte)mask];
                    var previousRotation = DynamicMeshState.PreviousFromMask[(byte)mask];
                    currentRotation      = (state & DynamicMeshState.Flags.IsDirty) == DynamicMeshState.Flags.IsDirty ? currentRotation : previousRotation;
                    var twoAgoRotation   = DynamicMeshState.TwoAgoFromMask[(byte)mask];
                    var buffer           = verticesBuffers[i];
                    var verticesCount    = blobs[i].meshBlob.Value.undeformedVertices.Length;

                    void* currentPtr, previousPtr, twoAgoPtr;

                    if (Unity.Burst.CompilerServices.Hint.Unlikely(verticesCount * 3 != buffer.Length))
                    {
                        currentPtr  = blobs[i].meshBlob.Value.undeformedVertices.GetUnsafePtr();
                        previousPtr = currentPtr;
                        twoAgoPtr   = currentPtr;

                        UnityEngine.Debug.LogError(
                            $"Entity {entities[i]} has the wrong number of vertices ({buffer.Length / 3} vs expected {verticesCount}) in DynamicBuffer<DynamicMeshVertex>. Uploading default mesh instead.");
                    }
                    else
                    {
                        currentPtr  = buffer.AsNativeArray().GetSubArray(verticesCount * currentRotation, verticesCount).GetUnsafeReadOnlyPtr();
                        previousPtr = buffer.AsNativeArray().GetSubArray(verticesCount * previousRotation, verticesCount).GetUnsafeReadOnlyPtr();
                        twoAgoPtr   = buffer.AsNativeArray().GetSubArray(verticesCount * twoAgoRotation, verticesCount).GetUnsafeReadOnlyPtr();
                    }

                    if (needsCurrent)
                    {
                        uint gpuTarget;
                        if ((classification & DeformClassification.CurrentDeform) != DeformClassification.None)
                            gpuTarget = currentShaderIndices[i].firstVertexIndex;
                        else if ((classification & DeformClassification.LegacyCompute) != DeformClassification.None)
                            gpuTarget = legacyComputeShaderIndices[i].firstVertexIndex;
                        else
                            gpuTarget = legacyDotsShaderIndices[i].parameters.x;
                        streamWriter.Write(new UploadPayload
                        {
                            ptr                   = currentPtr,
                            length                = (uint)verticesCount,
                            persistentBufferStart = gpuTarget
                        });
                    }
                    if (needsPrevious)
                    {
                        uint gpuTarget;
                        if ((classification & DeformClassification.PreviousDeform) != DeformClassification.None)
                            gpuTarget = previousShaderIndices[i].firstVertexIndex;
                        else
                            gpuTarget = legacyDotsShaderIndices[i].parameters.y;
                        streamWriter.Write(new UploadPayload
                        {
                            ptr                   = previousPtr,
                            length                = (uint)verticesCount,
                            persistentBufferStart = gpuTarget
                        });
                    }
                    if (needsTwoAgo)
                    {
                        streamWriter.Write(new UploadPayload
                        {
                            ptr                   = twoAgoPtr,
                            length                = (uint)verticesCount,
                            persistentBufferStart = twoAgoShaderIndices[i].firstVertexIndex
                        });
                    }
                }

                streamWriter.EndForEachIndex();
            }
        }

        [BurstCompile]
        struct MapPayloadsToUploadBufferJob : IJob
        {
            [ReadOnly] public NativeStream.Reader streamReader;
            public NativeList<UploadPayload>      payloads;
            public NativeReference<uint>          requiredUploadBufferSize;

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
                        var payload                = streamReader.Read<UploadPayload>();
                        payload.uploadBufferStart  = prefixSum;
                        prefixSum                 += payload.length;
                        payloads.AddNoResize(payload);
                    }
                }

                requiredUploadBufferSize.Value = prefixSum;
            }
        }

        [BurstCompile]
        struct WriteUploadsToBuffersJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<UploadPayload>                                payloads;
            public NativeArray<uint3>                                                   metaUploadBuffer;
            [NativeDisableParallelForRestriction] public NativeArray<DynamicMeshVertex> verticesUploadBuffer;

            public unsafe void Execute(int index)
            {
                var payload             = payloads[index];
                metaUploadBuffer[index] = new uint3(payload.uploadBufferStart, payload.persistentBufferStart, payload.length);
                var dstPtr              = verticesUploadBuffer.GetSubArray((int)payload.uploadBufferStart, (int)payload.length).GetUnsafePtr();
                UnsafeUtility.MemCpy(dstPtr, payload.ptr, sizeof(DynamicMeshVertex) * payload.length);
            }
        }
    }
}

