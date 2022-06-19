using System;
using System.Collections.Generic;
using System.Diagnostics;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class SkeletonMeshBindingReactiveSystem : SubSystem
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

        protected override void OnCreate()
        {
            m_newMeshesQuery = Fluent.WithAll<BindSkeletonRoot>(true).WithAll<MeshSkinningBlobReference>(true)
                               .Without<SkeletonDependent>()
                               .WithAny<MeshBindingPathsBlobReference>(true).WithAny<OverrideSkinningBoneIndex>(true).Build();

            m_bindableMeshesQuery = Fluent.WithAll<NeedsBindingFlag>().WithAll<BindSkeletonRoot>(true).WithAll<MeshSkinningBlobReference>(true).WithAll<SkeletonDependent>()
                                    .WithAny<MeshBindingPathsBlobReference>(true).WithAny<OverrideSkinningBoneIndex>(true).Build();

            m_deadMeshesQuery = Fluent.WithAll<SkeletonDependent>().Without<BindSkeletonRoot>().Without<MeshSkinningBlobReference>()
                                .Without<MeshBindingPathsBlobReference>().Without<OverrideSkinningBoneIndex>().Build();

            m_newSkeletonsQuery             = Fluent.WithAll<SkeletonRootTag>(true).Without<DependentSkinnedMesh>().Build();
            m_deadSkeletonsQuery            = Fluent.WithAll<DependentSkinnedMesh>().Without<SkeletonRootTag>().Build();
            m_aliveSkeletonsQuery           = Fluent.WithAll<SkeletonRootTag>(true).Build();
            m_newExposedSkeletonsQuery      = Fluent.WithAll<SkeletonRootTag>(true).Without<ExposedSkeletonCullingIndex>().WithAll<BoneReference>(true).Build();
            m_syncableExposedSkeletonsQuery = Fluent.WithAll<ExposedSkeletonCullingIndex>(true).WithAll<BoneReferenceIsDirtyFlag>(true).Build();
            m_deadExposedSkeletonsQuery     = Fluent.Without<BoneReference>().WithAll<ExposedSkeletonCullingIndex>().Build();
            m_deadExposedSkeletonsQuery2    = Fluent.Without<SkeletonRootTag>().WithAll<ExposedSkeletonCullingIndex>().Build();
            m_newOptimizedSkeletonsQuery    = Fluent.WithAll<SkeletonRootTag>(true).WithAll<OptimizedBoneToRoot>(true).Without<OptimizedSkeletonTag>().Build();
            m_deadOptimizedSkeletonsQuery   = Fluent.WithAll<OptimizedSkeletonTag>(true).Without<OptimizedBoneToRoot>().Build();
            m_deadOptimizedSkeletonsQuery2  = Fluent.WithAll<OptimizedSkeletonTag>(true).Without<SkeletonRootTag>().Build();
            m_cullableExposedBonesQuery     = Fluent.WithAll<BoneCullingIndex>().Build();

            worldBlackboardEntity.AddCollectionComponent(new MeshGpuManager
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

            worldBlackboardEntity.AddCollectionComponent(new BoneOffsetsGpuManager
            {
                entries            = new NativeList<BoneOffsetsEntry>(Allocator.Persistent),
                indexFreeList      = new NativeList<int>(Allocator.Persistent),
                offsets            = new NativeList<short>(Allocator.Persistent),
                gaps               = new NativeList<int2>(Allocator.Persistent),
                isDirty            = new NativeReference<bool>(Allocator.Persistent),
                hashToEntryMap     = new NativeHashMap<uint2, int>(128, Allocator.Persistent),
                pathPairToEntryMap = new NativeHashMap<PathMappingPair, int>(128, Allocator.Persistent)
            });

            worldBlackboardEntity.AddCollectionComponent(new ExposedCullingIndexManager
            {
                skeletonToCullingIndexMap = new NativeHashMap<Entity, int>(128, Allocator.Persistent),
                indexFreeList             = new NativeList<int>(Allocator.Persistent),
                maxIndex                  = new NativeReference<int>(Allocator.Persistent),
                cullingIndexToSkeletonMap = new NativeHashMap<int, EntityWithBuffer<DependentSkinnedMesh> >(128, Allocator.Persistent)
            });
        }

        protected override void OnUpdate()
        {
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

            //bool needsCullingJob = haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedskeletons2;
            //bool needsMeshJob = haveNewMeshes | haveDeadMeshes | haveBindableMeshes;
            //bool needsMeshStateOpsJob = haveNewMeshes | haveBindableMeshes;
            //bool needsBindingOpsJob = needsMeshJob;
            //bool needsCullingOpsJob = needsCullingJob | haveSyncableExposedSkeletons;

            // The '2' variants are covered by the base dead skeletons
            bool requiresStructuralChange = haveNewMeshes | haveDeadMeshes | haveNewSkeletons | haveDeadSkeletons |
                                            haveNewExposedSkeletons | haveDeadExposedSkeletons | haveNewOptimizedSkeletons | haveDeadOptimizedSkeletons;

            bool requiresManagers = haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveNewMeshes | haveDeadMeshes | haveBindableMeshes;

            ref var allocatorHandle = ref World.UpdateAllocator;
            var     allocator       = allocatorHandle.ToAllocator;

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
                meshGpuManager        = worldBlackboardEntity.GetCollectionComponent<MeshGpuManager>(false, out var meshGpuManagerJH);
                boneOffsetsGpuManager = worldBlackboardEntity.GetCollectionComponent<BoneOffsetsGpuManager>(false, out var boneOffsetsGpuManagerJH);
                cullingIndicesManager = worldBlackboardEntity.GetCollectionComponent<ExposedCullingIndexManager>(false, out var cullingIndicesManagerJH);
                var jhs               = new NativeList<JobHandle>(4, Allocator.Temp);
                jhs.Add(Dependency);
                jhs.Add(meshGpuManagerJH);
                jhs.Add(boneOffsetsGpuManagerJH);
                jhs.Add(cullingIndicesManagerJH);
                Dependency = JobHandle.CombineDependencies(jhs);
            }

            JobHandle                                        cullingJH             = default;
            NativeList<ExposedSkeletonCullingIndexOperation> cullingOps            = default;
            NativeReference<UnsafeBitArray>                  cullingIndicesToClear = default;
            if (haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveBindableMeshes)
            {
                cullingOps            = new NativeList<ExposedSkeletonCullingIndexOperation>(0, allocatorHandle.Handle);
                cullingIndicesToClear = new NativeReference<UnsafeBitArray>(allocatorHandle.Handle);
            }

            var lastSystemVersion   = LastSystemVersion;
            var globalSystemVersion = GlobalSystemVersion;

            if (haveDeadSkeletons)
            {
                Dependency = new FindDeadSkeletonsJob
                {
                    dependentsHandle = GetBufferTypeHandle<DependentSkinnedMesh>(true),
                    meshStateCdfe    = GetComponentDataFromEntity<SkeletonDependent>(false)
                }.ScheduleParallel(m_deadSkeletonsQuery, Dependency);
            }
            if (haveDeadMeshes)
            {
                Dependency = new FindDeadMeshesJob
                {
                    entityHandle           = GetEntityTypeHandle(),
                    depsHandle             = GetComponentTypeHandle<SkeletonDependent>(true),
                    bindingOpsBlockList    = bindingOpsBlockList,
                    meshRemoveOpsBlockList = meshRemoveOpsBlockList
                }.ScheduleParallel(m_deadMeshesQuery, Dependency);
            }

            if (haveNewMeshes || haveBindableMeshes)
            {
                var newMeshJob = new FindNewMeshesJob
                {
                    allocator                       = allocator,
                    bindingOpsBlockList             = bindingOpsBlockList,
                    bindSkeletonRootCdfe            = GetComponentDataFromEntity<BindSkeletonRoot>(true),
                    boneOwningSkeletonReferenceCdfe = GetComponentDataFromEntity<BoneOwningSkeletonReference>(true),
                    entityHandle                    = GetEntityTypeHandle(),
                    meshAddOpsBlockList             = meshAddOpsBlockList,
                    needsBindingHandle              = GetComponentTypeHandle<NeedsBindingFlag>(false),
                    overrideBonesHandle             = GetBufferTypeHandle<OverrideSkinningBoneIndex>(true),
                    pathBindingsBlobRefHandle       = GetComponentTypeHandle<MeshBindingPathsBlobReference>(true),
                    skeletonBindingPathsBlobRefCdfe = GetComponentDataFromEntity<SkeletonBindingPathsBlobReference>(true),
                    skeletonRootTagCdfe             = GetComponentDataFromEntity<SkeletonRootTag>(true),
                    skinningBlobRefHandle           = GetComponentTypeHandle<MeshSkinningBlobReference>(true),
                    radialBoundsHandle              = GetComponentTypeHandle<ShaderEffectRadialBounds>(true),
                    rootRefHandle                   = GetComponentTypeHandle<BindSkeletonRoot>(true)
                };

                if (haveNewMeshes)
                {
                    Dependency = newMeshJob.ScheduleParallel(m_newMeshesQuery, Dependency);
                }
                if (haveBindableMeshes)
                {
                    Dependency = new FindRebindMeshesJob
                    {
                        depsHandle             = GetComponentTypeHandle<SkeletonDependent>(false),
                        lastSystemVersion      = lastSystemVersion,
                        meshRemoveOpsBlockList = meshRemoveOpsBlockList,
                        newMeshesJob           = newMeshJob
                    }.ScheduleParallel(m_bindableMeshesQuery, Dependency);
                }
            }

            if (haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2)
            {
                int newExposedSkeletonsCount   = m_newExposedSkeletonsQuery.CalculateEntityCountWithoutFiltering();
                int deadExposedSkeletonsCount  = m_deadExposedSkeletonsQuery.CalculateEntityCountWithoutFiltering();
                int deadExposedSkeletonsCount2 = m_deadExposedSkeletonsQuery2.CalculateEntityCountWithoutFiltering();

                var newSkeletonsArray   = allocatorHandle.AllocateNativeArray<Entity>(newExposedSkeletonsCount);
                var deadSkeletonsArray  = allocatorHandle.AllocateNativeArray<Entity>(deadExposedSkeletonsCount);
                var deadSkeletonsArray2 = allocatorHandle.AllocateNativeArray<Entity>(deadExposedSkeletonsCount2);

                var entityHandle = GetEntityTypeHandle();
                Dependency       = new FindNewOrDeadDeadExposedSkeletonsJob
                {
                    entityHandle   = entityHandle,
                    newOrDeadArray = newSkeletonsArray
                }.ScheduleParallel(m_newExposedSkeletonsQuery, Dependency);
                Dependency = new FindNewOrDeadDeadExposedSkeletonsJob
                {
                    entityHandle   = entityHandle,
                    newOrDeadArray = deadSkeletonsArray
                }.ScheduleParallel(m_deadExposedSkeletonsQuery, Dependency);
                Dependency = new FindNewOrDeadDeadExposedSkeletonsJob
                {
                    entityHandle   = entityHandle,
                    newOrDeadArray = deadSkeletonsArray2
                }.ScheduleParallel(m_deadExposedSkeletonsQuery2, Dependency);

                cullingOps.Capacity = newExposedSkeletonsCount + m_syncableExposedSkeletonsQuery.CalculateEntityCountWithoutFiltering();

                cullingJH = new ProcessNewAndDeadExposedSkeletonsJob
                {
                    newExposedSkeletons   = newSkeletonsArray,
                    deadExposedSkeletons  = deadSkeletonsArray,
                    deadExposedSkeletons2 = deadSkeletonsArray2,
                    cullingManager        = cullingIndicesManager,
                    operations            = cullingOps,
                    indicesToClear        = cullingIndicesToClear,
                    allocator             = allocator
                }.Schedule(Dependency);
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
                    meshRemoveOpsBlockList = meshRemoveOpsBlockList,  // this is disposed here
                    boneManager            = boneOffsetsGpuManager,
                    meshManager            = meshGpuManager,
                    outputWriteOps         = meshBindingsStatesToWrite
                }.Schedule(Dependency);

                skeletonBindingOps                = new NativeList<BindUnbindOperation>(newMeshCount + deadMeshCount + bindableMeshCount, allocatorHandle.Handle);
                skeletonBindingOpsStartsAndCounts = new NativeList<int2>(aliveSkeletonsCount, allocatorHandle.Handle);

                skeletonBindingOpsJH = new BatchBindingOpsJob
                {
                    bindingsBlockList = bindingOpsBlockList,  // This is disposed here
                    operations        = skeletonBindingOps,
                    startsAndCounts   = skeletonBindingOpsStartsAndCounts
                }.Schedule(Dependency);
            }

            if (requiresStructuralChange)
            {
                // Kick the jobs so that the sorting happens while we do structural changes.
                // Todo: Does Complete already do this?
                JobHandle.ScheduleBatchedJobs();

                CompleteDependency();

                if (!EntityManager.Exists(m_failedBindingEntity))
                {
                    m_failedBindingEntity = EntityManager.CreateEntity();
                    EntityManager.AddComponents(m_failedBindingEntity, new ComponentTypes(typeof(LocalToWorld), typeof(FailedBindingsRootTag)));
                    EntityManager.SetName(m_failedBindingEntity, "Failed Bindings Root");
                }

                var optimizedTypes = new FixedList128Bytes<ComponentType>();
                optimizedTypes.Add(typeof(PerFrameSkeletonBufferMetadata));
                optimizedTypes.Add(typeof(OptimizedBoneToRoot));
                optimizedTypes.Add(typeof(OptimizedSkeletonTag));
                optimizedTypes.Add(typeof(SkeletonShaderBoundsOffset));
                optimizedTypes.Add(typeof(SkeletonWorldBounds));
                optimizedTypes.Add(typeof(OptimizedBoneBounds));
                optimizedTypes.Add(ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>());
                optimizedTypes.Add(ComponentType.ChunkComponent<ChunkSkeletonWorldBounds>());

                EntityManager.RemoveComponent<SkeletonDependent>(    m_deadMeshesQuery);
                EntityManager.RemoveComponent(                       m_deadOptimizedSkeletonsQuery, new ComponentTypes(optimizedTypes));
                EntityManager.RemoveComponent(                       m_deadExposedSkeletonsQuery,
                                                                     new ComponentTypes(typeof(ExposedSkeletonCullingIndex), typeof(PerFrameSkeletonBufferMetadata),
                                                                                        ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>()));
                EntityManager.RemoveComponent<DependentSkinnedMesh>( m_deadSkeletonsQuery);

                var transformComponentsThatWriteToLocalToParent = new FixedList128Bytes<ComponentType>();
                // Having both causes the mesh to not render in some circumstances. Still need to investigate how this happens.
                transformComponentsThatWriteToLocalToParent.Add(typeof(CopyLocalToParentFromBone));
                transformComponentsThatWriteToLocalToParent.Add(typeof(Translation));
                transformComponentsThatWriteToLocalToParent.Add(typeof(Rotation));
                transformComponentsThatWriteToLocalToParent.Add(typeof(Scale));
                transformComponentsThatWriteToLocalToParent.Add(typeof(NonUniformScale));
                transformComponentsThatWriteToLocalToParent.Add(typeof(ParentScaleInverse));
                transformComponentsThatWriteToLocalToParent.Add(typeof(CompositeRotation));
                transformComponentsThatWriteToLocalToParent.Add(typeof(CompositeScale));
                EntityManager.RemoveComponent(m_newMeshesQuery, new ComponentTypes(transformComponentsThatWriteToLocalToParent));
                EntityManager.AddComponent(m_newMeshesQuery, new ComponentTypes(typeof(SkeletonDependent), typeof(LocalToParent), typeof(Parent)));

                optimizedTypes.Add(typeof(DependentSkinnedMesh));

                EntityManager.AddComponent(m_newOptimizedSkeletonsQuery, new ComponentTypes(optimizedTypes));
                EntityManager.AddComponent(m_newExposedSkeletonsQuery,
                                           new ComponentTypes(typeof(DependentSkinnedMesh), typeof(PerFrameSkeletonBufferMetadata),
                                                              typeof(ExposedSkeletonCullingIndex), ComponentType.ChunkComponent<ChunkPerCameraSkeletonCullingMask>()));

                EntityManager.AddComponent<DependentSkinnedMesh>(m_newSkeletonsQuery);
            }

            if (haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2 | haveNewMeshes | haveBindableMeshes | haveDeadMeshes)
            {
                var jhs = new NativeList<JobHandle>(4, Allocator.Temp);
                jhs.Add(cullingJH);
                jhs.Add(meshBindingsJH);
                jhs.Add(skeletonBindingOpsJH);
                jhs.Add(Dependency);
                Dependency = JobHandle.CombineDependencies(jhs);
            }

            if (haveNewMeshes | haveBindableMeshes)
            {
                Dependency = new ProcessMeshStateOpsJob
                {
                    failedBindingEntity = m_failedBindingEntity,
                    ops                 = meshBindingsStatesToWrite.AsDeferredJobArray(),
                    parentCdfe          = GetComponentDataFromEntity<Parent>(false),
                    stateCdfe           = GetComponentDataFromEntity<SkeletonDependent>(false)
                }.Schedule(meshBindingsStatesToWrite, 16, Dependency);
            }

            if (haveNewMeshes | haveBindableMeshes | haveDeadMeshes)
            {
                Dependency = new ProcessBindingOpsJob
                {
                    boneBoundsCdfe            = GetComponentDataFromEntity<BoneBounds>(false),
                    boneOffsetsGpuManager     = boneOffsetsGpuManager,
                    boneRefsBfe               = GetBufferFromEntity<BoneReference>(true),
                    boneToRootsBfe            = GetBufferFromEntity<OptimizedBoneToRoot>(true),
                    dependentsBfe             = GetBufferFromEntity<DependentSkinnedMesh>(false),
                    meshGpuManager            = meshGpuManager,
                    meshStateCdfe             = GetComponentDataFromEntity<SkeletonDependent>(true),
                    operations                = skeletonBindingOps.AsDeferredJobArray(),
                    optimizedBoundsBfe        = GetBufferFromEntity<OptimizedBoneBounds>(false),
                    optimizedShaderBoundsCdfe = GetComponentDataFromEntity<SkeletonShaderBoundsOffset>(false),
                    startsAndCounts           = skeletonBindingOpsStartsAndCounts.AsDeferredJobArray()
                }.Schedule(skeletonBindingOpsStartsAndCounts, 1, Dependency);
            }

            if (haveSyncableExposedSkeletons && haveCullableExposedBones)
            {
                if (!(haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2))
                {
                    Dependency = Job.WithReadOnly(cullingIndicesManager).WithCode(() =>
                    {
                        cullingIndicesToClear.Value = new UnsafeBitArray(cullingIndicesManager.maxIndex.Value + 1, allocator);
                    }).Schedule(Dependency);
                }

                Dependency = new FindExposedSkeletonsToUpdateJob
                {
                    dirtyFlagHandle   = GetComponentTypeHandle<BoneReferenceIsDirtyFlag>(false),
                    entityHandle      = GetEntityTypeHandle(),
                    indicesToClear    = cullingIndicesToClear,
                    lastSystemVersion = lastSystemVersion,
                    manager           = cullingIndicesManager,
                    operations        = cullingOps
                }.Schedule(m_syncableExposedSkeletonsQuery, Dependency);
            }

            if ((haveSyncableExposedSkeletons | haveNewExposedSkeletons | haveDeadExposedSkeletons | haveDeadExposedSkeletons2) && haveCullableExposedBones)
            {
                Dependency = new ResetExposedBonesJob
                {
                    indexHandle    = GetComponentTypeHandle<BoneCullingIndex>(false),
                    indicesToClear = cullingIndicesToClear
                }.ScheduleParallel(m_cullableExposedBonesQuery, Dependency);
            }

            if (haveSyncableExposedSkeletons | haveNewExposedSkeletons)
            {
                Dependency = new SetExposedSkeletonCullingIndicesJob
                {
                    boneCullingIndexCdfe     = GetComponentDataFromEntity<BoneCullingIndex>(false),
                    boneIndexCdfe            = GetComponentDataFromEntity<BoneIndex>(false),
                    boneRefsBfe              = GetBufferFromEntity<BoneReference>(true),
                    operations               = cullingOps.AsDeferredJobArray(),
                    skeletonCullingIndexCdfe = GetComponentDataFromEntity<ExposedSkeletonCullingIndex>(false),
                    skeletonReferenceCdfe    = GetComponentDataFromEntity<BoneOwningSkeletonReference>(false)
                }.Schedule(cullingOps, 1, Dependency);
            }
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
        struct FindDeadSkeletonsJob : IJobEntityBatch
        {
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh> dependentsHandle;

            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<SkeletonDependent> meshStateCdfe;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var buffers = batchInChunk.GetBufferAccessor(dependentsHandle);

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var buffer = buffers[i].AsNativeArray();
                    for (int j = 0; j < buffer.Length; j++)
                    {
                        var entity            = buffer[j].skinnedMesh;
                        var state             = meshStateCdfe[entity];
                        state.root            = Entity.Null;
                        meshStateCdfe[entity] = state;
                    }
                }
            }
        }

        [BurstCompile]
        struct FindDeadMeshesJob : IJobEntityBatch
        {
            [ReadOnly] public EntityTypeHandle                       entityHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent> depsHandle;

            public UnsafeParallelBlockList bindingOpsBlockList;
            public UnsafeParallelBlockList meshRemoveOpsBlockList;
            [NativeSetThreadIndex] int     m_nativeThreadIndex;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var entities = batchInChunk.GetNativeArray(entityHandle);
                var deps     = batchInChunk.GetNativeArray(depsHandle);

                for (int i = 0; i < batchInChunk.Count; i++)
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
        struct FindNewMeshesJob : IJobEntityBatch
        {
            [ReadOnly] public EntityTypeHandle                                           entityHandle;
            [ReadOnly] public ComponentTypeHandle<BindSkeletonRoot>                      rootRefHandle;
            [ReadOnly] public ComponentTypeHandle<MeshSkinningBlobReference>             skinningBlobRefHandle;
            [ReadOnly] public ComponentDataFromEntity<SkeletonRootTag>                   skeletonRootTagCdfe;
            [ReadOnly] public ComponentDataFromEntity<BindSkeletonRoot>                  bindSkeletonRootCdfe;
            [ReadOnly] public ComponentDataFromEntity<BoneOwningSkeletonReference>       boneOwningSkeletonReferenceCdfe;
            [ReadOnly] public ComponentDataFromEntity<SkeletonBindingPathsBlobReference> skeletonBindingPathsBlobRefCdfe;

            // Optional
            [ReadOnly] public ComponentTypeHandle<MeshBindingPathsBlobReference> pathBindingsBlobRefHandle;
            [ReadOnly] public BufferTypeHandle<OverrideSkinningBoneIndex>        overrideBonesHandle;
            [ReadOnly] public ComponentTypeHandle<ShaderEffectRadialBounds>      radialBoundsHandle;
            public ComponentTypeHandle<NeedsBindingFlag>                         needsBindingHandle;

            public UnsafeParallelBlockList bindingOpsBlockList;
            public UnsafeParallelBlockList meshAddOpsBlockList;
            [NativeSetThreadIndex] int     m_nativeThreadIndex;

            public Allocator allocator;

            public unsafe void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var lower = new BitField64(~0UL);
                var upper = new BitField64(~0UL);
                if (batchInChunk.Has(needsBindingHandle))
                {
                    var needs = batchInChunk.GetNativeArray(needsBindingHandle);
                    for (int i = 0; i < math.min(batchInChunk.Count, 64); i++)
                    {
                        lower.SetBits(i, needs[i].needsBinding);
                        needs[i] = new NeedsBindingFlag { needsBinding = false };
                    }
                    for (int i = 0; i + 64 < batchInChunk.Count; i++)
                    {
                        upper.SetBits(i, needs[i + 64].needsBinding);
                        needs[i + 64] = new NeedsBindingFlag { needsBinding = false };
                    }
                    if ((lower.Value | upper.Value) == 0)
                        return;
                }

                var entities      = batchInChunk.GetNativeArray(entityHandle);
                var rootRefs      = batchInChunk.GetNativeArray(rootRefHandle);
                var skinningBlobs = batchInChunk.GetNativeArray(skinningBlobRefHandle);

                var hasPathBindings  = batchInChunk.Has(pathBindingsBlobRefHandle);
                var hasOverrideBones = batchInChunk.Has(overrideBonesHandle);
                var hasRadialBounds  = batchInChunk.Has(radialBoundsHandle);
                var pathBindings     = hasPathBindings ? batchInChunk.GetNativeArray(pathBindingsBlobRefHandle) : default;
                var overrideBones    = hasOverrideBones ? batchInChunk.GetBufferAccessor(overrideBonesHandle) : default;
                var radialBounds     = hasRadialBounds ? batchInChunk.GetNativeArray(radialBoundsHandle) : default;

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    if (!(i >= 64 ? upper.IsSet(i - 64) : lower.IsSet(i)))
                        continue;

                    var entity = entities[i];

                    var root = rootRefs[i].root;
                    if (root == Entity.Null)
                        continue;

                    if (!root.IsValid(skeletonRootTagCdfe))
                    {
                        bool found = false;

                        if (boneOwningSkeletonReferenceCdfe.HasComponent(root))
                        {
                            var skelRef = boneOwningSkeletonReferenceCdfe[root];
                            if (skelRef.skeletonRoot != Entity.Null)
                            {
                                found = true;
                                root  = skelRef.skeletonRoot;
                            }
                        }

                        if (!found && bindSkeletonRootCdfe.HasComponent(root))
                        {
                            var skelRef = bindSkeletonRootCdfe[root];
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

                            if (skeletonBindingPathsBlobRefCdfe.HasComponent(root))
                            {
                                skeletonPathsBlob = skeletonBindingPathsBlobRefCdfe[root].blob;
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
        struct FindRebindMeshesJob : IJobEntityBatch
        {
            public FindNewMeshesJob                       newMeshesJob;
            public ComponentTypeHandle<SkeletonDependent> depsHandle;

            public UnsafeParallelBlockList meshRemoveOpsBlockList;

            public uint lastSystemVersion;

            [NativeSetThreadIndex] int m_nativeThreadIndex;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                // The general strategy here is to unbind anything requesting a rebind
                // and then to treat it like a new mesh using that job's struct.
                if (!batchInChunk.DidChange(newMeshesJob.needsBindingHandle, lastSystemVersion))
                    return;

                {
                    // New scope so that the compiler doesn't keep these variables on the stack when running the NewMesh job.
                    var entities = batchInChunk.GetNativeArray(newMeshesJob.entityHandle);
                    var deps     = batchInChunk.GetNativeArray(depsHandle);
                    var needs    = batchInChunk.GetNativeArray(newMeshesJob.needsBindingHandle);

                    for (int i = 0; i < batchInChunk.Count; i++)
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

                newMeshesJob.Execute(batchInChunk, batchIndex);
            }
        }

        [BurstCompile]
        struct FindNewOrDeadDeadExposedSkeletonsJob : IJobEntityBatchWithIndex
        {
            [ReadOnly] public EntityTypeHandle entityHandle;
            public NativeArray<Entity>         newOrDeadArray;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
            {
                var entities = batchInChunk.GetNativeArray(entityHandle);
                NativeArray<Entity>.Copy(entities, 0, newOrDeadArray, indexOfFirstEntityInQuery, batchInChunk.Count);
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
                indicesToClear.Value = new UnsafeBitArray(cullingManager.maxIndex.Value, allocator);

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
                    int index  = cullingManager.skeletonToCullingIndexMap[entity];
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

                for (int i = 0; i < removeCount; i++)
                {
                    var op = removeOps[i];

                    {
                        ref var entry = ref meshManager.entries.ElementAt(op.oldState.meshEntryIndex);
                        entry.referenceCount--;
                        if (entry.referenceCount == 0)
                        {
                            var blob      = entry.blob;
                            int vertices  = blob.Value.verticesToSkin.Length;
                            int weights   = blob.Value.boneWeights.Length;
                            int bindPoses = blob.Value.bindPoses.Length;
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
                        {
                            entry.pathsReferences--;
                            if (entry.pathsReferences == 0)
                            {
                                boneManager.pathPairToEntryMap.Remove(new PathMappingPair {
                                    meshPaths = op.oldState.meshBindingBlob, skeletonPaths = op.oldState.skeletonBindingBlob
                                });
                            }
                        }
                        else
                            entry.overridesReferences--;

                        if (entry.pathsReferences == 0 && entry.overridesReferences == 0)
                        {
                            boneManager.gaps.Add(new int2(entry.start, entry.gpuCount));
                            boneManager.hashToEntryMap.Remove(entry.hash);
                            boneManager.indexFreeList.Add(op.oldState.boneOffsetEntryIndex);

                            entry              = default;
                            madeBoneOffsetGaps = true;
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
                                        boneManager.offsets.AddRange(newOffsetsCache);
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
                                    }

                                    boneManager.hashToEntryMap.Add(hash, entryIndex);
                                    boneManager.isDirty.Value = true;
                                }

                                boneManager.pathPairToEntryMap.Add(new PathMappingPair { meshPaths = op.meshBindingPathsBlob, skeletonPaths = op.skeletonBindingPathsBlob },
                                                                   entryIndex);
                                resultState.skeletonBindingBlob = op.skeletonBindingPathsBlob;
                                resultState.meshBindingBlob     = op.meshBindingPathsBlob;
                            }
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
                        prev.y         += array[j].x;
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
                        boneManager.gaps.Length--;
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
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<SkeletonDependent> stateCdfe;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<Parent>            parentCdfe;

            [ReadOnly] public NativeArray<MeshWriteStateOperation> ops;
            public Entity                                          failedBindingEntity;

            public void Execute(int index)
            {
                var op                   = ops[index];
                op.meshEntity[stateCdfe] = op.state;
                if (op.state.root == Entity.Null)
                    parentCdfe[op.meshEntity] = new Parent { Value = failedBindingEntity };
                else
                    parentCdfe[op.meshEntity] = new Parent { Value = op.state.root };
            }
        }

        [BurstCompile]
        struct ProcessBindingOpsJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<BindUnbindOperation>           operations;
            [ReadOnly] public NativeArray<int2>                          startsAndCounts;
            [ReadOnly] public ComponentDataFromEntity<SkeletonDependent> meshStateCdfe;
            [ReadOnly] public BufferFromEntity<OptimizedBoneToRoot>      boneToRootsBfe;
            [ReadOnly] public BufferFromEntity<BoneReference>            boneRefsBfe;
            [ReadOnly] public MeshGpuManager                             meshGpuManager;
            [ReadOnly] public BoneOffsetsGpuManager                      boneOffsetsGpuManager;

            [NativeDisableParallelForRestriction] public BufferFromEntity<DependentSkinnedMesh>              dependentsBfe;
            [NativeDisableParallelForRestriction] public BufferFromEntity<OptimizedBoneBounds>               optimizedBoundsBfe;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<BoneBounds>                 boneBoundsCdfe;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<SkeletonShaderBoundsOffset> optimizedShaderBoundsCdfe;

            [NativeDisableContainerSafetyRestriction, NoAlias] NativeList<float> boundsCache;

            public void Execute(int index)
            {
                int2 startAndCount = startsAndCounts[index];
                var  opsArray      = operations.GetSubArray(startAndCount.x, startAndCount.y);

                Entity skeletonEntity        = opsArray[0].targetEntity;
                var    depsBuffer            = dependentsBfe[skeletonEntity];
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
                    var meshState        = meshStateCdfe[opsArray[i].meshEntity];
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

                int changeStart = i;
                if (needsFullBoundsUpdate)
                {
                    ApplyMeshBounds(skeletonEntity, opsArray, 0, opsArray.Length, true);
                }
                else if (needsAddBoundsUpdate)
                {
                    ApplyMeshBounds(skeletonEntity, opsArray, addStart, opsArray.Length - addStart, false);
                }
                else if (needsAddBoundsUpdate)
                {
                    ApplyMeshBounds(skeletonEntity, opsArray, addStart, changeStart - addStart, false);
                }
            }

            void ApplyMeshBounds(Entity skeletonEntity, NativeArray<BindUnbindOperation> ops, int start, int count, bool reset)
            {
                if (boneToRootsBfe.HasComponent(skeletonEntity))
                {
                    // Optimized skeleton path
                    bool needsCollapse = reset;
                    var  boundsBuffer  = optimizedBoundsBfe[skeletonEntity];
                    if (boundsBuffer.IsEmpty)
                    {
                        needsCollapse = true;
                        boundsBuffer.ResizeUninitialized(boneToRootsBfe[skeletonEntity].Length);
                    }
                    var boundsArray = boundsBuffer.Reinterpret<float>().AsNativeArray();
                    if (needsCollapse)
                    {
                        var arr = boundsBuffer.Reinterpret<float>().AsNativeArray();
                        for (int i = 0; i < arr.Length; i++)
                            arr[i] = 0f;
                    }

                    float shaderBounds = 0f;
                    for (int i = start; i < start + count; i++)
                    {
                        var meshState        = meshStateCdfe[ops[i].meshEntity];
                        var boneOffsetsEntry = boneOffsetsGpuManager.entries[meshState.boneOffsetEntryIndex];
                        var boneOffsets      = boneOffsetsGpuManager.offsets.AsArray().GetSubArray(boneOffsetsEntry.start, boneOffsetsEntry.count);

                        needsCollapse      = false;
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
                        optimizedShaderBoundsCdfe[skeletonEntity] = new SkeletonShaderBoundsOffset
                        {
                            radialBoundsInWorldSpace = math.max(shaderBounds, optimizedShaderBoundsCdfe[skeletonEntity].radialBoundsInWorldSpace)
                        };
                    }

                    if (needsCollapse)
                    {
                        // Nothing valid is bound anymore. Shrink the buffer.
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
                    var boneRefs = boneRefsBfe[skeletonEntity].Reinterpret<Entity>().AsNativeArray();
                    boundsCache.Clear();
                    boundsCache.Resize(boneRefs.Length, NativeArrayOptions.ClearMemory);
                    var boundsArray = boundsCache.AsArray();

                    float shaderBounds = 0f;
                    for (int i = start; i < start + count; i++)
                    {
                        var meshState        = meshStateCdfe[ops[i].meshEntity];
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

                    if (reset)
                    {
                        // Overwrite the bounds
                        for (int i = 0; i < boundsArray.Length; i++)
                        {
                            boneBoundsCdfe[boneRefs[i]] = new BoneBounds { radialOffsetInBoneSpace = boundsArray[i], radialOffsetInWorldSpace = shaderBounds };
                        }
                    }
                    else
                    {
                        // Merge with new values
                        for (int i = 0; i < boundsArray.Length; i++)
                        {
                            var storedBounds            = boneBoundsCdfe[boneRefs[i]];
                            boneBoundsCdfe[boneRefs[i]] = new BoneBounds
                            {
                                radialOffsetInBoneSpace  = math.max(boundsArray[i], storedBounds.radialOffsetInBoneSpace),
                                radialOffsetInWorldSpace = math.max(shaderBounds, storedBounds.radialOffsetInWorldSpace)
                            };
                        }
                    }
                }
            }
        }

        // Schedule single
        [BurstCompile]
        struct FindExposedSkeletonsToUpdateJob : IJobEntityBatch
        {
            public NativeList<ExposedSkeletonCullingIndexOperation> operations;
            public ExposedCullingIndexManager                       manager;
            public NativeReference<UnsafeBitArray>                  indicesToClear;

            [ReadOnly] public EntityTypeHandle                   entityHandle;
            public ComponentTypeHandle<BoneReferenceIsDirtyFlag> dirtyFlagHandle;

            public uint lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var entities   = batchInChunk.GetNativeArray(entityHandle);
                var dirtyFlags = batchInChunk.GetNativeArray(dirtyFlagHandle);

                for (int i = 0; i < batchInChunk.Count; i++)
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
        struct ResetExposedBonesJob : IJobEntityBatch
        {
            [ReadOnly] public NativeReference<UnsafeBitArray> indicesToClear;
            public ComponentTypeHandle<BoneCullingIndex>      indexHandle;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var indices = batchInChunk.GetNativeArray(indexHandle).Reinterpret<int>();
                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    bool needsClearing = indices[i] < indicesToClear.Value.Length ? indicesToClear.Value.IsSet(indices[i]) : false;
                    indices[i]         = math.select(indices[i], 0, needsClearing);
                }
            }
        }

        [BurstCompile]
        struct SetExposedSkeletonCullingIndicesJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ExposedSkeletonCullingIndexOperation>                               operations;
            [ReadOnly] public BufferFromEntity<BoneReference>                                                 boneRefsBfe;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<ExposedSkeletonCullingIndex> skeletonCullingIndexCdfe;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<BoneCullingIndex>            boneCullingIndexCdfe;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<BoneIndex>                   boneIndexCdfe;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<BoneOwningSkeletonReference> skeletonReferenceCdfe;

            public void Execute(int index)
            {
                var op    = operations[index];
                var bones = boneRefsBfe[op.skeletonEntity].AsNativeArray();
                for (short i = 0; i < bones.Length; i++)
                {
                    if (boneCullingIndexCdfe.HasComponent(bones[i].bone))
                        boneCullingIndexCdfe[bones[i].bone] = new BoneCullingIndex { cullingIndex = op.index };
                    if (boneIndexCdfe.HasComponent(bones[i].bone))
                        boneIndexCdfe[bones[i].bone] = new BoneIndex { index = i };
                    if (skeletonReferenceCdfe.HasComponent(bones[i].bone))
                        skeletonReferenceCdfe[bones[i].bone] = new BoneOwningSkeletonReference { skeletonRoot = op.skeletonEntity };
                }
                skeletonCullingIndexCdfe[op.skeletonEntity] = new ExposedSkeletonCullingIndex { cullingIndex = op.index };
            }
        }
        #endregion
    }
}

