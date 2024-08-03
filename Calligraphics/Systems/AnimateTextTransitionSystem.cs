using Latios.Calligraphics.Rendering;
using Latios.Calligraphics.RichText;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Calligraphics.Systems
{
    [UpdateInGroup(typeof(CalligraphicsUpdateSuperSystem))]
    [UpdateAfter(typeof(GenerateGlyphsSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial struct AnimateTextTransitionSystem : ISystem
    {
        EntityQuery m_query;
        Rng         m_rng;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent()
                      .With<TextAnimationTransition>( false)
                      .With<RenderGlyph>(             false)
                      .With<TextRenderControl>(       false)
                      .With<GlyphMappingElement>(     true)
                      .Build();

            m_rng = new Rng(new FixedString128Bytes("AnimateTextTransitionSystem"));
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new TransitionJob
            {
                rng                       = m_rng,
                deltaTime                 = state.WorldUnmanaged.Time.DeltaTime,
                transitionHandle          = GetBufferTypeHandle<TextAnimationTransition>(false),
                textRenderControlHandle   = GetComponentTypeHandle<TextRenderControl>(false),
                renderGlyphHandle         = GetBufferTypeHandle<RenderGlyph>(false),
                glyphMappingElementHandle = GetBufferTypeHandle<GlyphMappingElement>(true),
                stringHandle              = GetBufferTypeHandle<CalliByte>(false)
            }.ScheduleParallel(m_query, state.Dependency);

            m_rng.Shuffle();
        }

        public void OnDestroy(ref SystemState state)
        {
            state.Dependency = new DisposeJob
            {
                transitionHandle = GetBufferTypeHandle<TextAnimationTransition>(false),
            }.ScheduleParallel(m_query, state.Dependency);
            state.Dependency.Complete();
        }

        [BurstCompile]
        public partial struct TransitionJob : IJobChunk
        {
            public float deltaTime;
            public Rng   rng;

            public BufferTypeHandle<TextAnimationTransition> transitionHandle;
            public BufferTypeHandle<RenderGlyph>             renderGlyphHandle;
            public ComponentTypeHandle<TextRenderControl>    textRenderControlHandle;
            public BufferTypeHandle<CalliByte>               stringHandle;

            [ReadOnly] public BufferTypeHandle<GlyphMappingElement> glyphMappingElementHandle;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // Dirty strings so that glyphs get "reset" next frame:
                chunk.GetBufferAccessor(ref stringHandle);

                var random              = rng.GetSequence(unfilteredChunkIndex);
                var transitionBuffers   = chunk.GetBufferAccessor(ref transitionHandle);
                var renderGlyphBuffers  = chunk.GetBufferAccessor(ref renderGlyphHandle);
                var glyphMappingBuffers = chunk.GetBufferAccessor(ref glyphMappingElementHandle);
                var textRenderControls  = chunk.GetNativeArray(ref textRenderControlHandle);

                for (int indexInChunk = 0; indexInChunk < chunk.Count; indexInChunk++)
                {
                    var transitions       = transitionBuffers[indexInChunk];
                    var renderGlyphs      = renderGlyphBuffers[indexInChunk];
                    var textRenderControl = textRenderControls[indexInChunk];

                    var glyphMapper = new GlyphMapper(glyphMappingBuffers[indexInChunk]);

                    for (int i = 0; i < transitions.Length; i++)
                    {
                        var transition = transitions[i];

                        //Loop if appropriate
                        if ((transition.endBehavior & TransitionEndBehavior.Loop) == TransitionEndBehavior.Loop)
                        {
                            if (transition.currentTime >= transition.transitionDelay + transition.transitionDuration &&
                                transition.currentTime >= transition.loopDelay)
                            {
                                transition.currentIteration++;
                                transition.currentTime = 0f;

                                if (transition.currentIteration > transition.loopCount &&
                                    (transition.endBehavior & TransitionEndBehavior.Revert) ==
                                    TransitionEndBehavior.Revert)
                                {
                                    AnimationResolver.DisposeTransition(ref transition);
                                    transitions.RemoveAtSwapBack(i);
                                    i--;
                                    continue;
                                }
                            }

                            if (transition.currentIteration > transition.loopCount)
                            {
                                transition.currentTime = 0f;
                            }
                        }

                        if (transition.currentTime == 0)
                        {
                            AnimationResolver.Initialize(ref transition, ref random, glyphMapper);
                        }

                        //Get scope indices
                        var startIndex = 0;
                        var endIndex   = 0;
                        switch (transition.scope)
                        {
                            case TransitionTextUnitScope.All:
                                startIndex = 0;
                                endIndex   = renderGlyphs.Length;
                                break;
                            case TransitionTextUnitScope.Glyph:
                                if (!glyphMapper.TryGetGlyphIndexForCharNoTags(transition.startIndex, out startIndex))
                                    startIndex = -1;
                                if (!glyphMapper.TryGetGlyphIndexForCharNoTags(transition.endIndex, out endIndex))
                                    endIndex = -1;
                                break;
                            case TransitionTextUnitScope.Word:
                                startIndex = glyphMapper.GetGlyphStartIndexAndCountForWord(math.min(transition.startIndex, glyphMapper.wordCount - 1)).x;
                                if (transition.endIndex >= glyphMapper.wordCount)
                                    endIndex = -1;
                                else if (transition.endIndex == glyphMapper.wordCount - 1)
                                    endIndex = renderGlyphs.Length - 1;
                                else if (transition.endIndex == transition.startIndex)
                                    endIndex = glyphMapper.GetGlyphStartIndexAndCountForWord(transition.endIndex + 1).x - 1;
                                else
                                    endIndex = glyphMapper.GetGlyphStartIndexAndCountForWord(transition.endIndex).x;

                                break;
                            case TransitionTextUnitScope.Line:
                                startIndex = glyphMapper.GetGlyphStartIndexAndCountForLine(math.min(transition.startIndex, glyphMapper.lineCount - 1)).x;
                                if (transition.endIndex >= glyphMapper.lineCount)
                                    endIndex = -1;
                                else if (transition.endIndex == glyphMapper.lineCount - 1)
                                    endIndex = renderGlyphs.Length - 1;
                                else if (transition.endIndex == transition.startIndex)
                                    endIndex = glyphMapper.GetGlyphStartIndexAndCountForLine(transition.endIndex + 1).x - 1;
                                else
                                    endIndex = glyphMapper.GetGlyphStartIndexAndCountForLine(transition.endIndex).x;

                                break;
                        }

                        if (startIndex > -1 && endIndex >= startIndex)
                        {
                            //Apply transition
                            float t = (transition.currentTime - transition.transitionDelay) /
                                      transition.transitionDuration;

                            AnimationResolver.SetValue(ref renderGlyphs, transition, glyphMapper, startIndex,
                                                       endIndex, t);
                        }

                        transition.currentTime += deltaTime;
                        transitions[i]          = transition;
                    }

                    textRenderControl.flags          = TextRenderControl.Flags.Dirty;
                    textRenderControls[indexInChunk] = textRenderControl;
                }
            }
        }

        [BurstCompile]
        public struct DisposeJob : IJobChunk
        {
            public BufferTypeHandle<TextAnimationTransition> transitionHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var transitionBuffers = chunk.GetBufferAccessor(ref transitionHandle);

                for (int indexInChunk = 0; indexInChunk < chunk.Count; indexInChunk++)
                {
                    var transitions = transitionBuffers[indexInChunk];
                    for (int i = 0; i < transitions.Length; i++)
                    {
                        var transition = transitions[i];
                        AnimationResolver.DisposeTransition(ref transition);
                    }
                }
            }
        }
    }
}

