using Latios.Calligraphics.HarfBuzz;
using Latios.Kinemation;
using static Unity.Entities.SystemAPI;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Calligraphics.Systems
{
    // Todo: Unity is really unstable with RenderTextures, and using UnityObjectRef with them seems to be especially flaky.
    // So this system remains managed for now.
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(Kinemation.Systems.DispatchRoundRobinEarlyExtensionsSuperSystem))]
    public unsafe partial class DispatchGlyphsSystem : SubSystem, ICullingComputeDispatchSystem<DispatchGlyphsSystem.CollectState, DispatchGlyphsSystem.WriteState>
    {
        const int kTextureDimension = 4096;
        const int kShelfAlignment   = 16;

        // Todo: Figure out if there are any platform differences to compensate for.
        static readonly bool kComputePixelUploadFlipY = false;

        static GraphicsBufferBroker.StaticID sGlyphBufferID = GraphicsBufferBroker.ReservePersistentBuffer();
        static GraphicsBufferBroker.StaticID sGlyphUploadID = GraphicsBufferBroker.ReserveUploadPool();
        static GraphicsBufferBroker.StaticID sPixelUploadID = GraphicsBufferBroker.ReserveUploadPool();

        EntityQuery                                          m_query;
        CullingComputeDispatchData<CollectState, WriteState> m_data;

        UnityObjectRef<ComputeShader> m_uploadGlyphsShader;
        UnityObjectRef<ComputeShader> m_copyBytesShader;
        UnityObjectRef<ComputeShader> m_uploadPixelsShader;

        GraphicsBufferBroker.StaticID m_glyphBufferID;
        GraphicsBufferBroker.StaticID m_glyphUploadID;
        GraphicsBufferBroker.StaticID m_pixelUploadID;

        TextureAtlasArray<byte>    m_sdf8Array;
        TextureAtlasArray<ushort>  m_sdf16Array;
        TextureAtlasArray<Color32> m_bitmapArray;

        DrawDelegates  m_drawDelegates;
        PaintDelegates m_paintDelegates;

        // Shader bindings
        int _src;
        int _dst;
        int _startOffset;
        int _meta;
        int _flipOffset;

        int _tmdSdf8;
        int _tmdSdf16;
        int _tmdBitmap;
        int _tmdGlyphs;

        // Prevent multiple updates per frame
        uint lastLatiosEntitiesGraphicsVersion;

        protected override void OnCreate()
        {
            ref var state = ref CheckedStateRef;

            m_query = QueryBuilder().WithAll<MaterialMeshInfo>().WithAllRW<GpuState>().WithPresent<PreviousRenderGlyph>().WithPresentRW<ResidentRange>().Build();

            var broker = worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();
            m_data     = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorldUnmanaged);

            m_uploadGlyphsShader = Resources.Load<ComputeShader>("UploadGlyphs");
            m_copyBytesShader    = Resources.Load<ComputeShader>("CopyBytes");
            m_uploadPixelsShader = Resources.Load<ComputeShader>("UploadPixels");
            m_pixelUploadID      = sPixelUploadID;
            broker.InitializeUploadPool(m_pixelUploadID, 4, GraphicsBuffer.Target.Raw);

            m_glyphBufferID = sGlyphBufferID;
            broker.InitializePersistentBuffer(m_glyphBufferID, 1024 * 16 * 128, 4, GraphicsBuffer.Target.Raw, m_copyBytesShader);
            m_glyphUploadID = sGlyphUploadID;
            broker.InitializeUploadPool(m_glyphUploadID, 4, GraphicsBuffer.Target.Raw);

            _src         = Shader.PropertyToID("_src");
            _dst         = Shader.PropertyToID("_dst");
            _startOffset = Shader.PropertyToID("_startOffset");
            _meta        = Shader.PropertyToID("_meta");
            _flipOffset  = Shader.PropertyToID("_flipOffset");

            _tmdSdf8        = Shader.PropertyToID("_tmdSdf8");
            _tmdSdf16       = Shader.PropertyToID("_tmdSdf16");
            _tmdBitmap      = Shader.PropertyToID("_tmdBitmap");
            _tmdGlyphs      = Shader.PropertyToID("_tmdGlyphs");
            var dummyBuffer = broker.GetPersistentBufferNoResize(m_glyphBufferID);
            GraphicsUnmanaged.SetGlobalBuffer(_tmdGlyphs, dummyBuffer);  // fix unbound _tmdGlyphs buffer issue

            var initialAtlasArraySize = 1;  // RenderTexture supports array size 1
            m_sdf8Array               = new TextureAtlasArray<byte>(_tmdSdf8, kTextureDimension, initialAtlasArraySize, RenderTextureFormat.R8, false, true);
            m_sdf16Array              = new TextureAtlasArray<ushort>(_tmdSdf16, kTextureDimension, initialAtlasArraySize, RenderTextureFormat.R16, false, true);
            m_bitmapArray             = new TextureAtlasArray<Color32>(_tmdBitmap, kTextureDimension, initialAtlasArraySize, RenderTextureFormat.ARGB32, true, false);  // Shader APIs will swizzle ARGB for us

            m_drawDelegates  = new DrawDelegates(true);
            m_paintDelegates = new PaintDelegates(true);

            var atlas = new AtlasTable(Allocator.Persistent, kTextureDimension, kShelfAlignment);
            worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(atlas);
            var glyphGpuTable = new GlyphGpuTable
            {
                bufferSize          = new NativeReference<uint>(Allocator.Persistent, NativeArrayOptions.ClearMemory),
                dispatchDynamicGaps = new NativeList<uint2>(Allocator.Persistent),
                residentGaps        = new NativeList<uint2>(Allocator.Persistent)
            };
            worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(glyphGpuTable);
        }

        protected override void OnUpdate()
        {
            var dispatchData = worldBlackboardEntity.GetComponentData<DispatchContext>();
            if (dispatchData.isCustomGraphicsDispatch)
            {
                var features = worldBlackboardEntity.GetComponentData<EnableUpdatingInCustomGraphics>();
                if (!features.text)
                    return;
            }
            if (dispatchData.globalSystemVersionOfLatiosEntitiesGraphics == lastLatiosEntitiesGraphicsVersion)
                return;
            lastLatiosEntitiesGraphicsVersion = dispatchData.globalSystemVersionOfLatiosEntitiesGraphics;
            ref var state                     = ref CheckedStateRef;
            m_data.DoUpdateManaged(ref state, this);
        }

        protected override void OnDestroy()
        {
            ref var state = ref CheckedStateRef;

            GraphicsBuffer b = null;
            Shader.SetGlobalBuffer(_tmdGlyphs, b);
            Texture2DArray t = null;
            Shader.SetGlobalTexture(_tmdSdf8,   t);
            Shader.SetGlobalTexture(_tmdSdf16,  t);
            Shader.SetGlobalTexture(_tmdBitmap, t);

            m_sdf8Array.Dispose();
            m_sdf16Array.Dispose();
            m_bitmapArray.Dispose();

            m_drawDelegates.Dispose();
            m_paintDelegates.Dispose();
        }

        public CollectState Collect(ref SystemState state)
        {
            var glyphTable    = worldBlackboardEntity.GetCollectionComponent<GlyphTable>(false);
            var glyphGpuTable = worldBlackboardEntity.GetCollectionComponent<GlyphGpuTable>(false);
            var atlasTable    = worldBlackboardEntity.GetCollectionComponent<AtlasTable>(false);

            var glyphEntryIDsToRasterizeSet = new NativeParallelHashSet<uint>(1, state.WorldUpdateAllocator);
            var allocateJh                  = new AllocateJob
            {
                glyphTable                  = glyphTable,
                glyphEntryIDsToRasterizeSet = glyphEntryIDsToRasterizeSet,
            }.Schedule(state.Dependency);

            var chunkCount                = m_query.CalculateChunkCountWithoutFiltering();
            var renderGlyphCapturesStream = new NativeStream(chunkCount, state.WorldUpdateAllocator);
            var captureJh                 = new CaptureRenderGlyphsJob
            {
                chunkMaterialMaskHandle     = SystemAPI.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                glyphEntryIDsToRasterizeSet = glyphEntryIDsToRasterizeSet.AsParallelWriter(),
                glyphTable                  = glyphTable,
                gpuStateHandle              = SystemAPI.GetComponentTypeHandle<GpuState>(false),
                materialLookup              = SystemAPI.GetBufferLookup<MaterialPropertyComponentType>(true),
                renderGlyphCapturesStream   = renderGlyphCapturesStream.AsWriter(),
                renderGlyphHandle           = SystemAPI.GetBufferTypeHandle<PreviousRenderGlyph>(true),
                residentRangeHandle         = SystemAPI.GetComponentTypeHandle<ResidentRange>(false),
                textShaderIndexHandle       = SystemAPI.GetComponentTypeHandle<TextShaderIndex>(false),
                worldBlackboardEntity       = worldBlackboardEntity,
            }.ScheduleParallel(m_query, allocateJh);

            var captures = new NativeList<RenderGlyphCapture>(state.WorldUpdateAllocator);
            var assignJh = new AssignShaderIndicesJob
            {
                captures                  = captures,
                glyphGpuTable             = glyphGpuTable,
                renderGlyphCapturesStream = renderGlyphCapturesStream
            }.Schedule(captureJh);

            var glyphEntryIDsToRasterize  = new NativeList<uint>(state.WorldUpdateAllocator);
            var atlasDirtyIDs             = new NativeList<uint>(state.WorldUpdateAllocator);
            var pixelUploadOffsetsInBytes = new NativeList<int>(state.WorldUpdateAllocator);
            var pixelBytesCount           = new NativeReference<int>(state.WorldUpdateAllocator);
            var atlasJh                   = new AllocateGlyphsInAtlasJob
            {
                atlasDirtyIDs               = atlasDirtyIDs,
                atlasTable                  = atlasTable,
                glyphEntryIDsToRasterize    = glyphEntryIDsToRasterize,
                glyphEntryIDsToRasterizeSet = glyphEntryIDsToRasterizeSet,
                glyphTable                  = glyphTable,
                pixelUploadOffsetsInBytes   = pixelUploadOffsetsInBytes,
                pixelBytesCount             = pixelBytesCount,
                enableAtlasGC               = true
            }.Schedule(captureJh);

            state.Dependency = JobHandle.CombineDependencies(assignJh, atlasJh);

            return new CollectState
            {
                atlasDirtyIDs             = atlasDirtyIDs,
                glyphEntryIDsToRasterize  = glyphEntryIDsToRasterize,
                glyphsToUpload            = captures,
                pixelUploadOffsetsInBytes = pixelUploadOffsetsInBytes,
                pixelBytesCount           = pixelBytesCount,
            };
        }

        public WriteState Write(ref SystemState state, ref CollectState collected)
        {
            WriteState writeState = default;

            if (collected.glyphsToUpload.IsEmpty && collected.glyphEntryIDsToRasterize.IsEmpty)
                return writeState;

            var glyphTable    = worldBlackboardEntity.GetCollectionComponent<GlyphTable>(true);
            var fontTable     = worldBlackboardEntity.GetCollectionComponent<FontTable>(true);
            var broker        = worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();
            writeState.broker = broker;

            var rasterizeJh    = state.Dependency;
            var uploadGlyphsJh = rasterizeJh;

            if (!collected.glyphEntryIDsToRasterize.IsEmpty)
            {
                int dirtySdf8Count;
                for (dirtySdf8Count = 0; dirtySdf8Count < collected.atlasDirtyIDs.Length; dirtySdf8Count++)
                {
                    var dirtyId = collected.atlasDirtyIDs[dirtySdf8Count];
                    if (dirtyId >= 0x40000000u)
                        break;
                }
                int dirtySdf16Count;
                for (dirtySdf16Count = dirtySdf8Count; dirtySdf16Count < collected.atlasDirtyIDs.Length; dirtySdf16Count++)
                {
                    var dirtyId = collected.atlasDirtyIDs[dirtySdf16Count];
                    if (dirtyId >= 0x80000000u)
                        break;
                }
                dirtySdf16Count      -= dirtySdf8Count;
                var dirtyBitmapCount  = collected.atlasDirtyIDs.Length - dirtySdf8Count - dirtySdf16Count;

                if (dirtySdf8Count > 0)
                {
                    m_sdf8Array.ReportDirtyIndices(collected.atlasDirtyIDs.AsArray().GetSubArray(0, dirtySdf8Count).AsSpan());
                    writeState.isSdf8Dirty = true;
                }
                if (dirtySdf16Count > 0)
                {
                    m_sdf16Array.ReportDirtyIndices(collected.atlasDirtyIDs.AsArray().GetSubArray(dirtySdf8Count, dirtySdf16Count).AsSpan());
                    writeState.isSdf16Dirty = true;
                }
                if (dirtyBitmapCount > 0)
                {
                    m_bitmapArray.ReportDirtyIndices(collected.atlasDirtyIDs.AsArray().GetSubArray(dirtySdf8Count + dirtySdf16Count, dirtyBitmapCount).AsSpan());
                    writeState.isBitmapDirty = true;
                }

                var uploadBuffer     = broker.GetUploadBuffer(m_pixelUploadID, (uint)collected.pixelBytesCount.Value / 4);
                var uploadArray      = uploadBuffer.LockBufferForWrite<byte>(0, collected.pixelBytesCount.Value);
                var uploadMetaBuffer = broker.GetMetaUint4UploadBuffer((uint)collected.glyphEntryIDsToRasterize.Length);
                var uploadMetaArray  = uploadMetaBuffer.LockBufferForWrite<uint4>(0, collected.glyphEntryIDsToRasterize.Length);

                rasterizeJh = new RasterizeJob
                {
                    drawDelegates             = m_drawDelegates,
                    fontTable                 = fontTable,
                    glyphEntryIDsToRasterize  = collected.glyphEntryIDsToRasterize.AsArray(),
                    glyphTable                = glyphTable,
                    paintDelegates            = m_paintDelegates,
                    pixelUploadOffsetsInBytes = collected.pixelUploadOffsetsInBytes.AsArray(),
                    uploadBuffer              = uploadArray,
                    uploadMetaBuffer          = uploadMetaArray,
                    atomicPrioritizer         = new NativeReference<int>(0, state.WorldUpdateAllocator),
                }.ScheduleParallel(collected.glyphEntryIDsToRasterize.Length, 1, rasterizeJh);

                writeState.pixelUploadBuffer               = uploadBuffer;
                writeState.pixelUploadBufferWriteCount     = collected.pixelBytesCount.Value;
                writeState.pixelUploadMetaBuffer           = uploadMetaBuffer;
                writeState.pixelUploadMetaBufferWriteCount = collected.glyphEntryIDsToRasterize.Length;
            }
            if (!collected.glyphsToUpload.IsEmpty)
            {
                var lastCapture      = collected.glyphsToUpload[^ 1];
                var glyphCount       = lastCapture.writeStart + lastCapture.glyphCount;
                var uploadBuffer     = broker.GetUploadBuffer(m_glyphUploadID, (uint)(glyphCount * UnsafeUtility.SizeOf<RenderGlyph>() / 4));
                var uploadArray      = uploadBuffer.LockBufferForWrite<RenderGlyph>(0, glyphCount);
                var captureCount     = collected.glyphsToUpload.Length;
                var uploadMetaBuffer = broker.GetMetaUint3UploadBuffer((uint)captureCount);
                var uploadMetaArray  = uploadMetaBuffer.LockBufferForWrite<uint3>(0, captureCount);

                uploadGlyphsJh = new WriteRenderGlyphsToGpuJob
                {
                    captures        = collected.glyphsToUpload.AsArray(),
                    uploadArray     = uploadArray,
                    uploadMetaArray = uploadMetaArray,
                    glyphTable      = glyphTable
                }.ScheduleParallel(collected.glyphsToUpload.Length, 8, uploadGlyphsJh);

                writeState.glyphUploadBuffer               = uploadBuffer;
                writeState.glyphUploadBufferWriteCount     = glyphCount;
                writeState.glyphUploadMetaBuffer           = uploadMetaBuffer;
                writeState.glyphUploadMetaBufferWriteCount = captureCount;
            }

            state.Dependency = JobHandle.CombineDependencies(rasterizeJh, uploadGlyphsJh);
            return writeState;
        }

        public void Dispatch(ref SystemState state, ref WriteState written)
        {
            if (written.isSdf8Dirty || written.isSdf16Dirty || written.isBitmapDirty)
            {
                written.pixelUploadBuffer.UnlockBufferAfterWrite<byte>(written.pixelUploadBufferWriteCount);
                written.pixelUploadMetaBuffer.UnlockBufferAfterWrite<uint4>(written.pixelUploadMetaBufferWriteCount);

                var shader = m_uploadPixelsShader.Value;
                shader.SetTexture(0, _tmdSdf8,   m_sdf8Array.GetRenderTextureForUpload());
                shader.SetTexture(0, _tmdSdf16,  m_sdf16Array.GetRenderTextureForUpload());
                shader.SetTexture(0, _tmdBitmap, m_bitmapArray.GetRenderTextureForUpload());
                m_uploadPixelsShader.SetBuffer(0, _src,  written.pixelUploadBuffer);
                m_uploadPixelsShader.SetBuffer(0, _meta, written.pixelUploadMetaBuffer);
                shader.SetInt(_flipOffset, math.select(0, kTextureDimension - 1, kComputePixelUploadFlipY));
                for (uint dispatchesRemaining = (uint)written.pixelUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    shader.SetInt(_startOffset, (int)offset);
                    shader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }
            }

            if (written.isSdf8Dirty)
                m_sdf8Array.ApplyChanges();
            if (written.isSdf16Dirty)
                m_sdf16Array.ApplyChanges();
            if (written.isBitmapDirty)
                m_bitmapArray.ApplyChanges();

            if (written.glyphUploadBufferWriteCount > 0)
            {
                var glyphGpuTable = worldBlackboardEntity.GetCollectionComponent<GlyphGpuTable>(true);

                written.glyphUploadMetaBuffer.UnlockBufferAfterWrite<uint3>(written.glyphUploadMetaBufferWriteCount);
                written.glyphUploadBuffer.UnlockBufferAfterWrite<RenderGlyph>(written.glyphUploadBufferWriteCount);

                var persistentBuffer = written.broker.GetPersistentBuffer(m_glyphBufferID, glyphGpuTable.bufferSize.Value);
                m_uploadGlyphsShader.SetBuffer(0, _dst,  persistentBuffer);
                m_uploadGlyphsShader.SetBuffer(0, _src,  written.glyphUploadBuffer);
                m_uploadGlyphsShader.SetBuffer(0, _meta, written.glyphUploadMetaBuffer);

                for (uint dispatchesRemaining = (uint)written.glyphUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_uploadGlyphsShader.SetInt(_startOffset, (int)offset);
                    m_uploadGlyphsShader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }

                GraphicsUnmanaged.SetGlobalBuffer(_tmdGlyphs, persistentBuffer);
            }
        }

        public struct CollectState
        {
            internal NativeList<RenderGlyphCapture> glyphsToUpload;
            internal NativeList<uint>               glyphEntryIDsToRasterize;
            internal NativeList<uint>               atlasDirtyIDs;
            internal NativeList<int>                pixelUploadOffsetsInBytes;
            internal NativeReference<int>           pixelBytesCount;
        }

        public struct WriteState
        {
            internal GraphicsBufferBroker broker;
            internal bool                 isSdf8Dirty;
            internal bool                 isSdf16Dirty;
            internal bool                 isBitmapDirty;

            internal GraphicsBufferUnmanaged glyphUploadBuffer;
            internal GraphicsBufferUnmanaged glyphUploadMetaBuffer;
            internal int                     glyphUploadBufferWriteCount;
            internal int                     glyphUploadMetaBufferWriteCount;

            internal GraphicsBufferUnmanaged pixelUploadBuffer;
            internal GraphicsBufferUnmanaged pixelUploadMetaBuffer;
            internal int                     pixelUploadBufferWriteCount;
            internal int                     pixelUploadMetaBufferWriteCount;
        }

        internal struct RenderGlyphCapture
        {
            public TextShaderIndex* textShaderIndexPtr;
            public ResidentRange*   residentRangePtr;
            public RenderGlyph*     glyphBuffer;
            public int              glyphCount;
            public bool             makeResident;
            public int              writeStart;
            public int              gpuStart;
        }
    }
}

