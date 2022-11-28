using System;
using System.Collections.Generic;
using System.Diagnostics;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    [DisableAutoCreation]
    public partial struct SkeletonMeshBindingReactiveSystem : ISystem
    {
        EntityQuery m_newMeshesQuery;
        EntityQuery m_bindableMeshesQuery;
        EntityQuery m_deadMeshesQuery;
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

        Entity m_failedBindingEntity;

        LatiosWorldUnmanaged latiosWorld;

        EntityTypeHandle                                   m_entityHandle;
        BufferTypeHandle<DependentSkinnedMesh>             m_dependentSkinnedMeshHandleRO;
        ComponentTypeHandle<SkeletonDependent>             m_skeletonDependentHandleRO;
        ComponentTypeHandle<NeedsBindingFlag>              m_needsBindingFlagHandleRW;
        BufferTypeHandle<OverrideSkinningBoneIndex>        m_overrideSkinningBoneIndexHandleRO;
        ComponentTypeHandle<MeshBindingPathsBlobReference> m_meshBindingPathsBlobReferenceHandleRO;
        ComponentTypeHandle<MeshSkinningBlobReference>     m_meshSkinningBlobReferenceHandleRO;
        ComponentTypeHandle<ShaderEffectRadialBounds>      m_shaderEffectRadialBoundsHandleRO;
        ComponentTypeHandle<BindSkeletonRoot>              m_bindSkeletonRootHandleRO;
        ComponentTypeHandle<SkeletonDependent>             m_skeletonDependentHandleRW;
        ComponentTypeHandle<BoneReferenceIsDirtyFlag>      m_boneReferenceIsDirtyFlagHandleRW;
        ComponentTypeHandle<BoneCullingIndex>              m_boneCullingIndexHandleRW;

        void CreateHandles(ref SystemState state)
        {
            m_entityHandle                          = state.GetEntityTypeHandle();
            m_dependentSkinnedMeshHandleRO          = state.GetBufferTypeHandle<DependentSkinnedMesh>(true);
            m_skeletonDependentHandleRO             = state.GetComponentTypeHandle<SkeletonDependent>(true);
            m_needsBindingFlagHandleRW              = state.GetComponentTypeHandle<NeedsBindingFlag>(false);
            m_overrideSkinningBoneIndexHandleRO     = state.GetBufferTypeHandle<OverrideSkinningBoneIndex>(true);
            m_meshBindingPathsBlobReferenceHandleRO = state.GetComponentTypeHandle<MeshBindingPathsBlobReference>(true);
            m_meshSkinningBlobReferenceHandleRO     = state.GetComponentTypeHandle<MeshSkinningBlobReference>(true);
            m_shaderEffectRadialBoundsHandleRO      = state.GetComponentTypeHandle<ShaderEffectRadialBounds>(true);
            m_bindSkeletonRootHandleRO              = state.GetComponentTypeHandle<BindSkeletonRoot>(true);
            m_skeletonDependentHandleRW             = state.GetComponentTypeHandle<SkeletonDependent>(false);
            m_boneReferenceIsDirtyFlagHandleRW      = state.GetComponentTypeHandle<BoneReferenceIsDirtyFlag>(false);
            m_boneCullingIndexHandleRW              = state.GetComponentTypeHandle<BoneCullingIndex>(false);
        }

        void UpdateHandles(ref SystemState state)
        {
            m_entityHandle.Update(ref state);
            m_dependentSkinnedMeshHandleRO.Update(ref state);
            m_skeletonDependentHandleRO.Update(ref state);
            m_needsBindingFlagHandleRW.Update(ref state);
            m_overrideSkinningBoneIndexHandleRO.Update(ref state);
            m_meshBindingPathsBlobReferenceHandleRO.Update(ref state);
            m_meshSkinningBlobReferenceHandleRO.Update(ref state);
            m_shaderEffectRadialBoundsHandleRO.Update(ref state);
            m_bindSkeletonRootHandleRO.Update(ref state);
            m_skeletonDependentHandleRW.Update(ref state);
            m_boneReferenceIsDirtyFlagHandleRW.Update(ref state);
            m_boneCullingIndexHandleRW.Update(ref state);
        }

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            CreateHandles(ref state);

            m_newMeshesQuery = state.Fluent().WithAll<BindSkeletonRoot>(true).WithAll<MeshSkinningBlobReference>(true)
                               .Without<SkeletonDependent>()
                               .WithAny<MeshBindingPathsBlobReference>(true).WithAny<OverrideSkinningBoneIndex>(true).Build();

            m_bindableMeshesQuery = state.Fluent().WithAll<NeedsBindingFlag>().WithAll<BindSkeletonRoot>(true).WithAll<MeshSkinningBlobReference>(true).WithAll<SkeletonDependent>()
                                    .WithAny<MeshBindingPathsBlobReference>(true).WithAny<OverrideSkinningBoneIndex>(true).Build();

            m_deadMeshesQuery = state.Fluent().WithAll<SkeletonDependent>().Without<BindSkeletonRoot>().Without<MeshSkinningBlobReference>()
                                .Without<MeshBindingPathsBlobReference>().Without<OverrideSkinningBoneIndex>().Build();

            m_newSkeletonsQuery             = state.Fluent().WithAll<SkeletonRootTag>(true).Without<DependentSkinnedMesh>().Build();
            m_deadSkeletonsQuery            = state.Fluent().WithAll<DependentSkinnedMesh>().Without<SkeletonRootTag>().Build();
            m_aliveSkeletonsQuery           = state.Fluent().WithAll<SkeletonRootTag>(true).Build();
            m_newExposedSkeletonsQuery      = state.Fluent().WithAll<SkeletonRootTag>(true).Without<ExposedSkeletonCullingIndex>().WithAll<BoneReference>(true).Build();
            m_syncableExposedSkeletonsQuery = state.Fluent().WithAll<ExposedSkeletonCullingIndex>(true).WithAll<BoneReferenceIsDirtyFlag>(true).Build();
            m_deadExposedSkeletonsQuery     = state.Fluent().Without<BoneReference>().WithAll<ExposedSkeletonCullingIndex>().Build();
            m_deadExposedSkeletonsQuery2    = state.Fluent().Without<SkeletonRootTag>().WithAll<ExposedSkeletonCullingIndex>().Build();
            m_newOptimizedSkeletonsQuery    = state.Fluent().WithAll<SkeletonRootTag>(true).WithAll<OptimizedBoneToRoot>(true).Without<OptimizedSkeletonTag>().Build();
            m_deadOptimizedSkeletonsQuery   = state.Fluent().WithAll<OptimizedSkeletonTag>(true).Without<OptimizedBoneToRoot>().Build();
            m_deadOptimizedSkeletonsQuery2  = state.Fluent().WithAll<OptimizedSkeletonTag>(true).Without<SkeletonRootTag>().Build();
            m_cullableExposedBonesQuery     = state.Fluent().WithAll<BoneCullingIndex>().Build();

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new MeshGpuManager
            {
                blobIndexMap        = new NativeHashMap<BlobAssetReference<MeshSkinningBlob>, int>(128, Allocator.Persistent),
                entries             = new NativeList<MeshGpuEntry>(Allocator.Persistent),
                indexFreeList       = new NativeList<int>(Allocator.Persistent),
                verticesGaps        = new NativeList<int2>(Allocator.Persistent),
                weightsGaps         = new NativeList<int2>(Allocator.Persistent),
                bindPosesGaps       = new NativeList<int2>(Allocator.Persistent),
                requiredBufferSizes = new NativeReference<MeshGpuRequiredSizes>(Allocator.Persistent),
                uploadCommands      = new NativeList<MeshGpuUploadCommand>(Allocator.Persistent)
            });

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new BoneOffsetsGpuManager
            {
                entries            = new NativeList<BoneOffsetsEntry>(Allocator.Persistent),
                indexFreeList      = new NativeList<int>(Allocator.Persistent),
                offsets            = new NativeList<short>(Allocator.Persistent),
                gaps               = new NativeList<int2>(Allocator.Persistent),
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
            UpdateHandles(ref state);

            // Todo: It may be possible to defer all structural changes to a sync point.
            // Whether or not that is worth pursuing remains to be seen.
            bool haveNewMeshes                = !m_newMeshesQuery.IsEmptyIgnoreFilter;
            bool haveBindableMeshes           = !m_bindableMeshesQuery.IsEmptyIgnoreFilter;
            bool haveDeadMeshes               = !m_deadMeshesQuery.IsEmptyIgnoreFilter;
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
            bool requiresStructuralChange = haveNewMeshes | haveDeadMeshes | haveNewSkeletons | haveDeadSkeletons |
                                            haveNewExposedSkeletons | haveDeadExposedSkeletons | haveNewOptimizedSkeletons | haveDeadOptimizedSkeletons;

            bool requiresManagers = haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveNewMeshes | haveDeadMeshes | haveBindableMeshes;

            var allocator = state.WorldUpdateAllocator;

            UnsafeParallelBlockList bindingOpsBlockList    = default;
            UnsafeParallelBlockList meshAddOpsBlockList    = default;
            UnsafeParallelBlockList meshRemoveOpsBlockList = default;
            if (haveNewMeshes | haveDeadMeshes | haveBindableMeshes)
            {
                bindingOpsBlockList    = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<BindUnbindOperation>(), 128, Allocator.TempJob);
                meshAddOpsBlockList    = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<MeshAddOperation>(), 128, Allocator.TempJob);
                meshRemoveOpsBlockList = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<MeshRemoveOperation>(), 128, Allocator.TempJob);
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
            if (haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveBindableMeshes)
            {
                cullingOps            = new NativeList<ExposedSkeletonCullingIndexOperation>(0, allocator);
                cullingIndicesToClear = new NativeReference<UnsafeBitArray>(allocator);
            }

            var lastSystemVersion   = state.LastSystemVersion;
            var globalSystemVersion = state.GlobalSystemVersion;

            if (haveDeadSkeletons)
            {
                state.Dependency = new FindDeadSkeletonsJob
                {
                    dependentsHandle = m_dependentSkinnedMeshHandleRO,
                    meshStateLookup  = GetComponentLookup<SkeletonDependent>(false)
                }.ScheduleParallel(m_deadSkeletonsQuery, state.Dependency);
            }
            if (haveDeadMeshes)
            {
                state.Dependency = new FindDeadMeshesJob
                {
                    entityHandle           = m_entityHandle,
                    depsHandle             = m_skeletonDependentHandleRO,
                    bindingOpsBlockList    = bindingOpsBlockList,
                    meshRemoveOpsBlockList = meshRemoveOpsBlockList
                }.ScheduleParallel(m_deadMeshesQuery, state.Dependency);
            }

            if (haveNewMeshes || haveBindableMeshes)
            {
                var newMeshJob = new FindNewMeshesJob
                {
                    allocator                         = allocator,
                    bindingOpsBlockList               = bindingOpsBlockList,
                    bindSkeletonRootLookup            = GetComponentLookup<BindSkeletonRoot>(true),
                    boneOwningSkeletonReferenceLookup = GetComponentLookup<BoneOwningSkeletonReference>(true),
                    entityHandle                      = m_entityHandle,
                    meshAddOpsBlockList               = meshAddOpsBlockList,
                    needsBindingHandle                = m_needsBindingFlagHandleRW,
                    overrideBonesHandle               = m_overrideSkinningBoneIndexHandleRO,
                    pathBindingsBlobRefHandle         = m_meshBindingPathsBlobReferenceHandleRO,
                    skeletonBindingPathsBlobRefLookup = GetComponentLookup<SkeletonBindingPathsBlobReference>(true),
                    skeletonRootTagLookup             = GetComponentLookup<SkeletonRootTag>(true),
                    skinningBlobRefHandle             = m_meshSkinningBlobReferenceHandleRO,
                    radialBoundsHandle                = m_shaderEffectRadialBoundsHandleRO,
                    rootRefHandle                     = m_bindSkeletonRootHandleRO,
                };

                if (haveNewMeshes)
                {
                    state.Dependency = newMeshJob.ScheduleParallel(m_newMeshesQuery, state.Dependency);
                }
                if (haveBindableMeshes)
                {
                    state.Dependency = new FindRebindMeshesJob
                    {
                        depsHandle             = m_skeletonDependentHandleRW,
                        lastSystemVersion      = lastSystemVersion,
                        meshRemoveOpsBlockList = meshRemoveOpsBlockList,
                        newMeshesJob           = newMeshJob
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

            JobHandle                           meshBindingsJH            = default;
            NativeList<MeshWriteStateOperation> meshBindingsStatesToWrite = default;

            JobHandle                       skeletonBindingOpsJH              = default;
            NativeList<BindUnbindOperation> skeletonBindingOps                = default;
            NativeList<int2>                skeletonBindingOpsStartsAndCounts = default;
            if (haveNewMeshes || haveDeadMeshes || haveBindableMeshes)
            {
                int newMeshCount        = m_newMeshesQuery.CalculateEntityCountWithoutFiltering();
                int deadMeshCount       = m_deadMeshesQuery.CalculateEntityCountWithoutFiltering();
                int bindableMeshCount   = m_bindableMeshesQuery.CalculateEntityCountWithoutFiltering();
                int aliveSkeletonsCount = m_aliveSkeletonsQuery.CalculateEntityCountWithoutFiltering();

                meshBindingsStatesToWrite = new NativeList<MeshWriteStateOperation>(newMeshCount + bindableMeshCount, allocator);

                meshBindingsJH = new ProcessMeshGpuChangesJob
                {
                    meshAddOpsBlockList    = meshAddOpsBlockList,  // This is disposed here
                    meshRemoveOpsBlockList = meshRemoveOpsBlockList,  // This is disposed here
                    boneManager            = boneOffsetsGpuManager,
                    meshManager            = meshGpuManager,
                    outputWriteOps         = meshBindingsStatesToWrite
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
                // Kick the jobs so that the sorting happens while we do structural changes.
                // Todo: Does Complete already do this?
                JobHandle.ScheduleBatchedJobs();

                state.CompleteDependency();

                if (!state.EntityManager.Exists(m_failedBindingEntity))
                {
                    m_failedBindingEntity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponent(m_failedBindingEntity,
                                                     new ComponentTypeSet(ComponentType.ReadWrite<LocalToWorld>(), ComponentType.ReadWrite<FailedBindingsRootTag>()));
                    state.EntityManager.SetName(m_failedBindingEntity, "Failed Bindings Root");
                }

                var optimizedTypes = new FixedList128Bytes<ComponentType>();
                optimizedTypes.Add(ComponentType.ReadWrite<PerFrameSkeletonBufferMetadata>());
                optimizedTypes.Add(ComponentType.ReadWrite<OptimizedBoneToRoot>());
                optimizedTypes.Add(ComponentType.ReadWrite<OptimizedSkeletonTag>());
                optimizedTypes.Add(ComponentType.ReadWrite<SkeletonShaderBoundsOffset>());
                optimizedTypes.Add(ComponentType.ReadWrite<SkeletonWorldBounds>());
                optimizedTypes.Add(ComponentType.ReadWrite<OptimizedBoneBounds>());
                optimizedTypes.Add(ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>());
                optimizedTypes.Add(ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingSplitsMask>());
                optimizedTypes.Add(ComponentType.ChunkComponent<ChunkSkeletonWorldBounds>());

                state.EntityManager.RemoveComponent<SkeletonDependent>(m_deadMeshesQuery);
                state.EntityManager.RemoveComponent(                   m_deadOptimizedSkeletonsQuery, new ComponentTypeSet(optimizedTypes));
                state.EntityManager.RemoveComponent(                   m_deadExposedSkeletonsQuery,
                                                                       new ComponentTypeSet(ComponentType.ReadWrite<ExposedSkeletonCullingIndex>(),
                                                                                            ComponentType.ReadWrite<PerFrameSkeletonBufferMetadata>(),
                                                                                            ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>(),
                                                                                            ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingSplitsMask>()));
                state.EntityManager.RemoveComponent<DependentSkinnedMesh>(m_deadSkeletonsQuery);

                var transformComponentsThatWriteToLocalToParent = new FixedList128Bytes<ComponentType>();
                // Having both causes the mesh to not render in some circumstances. Still need to investigate how this happens.
                transformComponentsThatWriteToLocalToParent.Add(ComponentType.ReadWrite<CopyLocalToParentFromBone>());
                transformComponentsThatWriteToLocalToParent.Add(ComponentType.ReadWrite<Translation>());
                transformComponentsThatWriteToLocalToParent.Add(ComponentType.ReadWrite<Rotation>());
                transformComponentsThatWriteToLocalToParent.Add(ComponentType.ReadWrite<Scale>());
                transformComponentsThatWriteToLocalToParent.Add(ComponentType.ReadWrite<NonUniformScale>());
                transformComponentsThatWriteToLocalToParent.Add(ComponentType.ReadWrite<ParentScaleInverse>());
                transformComponentsThatWriteToLocalToParent.Add(ComponentType.ReadWrite<CompositeRotation>());
                transformComponentsThatWriteToLocalToParent.Add(ComponentType.ReadWrite<CompositeScale>());
                state.EntityManager.RemoveComponent(m_newMeshesQuery, new ComponentTypeSet(transformComponentsThatWriteToLocalToParent));
                state.EntityManager.AddComponent(m_newMeshesQuery,
                                                 new ComponentTypeSet(ComponentType.ReadWrite<SkeletonDependent>(), ComponentType.ReadWrite<LocalToParent>(),
                                                                      ComponentType.ReadWrite<Parent>()));

                optimizedTypes.Add(ComponentType.ReadWrite<DependentSkinnedMesh>());

                state.EntityManager.AddComponent(m_newOptimizedSkeletonsQuery, new ComponentTypeSet(optimizedTypes));
                state.EntityManager.AddComponent(m_newExposedSkeletonsQuery,
                                                 new ComponentTypeSet(ComponentType.ReadWrite<DependentSkinnedMesh>(),
                                                                      ComponentType.ReadWrite<PerFrameSkeletonBufferMetadata>(),
                                                                      ComponentType.ReadWrite<ExposedSkeletonCullingIndex>(),
                                                                      ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>(),
                                                                      ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingSplitsMask>()));

                state.EntityManager.AddComponent<DependentSkinnedMesh>(m_newSkeletonsQuery);
            }

            if (haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveNewMeshes | haveBindableMeshes | haveDeadMeshes)
            {
                var jhs = new NativeList<JobHandle>(4, Allocator.Temp);
                jhs.Add(cullingJH);
                jhs.Add(meshBindingsJH);
                jhs.Add(skeletonBindingOpsJH);
                jhs.Add(state.Dependency);
                state.Dependency = JobHandle.CombineDependencies(jhs.AsArray());
            }

            if (haveNewMeshes | haveBindableMeshes)
            {
                state.Dependency = new ProcessMeshStateOpsJob
                {
                    failedBindingEntity = m_failedBindingEntity,
                    ops                 = meshBindingsStatesToWrite.AsDeferredJobArray(),
                    parentLookup        = GetComponentLookup<Parent>(false),
                    localToParentLookup = GetComponentLookup<LocalToParent>(false),
                    stateLookup         = GetComponentLookup<SkeletonDependent>(false)
                }.Schedule(meshBindingsStatesToWrite, 16, state.Dependency);
            }

            if (haveNewMeshes | haveBindableMeshes | haveDeadMeshes)
            {
                state.Dependency = new ProcessBindingOpsJob
                {
                    boneBoundsLookup            = GetComponentLookup<BoneBounds>(false),
                    boneOffsetsGpuManager       = boneOffsetsGpuManager,
                    boneRefsLookup              = GetBufferLookup<BoneReference>(true),
                    boneToRootsLookup           = GetBufferLookup<OptimizedBoneToRoot>(true),
                    dependentsLookup            = GetBufferLookup<DependentSkinnedMesh>(false),
                    meshGpuManager              = meshGpuManager,
                    meshStateLookup             = GetComponentLookup<SkeletonDependent>(true),
                    operations                  = skeletonBindingOps.AsDeferredJobArray(),
                    optimizedBoundsLookup       = GetBufferLookup<OptimizedBoneBounds>(false),
                    optimizedShaderBoundsLookup = GetComponentLookup<SkeletonShaderBoundsOffset>(false),
                    startsAndCounts             = skeletonBindingOpsStartsAndCounts.AsDeferredJobArray()
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

                m_boneReferenceIsDirtyFlagHandleRW.Update(ref state);
                state.Dependency = new FindExposedSkeletonsToUpdateJob
                {
                    dirtyFlagHandle   = m_boneReferenceIsDirtyFlagHandleRW,
                    entityHandle      = m_entityHandle,
                    indicesToClear    = cullingIndicesToClear,
                    lastSystemVersion = lastSystemVersion,
                    manager           = cullingIndicesManager,
                    operations        = cullingOps
                }.Schedule(m_syncableExposedSkeletonsQuery, state.Dependency);
            }

            if ((haveSyncableExposedSkeletons | haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2) && haveCullableExposedBones)
            {
                m_boneCullingIndexHandleRW.Update(ref state);
                state.Dependency = new ResetExposedBonesJob
                {
                    indexHandle    = m_boneCullingIndexHandleRW,
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

        struct MeshAddOperation : IComparable<MeshAddOperation>
        {
            public Entity                                       meshEntity;
            public EntityWith<SkeletonRootTag>                  root;
            public BlobAssetReference<MeshSkinningBlob>         skinningBlob;
            public BlobAssetReference<MeshBindingPathsBlob>     meshBindingPathsBlob;
            public BlobAssetReference<SkeletonBindingPathsBlob> skeletonBindingPathsBlob;
            public UnsafeList<short>                            overrideBoneBindings;
            public float                                        shaderEffectRadialBounds;

            public int CompareTo(MeshAddOperation other) => meshEntity.CompareTo(other.meshEntity);
        }

        struct MeshRemoveOperation : IComparable<MeshRemoveOperation>
        {
            public Entity            meshEntity;
            public SkeletonDependent oldState;

            public int CompareTo(MeshRemoveOperation other) => meshEntity.CompareTo(other.meshEntity);
        }

        struct MeshWriteStateOperation
        {
            public EntityWith<SkeletonDependent> meshEntity;
            public SkeletonDependent             state;
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
        struct FindDeadMeshesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                       entityHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent> depsHandle;

            public UnsafeParallelBlockList bindingOpsBlockList;
            public UnsafeParallelBlockList meshRemoveOpsBlockList;
            [NativeSetThreadIndex] int     m_nativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(entityHandle);
                var deps     = chunk.GetNativeArray(ref depsHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    // If the mesh is in a valid state, this is not null.
                    if (deps[i].skinningBlob != BlobAssetReference<MeshSkinningBlob>.Null)
                    {
                        // However, the mesh could still have an invalid skeleton if the skeleton died.
                        var target = deps[i].root;
                        if (target != Entity.Null)
                        {
                            bindingOpsBlockList.Write(new BindUnbindOperation { targetEntity = target, meshEntity = entities[i], opType = BindUnbindOperation.OpType.Unbind },
                                                      m_nativeThreadIndex);
                        }
                        meshRemoveOpsBlockList.Write(new MeshRemoveOperation { meshEntity = entities[i], oldState = deps[i] }, m_nativeThreadIndex);
                    }
                }
            }
        }

        [BurstCompile]
        struct FindNewMeshesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                                   entityHandle;
            [ReadOnly] public ComponentTypeHandle<BindSkeletonRoot>              rootRefHandle;
            [ReadOnly] public ComponentTypeHandle<MeshSkinningBlobReference>     skinningBlobRefHandle;
            [ReadOnly] public ComponentLookup<SkeletonRootTag>                   skeletonRootTagLookup;
            [ReadOnly] public ComponentLookup<BindSkeletonRoot>                  bindSkeletonRootLookup;
            [ReadOnly] public ComponentLookup<BoneOwningSkeletonReference>       boneOwningSkeletonReferenceLookup;
            [ReadOnly] public ComponentLookup<SkeletonBindingPathsBlobReference> skeletonBindingPathsBlobRefLookup;

            // Optional
            [ReadOnly] public ComponentTypeHandle<MeshBindingPathsBlobReference> pathBindingsBlobRefHandle;
            [ReadOnly] public BufferTypeHandle<OverrideSkinningBoneIndex>        overrideBonesHandle;
            [ReadOnly] public ComponentTypeHandle<ShaderEffectRadialBounds>      radialBoundsHandle;
            public ComponentTypeHandle<NeedsBindingFlag>                         needsBindingHandle;

            public UnsafeParallelBlockList bindingOpsBlockList;
            public UnsafeParallelBlockList meshAddOpsBlockList;
            [NativeSetThreadIndex] int     m_nativeThreadIndex;

            public Allocator allocator;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var lower = new BitField64(~0UL);
                var upper = new BitField64(~0UL);
                if (chunk.Has(ref needsBindingHandle))
                {
                    var needs = chunk.GetNativeArray(ref needsBindingHandle);
                    for (int i = 0; i < math.min(chunk.Count, 64); i++)
                    {
                        lower.SetBits(i, needs[i].needsBinding);
                        needs[i] = new NeedsBindingFlag { needsBinding = false };
                    }
                    for (int i = 0; i + 64 < chunk.Count; i++)
                    {
                        upper.SetBits(i, needs[i + 64].needsBinding);
                        needs[i + 64] = new NeedsBindingFlag { needsBinding = false };
                    }
                    if ((lower.Value | upper.Value) == 0)
                        return;
                }

                var entities      = chunk.GetNativeArray(entityHandle);
                var rootRefs      = chunk.GetNativeArray(ref rootRefHandle);
                var skinningBlobs = chunk.GetNativeArray(ref skinningBlobRefHandle);

                var hasPathBindings  = chunk.Has(ref pathBindingsBlobRefHandle);
                var hasOverrideBones = chunk.Has(ref overrideBonesHandle);
                var hasRadialBounds  = chunk.Has(ref radialBoundsHandle);
                var pathBindings     = hasPathBindings ? chunk.GetNativeArray(ref pathBindingsBlobRefHandle) : default;
                var overrideBones    = hasOverrideBones ? chunk.GetBufferAccessor(ref overrideBonesHandle) : default;
                var radialBounds     = hasRadialBounds ? chunk.GetNativeArray(ref radialBoundsHandle) : default;

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!(i >= 64 ? upper.IsSet(i - 64) : lower.IsSet(i)))
                        continue;

                    var entity = entities[i];

                    var root = rootRefs[i].root;
                    if (root == Entity.Null)
                        continue;

                    if (!root.IsValid(skeletonRootTagLookup))
                    {
                        bool found = false;

                        if (boneOwningSkeletonReferenceLookup.HasComponent(root))
                        {
                            var skelRef = boneOwningSkeletonReferenceLookup[root];
                            if (skelRef.skeletonRoot != Entity.Null)
                            {
                                found = true;
                                root  = skelRef.skeletonRoot;
                            }
                        }

                        if (!found && bindSkeletonRootLookup.HasComponent(root))
                        {
                            var skelRef = bindSkeletonRootLookup[root];
                            if (skelRef.root != Entity.Null)
                            {
                                found = true;
                                root  = skelRef.root;
                            }
                        }

                        if (!found)
                        {
                            UnityEngine.Debug.LogError(
                                $"Skinned Mesh Entity {entity} attempted to bind to entity {root.entity}, but the latter is not a skeleton nor references one.");
                            continue;
                        }
                    }

                    var skinningBlob = skinningBlobs[i].blob;
                    if (skinningBlob == BlobAssetReference<MeshSkinningBlob>.Null)
                        continue;

                    var radial = hasRadialBounds ? radialBounds[i].radialBounds : 0f;

                    var pathsBlob         = BlobAssetReference<MeshBindingPathsBlob>.Null;
                    var skeletonPathsBlob = BlobAssetReference<SkeletonBindingPathsBlob>.Null;
                    if (hasPathBindings)
                    {
                        pathsBlob = pathBindings[i].blob;
                        if (pathsBlob != BlobAssetReference<MeshBindingPathsBlob>.Null && !hasOverrideBones)
                        {
                            var numPaths = pathsBlob.Value.pathsInReversedNotation.Length;
                            var numPoses = skinningBlob.Value.bindPoses.Length;
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

                    UnsafeList<short> bonesList = default;
                    if (hasOverrideBones)
                    {
                        var bonesBuffer = overrideBones[i];
                        var numPoses    = skinningBlob.Value.bindPoses.Length;
                        if (bonesBuffer.Length != numPoses)
                        {
                            UnityEngine.Debug.LogError(
                                $"Skinned Mesh Entity {entity} does not have the required number of override bones. Has: {bonesBuffer.Length}, Requires: {numPoses}");
                            continue;
                        }

                        bonesList = new UnsafeList<short>(numPoses, allocator);
                        bonesList.AddRangeNoResize(bonesBuffer.GetUnsafeReadOnlyPtr(), numPoses);
                    }

                    meshAddOpsBlockList.Write(new MeshAddOperation
                    {
                        meshEntity               = entity,
                        root                     = root,
                        skinningBlob             = skinningBlob,
                        meshBindingPathsBlob     = pathsBlob,
                        overrideBoneBindings     = bonesList,
                        shaderEffectRadialBounds = radial,
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

        [BurstCompile]
        struct FindRebindMeshesJob : IJobChunk
        {
            public FindNewMeshesJob                       newMeshesJob;
            public ComponentTypeHandle<SkeletonDependent> depsHandle;

            public UnsafeParallelBlockList meshRemoveOpsBlockList;

            public uint lastSystemVersion;

            [NativeSetThreadIndex] int m_nativeThreadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // The general strategy here is to unbind anything requesting a rebind
                // and then to treat it like a new mesh using that job's struct.
                if (!chunk.DidChange(ref newMeshesJob.needsBindingHandle, lastSystemVersion))
                    return;

                {
                    // New scope so that the compiler doesn't keep these variables on the stack when running the NewMesh job.
                    var entities = chunk.GetNativeArray(newMeshesJob.entityHandle);
                    var deps     = chunk.GetNativeArray(ref depsHandle);
                    var needs    = chunk.GetNativeArray(ref newMeshesJob.needsBindingHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        // If the mesh is in a valid state, this is not null.
                        if (deps[i].skinningBlob != BlobAssetReference<MeshSkinningBlob>.Null && needs[i].needsBinding)
                        {
                            // However, the mesh could still have an invalid skeleton if the skeleton died.
                            var target = deps[i].root;
                            if (target != Entity.Null)
                            {
                                newMeshesJob.bindingOpsBlockList.Write(new BindUnbindOperation {
                                    targetEntity = target, meshEntity = entities[i], opType = BindUnbindOperation.OpType.Unbind
                                },
                                                                       m_nativeThreadIndex);
                            }
                            meshRemoveOpsBlockList.Write(new MeshRemoveOperation { meshEntity = entities[i], oldState = deps[i] }, m_nativeThreadIndex);

                            // We need to wipe our state clean in case the rebinding fails.
                            deps[i] = default;
                        }
                    }
                }

                newMeshesJob.Execute(chunk, unfilteredChunkIndex, useEnabledMask, chunkEnabledMask);
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
            public MeshGpuManager          meshManager;
            public BoneOffsetsGpuManager   boneManager;

            public NativeList<MeshWriteStateOperation> outputWriteOps;

            public unsafe void Execute()
            {
                int removeCount = meshRemoveOpsBlockList.Count();
                var removeOps   = new NativeArray<MeshRemoveOperation>(removeCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                meshRemoveOpsBlockList.GetElementValues(removeOps);
                meshRemoveOpsBlockList.Dispose();
                removeOps.Sort();

                MeshGpuRequiredSizes* requiredGpuSizes = (MeshGpuRequiredSizes*)meshManager.requiredBufferSizes.GetUnsafePtr();

                bool madeMeshGaps       = false;
                bool madeBoneOffsetGaps = false;

                var deadBoneEntryIndicesToClean = new NativeHashSet<int>(128, Allocator.Temp);
                for (int i = 0; i < removeCount; i++)
                {
                    var op = removeOps[i];

                    {
                        ref var entry = ref meshManager.entries.ElementAt(op.oldState.meshEntryIndex);
                        entry.referenceCount--;
                        if (entry.referenceCount == 0)
                        {
                            var blob      = entry.blob;
                            int vertices  = entry.verticesCount;
                            int weights   = entry.weightsCount;
                            int bindPoses = entry.bindPosesCount;
                            meshManager.verticesGaps.Add(new int2(entry.verticesStart, vertices));
                            meshManager.weightsGaps.Add(new int2(entry.weightsStart, weights));
                            meshManager.bindPosesGaps.Add(new int2(entry.bindPosesStart, bindPoses));
                            meshManager.indexFreeList.Add(op.oldState.meshEntryIndex);
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
                                        requiredGpuSizes->requiredVertexUploadSize   -= vertices;
                                        requiredGpuSizes->requiredWeightUploadSize   -= weights;
                                        requiredGpuSizes->requiredBindPoseUploadSize -= bindPoses;

                                        meshManager.uploadCommands.RemoveAtSwapBack(j);
                                        j--;
                                    }
                                }
                            }
                        }
                    }

                    {
                        ref var entry = ref boneManager.entries.ElementAt(op.oldState.boneOffsetEntryIndex);
                        if (op.oldState.meshBindingBlob != BlobAssetReference<MeshBindingPathsBlob>.Null)
                            entry.pathsReferences--;
                        else
                            entry.overridesReferences--;

                        if (entry.pathsReferences == 0 && entry.overridesReferences == 0)
                        {
                            boneManager.gaps.Add(new int2(entry.start, entry.gpuCount));
                            boneManager.hashToEntryMap.Remove(entry.hash);
                            boneManager.indexFreeList.Add(op.oldState.boneOffsetEntryIndex);

                            entry              = default;
                            madeBoneOffsetGaps = true;
                            deadBoneEntryIndicesToClean.Add(op.oldState.boneOffsetEntryIndex);
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
                    requiredGpuSizes->requiredVertexBufferSize   = CoellesceGaps(meshManager.verticesGaps, requiredGpuSizes->requiredVertexBufferSize);
                    requiredGpuSizes->requiredWeightBufferSize   = CoellesceGaps(meshManager.weightsGaps, requiredGpuSizes->requiredWeightBufferSize);
                    requiredGpuSizes->requiredBindPoseBufferSize = CoellesceGaps(meshManager.bindPosesGaps, requiredGpuSizes->requiredBindPoseBufferSize);
                }
                if (madeBoneOffsetGaps)
                {
                    if (!boneManager.gaps.IsCreated)
                        UnityEngine.Debug.LogError("boneManager.gaps is not created currently. This is an internal bug.");
                    if (boneManager.gaps.Length < 0)
                        UnityEngine.Debug.LogError($"boneManager.gaps.Length is {boneManager.gaps.Length}. This is an internal bug.");
                    boneManager.offsets.Length = CoellesceGaps(boneManager.gaps, boneManager.offsets.Length);
                }

                int addCount = meshAddOpsBlockList.Count();
                var addOps   = new NativeArray<MeshAddOperation>(addCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                meshAddOpsBlockList.GetElementValues(addOps);
                meshAddOpsBlockList.Dispose();
                addOps.Sort();
                outputWriteOps.Capacity           = addOps.Length;
                NativeList<short> newOffsetsCache = new NativeList<short>(Allocator.Temp);
                for (int i = 0; i < addCount; i++)
                {
                    var op   = addOps[i];
                    var blob = op.skinningBlob;

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
                                    count               = (short)op.overrideBoneBindings.Length,
                                    isValid             = true,
                                    hash                = hash,
                                    overridesReferences = 1,
                                    pathsReferences     = 0,
                                };

                                // Assume we only have 32bit indexing
                                if ((op.overrideBoneBindings.Length & 0x1) == 1)
                                    boneOffsetsEntry.gpuCount = (short)(op.overrideBoneBindings.Length + 1);
                                else
                                    boneOffsetsEntry.gpuCount = (short)op.overrideBoneBindings.Length;

                                if (AllocateInGap(boneManager.gaps, boneOffsetsEntry.gpuCount, out int offsetWriteIndex))
                                {
                                    boneOffsetsEntry.start = offsetWriteIndex;
                                    for (int j = 0; j < boneOffsetsEntry.count; j++)
                                        boneManager.offsets[j + offsetWriteIndex] = op.overrideBoneBindings[j];

                                    if (boneOffsetsEntry.count != boneOffsetsEntry.gpuCount)
                                        boneManager.offsets[offsetWriteIndex + boneOffsetsEntry.count] = 0;
                                }
                                else
                                {
                                    boneOffsetsEntry.start = boneManager.offsets.Length;
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
                                if (!BindingUtilities.TrySolveBindings(op.meshBindingPathsBlob, op.skeletonBindingPathsBlob, newOffsetsCache, out int failedMeshIndex))
                                {
                                    FixedString4096Bytes failedPath = default;
                                    failedPath.Append((byte*)op.meshBindingPathsBlob.Value.pathsInReversedNotation[failedMeshIndex].GetUnsafePtr(),
                                                      op.meshBindingPathsBlob.Value.pathsInReversedNotation[failedMeshIndex].Length);
                                    UnityEngine.Debug.LogError(
                                        $"Cannot bind entity {op.meshEntity} to {op.root.entity}. No match for index {failedMeshIndex} requesting path: {failedPath}");

                                    outputWriteOps.Add(new MeshWriteStateOperation { meshEntity = op.meshEntity, state = default });
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
                                        count               = (short)newOffsetsCache.Length,
                                        isValid             = true,
                                        hash                = hash,
                                        overridesReferences = 0,
                                        pathsReferences     = 1,
                                    };

                                    // Assume we only have 32bit indexing
                                    if ((newOffsetsCache.Length & 0x1) == 1)
                                        boneOffsetsEntry.gpuCount = (short)(newOffsetsCache.Length + 1);
                                    else
                                        boneOffsetsEntry.gpuCount = (short)newOffsetsCache.Length;

                                    if (AllocateInGap(boneManager.gaps, boneOffsetsEntry.gpuCount, out int offsetWriteIndex))
                                    {
                                        boneOffsetsEntry.start = offsetWriteIndex;
                                        for (int j = 0; j < boneOffsetsEntry.count; j++)
                                            boneManager.offsets[j + offsetWriteIndex] = newOffsetsCache[j];

                                        if (boneOffsetsEntry.count != boneOffsetsEntry.gpuCount)
                                            boneManager.offsets[offsetWriteIndex + boneOffsetsEntry.count] = 0;
                                    }
                                    else
                                    {
                                        boneOffsetsEntry.start = boneManager.offsets.Length;
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

                        int verticesNeeded = blob.Value.verticesToSkin.Length;
                        int verticesGpuStart;
                        if (AllocateInGap(meshManager.verticesGaps, verticesNeeded, out int verticesWriteIndex))
                        {
                            verticesGpuStart = verticesWriteIndex;
                        }
                        else
                        {
                            verticesGpuStart                            = requiredGpuSizes->requiredVertexBufferSize;
                            requiredGpuSizes->requiredVertexBufferSize += verticesNeeded;
                        }

                        int weightsNeeded = blob.Value.boneWeights.Length;
                        int weightsGpuStart;
                        if (AllocateInGap(meshManager.weightsGaps, verticesNeeded, out int weightsWriteIndex))
                        {
                            weightsGpuStart = weightsWriteIndex;
                        }
                        else
                        {
                            weightsGpuStart                             = requiredGpuSizes->requiredWeightBufferSize;
                            requiredGpuSizes->requiredWeightBufferSize += weightsNeeded;
                        }

                        int bindPosesNeeded = blob.Value.bindPoses.Length;
                        int bindPosesGpuStart;
                        if (AllocateInGap(meshManager.bindPosesGaps, verticesNeeded, out int bindPosesWriteIndex))
                        {
                            bindPosesGpuStart = bindPosesWriteIndex;
                        }
                        else
                        {
                            bindPosesGpuStart                             = requiredGpuSizes->requiredBindPoseBufferSize;
                            requiredGpuSizes->requiredBindPoseBufferSize += bindPosesNeeded;
                        }

                        meshManager.blobIndexMap.Add(blob, meshIndex);
                        ref var meshEntry = ref meshManager.entries.ElementAt(meshIndex);
                        meshEntry.referenceCount++;
                        meshEntry.verticesStart  = verticesGpuStart;
                        meshEntry.weightsStart   = weightsGpuStart;
                        meshEntry.bindPosesStart = bindPosesGpuStart;
                        meshEntry.blob           = blob;
                        meshEntry.verticesCount  = verticesNeeded;
                        meshEntry.weightsCount   = weightsNeeded;
                        meshEntry.bindPosesCount = bindPosesNeeded;

                        requiredGpuSizes->requiredVertexUploadSize   += blob.Value.verticesToSkin.Length;
                        requiredGpuSizes->requiredWeightUploadSize   += blob.Value.boneWeights.Length;
                        requiredGpuSizes->requiredBindPoseUploadSize += blob.Value.bindPoses.Length;
                        //meshManager.requiredVertexWeightsbufferSizesAndUploadSizes.Value += new int4(0, 0, blob.Value.verticesToSkin.Length, blob.Value.boneWeights.Length);
                        meshManager.uploadCommands.Add(new MeshGpuUploadCommand
                        {
                            blob           = blob,
                            verticesIndex  = verticesGpuStart,
                            weightsIndex   = weightsGpuStart,
                            bindPosesIndex = bindPosesGpuStart
                        });
                    }
                    resultState.meshEntryIndex                                  = meshIndex;
                    resultState.skinningBlob                                    = blob;
                    resultState.root                                            = op.root;
                    resultState.shaderEffectRadialBounds                        = op.shaderEffectRadialBounds;
                    outputWriteOps.Add(new MeshWriteStateOperation { meshEntity = op.meshEntity, state = resultState });
                }
            }

            int CoellesceGaps(NativeList<int2> gaps, int oldSize)
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

            bool AllocateInGap(NativeList<int2> gaps, int countNeeded, out int foundIndex)
            {
                int bestIndex = -1;
                int bestCount = int.MaxValue;

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
                    foundIndex = -1;
                    return false;
                }

                if (bestCount == countNeeded)
                {
                    foundIndex = gaps[bestIndex].x;
                    gaps.RemoveAtSwapBack(bestIndex);
                    return true;
                }

                foundIndex       = gaps[bestIndex].x;
                gaps[bestIndex] += new int2(countNeeded, -countNeeded);
                return true;
            }

            struct GapSorter : IComparer<int2>
            {
                public int Compare(int2 a, int2 b)
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
            [NativeDisableParallelForRestriction] public ComponentLookup<SkeletonDependent> stateLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Parent>            parentLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToParent>     localToParentLookup;

            [ReadOnly] public NativeArray<MeshWriteStateOperation> ops;
            public Entity                                          failedBindingEntity;

            public void Execute(int index)
            {
                var op                     = ops[index];
                stateLookup[op.meshEntity] = op.state;
                if (op.state.root == Entity.Null)
                    parentLookup[op.meshEntity] = new Parent { Value = failedBindingEntity };
                else
                    parentLookup[op.meshEntity] = new Parent { Value = op.state.root };
                localToParentLookup[op.meshEntity]                   = new LocalToParent { Value = float4x4.identity };
            }
        }

        [BurstCompile]
        struct ProcessBindingOpsJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<BindUnbindOperation>   operations;
            [ReadOnly] public NativeArray<int2>                  startsAndCounts;
            [ReadOnly] public ComponentLookup<SkeletonDependent> meshStateLookup;
            [ReadOnly] public BufferLookup<OptimizedBoneToRoot>  boneToRootsLookup;
            [ReadOnly] public BufferLookup<BoneReference>        boneRefsLookup;
            [ReadOnly] public MeshGpuManager                     meshGpuManager;
            [ReadOnly] public BoneOffsetsGpuManager              boneOffsetsGpuManager;

            [NativeDisableParallelForRestriction] public BufferLookup<DependentSkinnedMesh>          dependentsLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<OptimizedBoneBounds>           optimizedBoundsLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneBounds>                 boneBoundsLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<SkeletonShaderBoundsOffset> optimizedShaderBoundsLookup;

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
                    var meshState = meshStateLookup[opsArray[i].meshEntity];
                    if (meshState.root != Entity.Null)
                    {
                        var meshEntry        = meshGpuManager.entries[meshState.meshEntryIndex];
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[meshState.boneOffsetEntryIndex];
                        depsBuffer.Add(new DependentSkinnedMesh
                        {
                            skinnedMesh        = opsArray[i].meshEntity,
                            meshVerticesStart  = meshEntry.verticesStart,
                            meshVerticesCount  = meshEntry.blob.Value.verticesToSkin.Length,
                            meshWeightsStart   = meshEntry.weightsStart,
                            meshBindPosesStart = meshEntry.bindPosesStart,
                            meshBindPosesCount = meshEntry.blob.Value.bindPoses.Length,
                            boneOffsetsStart   = boneOffsetsEntry.start,
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
            }

            void AppendMeshBounds(Entity skeletonEntity, NativeArray<BindUnbindOperation> ops, int start, int count)
            {
                if (boneToRootsLookup.HasBuffer(skeletonEntity))
                {
                    // Optimized skeleton path
                    var boundsBuffer = optimizedBoundsLookup[skeletonEntity];
                    if (boundsBuffer.IsEmpty)
                    {
                        boundsBuffer.Resize(boneToRootsLookup[skeletonEntity].Length, NativeArrayOptions.ClearMemory);
                    }
                    var boundsArray = boundsBuffer.Reinterpret<float>().AsNativeArray();

                    float shaderBounds = 0f;
                    for (int i = start; i < start + count; i++)
                    {
                        var meshState = meshStateLookup[ops[i].meshEntity];
                        if (meshState.root == Entity.Null)
                            continue;
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[meshState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray(boneOffsetsEntry.start, boneOffsetsEntry.count);

                        ref var blobBounds = ref meshState.skinningBlob.Value.maxRadialOffsetsInBoneSpaceByBone;
                        short   k          = 0;
                        foreach (var j in boneOffsets)
                        {
                            if (j >= boundsArray.Length)
                                UnityEngine.Debug.LogError(
                                    $"Skinned Mesh Entity {ops[i].meshEntity} specifies a boneSkinningIndex of {j} but OptimizedBoneToRoot buffer on Entity {skeletonEntity} only has {boundsArray.Length} elements.");
                            else
                                boundsArray[j] = math.max(boundsArray[j], blobBounds[k]);
                            k++;
                        }
                        shaderBounds = math.max(shaderBounds, meshState.shaderEffectRadialBounds);
                    }
                    if (shaderBounds > 0f)
                    {
                        optimizedShaderBoundsLookup[skeletonEntity] = new SkeletonShaderBoundsOffset
                        {
                            radialBoundsInWorldSpace = math.max(shaderBounds, optimizedShaderBoundsLookup[skeletonEntity].radialBoundsInWorldSpace)
                        };
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

                    float shaderBounds = 0f;
                    for (int i = start; i < start + count; i++)
                    {
                        var meshState = meshStateLookup[ops[i].meshEntity];
                        if (meshState.root == Entity.Null)
                            continue;
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[meshState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray(boneOffsetsEntry.start, boneOffsetsEntry.count);

                        ref var blobBounds = ref meshState.skinningBlob.Value.maxRadialOffsetsInBoneSpaceByBone;
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
                        shaderBounds = math.max(shaderBounds, meshState.shaderEffectRadialBounds);
                    }

                    // Merge with new values
                    for (int i = 0; i < boundsArray.Length; i++)
                    {
                        var storedBounds              = boneBoundsLookup[boneRefs[i]];
                        boneBoundsLookup[boneRefs[i]] = new BoneBounds
                        {
                            radialOffsetInBoneSpace  = math.max(boundsArray[i], storedBounds.radialOffsetInBoneSpace),
                            radialOffsetInWorldSpace = math.max(shaderBounds, storedBounds.radialOffsetInWorldSpace)
                        };
                    }
                }
            }

            void RebindAllMeshBounds(Entity skeletonEntity, DynamicBuffer<DependentSkinnedMesh> depsBuffer)
            {
                if (boneToRootsLookup.HasBuffer(skeletonEntity))
                {
                    // Optimized skeleton path
                    var boundsBuffer = optimizedBoundsLookup[skeletonEntity];
                    if (boundsBuffer.IsEmpty)
                    {
                        boundsBuffer.Resize(boneToRootsLookup[skeletonEntity].Length, NativeArrayOptions.ClearMemory);
                    }
                    var boundsArray = boundsBuffer.Reinterpret<float>().AsNativeArray();

                    bool  needsCollapse = true;
                    float shaderBounds  = 0f;
                    for (int i = 0; i < depsBuffer.Length; i++)
                    {
                        var meshState = meshStateLookup[depsBuffer[i].skinnedMesh];
                        if (meshState.root == Entity.Null)
                            continue;
                        needsCollapse        = false;
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[meshState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray(boneOffsetsEntry.start, boneOffsetsEntry.count);

                        ref var blobBounds = ref meshState.skinningBlob.Value.maxRadialOffsetsInBoneSpaceByBone;
                        short   k          = 0;
                        foreach (var j in boneOffsets)
                        {
                            if (j >= boundsArray.Length)
                                UnityEngine.Debug.LogError(
                                    $"Skinned Mesh Entity {depsBuffer[i].skinnedMesh} specifies a boneSkinningIndex of {j} but OptimizedBoneToRoot buffer on Entity {skeletonEntity} only has {boundsArray.Length} elements.");
                            else
                                boundsArray[j] = math.max(boundsArray[j], blobBounds[k]);
                            k++;
                        }
                        shaderBounds = math.max(shaderBounds, meshState.shaderEffectRadialBounds);
                    }
                    if (shaderBounds > 0f)
                    {
                        optimizedShaderBoundsLookup[skeletonEntity] = new SkeletonShaderBoundsOffset
                        {
                            radialBoundsInWorldSpace = math.max(shaderBounds, optimizedShaderBoundsLookup[skeletonEntity].radialBoundsInWorldSpace)
                        };
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

                    float shaderBounds = 0f;
                    for (int i = 0; i < depsBuffer.Length; i++)
                    {
                        var meshState = meshStateLookup[depsBuffer[i].skinnedMesh];
                        if (meshState.root == Entity.Null)
                            continue;
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[meshState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray(boneOffsetsEntry.start, boneOffsetsEntry.count);

                        ref var blobBounds = ref meshState.skinningBlob.Value.maxRadialOffsetsInBoneSpaceByBone;
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
                        shaderBounds = math.max(shaderBounds, meshState.shaderEffectRadialBounds);
                    }

                    // Overwrite the bounds
                    for (int i = 0; i < boundsArray.Length; i++)
                    {
                        boneBoundsLookup[boneRefs[i]] = new BoneBounds { radialOffsetInBoneSpace = boundsArray[i], radialOffsetInWorldSpace = shaderBounds };
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
                var dirtyFlags = chunk.GetNativeArray(ref dirtyFlagHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (dirtyFlags[i].isDirty)
                    {
                        var index = manager.skeletonToCullingIndexMap[entities[i]];
                        if (index < indicesToClear.Value.Length)
                            indicesToClear.Value.Set(index, true);
                        operations.Add(new ExposedSkeletonCullingIndexOperation { index = index, skeletonEntity = entities[i] });
                        dirtyFlags[i]                                                                           = new BoneReferenceIsDirtyFlag { isDirty = false };
                    }
                }
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

