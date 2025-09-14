using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct GenerateBrgDrawCommandsSystem : ISystem
    {
        /// <summary>
        /// Access via WorldUnamanged.GetUnsafeSystemRef(), this can be used to tune the performance of your project for your use case.
        /// The differences are subtle, and for most use cases, adjusting this setting will have a negligible or negative impact.
        /// </summary>
        public bool optimizeForMainThread
        {
            get => m_useFewerJobs;
            set => m_useFewerJobs = value;
        }

        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_metaQuery;
        EntityQueryMask      m_motionVectorDeformQueryMask;
        EntityQuery          m_lodCrossfadeDependencyQuery;

        FindChunksWithVisibleJob m_findJob;
        ProfilerMarker           m_profilerEmitChunk;
        ProfilerMarker           m_profilerOnUpdate;

        bool m_useFewerJobs;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_metaQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkPerCameraCullingMask>(true).With<ChunkPerCameraCullingSplitsMask>(true)
                          .With<ChunkPerDispatchCullingMask>(false).With<EntitiesGraphicsChunkInfo>(true).Build();
            var motionVectorDeformQuery = state.Fluent().WithAnyEnabled<PreviousDeformShaderIndex, TwoAgoDeformShaderIndex, PreviousMatrixVertexSkinningShaderIndex>(true)
                                          .WithAnyEnabled<TwoAgoMatrixVertexSkinningShaderIndex, PreviousDqsVertexSkinningShaderIndex, TwoAgoDqsVertexSkinningShaderIndex>(true)
                                          .WithAnyEnabled<ShaderEffectRadialBounds,
                                                          LegacyDotsDeformParamsShaderIndex>(                                                                              true).
                                          Build();
            m_motionVectorDeformQueryMask = motionVectorDeformQuery.GetEntityQueryMask();
            m_lodCrossfadeDependencyQuery = state.EntityManager.CreateEntityQuery(new EntityQueryBuilder(Allocator.Temp).WithAll<LodCrossfade>());

            m_findJob = new FindChunksWithVisibleJob
            {
                perCameraCullingMaskHandle   = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkHeaderHandle            = state.GetComponentTypeHandle<ChunkHeader>(true),
                perDispatchCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(false)
            };

            m_useFewerJobs = false;

            m_profilerEmitChunk = new ProfilerMarker("EmitChunk");
            m_profilerOnUpdate  = new ProfilerMarker("OnUpdateGenerateBrg");
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            m_profilerOnUpdate.Begin();

            JobHandle finalJh        = default;
            JobHandle ecsJh          = default;
            JobHandle lodCrossfadeJh = default;

            var brgCullingContext = latiosWorld.worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>(false);
            var cullingContext    = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>();

            var chunkList = new NativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);

            m_findJob.chunkHeaderHandle.Update(ref state);
            m_findJob.chunksToProcess = chunkList.AsParallelWriter();
            m_findJob.perCameraCullingMaskHandle.Update(ref state);
            m_findJob.perDispatchCullingMaskHandle.Update(ref state);

            // TODO: Dynamically estimate this based on past frames
            int binCountEstimate       = 1;
            var chunkDrawCommandOutput = new ChunkDrawCommandOutput(
                binCountEstimate,
                brgCullingContext.cullingThreadLocalAllocator,
                brgCullingContext.batchCullingOutput);

            var emitDrawCommandsJob = new EmitDrawCommandsJob
            {
                BRGRenderMeshArrays                   = brgCullingContext.brgRenderMeshArrays,
                CameraPosition                        = cullingContext.lodParameters.cameraPosition,
                chunkPerCameraCullingMaskHandle       = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkPerCameraCullingSplitsMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingSplitsMask>(true),
                chunksToProcess                       = chunkList.AsDeferredJobArray(),
                CullingLayerMask                      = cullingContext.cullingLayerMask,
                DepthSorted                           = GetComponentTypeHandle<DepthSorted_Tag>(true),
                DrawCommandOutput                     = chunkDrawCommandOutput,
#if UNITY_EDITOR
                EditorDataComponentHandle = GetSharedComponentTypeHandle<EditorRenderData>(),
#endif
                EntitiesGraphicsChunkInfo    = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                LastSystemVersion            = state.LastSystemVersion,
                LightMaps                    = ManagedAPI.GetSharedComponentTypeHandle<LightMaps>(),
                lodCrossfadeHandle           = GetComponentTypeHandle<LodCrossfade>(true),
                motionVectorDeformQueryMask  = m_motionVectorDeformQueryMask,
                PostProcessMatrix            = GetComponentTypeHandle<PostProcessMatrix>(true),
                MaterialMeshInfo             = GetComponentTypeHandle<MaterialMeshInfo>(true),
                meshLodHandle                = GetComponentTypeHandle<MeshLod>(true),
                ProceduralMotion             = GetComponentTypeHandle<PerVertexMotionVectors_Tag>(true),
                ProfilerEmitChunk            = m_profilerEmitChunk,
                promiseHandle                = GetComponentTypeHandle<PromiseAllEntitiesInChunkUseSameMaterialMeshInfoTag>(true),
                RenderFilterSettings         = GetSharedComponentTypeHandle<RenderFilterSettings>(),
                RenderMeshArray              = ManagedAPI.GetSharedComponentTypeHandle<RenderMeshArray>(),
                rendererPriorityHandle       = GetComponentTypeHandle<RendererPriority>(true),
                overrideMeshInRangeTagHandle = GetComponentTypeHandle<OverrideMeshInRangeTag>(true),
                SceneCullingMask             = cullingContext.sceneCullingMask,
                speedTreeCrossfadeTagHandle  = GetComponentTypeHandle<SpeedTreeCrossfadeTag>(true),
                splitsAreValid               = cullingContext.viewType == BatchCullingViewType.Light,
                useMmiRangeLodTagHandle      = GetComponentTypeHandle<UseMmiRangeLodTag>(true),
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                WorldTransform = GetComponentTypeHandle<WorldTransform>(true),
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                WorldTransform = GetComponentTypeHandle<Unity.Transforms.LocalToWorld>(true),
#endif
            };

            var findDependency = m_findJob.ScheduleParallelByRef(m_metaQuery, state.Dependency);

            var emitDrawCommandsDependency = emitDrawCommandsJob.ScheduleByRef(chunkList, 1, findDependency);
            ecsJh                          = emitDrawCommandsDependency;

            if (!m_useFewerJobs)
            {
                var collectGlobalBinsDependency =
                    chunkDrawCommandOutput.BinCollector.ScheduleFinalize(emitDrawCommandsDependency);
                var sortBinsDependency = DrawBinSort.ScheduleBinSort(
                    brgCullingContext.cullingThreadLocalAllocator.GeneralAllocator,
                    chunkDrawCommandOutput.SortedBins,
                    chunkDrawCommandOutput.UnsortedBins,
                    collectGlobalBinsDependency);

                var allocateWorkItemsJob = new AllocateWorkItemsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var collectWorkItemsJob = new CollectWorkItemsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var flushWorkItemsJob = new FlushWorkItemsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var allocateInstancesJob = new AllocateInstancesJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var allocateDrawCommandsJob = new AllocateDrawCommandsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput
                };

                var expandInstancesJob = new ExpandVisibleInstancesJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var generateDrawCommandsJob = new GenerateDrawCommandsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var generateDrawRangesJob = new GenerateDrawRangesJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                    FilterSettings    = brgCullingContext.batchFilterSettingsByRenderFilterSettingsSharedIndex,
                };

                var allocateWorkItemsDependency = allocateWorkItemsJob.Schedule(collectGlobalBinsDependency);
                var collectWorkItemsDependency  = collectWorkItemsJob.ScheduleWithIndirectList(
                    chunkDrawCommandOutput.UnsortedBins, 1, allocateWorkItemsDependency);

                var flushWorkItemsDependency =
                    flushWorkItemsJob.Schedule(ChunkDrawCommandOutput.NumThreads, 1, collectWorkItemsDependency);

                var allocateInstancesDependency = allocateInstancesJob.Schedule(flushWorkItemsDependency);

                var allocateDrawCommandsDependency = allocateDrawCommandsJob.Schedule(
                    JobHandle.CombineDependencies(sortBinsDependency, flushWorkItemsDependency));

                var allocationsDependency = JobHandle.CombineDependencies(
                    allocateInstancesDependency,
                    allocateDrawCommandsDependency);

                var expandInstancesDependency = expandInstancesJob.ScheduleWithIndirectList(
                    chunkDrawCommandOutput.WorkItems,
                    1,
                    allocateInstancesDependency);
                var generateDrawCommandsDependency = generateDrawCommandsJob.ScheduleWithIndirectList(
                    chunkDrawCommandOutput.SortedBins,
                    1,
                    allocationsDependency);
                var generateDrawRangesDependency = generateDrawRangesJob.Schedule(allocateDrawCommandsDependency);

                var expansionDependency = JobHandle.CombineDependencies(
                    expandInstancesDependency,
                    generateDrawCommandsDependency,
                    generateDrawRangesDependency);

                finalJh        = expansionDependency;
                lodCrossfadeJh = expandInstancesDependency;
            }
            else
            {
                var singleJobDependency = new SingleThreadedJob
                {
                    chunkDrawCommandOutput = chunkDrawCommandOutput,
                    brgFilterSettings      = brgCullingContext.batchFilterSettingsByRenderFilterSettingsSharedIndex
                }.Schedule(emitDrawCommandsDependency);
                lodCrossfadeJh = singleJobDependency;
                finalJh        = singleJobDependency;
            }

            state.Dependency = ecsJh;
            latiosWorld.worldBlackboardEntity.UpdateJobDependency<BrgCullingContext>(finalJh, false);
            m_lodCrossfadeDependencyQuery.AddDependency(lodCrossfadeJh);

            m_profilerOnUpdate.End();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            m_lodCrossfadeDependencyQuery.Dispose();
        }

        [BurstCompile]
        unsafe struct SingleThreadedJob : IJob
        {
            public ChunkDrawCommandOutput                                     chunkDrawCommandOutput;
            [ReadOnly] public NativeParallelHashMap<int, BatchFilterSettings> brgFilterSettings;

            public void Execute()
            {
                chunkDrawCommandOutput.BinCollector.RunFinalizeImmediate();
                DrawBinSort.RunBinSortImmediate(
                    chunkDrawCommandOutput.ThreadLocalAllocator.GeneralAllocator,
                    chunkDrawCommandOutput.SortedBins,
                    chunkDrawCommandOutput.UnsortedBins);

                var allocateWorkItemsJob = new AllocateWorkItemsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var collectWorkItemsJob = new CollectWorkItemsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var flushWorkItemsJob = new FlushWorkItemsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var allocateInstancesJob = new AllocateInstancesJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var allocateDrawCommandsJob = new AllocateDrawCommandsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput
                };

                var expandInstancesJob = new ExpandVisibleInstancesJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var generateDrawCommandsJob = new GenerateDrawCommandsJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                };

                var generateDrawRangesJob = new GenerateDrawRangesJob
                {
                    DrawCommandOutput = chunkDrawCommandOutput,
                    FilterSettings    = brgFilterSettings,
                };

                allocateWorkItemsJob.Execute();
                collectWorkItemsJob.RunImmediateWithIndirectList(chunkDrawCommandOutput.UnsortedBins);

                flushWorkItemsJob.RunImmediate(ChunkDrawCommandOutput.NumThreads);

                allocateInstancesJob.Execute();

                allocateDrawCommandsJob.Execute();

                expandInstancesJob.RunImmediateWithIndirectList(chunkDrawCommandOutput.WorkItems);
                generateDrawCommandsJob.RunImmediateWithIndirectList(chunkDrawCommandOutput.SortedBins);
                generateDrawRangesJob.Execute();
            }
        }
    }

    internal static class IndirectListScheduleExtensions
    {
        public static void RunImmediateWithIndirectList<TJob, TList>(this TJob job, IndirectList<TList> list) where TJob : unmanaged, IJobParallelForDefer where TList : unmanaged
        {
            for (int i = 0; i < list.Length; i++)
                job.Execute(i);
        }

        public static void RunImmediate<T>(this T job, int count) where T : unmanaged, IJobParallelFor
        {
            for (int i = 0; i < count; i++)
                job.Execute(i);
        }
    }
}

