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

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct InitializeAndFilterPerCameraSystem : ISystem
    {
        EntityQuery m_metaQuery;
        Job         m_job;

        LatiosWorldUnmanaged latiosWorld;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_metaQuery = state.Fluent().WithAll<ChunkPerFrameCullingMask>(false).WithAll<ChunkHeader>(true).WithAll<EntitiesGraphicsChunkInfo>(true).Build();

            m_job = new Job
            {
                chunkInfoHandle = state.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                headerHandle    = state.GetComponentTypeHandle<ChunkHeader>(true),
                maskHandle      = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(),
                filterHandle    = state.GetSharedComponentTypeHandle<RenderFilterSettings>()
            };

#if UNITY_EDITOR
            DynamicSharedComponentTypeHandle dynamicHandle = default;
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
                    dynamicHandle = state.GetDynamicSharedComponentTypeHandle(ComponentType.ReadOnly(t.TypeIndex));
                }
            }

            m_editorJob = new EditorJob
            {
                headerHandle              = m_job.headerHandle,
                maskHandle                = m_job.maskHandle,
                editorDataComponentHandle = dynamicHandle,
                entityHandle              = state.GetEntityTypeHandle()
            };
#endif
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_job.chunkInfoHandle.Update(ref state);
            m_job.filterHandle.Update(ref state);
            m_job.headerHandle.Update(ref state);
            m_job.maskHandle.Update(ref state);

            m_job.cullingContext = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>();

            state.Dependency = m_job.ScheduleParallelByRef(m_metaQuery, state.Dependency);

#if UNITY_EDITOR
            m_editorJob.editorDataComponentHandle.Update(ref state);
            m_editorJob.headerHandle.Update(ref state);
            m_editorJob.maskHandle.Update(ref state);
            m_editorJob.entityHandle.Update(ref state);

            var engineContext = latiosWorld.worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>(true);
            m_editorJob.sceneCullingMask     = m_job.cullingContext.sceneCullingMask;
            m_editorJob.includeExcludeFilter = engineContext.includeExcludeListFilter;

            state.Dependency = m_editorJob.ScheduleParallelByRef(m_metaQuery, state.Dependency);
#endif
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            public ComponentTypeHandle<ChunkPerCameraCullingMask>             maskHandle;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> filterHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>                headerHandle;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo>  chunkInfoHandle;

            public CullingContext cullingContext;

            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var ptr = metaChunk.GetComponentDataPtrRW(ref maskHandle);
                UnsafeUtility.MemClear(ptr, sizeof(ChunkPerCameraCullingMask) * metaChunk.Count);

                var chunkPerCameraMasks = (ChunkPerCameraCullingMask*)ptr;

                var chunkHeaders = (ChunkHeader*)metaChunk.GetComponentDataPtrRO(ref headerHandle);
                var chunkInfos   = (EntitiesGraphicsChunkInfo*)metaChunk.GetComponentDataPtrRO(ref chunkInfoHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
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

                    // sceneCullingMask gets handled in a separate Editor-only job

                    int lowBitCount  = math.min(64, chunk.Count);
                    int highBitCount = chunk.Count - 64;
                    chunkPerCameraMasks[i].lower.SetBits(0, true, lowBitCount);
                    if (highBitCount > 0)
                        chunkPerCameraMasks[i].upper.SetBits(0, true, highBitCount);
                }
            }
        }

#if UNITY_EDITOR
        EditorJob m_editorJob;

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
                var chunkPerCameraMasks = (ChunkPerCameraCullingMask*)metaChunk.GetComponentDataPtrRW(ref maskHandle);
                var chunkHeaders        = (ChunkHeader*)metaChunk.GetComponentDataPtrRO(ref headerHandle);
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

                        chunkPerCameraMasks[i] = new ChunkPerCameraCullingMask
                        {
                            lower = new BitField64(lower),
                            upper = new BitField64(upper)
                        };
                    }
                }
            }
        }
#endif
    }
}

