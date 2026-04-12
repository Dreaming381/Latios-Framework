using System;
using Latios.Unsafe;
using static Unity.Entities.SystemAPI;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Calligraphics.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [RequireMatchingQueriesForUpdate]
    [UpdateAfter(typeof(GenerateGlyphsSystem))]
    [BurstCompile]
    public unsafe partial struct UpdateGlyphsRenderersSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_query;
        EntityQuery          m_deadQuery;

        uint m_twoAgoSystemVersion;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = QueryBuilder().WithAny<RenderGlyph, AnimatedRenderGlyph>().WithPresentRW<GpuState, MaterialMeshInfo>().WithPresentRW<RenderBounds>().Build();
            m_deadQuery = QueryBuilder().WithPresent<PreviousRenderGlyph>().WithAbsent<RenderGlyph, AnimatedRenderGlyph>().Build();

            m_twoAgoSystemVersion = 0;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //var refCountChangeBlocklist       = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<RefCountChange>(), 256, state.WorldUpdateAllocator);
            //var residentDeallocationBlocklist = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<ResidentRange>(), 256, state.WorldUpdateAllocator);

            var deadChunkCount                 = m_deadQuery.CalculateChunkCountWithoutFiltering();
            var chunkCount                     = m_query.CalculateChunkCountWithoutFiltering();
            var refCountChangeBlocklistA       = new NativeStream(deadChunkCount, state.WorldUpdateAllocator);
            var residentDeallocationBlocklistA = new NativeStream(deadChunkCount, state.WorldUpdateAllocator);
            var refCountChangeBlocklistB       = new NativeStream(chunkCount, state.WorldUpdateAllocator);
            var residentDeallocationBlocklistB = new NativeStream(chunkCount, state.WorldUpdateAllocator);

            var newEntitiesArrays = latiosWorld.worldBlackboardEntity.GetCollectionComponent<NewEntitiesList>(true);
            if (newEntitiesArrays.newGlyphEntities.Length > 0)
            {
                state.Dependency = new DirtyNewJob
                {
                    newEntities         = newEntitiesArrays.newGlyphEntities.AsArray(),
                    animatedGlyphLookup = GetBufferLookup<AnimatedRenderGlyph>(false),
                    glyphLookup         = GetBufferLookup<RenderGlyph>(false)
                }.ScheduleParallel(newEntitiesArrays.newGlyphEntities.Length, 128, state.Dependency);
                state.Dependency = new ClearNewEntitiesListJob { newEntities = newEntitiesArrays.newGlyphEntities }.Schedule(state.Dependency);
            }

            if (!m_deadQuery.IsEmptyIgnoreFilter)
            {
                var ecb          = GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
                state.Dependency = new RecordDeadJob
                {
                    entityHandle                  = GetEntityTypeHandle(),
                    ecb                           = ecb.AsParallelWriter(),
                    rangeHandle                   = GetComponentTypeHandle<ResidentRange>(false),
                    previousRenderGlyphsHandle    = GetBufferTypeHandle<PreviousRenderGlyph>(true),
                    refCountChangeBlocklist       = refCountChangeBlocklistA.AsWriter(),
                    residentDeallocationBlocklist = residentDeallocationBlocklistA.AsWriter()
                }.ScheduleParallel(m_deadQuery, state.Dependency);
            }

            state.Dependency = new UpdateChangedGlyphsJob
            {
                animatedRenderGlyphHandle     = GetBufferTypeHandle<AnimatedRenderGlyph>(true),
                entityHandle                  = GetEntityTypeHandle(),
                gpuStateHandle                = GetComponentTypeHandle<GpuState>(false),
                lastSystemVersion             = state.LastSystemVersion,
                materialMeshInfoHandle        = GetComponentTypeHandle<MaterialMeshInfo>(false),
                previousRenderGlyphHandle     = GetBufferTypeHandle<PreviousRenderGlyph>(false),
                refCountChangeBlocklist       = refCountChangeBlocklistB.AsWriter(),
                renderBoundsHandle            = GetComponentTypeHandle<RenderBounds>(false),
                renderGlyphHandle             = GetBufferTypeHandle<RenderGlyph>(true),
                residentDeallocationBlocklist = residentDeallocationBlocklistB.AsWriter(),
                residentRangeHandle           = GetComponentTypeHandle<ResidentRange>(false),
                twoAgoSystemVersion           = m_twoAgoSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);

            var atlasTable = latiosWorld.worldBlackboardEntity.GetCollectionComponent<AtlasTable>(false);
            var glyphTable = latiosWorld.worldBlackboardEntity.GetCollectionComponent<GlyphTable>(false);
            var gpuTable   = latiosWorld.worldBlackboardEntity.GetCollectionComponent<GlyphGpuTable>(false);

            var jhA = new ApplyRefCountDeltasToGlyphTableJob
            {
                atlasTable               = atlasTable,
                glyphTable               = glyphTable,
                refCountChangeBlocklistA = refCountChangeBlocklistA.AsReader(),
                refCountChangeBlocklistB = refCountChangeBlocklistB.AsReader(),
                enableAtlasGC            = true
            }.Schedule(state.Dependency);

            var jhB = new DeallocateResidentsJob
            {
                gpuTable                       = gpuTable,
                residentDeallocationBlocklistA = residentDeallocationBlocklistA.AsReader(),
                residentDeallocationBlocklistB = residentDeallocationBlocklistB.AsReader()
            }.Schedule(state.Dependency);

            state.Dependency      = JobHandle.CombineDependencies(jhA, jhB);
            m_twoAgoSystemVersion = state.LastSystemVersion;
        }

        struct RefCountChange
        {
            public uint glyphEntryId;
            public int  refCountDelta;
        }

        struct RefCountChangePtr
        {
            public RefCountChange* ptr;
        }

        [BurstCompile]
        struct DirtyNewJob : IJobFor
        {
            [ReadOnly] public NativeArray<Entity>                                          newEntities;
            [NativeDisableParallelForRestriction] public BufferLookup<RenderGlyph>         glyphLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<AnimatedRenderGlyph> animatedGlyphLookup;

            public void Execute(int i)
            {
                var entity = newEntities[i];
                if (!glyphLookup.TryGetBuffer(entity, out _))
                    animatedGlyphLookup.TryGetBuffer(entity, out _);
            }
        }

        [BurstCompile]
        struct ClearNewEntitiesListJob : IJob
        {
            public NativeList<Entity> newEntities;

            public void Execute() => newEntities.Clear();
        }

        [BurstCompile]
        struct RecordDeadJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                      entityHandle;
            [ReadOnly] public BufferTypeHandle<PreviousRenderGlyph> previousRenderGlyphsHandle;
            public ComponentTypeHandle<ResidentRange>               rangeHandle;
            public NativeStream.Writer                              residentDeallocationBlocklist;
            public NativeStream.Writer                              refCountChangeBlocklist;
            public EntityCommandBuffer.ParallelWriter               ecb;

            [NativeSetThreadIndex] int             threadIndex;
            UnsafeHashMap<uint, RefCountChangePtr> threadRefCountChangeMap;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!threadRefCountChangeMap.IsCreated)
                    threadRefCountChangeMap = new UnsafeHashMap<uint, RefCountChangePtr>(1024, Allocator.Temp);

                var typeSet = new ComponentTypeSet(ComponentType.ReadWrite<ResidentRange>(), ComponentType.ReadWrite<PreviousRenderGlyph>());
                ecb.RemoveComponent(unfilteredChunkIndex, chunk.GetNativeArray(entityHandle), in typeSet);

                residentDeallocationBlocklist.BeginForEachIndex(unfilteredChunkIndex);
                refCountChangeBlocklist.BeginForEachIndex(unfilteredChunkIndex);

                var ranges = (ResidentRange*)chunk.GetRequiredComponentDataPtrRO(ref rangeHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (ranges[i].count > 0)
                    {
                        residentDeallocationBlocklist.Write(ranges[i]);
                        ranges[i] = default;
                    }
                }
                var glyphsBuffers = chunk.GetBufferAccessor(ref previousRenderGlyphsHandle);
                for (int index = 0; index < chunk.Count; index++)
                {
                    var glyphs = glyphsBuffers[index].Reinterpret<RenderGlyph>().AsNativeArray();
                    for (int i = 0; i < glyphs.Length; i++)
                    {
                        var id = glyphs[i].glyphEntryId;
                        if (threadRefCountChangeMap.TryGetValue(id, out var ptr))
                        {
                            ptr.ptr->refCountDelta--;
                            //Debug.Log($"dead glyph delta {id}: {ptr.ptr->refCountDelta}");
                        }
                        else
                        {
                            var newPtr = new RefCountChangePtr { ptr = (RefCountChange*)refCountChangeBlocklist.Allocate(UnsafeUtility.SizeOf<RefCountChange>()) };
                            newPtr.ptr->glyphEntryId                 = id;
                            newPtr.ptr->refCountDelta                = -1;
                            threadRefCountChangeMap.Add(id, newPtr);
                            //Debug.Log($"dead glyph delta {id}: {newPtr.ptr->refCountDelta}");
                        }
                    }
                }

                residentDeallocationBlocklist.EndForEachIndex();
                refCountChangeBlocklist.EndForEachIndex();
            }
        }

        [BurstCompile]
        struct UpdateChangedGlyphsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                      entityHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyph>         renderGlyphHandle;
            [ReadOnly] public BufferTypeHandle<AnimatedRenderGlyph> animatedRenderGlyphHandle;
            public BufferTypeHandle<PreviousRenderGlyph>            previousRenderGlyphHandle;
            public ComponentTypeHandle<GpuState>                    gpuStateHandle;
            public ComponentTypeHandle<ResidentRange>               residentRangeHandle;
            public ComponentTypeHandle<MaterialMeshInfo>            materialMeshInfoHandle;
            public ComponentTypeHandle<RenderBounds>                renderBoundsHandle;
            public NativeStream.Writer                              refCountChangeBlocklist;
            public NativeStream.Writer                              residentDeallocationBlocklist;

            public uint lastSystemVersion;
            public uint twoAgoSystemVersion;

            [NativeSetThreadIndex] int             threadIndex;
            UnsafeHashMap<uint, RefCountChangePtr> threadRefCountChangeMap;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var  animatedGlyphBuffers = chunk.GetBufferAccessor(ref animatedRenderGlyphHandle);
                bool hasAnimated          = animatedGlyphBuffers.Length > 0;

                if (hasAnimated && !chunk.DidChange(ref animatedRenderGlyphHandle, twoAgoSystemVersion))
                    return;
                else if (!hasAnimated && !chunk.DidChange(ref renderGlyphHandle, twoAgoSystemVersion))
                    return;

                if (!threadRefCountChangeMap.IsCreated)
                    threadRefCountChangeMap = new UnsafeHashMap<uint, RefCountChangePtr>(1024, Allocator.Temp);

                refCountChangeBlocklist.BeginForEachIndex(unfilteredChunkIndex);
                residentDeallocationBlocklist.BeginForEachIndex(unfilteredChunkIndex);

                var glyphBuffers               = !hasAnimated? chunk.GetBufferAccessor(ref renderGlyphHandle) : default;
                var previousRenderGlyphBuffers = chunk.GetBufferAccessor(ref previousRenderGlyphHandle);

                var gpuStates = (GpuState*)chunk.GetRequiredComponentDataPtrRW(ref gpuStateHandle);

                chunk.SetComponentEnabledForAll(ref gpuStateHandle, false);
                var gpuStateMask = chunk.GetEnabledMask(ref gpuStateHandle);

                bool glyphsChanged = animatedGlyphBuffers.Length > 0 ? chunk.DidChange(ref animatedRenderGlyphHandle, lastSystemVersion) : chunk.DidChange(ref renderGlyphHandle,
                                                                                                                                                           lastSystemVersion);
                if (!glyphsChanged)
                {
                    // Nothing changed. Promote any dynamic glyph buffers to resident and exit.
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (gpuStates[i].state == GpuState.State.Dynamic)
                        {
                            gpuStates[i].state = GpuState.State.DynamicPromoteToResident;
                            gpuStateMask[i]    = true;
                        }
                        else if (gpuStates[i].state == GpuState.State.Uncommitted || gpuStates[i].state == GpuState.State.ResidentUncommitted)
                        {
                            gpuStateMask[i] = true;
                        }
                    }
                    refCountChangeBlocklist.EndForEachIndex();
                    residentDeallocationBlocklist.EndForEachIndex();
                    return;
                }

                // Something got flagged as changed. These could be new glyphs, or the text was altered on one of the entities.
                var residentRanges = previousRenderGlyphBuffers.Length > 0 ? chunk.GetComponentDataPtrRW(ref residentRangeHandle) : null;

                {
                    var mmis         = (MaterialMeshInfo*)chunk.GetRequiredComponentDataPtrRW(ref materialMeshInfoHandle);
                    var renderBounds = (RenderBounds*)chunk.GetRequiredComponentDataPtrRW(ref renderBoundsHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var glyphs         = animatedGlyphBuffers.Length > 0 ? animatedGlyphBuffers[i].Reinterpret<RenderGlyph>() : glyphBuffers[i];
                        var previousGlyphs = previousRenderGlyphBuffers[i];
                        if (glyphs.Length == previousGlyphs.Length &&
                            (glyphs.Length == 0 ||
                             UnsafeUtility.MemCmp(glyphs.GetUnsafeReadOnlyPtr(), previousGlyphs.GetUnsafeReadOnlyPtr(), glyphs.Length * UnsafeUtility.SizeOf<RenderGlyph>()) == 0))
                        {
                            // Nothing changed. Promote dynamic to resident if necessary.
                            if (gpuStates[i].state == GpuState.State.Dynamic && glyphs.Length != 0)
                            {
                                gpuStates[i].state = GpuState.State.DynamicPromoteToResident;
                                gpuStateMask[i]    = true;
                            }
                            continue;
                        }

                        // Something changed. Figure out if we need to deallocate or just do an update.
                        if (glyphs.Length != previousGlyphs.Length && residentRanges[i].count != 0)
                        {
                            // We need to deallocate.
                            residentDeallocationBlocklist.Write(residentRanges[i]);
                            //UnityEngine.Debug.Log($"Deallocated resident range: {residentRanges[i].start}, {residentRanges[i].count}");
                            residentRanges[i] = default;
                        }
                        // Reset the state, update ref counts, and copy previousGlyphs
                        gpuStates[i].state = residentRanges[i].count != 0 ? GpuState.State.ResidentUncommitted : GpuState.State.Uncommitted;
                        gpuStateMask[i]    = true;
                        UpdateRefCounts(previousGlyphs.Reinterpret<RenderGlyph>().AsNativeArray().AsReadOnlySpan(), -1);
                        UpdateRefCounts(glyphs.AsNativeArray().AsReadOnlySpan(),                                    1);
                        previousGlyphs.Clear();
                        previousGlyphs.AddRange(glyphs.Reinterpret<PreviousRenderGlyph>().AsNativeArray());
                        UpdateBaseRenderingData(ref renderBounds[i], ref mmis[i], glyphs.AsNativeArray().AsReadOnlySpan());
                    }
                }
                refCountChangeBlocklist.EndForEachIndex();
                residentDeallocationBlocklist.EndForEachIndex();
            }

            void UpdateRefCounts(ReadOnlySpan<RenderGlyph> glyphs, int delta)
            {
                for (int i = 0; i < glyphs.Length; i++)
                {
                    var id = glyphs[i].glyphEntryId;
                    if (threadRefCountChangeMap.TryGetValue(id, out var ptr))
                    {
                        ptr.ptr->refCountDelta += delta;
                        //Debug.Log($"update glyph delta {id}: {ptr.ptr->refCountDelta}");
                    }
                    else
                    {
                        var newPtr = new RefCountChangePtr { ptr = (RefCountChange*)refCountChangeBlocklist.Allocate(UnsafeUtility.SizeOf<RefCountChange>()) };
                        newPtr.ptr->glyphEntryId                 = id;
                        newPtr.ptr->refCountDelta                = delta;
                        threadRefCountChangeMap.Add(id, newPtr);
                        //Debug.Log($"update glyph delta {id}: {newPtr.ptr->refCountDelta}");
                    }
                }
            }

            void UpdateBaseRenderingData(ref RenderBounds bounds, ref MaterialMeshInfo mmi, in ReadOnlySpan<RenderGlyph> glyphs)
            {
                float4 min = float.MaxValue;
                float4 max = float.MinValue;
                for (int i = 0; i < glyphs.Length; i++)
                {
                    var glyph  = glyphs[i];
                    var bottom = new float4(glyph.blPosition, glyph.brPosition);
                    var top    = new float4(glyph.tlPosition, glyph.trPosition);
                    min        = math.min(min, math.min(top, bottom));
                    max        = math.max(max, math.max(top, bottom));
                }
                var min2 = math.min(min.xy, min.zw);
                var max2 = math.max(max.xy, max.zw);

                var center  = new float3(0.5f * (min2 + max2), 0f);
                var extents = new float3((center.xy - min2), 0f);
                if (glyphs.Length == 0)
                {
                    center  = 0f;
                    extents = 0f;
                }
                bounds = new RenderBounds { Value = new AABB { Center = center, Extents = extents } };;

                RenderingTools.SetSubMesh(glyphs.Length, ref mmi);
            }
        }

        [BurstCompile]
        struct ApplyRefCountDeltasToGlyphTableJob : IJob
        {
            [ReadOnly] public NativeStream.Reader refCountChangeBlocklistA;
            [ReadOnly] public NativeStream.Reader refCountChangeBlocklistB;
            public GlyphTable                     glyphTable;
            public AtlasTable                     atlasTable;
            public bool                           enableAtlasGC;

            public void Execute()
            {
                var count                  = refCountChangeBlocklistA.Count() + refCountChangeBlocklistB.Count();
                var atlasRemovalCandidates = enableAtlasGC ? atlasTable.atlasRemovalCandidates : new NativeHashSet<uint>(count, Allocator.Temp);

                //for (int streamSource = 0; streamSource < 3; streamSource++)  // bug?
                for (int streamSource = 0; streamSource < 2; streamSource++)  // fix for bug?
                {
                    ref var stream = ref refCountChangeBlocklistA;
                    if (streamSource == 1)
                        stream        = ref refCountChangeBlocklistB;
                    int streamIndices = stream.ForEachCount;
                    for (int streamIndex = 0; streamIndex < streamIndices; streamIndex++)
                    {
                        int elementsInIndex = stream.BeginForEachIndex(streamIndex);
                        for (int i = 0; i < elementsInIndex; i++)
                        {
                            var     delta = stream.Read<RefCountChange>();
                            ref var entry = ref glyphTable.GetEntryRW(delta.glyphEntryId);
                            //Debug.Log($"streamsource {streamSource}: Set ref count for glyph {delta.glyphEntryId}: {entry.refCount} + {delta.refCountDelta} = {entry.refCount + delta.refCountDelta} ");
                            //var     oldCount  = delta.refCountDelta;  // bug?
                            var oldCount    = entry.refCount;  // fix for bug?
                            entry.refCount += delta.refCountDelta;
                            if (entry.isInAtlas)
                            {
                                // There can be duplicate entry IDs. So it is possible we decrease the ref count to 0, only to increase it again.
                                // Rather than preduplicate them, we add and remove from a hashset. We only consider entries in the atlas that
                                // had a zero-nonzero change, which makes the set smaller and requires far fewer random accesses.
                                bool wasEmpty = oldCount <= 0;
                                bool isEmpty  = entry.refCount <= 0;
                                if (wasEmpty && !isEmpty)
                                    atlasRemovalCandidates.Remove(delta.glyphEntryId);
                                else if (!wasEmpty && isEmpty)
                                    atlasRemovalCandidates.Add(delta.glyphEntryId);
                            }
                        }
                        stream.EndForEachIndex();
                    }
                }
                if (enableAtlasGC)
                    return;

                // We know for sure that these entry IDs are no longer referenced. Therefore, we can actually remove them.
                var entriesToRemove = atlasRemovalCandidates.ToNativeArray(Allocator.Temp);
                entriesToRemove.Sort();  // Determinism for debugging
                foreach (var id in entriesToRemove)
                {
                    ref var entry         = ref glyphTable.GetEntryRW(id);
                    var     doublePadding = 2 * entry.padding;
                    atlasTable.Free(id, (short)(entry.width + doublePadding), (short)(entry.height + doublePadding), entry.x, entry.y, entry.z);
                    //if (entry.key.format == RenderFormat.SDF8)
                    //    UnityEngine.Debug.Log($"Freeing {entry.x} {entry.y}, width {entry.width}");
                    entry.x = -1;
                    entry.y = -1;
                    entry.z = -1;
                }
            }
        }

        [BurstCompile]
        struct DeallocateResidentsJob : IJob
        {
            [ReadOnly] public NativeStream.Reader residentDeallocationBlocklistA;
            [ReadOnly] public NativeStream.Reader residentDeallocationBlocklistB;
            public GlyphGpuTable                  gpuTable;

            public void Execute()
            {
                gpuTable.residentGaps.AddRange(gpuTable.dispatchDynamicGaps.AsArray());
                gpuTable.dispatchDynamicGaps.Clear();

                for (int streamSource = 0; streamSource < 2; streamSource++)
                {
                    ref var stream = ref residentDeallocationBlocklistA;
                    if (streamSource == 1)
                        stream        = ref residentDeallocationBlocklistB;
                    int streamIndices = stream.ForEachCount;
                    for (int streamIndex = 0; streamIndex < streamIndices; streamIndex++)
                    {
                        int elementsInIndex = stream.BeginForEachIndex(streamIndex);
                        for (int i = 0; i < elementsInIndex; i++)
                        {
                            var range = stream.Read<ResidentRange>();
                            gpuTable.residentGaps.Add(new uint2(range.start, range.count));
                        }
                    }
                }

                var totals                = gpuTable.bufferSize.Value;
                totals                    = GapAllocator.CoalesceGaps(gpuTable.residentGaps, totals);
                gpuTable.bufferSize.Value = totals;
            }
        }
    }
}

