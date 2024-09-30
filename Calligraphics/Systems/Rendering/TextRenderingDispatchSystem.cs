using Latios.Kinemation;
using Latios.Kinemation.Systems;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

// Todo: This system needs to be reworked for more optimal scheduling.

namespace Latios.Calligraphics.Rendering.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(CullingRoundRobinEarlyExtensionsSuperSystem))]
    [DisableAutoCreation]
    public partial struct TextRenderingDispatchSystem : ISystem, ICullingComputeDispatchSystem<TextRenderingDispatchSystem.CollectState,
                                                                                               TextRenderingDispatchSystem.WriteState>
    {
        LatiosWorldUnmanaged latiosWorld;

        UnityObjectRef<ComputeShader> m_uploadGlyphsShader;
        UnityObjectRef<ComputeShader> m_uploadMasksShader;

        EntityQuery m_glyphsQuery;
        EntityQuery m_masksQuery;
        EntityQuery m_allQuery;
        EntityQuery m_glyphsAndMasksQuery;

        CullingComputeDispatchData<CollectState, WriteState> m_data;
        GraphicsBufferBroker                                 graphicsBroker;

        // Shader bindings
        int _src;
        int _dst;
        int _startOffset;
        int _meta;
        int _elementSizeInBytes;

        int _latiosTextBuffer;
        int _latiosTextMaskBuffer;

        internal static GraphicsBufferBroker.StaticID kGlyphsBufferID     = GraphicsBufferBroker.ReservePersistentBuffer();
        internal static GraphicsBufferBroker.StaticID kGlyphMasksBufferID = GraphicsBufferBroker.ReservePersistentBuffer();
        static GraphicsBufferBroker.StaticID          kGlyphsUploadID     = GraphicsBufferBroker.ReserveUploadPool();
        static GraphicsBufferBroker.StaticID          kGlyphMasksUploadID = GraphicsBufferBroker.ReserveUploadPool();

        GraphicsBufferBroker.StaticID m_glyphsBufferID;
        GraphicsBufferBroker.StaticID m_glyphMasksBufferID;
        GraphicsBufferBroker.StaticID m_glyphsUploadID;
        GraphicsBufferBroker.StaticID m_glyphMasksUploadID;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_data = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorld);

            m_glyphsQuery = state.Fluent().With<RenderGlyph, TextRenderControl, RenderBounds>(true).With<TextShaderIndex>(false)
                            .With<ChunkPerDispatchCullingMask, ChunkPerFrameCullingMask>(true,  true).Without<GpuResidentTextTag>().Build();
            m_masksQuery = state.Fluent().With<TextMaterialMaskShaderIndex>(false).With<RenderBounds, RenderGlyphMask>(true)
                           .With<ChunkPerDispatchCullingMask, ChunkPerFrameCullingMask>(true,  true).Without<GpuResidentTextTag>().Build();
            m_allQuery = state.Fluent().WithAnyEnabled<TextShaderIndex, TextMaterialMaskShaderIndex>(true).With<RenderBounds>(true)
                         .With<ChunkPerDispatchCullingMask>(                          false, true).With<ChunkPerFrameCullingMask>(true, true)
                         .Without<GpuResidentTextTag>().Build();
            m_glyphsAndMasksQuery = state.Fluent().With<RenderGlyph, TextRenderControl, RenderBounds>(true)
                                    .With<TextShaderIndex, TextMaterialMaskShaderIndex, RenderGlyphMask>(true)
                                    .With<AdditionalFontMaterialEntity>(                                 true)
                                    .With<ChunkPerDispatchCullingMask, ChunkPerFrameCullingMask>(        true, true).Without<GpuResidentTextTag>().Build();

            var copyByteAddressShader = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("CopyBytes");
            m_uploadGlyphsShader      = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("UploadGlyphs");
            m_uploadMasksShader       = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("UploadBytes");
            _src                      = Shader.PropertyToID("_src");
            _dst                      = Shader.PropertyToID("_dst");
            _startOffset              = Shader.PropertyToID("_startOffset");
            _meta                     = Shader.PropertyToID("_meta");
            _elementSizeInBytes       = Shader.PropertyToID("_elementSizeInBytes");
            _latiosTextBuffer         = Shader.PropertyToID("_latiosTextBuffer");
            _latiosTextMaskBuffer     = Shader.PropertyToID("_latiosTextMaskBuffer");

            if (!latiosWorld.worldBlackboardEntity.HasComponent<GraphicsBufferBroker>())
                throw new System.InvalidOperationException("Calligraphics must be installed after Kinemation.");
            graphicsBroker = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();
            graphicsBroker.InitializePersistentBuffer(kGlyphsBufferID, 128 * 96, 4, GraphicsBuffer.Target.Raw, copyByteAddressShader);
            graphicsBroker.InitializeUploadPool(kGlyphsUploadID, 4, GraphicsBuffer.Target.Raw);
            graphicsBroker.InitializePersistentBuffer(kGlyphMasksBufferID, 128 * 4, 4, GraphicsBuffer.Target.Raw, copyByteAddressShader);
            graphicsBroker.InitializeUploadPool(kGlyphMasksUploadID, 4, GraphicsBuffer.Target.Raw);

            m_glyphsBufferID     = kGlyphsBufferID;
            m_glyphMasksBufferID = kGlyphMasksBufferID;
            m_glyphsUploadID     = kGlyphsUploadID;
            m_glyphMasksUploadID = kGlyphMasksUploadID;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state) => m_data.DoUpdate(ref state, ref this);

        public CollectState Collect(ref SystemState state)
        {
            var   materials             = latiosWorld.worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>().AsNativeArray();
            int   textIndex             = materials.IndexOf(ComponentType.ReadOnly<TextShaderIndex>());
            ulong textMaterialMaskLower = (ulong)textIndex >= 64UL ? 0UL : (1UL << textIndex);
            ulong textMaterialMaskUpper = (ulong)textIndex >= 64UL ? (1UL << (textIndex - 64)) : 0UL;
            int   fontIndex             = materials.IndexOf(ComponentType.ReadOnly<TextMaterialMaskShaderIndex>());
            ulong fontMaterialMaskLower = (ulong)fontIndex >= 64UL ? 0UL : (1U << fontIndex);
            ulong fontMaterialMaskUpper = (ulong)fontIndex >= 64UL ? (1UL << (fontIndex - 64)) : 0UL;

            var materialMasksJh = new UpdateMaterialMasksJob
            {
                glyphMasksHandle                = SystemAPI.GetBufferTypeHandle<RenderGlyphMask>(true),
                glyphMaterialMaskLower          = textMaterialMaskLower,
                glyphMaterialMaskUpper          = textMaterialMaskUpper,
                glyphsHandle                    = SystemAPI.GetBufferTypeHandle<RenderGlyph>(true),
                maskMaterialMaskLower           = fontMaterialMaskLower,
                maskMaterialMaskUpper           = fontMaterialMaskUpper,
                materialPropertyDirtyMaskHandle = SystemAPI.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                perDispatchMaskHandle           = SystemAPI.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(false),
                perFrameMaskHandle              = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true)
            }.ScheduleParallel(m_allQuery, state.Dependency);

            var foundChildrenDependenciesJh = materialMasksJh;
            var glyphsWithChildrenCount     = m_glyphsAndMasksQuery.CalculateChunkCountWithoutFiltering();
            var map                         = new NativeParallelHashMap<ArchetypeChunk, v128>(glyphsWithChildrenCount, state.WorldUpdateAllocator);
            if (glyphsWithChildrenCount > 0)
            {
                foundChildrenDependenciesJh = new FindCulledGlyphHoldersWithVisibleChildrenJob
                {
                    additionalEntitiesHandle = SystemAPI.GetBufferTypeHandle<AdditionalFontMaterialEntity>(true),
                    esil                     = SystemAPI.GetEntityStorageInfoLookup(),
                    map                      = map.AsParallelWriter(),
                    perDispatchMaskHandle    = SystemAPI.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true),
                    perFrameMaskHandle       = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true)
                }.ScheduleParallel(m_glyphsAndMasksQuery, materialMasksJh);
            }

            var glyphStreamCount       = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator);
            glyphStreamCount[0]        = m_glyphsQuery.CalculateChunkCountWithoutFiltering();
            var glyphStreamConstructJh = NativeStream.ScheduleConstruct(out var glyphStream, glyphStreamCount, default, state.WorldUpdateAllocator);
            var collectGlyphsJh        = new GatherGlyphUploadOperationsJob
            {
                additonalEntitiesHandle     = SystemAPI.GetBufferTypeHandle<AdditionalFontMaterialEntity>(true),
                glyphCountThisFrameLookup   = SystemAPI.GetComponentLookup<GlyphCountThisFrame>(false),
                glyphCountThisPass          = 0,
                glyphsHandle                = SystemAPI.GetBufferTypeHandle<RenderGlyph>(true),
                glyphMaskHandle             = SystemAPI.GetBufferTypeHandle<RenderGlyphMask>(true),
                gpuResidentGlyphCountLookup = SystemAPI.GetComponentLookup<GpuResidentGlyphCount>(true),
                map                         = map,
                perDispatchMaskHandle       = SystemAPI.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true),
                perFrameMaskHandle          = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                streamWriter                = glyphStream.AsWriter(),
                textShaderIndexHandle       = SystemAPI.GetComponentTypeHandle<TextShaderIndex>(false),
                trcHandle                   = SystemAPI.GetComponentTypeHandle<TextRenderControl>(true),
                worldBlackboardEntity       = latiosWorld.worldBlackboardEntity
            }.Schedule(m_glyphsQuery, JobHandle.CombineDependencies(glyphStreamConstructJh, foundChildrenDependenciesJh));

            var glyphPayloads                 = new NativeList<UploadPayload>(1, state.WorldUpdateAllocator);
            var requiredGlyphUploadBufferSize = new NativeReference<uint>(state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var finalFirstPhaseJh             = new MapPayloadsToUploadBufferJob
            {
                streamReader             = glyphStream.AsReader(),
                payloads                 = glyphPayloads,
                requiredUploadBufferSize = requiredGlyphUploadBufferSize
            }.Schedule(collectGlyphsJh);

            var maskPayloads                 = new NativeList<UploadPayload>(1, state.WorldUpdateAllocator);
            var requiredMaskUploadBufferSize = new NativeReference<uint>(state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

            if (glyphsWithChildrenCount > 0)
            {
                var maskStreamCount       = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator);
                maskStreamCount[0]        = m_masksQuery.CalculateChunkCountWithoutFiltering();
                var maskStreamConstructJh = NativeStream.ScheduleConstruct(out var maskStream, maskStreamCount, default, state.WorldUpdateAllocator);

                var collectMasksJh = new GatherMaskUploadOperationsJob
                {
                    glyphMasksHandle           = SystemAPI.GetBufferTypeHandle<RenderGlyphMask>(true),
                    gpuResidentMaskCountLookup = SystemAPI.GetComponentLookup<GpuResidentMaskCount>(true),
                    maskCountThisFrameLookup   = SystemAPI.GetComponentLookup<MaskCountThisFrame>(false),
                    maskCountThisPass          = 0,
                    maskShaderIndexHandle      = SystemAPI.GetComponentTypeHandle<TextMaterialMaskShaderIndex>(false),
                    perDispatchMaskHandle      = SystemAPI.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true),
                    perFrameMaskHandle         = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                    streamWriter               = maskStream.AsWriter(),
                    worldBlackboardEntity      = latiosWorld.worldBlackboardEntity
                }.Schedule(m_masksQuery, JobHandle.CombineDependencies(maskStreamConstructJh, materialMasksJh));

                var batchMasksJh = new MapPayloadsToUploadBufferJob
                {
                    streamReader             = maskStream.AsReader(),
                    payloads                 = maskPayloads,
                    requiredUploadBufferSize = requiredMaskUploadBufferSize
                }.Schedule(collectMasksJh);

                var copyPropertiesJh = new CopyGlyphShaderIndicesJob
                {
                    additionalEntitiesHandle = SystemAPI.GetBufferTypeHandle<AdditionalFontMaterialEntity>(true),
                    perDispatchMaskHandle    = SystemAPI.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true),
                    perFrameMaskHandle       = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                    shaderIndexHandle        = SystemAPI.GetComponentTypeHandle<TextShaderIndex>(true),
                    renderGlyphMaskLookup    = SystemAPI.GetBufferLookup<RenderGlyphMask>(true),
                    shaderIndexLookup        = SystemAPI.GetComponentLookup<TextShaderIndex>(false)
                }.ScheduleParallel(m_glyphsAndMasksQuery, collectGlyphsJh);

                finalFirstPhaseJh = JobHandle.CombineDependencies(finalFirstPhaseJh, batchMasksJh, copyPropertiesJh);
            }

            state.Dependency = finalFirstPhaseJh;

            return new CollectState
            {
                glyphPayloads                 = glyphPayloads,
                requiredGlyphUploadBufferSize = requiredGlyphUploadBufferSize,
                maskPayloads                  = maskPayloads,
                requiredMaskUploadBufferSize  = requiredMaskUploadBufferSize,
                glyphsWithChildrenCount       = glyphsWithChildrenCount
            };
        }

        public WriteState Write(ref SystemState state, ref CollectState collectState)
        {
            if (collectState.glyphPayloads.IsEmpty)
            {
                // skip rest of loop.
                return default;
            }

            var glyphPayloads                 = collectState.glyphPayloads;
            var requiredGlyphUploadBufferSize = collectState.requiredGlyphUploadBufferSize.Value;

            var glyphUploadBuffer = graphicsBroker.GetUploadBuffer(m_glyphsUploadID, math.max(requiredGlyphUploadBufferSize, 128) * 24);
            var glyphMetaBuffer   = graphicsBroker.GetMetaUint4UploadBuffer((uint)glyphPayloads.Length);

            var finalSecondPhaseJh = new WriteGlyphsUploadsToBuffersJob
            {
                payloads           = glyphPayloads.AsDeferredJobArray(),
                glyphsUploadBuffer = glyphUploadBuffer.LockBufferForWrite<RenderGlyph>(0, (int)requiredGlyphUploadBufferSize),
                metaUploadBuffer   = glyphMetaBuffer.LockBufferForWrite<uint4>(0, glyphPayloads.Length)
            }.Schedule(glyphPayloads, 1, state.Dependency);

            var maskPayloads                 = collectState.maskPayloads;
            var requiredMaskUploadBufferSize = collectState.requiredMaskUploadBufferSize.Value;

            GraphicsBufferUnmanaged maskUploadBuffer = default;
            GraphicsBufferUnmanaged maskMetaBuffer   = default;

            if (collectState.glyphsWithChildrenCount > 0)
            {
                maskUploadBuffer = graphicsBroker.GetUploadBuffer(m_glyphMasksUploadID, math.max(requiredMaskUploadBufferSize, 128));
                maskMetaBuffer   = graphicsBroker.GetMetaUint3UploadBuffer((uint)maskPayloads.Length);

                var maskJh = new WriteMasksUploadsToBuffersJob
                {
                    payloads          = maskPayloads.AsDeferredJobArray(),
                    masksUploadBuffer = maskUploadBuffer.LockBufferForWrite<uint>(0, (int)requiredMaskUploadBufferSize),
                    metaUploadBuffer  = maskMetaBuffer.LockBufferForWrite<uint3>(0, maskPayloads.Length)
                }.Schedule(maskPayloads, 1, state.Dependency);

                finalSecondPhaseJh = JobHandle.CombineDependencies(finalSecondPhaseJh, maskJh);
            }
            state.Dependency = finalSecondPhaseJh;

            return new WriteState
            {
                glyphMetaBuffer               = glyphMetaBuffer,
                glyphUploadBuffer             = glyphUploadBuffer,
                maskMetaBuffer                = maskMetaBuffer,
                maskUploadBuffer              = maskUploadBuffer,
                requiredGlyphUploadBufferSize = requiredGlyphUploadBufferSize,
                requiredMaskUploadBufferSize  = requiredMaskUploadBufferSize,
                glyphPayloads                 = glyphPayloads,
                maskPayloads                  = maskPayloads,
                glyphsWithChildrenCount       = collectState.glyphsWithChildrenCount
            };
        }

        public void Dispatch(ref SystemState state, ref WriteState writeState)
        {
            if (!writeState.glyphPayloads.IsCreated)
                return;

            var glyphUploadBuffer             = writeState.glyphUploadBuffer;
            var glyphMetaBuffer               = writeState.glyphMetaBuffer;
            var requiredGlyphUploadBufferSize = writeState.requiredGlyphUploadBufferSize;
            var glyphPayloads                 = writeState.glyphPayloads;

            var maskUploadBuffer             = writeState.maskUploadBuffer;
            var maskMetaBuffer               = writeState.maskMetaBuffer;
            var requiredMaskUploadBufferSize = writeState.requiredMaskUploadBufferSize;
            var maskPayloads                 = writeState.maskPayloads;

            glyphUploadBuffer.UnlockBufferAfterWrite<RenderGlyph>((int)requiredGlyphUploadBufferSize);
            glyphMetaBuffer.UnlockBufferAfterWrite<uint4>(glyphPayloads.Length);

            if (writeState.glyphsWithChildrenCount > 0)
            {
                maskUploadBuffer.UnlockBufferAfterWrite<uint>((int)requiredMaskUploadBufferSize);
                maskMetaBuffer.UnlockBufferAfterWrite<uint3>((int)maskPayloads.Length);
            }

            var frameGlyphCount       = latiosWorld.worldBlackboardEntity.GetComponentData<GlyphCountThisFrame>().glyphCount;
            var gpuResidentGlyphCount = latiosWorld.worldBlackboardEntity.GetComponentData<GpuResidentGlyphCount>().glyphCount;
            var persistentGlyphBuffer = graphicsBroker.GetPersistentBuffer(m_glyphsBufferID, math.max(frameGlyphCount + gpuResidentGlyphCount, 128) * 24);
            m_uploadGlyphsShader.SetBuffer(0, _dst,  persistentGlyphBuffer);
            m_uploadGlyphsShader.SetBuffer(0, _src,  glyphUploadBuffer);
            m_uploadGlyphsShader.SetBuffer(0, _meta, glyphMetaBuffer);

            for (uint dispatchesRemaining = (uint)glyphPayloads.Length, offset = 0; dispatchesRemaining > 0;)
            {
                uint dispatchCount = math.min(dispatchesRemaining, 65535);
                m_uploadGlyphsShader.SetInt(_startOffset, (int)offset);
                m_uploadGlyphsShader.Dispatch(0, (int)dispatchCount, 1, 1);
                offset              += dispatchCount;
                dispatchesRemaining -= dispatchCount;
            }
            GraphicsUnmanaged.SetGlobalBuffer(_latiosTextBuffer, persistentGlyphBuffer);

            var frameMaskCount       = latiosWorld.worldBlackboardEntity.GetComponentData<MaskCountThisFrame>().maskCount;
            var gpuResidentMaskCount = latiosWorld.worldBlackboardEntity.GetComponentData<GpuResidentMaskCount>().maskCount;
            var persistentMaskBuffer = graphicsBroker.GetPersistentBuffer(m_glyphMasksBufferID, math.max(frameMaskCount + gpuResidentMaskCount, 128));

            if (writeState.glyphsWithChildrenCount > 0)
            {
                m_uploadMasksShader.SetBuffer(0, _dst,  persistentMaskBuffer);
                m_uploadMasksShader.SetBuffer(0, _src,  maskUploadBuffer);
                m_uploadMasksShader.SetBuffer(0, _meta, maskMetaBuffer);
                m_uploadMasksShader.SetInt(_elementSizeInBytes, 4);

                for (uint dispatchesRemaining = (uint)maskPayloads.Length, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_uploadMasksShader.SetInt(_startOffset, (int)offset);
                    m_uploadMasksShader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }
            }
            GraphicsUnmanaged.SetGlobalBuffer(_latiosTextMaskBuffer, persistentMaskBuffer);
        }

        public struct CollectState
        {
            internal NativeList<UploadPayload> glyphPayloads;
            internal NativeReference<uint>     requiredGlyphUploadBufferSize;
            internal NativeList<UploadPayload> maskPayloads;
            internal NativeReference<uint>     requiredMaskUploadBufferSize;
            internal int                       glyphsWithChildrenCount;
        }

        public struct WriteState
        {
            internal GraphicsBufferUnmanaged   glyphUploadBuffer;
            internal GraphicsBufferUnmanaged   glyphMetaBuffer;
            internal NativeList<UploadPayload> glyphPayloads;
            internal uint                      requiredGlyphUploadBufferSize;
            internal GraphicsBufferUnmanaged   maskUploadBuffer;
            internal GraphicsBufferUnmanaged   maskMetaBuffer;
            internal NativeList<UploadPayload> maskPayloads;
            internal uint                      requiredMaskUploadBufferSize;
            internal int                       glyphsWithChildrenCount;
        }

        internal unsafe struct UploadPayload
        {
            public void* ptr;
            public uint  length;
            public uint  persistentBufferStart;
            public uint  uploadBufferStart;
            public uint  controls;
        }

        // Schedule Parallel
        [BurstCompile]
        struct UpdateMaterialMasksJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask> perFrameMaskHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyph>                 glyphsHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyphMask>             glyphMasksHandle;
            public ComponentTypeHandle<ChunkPerDispatchCullingMask>         perDispatchMaskHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>      materialPropertyDirtyMaskHandle;

            public ulong glyphMaterialMaskLower;
            public ulong glyphMaterialMaskUpper;
            public ulong maskMaterialMaskLower;
            public ulong maskMaterialMaskUpper;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var dispatchMask = ref chunk.GetChunkComponentRefRW(ref perDispatchMaskHandle);
                var     frameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var     lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var     upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return;

                ref var dirtyMask = ref chunk.GetChunkComponentRefRW(ref materialPropertyDirtyMaskHandle);

                var glyphMasksBuffers = chunk.GetBufferAccessor(ref glyphMasksHandle);
                if (glyphMasksBuffers.Length > 0)
                {
                    var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        var buffer = glyphMasksBuffers[i];
                        if (buffer.Length == 0)
                        {
                            ref var bitHolder = ref i >= 64 ? ref dispatchMask.upper : ref dispatchMask.lower;
                            bitHolder.SetBits(i % 64, false);
                        }
                    }
                    if ((dispatchMask.upper.Value | dispatchMask.lower.Value) != 0)
                    {
                        dirtyMask.lower.Value |= maskMaterialMaskLower;
                        dirtyMask.upper.Value |= maskMaterialMaskUpper;
                        dirtyMask.lower.Value |= glyphMaterialMaskLower;
                        dirtyMask.upper.Value |= glyphMaterialMaskUpper;
                    }
                }
                else
                {
                    var glyphsBuffers = chunk.GetBufferAccessor(ref glyphsHandle);
                    var enumerator    = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        var buffer = glyphsBuffers[i];
                        if (buffer.Length == 0)
                        {
                            ref var bitHolder = ref i >= 64 ? ref dispatchMask.upper : ref dispatchMask.lower;
                            bitHolder.SetBits(i % 64, false);
                        }
                    }
                    if ((dispatchMask.upper.Value | dispatchMask.lower.Value) != 0)
                    {
                        dirtyMask.lower.Value |= glyphMaterialMaskLower;
                        dirtyMask.upper.Value |= glyphMaterialMaskUpper;
                    }
                }
            }
        }

        // Schedule Parallel
        [BurstCompile]
        struct FindCulledGlyphHoldersWithVisibleChildrenJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [ReadOnly] public BufferTypeHandle<AdditionalFontMaterialEntity>   additionalEntitiesHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchMaskHandle;
            [ReadOnly] public EntityStorageInfoLookup                          esil;

            public NativeParallelHashMap<ArchetypeChunk, v128>.ParallelWriter map;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var dispatchMask     = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                var frameMask        = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower            = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var upper            = dispatchMask.upper.Value & (~frameMask.upper.Value);
                upper                = ~upper;
                lower                = ~lower;
                BitField64 lowerMask = default;
                lowerMask.SetBits(0, true, math.min(chunk.Count, 64));
                BitField64 upperMask = default;
                if (chunk.Count > 64)
                    upperMask.SetBits(0, true, chunk.Count - 64);
                upper &= upperMask.Value;
                lower &= lowerMask.Value;

                if ((upper | lower) == 0)
                    return;

                var entitiesBuffers = chunk.GetBufferAccessor(ref additionalEntitiesHandle);
                var enumerator      = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    bool survive = false;
                    foreach (var entity in entitiesBuffers[i])
                    {
                        var info              = esil[entity.entity];
                        var childDispatchMask = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                        var childFrameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                        if (info.IndexInChunk >= 64)
                        {
                            if (!childFrameMask.upper.IsSet(info.IndexInChunk - 64) && childDispatchMask.upper.IsSet(info.IndexInChunk - 64))
                            {
                                survive = true;
                                break;
                            }
                        }
                        else
                        {
                            if (!childFrameMask.lower.IsSet(info.IndexInChunk) && childDispatchMask.lower.IsSet(info.IndexInChunk))
                            {
                                survive = true;
                                break;
                            }
                        }
                    }
                    if (!survive)
                    {
                        if (i >= 64)
                            upper ^= 1u << (i - 64);
                        else
                            lower ^= 1u << i;
                    }
                }
                if ((upper | lower) != 0)
                {
                    map.TryAdd(chunk, new v128(lower, upper));
                }
            }
        }

        // Schedule Single
        [BurstCompile]
        struct GatherGlyphUploadOperationsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<TextRenderControl>           trcHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyph>                    glyphsHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyphMask>                glyphMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchMaskHandle;
            [ReadOnly] public BufferTypeHandle<AdditionalFontMaterialEntity>   additonalEntitiesHandle;
            [ReadOnly] public NativeParallelHashMap<ArchetypeChunk, v128>      map;
            [ReadOnly] public ComponentLookup<GpuResidentGlyphCount>           gpuResidentGlyphCountLookup;
            public ComponentTypeHandle<TextShaderIndex>                        textShaderIndexHandle;
            public ComponentLookup<GlyphCountThisFrame>                        glyphCountThisFrameLookup;
            public Entity                                                      worldBlackboardEntity;

            public uint glyphCountThisPass;

            [NativeDisableParallelForRestriction] public NativeStream.Writer streamWriter;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var dispatchMask = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                var frameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);

                if (chunk.Has(ref additonalEntitiesHandle))
                {
                    // If any child is being rendered, we still need to upload glyphs.
                    if (map.TryGetValue(chunk, out var extraBits))
                    {
                        lower |= extraBits.ULong0;
                        upper |= extraBits.ULong1;
                    }
                }
                if ((upper | lower) == 0)
                    return;

                ref var glyphCountThisFrame   = ref glyphCountThisFrameLookup.GetRefRW(worldBlackboardEntity).ValueRW.glyphCount;
                var     gpuResidentGlyphCount = gpuResidentGlyphCountLookup[worldBlackboardEntity].glyphCount;

                streamWriter.BeginForEachIndex(unfilteredChunkIndex);
                var trcs          = chunk.GetNativeArray(ref trcHandle);
                var glyphsBuffers = chunk.GetBufferAccessor(ref glyphsHandle);
                var masksBuffers  = chunk.GetBufferAccessor(ref glyphMaskHandle);
                var shaderIndices = chunk.GetNativeArray(ref textShaderIndexHandle);

                var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var trc        = trcs[i].flags;
                    var buffer     = glyphsBuffers[i];
                    int glyphCount = buffer.Length;
                    if (masksBuffers.Length > 0)
                    {
                        glyphCount = masksBuffers[i].Length * 16;
                    }
                    shaderIndices[i] = new TextShaderIndex
                    {
                        firstGlyphIndex = glyphCountThisFrame + gpuResidentGlyphCount,
                        glyphCount      = (uint)glyphCount
                    };

                    streamWriter.Write(new UploadPayload
                    {
                        ptr                   = buffer.GetUnsafeReadOnlyPtr(),
                        length                = (uint)buffer.Length,
                        uploadBufferStart     = glyphCountThisPass,
                        persistentBufferStart = glyphCountThisFrame + gpuResidentGlyphCount,
                        controls              = (uint)trc
                    });
                    glyphCountThisPass  += (uint)buffer.Length;
                    glyphCountThisFrame += (uint)buffer.Length;
                }

                streamWriter.EndForEachIndex();
            }
        }

        // Schedule Single
        [BurstCompile]
        struct GatherMaskUploadOperationsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyphMask>                glyphMasksHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchMaskHandle;
            [ReadOnly] public ComponentLookup<GpuResidentMaskCount>            gpuResidentMaskCountLookup;
            public ComponentTypeHandle<TextMaterialMaskShaderIndex>            maskShaderIndexHandle;
            public ComponentLookup<MaskCountThisFrame>                         maskCountThisFrameLookup;
            public Entity                                                      worldBlackboardEntity;

            public uint maskCountThisPass;

            [NativeDisableParallelForRestriction] public NativeStream.Writer streamWriter;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var dispatchMask = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                var frameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return;

                ref var maskCountThisFrame   = ref maskCountThisFrameLookup.GetRefRW(worldBlackboardEntity).ValueRW.maskCount;
                var     gpuResidentMaskCount = gpuResidentMaskCountLookup[worldBlackboardEntity].maskCount;

                streamWriter.BeginForEachIndex(unfilteredChunkIndex);
                var glyphMasksBuffers = chunk.GetBufferAccessor(ref glyphMasksHandle);
                var shaderIndices     = chunk.GetNativeArray(ref maskShaderIndexHandle);

                var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var buffer       = glyphMasksBuffers[i];
                    shaderIndices[i] = new TextMaterialMaskShaderIndex
                    {
                        firstMaskIndex = maskCountThisFrame + gpuResidentMaskCount
                    };

                    streamWriter.Write(new UploadPayload
                    {
                        ptr                   = buffer.GetUnsafeReadOnlyPtr(),
                        length                = (uint)buffer.Length,
                        uploadBufferStart     = maskCountThisPass,
                        persistentBufferStart = maskCountThisFrame + gpuResidentMaskCount,
                        controls              = 0
                    });
                    maskCountThisPass  += (uint)buffer.Length;
                    maskCountThisFrame += (uint)buffer.Length;
                }

                streamWriter.EndForEachIndex();
            }
        }

        // Schedule Parallel
        [BurstCompile]
        struct CopyGlyphShaderIndicesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>                   perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask>                perDispatchMaskHandle;
            [ReadOnly] public ComponentTypeHandle<TextShaderIndex>                            shaderIndexHandle;
            [ReadOnly] public BufferTypeHandle<AdditionalFontMaterialEntity>                  additionalEntitiesHandle;
            [ReadOnly] public BufferLookup<RenderGlyphMask>                                   renderGlyphMaskLookup;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<TextShaderIndex> shaderIndexLookup;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var dispatchMask = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                var frameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
                if ((upper | lower) == 0)
                    return;

                var additionalEntitiesBuffers = chunk.GetBufferAccessor(ref additionalEntitiesHandle);
                var shaderIndices             = chunk.GetNativeArray(ref shaderIndexHandle);

                var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var indices = shaderIndices[i];
                    foreach (var entity in additionalEntitiesBuffers[i])
                    {
                        var maskBuffer                   = renderGlyphMaskLookup[entity.entity];
                        indices.glyphCount               = (uint)(16 * maskBuffer.Length);
                        shaderIndexLookup[entity.entity] = indices;
                    }
                }
            }
        }

        [BurstCompile]
        struct MapPayloadsToUploadBufferJob : IJob
        {
            [ReadOnly] public NativeStream.Reader streamReader;
            public NativeList<UploadPayload>      payloads;
            public NativeReference<uint>          requiredUploadBufferSize;

            public void Execute()
            {
                var totalCount    = streamReader.Count();
                payloads.Capacity = totalCount;
                var  streamCount  = streamReader.ForEachCount;
                uint sum          = 0;  // Prefixing is done in previous job.

                for (int streamIndex = 0; streamIndex < streamCount; streamIndex++)
                {
                    var count = streamReader.BeginForEachIndex(streamIndex);
                    for (int i = 0; i < count; i++)
                    {
                        var payload  = streamReader.Read<UploadPayload>();
                        sum         += payload.length;
                        payloads.AddNoResize(payload);
                    }
                }

                requiredUploadBufferSize.Value = sum;
            }
        }

        // Schedule Parallel
        [BurstCompile]
        struct WriteGlyphsUploadsToBuffersJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<UploadPayload>                          payloads;
            public NativeArray<uint4>                                             metaUploadBuffer;
            [NativeDisableParallelForRestriction] public NativeArray<RenderGlyph> glyphsUploadBuffer;

            public unsafe void Execute(int index)
            {
                var payload             = payloads[index];
                metaUploadBuffer[index] = new uint4(payload.uploadBufferStart, payload.persistentBufferStart, payload.length, payload.controls);
                var dstPtr              = glyphsUploadBuffer.GetSubArray((int)payload.uploadBufferStart, (int)payload.length).GetUnsafePtr();
                UnsafeUtility.MemCpy(dstPtr, payload.ptr, sizeof(RenderGlyph) * payload.length);
            }
        }

        // Schedule Parallel
        [BurstCompile]
        struct WriteMasksUploadsToBuffersJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<UploadPayload>                   payloads;
            public NativeArray<uint3>                                      metaUploadBuffer;
            [NativeDisableParallelForRestriction] public NativeArray<uint> masksUploadBuffer;

            public unsafe void Execute(int index)
            {
                var payload             = payloads[index];
                metaUploadBuffer[index] = new uint3(payload.uploadBufferStart, payload.persistentBufferStart, payload.length);
                var dstPtr              = masksUploadBuffer.GetSubArray((int)payload.uploadBufferStart, (int)payload.length).GetUnsafePtr();
                UnsafeUtility.MemCpy(dstPtr, payload.ptr, sizeof(uint) * payload.length);
            }
        }
    }
}

