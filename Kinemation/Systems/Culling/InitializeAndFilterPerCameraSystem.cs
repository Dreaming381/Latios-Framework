using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct InitializeAndFilterPerCameraSystem : ISystem
    {
        EntityQuery m_metaQuery;

        LatiosWorldUnmanaged       latiosWorld;
        DynamicComponentTypeHandle m_materialMeshInfoHandle;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld              = state.GetLatiosWorldUnmanaged();
            m_metaQuery              = state.Fluent().With<ChunkPerCameraCullingMask>(false).With<ChunkHeader>(true).With<EntitiesGraphicsChunkInfo>(true).Build();
            m_materialMeshInfoHandle = state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<MaterialMeshInfo>());

#if UNITY_EDITOR
            m_dynamicEditorHandle = default;
            foreach (var t in TypeManager.AllTypes)
            {
                if (t.Category != TypeManager.TypeCategory.ISharedComponentData)
                    continue;
                var type = t.Type;
                if (type.Namespace == null)
                    continue;
                if (type.Namespace != "Unity.Entities")
                    continue;
                if (type.Name.Contains("EditorRenderData"))
                {
                    m_dynamicEditorHandle = state.GetDynamicSharedComponentTypeHandle(ComponentType.ReadOnly(t.TypeIndex));
                }
            }
#endif
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_materialMeshInfoHandle.Update(ref state);
            var cullingContext = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>();
            state.Dependency   = new Job
            {
                chunkInfoHandle        = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                headerHandle           = GetComponentTypeHandle<ChunkHeader>(true),
                perCameraMaskHandle    = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                filterHandle           = GetSharedComponentTypeHandle<RenderFilterSettings>(),
                lightMapsHandle        = ManagedAPI.GetSharedComponentTypeHandle<LightMaps>(),
                materialMeshInfoHandle = m_materialMeshInfoHandle,
                cullingContext         = cullingContext
            }.ScheduleParallel(m_metaQuery, state.Dependency);

#if UNITY_EDITOR
            m_dynamicEditorHandle.Update(ref state);

            var engineContext = latiosWorld.worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>(true);

            state.Dependency = new EditorJob
            {
                headerHandle              = GetComponentTypeHandle<ChunkHeader>(true),
                maskHandle                = GetComponentTypeHandle<ChunkPerCameraCullingMask>(),
                editorDataComponentHandle = m_dynamicEditorHandle,
                entityHandle              = GetEntityTypeHandle(),
                sceneCullingMask          = cullingContext.sceneCullingMask,
                includeExcludeFilter      = engineContext.includeExcludeListFilter
            }.ScheduleParallel(m_metaQuery, state.Dependency);
#endif
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            public ComponentTypeHandle<ChunkPerCameraCullingMask>             perCameraMaskHandle;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> filterHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>                headerHandle;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo>  chunkInfoHandle;
            [ReadOnly] public SharedComponentTypeHandle<LightMaps>            lightMapsHandle;
            [ReadOnly] public DynamicComponentTypeHandle                      materialMeshInfoHandle;

            public CullingContext cullingContext;

            public bool clearDispatch;

            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunkPerCameraMasks = metaChunk.GetComponentDataPtrRW(ref perCameraMaskHandle);
                UnsafeUtility.MemClear(chunkPerCameraMasks, sizeof(ChunkPerCameraCullingMask) * metaChunk.Count);

                var chunkHeaders = metaChunk.GetComponentDataPtrRO(ref headerHandle);
                var chunkInfos   = metaChunk.GetComponentDataPtrRO(ref chunkInfoHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
                    chunkPerCameraMasks[i] = default;

                    if (!chunkInfos[i].Valid)
                        continue;

                    ref var chunk = ref chunkHeaders[i].ArchetypeChunk;

                    var filter = chunk.Has(filterHandle) ? chunk.GetSharedComponent(filterHandle) : RenderFilterSettings.Default;
                    if (cullingContext.viewType == BatchCullingViewType.Light && filter.ShadowCastingMode == ShadowCastingMode.Off)
                        continue;

                    if (cullingContext.viewType == BatchCullingViewType.Camera && filter.ShadowCastingMode == ShadowCastingMode.ShadowsOnly)
                        continue;

                    if ((cullingContext.cullingLayerMask & (1 << filter.Layer)) == 0)
                        continue;

                    if ((cullingContext.cullingFlags & BatchCullingFlags.CullLightmappedShadowCasters) == BatchCullingFlags.CullLightmappedShadowCasters &&
                        chunk.GetSharedComponentIndex(lightMapsHandle) >= 0)
                        continue;

                    // sceneCullingMask gets handled in a separate Editor-only job

                    var enabled                        = chunk.GetEnableableBits(ref materialMeshInfoHandle);
                    chunkPerCameraMasks[i].lower.Value = enabled.ULong0;
                    chunkPerCameraMasks[i].upper.Value = enabled.ULong1;
                }
            }
        }

#if UNITY_EDITOR
        DynamicSharedComponentTypeHandle m_dynamicEditorHandle;

        [BurstCompile]
        struct EditorJob : IJobChunk
        {
            public ComponentTypeHandle<ChunkPerCameraCullingMask> maskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader> headerHandle;
            [ReadOnly] public EntityTypeHandle entityHandle;

            [ReadOnly] public DynamicSharedComponentTypeHandle editorDataComponentHandle;
            [ReadOnly] public IncludeExcludeListFilter includeExcludeFilter;

            public ulong sceneCullingMask;

            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunkPerCameraMasks = metaChunk.GetComponentDataPtrRW(ref maskHandle);
                var chunkHeaders        = metaChunk.GetComponentDataPtrRO(ref headerHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
                    if (chunkPerCameraMasks[i].lower.Value == 0)
                        continue;

                    ref var chunk                 = ref chunkHeaders[i].ArchetypeChunk;
                    int editorRenderDataIndex = chunk.GetSharedComponentIndex(ref editorDataComponentHandle);  // Safe to call even if chunk doesn't have component

                    // If we can't find a culling mask, use the default
                    ulong chunkSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.DefaultSceneCullingMask;
                    if (chunk.Has(ref editorDataComponentHandle))
                    {
                        chunkSceneCullingMask = *(ulong*)chunk.GetDynamicSharedComponentDataAddress(ref editorDataComponentHandle);
                    }

                    // Cull the chunk if the scene mask intersection is empty.
                    if ( (sceneCullingMask & chunkSceneCullingMask) == 0)
                    {
                        chunkPerCameraMasks[i] = default;
                    }

                    if (includeExcludeFilter.IsEnabled)
                    {
                        var entities = chunk.GetNativeArray(entityHandle);
                        ulong lower    = 0;
                        ulong upper    = 0;
                        for (int j = 0; j < math.min(chunk.Count, 64); j++)
                        {
                            lower |= math.select(0ul, 1ul, includeExcludeFilter.EntityPassesFilter(entities[j].Index)) << j;
                        }
                        for (int j = 64; j < chunk.Count; j++)
                        {
                            upper |= math.select(0ul, 1ul, includeExcludeFilter.EntityPassesFilter(entities[j].Index)) << (j - 64);
                        }

                        chunkPerCameraMasks[i].lower.Value &= lower;
                        chunkPerCameraMasks[i].upper.Value &= upper;
                    }
                }
            }
        }
#endif
    }
}

