using System;
using System.Collections.Generic;
using Latios.Kinemation.InternalSourceGen;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    [DisableAutoCreation]
    public partial struct KinemationBindingReactiveSystem : ISystem
    {
        EntityQuery m_newMeshesQuery;
        EntityQuery m_deadMeshesQuery;
        EntityQuery m_newPreviousPostProcessMatrixQuery;
        EntityQuery m_deadPreviousPostProcessMatrixQuery;

        EntityQuery m_newDeformMeshesQuery;
        EntityQuery m_bindableMeshesQuery;
        EntityQuery m_deadDeformMeshesQuery;
        EntityQuery m_newSkinnedMeshesQuery;
        EntityQuery m_deadSkinnedMeshesQuery;
        EntityQuery m_newCopyDeformQuery;
        EntityQuery m_deadCopyDeformQuery;
        EntityQuery m_disableComputeDeformQuery;
        EntityQuery m_enableComputeDeformQuery;
        EntityQuery m_meshesWithReinitQuery;

        EntityQuery m_newSkeletonsQuery;
        EntityQuery m_deadSkeletonsQuery;
        EntityQuery m_newExposedSkeletonsQuery;
        EntityQuery m_syncableExposedSkeletonsQuery;
        EntityQuery m_deadExposedSkeletonsQuery;
        EntityQuery m_deadExposedSkeletonsQuery2;
        EntityQuery m_newOptimizedSkeletonsQuery;
        EntityQuery m_deadOptimizedSkeletonsQuery;
        EntityQuery m_deadOptimizedSkeletonsQuery2;
        EntityQuery m_cullableExposedBonesQuery;
        EntityQuery m_aliveSkeletonsQuery;

        Entity m_failedSkeletonMeshBindingEntity;

        LatiosWorldUnmanaged latiosWorld;

        ParentReadWriteAspect.Lookup m_parentLookup;

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        // Dummy for Unity Transforms
        struct PreviousTransform : IComponentData
        {
        }
#endif

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_parentLookup = new ParentReadWriteAspect.Lookup(ref state);

            m_newMeshesQuery                    = state.Fluent().With<MaterialMeshInfo>(true).Without<ChunkPerFrameCullingMask>(true).Build();
            m_deadMeshesQuery                   = state.Fluent().With<ChunkPerFrameCullingMask>(false, true).Without<MaterialMeshInfo>().Build();
            m_newPreviousPostProcessMatrixQuery = state.Fluent().With<PostProcessMatrix>(true).With<PreviousTransform>(true)
                                                  .Without<PreviousPostProcessMatrix>().IncludeDisabledEntities().Build();
            m_deadPreviousPostProcessMatrixQuery = state.Fluent().With<PreviousPostProcessMatrix>(true).Without<PostProcessMatrix>().Build();

            m_newDeformMeshesQuery  = state.Fluent().With<MeshDeformDataBlobReference>(true).Without<BoundMesh>().Build();
            m_bindableMeshesQuery   = state.Fluent().WithAnyEnabled<NeedsBindingFlag, BoundMeshNeedsReinit>().With<MeshDeformDataBlobReference>(true).With<BoundMesh>().Build();
            m_deadDeformMeshesQuery = state.Fluent().With<BoundMesh>().Without<MeshDeformDataBlobReference>().Build();

            m_newSkinnedMeshesQuery = state.Fluent().With<BindSkeletonRoot>(true).With<MeshDeformDataBlobReference>(true)
                                      .Without<SkeletonDependent>()
                                      .WithAnyEnabled<MeshBindingPathsBlobReference>(true).WithAnyEnabled<OverrideSkinningBoneIndex>(true).Build();

            m_deadSkinnedMeshesQuery = state.Fluent().With<SkeletonDependent>().Without<MeshDeformDataBlobReference>().Build();
            m_newCopyDeformQuery     = state.Fluent().With<CopyDeformFromEntity>(true).Without<ChunkCopyDeformTag>(true).Build();
            m_deadCopyDeformQuery    = state.Fluent().With<ChunkCopyDeformTag>(false, true).Without<CopyDeformFromEntity>().Build();

            m_disableComputeDeformQuery = state.Fluent().With<DisableComputeShaderProcessingTag>(true).With<ChunkSkinningCullingTag>(false, true).Build();
            m_enableComputeDeformQuery  = state.Fluent().With<SkeletonDependent>(true).Without<DisableComputeShaderProcessingTag>().Without<ChunkSkinningCullingTag>(true).Build();

            m_meshesWithReinitQuery = state.Fluent().With<BoundMeshNeedsReinit>().Build();

            m_newSkeletonsQuery             = state.Fluent().With<SkeletonRootTag>(true).Without<DependentSkinnedMesh>().Build();
            m_deadSkeletonsQuery            = state.Fluent().With<DependentSkinnedMesh>().Without<SkeletonRootTag>().Build();
            m_aliveSkeletonsQuery           = state.Fluent().With<SkeletonRootTag>(true).Build();
            m_newExposedSkeletonsQuery      = state.Fluent().With<SkeletonRootTag>(true).Without<ExposedSkeletonCullingIndex>().With<BoneReference>(true).Build();
            m_syncableExposedSkeletonsQuery = state.Fluent().With<ExposedSkeletonCullingIndex>(true).With<BoneReferenceIsDirtyFlag>(true).Build();
            m_deadExposedSkeletonsQuery     = state.Fluent().Without<BoneReference>().With<ExposedSkeletonCullingIndex>().Build();
            m_deadExposedSkeletonsQuery2    = state.Fluent().Without<SkeletonRootTag>().With<ExposedSkeletonCullingIndex>().Build();
            m_newOptimizedSkeletonsQuery    = state.Fluent().With<SkeletonRootTag>(true).With<OptimizedBoneTransform>(true).Without<OptimizedSkeletonTag>().Build();
            m_deadOptimizedSkeletonsQuery   = state.Fluent().With<OptimizedSkeletonTag>(true).Without<OptimizedBoneTransform>().Build();
            m_deadOptimizedSkeletonsQuery2  = state.Fluent().With<OptimizedSkeletonTag>(true).Without<SkeletonRootTag>().Build();
            m_cullableExposedBonesQuery     = state.Fluent().With<BoneCullingIndex>().Build();

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new MeshGpuManager
            {
                blobIndexMap        = new NativeHashMap<BlobAssetReference<MeshDeformDataBlob>, int>(128, Allocator.Persistent),
                entries             = new NativeList<MeshGpuEntry>(Allocator.Persistent),
                indexFreeList       = new NativeList<int>(Allocator.Persistent),
                verticesGaps        = new NativeList<uint2>(Allocator.Persistent),
                weightsGaps         = new NativeList<uint2>(Allocator.Persistent),
                bindPosesGaps       = new NativeList<uint2>(Allocator.Persistent),
                blendShapesGaps     = new NativeList<uint2>(Allocator.Persistent),
                requiredBufferSizes = new NativeReference<MeshGpuRequiredSizes>(Allocator.Persistent),
                uploadCommands      = new NativeList<MeshGpuUploadCommand>(Allocator.Persistent)
            });

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new BoneOffsetsGpuManager
            {
                entries            = new NativeList<BoneOffsetsEntry>(Allocator.Persistent),
                indexFreeList      = new NativeList<int>(Allocator.Persistent),
                offsets            = new NativeList<short>(Allocator.Persistent),
                gaps               = new NativeList<uint2>(Allocator.Persistent),
                isDirty            = new NativeReference<bool>(Allocator.Persistent),
                hashToEntryMap     = new NativeHashMap<uint2, int>(128, Allocator.Persistent),
                pathPairToEntryMap = new NativeHashMap<PathMappingPair, int>(128, Allocator.Persistent)
            });

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new ExposedCullingIndexManager
            {
                skeletonToCullingIndexMap = new NativeHashMap<Entity, int>(128, Allocator.Persistent),
                indexFreeList             = new NativeList<int>(Allocator.Persistent),
                maxIndex                  = new NativeReference<int>(Allocator.Persistent),
                cullingIndexToSkeletonMap = new NativeHashMap<int, EntityWithBuffer<DependentSkinnedMesh> >(128, Allocator.Persistent)
            });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            bool haveNewMeshes                     = !m_newMeshesQuery.IsEmptyIgnoreFilter;
            bool haveDeadMeshes                    = !m_deadMeshesQuery.IsEmptyIgnoreFilter;
            bool haveNewPreviousPostProcessMatrix  = !m_newPreviousPostProcessMatrixQuery.IsEmptyIgnoreFilter;
            bool haveDeadPreviousPostProcessMatrix = !m_deadPreviousPostProcessMatrixQuery.IsEmptyIgnoreFilter;

            // Skinned boundMeshes are a special case of deform boundMeshes in which extra structural changes are required.
            bool haveMeshesWithReinit = !m_meshesWithReinitQuery.IsEmptyIgnoreFilter;
            bool haveNewDeformMeshes  = !m_newDeformMeshesQuery.IsEmptyIgnoreFilter;
            bool haveBindableMeshes   = !m_bindableMeshesQuery.IsEmptyIgnoreFilter;
            bool haveDeadDeformMeshes = !m_deadDeformMeshesQuery.IsEmptyIgnoreFilter;
            bool haveNewCopyMeshes    = !m_newCopyDeformQuery.IsEmptyIgnoreFilter;
            bool haveDeadCopyMeshes   = !m_deadCopyDeformQuery.IsEmptyIgnoreFilter;

            bool haveNewSkeletons             = !m_newSkeletonsQuery.IsEmptyIgnoreFilter;
            bool haveAliveSkeletons           = !m_aliveSkeletonsQuery.IsEmptyIgnoreFilter;
            bool haveDeadSkeletons            = !m_deadSkeletonsQuery.IsEmptyIgnoreFilter;
            bool haveNewExposedSkeletons      = !m_newExposedSkeletonsQuery.IsEmptyIgnoreFilter;
            bool haveSyncableExposedSkeletons = !m_syncableExposedSkeletonsQuery.IsEmptyIgnoreFilter;
            bool haveDeadExposedSkeletons     = !m_deadExposedSkeletonsQuery.IsEmptyIgnoreFilter;
            bool haveDeadExposedSkeletons2    = !m_deadExposedSkeletonsQuery2.IsEmptyIgnoreFilter;
            bool haveNewOptimizedSkeletons    = !m_newOptimizedSkeletonsQuery.IsEmptyIgnoreFilter;
            bool haveDeadOptimizedSkeletons   = !m_deadOptimizedSkeletonsQuery.IsEmptyIgnoreFilter;
            bool haveDeadOptimizedSkeletons2  = !m_deadOptimizedSkeletonsQuery2.IsEmptyIgnoreFilter;
            bool haveCullableExposedBones     = !m_cullableExposedBonesQuery.IsEmptyIgnoreFilter;

            // The '2' variants are covered by the base dead skeletons
            bool requiresStructuralChange = haveMeshesWithReinit | haveNewDeformMeshes | haveDeadDeformMeshes | haveNewSkeletons | haveDeadSkeletons |
                                            haveNewExposedSkeletons | haveDeadExposedSkeletons | haveNewOptimizedSkeletons | haveDeadOptimizedSkeletons |
                                            haveNewMeshes | haveDeadMeshes | haveNewPreviousPostProcessMatrix | haveDeadPreviousPostProcessMatrix |
                                            haveNewCopyMeshes | haveDeadCopyMeshes;

            bool requiresManagers = haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveNewDeformMeshes | haveDeadDeformMeshes |
                                    haveBindableMeshes;

            var allocator = state.WorldUpdateAllocator;

            UnsafeParallelBlockList bindingOpsBlockList           = default;
            UnsafeParallelBlockList meshAddOpsBlockList           = default;
            UnsafeParallelBlockList meshRemoveOpsBlockList        = default;
            UnsafeParallelBlockList skinnedMeshAddOpsBlockList    = default;
            UnsafeParallelBlockList skinnedMeshRemoveOpsBlockList = default;
            if (haveNewDeformMeshes | haveDeadDeformMeshes | haveBindableMeshes)
            {
                bindingOpsBlockList           = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<BindUnbindOperation>(), 128, allocator);
                meshAddOpsBlockList           = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<SkinnedMeshAddOperation>(), 128, allocator);
                meshRemoveOpsBlockList        = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<SkinnedMeshRemoveOperation>(), 128, allocator);
                skinnedMeshAddOpsBlockList    = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<SkinnedMeshAddOperation>(), 128, allocator);
                skinnedMeshRemoveOpsBlockList = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<SkinnedMeshRemoveOperation>(), 128, allocator);
            }

            MeshGpuManager             meshGpuManager        = default;
            BoneOffsetsGpuManager      boneOffsetsGpuManager = default;
            ExposedCullingIndexManager cullingIndicesManager = default;
            if (requiresManagers)
            {
                meshGpuManager        = latiosWorld.worldBlackboardEntity.GetCollectionComponent<MeshGpuManager>(false);
                boneOffsetsGpuManager = latiosWorld.worldBlackboardEntity.GetCollectionComponent<BoneOffsetsGpuManager>(false);
                cullingIndicesManager = latiosWorld.worldBlackboardEntity.GetCollectionComponent<ExposedCullingIndexManager>(false);
            }

            JobHandle                                        cullingJH             = default;
            NativeList<ExposedSkeletonCullingIndexOperation> cullingOps            = default;
            NativeReference<UnsafeBitArray>                  cullingIndicesToClear = default;
            if (haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveSyncableExposedSkeletons)
            {
                cullingOps            = new NativeList<ExposedSkeletonCullingIndexOperation>(0, allocator);
                cullingIndicesToClear = new NativeReference<UnsafeBitArray>(allocator);
            }

            var lastSystemVersion = state.LastSystemVersion;

            if (haveDeadSkeletons)
            {
                state.Dependency = new FindDeadSkeletonsJob
                {
                    dependentsHandle = GetBufferTypeHandle<DependentSkinnedMesh>(true),
                    meshStateLookup  = GetComponentLookup<SkeletonDependent>(false)
                }.ScheduleParallel(m_deadSkeletonsQuery, state.Dependency);
            }
            if (haveDeadDeformMeshes)
            {
                state.Dependency = new FindDeadDeformMeshesJob
                {
                    entityHandle                  = GetEntityTypeHandle(),
                    boundMeshHandle               = GetComponentTypeHandle<BoundMesh>(true),
                    depsHandle                    = GetComponentTypeHandle<SkeletonDependent>(true),
                    bindingOpsBlockList           = bindingOpsBlockList,
                    meshRemoveOpsBlockList        = meshRemoveOpsBlockList,
                    skinnedMeshRemoveOpsBlockList = skinnedMeshRemoveOpsBlockList
                }.ScheduleParallel(m_deadDeformMeshesQuery, state.Dependency);
            }

            if (haveNewDeformMeshes || haveBindableMeshes)
            {
                var newMeshJob = new FindNewDeformMeshesJob
                {
                    allocator                         = allocator,
                    bindingOpsBlockList               = bindingOpsBlockList,
                    bindSkeletonRootLookup            = GetComponentLookup<BindSkeletonRoot>(true),
                    boneOwningSkeletonReferenceLookup = GetComponentLookup<BoneOwningSkeletonReference>(true),
                    entityHandle                      = GetEntityTypeHandle(),
                    meshAddOpsBlockList               = meshAddOpsBlockList,
                    skinnedMeshAddOpsBlockList        = skinnedMeshAddOpsBlockList,
                    overrideBonesHandle               = GetBufferTypeHandle<OverrideSkinningBoneIndex>(true),
                    pathBindingsBlobRefHandle         = GetComponentTypeHandle<MeshBindingPathsBlobReference>(true),
                    skeletonBindingPathsBlobRefLookup = GetComponentLookup<SkeletonBindingPathsBlobReference>(true),
                    skeletonRootTagLookup             = GetComponentLookup<SkeletonRootTag>(true),
                    meshBlobRefHandle                 = GetComponentTypeHandle<MeshDeformDataBlobReference>(true),
                    rootRefHandle                     = GetComponentTypeHandle<BindSkeletonRoot>(true),
                    prefabLookup                      = GetComponentLookup<Prefab>(true),
                    disabledLookup                    = GetComponentLookup<Disabled>(true)
                };

                if (haveNewMeshes)
                {
                    state.Dependency = newMeshJob.ScheduleParallel(m_newDeformMeshesQuery, state.Dependency);
                }
                if (haveBindableMeshes)
                {
                    state.Dependency = new FindRebindMeshesJob
                    {
                        boundMeshHandle               = GetComponentTypeHandle<BoundMesh>(false),
                        depsHandle                    = GetComponentTypeHandle<SkeletonDependent>(false),
                        lastSystemVersion             = lastSystemVersion,
                        meshRemoveOpsBlockList        = meshRemoveOpsBlockList,
                        skinnedMeshRemoveOpsBlockList = skinnedMeshRemoveOpsBlockList,
                        needsBindingHandle            = GetComponentTypeHandle<NeedsBindingFlag>(false),
                        newMeshesJob                  = newMeshJob,
                        needsReinitHandle             = GetComponentTypeHandle<BoundMeshNeedsReinit>(true)
                    }.ScheduleParallel(m_bindableMeshesQuery, state.Dependency);
                }
            }

            if (haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2)
            {
                int newExposedSkeletonsCount = m_newExposedSkeletonsQuery.CalculateEntityCountWithoutFiltering();

                var newSkeletonsList   = m_newExposedSkeletonsQuery.ToEntityListAsync(allocator, out var jhNew);
                var deadSkeletonsList  = m_deadExposedSkeletonsQuery.ToEntityListAsync(allocator, out var jhDead);
                var deadSkeletonsList2 = m_deadExposedSkeletonsQuery2.ToEntityListAsync(allocator, out var jhDead2);

                cullingOps.Capacity = newExposedSkeletonsCount + m_syncableExposedSkeletonsQuery.CalculateEntityCountWithoutFiltering();
                var jhs             = new NativeArray<JobHandle>(4, Allocator.Temp);
                jhs[0]              = jhNew;
                jhs[1]              = jhDead;
                jhs[2]              = jhDead2;
                jhs[3]              = state.Dependency;
                state.Dependency    = JobHandle.CombineDependencies(jhs);
                cullingJH           = new ProcessNewAndDeadExposedSkeletonsJob
                {
                    newExposedSkeletons   = newSkeletonsList.AsDeferredJobArray(),
                    deadExposedSkeletons  = deadSkeletonsList.AsDeferredJobArray(),
                    deadExposedSkeletons2 = deadSkeletonsList2.AsDeferredJobArray(),
                    cullingManager        = cullingIndicesManager,
                    operations            = cullingOps,
                    indicesToClear        = cullingIndicesToClear,
                    allocator             = allocator
                }.Schedule(state.Dependency);
            }

            JobHandle                                  meshBindingsJH                   = default;
            NativeList<MeshWriteStateOperation>        meshBindingStatesToWrite         = default;
            NativeList<SkinnedMeshWriteStateOperation> skinnedMeshBindingsStatesToWrite = default;

            JobHandle                       skeletonBindingOpsJH              = default;
            NativeList<BindUnbindOperation> skeletonBindingOps                = default;
            NativeList<int2>                skeletonBindingOpsStartsAndCounts = default;
            if (haveNewDeformMeshes || haveDeadDeformMeshes || haveBindableMeshes)
            {
                int newMeshCount        = m_newDeformMeshesQuery.CalculateEntityCountWithoutFiltering();
                int deadMeshCount       = m_deadDeformMeshesQuery.CalculateEntityCountWithoutFiltering();
                int bindableMeshCount   = m_bindableMeshesQuery.CalculateEntityCountWithoutFiltering();
                int aliveSkeletonsCount = m_aliveSkeletonsQuery.CalculateEntityCountWithoutFiltering();

                meshBindingStatesToWrite         = new NativeList<MeshWriteStateOperation>(newMeshCount + bindableMeshCount, allocator);
                skinnedMeshBindingsStatesToWrite = new NativeList<SkinnedMeshWriteStateOperation>(newMeshCount + bindableMeshCount, allocator);

                meshBindingsJH = new ProcessMeshGpuChangesJob
                {
                    meshAddOpsBlockList           = meshAddOpsBlockList,
                    meshRemoveOpsBlockList        = meshRemoveOpsBlockList,
                    skinnedMeshAddOpsBlockList    = skinnedMeshAddOpsBlockList,
                    skinnedMeshRemoveOpsBlockList = skinnedMeshRemoveOpsBlockList,
                    boneManager                   = boneOffsetsGpuManager,
                    meshManager                   = meshGpuManager,
                    outputWriteOps                = meshBindingStatesToWrite,
                    outputSkinnedWriteOps         = skinnedMeshBindingsStatesToWrite
                }.Schedule(state.Dependency);

                skeletonBindingOps                = new NativeList<BindUnbindOperation>(newMeshCount + deadMeshCount + bindableMeshCount, allocator);
                skeletonBindingOpsStartsAndCounts = new NativeList<int2>(aliveSkeletonsCount, allocator);

                skeletonBindingOpsJH = new BatchBindingOpsJob
                {
                    bindingsBlockList = bindingOpsBlockList,  // This is disposed here
                    operations        = skeletonBindingOps,
                    startsAndCounts   = skeletonBindingOpsStartsAndCounts
                }.Schedule(state.Dependency);
            }

            if (requiresStructuralChange)
            {
                // Kick the jobs so that the sorting and mesh evaluation happens while we do structural changes.
                // Todo: Does Complete already do this?
                JobHandle.ScheduleBatchedJobs();

                // If we remove this component right away, we'll end up with motion vector artifacts. So we defer it by one frame.
                latiosWorld.syncPoint.CreateEntityCommandBuffer().RemoveComponent<PreviousPostProcessMatrix>(m_deadPreviousPostProcessMatrixQuery.ToEntityArray(Allocator.Temp));

                state.CompleteDependency();

                if (!state.EntityManager.Exists(m_failedSkeletonMeshBindingEntity))
                {
                    m_failedSkeletonMeshBindingEntity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponent(m_failedSkeletonMeshBindingEntity,
                                                     new ComponentTypeSet(Transforms.Abstract.QueryExtensions.GetAbstractWorldTransformRWComponentType(),
                                                                          ComponentType.ReadWrite<FailedBindingsRootTag>()));
                    state.EntityManager.SetName(m_failedSkeletonMeshBindingEntity, "Failed Bindings Root");
                }

                state.EntityManager.RemoveComponent<BoundMeshNeedsReinit>(m_meshesWithReinitQuery);
                state.EntityManager.RemoveComponent(                      m_deadSkinnedMeshesQuery, new ComponentTypeSet(ComponentType.ReadWrite<BoundMesh>(),
                                                                                                                         ComponentType.ReadWrite<SkeletonDependent>(),
                                                                                                                         ComponentType.ChunkComponentReadOnly<ChunkSkinningCullingTag>(),
                                                                                                                         ComponentType.ChunkComponent<ChunkDeformPrefixSums>()));

                // If CopyLocalToParentFromBone somehow gets added by accident, we might as well remove it.
                // Also, we remove the LocalTransform and ParentToWorldTransform now to possibly prevent a structural change later.
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                state.EntityManager.RemoveComponent(m_newSkinnedMeshesQuery, new ComponentTypeSet(ComponentType.ReadWrite<CopyLocalToParentFromBone>(),
                                                                                                  ComponentType.ReadWrite<LocalTransform>(),
                                                                                                  ComponentType.ReadWrite<ParentToWorldTransform>()));
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                state.EntityManager.RemoveComponent<CopyLocalToParentFromBone>(m_newSkinnedMeshesQuery);
#endif
                var skinnedMeshAddTypes = new FixedList128Bytes<ComponentType>();
                skinnedMeshAddTypes.Add(ComponentType.ReadWrite<BoundMesh>());
                skinnedMeshAddTypes.Add(ComponentType.ReadWrite<SkeletonDependent>());
                skinnedMeshAddTypes.Add(ComponentType.ReadOnly<CopyParentWorldTransformTag>());
                skinnedMeshAddTypes.Add(ParentReadWriteAspect.componentType);
                skinnedMeshAddTypes.Add(ComponentType.ChunkComponentReadOnly<ChunkSkinningCullingTag>());
                skinnedMeshAddTypes.Add(ComponentType.ChunkComponent<ChunkDeformPrefixSums>());

                state.EntityManager.AddComponent(m_newSkinnedMeshesQuery, new ComponentTypeSet(in skinnedMeshAddTypes));

                state.EntityManager.RemoveComponent(m_deadDeformMeshesQuery, new ComponentTypeSet(ComponentType.ReadWrite<BoundMesh>(),
                                                                                                  ComponentType.ChunkComponent<ChunkDeformPrefixSums>()));
                state.EntityManager.AddComponent( m_newDeformMeshesQuery, new ComponentTypeSet(ComponentType.ReadWrite<BoundMesh>(),
                                                                                               ComponentType.ChunkComponent<ChunkDeformPrefixSums>()));

                state.EntityManager.AddChunkComponentData<ChunkSkinningCullingTag>(m_enableComputeDeformQuery, default);
                state.EntityManager.RemoveChunkComponentData<ChunkSkinningCullingTag>(m_disableComputeDeformQuery);
                state.EntityManager.AddComponent<PreviousPostProcessMatrix>(m_newPreviousPostProcessMatrixQuery);

                state.EntityManager.RemoveComponent(m_deadCopyDeformQuery, ComponentType.ChunkComponent<ChunkCopyDeformTag>());
                state.EntityManager.AddComponent(m_newCopyDeformQuery, ComponentType.ChunkComponent<ChunkCopyDeformTag>());
                state.EntityManager.RemoveComponent(m_deadMeshesQuery, new ComponentTypeSet(ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                                            ComponentType.ChunkComponent<ChunkPerDispatchCullingMask>(),
                                                                                            ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                                            ComponentType.ChunkComponent<ChunkPerCameraCullingSplitsMask>(),
                                                                                            ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>()));
                state.EntityManager.AddComponent(m_newMeshesQuery, new ComponentTypeSet(ComponentType.ChunkComponent<ChunkPerFrameCullingMask>(),
                                                                                        ComponentType.ChunkComponent<ChunkPerDispatchCullingMask>(),
                                                                                        ComponentType.ChunkComponent<ChunkPerCameraCullingMask>(),
                                                                                        ComponentType.ChunkComponent<ChunkPerCameraCullingSplitsMask>(),
                                                                                        ComponentType.ChunkComponent<ChunkMaterialPropertyDirtyMask>()));

                var optimizedTypes = new FixedList128Bytes<ComponentType>();
                optimizedTypes.Add(ComponentType.ReadWrite<OptimizedBoneTransform>());
                optimizedTypes.Add(ComponentType.ReadWrite<OptimizedSkeletonState>());
                optimizedTypes.Add(ComponentType.ReadWrite<OptimizedSkeletonTag>());
                optimizedTypes.Add(ComponentType.ReadWrite<OptimizedSkeletonWorldBounds>());
                optimizedTypes.Add(ComponentType.ReadWrite<OptimizedBoneBounds>());
                optimizedTypes.Add(ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>());
                optimizedTypes.Add(ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingSplitsMask>());
                optimizedTypes.Add(ComponentType.ChunkComponent<ChunkOptimizedSkeletonWorldBounds>());

                state.EntityManager.RemoveComponent( m_deadOptimizedSkeletonsQuery, new ComponentTypeSet(optimizedTypes));
                state.EntityManager.RemoveComponent( m_deadExposedSkeletonsQuery,
                                                     new ComponentTypeSet(ComponentType.ReadWrite<ExposedSkeletonCullingIndex>(),
                                                                          ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>(),
                                                                          ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingSplitsMask>()));
                state.EntityManager.RemoveComponent(m_deadSkeletonsQuery, new ComponentTypeSet(ComponentType.ReadWrite<DependentSkinnedMesh>(),
                                                                                               ComponentType.ReadWrite<SkeletonBoundsOffsetFromMeshes>()));

                optimizedTypes.Add(ComponentType.ReadWrite<DependentSkinnedMesh>());
                optimizedTypes.Add(ComponentType.ReadWrite<SkeletonBoundsOffsetFromMeshes>());

                state.EntityManager.AddComponent(m_newOptimizedSkeletonsQuery, new ComponentTypeSet(optimizedTypes));
                state.EntityManager.AddComponent(m_newExposedSkeletonsQuery,   new ComponentTypeSet(ComponentType.ReadWrite<DependentSkinnedMesh>(),
                                                                                                    ComponentType.ReadWrite<SkeletonBoundsOffsetFromMeshes>(),
                                                                                                    ComponentType.ReadWrite<ExposedSkeletonCullingIndex>(),
                                                                                                    ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>(),
                                                                                                    ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingSplitsMask>()));

                state.EntityManager.AddComponent(m_newSkeletonsQuery, new ComponentTypeSet(ComponentType.ReadWrite<DependentSkinnedMesh>(),
                                                                                           ComponentType.ReadWrite<SkeletonBoundsOffsetFromMeshes>()));
            }

            if (haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveNewDeformMeshes | haveBindableMeshes | haveDeadDeformMeshes)
            {
                var jhs = new NativeList<JobHandle>(4, Allocator.Temp);
                jhs.Add(cullingJH);
                jhs.Add(meshBindingsJH);
                jhs.Add(skeletonBindingOpsJH);
                jhs.Add(state.Dependency);
                state.Dependency = JobHandle.CombineDependencies(jhs.AsArray());
            }

            if (haveNewDeformMeshes | haveBindableMeshes)
            {
                state.Dependency = new ProcessMeshStateOpsJob
                {
                    ops         = meshBindingStatesToWrite.AsDeferredJobArray(),
                    stateLookup = GetComponentLookup<BoundMesh>(false)
                }.Schedule(meshBindingStatesToWrite, 16, state.Dependency);

                m_parentLookup.Update(ref state);
                state.Dependency = new ProcessSkinnedMeshStateOpsJob
                {
                    failedBindingEntity = m_failedSkeletonMeshBindingEntity,
                    ops                 = skinnedMeshBindingsStatesToWrite.AsDeferredJobArray(),
                    parentLookup        = m_parentLookup,
                    stateLookup         = GetComponentLookup<SkeletonDependent>(false),
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                    localTransformLookup = GetComponentLookup<Unity.Transforms.LocalTransform>(false)
#endif
                }.Schedule(skinnedMeshBindingsStatesToWrite, 16, state.Dependency);
            }

            if (haveNewDeformMeshes | haveBindableMeshes | haveDeadDeformMeshes)
            {
                state.Dependency = new ProcessBindingOpsJob
                {
                    boneBoundsLookup        = GetComponentLookup<BoneBounds>(false),
                    boneOffsetsGpuManager   = boneOffsetsGpuManager,
                    boneRefsLookup          = GetBufferLookup<BoneReference>(true),
                    boneTransformLookup     = GetBufferLookup<OptimizedBoneTransform>(true),
                    boundMeshLookup         = GetComponentLookup<BoundMesh>(true),
                    dependentsLookup        = GetBufferLookup<DependentSkinnedMesh>(false),
                    hierarchyLookup         = GetComponentLookup<OptimizedSkeletonHierarchyBlobReference>(true),
                    meshGpuManager          = meshGpuManager,
                    skeletonDependentLookup = GetComponentLookup<SkeletonDependent>(false),
                    operations              = skeletonBindingOps.AsDeferredJobArray(),
                    optimizedBoundsLookup   = GetBufferLookup<OptimizedBoneBounds>(false),
                    startsAndCounts         = skeletonBindingOpsStartsAndCounts.AsDeferredJobArray(),
                }.Schedule(skeletonBindingOpsStartsAndCounts, 1, state.Dependency);
            }

            if (haveSyncableExposedSkeletons && haveCullableExposedBones)
            {
                if (!(haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2))
                {
                    state.Dependency = new AllocateCullingIndicesToClearBitmaskJob
                    {
                        allocator             = allocator,
                        cullingIndicesManager = cullingIndicesManager,
                        cullingIndicesToClear = cullingIndicesToClear
                    }.Schedule(state.Dependency);
                }

                state.Dependency = new FindExposedSkeletonsToUpdateJob
                {
                    dirtyFlagHandle   = GetComponentTypeHandle<BoneReferenceIsDirtyFlag>(false),
                    entityHandle      = GetEntityTypeHandle(),
                    indicesToClear    = cullingIndicesToClear,
                    lastSystemVersion = lastSystemVersion,
                    manager           = cullingIndicesManager,
                    operations        = cullingOps
                }.Schedule(m_syncableExposedSkeletonsQuery, state.Dependency);
            }

            if ((haveSyncableExposedSkeletons | haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2) && haveCullableExposedBones)
            {
                state.Dependency = new ResetExposedBonesJob
                {
                    indexHandle    = GetComponentTypeHandle<BoneCullingIndex>(false),
                    indicesToClear = cullingIndicesToClear
                }.ScheduleParallel(m_cullableExposedBonesQuery, state.Dependency);
            }

            if (haveSyncableExposedSkeletons | haveNewExposedSkeletons)
            {
                state.Dependency = new SetExposedSkeletonCullingIndicesJob
                {
                    boneCullingIndexLookup     = GetComponentLookup<BoneCullingIndex>(false),
                    boneIndexLookup            = GetComponentLookup<BoneIndex>(false),
                    boneRefsLookup             = GetBufferLookup<BoneReference>(true),
                    operations                 = cullingOps.AsDeferredJobArray(),
                    skeletonCullingIndexLookup = GetComponentLookup<ExposedSkeletonCullingIndex>(false),
                    skeletonReferenceLookup    = GetComponentLookup<BoneOwningSkeletonReference>(false)
                }.Schedule(cullingOps, 1, state.Dependency);
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        #region Types
        struct MeshAddOperation : IComparable<MeshAddOperation>
        {
            public Entity                                 meshEntity;
            public BlobAssetReference<MeshDeformDataBlob> meshBlob;

            public int CompareTo(MeshAddOperation other) => meshEntity.CompareTo(other.meshEntity);
        }

        struct MeshRemoveOperation : IComparable<MeshRemoveOperation>
        {
            public Entity    meshEntity;
            public BoundMesh oldBoundMeshState;

            public int CompareTo(MeshRemoveOperation other) => meshEntity.CompareTo(other.meshEntity);
        }

        struct MeshWriteStateOperation
        {
            public EntityWith<BoundMesh> meshEntity;
            public BoundMesh             meshState;
        }

        struct SkinnedMeshWriteStateOperation
        {
            public EntityWith<SkeletonDependent> meshEntity;
            public SkeletonDependent             skinnedState;
        }

        struct BindUnbindOperation : IComparable<BindUnbindOperation>
        {
            public enum OpType : byte
            {
                Unbind = 0,
                Bind = 1,
            }

            public Entity targetEntity;
            public Entity meshEntity;
            public OpType opType;

            public int CompareTo(BindUnbindOperation other)
            {
                var compare = targetEntity.CompareTo(other.targetEntity);
                if (compare == 0)
                {
                    compare = ((byte)opType).CompareTo((byte)other.opType);
                    if (compare == 0)
                        compare = meshEntity.CompareTo(other.meshEntity);
                }
                return compare;
            }
        }

        struct SkinnedMeshAddOperation : IComparable<SkinnedMeshAddOperation>
        {
            public Entity                                       meshEntity;
            public EntityWith<SkeletonRootTag>                  root;
            public BlobAssetReference<MeshDeformDataBlob>       meshBlob;
            public BlobAssetReference<MeshBindingPathsBlob>     meshBindingPathsBlob;
            public BlobAssetReference<SkeletonBindingPathsBlob> skeletonBindingPathsBlob;
            public UnsafeList<short>                            overrideBoneBindings;

            public int CompareTo(SkinnedMeshAddOperation other) => meshEntity.CompareTo(other.meshEntity);
        }

        struct SkinnedMeshRemoveOperation : IComparable<SkinnedMeshRemoveOperation>
        {
            public Entity            meshEntity;
            public BoundMesh         oldBoundMeshState;
            public SkeletonDependent oldDependentState;

            public int CompareTo(SkinnedMeshRemoveOperation other) => meshEntity.CompareTo(other.meshEntity);
        }

        struct ExposedSkeletonCullingIndexOperation
        {
            public Entity skeletonEntity;
            public int    index;
        }
        #endregion

        #region PreSync Jobs
        [BurstCompile]
        struct FindDeadSkeletonsJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh> dependentsHandle;

            [NativeDisableParallelForRestriction] public ComponentLookup<SkeletonDependent> meshStateLookup;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var buffers = chunk.GetBufferAccessor(ref dependentsHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var buffer = buffers[i].AsNativeArray();
                    for (int j = 0; j < buffer.Length; j++)
                    {
                        var entity              = buffer[j].skinnedMesh;
                        var state               = meshStateLookup[entity];
                        state.root              = Entity.Null;
                        meshStateLookup[entity] = state;
                    }
                }
            }
        }

        [BurstCompile]
        struct FindDeadDeformMeshesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                       entityHandle;
            [ReadOnly] public ComponentTypeHandle<BoundMesh>         boundMeshHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent> depsHandle;

            public UnsafeParallelBlockList bindingOpsBlockList;
            public UnsafeParallelBlockList meshRemoveOpsBlockList;
            public UnsafeParallelBlockList skinnedMeshRemoveOpsBlockList;
            [NativeSetThreadIndex] int     m_nativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(entityHandle);
                var meshes   = chunk.GetNativeArray(ref boundMeshHandle);

                // This accounts for both skinned boundMeshes and blend shape boundMeshes.
                for (int i = 0; i < chunk.Count; i++)
                {
                    // If the mesh is in a valid state, this is not null.
                    if (meshes[i].meshBlob != BlobAssetReference<MeshDeformDataBlob>.Null)
                    {
                        meshRemoveOpsBlockList.Write(new MeshRemoveOperation { meshEntity = entities[i], oldBoundMeshState = meshes[i] }, m_nativeThreadIndex);
                    }
                }

                // Everything from here is specific to skinned boundMeshes
                if (chunk.Has(ref depsHandle))
                {
                    var deps = chunk.GetNativeArray(ref depsHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        // If the mesh is in a valid state, this is not null.
                        if (meshes[i].meshBlob != BlobAssetReference<MeshDeformDataBlob>.Null)
                        {
                            // However, the mesh could still have an invalid skeleton if the skeleton died.
                            var target = deps[i].root;
                            if (target != Entity.Null)
                            {
                                bindingOpsBlockList.Write(new BindUnbindOperation
                                {
                                    targetEntity = target,
                                    meshEntity   = entities[i],
                                    opType       = BindUnbindOperation.OpType.Unbind
                                }, m_nativeThreadIndex);
                            }
                            skinnedMeshRemoveOpsBlockList.Write(new SkinnedMeshRemoveOperation
                            {
                                meshEntity        = entities[i],
                                oldBoundMeshState = meshes[i],
                                oldDependentState = deps[i]
                            }, m_nativeThreadIndex);
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct FindNewDeformMeshesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                                 entityHandle;
            [ReadOnly] public ComponentTypeHandle<MeshDeformDataBlobReference> meshBlobRefHandle;

            [ReadOnly] public ComponentLookup<SkeletonRootTag>                   skeletonRootTagLookup;
            [ReadOnly] public ComponentLookup<BindSkeletonRoot>                  bindSkeletonRootLookup;
            [ReadOnly] public ComponentLookup<BoneOwningSkeletonReference>       boneOwningSkeletonReferenceLookup;
            [ReadOnly] public ComponentLookup<SkeletonBindingPathsBlobReference> skeletonBindingPathsBlobRefLookup;
            [ReadOnly] public ComponentLookup<Prefab>                            prefabLookup;
            [ReadOnly] public ComponentLookup<Disabled>                          disabledLookup;

            // Optional
            [ReadOnly] public ComponentTypeHandle<BindSkeletonRoot>              rootRefHandle;
            [ReadOnly] public ComponentTypeHandle<MeshBindingPathsBlobReference> pathBindingsBlobRefHandle;
            [ReadOnly] public BufferTypeHandle<OverrideSkinningBoneIndex>        overrideBonesHandle;

            public UnsafeParallelBlockList bindingOpsBlockList;
            public UnsafeParallelBlockList meshAddOpsBlockList;
            public UnsafeParallelBlockList skinnedMeshAddOpsBlockList;
            [NativeSetThreadIndex] int     m_nativeThreadIndex;

            public Allocator allocator;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var lower = new BitField64(~0UL);
                var upper = new BitField64(~0UL);
                if (useEnabledMask)
                {
                    lower.Value = chunkEnabledMask.ULong0;
                    upper.Value = chunkEnabledMask.ULong1;
                }

                var entities  = chunk.GetNativeArray(entityHandle);
                var meshBlobs = chunk.GetNativeArray(ref meshBlobRefHandle);

                // First, register the mesh itself. This accounts for BlendShapes or other use cases.
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!(i >= 64 ? upper.IsSet(i - 64) : lower.IsSet(i)))
                        continue;

                    var entity = entities[i];

                    var meshBlob = meshBlobs[i].blob;
                    if (meshBlob == BlobAssetReference<MeshDeformDataBlob>.Null)
                        continue;

                    meshAddOpsBlockList.Write(new MeshAddOperation
                    {
                        meshEntity = entity,
                        meshBlob   = meshBlob
                    }, m_nativeThreadIndex);
                }

                // Everything from this point on is specific to skinned boundMeshes bound to skeletons.
                var hasRootRefs = chunk.Has(ref rootRefHandle);
                if (hasRootRefs)
                {
                    var hasPathBindings  = chunk.Has(ref pathBindingsBlobRefHandle);
                    var hasOverrideBones = chunk.Has(ref overrideBonesHandle);

                    if (!hasPathBindings && !hasOverrideBones)
                        return;

                    var rootRefs      = chunk.GetNativeArray(ref rootRefHandle);
                    var pathBindings  = chunk.GetNativeArray(ref pathBindingsBlobRefHandle);
                    var overrideBones = chunk.GetBufferAccessor(ref overrideBonesHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (!(i >= 64 ? upper.IsSet(i - 64) : lower.IsSet(i)))
                            continue;

                        var entity = entities[i];

                        var root = rootRefs[i].root;
                        if (root == Entity.Null)
                            continue;

                        // If the root reference doesn't actually point to skeleton root,
                        // it might instead point to a bone or other entity that references
                        // the skeleton. We search for it here. This lets the user do things
                        // like bind the mesh to skeleton based on a raycast hitting a bone
                        // entity with a collider.
                        var  target = root;
                        bool found  = false;
                        for (int searchIters = 0; searchIters < 1000; searchIters++)
                        {
                            if (skeletonRootTagLookup.HasComponent(target))
                            {
                                root  = target;
                                found = true;
                                break;
                            }
                            else
                            {
                                bool shouldContinue = false;
                                if (boneOwningSkeletonReferenceLookup.HasComponent(target))
                                {
                                    var skelRef = boneOwningSkeletonReferenceLookup[target];
                                    if (skelRef.skeletonRoot != Entity.Null)
                                    {
                                        target         = skelRef.skeletonRoot;
                                        shouldContinue = true;
                                    }
                                }

                                if (bindSkeletonRootLookup.HasComponent(target))
                                {
                                    var skelRef = bindSkeletonRootLookup[target];
                                    if (skelRef.root != Entity.Null)
                                    {
                                        target         = skelRef.root;
                                        shouldContinue = true;
                                    }
                                }

                                if (!shouldContinue)
                                    break;

                                if (searchIters >= 999)
                                    UnityEngine.Debug.LogError(
                                        "Searched through BindSkeletonRoot for 1000 iterations but did not find a skeleton root. If you see this message, please report it to the Latios Framework developers. Thanks!");
                            }
                        }

                        if (!found)
                        {
                            UnityEngine.Debug.LogError(
                                $"Skinned Mesh Entity {entity} attempted to bind to entity {root.entity}, but the latter is not a skeleton nor references one.");
                            continue;
                        }

                        if (disabledLookup.HasComponent(root))
                        {
                            UnityEngine.Debug.LogError($"Skinned Mesh Entity {entity} attempted to bind to entity {root.entity}, but the latter is disabled.");
                            continue;
                        }
                        if (prefabLookup.HasComponent(root))
                        {
                            UnityEngine.Debug.LogError($"Skinned Mesh Entity {entity} attempted to bind to entity {root.entity}, but the latter is a prefab.");
                            continue;
                        }

                        var meshBlob = meshBlobs[i].blob;
                        if (meshBlob == BlobAssetReference<MeshDeformDataBlob>.Null)
                            continue;

                        // This next check is for binding paths (strings).
                        // We only check that the number of mesh paths match the number of bindposes,
                        // and that the skeleton has binding paths to match with.
                        // Full binding is done later asynchronous to structural changes.
                        // This is the common case.
                        var pathsBlob         = BlobAssetReference<MeshBindingPathsBlob>.Null;
                        var skeletonPathsBlob = BlobAssetReference<SkeletonBindingPathsBlob>.Null;
                        if (hasPathBindings)
                        {
                            pathsBlob = pathBindings[i].blob;
                            if (pathsBlob != BlobAssetReference<MeshBindingPathsBlob>.Null && !hasOverrideBones)
                            {
                                var numPaths = pathsBlob.Value.pathsInReversedNotation.Length;
                                var numPoses = meshBlob.Value.skinningData.bindPoses.Length;
                                if (numPaths != numPoses)
                                {
                                    UnityEngine.Debug.LogError(
                                        $"Skinned Mesh Entity {entity} has incompatible MeshSkinningBlob and MeshBindingPathsBlob. The following should be equal: Bindposes: {numPoses}, Paths: {numPaths}");
                                    continue;
                                }

                                if (skeletonBindingPathsBlobRefLookup.HasComponent(root))
                                {
                                    skeletonPathsBlob = skeletonBindingPathsBlobRefLookup[root].blob;
                                    if (skeletonPathsBlob == BlobAssetReference<SkeletonBindingPathsBlob>.Null)
                                    {
                                        UnityEngine.Debug.LogError(
                                            $"Skinned Mesh Entity {entity} attempted to bind to entity {root.entity}, but the latter has a null SkeletonBindingPathsBlob and the skinned mesh entity does not have OverrideSkinningBoneIndex.");
                                        continue;
                                    }
                                }
                                else
                                {
                                    UnityEngine.Debug.LogError(
                                        $"Skinned Mesh Entity {entity} attempted to bind to entity {root.entity}, but the latter does not have a SkeletonBindingPathsBlob and the skinned mesh entity does not have OverrideSkinningBoneIndex.");
                                }
                            }
                            else if (!hasOverrideBones)
                                continue;
                        }

                        // This checks override bones are sized correctly relative to the bindposes,
                        // and if necessary, allocates a copy for later.
                        // Most boundMeshes do not have override bones.
                        UnsafeList<short> bonesList = default;
                        if (hasOverrideBones)
                        {
                            var bonesBuffer = overrideBones[i];
                            var numPoses    = meshBlob.Value.skinningData.bindPoses.Length;
                            if (bonesBuffer.Length != numPoses)
                            {
                                UnityEngine.Debug.LogError(
                                    $"Skinned Mesh Entity {entity} does not have the required number of override bones. Has: {bonesBuffer.Length}, Requires: {numPoses}");
                                continue;
                            }

                            bonesList = new UnsafeList<short>(numPoses, allocator);
                            bonesList.AddRangeNoResize(bonesBuffer.GetUnsafeReadOnlyPtr(), numPoses);
                        }

                        skinnedMeshAddOpsBlockList.Write(new SkinnedMeshAddOperation
                        {
                            meshEntity               = entity,
                            root                     = root,
                            meshBlob                 = meshBlob,
                            meshBindingPathsBlob     = pathsBlob,
                            overrideBoneBindings     = bonesList,
                            skeletonBindingPathsBlob = skeletonPathsBlob,
                        }, m_nativeThreadIndex);

                        bindingOpsBlockList.Write(new BindUnbindOperation
                        {
                            meshEntity   = entity,
                            targetEntity = root,
                            opType       = BindUnbindOperation.OpType.Bind
                        }, m_nativeThreadIndex);
                    }
                }
            }
        }

        [BurstCompile]
        struct FindRebindMeshesJob : IJobChunk
        {
            public FindNewDeformMeshesJob                 newMeshesJob;
            public ComponentTypeHandle<BoundMesh>         boundMeshHandle;
            public ComponentTypeHandle<SkeletonDependent> depsHandle;
            public ComponentTypeHandle<NeedsBindingFlag>  needsBindingHandle;

            [ReadOnly] public ComponentTypeHandle<BoundMeshNeedsReinit> needsReinitHandle;

            public UnsafeParallelBlockList meshRemoveOpsBlockList;
            public UnsafeParallelBlockList skinnedMeshRemoveOpsBlockList;

            public uint lastSystemVersion;

            [NativeSetThreadIndex] int m_nativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsReinint     = chunk.Has(ref needsReinitHandle);
                bool hasNeedsBindings = chunk.Has(ref needsBindingHandle);

                // The general strategy here is to unbind anything requesting a rebind
                // and then to treat it like a new mesh using that job's struct.
                if (!chunk.DidChange(ref needsBindingHandle, lastSystemVersion) && !needsReinint)
                    return;

                var reinitLower = new BitField64();
                var reinitUpper = new BitField64();

                {
                    // New scope so that the compiler doesn't keep these variables on the stack when running the NewMesh job.
                    var  entities      = chunk.GetNativeArray(newMeshesJob.entityHandle);
                    var  boundMeshes   = chunk.GetNativeArray(ref boundMeshHandle);
                    var  targetMeshes  = chunk.GetNativeArray(ref newMeshesJob.meshBlobRefHandle);
                    bool isSkinnedMesh = chunk.Has(ref depsHandle);
                    var  deps          = chunk.GetNativeArray(ref depsHandle);

                    EnabledMask needs = default;

                    // Todo: This should be flipped in 0.10.0-beta.4
                    if (hasNeedsBindings)
                        needs = chunk.GetEnabledMask(ref needsBindingHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        // If the mesh is in a valid state, this is not null.
                        bool rebind = boundMeshes[i].meshBlob != BlobAssetReference<MeshDeformDataBlob>.Null && hasNeedsBindings && needs[i];
                        bool reinit = needsReinint && boundMeshes[i].meshBlob != targetMeshes[i].blob;
                        if (rebind || reinit)
                        {
                            if (isSkinnedMesh)
                            {
                                // However, the mesh could still have an invalid skeleton if the skeleton died.
                                var target = deps[i].root;
                                if (target != Entity.Null)
                                {
                                    newMeshesJob.bindingOpsBlockList.Write(new BindUnbindOperation
                                    {
                                        targetEntity = target,
                                        meshEntity   = entities[i],
                                        opType       = BindUnbindOperation.OpType.Unbind
                                    }, m_nativeThreadIndex);
                                }
                                skinnedMeshRemoveOpsBlockList.Write(new SkinnedMeshRemoveOperation
                                {
                                    meshEntity        = entities[i],
                                    oldBoundMeshState = boundMeshes[i],
                                    oldDependentState = deps[i]
                                }, m_nativeThreadIndex);

                                // We need to wipe our state clean in case the rebinding fails.
                                deps[i] = default;
                            }
                            meshRemoveOpsBlockList.Write(new MeshRemoveOperation
                            {
                                meshEntity        = entities[i],
                                oldBoundMeshState = boundMeshes[i]
                            }, m_nativeThreadIndex);

                            if (reinit)
                            {
                                if (i < 64)
                                    reinitLower.SetBits(i, true);
                                else
                                    reinitUpper.SetBits(i - 64, true);
                            }
                        }
                    }
                    if (hasNeedsBindings)
                        chunk.SetComponentEnabledForAll(ref needsBindingHandle, false);
                }

                if (hasNeedsBindings)
                    newMeshesJob.Execute(chunk, unfilteredChunkIndex, useEnabledMask, chunkEnabledMask);
                else if (needsReinint)
                    newMeshesJob.Execute(chunk, unfilteredChunkIndex, true, new v128(reinitLower.Value, reinitUpper.Value));
            }
        }
        #endregion

        #region Sync Jobs
        [BurstCompile]
        struct ProcessNewAndDeadExposedSkeletonsJob : IJob
        {
            [ReadOnly] public NativeArray<Entity>                   newExposedSkeletons;
            [ReadOnly] public NativeArray<Entity>                   deadExposedSkeletons;
            [ReadOnly] public NativeArray<Entity>                   deadExposedSkeletons2;
            public ExposedCullingIndexManager                       cullingManager;
            public NativeList<ExposedSkeletonCullingIndexOperation> operations;
            public NativeReference<UnsafeBitArray>                  indicesToClear;

            public Allocator allocator;

            public void Execute()
            {
                // We never shrink indices, so we can safely set indicesToClear now.
                indicesToClear.Value = new UnsafeBitArray(cullingManager.maxIndex.Value + 1, allocator);

                for (int i = 0; i < deadExposedSkeletons.Length; i++)
                {
                    var entity = deadExposedSkeletons[i];
                    int index  = cullingManager.skeletonToCullingIndexMap[entity];
                    cullingManager.indexFreeList.Add(index);
                    cullingManager.skeletonToCullingIndexMap.Remove(entity);
                    cullingManager.cullingIndexToSkeletonMap.Remove(index);
                    indicesToClear.Value.Set(index, true);
                }
                for (int i = 0; i < deadExposedSkeletons2.Length; i++)
                {
                    var entity = deadExposedSkeletons2[i];
                    if (!cullingManager.skeletonToCullingIndexMap.ContainsKey(entity))
                        continue;
                    int index = cullingManager.skeletonToCullingIndexMap[entity];
                    cullingManager.indexFreeList.Add(index);
                    cullingManager.skeletonToCullingIndexMap.Remove(entity);
                    cullingManager.cullingIndexToSkeletonMap.Remove(index);
                    indicesToClear.Value.Set(index, true);
                }

                for (int i = 0; i < newExposedSkeletons.Length; i++)
                {
                    int index;
                    if (!cullingManager.indexFreeList.IsEmpty)
                    {
                        index = cullingManager.indexFreeList[0];
                        cullingManager.indexFreeList.RemoveAtSwapBack(0);
                        indicesToClear.Value.Set(index, true);  // No harm in doing this in case something stupid happens with enabled states.
                    }
                    else
                    {
                        // Index 0 is reserved for prefabs and orphaned bones
                        index = cullingManager.maxIndex.Value + 1;
                        cullingManager.maxIndex.Value++;
                    }
                    cullingManager.skeletonToCullingIndexMap.Add(newExposedSkeletons[i], index);
                    cullingManager.cullingIndexToSkeletonMap.Add(index, newExposedSkeletons[i]);

                    operations.Add(new ExposedSkeletonCullingIndexOperation
                    {
                        skeletonEntity = newExposedSkeletons[i],
                        index          = index
                    });
                }
            }
        }

        [BurstCompile]
        struct BatchBindingOpsJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public UnsafeParallelBlockList bindingsBlockList;
            public NativeList<BindUnbindOperation>                             operations;
            public NativeList<int2>                                            startsAndCounts;

            public void Execute()
            {
                var count = bindingsBlockList.Count();
                operations.ResizeUninitialized(count);
                bindingsBlockList.GetElementValues(operations.AsArray());
                operations.Sort();
                Entity  lastEntity        = Entity.Null;
                int2    nullCounts        = default;
                ref var currentStartCount = ref nullCounts;
                for (int i = 0; i < count; i++)
                {
                    if (operations[i].targetEntity != lastEntity)
                    {
                        startsAndCounts.Add(new int2(i, 1));
                        currentStartCount = ref startsAndCounts.ElementAt(startsAndCounts.Length - 1);
                        lastEntity        = operations[i].targetEntity;
                    }
                    else
                        currentStartCount.y++;
                }
                bindingsBlockList.Dispose();
            }
        }

        [BurstCompile]
        struct ProcessMeshGpuChangesJob : IJob
        {
            public UnsafeParallelBlockList meshAddOpsBlockList;
            public UnsafeParallelBlockList meshRemoveOpsBlockList;
            public UnsafeParallelBlockList skinnedMeshAddOpsBlockList;
            public UnsafeParallelBlockList skinnedMeshRemoveOpsBlockList;
            public MeshGpuManager          meshManager;
            public BoneOffsetsGpuManager   boneManager;

            public NativeList<MeshWriteStateOperation>        outputWriteOps;
            public NativeList<SkinnedMeshWriteStateOperation> outputSkinnedWriteOps;

            public unsafe void Execute()
            {
                int skinnedRemoveCount = skinnedMeshRemoveOpsBlockList.Count();
                var skinnedRemoveOps   = new NativeArray<SkinnedMeshRemoveOperation>(skinnedRemoveCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                skinnedMeshRemoveOpsBlockList.GetElementValues(skinnedRemoveOps);
                skinnedRemoveOps.Sort();

                MeshGpuRequiredSizes* requiredGpuSizes = meshManager.requiredBufferSizes.GetUnsafePtr();

                bool madeBoneOffsetGaps = false;

                var deadBoneEntryIndicesToClean = new NativeHashSet<int>(128, Allocator.Temp);
                for (int i = 0; i < skinnedRemoveCount; i++)
                {
                    var op = skinnedRemoveOps[i];

                    {
                        ref var entry = ref boneManager.entries.ElementAt(op.oldDependentState.boneOffsetEntryIndex);
                        if (op.oldDependentState.meshBindingBlob != BlobAssetReference<MeshBindingPathsBlob>.Null)
                            entry.pathsReferences--;
                        else
                            entry.overridesReferences--;

                        if (entry.pathsReferences == 0 && entry.overridesReferences == 0)
                        {
                            boneManager.gaps.Add(new uint2(entry.start, entry.gpuCount));
                            boneManager.hashToEntryMap.Remove(entry.hash);
                            boneManager.indexFreeList.Add(op.oldDependentState.boneOffsetEntryIndex);

                            entry              = default;
                            madeBoneOffsetGaps = true;
                            deadBoneEntryIndicesToClean.Add(op.oldDependentState.boneOffsetEntryIndex);
                        }
                    }
                }

                int meshRemoveCount = meshRemoveOpsBlockList.Count();
                var meshRemoveOps   = new NativeArray<MeshRemoveOperation>(meshRemoveCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                meshRemoveOpsBlockList.GetElementValues(meshRemoveOps);
                meshRemoveOps.Sort();

                bool madeMeshGaps = false;

                for (int i = 0; i < meshRemoveCount; i++)
                {
                    var op = meshRemoveOps[i];

                    ref var entry = ref meshManager.entries.ElementAt(op.oldBoundMeshState.meshEntryIndex);
                    entry.referenceCount--;
                    if (entry.referenceCount == 0)
                    {
                        var  blob        = entry.blob;
                        uint vertices    = entry.verticesCount;
                        uint weights     = entry.weightsCount;
                        uint bindPoses   = entry.bindPosesCount;
                        uint blendShapes = entry.blendShapesCount;
                        meshManager.verticesGaps.Add(new uint2(entry.verticesStart, vertices));
                        meshManager.weightsGaps.Add(new uint2(entry.weightsStart, weights));
                        meshManager.bindPosesGaps.Add(new uint2(entry.bindPosesStart, bindPoses));
                        meshManager.blendShapesGaps.Add(new uint2(entry.blendShapesStart, blendShapes));
                        meshManager.indexFreeList.Add(op.oldBoundMeshState.meshEntryIndex);
                        entry = default;

                        madeMeshGaps = true;

                        meshManager.blobIndexMap.Remove(blob);

                        if (!meshManager.uploadCommands.IsEmpty)
                        {
                            // This only happens if this system was invoked multiple times between renders and a mesh was added and removed in that time.
                            for (int j = 0; j < meshManager.uploadCommands.Length; j++)
                            {
                                if (meshManager.uploadCommands[j].blob == blob)
                                {
                                    requiredGpuSizes->requiredVertexUploadSize      -= vertices;
                                    requiredGpuSizes->requiredWeightUploadSize      -= weights;
                                    requiredGpuSizes->requiredBindPoseUploadSize    -= bindPoses;
                                    requiredGpuSizes->requiredBlendShapesUploadSize -= blendShapes;

                                    meshManager.uploadCommands.RemoveAtSwapBack(j);
                                    j--;
                                }
                            }
                        }
                    }
                }

                if (!deadBoneEntryIndicesToClean.IsEmpty)
                {
                    // Remove all path references from the pair map
                    var pairs = boneManager.pathPairToEntryMap.GetKeyValueArrays(Allocator.Temp);
                    for (int i = 0; i < pairs.Length; i++)
                    {
                        if (deadBoneEntryIndicesToClean.Contains(pairs.Values[i]))
                        {
                            boneManager.pathPairToEntryMap.Remove(pairs.Keys[i]);
                        }
                    }
                }

                // coellesce gaps
                if (madeMeshGaps)
                {
                    requiredGpuSizes->requiredVertexBufferSize      = CoellesceGaps(meshManager.verticesGaps, requiredGpuSizes->requiredVertexBufferSize);
                    requiredGpuSizes->requiredWeightBufferSize      = CoellesceGaps(meshManager.weightsGaps, requiredGpuSizes->requiredWeightBufferSize);
                    requiredGpuSizes->requiredBindPoseBufferSize    = CoellesceGaps(meshManager.bindPosesGaps, requiredGpuSizes->requiredBindPoseBufferSize);
                    requiredGpuSizes->requiredBlendShapesBufferSize = CoellesceGaps(meshManager.blendShapesGaps, requiredGpuSizes->requiredBlendShapesBufferSize);
                }
                if (madeBoneOffsetGaps)
                {
                    if (!boneManager.gaps.IsCreated)
                        UnityEngine.Debug.LogError("boneManager.gaps is not created currently. This is an internal bug.");
                    if (boneManager.gaps.Length < 0)
                        UnityEngine.Debug.LogError($"boneManager.gaps.Length is {boneManager.gaps.Length}. This is an internal bug.");
                    boneManager.offsets.Length = (int)CoellesceGaps(boneManager.gaps, (uint)boneManager.offsets.Length);
                }

                int skinnedAddCount = skinnedMeshAddOpsBlockList.Count();
                var skinnedAddOps   = new NativeArray<SkinnedMeshAddOperation>(skinnedAddCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                skinnedMeshAddOpsBlockList.GetElementValues(skinnedAddOps);
                skinnedAddOps.Sort();
                outputSkinnedWriteOps.Capacity    = skinnedAddOps.Length;
                NativeList<short> newOffsetsCache = new NativeList<short>(Allocator.Temp);
                for (int i = 0; i < skinnedAddCount; i++)
                {
                    var op   = skinnedAddOps[i];
                    var blob = op.meshBlob;

                    SkeletonDependent resultState = default;

                    // Binding analysis
                    {
                        if (op.overrideBoneBindings.IsCreated)
                        {
                            var hash = xxHash3.Hash64(op.overrideBoneBindings.Ptr, op.overrideBoneBindings.Length * sizeof(short));
                            if (boneManager.hashToEntryMap.TryGetValue(hash, out var entryIndex))
                            {
                                ref var entry = ref boneManager.entries.ElementAt(entryIndex);
                                entry.overridesReferences++;
                            }
                            else
                            {
                                var boneOffsetsEntry = new BoneOffsetsEntry
                                {
                                    count               = (ushort)op.overrideBoneBindings.Length,
                                    isValid             = true,
                                    hash                = hash,
                                    overridesReferences = 1,
                                    pathsReferences     = 0,
                                };

                                // Assume we only have 32bit indexing
                                if ((op.overrideBoneBindings.Length & 0x1) == 1)
                                    boneOffsetsEntry.gpuCount = (ushort)(op.overrideBoneBindings.Length + 1);
                                else
                                    boneOffsetsEntry.gpuCount = (ushort)op.overrideBoneBindings.Length;

                                if (AllocateInGap(boneManager.gaps, boneOffsetsEntry.gpuCount, out uint offsetWriteIndex))
                                {
                                    boneOffsetsEntry.start = offsetWriteIndex;
                                    for (uint j = 0; j < boneOffsetsEntry.count; j++)
                                        boneManager.offsets[(int)(j + offsetWriteIndex)] = op.overrideBoneBindings[(int)j];

                                    if (boneOffsetsEntry.count != boneOffsetsEntry.gpuCount)
                                        boneManager.offsets[(int)(offsetWriteIndex + boneOffsetsEntry.count)] = 0;
                                }
                                else
                                {
                                    boneOffsetsEntry.start = (uint)boneManager.offsets.Length;
                                    boneManager.offsets.AddRange(op.overrideBoneBindings.Ptr, boneOffsetsEntry.count);
                                    if (boneOffsetsEntry.count != boneOffsetsEntry.gpuCount)
                                        boneManager.offsets.Add(0);
                                }

                                if (boneManager.indexFreeList.IsEmpty)
                                {
                                    entryIndex = boneManager.entries.Length;
                                    boneManager.entries.Add(boneOffsetsEntry);
                                }
                                else
                                {
                                    entryIndex = boneManager.indexFreeList[0];
                                    boneManager.indexFreeList.RemoveAtSwapBack(0);
                                    boneManager.entries[entryIndex] = boneOffsetsEntry;
                                }

                                boneManager.hashToEntryMap.Add(hash, entryIndex);
                                boneManager.isDirty.Value = true;
                            }

                            op.overrideBoneBindings.Dispose();
                            resultState.skeletonBindingBlob  = default;
                            resultState.meshBindingBlob      = default;
                            resultState.boneOffsetEntryIndex = entryIndex;
                        }
                        else
                        {
                            var pathPair = new PathMappingPair { meshPaths = op.meshBindingPathsBlob, skeletonPaths = op.skeletonBindingPathsBlob };
                            if (boneManager.pathPairToEntryMap.TryGetValue(pathPair, out int entryIndex))
                            {
                                ref var entry = ref boneManager.entries.ElementAt(entryIndex);
                                entry.pathsReferences++;
                            }
                            else
                            {
                                if (op.meshBindingPathsBlob == BlobAssetReference<MeshBindingPathsBlob>.Null)
                                {
                                    UnityEngine.Debug.LogError(
                                        $"Cannot bind entity {op.meshEntity} to {op.root.entity}. MeshBindingPathsBlob was null.");

                                    outputSkinnedWriteOps.Add(new SkinnedMeshWriteStateOperation { meshEntity = op.meshEntity, skinnedState = default });
                                    continue;
                                }
                                else if (op.skeletonBindingPathsBlob == BlobAssetReference<SkeletonBindingPathsBlob>.Null)
                                {
                                    UnityEngine.Debug.LogError(
                                        $"Cannot bind entity {op.meshEntity} to {op.root.entity}. SkeletonBindingPathsBlob was null.");

                                    outputSkinnedWriteOps.Add(new SkinnedMeshWriteStateOperation { meshEntity = op.meshEntity, skinnedState = default });
                                    continue;
                                }
                                else if (!BindingUtilities.TrySolveBindings(op.meshBindingPathsBlob, op.skeletonBindingPathsBlob, newOffsetsCache, out int failedMeshIndex))
                                {
                                    FixedString4096Bytes failedPath = default;
                                    failedPath.Append((byte*)op.meshBindingPathsBlob.Value.pathsInReversedNotation[failedMeshIndex].GetUnsafePtr(),
                                                      op.meshBindingPathsBlob.Value.pathsInReversedNotation[failedMeshIndex].Length);
                                    UnityEngine.Debug.LogError(
                                        $"Cannot bind entity {op.meshEntity} to {op.root.entity}. No match for index {failedMeshIndex} requesting path: {failedPath}");

                                    outputSkinnedWriteOps.Add(new SkinnedMeshWriteStateOperation { meshEntity = op.meshEntity, skinnedState = default });
                                    continue;
                                }

                                var hash = xxHash3.Hash64(newOffsetsCache.GetUnsafeReadOnlyPtr(), newOffsetsCache.Length * sizeof(short));
                                if (boneManager.hashToEntryMap.TryGetValue(hash, out entryIndex))
                                {
                                    ref var entry = ref boneManager.entries.ElementAt(entryIndex);
                                    entry.pathsReferences++;
                                }
                                else
                                {
                                    var boneOffsetsEntry = new BoneOffsetsEntry
                                    {
                                        count               = (ushort)newOffsetsCache.Length,
                                        isValid             = true,
                                        hash                = hash,
                                        overridesReferences = 0,
                                        pathsReferences     = 1,
                                    };

                                    // Assume we only have 32bit indexing
                                    if ((newOffsetsCache.Length & 0x1) == 1)
                                        boneOffsetsEntry.gpuCount = (ushort)(newOffsetsCache.Length + 1);
                                    else
                                        boneOffsetsEntry.gpuCount = (ushort)newOffsetsCache.Length;

                                    if (AllocateInGap(boneManager.gaps, boneOffsetsEntry.gpuCount, out uint offsetWriteIndex))
                                    {
                                        boneOffsetsEntry.start = offsetWriteIndex;
                                        for (int j = 0; j < boneOffsetsEntry.count; j++)
                                            boneManager.offsets[(int)(j + offsetWriteIndex)] = newOffsetsCache[j];

                                        if (boneOffsetsEntry.count != boneOffsetsEntry.gpuCount)
                                            boneManager.offsets[(int)(offsetWriteIndex + boneOffsetsEntry.count)] = 0;
                                    }
                                    else
                                    {
                                        boneOffsetsEntry.start = (uint)boneManager.offsets.Length;
                                        boneManager.offsets.AddRange(newOffsetsCache.AsArray());
                                        if (boneOffsetsEntry.count != boneOffsetsEntry.gpuCount)
                                            boneManager.offsets.Add(0);
                                    }

                                    if (boneManager.indexFreeList.IsEmpty)
                                    {
                                        entryIndex = boneManager.entries.Length;
                                        boneManager.entries.Add(boneOffsetsEntry);
                                    }
                                    else
                                    {
                                        entryIndex = boneManager.indexFreeList[0];
                                        boneManager.indexFreeList.RemoveAtSwapBack(0);
                                        boneManager.entries[entryIndex] = boneOffsetsEntry;
                                    }

                                    boneManager.hashToEntryMap.Add(hash, entryIndex);
                                    boneManager.isDirty.Value = true;
                                }

                                boneManager.pathPairToEntryMap.Add(new PathMappingPair { meshPaths = op.meshBindingPathsBlob, skeletonPaths = op.skeletonBindingPathsBlob },
                                                                   entryIndex);
                            }
                            resultState.skeletonBindingBlob  = op.skeletonBindingPathsBlob;
                            resultState.meshBindingBlob      = op.meshBindingPathsBlob;
                            resultState.boneOffsetEntryIndex = entryIndex;
                        }
                    }

                    resultState.root                                                          = op.root;
                    outputSkinnedWriteOps.Add(new SkinnedMeshWriteStateOperation { meshEntity = op.meshEntity, skinnedState = resultState });
                }

                int meshAddCount = meshAddOpsBlockList.Count();
                var meshAddOps   = new NativeArray<MeshAddOperation>(meshAddCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                meshAddOpsBlockList.GetElementValues(meshAddOps);
                meshAddOps.Sort();
                outputWriteOps.Capacity = meshAddOps.Length;
                for (int i = 0; i < meshAddCount; i++)
                {
                    var op   = meshAddOps[i];
                    var blob = op.meshBlob;

                    BoundMesh resultState = default;

                    if (meshManager.blobIndexMap.TryGetValue(blob, out int meshIndex))
                    {
                        meshManager.entries.ElementAt(meshIndex).referenceCount++;
                    }
                    else
                    {
                        if (!meshManager.indexFreeList.IsEmpty)
                        {
                            meshIndex = meshManager.indexFreeList[0];
                            meshManager.indexFreeList.RemoveAtSwapBack(0);
                        }
                        else
                        {
                            meshIndex = meshManager.entries.Length;
                            meshManager.entries.Add(default);
                        }

                        uint verticesNeeded = (uint)blob.Value.undeformedVertices.Length;
                        uint verticesGpuStart;
                        if (AllocateInGap(meshManager.verticesGaps, verticesNeeded, out uint verticesWriteIndex))
                        {
                            verticesGpuStart = verticesWriteIndex;
                        }
                        else
                        {
                            verticesGpuStart                            = requiredGpuSizes->requiredVertexBufferSize;
                            requiredGpuSizes->requiredVertexBufferSize += verticesNeeded;
                        }

                        uint weightsNeeded = (uint)blob.Value.skinningData.boneWeights.Length;
                        uint weightsGpuStart;
                        if (AllocateInGap(meshManager.weightsGaps, weightsNeeded, out uint weightsWriteIndex))
                        {
                            weightsGpuStart = weightsWriteIndex;
                        }
                        else
                        {
                            weightsGpuStart                             = requiredGpuSizes->requiredWeightBufferSize;
                            requiredGpuSizes->requiredWeightBufferSize += weightsNeeded;
                        }

                        uint bindPosesNeeded = (uint)blob.Value.skinningData.bindPoses.Length + (uint)blob.Value.skinningData.bindPosesDQ.Length;
                        uint bindPosesGpuStart;
                        if (AllocateInGap(meshManager.bindPosesGaps, bindPosesNeeded, out uint bindPosesWriteIndex))
                        {
                            bindPosesGpuStart = bindPosesWriteIndex;
                        }
                        else
                        {
                            bindPosesGpuStart                             = requiredGpuSizes->requiredBindPoseBufferSize;
                            requiredGpuSizes->requiredBindPoseBufferSize += bindPosesNeeded;
                        }

                        uint blendShapesNeeded = (uint)blob.Value.blendShapesData.gpuData.Length;
                        uint blendShapesGpuStart;
                        if (AllocateInGap(meshManager.blendShapesGaps, blendShapesNeeded, out uint blendShapesWriteIndex))
                        {
                            blendShapesGpuStart = blendShapesWriteIndex;
                        }
                        else
                        {
                            blendShapesGpuStart                              = requiredGpuSizes->requiredBlendShapesBufferSize;
                            requiredGpuSizes->requiredBlendShapesBufferSize += blendShapesNeeded;
                        }

                        meshManager.blobIndexMap.Add(blob, meshIndex);
                        ref var meshEntry = ref meshManager.entries.ElementAt(meshIndex);
                        meshEntry.referenceCount++;
                        meshEntry.verticesStart    = verticesGpuStart;
                        meshEntry.weightsStart     = weightsGpuStart;
                        meshEntry.bindPosesStart   = bindPosesGpuStart;
                        meshEntry.blendShapesStart = blendShapesGpuStart;
                        meshEntry.blob             = blob;
                        meshEntry.verticesCount    = verticesNeeded;
                        meshEntry.weightsCount     = weightsNeeded;
                        meshEntry.bindPosesCount   = bindPosesNeeded;
                        meshEntry.blendShapesCount = blendShapesNeeded;

                        requiredGpuSizes->requiredVertexUploadSize      += verticesNeeded;
                        requiredGpuSizes->requiredWeightUploadSize      += weightsNeeded;
                        requiredGpuSizes->requiredBindPoseUploadSize    += bindPosesNeeded;
                        requiredGpuSizes->requiredBlendShapesUploadSize += blendShapesNeeded;

                        meshManager.uploadCommands.Add(new MeshGpuUploadCommand
                        {
                            blob             = blob,
                            verticesIndex    = verticesGpuStart,
                            weightsIndex     = weightsGpuStart,
                            bindPosesIndex   = bindPosesGpuStart,
                            blendShapesIndex = blendShapesGpuStart,
                        });
                    }
                    resultState.meshEntryIndex                                  = meshIndex;
                    resultState.meshBlob                                        = blob;
                    outputWriteOps.Add(new MeshWriteStateOperation { meshEntity = op.meshEntity, meshState = resultState });
                }
            }

            uint CoellesceGaps(NativeList<uint2> gaps, uint oldSize)
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

            bool AllocateInGap(NativeList<uint2> gaps, uint countNeeded, out uint foundIndex)
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
        #endregion

        #region Post Sync Jobs
        [BurstCompile]
        struct ProcessMeshStateOpsJob : IJobParallelForDefer
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<BoundMesh> stateLookup;
            [ReadOnly] public NativeArray<MeshWriteStateOperation>                  ops;

            public void Execute(int index)
            {
                var op                     = ops[index];
                stateLookup[op.meshEntity] = op.meshState;
            }
        }

        [BurstCompile]
        struct ProcessSkinnedMeshStateOpsJob : IJobParallelForDefer
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<SkeletonDependent> stateLookup;
            [NativeDisableParallelForRestriction] public ParentReadWriteAspect.Lookup       parentLookup;
            [ReadOnly] public NativeArray<SkinnedMeshWriteStateOperation>                   ops;
            public Entity                                                                   failedBindingEntity;

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            [NativeDisableParallelForRestriction] public ComponentLookup<Unity.Transforms.LocalTransform> localTransformLookup;
#endif
            public void Execute(int index)
            {
                var op                     = ops[index];
                stateLookup[op.meshEntity] = op.skinnedState;
                var parentAspect           = parentLookup[op.meshEntity];
                if (op.skinnedState.root == Entity.Null)
                    parentAspect.parent = failedBindingEntity;
                else
                    parentAspect.parent = op.skinnedState.root;

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                localTransformLookup[op.meshEntity] = Unity.Transforms.LocalTransform.Identity;
#endif
            }
        }

        [BurstCompile]
        struct ProcessBindingOpsJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<BindUnbindOperation>                         operations;
            [ReadOnly] public NativeArray<int2>                                        startsAndCounts;
            [ReadOnly] public BufferLookup<OptimizedBoneTransform>                     boneTransformLookup;
            [ReadOnly] public BufferLookup<BoneReference>                              boneRefsLookup;
            [ReadOnly] public ComponentLookup<BoundMesh>                               boundMeshLookup;
            [ReadOnly] public ComponentLookup<OptimizedSkeletonHierarchyBlobReference> hierarchyLookup;
            [ReadOnly] public MeshGpuManager                                           meshGpuManager;
            [ReadOnly] public BoneOffsetsGpuManager                                    boneOffsetsGpuManager;

            [NativeDisableParallelForRestriction] public BufferLookup<DependentSkinnedMesh> dependentsLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<OptimizedBoneBounds>  optimizedBoundsLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneBounds>        boneBoundsLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<SkeletonDependent> skeletonDependentLookup;

            [NativeDisableContainerSafetyRestriction, NoAlias] NativeList<float> boundsCache;

            public void Execute(int index)
            {
                int2 startAndCount = startsAndCounts[index];
                var  opsArray      = operations.GetSubArray(startAndCount.x, startAndCount.y);

                Entity skeletonEntity        = opsArray[0].targetEntity;
                var    depsBuffer            = dependentsLookup[skeletonEntity];
                bool   needsFullBoundsUpdate = false;
                bool   needsAddBoundsUpdate  = false;

                // Todo: This might be really slow
                int i = 0;
                for (; i < opsArray.Length && opsArray[i].opType == BindUnbindOperation.OpType.Unbind; i++)
                {
                    for (int j = 0; j < depsBuffer.Length; j++)
                    {
                        if (depsBuffer[j].skinnedMesh == opsArray[i].meshEntity)
                        {
                            depsBuffer.RemoveAtSwapBack(j);
                            needsFullBoundsUpdate = true;
                            break;
                        }
                    }
                }

                int addStart = i;
                for (; i < opsArray.Length && opsArray[i].opType == BindUnbindOperation.OpType.Bind; i++)
                {
                    var skinningState = skeletonDependentLookup[opsArray[i].meshEntity];
                    if (skinningState.root != Entity.Null)
                    {
                        var meshState        = boundMeshLookup[opsArray[i].meshEntity];
                        var meshEntry        = meshGpuManager.entries[meshState.meshEntryIndex];
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[skinningState.boneOffsetEntryIndex];
                        depsBuffer.Add(new DependentSkinnedMesh
                        {
                            skinnedMesh        = opsArray[i].meshEntity,
                            meshVerticesStart  = meshEntry.verticesStart,
                            meshWeightsStart   = meshEntry.weightsStart,
                            meshBindPosesStart = meshEntry.bindPosesStart,
                            boneOffsetsCount   = boneOffsetsEntry.count,
                            boneOffsetsStart   = boneOffsetsEntry.start,
                            meshRadialOffset   = 0f,
                        });
                        needsAddBoundsUpdate = true;
                    }
                }

                if (needsFullBoundsUpdate)
                {
                    RebindAllMeshBounds(skeletonEntity, depsBuffer);
                }
                else if (needsAddBoundsUpdate)
                {
                    AppendMeshBounds(skeletonEntity, opsArray, addStart, opsArray.Length - addStart);
                }

                for (int j = 0; j < depsBuffer.Length; j++)
                {
                    var skinningState                                         = skeletonDependentLookup.GetRefRW(depsBuffer[j].skinnedMesh);
                    skinningState.ValueRW.indexInDependentSkinnedMeshesBuffer = j;
                }
            }

            void AppendMeshBounds(Entity skeletonEntity, NativeArray<BindUnbindOperation> ops, int start, int count)
            {
                if (boneTransformLookup.HasBuffer(skeletonEntity))
                {
                    // Optimized skeleton path
                    var boundsBuffer = optimizedBoundsLookup[skeletonEntity];
                    if (boundsBuffer.IsEmpty)
                    {
                        if (hierarchyLookup.TryGetComponent(skeletonEntity, out var hierarchyRef))
                            boundsBuffer.Resize(hierarchyRef.blob.Value.parentIndices.Length, NativeArrayOptions.ClearMemory);
                        else
                            boundsBuffer.Resize(boneTransformLookup[skeletonEntity].Length / 6, NativeArrayOptions.ClearMemory);
                    }
                    var boundsArray = boundsBuffer.Reinterpret<float>().AsNativeArray();

                    for (int i = start; i < start + count; i++)
                    {
                        var skinningState = skeletonDependentLookup[ops[i].meshEntity];
                        if (skinningState.root == Entity.Null)
                            continue;
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[skinningState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray((int)boneOffsetsEntry.start, boneOffsetsEntry.count);

                        ref var blobBounds = ref boundMeshLookup[ops[i].meshEntity].meshBlob.Value.skinningData.maxRadialOffsetsInBoneSpaceByBone;
                        short   k          = 0;
                        foreach (var j in boneOffsets)
                        {
                            if (j >= boundsArray.Length)
                                UnityEngine.Debug.LogError(
                                    $"Skinned Mesh Entity {ops[i].meshEntity} specifies a boneSkinningIndex of {j} but OptimizedBoneTransform buffer on Entity {skeletonEntity} only has {boundsArray.Length} elements.");
                            else
                                boundsArray[j] = math.max(boundsArray[j], blobBounds[k]);
                            k++;
                        }
                    }
                }
                else
                {
                    // Exposed skeleton path
                    if (!boundsCache.IsCreated)
                    {
                        boundsCache = new NativeList<float>(Allocator.Temp);
                    }
                    var boneRefs = boneRefsLookup[skeletonEntity].Reinterpret<Entity>().AsNativeArray();
                    boundsCache.Clear();
                    boundsCache.Resize(boneRefs.Length, NativeArrayOptions.ClearMemory);
                    var boundsArray = boundsCache.AsArray();

                    for (int i = start; i < start + count; i++)
                    {
                        var skinningState = skeletonDependentLookup[ops[i].meshEntity];
                        if (skinningState.root == Entity.Null)
                            continue;
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[skinningState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray((int)boneOffsetsEntry.start, boneOffsetsEntry.count);

                        ref var blobBounds = ref boundMeshLookup[ops[i].meshEntity].meshBlob.Value.skinningData.maxRadialOffsetsInBoneSpaceByBone;
                        short   k          = 0;
                        foreach (var j in boneOffsets)
                        {
                            if (j >= boundsArray.Length)
                                UnityEngine.Debug.LogError(
                                    $"Skinned Mesh Entity {ops[i].meshEntity} specifies a boneSkinningIndex of {j} but BoneReference buffer on Entity {skeletonEntity} only has {boundsArray.Length} elements.");
                            else
                                boundsArray[j] = math.max(boundsArray[j], blobBounds[k]);
                            k++;
                        }
                    }

                    // Merge with new values
                    for (int i = 0; i < boundsArray.Length; i++)
                    {
                        var storedBounds              = boneBoundsLookup[boneRefs[i]];
                        boneBoundsLookup[boneRefs[i]] = new BoneBounds
                        {
                            radialOffsetInBoneSpace = math.max(boundsArray[i], storedBounds.radialOffsetInBoneSpace),
                        };
                    }
                }
            }

            void RebindAllMeshBounds(Entity skeletonEntity, DynamicBuffer<DependentSkinnedMesh> depsBuffer)
            {
                if (boneTransformLookup.HasBuffer(skeletonEntity))
                {
                    // Optimized skeleton path
                    var boundsBuffer = optimizedBoundsLookup[skeletonEntity];
                    if (boundsBuffer.IsEmpty)
                    {
                        boundsBuffer.Resize(boneTransformLookup[skeletonEntity].Length, NativeArrayOptions.ClearMemory);
                    }
                    var boundsArray = boundsBuffer.Reinterpret<float>().AsNativeArray();

                    bool needsCollapse = true;
                    for (int i = 0; i < depsBuffer.Length; i++)
                    {
                        var meshState = skeletonDependentLookup[depsBuffer[i].skinnedMesh];
                        if (meshState.root == Entity.Null)
                            continue;
                        needsCollapse        = false;
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[meshState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray((int)boneOffsetsEntry.start, boneOffsetsEntry.count);

                        ref var blobBounds = ref boundMeshLookup[depsBuffer[i].skinnedMesh].meshBlob.Value.skinningData.maxRadialOffsetsInBoneSpaceByBone;
                        short   k          = 0;
                        foreach (var j in boneOffsets)
                        {
                            if (j >= boundsArray.Length)
                                UnityEngine.Debug.LogError(
                                    $"Skinned Mesh Entity {depsBuffer[i].skinnedMesh} specifies a boneSkinningIndex of {j} but OptimizedBoneTransform buffer on Entity {skeletonEntity} only has {boundsArray.Length} elements.");
                            else
                                boundsArray[j] = math.max(boundsArray[j], blobBounds[k]);
                            k++;
                        }
                    }

                    if (needsCollapse)
                    {
                        boundsBuffer.Clear();
                    }
                }
                else
                {
                    // Exposed skeleton path
                    if (!boundsCache.IsCreated)
                    {
                        boundsCache = new NativeList<float>(Allocator.Temp);
                    }
                    var boneRefs = boneRefsLookup[skeletonEntity].Reinterpret<Entity>().AsNativeArray();
                    boundsCache.Clear();
                    boundsCache.Resize(boneRefs.Length, NativeArrayOptions.ClearMemory);
                    var boundsArray = boundsCache.AsArray();

                    for (int i = 0; i < depsBuffer.Length; i++)
                    {
                        var meshState = skeletonDependentLookup[depsBuffer[i].skinnedMesh];
                        if (meshState.root == Entity.Null)
                            continue;
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[meshState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray((int)boneOffsetsEntry.start, boneOffsetsEntry.count);

                        ref var blobBounds = ref boundMeshLookup[depsBuffer[i].skinnedMesh].meshBlob.Value.skinningData.maxRadialOffsetsInBoneSpaceByBone;
                        short   k          = 0;
                        foreach (var j in boneOffsets)
                        {
                            if (j >= boundsArray.Length)
                                UnityEngine.Debug.LogError(
                                    $"Skinned Mesh Entity {depsBuffer[i].skinnedMesh} specifies a boneSkinningIndex of {j} but BoneReference buffer on Entity {skeletonEntity} only has {boundsArray.Length} elements.");
                            else
                                boundsArray[j] = math.max(boundsArray[j], blobBounds[k]);
                            k++;
                        }
                    }

                    // Overwrite the bounds
                    for (int i = 0; i < boundsArray.Length; i++)
                    {
                        boneBoundsLookup[boneRefs[i]] = new BoneBounds { radialOffsetInBoneSpace = boundsArray[i] };
                    }
                }
            }
        }

        [BurstCompile]
        struct AllocateCullingIndicesToClearBitmaskJob : IJob
        {
            [ReadOnly] public ExposedCullingIndexManager cullingIndicesManager;
            public NativeReference<UnsafeBitArray>       cullingIndicesToClear;
            public Allocator                             allocator;

            public void Execute()
            {
                cullingIndicesToClear.Value = new UnsafeBitArray(cullingIndicesManager.maxIndex.Value + 1, allocator);
            }
        }

        // Schedule single
        [BurstCompile]
        struct FindExposedSkeletonsToUpdateJob : IJobChunk
        {
            public NativeList<ExposedSkeletonCullingIndexOperation> operations;
            public ExposedCullingIndexManager                       manager;
            public NativeReference<UnsafeBitArray>                  indicesToClear;

            [ReadOnly] public EntityTypeHandle                   entityHandle;
            public ComponentTypeHandle<BoneReferenceIsDirtyFlag> dirtyFlagHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities   = chunk.GetNativeArray(entityHandle);
                var dirtyFlags = chunk.GetEnabledMask(ref dirtyFlagHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (dirtyFlags[i])
                    {
                        var index = manager.skeletonToCullingIndexMap[entities[i]];
                        if (index < indicesToClear.Value.Length)
                            indicesToClear.Value.Set(index, true);
                        operations.Add(new ExposedSkeletonCullingIndexOperation { index = index, skeletonEntity = entities[i] });
                    }
                }
                chunk.SetComponentEnabledForAll(ref dirtyFlagHandle, false);
            }
        }

        [BurstCompile]
        struct ResetExposedBonesJob : IJobChunk
        {
            [ReadOnly] public NativeReference<UnsafeBitArray> indicesToClear;
            public ComponentTypeHandle<BoneCullingIndex>      indexHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var indices = chunk.GetNativeArray(ref indexHandle).Reinterpret<int>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    bool needsClearing = indices[i] < indicesToClear.Value.Length ? indicesToClear.Value.IsSet(indices[i]) : false;
                    indices[i]         = math.select(indices[i], 0, needsClearing);
                }
            }
        }

        [BurstCompile]
        struct SetExposedSkeletonCullingIndicesJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ExposedSkeletonCullingIndexOperation>                       operations;
            [ReadOnly] public BufferLookup<BoneReference>                                             boneRefsLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<ExposedSkeletonCullingIndex> skeletonCullingIndexLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneCullingIndex>            boneCullingIndexLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneIndex>                   boneIndexLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneOwningSkeletonReference> skeletonReferenceLookup;

            public void Execute(int index)
            {
                var op    = operations[index];
                var bones = boneRefsLookup[op.skeletonEntity].AsNativeArray();
                for (short i = 0; i < bones.Length; i++)
                {
                    if (boneCullingIndexLookup.HasComponent(bones[i].bone))
                        boneCullingIndexLookup[bones[i].bone] = new BoneCullingIndex { cullingIndex = op.index };
                    if (boneIndexLookup.HasComponent(bones[i].bone))
                        boneIndexLookup[bones[i].bone] = new BoneIndex { index = i };
                    if (skeletonReferenceLookup.HasComponent(bones[i].bone))
                        skeletonReferenceLookup[bones[i].bone] = new BoneOwningSkeletonReference { skeletonRoot = op.skeletonEntity };
                }
                skeletonCullingIndexLookup[op.skeletonEntity] = new ExposedSkeletonCullingIndex { cullingIndex = op.index };
            }
        }
        #endregion
    }
}

