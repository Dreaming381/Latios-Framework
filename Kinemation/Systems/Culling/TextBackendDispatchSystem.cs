using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

// Todo: This system needs to be reworked for more optimal scheduling.

namespace Latios.Kinemation.TextBackend.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class TextBackendDispatchSystem : CullingComputeDispatchSubSystemBase
    {
        ComputeShader m_uploadShader;

        EntityQuery m_query;

        // Shader bindings
        int _src;
        int _dst;
        int _startOffset;
        int _meta;

        int _latiosTextBuffer;

        protected override void OnCreate()
        {
            m_query = Fluent.With<RenderGlyph>(true).With<TextRenderControl>(true).With<RenderBounds>(true)
                      .With<ChunkPerCameraCullingMask>(false, true).With<ChunkPerFrameCullingMask>(true, true).Build();

            m_uploadShader    = Resources.Load<ComputeShader>("UploadGlyphs");
            _src              = Shader.PropertyToID("_src");
            _dst              = Shader.PropertyToID("_dst");
            _startOffset      = Shader.PropertyToID("_startOffset");
            _meta             = Shader.PropertyToID("_meta");
            _latiosTextBuffer = Shader.PropertyToID("_latiosTextBuffer");
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

                int textIndex = worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>()
                                .AsNativeArray().IndexOf(ComponentType.ReadOnly<TextShaderIndex>());
                ulong textMaterialMaskLower = (ulong)textIndex >= 64UL ? 0UL : (1UL << textIndex);
                ulong textMaterialMaskUpper = (ulong)textIndex >= 64UL ? (1UL << (textIndex - 64)) : 0UL;

                var streamCount       = CollectionHelper.CreateNativeArray<int>(1, WorldUpdateAllocator);
                streamCount[0]        = m_query.CalculateChunkCountWithoutFiltering();
                var streamConstructJh = NativeStream.ScheduleConstruct(out var stream, streamCount, default, WorldUpdateAllocator);
                var collectJh         = new GatherUploadOperationsJob
                {
                    glyphCountThisFrameLookup       = SystemAPI.GetComponentLookup<GlyphCountThisFrame>(false),
                    glyphCountThisPass              = 0,
                    glyphsHandle                    = SystemAPI.GetBufferTypeHandle<RenderGlyph>(true),
                    materialMaskLower               = textMaterialMaskLower,
                    materialMaskUpper               = textMaterialMaskUpper,
                    materialPropertyDirtyMaskHandle = SystemAPI.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                    perCameraMaskHandle             = SystemAPI.GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                    perFrameMaskHandle              = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                    streamWriter                    = stream.AsWriter(),
                    textShaderIndexHandle           = SystemAPI.GetComponentTypeHandle<TextShaderIndex>(false),
                    trcHandle                       = SystemAPI.GetComponentTypeHandle<TextRenderControl>(true),
                    worldBlackboardEntity           = worldBlackboardEntity
                }.Schedule(m_query, JobHandle.CombineDependencies(streamConstructJh, Dependency));

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

                var uploadBuffer = graphicsPool.GetGlyphsUploadBuffer(requiredUploadBufferSize.Value);
                var metaBuffer   = graphicsPool.GetDispatchMetaBuffer((uint)payloads.Length);

                Dependency = new WriteUploadsToBuffersJob
                {
                    payloads           = payloads.AsDeferredJobArray(),
                    glyphsUploadBuffer = uploadBuffer.LockBufferForWrite<RenderGlyph>(0, (int)requiredUploadBufferSize.Value),
                    metaUploadBuffer   = metaBuffer.LockBufferForWrite<uint4>(0, payloads.Length)
                }.Schedule(payloads, 1, Dependency);

                yield return true;

                if (!GetPhaseActions(CullingComputeDispatchState.Dispatch, out terminate))
                    continue;

                uploadBuffer.UnlockBufferAfterWrite<RenderGlyph>((int)requiredUploadBufferSize.Value);
                metaBuffer.UnlockBufferAfterWrite<uint4>(payloads.Length);

                if (terminate)
                    break;

                var persistentBuffer = graphicsPool.GetGlyphsBuffer(worldBlackboardEntity.GetComponentData<GlyphCountThisFrame>().glyphCount);
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

                Shader.SetGlobalBuffer(_latiosTextBuffer, persistentBuffer);

                yield return true;
            }
        }

        unsafe struct UploadPayload
        {
            public void* ptr;
            public uint  length;
            public uint  persistentBufferStart;
            public uint  uploadBufferStart;
            public uint  controls;
        }

        // Schedule Single
        [BurstCompile]
        struct GatherUploadOperationsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask> perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<TextRenderControl>        trcHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyph>                 glyphsHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingMask>           perCameraMaskHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>      materialPropertyDirtyMaskHandle;
            public ComponentTypeHandle<TextShaderIndex>                     textShaderIndexHandle;
            public ComponentLookup<GlyphCountThisFrame>                     glyphCountThisFrameLookup;
            public Entity                                                   worldBlackboardEntity;

            public uint glyphCountThisPass;

            public ulong materialMaskLower;
            public ulong materialMaskUpper;

            [NativeDisableParallelForRestriction] public NativeStream.Writer streamWriter;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var cameraMask = ref chunk.GetChunkComponentRefRW(ref perCameraMaskHandle);
                var     frameMask  = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var     lower      = cameraMask.lower.Value & (~frameMask.lower.Value);
                var     upper      = cameraMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return;

                ref var dirtyMask      = ref chunk.GetChunkComponentRefRW(ref materialPropertyDirtyMaskHandle);
                dirtyMask.lower.Value |= materialMaskLower;
                dirtyMask.upper.Value |= materialMaskUpper;

                ref var glyphCountThisFrame = ref glyphCountThisFrameLookup.GetRefRW(worldBlackboardEntity).ValueRW.glyphCount;

                streamWriter.BeginForEachIndex(unfilteredChunkIndex);
                var trcs          = chunk.GetNativeArray(ref trcHandle);
                var glyphsBuffers = chunk.GetBufferAccessor(ref glyphsHandle);
                var shaderIndices = chunk.GetNativeArray(ref textShaderIndexHandle);

                var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var trc          = trcs[i].flags;
                    var buffer       = glyphsBuffers[i];
                    shaderIndices[i] = new TextShaderIndex
                    {
                        firstGlyphIndex = glyphCountThisFrame,
                        glyphCount      = (uint)buffer.Length
                    };

                    if (buffer.Length == 0)
                    {
                        ref var bitHolder = ref i >= 64 ? ref cameraMask.upper : ref cameraMask.lower;
                        bitHolder.SetBits(i % 64, false);
                    }

                    streamWriter.Write(new UploadPayload
                    {
                        ptr                   = buffer.GetUnsafeReadOnlyPtr(),
                        length                = (uint)buffer.Length,
                        uploadBufferStart     = glyphCountThisPass,
                        persistentBufferStart = glyphCountThisFrame,
                        controls              = (uint)trc
                    });
                    glyphCountThisPass  += (uint)buffer.Length;
                    glyphCountThisFrame += (uint)buffer.Length;
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
                uint sum          = 0;  // Prefixing is done in previous job.

                for (int streamIndex = 0; streamIndex < streamCount; streamIndex++)
                {
                    var count = streamReader.BeginForEachIndex(streamIndex);
                    for (int i = 0; i < count; i++)
                    {
                        var payload  = streamReader.Read<UploadPayload>();
                        sum         += payload.length;
                        payloads.AddNoResize(payload);
                    }
                }

                requiredUploadBufferSize.Value = sum;
            }
        }

        [BurstCompile]
        struct WriteUploadsToBuffersJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<UploadPayload>                          payloads;
            public NativeArray<uint4>                                             metaUploadBuffer;
            [NativeDisableParallelForRestriction] public NativeArray<RenderGlyph> glyphsUploadBuffer;

            public unsafe void Execute(int index)
            {
                var payload             = payloads[index];
                metaUploadBuffer[index] = new uint4(payload.uploadBufferStart, payload.persistentBufferStart, payload.length, payload.controls);
                var dstPtr              = glyphsUploadBuffer.GetSubArray((int)payload.uploadBufferStart, (int)payload.length).GetUnsafePtr();
                UnsafeUtility.MemCpy(dstPtr, payload.ptr, sizeof(RenderGlyph) * payload.length);
            }
        }
    }
}

