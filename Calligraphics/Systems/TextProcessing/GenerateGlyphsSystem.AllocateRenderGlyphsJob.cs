using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace Latios.Calligraphics.Systems
{
    public partial struct GenerateGlyphsSystem
    {
        [BurstCompile]
        internal partial struct AllocateRenderGlyphsJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<CalliByte> calliByteHandle;
            public BufferTypeHandle<RenderGlyph>          renderGlyphHandle;

            public uint lastSystemVersion;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!(chunk.DidChange(ref calliByteHandle, lastSystemVersion)))
                    return;

                var calliBytesBuffers  = chunk.GetBufferAccessor(ref calliByteHandle);
                var renderGlyphBuffers = chunk.GetBufferAccessor(ref renderGlyphHandle);

                for (int indexInChunk = 0; indexInChunk < chunk.Count; indexInChunk++)
                {
                    var calliBytesBuffer  = calliBytesBuffers[indexInChunk];
                    var renderGlyphBuffer = renderGlyphBuffers[indexInChunk];
                    renderGlyphBuffer.EnsureCapacity(calliBytesBuffer.Length);
                }
            }
        }
    }
}

