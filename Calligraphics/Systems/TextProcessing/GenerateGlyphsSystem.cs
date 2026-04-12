using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;

namespace Latios.Calligraphics.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    [DisableAutoCreation]
    public partial struct GenerateGlyphsSystem : ISystem
    {
        LatiosWorldUnmanaged           latiosWorld;
        EntityQuery                    m_query;
        static readonly ProfilerMarker sShapeMarker  = new ProfilerMarker("hb_shape");
        static readonly ProfilerMarker sBufferMarker = new ProfilerMarker("buffer");

        bool m_skipChangeFilter;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = SystemAPI.QueryBuilder()
                          .WithAllRW<CalliByte>()
                          .WithAll<TextBaseConfiguration>()
                          .Build();

            m_skipChangeFilter = (state.WorldUnmanaged.Flags & WorldFlags.Editor) == WorldFlags.Editor;

            var glyphTable = new GlyphTable
            {
                entries          = new NativeList<GlyphTable.Entry>(1024, Allocator.Persistent),
                glyphHashToIdMap = new NativeHashMap<GlyphTable.Key, uint>(1024, Allocator.Persistent)
            };
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(glyphTable);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var fontTable  = latiosWorld.worldBlackboardEntity.GetCollectionComponent<FontTable>(true);
            var glyphTable = latiosWorld.worldBlackboardEntity.GetCollectionComponent<GlyphTable>(false);

            int entityCount        = m_query.CalculateEntityCountWithoutFiltering();
            var chunkCount         = m_query.CalculateChunkCountWithoutFiltering();
            var missingGlyphStream = new NativeStream(chunkCount, state.WorldUpdateAllocator);
            var glyphOTFStream     = new NativeStream(entityCount, state.WorldUpdateAllocator);
            var xmlTagStream       = new NativeStream(entityCount, state.WorldUpdateAllocator);

            var firstEntityIndexInChunk = m_query.CalculateBaseEntityIndexArrayAsync(state.WorldUpdateAllocator, state.Dependency, out JobHandle firstEntityJH);

            //optional single threaded job to pre-allcoate RenderGlyphbuffer...pays off when spawning a lot of new TextRenderer
            var allocateJH = new AllocateRenderGlyphsJob
            {
                calliByteHandle   = SystemAPI.GetBufferTypeHandle<CalliByte>(true),
                renderGlyphHandle = SystemAPI.GetBufferTypeHandle<RenderGlyph>(false),

                lastSystemVersion = m_skipChangeFilter ? 0 : state.LastSystemVersion,
            }.Schedule(m_query, state.Dependency);

            state.Dependency = new ExtractTagsJob
            {
                firstEntityIndexInChunk     = firstEntityIndexInChunk,
                xmlTagStream                = xmlTagStream.AsWriter(),
                calliByteHandle             = SystemAPI.GetBufferTypeHandle<CalliByte>(true),
                textBaseConfigurationHandle = SystemAPI.GetComponentTypeHandle<TextBaseConfiguration>(true),

                lastSystemVersion = m_skipChangeFilter ? 0 : state.LastSystemVersion,
            }.ScheduleParallel(m_query, firstEntityJH);

            state.Dependency = new ShapeJob
            {
                shapeMarker  = sShapeMarker,
                bufferMarker = sBufferMarker,

                firstEntityIndexInChunk = firstEntityIndexInChunk,
                glyphOTFStream          = glyphOTFStream.AsWriter(),
                missingGlyphsStream     = missingGlyphStream.AsWriter(),
                xmlTagStream            = xmlTagStream.AsReader(),

                glyphTable                  = glyphTable,
                fontTable                   = fontTable,
                textBaseConfigurationHandle = SystemAPI.GetComponentTypeHandle<TextBaseConfiguration>(true),
                calliByteHandle             = SystemAPI.GetBufferTypeHandle<CalliByte>(true),

                lastSystemVersion = m_skipChangeFilter ? 0 : state.LastSystemVersion,
            }.ScheduleParallel(m_query, state.Dependency);

            var missingGlyphsToAdd = new NativeList<GlyphTable.Key>(state.WorldUpdateAllocator);
            state.Dependency       = new AllocateNewGlyphsJob
            {
                fontTable           = fontTable,
                glyphTable          = glyphTable,
                missingGlyphsStream = missingGlyphStream.AsReader(),
                missingGlyphsToAdd  = missingGlyphsToAdd
            }.Schedule(state.Dependency);

            // Todo: As of harfbuzz 12.0.0, a Face object contains various table accerators for each glyph type.
            // For example, true-type outlines have a separate accelerator than COLR. Each accelerator contains
            // a scratch buffer which is acquired by mutex. And fetching the glyph extents locks this mutex.
            // Based on this, the most likely way to parallelize capturing glyph extents would be to group new
            // glyphs by face and then by type. However, this isn't the full story.
            // True-type glyphs are cheap to calculate extents for, and so there may not be any benefit to
            // parallelizing those in practice. COLR may be more expensive, but current tests still show this
            // to not be very significant except for the very first glyph processed, which has multiple
            // milliseconds of latency. And if multiple threads attempt to operate on COLR glyphs before the
            // first one is done, the CPU runs into some kind of thrashing situation. This requires more
            // investigation and testing to characterize what operations are actually parallelizable. In the
            // meantime, we run this job single-threaded.
            state.Dependency = new PopulateNewGlyphsJob
            {
                fontTable     = fontTable,
                glyphEntries  = glyphTable.entries.AsDeferredJobArray(),
                missingGlyphs = missingGlyphsToAdd.AsDeferredJobArray()
                                //}.Schedule(missingGlyphsToAdd, 4, state.Dependency);
            }.Schedule(state.Dependency);

            state.Dependency = JobHandle.CombineDependencies(state.Dependency, allocateJH);
            state.Dependency = new GenerateRenderGlyphsJob
            {
                renderGlyphHandle         = SystemAPI.GetBufferTypeHandle<RenderGlyph>(false),
                previousRenderGlyphHandle = SystemAPI.GetBufferTypeHandle<PreviousRenderGlyph>(false),

                fontTable  = fontTable,
                glyphTable = glyphTable,

                glyphOTFStream          = glyphOTFStream.AsReader(),
                xmlTagStream            = xmlTagStream.AsReader(),
                firstEntityIndexInChunk = firstEntityIndexInChunk,

                calliByteHandle             = SystemAPI.GetBufferTypeHandle<CalliByte>(true),
                textBaseConfigurationHandle = SystemAPI.GetComponentTypeHandle<TextBaseConfiguration>(true),

                textColorGradientEntity = latiosWorld.worldBlackboardEntity,
                textColorGradientLookup = SystemAPI.GetBufferLookup<TextColorGradient>(true),

                lastSystemVersion = m_skipChangeFilter ? 0 : state.LastSystemVersion,
            }.ScheduleParallel(m_query, state.Dependency);
        }
    }
}

