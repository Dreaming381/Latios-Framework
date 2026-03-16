using Font = Latios.Calligraphics.HarfBuzz.Font;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Latios.Calligraphics.Systems
{
    public partial struct GenerateGlyphsSystem
    {
        [BurstCompile]
        struct AllocateNewGlyphsJob : IJob
        {
            [ReadOnly] public FontTable       fontTable;
            public GlyphTable                 glyphTable;
            public NativeStream.Reader        missingGlyphsStream;
            public NativeList<GlyphTable.Key> missingGlyphsToAdd;

            public void Execute()
            {
                // Deduplicate
                var requestCount          = missingGlyphsStream.Count();
                var uniqueMissingGlyphSet = new UnsafeHashSet<GlyphTable.Key>(requestCount, Allocator.Temp);
                for (int chunk = 0; chunk < missingGlyphsStream.ForEachCount; chunk++)
                {
                    var elementsInChunk = missingGlyphsStream.BeginForEachIndex(chunk);
                    for (int i = 0; i < elementsInChunk; i++)
                    {
                        var key = missingGlyphsStream.Read<GlyphTable.Key>();
                        uniqueMissingGlyphSet.Add(key);
                    }
                }

                missingGlyphsToAdd.Capacity = uniqueMissingGlyphSet.Count;
                uint nextIndex              = (uint)glyphTable.glyphHashToIdMap.Count;
                foreach (var key in uniqueMissingGlyphSet)
                {
                    missingGlyphsToAdd.AddNoResize(key);
                    var  nextId  = nextIndex;
                    uint topBits = key.format == RenderFormat.Bitmap8888 ? 3 : (uint)key.textureSize;
                    Bits.SetBits(ref nextId, 30, 2, topBits);
                    glyphTable.glyphHashToIdMap.Add(key, nextId);
                    nextIndex++;
                }
                glyphTable.entries.AddReplicate(default, missingGlyphsToAdd.Length);
            }
        }

        [BurstCompile]
        struct PopulateNewGlyphsJob : IJobParallelForDefer, IJob
        {
            [ReadOnly] public NativeArray<GlyphTable.Key>                              missingGlyphs;
            [ReadOnly] public FontTable                                                fontTable;
            [NativeDisableParallelForRestriction] public NativeArray<GlyphTable.Entry> glyphEntries;

            [NativeSetThreadIndex]
            int threadIndex;

            GlyphTable.Key                           lastKey;
            [NativeDisableUnsafePtrRestriction] Font lastFont;
            bool                                     initialized;

            public void Execute()
            {
                for (int i = 0; i < missingGlyphs.Length; i++)
                {
                    Execute(i);
                }
            }

            public void Execute(int i)
            {
                var missingGlyph = missingGlyphs[i];
                var font         = lastFont;

                if (!initialized || RequiresFontSetup(lastKey, missingGlyph))
                {
                    font = fontTable.GetOrCreateFont(missingGlyph.faceIndex, threadIndex);
                    if (fontTable.faces[missingGlyph.faceIndex].HasVarData && font.currentVariableProfileIndex != missingGlyph.variableProfileIndex)
                        font = fontTable.SetVariableProfile(missingGlyph.faceIndex, threadIndex, missingGlyph.variableProfileIndex);

                    var samplingSize = missingGlyph.GetSamplingSize();
                    font.SetScale(samplingSize, samplingSize);
                    initialized = true;
                    lastFont    = font;
                    lastKey     = missingGlyph;
                }

                // performance watchout:  hb_font_get_glyph_extents is a very costly function.
                // For a COLR glyph, rect is determined by parsing all vertices of maybe 20 sub-glyphs
                // in my tests getting glyph extents in parallel resulted in
                // total time = thread * (single thread time)
                // reason unknown. Could be mutex lock. Or single thread benefits more
                // from font acceleration structures populated with each hb_font_get_glyph_extents call
                font.GetGlyphExtents(missingGlyph.glyphIndex, out var extents);

                var padding  = missingGlyph.GetSpread() + 1;
                var newEntry = new GlyphTable.Entry
                {
                    key      = missingGlyph,
                    refCount = 0,
                    x        = -1,
                    y        = -1,
                    z        = -1,
                    width    = (short)extents.width,
                    height   = (short)(extents.height),
                    xBearing = (short)extents.x_bearing,
                    yBearing = (short)extents.y_bearing,  // Harfbuzz is y-up
                    padding  = (short)padding,
                };
                var baseIndex               = glyphEntries.Length - missingGlyphs.Length;
                glyphEntries[baseIndex + i] = newEntry;
            }

            bool RequiresFontSetup(GlyphTable.Key lastKey, GlyphTable.Key thisKey)
            {
                var a = lastKey.packed & 0xffffffffffff0000;
                var b = thisKey.packed & 0xffffffffffff0000;
                return a != b;
            }
        }
    }
}

