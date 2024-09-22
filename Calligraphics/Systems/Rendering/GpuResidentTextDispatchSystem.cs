using System.Collections.Generic;
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

namespace Latios.Calligraphics.Rendering.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(CullingRoundRobinEarlyExtensionsSuperSystem))]
    [UpdateBefore(typeof(TextRenderingDispatchSystem))]
    [DisableAutoCreation]
    public partial struct GpuResidentTextDispatchSystem : ISystem, ICullingComputeDispatchSystem<GpuResidentTextDispatchSystem.CollectState,
                                                                                                 GpuResidentTextDispatchSystem.WriteState>
    {
        LatiosWorldUnmanaged latiosWorld;

        DynamicComponentTypeHandle m_gpuUpdateFlagDynamicHandle;

        UnityObjectRef<ComputeShader> m_uploadGlyphsShader;
        UnityObjectRef<ComputeShader> m_uploadMasksShader;

        EntityQuery m_newGlyphsQuery;
        EntityQuery m_newMasksQuery;
        EntityQuery m_changedGlyphsQuery;
        EntityQuery m_changedMasksQuery;
        EntityQuery m_newAndChangedGlyphsWithMasksQuery;
        EntityQuery m_deadQuery;
        EntityQuery m_allQuery;
        EntityQuery m_newQuery;

        NativeList<uint2> m_glyphGaps;
        NativeList<uint2> m_maskGaps;

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

        static GraphicsBufferBroker.StaticID kGlyphsBufferID     = TextRenderingDispatchSystem.kGlyphsBufferID;
        static GraphicsBufferBroker.StaticID kGlyphsUploadID     = GraphicsBufferBroker.ReserveUploadPool();
        static GraphicsBufferBroker.StaticID kGlyphMasksBufferID = TextRenderingDispatchSystem.kGlyphMasksBufferID;
        static GraphicsBufferBroker.StaticID kGlyphMasksUploadID = GraphicsBufferBroker.ReserveUploadPool();

        GraphicsBufferBroker.StaticID m_glyphsBufferID;
        GraphicsBufferBroker.StaticID m_glyphMasksBufferID;
        GraphicsBufferBroker.StaticID m_glyphsUploadID;
        GraphicsBufferBroker.StaticID m_glyphMasksUploadID;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_data = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorld);

            m_gpuUpdateFlagDynamicHandle = state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<GpuResidentUpdateFlag>());

            m_newGlyphsQuery = state.Fluent().With<RenderGlyph, TextRenderControl, RenderBounds>(true)
                               .With<GpuResidentTextTag>( true)
                               .With<TextShaderIndex>(    false)
                               .Without<GpuResidentAllocation>().Build();
            m_newMasksQuery = state.Fluent().With<TextMaterialMaskShaderIndex>(false)
                              .With<RenderBounds, RenderGlyphMask, GpuResidentTextTag>(true)
                              .Without<GpuResidentAllocation>().Build();
            m_changedGlyphsQuery = state.Fluent().With<RenderGlyph, TextRenderControl, RenderBounds>(true)
                                   .With<GpuResidentTextTag>(                     true)
                                   .With<TextShaderIndex, GpuResidentAllocation>( false)
                                   .WithEnabled<GpuResidentUpdateFlag>(true).Build();
            m_changedMasksQuery = state.Fluent().With<RenderBounds, RenderGlyphMask, GpuResidentTextTag>(true)
                                  .With<TextMaterialMaskShaderIndex, GpuResidentAllocation>(false)
                                  .WithEnabled<GpuResidentUpdateFlag>(true).Build();
            m_newAndChangedGlyphsWithMasksQuery = state.Fluent().With<RenderGlyph, TextRenderControl, RenderBounds>(true)
                                                  .With<GpuResidentTextTag, AdditionalFontMaterialEntity>(true)
                                                  .With<TextShaderIndex>(                                 false).Build();
            m_deadQuery = state.Fluent().With<GpuResidentAllocation>(true).Without<GpuResidentTextTag>().Build();
            m_allQuery  = state.Fluent().WithAnyEnabled<TextShaderIndex, TextMaterialMaskShaderIndex>(true)
                          .With<RenderBounds, GpuResidentTextTag>(                true).Build();
            m_newQuery = state.Fluent().With<TextShaderIndex, TextRenderControl, RenderBounds>(true)
                         .With<GpuResidentTextTag>(                              true)
                         .WithAnyEnabled<RenderGlyph, RenderGlyphMask>(true)
                         .Without<GpuResidentAllocation>().Build();

            m_uploadGlyphsShader  = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("UploadGlyphs");
            m_uploadMasksShader   = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("UploadBytes");
            _src                  = Shader.PropertyToID("_src");
            _dst                  = Shader.PropertyToID("_dst");
            _startOffset          = Shader.PropertyToID("_startOffset");
            _meta                 = Shader.PropertyToID("_meta");
            _elementSizeInBytes   = Shader.PropertyToID("_elementSizeInBytes");
            _latiosTextBuffer     = Shader.PropertyToID("_latiosTextBuffer");
            _latiosTextMaskBuffer = Shader.PropertyToID("_latiosTextMaskBuffer");

            m_glyphGaps = new NativeList<uint2>(Allocator.Persistent);
            m_maskGaps  = new NativeList<uint2>(Allocator.Persistent);

            latiosWorld.worldBlackboardEntity.AddComponent<GpuResidentGlyphCount>();
            latiosWorld.worldBlackboardEntity.AddComponentData(new GpuResidentMaskCount { maskCount = 1});

            if (!latiosWorld.worldBlackboardEntity.HasComponent<GraphicsBufferBroker>())
                throw new System.InvalidOperationException("Calligraphics must be installed after Kinemation.");
            graphicsBroker = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();
            graphicsBroker.InitializeUploadPool(kGlyphsUploadID,     4, GraphicsBuffer.Target.Raw);
            graphicsBroker.InitializeUploadPool(kGlyphMasksUploadID, 4, GraphicsBuffer.Target.Raw);

            m_glyphsBufferID     = kGlyphsBufferID;
            m_glyphMasksBufferID = kGlyphMasksBufferID;
            m_glyphsUploadID     = kGlyphsUploadID;
            m_glyphMasksUploadID = kGlyphsUploadID;
        }

        public void OnDestroy(ref SystemState state)
        {
            m_glyphGaps.Dispose();
            m_maskGaps.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state) => m_data.DoUpdate(ref state, ref this);

        public CollectState Collect(ref SystemState state)
        {
            // Skip after the first camera in this frame.
            int dispatchPassIndex = latiosWorld.worldBlackboardEntity.GetComponentData<DispatchContext>().dispatchIndexThisFrame;
            if (dispatchPassIndex != 0)
            {
                return default;
            }

            var   materials             = latiosWorld.worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>().AsNativeArray();
            int   textIndex             = materials.IndexOf(ComponentType.ReadOnly<TextShaderIndex>());
            ulong textMaterialMaskLower = (ulong)textIndex >= 64UL ? 0UL : (1UL << textIndex);
            ulong textMaterialMaskUpper = (ulong)textIndex >= 64UL ? (1UL << (textIndex - 64)) : 0UL;
            int   fontIndex             = materials.IndexOf(ComponentType.ReadOnly<TextMaterialMaskShaderIndex>());
            ulong fontMaterialMaskLower = (ulong)fontIndex >= 64UL ? 0UL : (1U << fontIndex);
            ulong fontMaterialMaskUpper = (ulong)fontIndex >= 64UL ? (1UL << (fontIndex - 64)) : 0UL;

            var set = new ComponentTypeSet(ComponentType.ReadOnly<GpuResidentAllocation>(), ComponentType.ReadOnly<GpuResidentUpdateFlag>());
            latiosWorld.syncPoint.CreateEntityCommandBuffer().RemoveComponent(m_deadQuery.ToEntityArray(Allocator.Temp), in set);

            var materialMasksJh = new UpdateMaterialMasksJob
            {
                glyphMasksHandle                = SystemAPI.GetBufferTypeHandle<RenderGlyphMask>(true),
                glyphMaterialMaskLower          = textMaterialMaskLower,
                glyphMaterialMaskUpper          = textMaterialMaskUpper,
                gpuResidentUpdateFlagHandle     = SystemAPI.GetComponentTypeHandle<GpuResidentUpdateFlag>(true),
                maskMaterialMaskLower           = fontMaterialMaskLower,
                maskMaterialMaskUpper           = fontMaterialMaskUpper,
                materialPropertyDirtyMaskHandle = SystemAPI.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
            }.ScheduleParallel(m_allQuery, state.Dependency);

            // Glyphs
            var deadGlyphsJh = new RemoveDeadGlyphsJob
            {
                glyphGaps                  = m_glyphGaps,
                gpuResidentAlloctionHandle = SystemAPI.GetComponentTypeHandle<GpuResidentAllocation>(true),
                worldBlackboardEntity      = latiosWorld.worldBlackboardEntity
            }.Schedule(m_deadQuery,     state.Dependency);

            var changedGlyphChunks  = new NativeList<ChangedChunk>(m_changedGlyphsQuery.CalculateChunkCountWithoutFiltering(), state.WorldUpdateAllocator);
            var findChangedGlyphsJh = new FindChangedChunksJob
            {
                changedChunks = changedGlyphChunks,
            }.Schedule(m_changedGlyphsQuery, state.Dependency);

            var glyphStreamCount = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator);
            glyphStreamCount[0]  = m_newGlyphsQuery.CalculateChunkCountWithoutFiltering();
            bool isSingle        = glyphStreamCount[0] == 0;
            if (isSingle)
                glyphStreamCount[0]    = 1;
            var glyphStreamConstructJh = NativeStream.ScheduleConstruct(out var glyphStream, glyphStreamCount, default, state.WorldUpdateAllocator);
            var collectGlyphsJob       = new GatherGlyphUploadOperationsJob
            {
                changedChunks               = changedGlyphChunks,
                glyphGaps                   = m_glyphGaps,
                glyphsHandle                = SystemAPI.GetBufferTypeHandle<RenderGlyph>(true),
                glyphMaskHandle             = SystemAPI.GetBufferTypeHandle<RenderGlyphMask>(true),
                gpuResidentGlyphCountLookup = SystemAPI.GetComponentLookup<GpuResidentGlyphCount>(false),
                gpuResidentAlloctionHandle  = SystemAPI.GetComponentTypeHandle<GpuResidentAllocation>(false),
                streamWriter                = glyphStream.AsWriter(),
                textShaderIndexHandle       = SystemAPI.GetComponentTypeHandle<TextShaderIndex>(false),
                trcHandle                   = SystemAPI.GetComponentTypeHandle<TextRenderControl>(true),
                worldBlackboardEntity       = latiosWorld.worldBlackboardEntity,
            };
            var collectGlyphsJh = JobHandle.CombineDependencies(glyphStreamConstructJh, deadGlyphsJh, findChangedGlyphsJh);
            if (isSingle)
                collectGlyphsJh = collectGlyphsJob.Schedule(collectGlyphsJh);
            else
                collectGlyphsJh = collectGlyphsJob.Schedule(m_newGlyphsQuery, collectGlyphsJh);

            var glyphPayloads                 = new NativeList<UploadPayload>(1, state.WorldUpdateAllocator);
            var requiredGlyphUploadBufferSize = new NativeReference<uint>(state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var finalGlyphsJh                 = new MapPayloadsToUploadBufferJob
            {
                streamReader             = glyphStream.AsReader(),
                payloads                 = glyphPayloads,
                requiredUploadBufferSize = requiredGlyphUploadBufferSize
            }.Schedule(collectGlyphsJh);

            // Masks
            var deadMasksJh = new RemoveDeadMasksJob
            {
                maskGaps                   = m_maskGaps,
                gpuResidentAlloctionHandle = SystemAPI.GetComponentTypeHandle<GpuResidentAllocation>(true),
                worldBlackboardEntity      = latiosWorld.worldBlackboardEntity
            }.Schedule(m_deadQuery, collectGlyphsJh);

            var changedMaskChunks  = new NativeList<ChangedChunk>(m_changedMasksQuery.CalculateChunkCountWithoutFiltering(), state.WorldUpdateAllocator);
            var findChangedMasksJh = new FindChangedChunksJob
            {
                changedChunks = changedMaskChunks,
            }.Schedule(m_changedMasksQuery, state.Dependency);

            var maskPayloads                 = new NativeList<UploadPayload>(1, state.WorldUpdateAllocator);
            var requiredMaskUploadBufferSize = new NativeReference<uint>(state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var maskStreamCount              = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator);
            maskStreamCount[0]               = m_newMasksQuery.CalculateChunkCountWithoutFiltering();
            isSingle                         = maskStreamCount[0] == 0;
            if (isSingle)
                maskStreamCount[0]    = 1;
            var maskStreamConstructJh = NativeStream.ScheduleConstruct(out var maskStream, maskStreamCount, default, state.WorldUpdateAllocator);

            var collectMasksJob = new GatherMaskUploadOperationsJob
            {
                additonalEntitiesHandle           = SystemAPI.GetBufferTypeHandle<AdditionalFontMaterialEntity>(true),
                changedChunks                     = changedMaskChunks,
                maskGaps                          = m_maskGaps,
                glyphMaskHandle                   = SystemAPI.GetBufferTypeHandle<RenderGlyphMask>(true),
                gpuResidentMaskCountLookup        = SystemAPI.GetComponentLookup<GpuResidentMaskCount>(false),
                gpuResidentAlloctionHandle        = SystemAPI.GetComponentTypeHandle<GpuResidentAllocation>(false),
                streamWriter                      = maskStream.AsWriter(),
                textMaterialMaskShaderIndexHandle = SystemAPI.GetComponentTypeHandle<TextMaterialMaskShaderIndex>(false),
                worldBlackboardEntity             = latiosWorld.worldBlackboardEntity,
            };
            var collectMasksJh = CollectionsExtensions.CombineDependencies(stackalloc JobHandle[] { maskStreamConstructJh, findChangedMasksJh, deadMasksJh, materialMasksJh });
            if (isSingle)
                collectMasksJh = collectMasksJob.Schedule(collectMasksJh);
            else
                collectMasksJh = collectMasksJob.Schedule(m_newMasksQuery, collectMasksJh);

            var batchMasksJh = new MapPayloadsToUploadBufferJob
            {
                streamReader             = maskStream.AsReader(),
                payloads                 = maskPayloads,
                requiredUploadBufferSize = requiredMaskUploadBufferSize,
            }.Schedule(collectMasksJh);

            m_gpuUpdateFlagDynamicHandle.Update(ref state);
            var copyPropertiesJh = new CopyGlyphShaderIndicesJob
            {
                additionalEntitiesHandle    = SystemAPI.GetBufferTypeHandle<AdditionalFontMaterialEntity>(true),
                shaderIndexHandle           = SystemAPI.GetComponentTypeHandle<TextShaderIndex>(true),
                renderGlyphMaskLookup       = SystemAPI.GetBufferLookup<RenderGlyphMask>(true),
                gpuResidentUpdateFlagHandle = m_gpuUpdateFlagDynamicHandle,
                shaderIndexLookup           = SystemAPI.GetComponentLookup<TextShaderIndex>(false),
            }.ScheduleParallel(m_newAndChangedGlyphsWithMasksQuery, collectGlyphsJh);

            // Cleanup
            var accb = latiosWorld.syncPoint.CreateAddComponentsCommandBuffer<GpuResidentAllocation>(AddComponentsDestroyedEntityResolution.AddToNewEntityAndDestroy);
            accb.AddComponentTag<GpuResidentUpdateFlag>();
            var structuralChangesJh = new QueueChangesForNewChunksJob
            {
                accb               = accb.AsParallelWriter(),
                entityHandle       = SystemAPI.GetEntityTypeHandle(),
                glyphMaskHandle    = SystemAPI.GetBufferTypeHandle<RenderGlyphMask>(true),
                glyphsHandle       = SystemAPI.GetBufferTypeHandle<RenderGlyph>(true),
                materialMaskHandle = SystemAPI.GetComponentTypeHandle<TextMaterialMaskShaderIndex>(true),
                shaderIndexHandle  = SystemAPI.GetComponentTypeHandle<TextShaderIndex>(true)
            }.ScheduleParallel(m_newQuery, JobHandle.CombineDependencies(copyPropertiesJh, collectMasksJh));

            state.Dependency = JobHandle.CombineDependencies(finalGlyphsJh, batchMasksJh, structuralChangesJh);

            return new CollectState
            {
                glyphPayloads                 = glyphPayloads,
                requiredGlyphUploadBufferSize = requiredGlyphUploadBufferSize,
                maskPayloads                  = maskPayloads,
                requiredMaskUploadBufferSize  = requiredMaskUploadBufferSize
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

            var maskUploadBuffer = graphicsBroker.GetUploadBuffer(m_glyphMasksUploadID, math.max(requiredMaskUploadBufferSize, 128));
            var maskMetaBuffer   = graphicsBroker.GetMetaUint3UploadBuffer((uint)maskPayloads.Length);

            var maskJh = new WriteMasksUploadsToBuffersJob
            {
                payloads          = maskPayloads.AsDeferredJobArray(),
                masksUploadBuffer = maskUploadBuffer.LockBufferForWrite<uint>(0, (int)requiredMaskUploadBufferSize),
                metaUploadBuffer  = maskMetaBuffer.LockBufferForWrite<uint3>(0, maskPayloads.Length)
            }.Schedule(maskPayloads, 1, state.Dependency);

            finalSecondPhaseJh = JobHandle.CombineDependencies(finalSecondPhaseJh, maskJh);

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

            maskUploadBuffer.UnlockBufferAfterWrite<uint>((int)requiredMaskUploadBufferSize);
            maskMetaBuffer.UnlockBufferAfterWrite<uint3>(maskPayloads.Length);

            var gpuResidentGlyphCount = latiosWorld.worldBlackboardEntity.GetComponentData<GpuResidentGlyphCount>().glyphCount;
            var persistentGlyphBuffer = graphicsBroker.GetPersistentBuffer(m_glyphsBufferID, math.max(gpuResidentGlyphCount, 128) * 24);
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

            var gpuResidentMaskCount = latiosWorld.worldBlackboardEntity.GetComponentData<GpuResidentMaskCount>().maskCount;
            var persistentMaskBuffer = graphicsBroker.GetPersistentBuffer(m_glyphMasksBufferID, math.max(gpuResidentMaskCount, 128));

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

            GraphicsUnmanaged.SetGlobalBuffer(_latiosTextMaskBuffer, persistentMaskBuffer);
        }

        public struct CollectState
        {
            internal NativeList<UploadPayload> glyphPayloads;
            internal NativeReference<uint>     requiredGlyphUploadBufferSize;
            internal NativeList<UploadPayload> maskPayloads;
            internal NativeReference<uint>     requiredMaskUploadBufferSize;
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
        }

        // Schedule Parallel
        [BurstCompile]
        struct UpdateMaterialMasksJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<GpuResidentUpdateFlag> gpuResidentUpdateFlagHandle;  // Here to help Unity schedule things since enabled queries can be buggy.
            [ReadOnly] public BufferTypeHandle<RenderGlyphMask>          glyphMasksHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>   materialPropertyDirtyMaskHandle;

            public ulong glyphMaterialMaskLower;
            public ulong glyphMaterialMaskUpper;
            public ulong maskMaterialMaskLower;
            public ulong maskMaterialMaskUpper;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var dirtyMask = ref chunk.GetChunkComponentRefRW(ref materialPropertyDirtyMaskHandle);

                dirtyMask.lower.Value |= glyphMaterialMaskLower;
                dirtyMask.upper.Value |= glyphMaterialMaskUpper;
                if (chunk.Has(ref glyphMasksHandle))
                {
                    dirtyMask.lower.Value |= maskMaterialMaskLower;
                    dirtyMask.upper.Value |= maskMaterialMaskUpper;
                }
            }
        }

        struct ChangedChunk
        {
            public ArchetypeChunk chunk;
            public v128           enabledMask;
            public bool           useEnabledMask;
        }

        internal unsafe struct UploadPayload
        {
            public void* ptr;
            public uint  length;
            public uint  persistentBufferStart;
            public uint  uploadBufferStart;
            public uint  controls;
        }

        // Schedule Single
        [BurstCompile]
        struct FindChangedChunksJob : IJobChunk
        {
            public NativeList<ChangedChunk> changedChunks;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                changedChunks.AddNoResize(new ChangedChunk { chunk = chunk, enabledMask = chunkEnabledMask, useEnabledMask = useEnabledMask });
            }
        }

        // Schedule Single
        [BurstCompile]
        struct RemoveDeadGlyphsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<GpuResidentAllocation> gpuResidentAlloctionHandle;
            public NativeList<uint2>                                     glyphGaps;
            public Entity                                                worldBlackboardEntity;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var allocations = chunk.GetNativeArray(ref gpuResidentAlloctionHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var alloc = allocations[i];
                    glyphGaps.Add(new uint2(alloc.glyphStart, alloc.glyphCount));
                }
            }
        }

        // Schedule Single
        [BurstCompile]
        struct RemoveDeadMasksJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<GpuResidentAllocation> gpuResidentAlloctionHandle;
            public NativeList<uint2>                                     maskGaps;
            public Entity                                                worldBlackboardEntity;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var allocations = chunk.GetNativeArray(ref gpuResidentAlloctionHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var alloc = allocations[i];
                    maskGaps.Add(new uint2(alloc.maskStart, alloc.maskCount));
                }
            }
        }

        // Schedule Single
        [BurstCompile]
        struct GatherGlyphUploadOperationsJob : IJobChunk, IJob
        {
            [ReadOnly] public ComponentTypeHandle<TextRenderControl> trcHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyph>          glyphsHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyphMask>      glyphMaskHandle;
            [ReadOnly] public NativeList<ChangedChunk>               changedChunks;
            public NativeList<uint2>                                 glyphGaps;
            public ComponentTypeHandle<GpuResidentAllocation>        gpuResidentAlloctionHandle;
            public ComponentTypeHandle<TextShaderIndex>              textShaderIndexHandle;
            public ComponentLookup<GpuResidentGlyphCount>            gpuResidentGlyphCountLookup;
            public Entity                                            worldBlackboardEntity;

            [NativeDisableParallelForRestriction] public NativeStream.Writer streamWriter;

            uint glyphCountThisPass;
            bool didFirstIteration;
            bool isSingle;

            public unsafe void Execute()
            {
                isSingle = true;
                Execute(default, default, default, default);
            }

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var gpuResidentGlyphCount = ref gpuResidentGlyphCountLookup.GetRefRW(worldBlackboardEntity).ValueRW.glyphCount;

                streamWriter.BeginForEachIndex(unfilteredChunkIndex);

                if (!didFirstIteration)
                {
                    didFirstIteration = true;
                    RemoveOldAllocations();
                    gpuResidentGlyphCount = GapAllocation.CoellesceGaps(glyphGaps, gpuResidentGlyphCount);
                    foreach (var changedChunk in  changedChunks)
                    {
                        AddNewAllocations(changedChunk.chunk, changedChunk.useEnabledMask, changedChunk.enabledMask, ref gpuResidentGlyphCount);
                    }
                }

                if (!isSingle)
                    AddNewAllocations(chunk, useEnabledMask, chunkEnabledMask, ref gpuResidentGlyphCount);

                streamWriter.EndForEachIndex();
            }

            void RemoveOldAllocations()
            {
                foreach (var changedChunk in changedChunks)
                {
                    var allocations = changedChunk.chunk.GetNativeArray(ref gpuResidentAlloctionHandle);
                    var enumerator  = new ChunkEntityEnumerator(changedChunk.useEnabledMask, changedChunk.enabledMask, changedChunk.chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        var alloc = allocations[i];
                        glyphGaps.Add(new uint2(alloc.glyphStart, alloc.glyphCount));
                    }
                }
            }

            unsafe void AddNewAllocations(in ArchetypeChunk chunk, bool useEnabledMask, v128 chunkEnabledMask, ref uint gpuResidentGlyphCount)
            {
                var allocations   = chunk.GetNativeArray(ref gpuResidentAlloctionHandle);
                var trcs          = chunk.GetNativeArray(ref trcHandle);
                var glyphsBuffers = chunk.GetBufferAccessor(ref glyphsHandle);
                var masksBuffers  = chunk.GetBufferAccessor(ref glyphMaskHandle);
                var shaderIndices = chunk.GetNativeArray(ref textShaderIndexHandle);
                var enumerator    = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var trc              = trcs[i].flags;
                    var buffer           = glyphsBuffers[i];
                    int glyphCountToDraw = buffer.Length;
                    if (masksBuffers.Length > 0)
                    {
                        glyphCountToDraw = masksBuffers[i].Length * 16;
                    }

                    uint glyphCountToStore = (uint)buffer.Length;
                    uint firstIndex        = GapAllocation.Allocate(glyphGaps, glyphCountToStore, ref gpuResidentGlyphCount);

                    if (allocations.Length > 0)
                    {
                        var alloc        = allocations[i];
                        alloc.glyphStart = firstIndex;
                        alloc.glyphCount = glyphCountToStore;
                        allocations[i]   = alloc;
                    }

                    shaderIndices[i] = new TextShaderIndex
                    {
                        firstGlyphIndex = firstIndex,
                        glyphCount      = (uint)glyphCountToDraw
                    };

                    streamWriter.Write(new UploadPayload
                    {
                        ptr                   = buffer.GetUnsafeReadOnlyPtr(),
                        length                = (uint)buffer.Length,
                        uploadBufferStart     = glyphCountThisPass,
                        persistentBufferStart = firstIndex,
                        controls              = (uint)trc
                    });
                    glyphCountThisPass += glyphCountToStore;
                }
            }
        }

        // Schedule Single
        [BurstCompile]
        struct GatherMaskUploadOperationsJob : IJobChunk, IJob
        {
            [ReadOnly] public BufferTypeHandle<RenderGlyphMask>              glyphMaskHandle;
            [ReadOnly] public BufferTypeHandle<AdditionalFontMaterialEntity> additonalEntitiesHandle;
            [ReadOnly] public NativeList<ChangedChunk>                       changedChunks;
            public NativeList<uint2>                                         maskGaps;
            public ComponentTypeHandle<GpuResidentAllocation>                gpuResidentAlloctionHandle;
            public ComponentTypeHandle<TextMaterialMaskShaderIndex>          textMaterialMaskShaderIndexHandle;
            public ComponentLookup<GpuResidentMaskCount>                     gpuResidentMaskCountLookup;
            public Entity                                                    worldBlackboardEntity;

            [NativeDisableParallelForRestriction] public NativeStream.Writer streamWriter;

            uint maskCountThisPass;
            bool didFirstIteration;
            bool isSingle;

            public unsafe void Execute()
            {
                isSingle = true;
                Execute(default, default, default, default);
            }

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var gpuResidentMaskCount = ref gpuResidentMaskCountLookup.GetRefRW(worldBlackboardEntity).ValueRW.maskCount;

                streamWriter.BeginForEachIndex(unfilteredChunkIndex);

                if (!didFirstIteration)
                {
                    didFirstIteration = true;
                    RemoveOldAllocations();
                    gpuResidentMaskCount = GapAllocation.CoellesceGaps(maskGaps, gpuResidentMaskCount);
                    foreach (var changedChunk in changedChunks)
                    {
                        AddNewAllocations(changedChunk.chunk, changedChunk.useEnabledMask, changedChunk.enabledMask, ref gpuResidentMaskCount);
                    }
                }

                if (!isSingle)
                    AddNewAllocations(chunk, useEnabledMask, chunkEnabledMask, ref gpuResidentMaskCount);

                streamWriter.EndForEachIndex();
            }

            void RemoveOldAllocations()
            {
                foreach (var changedChunk in changedChunks)
                {
                    var allocations = changedChunk.chunk.GetNativeArray(ref gpuResidentAlloctionHandle);
                    var enumerator  = new ChunkEntityEnumerator(changedChunk.useEnabledMask, changedChunk.enabledMask, changedChunk.chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        var alloc = allocations[i];
                        maskGaps.Add(new uint2(alloc.maskStart, alloc.maskCount));
                    }
                }
            }

            unsafe void AddNewAllocations(in ArchetypeChunk chunk, bool useEnabledMask, v128 chunkEnabledMask, ref uint gpuResidentMaskCount)
            {
                var allocations   = chunk.GetNativeArray(ref gpuResidentAlloctionHandle);
                var masksBuffers  = chunk.GetBufferAccessor(ref glyphMaskHandle);
                var shaderIndices = chunk.GetNativeArray(ref textMaterialMaskShaderIndexHandle);
                var enumerator    = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var  buffer           = masksBuffers[i];
                    uint maskCountToStore = (uint)buffer.Length;
                    uint firstIndex       = GapAllocation.Allocate(maskGaps, maskCountToStore, ref gpuResidentMaskCount);

                    if (allocations.Length > 0)
                    {
                        var alloc       = allocations[i];
                        alloc.maskStart = firstIndex;
                        alloc.maskCount = maskCountToStore;
                        allocations[i]  = alloc;
                    }

                    shaderIndices[i] = new TextMaterialMaskShaderIndex
                    {
                        firstMaskIndex = firstIndex,
                    };

                    streamWriter.Write(new UploadPayload
                    {
                        ptr                   = buffer.GetUnsafeReadOnlyPtr(),
                        length                = (uint)buffer.Length,
                        uploadBufferStart     = maskCountThisPass,
                        persistentBufferStart = firstIndex,
                        controls              = 0
                    });
                    maskCountThisPass += maskCountToStore;
                }
            }
        }

        // Schedule Parallel
        [BurstCompile]
        struct CopyGlyphShaderIndicesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<TextShaderIndex>                            shaderIndexHandle;
            [ReadOnly] public BufferTypeHandle<AdditionalFontMaterialEntity>                  additionalEntitiesHandle;
            [ReadOnly] public BufferLookup<RenderGlyphMask>                                   renderGlyphMaskLookup;
            [ReadOnly] public DynamicComponentTypeHandle                                      gpuResidentUpdateFlagHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<TextShaderIndex> shaderIndexLookup;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var additionalEntitiesBuffers = chunk.GetBufferAccessor(ref additionalEntitiesHandle);
                var shaderIndices             = chunk.GetNativeArray(ref shaderIndexHandle);
                var enabledMask               = chunkEnabledMask;

                if (chunk.Has(ref gpuResidentUpdateFlagHandle))
                {
                    useEnabledMask = true;
                    enabledMask    = chunk.GetEnableableBits(ref gpuResidentUpdateFlagHandle);
                    if (enabledMask.ULong0 == 0 && enabledMask.ULong1 == 0)
                        return;
                }

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, enabledMask, chunk.Count);
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

        // Schedule Parallel
        [BurstCompile]
        struct QueueChangesForNewChunksJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                                      entityHandle;
            [ReadOnly] public ComponentTypeHandle<TextShaderIndex>                  shaderIndexHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyph>                         glyphsHandle;
            [ReadOnly] public ComponentTypeHandle<TextMaterialMaskShaderIndex>      materialMaskHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyphMask>                     glyphMaskHandle;
            public AddComponentsCommandBuffer<GpuResidentAllocation>.ParallelWriter accb;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var glyphBuffers = chunk.GetBufferAccessor(ref glyphsHandle);
                var maskBuffers  = chunk.GetBufferAccessor(ref glyphMaskHandle);
                var entities     = chunk.GetNativeArray(entityHandle);

                if (glyphBuffers.Length > 0 && maskBuffers.Length > 0)
                {
                    var glyphs = chunk.GetNativeArray(ref shaderIndexHandle);
                    var masks  = chunk.GetNativeArray(ref materialMaskHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        accb.Add(entities[i], new GpuResidentAllocation
                        {
                            glyphStart = glyphs[i].firstGlyphIndex,
                            glyphCount = (uint)glyphBuffers[i].Length,
                            maskStart  = masks[i].firstMaskIndex,
                            maskCount  = (uint)maskBuffers[i].Length
                        }, unfilteredChunkIndex);
                    }
                }
                else if (glyphBuffers.Length > 0)
                {
                    var glyphs = chunk.GetNativeArray(ref shaderIndexHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        accb.Add(entities[i], new GpuResidentAllocation
                        {
                            glyphStart = glyphs[i].firstGlyphIndex,
                            glyphCount = (uint)glyphBuffers[i].Length,
                            maskStart  = 0,
                            maskCount  = 0
                        }, unfilteredChunkIndex);
                    }
                }
                else
                {
                    var masks = chunk.GetNativeArray(ref materialMaskHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        accb.Add(entities[i], new GpuResidentAllocation
                        {
                            glyphStart = 0,
                            glyphCount = 0,
                            maskStart  = masks[i].firstMaskIndex,
                            maskCount  = (uint)maskBuffers[i].Length
                        }, unfilteredChunkIndex);
                    }
                }
            }
        }

        static class GapAllocation
        {
            public static uint CoellesceGaps(NativeList<uint2> gaps, uint oldSize)
            {
                gaps.Sort(new GapSorter());
                int dst   = 1;
                var array = gaps.AsArray();
                for (int j = 1; j < array.Length; j++)
                {
                    array[dst] = array[j];
                    var prev   = array[dst - 1];
                    if (prev.x + prev.y == array[j].x)
                    {
                        prev.y         += array[j].y;
                        array[dst - 1]  = prev;
                    }
                    else
                        dst++;
                }

                gaps.Length = dst;

                if (!gaps.IsEmpty)
                {
                    var backItem = gaps[gaps.Length - 1];
                    if (backItem.x + backItem.y == oldSize)
                    {
                        gaps.Length--;
                        return backItem.x;
                    }
                }

                return oldSize;
            }

            public static uint Allocate(NativeList<uint2> gaps, uint countNeeded, ref uint totalBufferSize)
            {
                if (!AllocateInGap(gaps, countNeeded, out var result))
                {
                    result           = totalBufferSize;
                    totalBufferSize += countNeeded;
                }
                return result;
            }

            static bool AllocateInGap(NativeList<uint2> gaps, uint countNeeded, out uint foundIndex)
            {
                int  bestIndex = -1;
                uint bestCount = uint.MaxValue;

                for (int i = 0; i < gaps.Length; i++)
                {
                    if (gaps[i].y >= countNeeded && gaps[i].y < bestCount)
                    {
                        bestIndex = i;
                        bestCount = gaps[i].y;
                    }
                }

                if (bestIndex < 0)
                {
                    foundIndex = 0;
                    return false;
                }

                if (bestCount == countNeeded)
                {
                    foundIndex = gaps[bestIndex].x;
                    gaps.RemoveAtSwapBack(bestIndex);
                    return true;
                }

                foundIndex       = gaps[bestIndex].x;
                var bestGap      = gaps[bestIndex];
                bestGap.x       += countNeeded;
                bestGap.y       -= countNeeded;
                gaps[bestIndex]  = bestGap;
                return true;
            }

            struct GapSorter : IComparer<uint2>
            {
                public int Compare(uint2 a, uint2 b)
                {
                    return a.x.CompareTo(b.x);
                }
            }
        }
    }
}

