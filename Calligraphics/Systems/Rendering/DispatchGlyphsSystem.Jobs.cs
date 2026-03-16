using System.Collections.Generic;
using System.Threading;
using Latios.Calligraphics.HarfBuzz;
using Latios.Calligraphics.HarfBuzz.Bitmap;
using Latios.Kinemation;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics.Systems
{
    public unsafe partial class DispatchGlyphsSystem
    {
        #region Collect Jobs
        [BurstCompile]
        struct AllocateJob : IJob
        {
            [ReadOnly] public GlyphTable       glyphTable;
            public NativeParallelHashSet<uint> glyphEntryIDsToRasterizeSet;

            public void Execute()
            {
                // set 3x larger than needed because of https://discussions.unity.com/t/hashmap-is-full-error-before-hashmap-is-full/809238
                glyphEntryIDsToRasterizeSet.Capacity = 3 * math.max(glyphTable.entries.Length, glyphEntryIDsToRasterizeSet.Capacity);
            }
        }

        [BurstCompile]
        struct CaptureRenderGlyphsJob : IJobChunk
        {
            [ReadOnly] public GlyphTable                                  glyphTable;
            [ReadOnly] public BufferTypeHandle<PreviousRenderGlyph>       renderGlyphHandle;
            [ReadOnly] public BufferLookup<MaterialPropertyComponentType> materialLookup;
            public ComponentTypeHandle<TextShaderIndex>                   textShaderIndexHandle;
            public ComponentTypeHandle<ResidentRange>                     residentRangeHandle;
            public ComponentTypeHandle<GpuState>                          gpuStateHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>    chunkMaterialMaskHandle;

            [NativeDisableParallelForRestriction] public NativeStream.Writer renderGlyphCapturesStream;
            public NativeParallelHashSet<uint>.ParallelWriter                glyphEntryIDsToRasterizeSet;

            public Entity worldBlackboardEntity;

            ulong materialPropertyMaskLower;
            ulong materialPropertyMaskUpper;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                renderGlyphCapturesStream.BeginForEachIndex(unfilteredChunkIndex);

                var shaderPtr    = chunk.GetComponentDataPtrRW(ref textShaderIndexHandle);
                var residentPtr  = (ResidentRange*)chunk.GetRequiredComponentDataPtrRW(ref residentRangeHandle);
                var gpuStates    = (GpuState*)chunk.GetRequiredComponentDataPtrRW(ref gpuStateHandle);
                var glyphBuffers = chunk.GetBufferAccessor(ref renderGlyphHandle);
                var gpuStateMask = chunk.GetEnabledMask(ref gpuStateHandle);

                if (shaderPtr != null)
                {
                    if (materialPropertyMaskLower == 0 && materialPropertyMaskUpper == 0)
                    {
                        var materials     = materialLookup[worldBlackboardEntity].AsNativeArray().Reinterpret<ComponentType>();
                        var materialIndex = materials.IndexOf(ComponentType.ReadOnly<TextShaderIndex>());
                        if (materialIndex < 64)
                            materialPropertyMaskLower = 1u << materialIndex;
                        else
                            materialPropertyMaskUpper = 1u << (materialIndex - 64);
                    }
                    var materialMask          = chunk.GetChunkComponentRefRW(ref chunkMaterialMaskHandle);
                    materialMask.lower.Value |= materialPropertyMaskLower;
                    materialMask.upper.Value |= materialPropertyMaskUpper;
                }

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var entityIndex))
                {
                    gpuStateMask[entityIndex] = false;
                    bool resident             = gpuStates[entityIndex].state == GpuState.State.DynamicPromoteToResident ||
                                                gpuStates[entityIndex].state == GpuState.State.ResidentUncommitted;
                    gpuStates[entityIndex].state = resident ? GpuState.State.Resident : GpuState.State.Dynamic;
                    var glyphs                   = glyphBuffers[entityIndex];
                    renderGlyphCapturesStream.Write(new RenderGlyphCapture
                    {
                        glyphBuffer        = glyphs.Length != 0 ? (RenderGlyph*)glyphs.GetUnsafeReadOnlyPtr() : null,
                        glyphCount         = glyphs.Length,
                        makeResident       = resident,
                        residentRangePtr   = residentPtr + entityIndex,
                        textShaderIndexPtr = shaderPtr != null ? shaderPtr + entityIndex : null,
                    });
                    foreach (var glyph in glyphs)
                    {
                        var entry = glyphTable.GetEntry(glyph.glyph.glyphEntryId);
                        if (!entry.isInAtlas)
                            glyphEntryIDsToRasterizeSet.Add(glyph.glyph.glyphEntryId);
                    }
                }

                renderGlyphCapturesStream.EndForEachIndex();
            }
        }

        [BurstCompile]
        struct AssignShaderIndicesJob : IJob
        {
            [ReadOnly] public NativeStream        renderGlyphCapturesStream;
            public NativeList<RenderGlyphCapture> captures;
            public GlyphGpuTable                  glyphGpuTable;

            public void Execute()
            {
                int captureCount  = renderGlyphCapturesStream.Count();
                captures.Capacity = captureCount;

                int writeBufferOffset  = 0;
                int dynamicCount       = 0;
                var residentBufferSize = glyphGpuTable.bufferSize.Value;

                for (int stream = 0; stream < renderGlyphCapturesStream.ForEachCount; stream++)
                {
                    var reader = renderGlyphCapturesStream.AsReader();
                    for (int i = reader.BeginForEachIndex(stream); i > 0; i--)
                    {
                        var capture         = reader.Read<RenderGlyphCapture>();
                        capture.writeStart  = writeBufferOffset;
                        writeBufferOffset  += capture.glyphCount;
                        if (capture.makeResident)
                        {
                            if (capture.residentRangePtr->count != capture.glyphCount)
                            {
                                GapAllocator.TryAllocate(glyphGpuTable.residentGaps, (uint)capture.glyphCount, ref residentBufferSize, out var newLocation);
                                capture.gpuStart = (int)newLocation;
                                if (capture.textShaderIndexPtr != null)
                                {
                                    capture.textShaderIndexPtr->firstGlyphIndex = newLocation;
                                    capture.textShaderIndexPtr->glyphCount      = (uint)capture.glyphCount;
                                }
                                capture.residentRangePtr->start = newLocation;
                                capture.residentRangePtr->count = (uint)capture.glyphCount;
                                //UnityEngine.Debug.Log($"Allocated resident range: {capture.residentRangePtr->start}, {capture.residentRangePtr->count}");
                            }
                            else
                            {
                                capture.gpuStart = (int)capture.residentRangePtr->start;
                                //UnityEngine.Debug.Log($"Updated resident range: {capture.residentRangePtr->start}, {capture.residentRangePtr->count}");
                            }
                        }
                        else
                        {
                            dynamicCount += capture.glyphCount;
                        }
                        captures.AddNoResize(capture);
                    }
                    reader.EndForEachIndex();
                }
                if (dynamicCount > 0)
                {
                    GapAllocator.TryAllocate(glyphGpuTable.residentGaps, (uint)dynamicCount, ref residentBufferSize, out var dynamicStart);
                    //UnityEngine.Debug.Log($"Allocated dynamic region: {dynamicStart}, {dynamicCount}");
                    glyphGpuTable.dispatchDynamicGaps.Add(new uint2(dynamicStart, (uint)dynamicCount));
                    for (int i = 0; i < captures.Length; i++)
                    {
                        ref var capture = ref captures.ElementAt(i);
                        if (capture.makeResident)
                            continue;
                        capture.gpuStart  = (int)dynamicStart;
                        dynamicStart     += (uint)capture.glyphCount;
                        if (capture.textShaderIndexPtr != null)
                        {
                            capture.textShaderIndexPtr->firstGlyphIndex = (uint)capture.gpuStart;
                            capture.textShaderIndexPtr->glyphCount      = (uint)capture.glyphCount;
                            //UnityEngine.Debug.Log($"Allocated dynamic range: {capture.textShaderIndexPtr->firstGlyphIndex}, {capture.textShaderIndexPtr->glyphCount}");
                        }
                    }
                }

                glyphGpuTable.bufferSize.Value = residentBufferSize;

                // Remove empty buffers from upload list.
                int dstIndex = 0;
                for (int i = 0; i < captures.Length; i++)
                {
                    if (captures[i].glyphCount != 0)
                    {
                        captures[dstIndex] = captures[i];
                        dstIndex++;
                    }
                }
                captures.Length = dstIndex;
            }
        }

        [BurstCompile]
        struct AllocateGlyphsInAtlasJob : IJob
        {
            [ReadOnly] public NativeParallelHashSet<uint> glyphEntryIDsToRasterizeSet;
            public NativeList<uint>                       glyphEntryIDsToRasterize;
            public NativeList<uint>                       atlasDirtyIDs;
            public NativeList<int>                        pixelUploadOffsetsInBytes;
            public NativeReference<int>                   pixelBytesCount;
            public GlyphTable                             glyphTable;
            public AtlasTable                             atlasTable;
            public bool                                   enableAtlasGC;

            public void Execute()
            {
                var count                         = glyphEntryIDsToRasterizeSet.Count();
                glyphEntryIDsToRasterize.Capacity = count;
                foreach (var glyph in glyphEntryIDsToRasterizeSet)
                    glyphEntryIDsToRasterize.AddNoResize(glyph);
                // We sort in reverse order, because higher values of the top two bits tend to be the most expensive,
                // so we want to have those earlier in the list when we rasterize them.
                if(enableAtlasGC)
                    glyphEntryIDsToRasterize.Sort(new GlyphEntryComparer(in glyphTable));
                else
                    glyphEntryIDsToRasterize.Sort(new ReverseComparer());

                UnsafeHashSet<uint> dirtyAtlasIDSet = new UnsafeHashSet<uint>(32, Allocator.Temp);
                int                 runningOffset   = 0;

                foreach (var glyph in glyphEntryIDsToRasterize)
                {
                    ref var glyphEntry    = ref glyphTable.GetEntryRW(glyph);
                    var     doublePadding = 2 * glyphEntry.padding;
                    var     paddedWith    = glyphEntry.width + doublePadding;
                    var     paddedHeight  = glyphEntry.height + doublePadding;
                    if (enableAtlasGC)
                    {
                        if (!atlasTable.TryAllocateNoNewSlice(glyph, (short)(paddedWith), (short)(paddedHeight), out glyphEntry.x, out glyphEntry.y, out glyphEntry.z))
                        {
                            atlasTable.Free(ref glyphTable);  //dispose all glyphs with refCount 0
                            atlasTable.atlasRemovalCandidates.Clear();
                            atlasTable.Allocate(glyph, (short)(paddedWith), (short)(paddedHeight), out glyphEntry.x, out glyphEntry.y, out glyphEntry.z);
                        }
                    }
                    else
                        atlasTable.Allocate(glyph, (short)(paddedWith), (short)(paddedHeight), out glyphEntry.x, out glyphEntry.y, out glyphEntry.z);

                    //if (glyphEntry.key.format == RenderFormat.SDF8)
                    //    UnityEngine.Debug.Log($"Allocating {glyphEntry.x} {glyphEntry.y}, width {glyphEntry.width}");
                    uint id  = (uint)glyphEntry.z;
                    id      |= glyph & 0xc0000000;
                    dirtyAtlasIDSet.Add(id);
                    int pixelCount = paddedWith * paddedHeight;
                    pixelUploadOffsetsInBytes.Add(runningOffset);
                    switch (glyphEntry.key.format)
                    {
                        case RenderFormat.SDF8:
                            runningOffset += CollectionHelper.Align(pixelCount, 4);
                            break;
                        case RenderFormat.SDF16:
                            runningOffset += CollectionHelper.Align(pixelCount * 2, 4);
                            break;
                        case RenderFormat.Bitmap8888:
                            runningOffset += pixelCount * 4;
                            break;
                    }
                }
                pixelBytesCount.Value = runningOffset;

                atlasDirtyIDs.Capacity = dirtyAtlasIDSet.Count;
                foreach (var id in dirtyAtlasIDSet)
                    atlasDirtyIDs.AddNoResize(id);
                atlasDirtyIDs.Sort();
            }
        }

        struct ReverseComparer : IComparer<uint>
        {
            public int Compare(uint x, uint y) => - x.CompareTo(y);
        }
        struct GlyphEntryComparer : IComparer<uint>
        {
            private readonly GlyphTable _glyphTable;

            public GlyphEntryComparer(in GlyphTable glyphTable)
            => _glyphTable = glyphTable;

            public int Compare(uint x, uint y)
            {
                ref readonly var ex = ref _glyphTable.GetEntryRef(x);
                ref readonly var ey = ref _glyphTable.GetEntryRef(y);

                var c = ((byte)ey.key.format).CompareTo((byte)ex.key.format);  // reverse order to prioritize bitmaps first, then SDF16, then SDF8.
                if (c != 0)
                    return c;

                c = ey.height.CompareTo(ex.height);  //reverse comparison (largest height goes first)
                if (c != 0)
                    return c;

                return y.CompareTo(x);  //reverse entry index comparison
            }
        }
        #endregion

        #region Write Jobs
        [BurstCompile]
        struct RasterizeJob : IJobFor
        {
            [ReadOnly] public NativeArray<uint>                               glyphEntryIDsToRasterize;
            [ReadOnly] public GlyphTable                                      glyphTable;
            [ReadOnly] public FontTable                                       fontTable;
            [ReadOnly] public NativeArray<int>                                pixelUploadOffsetsInBytes;
            [NativeDisableParallelForRestriction] public NativeArray<byte>    uploadBuffer;
            [NativeDisableParallelForRestriction] public NativeArray<uint4>   uploadMetaBuffer;  // Disable parallel in case compute upload is disabled
            [NativeDisableParallelForRestriction] public NativeReference<int> atomicPrioritizer;

            [NativeDisableUnsafePtrRestriction] public DrawDelegates  drawDelegates;
            [NativeDisableUnsafePtrRestriction] public PaintDelegates paintDelegates;

            [NativeDisableContainerSafetyRestriction] DrawData drawData;
            [NativeSetThreadIndex] int                         threadIndex;

            static readonly Unity.Profiling.ProfilerMarker kPaintMarker = new Unity.Profiling.ProfilerMarker("Rasterize Paint");

            public unsafe void Execute(int workerIndex)
            {
                var glyphIndex = Interlocked.Increment(ref *atomicPrioritizer.GetUnsafePtr()) - 1;

                var glyphEntry = glyphTable.GetEntry(glyphEntryIDsToRasterize[glyphIndex]);

                // If the glyph doesn't have any real size, then there's nothing to rasterize.
                if (glyphEntry.width == 0 || glyphEntry.height == 0)
                {
                    uploadMetaBuffer[glyphIndex] = default;
                    return;
                }

                var face = fontTable.faces[glyphEntry.key.faceIndex];

                //Debug.Log($"Rasterize glyphIndex {glyphIndex} of font {face.GetName(NameID.FONT_FAMILY,Language.English())} {face.GetName(NameID.FONT_SUBFAMILY, Language.English())}");

                var font = fontTable.GetOrCreateFont(glyphEntry.key.faceIndex, threadIndex);
                if (face.HasVarData && font.currentVariableProfileIndex != glyphEntry.key.variableProfileIndex)
                    font = fontTable.SetVariableProfile(glyphEntry.key.faceIndex, threadIndex, glyphEntry.key.variableProfileIndex);

                var samplingSize = glyphEntry.key.GetSamplingSize();
                font.SetScale(samplingSize, samplingSize);
                // Todo: maxDeviation doesn't do anything right now, and instead the devsq < 0.333 metric is hardcoded.
                // However, for SDF rendering, signed distance value accuracy increases as the permitted deviation approaches
                // spread / (2^bitdepth). However, max accuracy is probably really slow (needs experimentation) to the point
                // where trying to evaluate the quadratics directly may be faster.
                var maxDeviation = BezierMath.GetMaxDeviation(font.GetScale().x);
                if (!drawData.edges.IsCreated)
                    drawData = new DrawData(256, 16, maxDeviation, Allocator.Temp);
                drawData.Clear();
                drawData.maxDeviation = maxDeviation;

                if (glyphEntry.key.format == RenderFormat.SDF8)
                {
                    font.DrawGlyph(glyphEntry.key.glyphIndex, drawDelegates, ref drawData);
                    var paddedAtlasRect  = glyphEntry.PaddedAtlasRect;
                    var sdf8TextureSlice = GetSdf8Upload(glyphIndex, paddedAtlasRect.width, paddedAtlasRect.height);
                    {
                        uint x                        = (uint)glyphEntry.z;
                        x                            |= ((uint)glyphEntry.key.format) << 30;
                        uint y                        = (uint)pixelUploadOffsetsInBytes[glyphIndex] / 4;
                        uint z                        = (uint)paddedAtlasRect.x;
                        z                            |= ((uint)paddedAtlasRect.y) << 16;
                        uint w                        = (uint)paddedAtlasRect.width;
                        w                            |= ((uint)paddedAtlasRect.height) << 16;
                        uploadMetaBuffer[glyphIndex]  = new uint4(x, y, z, w);
                    }

                    // remove overlaps using Clipper
                    // not needed for static postscript fonts which are not permitted to have overlaps
                    if (face.sdfOrientation == SDFOrientation.TRUETYPE || face.HasVarData)
                    {
                        // clipper always outputs polygon oriented CCW for outer contours and CW for holes,
                        // which is the same as postscript convention:
                        // Truetype: CW for outer contours, CCW for holes
                        // Postscript: CCW for outer contours, CW for holes
                        PaintUtils.removeOverlapsMarker.Begin();
                        PolygonOperation.RemoveSelfIntersections(ref drawData);
                        face.sdfOrientation = SDFOrientation.POSTSCRIPT;
                        PaintUtils.removeOverlapsMarker.End();
                    }
                    SdfRasterizer.RasterizeSdf8(drawData, sdf8TextureSlice, paddedAtlasRect, glyphEntry.padding, glyphEntry.key.GetSpread());
                }
                else if (glyphEntry.key.format == RenderFormat.SDF16)
                {
                    font.DrawGlyph(glyphEntry.key.glyphIndex, drawDelegates, ref drawData);
                    var paddedAtlasRect   = glyphEntry.PaddedAtlasRect;
                    var sdf16TextureSlice = GetSdf16Upload(glyphIndex, paddedAtlasRect.width, paddedAtlasRect.height);
                    {
                        uint x                        = (uint)glyphEntry.z;
                        x                            |= ((uint)glyphEntry.key.format) << 30;
                        uint y                        = (uint)pixelUploadOffsetsInBytes[glyphIndex] / 4;
                        uint z                        = (uint)paddedAtlasRect.x;
                        z                            |= ((uint)paddedAtlasRect.y) << 16;
                        uint w                        = (uint)paddedAtlasRect.width;
                        w                            |= ((uint)paddedAtlasRect.height) << 16;
                        uploadMetaBuffer[glyphIndex]  = new uint4(x, y, z, w);
                    }

                    // remove overlaps using Clipper
                    // not needed for static postscript fonts which are not permitted to have overlaps
                    if (face.sdfOrientation == SDFOrientation.TRUETYPE || face.HasVarData)
                    {
                        // clipper always outputs polygon oriented CCW for outer contours and CW for holes,
                        // which is the same as postscript convention:
                        // Truetype: CW for outer contours, CCW for holes
                        // Postscript: CCW for outer contours, CW for holes
                        PaintUtils.removeOverlapsMarker.Begin();
                        PolygonOperation.RemoveSelfIntersections(ref drawData);
                        face.sdfOrientation = SDFOrientation.POSTSCRIPT;
                        PaintUtils.removeOverlapsMarker.End();
                    }
                    SdfRasterizer.RasterizeSdf16(drawData, sdf16TextureSlice, paddedAtlasRect, glyphEntry.padding, glyphEntry.key.GetSpread());
                }
                else if (glyphEntry.key.format == RenderFormat.Bitmap8888)
                {
                    PaintData paintData     = default;
                    paintData.drawDelegates = drawDelegates;
                    paintData.clipGlyph     = drawData;
                    paintData.Clear();

                    // harfbuzz is not pushing clipRects anymore for bounded glyphs as of https://github.com/harfbuzz/harfbuzz/pull/5294
                    // Boundedness calculation as per https://learn.microsoft.com/en-us/typography/opentype/spec/colr#glyph-metrics-and-boundedness
                    // is not quite clear. This fix here is  assuming the bound is the clipRect of the base glyph. Need to allocate paint surface here
                    // as it is not allocated via hb_paint_funcs_set_push_clip_rectangle_func for bounded glyphs
                    paintData.clipRect = glyphEntry.ClipRect;
                    paintData.clipRect.Expand(1);  //prevents rendering artifacts that occur for outlines that strech from minX to maxX of clipRect, reason unknown
                    paintData.paintSurface = new NativeArray<ColorARGB>(paintData.clipRect.intWidth * paintData.clipRect.intHeight, Allocator.Temp);

                    kPaintMarker.Begin();
                    font.PaintGlyph(glyphEntry.key.glyphIndex, ref paintData, paintDelegates, 0, new ColorARGB(255, 0, 0, 0));
                    kPaintMarker.End();
                    if (paintData.paintSurface.Length > 0)
                    {
                        var bitmapTextureSlice = GetBitmapUpload(glyphIndex, glyphEntry.width, glyphEntry.height);
                        {
                            uint x                        = (uint)glyphEntry.z;
                            x                            |= ((uint)glyphEntry.key.format) << 30;
                            uint y                        = (uint)pixelUploadOffsetsInBytes[glyphIndex] / 4;
                            uint z                        = (uint)glyphEntry.x;
                            z                            |= ((uint)glyphEntry.y) << 16;
                            uint w                        = (uint)glyphEntry.width;
                            w                            |= ((uint)glyphEntry.height) << 16;
                            uploadMetaBuffer[glyphIndex]  = new uint4(x, y, z, w);
                        }
                        for (int i = 0; i < bitmapTextureSlice.Length; i++)
                        {
                            var argb              = paintData.paintSurface[i];
                            bitmapTextureSlice[i] = new Color32(argb.r, argb.g, argb.b, argb.a);
                        }
                    }
                    else
                        uploadMetaBuffer[glyphIndex] = default;
                }
            }

            NativeArray<byte> GetSdf8Upload(int glyphIndex, int width, int height)
            {
                int pixelCount = width * height;
                var pixelStart = pixelUploadOffsetsInBytes[glyphIndex];
                return uploadBuffer.GetSubArray(pixelStart, pixelCount);
            }

            NativeArray<ushort> GetSdf16Upload(int glyphIndex, int width, int height)
            {
                int pixelCount = width * height * 2;
                var pixelStart = pixelUploadOffsetsInBytes[glyphIndex];
                return uploadBuffer.GetSubArray(pixelStart, pixelCount).Reinterpret<ushort>(1);
            }

            NativeArray<Color32> GetBitmapUpload(int glyphIndex, int width, int height)
            {
                int pixelCount = width * height * 4;
                var pixelStart = pixelUploadOffsetsInBytes[glyphIndex];
                return uploadBuffer.GetSubArray(pixelStart, pixelCount).Reinterpret<Color32>(1);
            }
        }

        [BurstCompile]
        struct WriteRenderGlyphsToGpuJob : IJobFor
        {
            [ReadOnly] public GlyphTable                                          glyphTable;
            [ReadOnly] public NativeArray<RenderGlyphCapture>                     captures;
            [NativeDisableParallelForRestriction] public NativeArray<RenderGlyph> uploadArray;
            public NativeArray<uint3>                                             uploadMetaArray;

            public void Execute(int index)
            {
                const float kTextureResolutionFloatInverse = 1f / kTextureDimension;
                var         capture                        = captures[index];
                for (int i = 0; i < capture.glyphCount; i++)
                {
                    var glyph = capture.glyphBuffer[i];
                    var entry = glyphTable.GetEntry(glyph.glyphEntryId);

                    glyph.arrayIndex = (uint)entry.z;
                    // Todo: Currently we are overwriting these values because glyph generation doesn't need to augment these.
                    // Should we change that there? Or should we change the RenderGlyph comment?

                    glyph.blUVA = new float2(entry.x, entry.y) * kTextureResolutionFloatInverse;
                    glyph.trUVA = glyph.blUVA + (new float2(entry.width, entry.height) + entry.padding * 2) * kTextureResolutionFloatInverse;

                    // Debug:
                    //if (i < 5 && entry.key.format == RenderFormat.SDF8)
                    //{
                    //    UnityEngine.Debug.Log($"x: {entry.x}, y: {entry.y}, width: {entry.width}, height: {entry.height}, arrayIndex: {entry.z}, blUVA: {glyph.blUVA}, trUVA: {glyph.trUVA}");
                    //}

                    // Assigning back the values breaks MemCmp.
                    //capture.glyphBuffer[i] = glyph;

                    uploadArray[capture.writeStart + i] = glyph;
                }
                uploadMetaArray[index] = new uint3((uint)capture.writeStart, (uint)capture.gpuStart, (uint)capture.glyphCount);
            }
        }
        #endregion
    }
}

