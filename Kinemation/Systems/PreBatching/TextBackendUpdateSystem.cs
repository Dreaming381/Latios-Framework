using Latios.Psyshock;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.TextBackend.Systems
{
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct TextBackendUpdateSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_query;
        bool        m_skipChangeFilter;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_query = state.Fluent().WithAll<RenderGlyph>(true).WithAll<RenderBounds>().WithAll<TextRenderControl>().WithAll<MaterialMeshInfo>().Build();

            latiosWorld.worldBlackboardEntity.AddComponent<GlyphCountThisFrame>();
            m_skipChangeFilter = (state.WorldUnmanaged.Flags & WorldFlags.Editor) == WorldFlags.Editor;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            latiosWorld.worldBlackboardEntity.SetComponentData(new GlyphCountThisFrame { glyphCount = 0 });

            state.Dependency = new Job
            {
                glyphHandle            = GetBufferTypeHandle<RenderGlyph>(true),
                boundsHandle           = GetComponentTypeHandle<RenderBounds>(false),
                controlHandle          = GetComponentTypeHandle<TextRenderControl>(false),
                materialMeshInfoHandle = GetComponentTypeHandle<MaterialMeshInfo>(false),
                lastSystemVersion      = m_skipChangeFilter ? 0 : state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<RenderGlyph> glyphHandle;
            public ComponentTypeHandle<RenderBounds>        boundsHandle;
            public ComponentTypeHandle<TextRenderControl>   controlHandle;
            public ComponentTypeHandle<MaterialMeshInfo>    materialMeshInfoHandle;
            public uint                                     lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!(chunk.DidChange(ref glyphHandle, lastSystemVersion) || chunk.DidChange(ref controlHandle, lastSystemVersion)))
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

                for (int entity = firstEntityNeedingUpdate; entity < chunk.Count; entity++)
                {
                    if ((ctrlRW[entity].flags & TextRenderControl.Flags.Dirty) != TextRenderControl.Flags.Dirty)
                        continue;
                    ctrlRW[entity].flags &= ~TextRenderControl.Flags.Dirty;

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
                        mmi.Submesh = 0;
                    else if (glyphBuffer.Length <= 64)
                        mmi.Submesh = 1;
                    else if (glyphBuffer.Length <= 512)
                        mmi.Submesh = 2;
                    else if (glyphBuffer.Length <= 4096)
                        mmi.Submesh = 3;
                    else if (glyphBuffer.Length <= 16384)
                        mmi.Submesh = 4;
                    else
                    {
                        UnityEngine.Debug.LogWarning("Glyphs in RenderGlyph buffer exceeds max capacity of 16384 and will be truncated.");
                        mmi.Submesh = 4;
                    }
                }
            }
        }
    }
}

