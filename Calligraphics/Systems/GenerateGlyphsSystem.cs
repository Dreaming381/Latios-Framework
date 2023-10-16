using Latios.Calligraphics.RichText;
using Latios.Calligraphics.RichText.Parsing;
using Latios.Kinemation.Systems;
using Latios.Kinemation.TextBackend;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Calligraphics.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
    [UpdateBefore(typeof(KinemationRenderUpdateSuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial struct GenerateGlyphsSystem : ISystem
    {
        EntityQuery m_singleFontQuery;
        EntityQuery m_multiFontQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_singleFontQuery = state.Fluent()
                                .With<FontBlobReference>(    true)
                                .With<RenderGlyph>(          false)
                                .With<CalliByte>(            true)
                                .With<TextBaseConfiguration>(true)
                                .With<TextRenderControl>(    false)
                                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                calliByteHandle             = GetBufferTypeHandle<CalliByte>(true),
                fontBlobReferenceHandle     = GetComponentTypeHandle<FontBlobReference>(true),
                glyphMappingElementHandle   = GetBufferTypeHandle<GlyphMappingElement>(false),
                glyphMappingMaskHandle      = GetComponentTypeHandle<GlyphMappingMask>(true),
                renderGlyphHandle           = GetBufferTypeHandle<RenderGlyph>(false),
                textBaseConfigurationHandle = GetComponentTypeHandle<TextBaseConfiguration>(true),
                textRenderControlHandle     = GetComponentTypeHandle<TextRenderControl>(false),
            }.ScheduleParallel(m_singleFontQuery, state.Dependency);
        }

        [BurstCompile]
        public partial struct Job : IJobChunk
        {
            public BufferTypeHandle<RenderGlyph>          renderGlyphHandle;
            public BufferTypeHandle<GlyphMappingElement>  glyphMappingElementHandle;
            public ComponentTypeHandle<TextRenderControl> textRenderControlHandle;

            [ReadOnly] public ComponentTypeHandle<GlyphMappingMask>      glyphMappingMaskHandle;
            [ReadOnly] public BufferTypeHandle<CalliByte>                calliByteHandle;
            [ReadOnly] public ComponentTypeHandle<TextBaseConfiguration> textBaseConfigurationHandle;
            [ReadOnly] public ComponentTypeHandle<FontBlobReference>     fontBlobReferenceHandle;

            [NativeDisableContainerSafetyRestriction]
            private NativeList<RichTextTag> m_richTextTags;

            private GlyphMappingWriter m_glyphMappingWriter;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var calliBytesBuffers      = chunk.GetBufferAccessor(ref calliByteHandle);
                var renderGlyphBuffers     = chunk.GetBufferAccessor(ref renderGlyphHandle);
                var glyphMappingBuffers    = chunk.GetBufferAccessor(ref glyphMappingElementHandle);
                var glyphMappingMasks      = chunk.GetNativeArray(ref glyphMappingMaskHandle);
                var textBaseConfigurations = chunk.GetNativeArray(ref textBaseConfigurationHandle);
                var fontBlobReferences     = chunk.GetNativeArray(ref fontBlobReferenceHandle);
                var textRenderControls     = chunk.GetNativeArray(ref textRenderControlHandle);

                for (int indexInChunk = 0; indexInChunk < chunk.Count; indexInChunk++)
                {
                    var calliBytes            = calliBytesBuffers[indexInChunk];
                    var renderGlyphs          = renderGlyphBuffers[indexInChunk];
                    var fontBlobReference     = fontBlobReferences[indexInChunk];
                    var textBaseConfiguration = textBaseConfigurations[indexInChunk];
                    var textRenderControl     = textRenderControls[indexInChunk];

                    m_glyphMappingWriter.StartWriter(glyphMappingMasks.Length > 0 ? glyphMappingMasks[indexInChunk].mask : default);

                    if (!m_richTextTags.IsCreated)
                    {
                        m_richTextTags = new NativeList<RichTextTag>(Allocator.Temp);
                    }

                    RichTextParser.ParseTags(ref m_richTextTags, calliBytes);

                    GlyphGeneration.CreateRenderGlyphs(ref renderGlyphs,
                                                       ref m_glyphMappingWriter,
                                                       ref fontBlobReference.blob.Value,
                                                       in calliBytes,
                                                       in textBaseConfiguration,
                                                       ref m_richTextTags);

                    if (glyphMappingBuffers.Length > 0)
                    {
                        var mapping = glyphMappingBuffers[indexInChunk];
                        m_glyphMappingWriter.EndWriter(ref mapping, renderGlyphs.Length);
                    }

                    textRenderControl.flags          = TextRenderControl.Flags.Dirty;
                    textRenderControls[indexInChunk] = textRenderControl;
                }
            }
        }
    }
}

