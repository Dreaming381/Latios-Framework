using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
#if !LATIOS_TRANSFORMS_UNITY
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(Latios.Transforms.Systems.ExportToGameObjectTransformsEndSimulationSuperSystem), OrderFirst = true)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ForceInitializeUninitializedOptimizedSkeletonsSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.Fluent().With<OptimizedSkeletonHierarchyBlobReference>(true).With<OptimizedSkeletonState, OptimizedBoneTransform, WorldTransform>(false)
            .Without<OptimizedSkeletonTag>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new Job
            {
                transformHandle = new TransformAspectParallelChunkHandle(SystemAPI.GetComponentLookup<WorldTransform>(false),
                                                                         SystemAPI.GetComponentTypeHandle<RootReference>(true),
                                                                         SystemAPI.GetBufferLookup<EntityInHierarchy>(true),
                                                                         SystemAPI.GetBufferLookup<EntityInHierarchyCleanup>(true),
                                                                         SystemAPI.GetEntityStorageInfoLookup(),
                                                                         ref state),
                socketLookup = GetComponentLookup<Socket>(true)
            };
            var jh           = job.transformHandle.ScheduleChunkCaptureForQuery(m_query, state.Dependency);
            jh               = job.transformHandle.ScheduleChunkGrouping(jh);
            state.Dependency = job.GetTransformsScheduler().ScheduleParallel(jh);
        }

#if LATIOS_BURST_DETERMINISM
        [BurstCompile(FloatMode = FloatMode.Deterministic)]
#else
        [BurstCompile]
#endif
        struct Job : IJobChunk, IJobChunkParallelTransform
        {
            public TransformAspectParallelChunkHandle                                      transformHandle;
            [ReadOnly] public ComponentTypeHandle<OptimizedSkeletonHierarchyBlobReference> hierarchyHandle;
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh>                       skinnedMeshesHandle;
            [ReadOnly] public ComponentLookup<Socket>                                      socketLookup;
            public ComponentTypeHandle<OptimizedSkeletonState>                             stateHandle;
            public BufferTypeHandle<OptimizedBoneTransform>                                boneTransformHandle;

            public ref TransformAspectParallelChunkHandle transformAspectHandleAccess => ref transformHandle.RefAccess();

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                transformHandle.OnChunkBegin(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);

                var hierarchies        = chunk.GetNativeArray(ref hierarchyHandle);
                var boneBuffers        = chunk.GetBufferAccessor(ref boneTransformHandle);
                var states             = chunk.GetNativeArray(ref stateHandle);
                var skinnedMeshBuffers = chunk.GetBufferAccessor(ref skinnedMeshesHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var                                            boneBuffer       = boneBuffers[i];
                    DynamicBuffer<OptimizedBoneInertialBlendState> dummyBlendBuffer = default;

                    _ = new OptimizedSkeletonAspect(transformHandle[i],
                                                    ref socketLookup,
                                                    new RefRO<OptimizedSkeletonHierarchyBlobReference>(hierarchies, i),
                                                    new RefRW<OptimizedSkeletonState>(states, i),
                                                    ref boneBuffer,
                                                    ref dummyBlendBuffer,
                                                    skinnedMeshBuffers.Length > 0 ? skinnedMeshBuffers[i] : default);
                }
            }
        }
    }

#else

    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(Unity.Transforms.TransformSystemGroup))]
    [UpdateBefore(typeof(UpdateSocketsSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ForceInitializeUninitializedOptimizedSkeletonsSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.Fluent().With<OptimizedSkeletonHierarchyBlobReference, Unity.Transforms.LocalToWorld>(true).With<OptimizedSkeletonState, OptimizedBoneTransform>(false)
            .Without<OptimizedSkeletonTag>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new Job
            {
                boneTransformHandle = GetBufferTypeHandle<OptimizedBoneTransform>(false),
                hierarchyHandle     = GetComponentTypeHandle<OptimizedSkeletonHierarchyBlobReference>(true),
                stateHandle         = GetComponentTypeHandle<OptimizedSkeletonState>(false),
                transformHandle     = GetComponentTypeHandle<Unity.Transforms.LocalToWorld>(true),
            };
            state.Dependency = job.ScheduleParallel(m_query, state.Dependency);
        }

#if LATIOS_BURST_DETERMINISM
        [BurstCompile(FloatMode = FloatMode.Deterministic)]
#else
        [BurstCompile]
#endif
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<OptimizedSkeletonHierarchyBlobReference> hierarchyHandle;
            [ReadOnly] public ComponentTypeHandle<Unity.Transforms.LocalToWorld> transformHandle;
            public ComponentTypeHandle<OptimizedSkeletonState> stateHandle;
            public BufferTypeHandle<OptimizedBoneTransform> boneTransformHandle;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var ltws        = chunk.GetComponentDataPtrRO(ref transformHandle);
                var hierarchies = chunk.GetNativeArray(ref hierarchyHandle);
                var boneBuffers = chunk.GetBufferAccessor(ref boneTransformHandle);
                var states      = chunk.GetNativeArray(ref stateHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var boneBuffer       = boneBuffers[i];
                    DynamicBuffer<OptimizedBoneInertialBlendState> dummyBlendBuffer = default;

                    _ = new OptimizedSkeletonAspect(in transformHandle[i],
                                                    new RefRO<OptimizedSkeletonHierarchyBlobReference>(hierarchies, i),
                                                    new RefRW<OptimizedSkeletonState>(states, i),
                                                    ref boneBuffer,
                                                    ref dummyBlendBuffer,
                                                    default);
                }
            }
        }
    }
#endif
}

