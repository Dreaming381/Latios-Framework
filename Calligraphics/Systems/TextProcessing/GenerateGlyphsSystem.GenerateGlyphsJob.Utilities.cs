using Latios.Calligraphics.HarfBuzz;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics.Systems
{
    public partial struct GenerateGlyphsSystem
    {
        partial struct GenerateRenderGlyphsJob
        {
            static float2 RotatePoint(float2 point, float2 pivot, float sin, float cos)
            {
                float2 translated = point - pivot;
                return new float2(
                    translated.x * cos - translated.y * sin,
                    translated.x * sin + translated.y * cos
                    ) + pivot;
            }

            static float GetTopAnchorForConfig(ref Font font, VerticalAlignmentOptions verticalMode, float baseScale, float oldValue = float.PositiveInfinity)
            {
                bool replace = oldValue == float.PositiveInfinity;
                switch (verticalMode)
                {
                    case VerticalAlignmentOptions.TopBase: return 0f;
                    case VerticalAlignmentOptions.MiddleTopAscentToBottomDescent:
                    case VerticalAlignmentOptions.TopAscent: return baseScale *
                               math.max(font.fontExtents.ascender - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                    case VerticalAlignmentOptions.TopDescent: return baseScale * math.min(font.fontExtents.descender - font.baseLine, oldValue);
                    case VerticalAlignmentOptions.TopCap: return baseScale * math.max(font.capHeight - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                    case VerticalAlignmentOptions.TopMean: return baseScale * math.max(font.xHeight - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                    default: return 0f;
                }
            }

            static float GetBottomAnchorForConfig(ref Font font, VerticalAlignmentOptions verticalMode, float baseScale, float oldValue = float.PositiveInfinity)
            {
                bool replace = oldValue == float.PositiveInfinity;
                switch (verticalMode)
                {
                    case VerticalAlignmentOptions.BottomBase: return 0f;
                    case VerticalAlignmentOptions.BottomAscent: return baseScale *
                               math.max(font.fontExtents.ascender - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                    case VerticalAlignmentOptions.MiddleTopAscentToBottomDescent:
                    case VerticalAlignmentOptions.BottomDescent: return baseScale * math.min(font.fontExtents.descender - font.baseLine, oldValue);
                    case VerticalAlignmentOptions.BottomCap: return baseScale * math.max(font.capHeight - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                    case VerticalAlignmentOptions.BottomMean: return baseScale * math.max(font.xHeight - font.baseLine, math.select(oldValue, float.NegativeInfinity, replace));
                    default: return 0f;
                }
            }

            static unsafe void ApplyHorizontalAlignmentToGlyphs(ref NativeArray<RenderGlyph> renderGlyphs,
                                                                ref FixedList512Bytes<int>   characterGlyphIndicesWithPreceedingSpacesInLine,
                                                                float width,
                                                                HorizontalAlignmentOptions alignMode)
            {
                if ((alignMode) == HorizontalAlignmentOptions.Left)
                {
                    characterGlyphIndicesWithPreceedingSpacesInLine.Clear();
                    return;
                }

                var renderGlyphsPtr = (RenderGlyph*)renderGlyphs.GetUnsafePtr();
                if (alignMode == HorizontalAlignmentOptions.Center)
                {
                    float offset = renderGlyphsPtr[renderGlyphs.Length - 1].trPosition.x / 2f;
                    for (int i = 0; i < renderGlyphs.Length; i++)
                    {
                        renderGlyphsPtr[i].blPosition.x -= offset;
                        renderGlyphsPtr[i].trPosition.x -= offset;
                        renderGlyphsPtr[i].tlPosition.x -= offset;
                        renderGlyphsPtr[i].brPosition.x -= offset;
                    }
                }
                else if (alignMode == HorizontalAlignmentOptions.Right)
                {
                    float offset = renderGlyphsPtr[renderGlyphs.Length - 1].trPosition.x;
                    for (int i = 0; i < renderGlyphs.Length; i++)
                    {
                        renderGlyphsPtr[i].blPosition.x -= offset;
                        renderGlyphsPtr[i].trPosition.x -= offset;
                        renderGlyphsPtr[i].tlPosition.x -= offset;
                        renderGlyphsPtr[i].brPosition.x -= offset;
                    }
                }
                else  // Justified
                {
                    float nudgePerSpace     = (width - renderGlyphsPtr[renderGlyphs.Length - 1].trPosition.x) / characterGlyphIndicesWithPreceedingSpacesInLine.Length;
                    float accumulatedOffset = 0f;
                    int   indexInIndices    = 0;
                    for (int i = 0; i < renderGlyphs.Length; i++)
                    {
                        while (indexInIndices < characterGlyphIndicesWithPreceedingSpacesInLine.Length &&
                               characterGlyphIndicesWithPreceedingSpacesInLine[indexInIndices] == i)
                        {
                            accumulatedOffset += nudgePerSpace;
                            indexInIndices++;
                        }

                        renderGlyphsPtr[i].blPosition.x += accumulatedOffset;
                        renderGlyphsPtr[i].trPosition.x += accumulatedOffset;
                        renderGlyphsPtr[i].tlPosition.x += accumulatedOffset;
                        renderGlyphsPtr[i].brPosition.x += accumulatedOffset;
                    }
                }
                characterGlyphIndicesWithPreceedingSpacesInLine.Clear();
            }

            static unsafe void ApplyVerticalOffsetToGlyphs(ref NativeArray<RenderGlyph> renderGlyphs, float accumulatedVerticalOffset)
            {
                for (int i = 0; i < renderGlyphs.Length; i++)
                {
                    var glyph           = renderGlyphs[i];
                    glyph.blPosition.y -= accumulatedVerticalOffset;
                    glyph.tlPosition.y -= accumulatedVerticalOffset;
                    glyph.trPosition.y -= accumulatedVerticalOffset;
                    glyph.brPosition.y -= accumulatedVerticalOffset;
                    renderGlyphs[i]     = glyph;
                }
            }

            static unsafe void ApplyVerticalAlignmentToGlyphs(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                                                              float topAnchor,
                                                              float bottomAnchor,
                                                              float accumulatedVerticalOffset,
                                                              VerticalAlignmentOptions alignMode)
            {
                var renderGlyphsPtr = (RenderGlyph*)renderGlyphs.GetUnsafePtr();
                switch (alignMode)
                {
                    case VerticalAlignmentOptions.TopBase:
                        return;
                    case VerticalAlignmentOptions.TopAscent:
                    case VerticalAlignmentOptions.TopDescent:
                    case VerticalAlignmentOptions.TopCap:
                    case VerticalAlignmentOptions.TopMean:
                    {
                        // Positions were calculated relative to the baseline.
                        // Shift everything down so that y = 0 is on the target line.
                        for (int i = 0; i < renderGlyphs.Length; i++)
                        {
                            renderGlyphsPtr[i].blPosition.y -= topAnchor;
                            renderGlyphsPtr[i].tlPosition.y -= topAnchor;
                            renderGlyphsPtr[i].trPosition.y -= topAnchor;
                            renderGlyphsPtr[i].brPosition.y -= topAnchor;
                        }
                        break;
                    }
                    case VerticalAlignmentOptions.BottomBase:
                    case VerticalAlignmentOptions.BottomAscent:
                    case VerticalAlignmentOptions.BottomDescent:
                    case VerticalAlignmentOptions.BottomCap:
                    case VerticalAlignmentOptions.BottomMean:
                    {
                        float offset = accumulatedVerticalOffset - bottomAnchor;
                        for (int i = 0; i < renderGlyphs.Length; i++)
                        {
                            renderGlyphsPtr[i].blPosition.y += offset;
                            renderGlyphsPtr[i].tlPosition.y += offset;
                            renderGlyphsPtr[i].trPosition.y += offset;
                            renderGlyphsPtr[i].brPosition.y += offset;
                        }
                        break;
                    }
                    case VerticalAlignmentOptions.MiddleTopAscentToBottomDescent:
                    {
                        float fullHeight = accumulatedVerticalOffset - topAnchor - bottomAnchor;
                        float offset     = fullHeight / 2f;
                        for (int i = 0; i < renderGlyphs.Length; i++)
                        {
                            renderGlyphsPtr[i].blPosition.y += offset;
                            renderGlyphsPtr[i].tlPosition.y += offset;
                            renderGlyphsPtr[i].trPosition.y += offset;
                            renderGlyphsPtr[i].brPosition.y += offset;
                        }
                        break;
                    }
                }
            }

            static unsafe void ApplyOffsetChange(ref DynamicBuffer<RenderGlyph> renderGlyphs,
                                                 int lastWordStartCharacterGlyphIndex,
                                                 float xOffsetChange,
                                                 float yOffsetChange)
            {
                var renderGlyphsPtr = (RenderGlyph*)renderGlyphs.GetUnsafePtr();
                for (int i = lastWordStartCharacterGlyphIndex, ii = renderGlyphs.Length; i < ii; i++)
                {
                    renderGlyphsPtr[i].blPosition.y -= yOffsetChange;
                    renderGlyphsPtr[i].blPosition.x -= xOffsetChange;
                    renderGlyphsPtr[i].tlPosition.y -= yOffsetChange;
                    renderGlyphsPtr[i].tlPosition.x -= xOffsetChange;
                    renderGlyphsPtr[i].trPosition.y -= yOffsetChange;
                    renderGlyphsPtr[i].trPosition.x -= xOffsetChange;
                    renderGlyphsPtr[i].brPosition.y -= yOffsetChange;
                    renderGlyphsPtr[i].brPosition.x -= xOffsetChange;
                }
            }

            static half4 GetColorAsHDRHalf4(UnityEngine.Color32 c)
            {
                return new half4(new half(c.r / 255f), new half(c.g / 255f), new half(c.b / 255f), new half(c.a / 255f));
            }
        }
    }
}

