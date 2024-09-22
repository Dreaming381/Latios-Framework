using Latios.Psyshock;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Calligraphics.Rendering.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [DisableAutoCreation]
    [UpdateInGroup(typeof(Calligraphics.Systems.CalligraphicsUpdateSuperSystem), OrderLast = true)]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct TextRenderingUpdateSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_singleFontQuery;
        EntityQuery m_multiFontQuery;
        EntityQuery m_gpuResidentQuery;
        bool        m_skipChangeFilter;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_singleFontQuery  = state.Fluent().With<RenderGlyph>(true).With<RenderBounds, TextRenderControl, MaterialMeshInfo>().Without<FontMaterialSelectorForGlyph>().Build();
            m_multiFontQuery   = state.Fluent().With<RenderGlyph, FontMaterialSelectorForGlyph>(true).With<RenderBounds, TextRenderControl, MaterialMeshInfo>().Build();
            m_gpuResidentQuery = state.Fluent().WithEnabled<GpuResidentUpdateFlag>(false).Build();

            latiosWorld.worldBlackboardEntity.AddComponent<GlyphCountThisFrame>();
            latiosWorld.worldBlackboardEntity.AddComponent<MaskCountThisFrame>();
            m_skipChangeFilter = (state.WorldUnmanaged.Flags & WorldFlags.Editor) == WorldFlags.Editor;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            latiosWorld.worldBlackboardEntity.SetComponentData(new GlyphCountThisFrame { glyphCount = 0 });
            latiosWorld.worldBlackboardEntity.SetComponentData(new MaskCountThisFrame { maskCount   = 1 });  // Zero reserved for no mask

            state.Dependency = new ClearGpuResidentFlagsJob
            {
                gpuResidentHandle = GetComponentTypeHandle<GpuResidentUpdateFlag>(false)
            }.ScheduleParallel(m_gpuResidentQuery, state.Dependency);

            state.Dependency = new SingleFontJob
            {
                glyphHandle            = GetBufferTypeHandle<RenderGlyph>(true),
                boundsHandle           = GetComponentTypeHandle<RenderBounds>(false),
                controlHandle          = GetComponentTypeHandle<TextRenderControl>(false),
                materialMeshInfoHandle = GetComponentTypeHandle<MaterialMeshInfo>(false),
                gpuResidentHandle      = GetComponentTypeHandle<GpuResidentUpdateFlag>(false),
                lastSystemVersion      = m_skipChangeFilter ? 0 : state.LastSystemVersion
            }.ScheduleParallel(m_singleFontQuery, state.Dependency);

            state.Dependency = new MultiFontJob
            {
                additionalEntityHandle      = GetBufferTypeHandle<AdditionalFontMaterialEntity>(true),
                boundsLookup                = GetComponentLookup<RenderBounds>(false),
                controlLookup               = GetComponentLookup<TextRenderControl>(false),
                entityHandle                = GetEntityTypeHandle(),
                glyphHandle                 = GetBufferTypeHandle<RenderGlyph>(true),
                glyphMaskLookup             = GetBufferLookup<RenderGlyphMask>(false),
                gpuResidentAllocationLookup = GetComponentLookup<GpuResidentUpdateFlag>(false),
                lastSystemVersion           = m_skipChangeFilter ? 0 : state.LastSystemVersion,
                materialMeshInfoLookup      = GetComponentLookup<MaterialMeshInfo>(false),
                selectorHandle              = GetBufferTypeHandle<FontMaterialSelectorForGlyph>(true)
            }.ScheduleParallel(m_multiFontQuery, state.Dependency);
        }

        [BurstCompile]
        struct ClearGpuResidentFlagsJob : IJobChunk
        {
            public ComponentTypeHandle<GpuResidentUpdateFlag> gpuResidentHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                chunk.SetComponentEnabledForAll(ref gpuResidentHandle, false);
            }
        }

        [BurstCompile]
        struct SingleFontJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<RenderGlyph>   glyphHandle;
            public ComponentTypeHandle<RenderBounds>          boundsHandle;
            public ComponentTypeHandle<TextRenderControl>     controlHandle;
            public ComponentTypeHandle<MaterialMeshInfo>      materialMeshInfoHandle;
            public ComponentTypeHandle<GpuResidentUpdateFlag> gpuResidentHandle;
            public uint                                       lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidChange(ref controlHandle, lastSystemVersion))
                    return;

                var ctrlRO                   = chunk.GetComponentDataPtrRO(ref controlHandle);
                int firstEntityNeedingUpdate = 0;
                for (; firstEntityNeedingUpdate < chunk.Count; firstEntityNeedingUpdate++)
                {
                    if ((ctrlRO[firstEntityNeedingUpdate].flags & TextRenderControl.Flags.Dirty) == TextRenderControl.Flags.Dirty)
                        break;
                }
                if (firstEntityNeedingUpdate >= chunk.Count)
                    return;

                var ctrlRW       = chunk.GetComponentDataPtrRW(ref controlHandle);
                var bounds       = chunk.GetComponentDataPtrRW(ref boundsHandle);
                var mmis         = chunk.GetComponentDataPtrRW(ref materialMeshInfoHandle);
                var glyphBuffers = chunk.GetBufferAccessor(ref glyphHandle);
                var bits         = chunk.GetEnabledMask(ref gpuResidentHandle);
                var bitsValid    = bits.GetOptionalEnabledRefRO<GpuResidentUpdateFlag>(firstEntityNeedingUpdate).IsValid;  // Todo: There should be a better option here.

                for (int entity = firstEntityNeedingUpdate; entity < chunk.Count; entity++)
                {
                    if ((ctrlRW[entity].flags & TextRenderControl.Flags.Dirty) != TextRenderControl.Flags.Dirty)
                        continue;
                    ctrlRW[entity].flags &= ~TextRenderControl.Flags.Dirty;
                    if (bitsValid)
                        bits[entity] = true;

                    var  glyphBuffer = glyphBuffers[entity].AsNativeArray();
                    Aabb aabb        = new Aabb(float.MaxValue, float.MinValue);
                    for (int i = 0; i < glyphBuffer.Length; i++)
                    {
                        var glyph  = glyphBuffer[i];
                        var c      = (glyph.blPosition + glyph.trPosition) / 2f;
                        var e      = math.length(c - glyph.blPosition);
                        e         += glyph.shear;
                        aabb       = Physics.CombineAabb(aabb, new Aabb(new float3(c - e, 0f), new float3(c + e, 0f)));
                    }

                    Physics.GetCenterExtents(aabb, out var center, out var extents);
                    if (glyphBuffer.Length == 0)
                    {
                        center  = 0f;
                        extents = 0f;
                    }
                    bounds[entity] = new RenderBounds { Value = new AABB { Center = center, Extents = extents } };
                    ref var mmi                                                                     = ref mmis[entity];

                    if (glyphBuffer.Length <= 8)
                        mmi.SubMesh = 0;
                    else if (glyphBuffer.Length <= 64)
                        mmi.SubMesh = 1;
                    else if (glyphBuffer.Length <= 512)
                        mmi.SubMesh = 2;
                    else if (glyphBuffer.Length <= 4096)
                        mmi.SubMesh = 3;
                    else if (glyphBuffer.Length <= 16384)
                        mmi.SubMesh = 4;
                    else
                    {
                        UnityEngine.Debug.LogWarning("Glyphs in RenderGlyph buffer exceeds max capacity of 16384 and will be truncated.");
                        mmi.SubMesh = 4;
                    }
                }
            }
        }

        [BurstCompile]
        struct MultiFontJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                                                  entityHandle;
            [ReadOnly] public BufferTypeHandle<RenderGlyph>                                     glyphHandle;
            [ReadOnly] public BufferTypeHandle<FontMaterialSelectorForGlyph>                    selectorHandle;
            [ReadOnly] public BufferTypeHandle<AdditionalFontMaterialEntity>                    additionalEntityHandle;
            [NativeDisableParallelForRestriction] public ComponentLookup<RenderBounds>          boundsLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<TextRenderControl>     controlLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<MaterialMeshInfo>      materialMeshInfoLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<RenderGlyphMask>          glyphMaskLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<GpuResidentUpdateFlag> gpuResidentAllocationLookup;
            public uint                                                                         lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(entityHandle);
                if (!controlLookup.DidChange(entities[0], lastSystemVersion))
                    return;

                var glyphBuffers    = chunk.GetBufferAccessor(ref glyphHandle);
                var selectorBuffers = chunk.GetBufferAccessor(ref selectorHandle);
                var entityBuffers   = chunk.GetBufferAccessor(ref additionalEntityHandle);

                FixedList4096Bytes<FontMaterialInstance> instances = default;

                for (int entityIndex = 0; entityIndex < chunk.Count; entityIndex++)
                {
                    var entity = entities[entityIndex];
                    instances.Clear();

                    var ctrl = controlLookup[entity];
                    if ((ctrl.flags & TextRenderControl.Flags.Dirty) != TextRenderControl.Flags.Dirty)
                        continue;
                    ctrl.flags &= ~TextRenderControl.Flags.Dirty;
                    {
                        var gpuBit = gpuResidentAllocationLookup.GetEnabledRefRWOptional<GpuResidentUpdateFlag>(entity);
                        if (gpuBit.IsValid)
                            gpuBit.ValueRW = true;
                    }

                    var glyphBuffer    = glyphBuffers[entityIndex].AsNativeArray();
                    var selectorBuffer = selectorBuffers[entityIndex].AsNativeArray().Reinterpret<byte>();
                    var entityBuffer   = entityBuffers[entityIndex].AsNativeArray().Reinterpret<Entity>();

                    instances.Add(new FontMaterialInstance
                    {
                        masks  = glyphMaskLookup[entity].Reinterpret<uint>(),
                        aabb   = new Aabb(float.MaxValue, float.MinValue),
                        entity = entity
                    });

                    for (int i = 0; i < entityBuffer.Length; i++)
                    {
                        instances.Add(new FontMaterialInstance
                        {
                            masks  = glyphMaskLookup[entityBuffer[i]].Reinterpret<uint>(),
                            aabb   = new Aabb(float.MaxValue, float.MinValue),
                            entity = entityBuffer[i]
                        });
                    }
                    foreach (var instance in instances)
                    {
                        instance.masks.Clear();
                    }

                    var glyphCount = math.min(glyphBuffer.Length, selectorBuffer.Length);
                    for (int i = 0; i < glyphCount; i++)
                    {
                        var glyph  = glyphBuffer[i];
                        var c      = (glyph.blPosition + glyph.trPosition) / 2f;
                        var e      = math.length(c - glyph.blPosition);
                        e         += glyph.shear;

                        var     selectIndex = selectorBuffer[i];
                        ref var instance    = ref instances.ElementAt(selectIndex);

                        instance.aabb = Physics.CombineAabb(instance.aabb, new Aabb(new float3(c - e, 0f), new float3(c + e, 0f)));

                        if (instance.masks.Length > 0)
                        {
                            ref var lastMask = ref instance.masks.ElementAt(instance.masks.Length - 1);
                            var     offset   = lastMask & 0xffff;
                            if (i - offset < 16)
                            {
                                var bit   = i - offset + 16;
                                lastMask |= 1u << (byte)bit;
                                continue;
                            }
                        }
                        instance.masks.Add((uint)i + 0x10000);
                    }

                    for (int i = 0; i < instances.Length; i++)
                    {
                        ref var instance = ref instances.ElementAt(i);

                        var gpuBit = gpuResidentAllocationLookup.GetEnabledRefRWOptional<GpuResidentUpdateFlag>(instance.entity);
                        if (gpuBit.IsValid)
                            gpuBit.ValueRW = true;

                        Physics.GetCenterExtents(instance.aabb, out var center, out var extents);
                        if (glyphBuffer.Length == 0)
                        {
                            center  = 0f;
                            extents = 0f;
                        }
                        boundsLookup[instance.entity]  = new RenderBounds { Value = new AABB { Center = center, Extents = extents } };
                        controlLookup[instance.entity]                                                                  = ctrl;
                        ref var mmi                                                                                     =
                            ref materialMeshInfoLookup.GetRefRW(instance.entity).ValueRW;

                        var quadCount = instance.masks.Length * 16;
                        if (quadCount == 16)
                            quadCount = math.countbits(instance.masks[0] & 0xffff0000);
                        if (quadCount <= 8)
                            mmi.SubMesh = 0;
                        else if (glyphBuffer.Length <= 64)
                            mmi.SubMesh = 1;
                        else if (glyphBuffer.Length <= 512)
                            mmi.SubMesh = 2;
                        else if (glyphBuffer.Length <= 4096)
                            mmi.SubMesh = 3;
                        else if (glyphBuffer.Length <= 16384)
                            mmi.SubMesh = 4;
                        else
                        {
                            UnityEngine.Debug.LogWarning("Glyphs in RenderGlyph buffer exceeds max capacity of 16384 and will be truncated.");
                            mmi.SubMesh = 4;
                        }
                    }
                }
            }

            struct FontMaterialInstance
            {
                public DynamicBuffer<uint> masks;
                public Aabb                aabb;
                public Entity              entity;
            }
        }
    }
}

