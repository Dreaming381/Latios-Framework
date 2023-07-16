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
                                .WithAll<FontBlobReference>(    true)
                                .WithAll<RenderGlyph>(          false)
                                .WithAll<CalliByte>(            true)
                                .WithAll<TextBaseConfiguration>(true)
                                .WithAll<TextRenderControl>(    false)
                                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                calliByteHandle             = GetBufferTypeHandle<CalliByte>(true),
                textRenderControlHandle     = GetComponentTypeHandle<TextRenderControl>(false),
                renderGlyphHandle           = GetBufferTypeHandle<RenderGlyph>(false),
                textBaseConfigurationHandle = GetComponentTypeHandle<TextBaseConfiguration>(true),
                fontBlobReferenceHandle     = GetComponentTypeHandle<FontBlobReference>(true),
            }.ScheduleParallel(m_singleFontQuery, state.Dependency);
        }

        [BurstCompile]
        public partial struct Job : IJobChunk
        {
            public BufferTypeHandle<RenderGlyph>          renderGlyphHandle;
            public ComponentTypeHandle<TextRenderControl> textRenderControlHandle;

            [ReadOnly]
            public BufferTypeHandle<CalliByte> calliByteHandle;
            [ReadOnly]
            public ComponentTypeHandle<TextBaseConfiguration> textBaseConfigurationHandle;
            [ReadOnly]
            public ComponentTypeHandle<FontBlobReference> fontBlobReferenceHandle;

            [NativeDisableContainerSafetyRestriction]
            private NativeList<RichTextTag> m_richTextTags;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var calliBytesBuffers      = chunk.GetBufferAccessor(ref calliByteHandle);
                var renderGlyphBuffers     = chunk.GetBufferAccessor(ref renderGlyphHandle);
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

                    if (!m_richTextTags.IsCreated)
                    {
                        m_richTextTags = new NativeList<RichTextTag>(Allocator.Temp);
                    }

                    RichTextParser.ParseTags(ref m_richTextTags, calliBytes);

                    GlyphGeneration.CreateRenderGlyphs(ref renderGlyphs, ref fontBlobReference.blob.Value, in calliBytes, in textBaseConfiguration, ref m_richTextTags);

                    textRenderControl.flags          = TextRenderControl.Flags.Dirty;
                    textRenderControls[indexInChunk] = textRenderControl;
                }
            }
        }
    }
}

