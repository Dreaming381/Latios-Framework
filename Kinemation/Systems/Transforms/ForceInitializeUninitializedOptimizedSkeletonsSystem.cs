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
    [UpdateInGroup(typeof(Latios.Transforms.Systems.ExportToGameObjectTransformsEndInitializationSuperSystem), OrderFirst = true)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ForceInitializeUninitializedOptimizedSkeletonsSystem : ISystem
    {
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
            job.Schedule();
            state.Dependency = job.transformHandle.ScheduleChunkGrouping(state.Dependency);
            state.Dependency = job.GetTransformsScheduler().ScheduleParallel(state.Dependency);
        }

#if LATIOS_BURST_DETERMINISM
        [BurstCompile(FloatMode = FloatMode.Deterministic)]
#else
        [BurstCompile]
#endif
        [WithAll(typeof(WorldTransform))]
        [WithNone(typeof(OptimizedSkeletonTag))]
        partial struct Job : IJobEntity, IJobEntityChunkBeginEnd, IJobChunkParallelTransform
        {
            public TransformAspectParallelChunkHandle transformHandle;
            [ReadOnly] public ComponentLookup<Socket> socketLookup;

            public ref TransformAspectParallelChunkHandle transformAspectHandleAccess => ref transformHandle.RefAccess();

            public void Execute([EntityIndexInChunk] int indexInChunk,
                                ref DynamicBuffer<OptimizedBoneTransform>          bones,
                                ref DynamicBuffer<OptimizedBoneInertialBlendState> blends,
                                RefRW<OptimizedSkeletonState>                      state,
                                RefRO<OptimizedSkeletonHierarchyBlobReference>     blobRef,
                                in DynamicBuffer<DependentSkinnedMesh>             deps)
            {
                var transform = transformHandle[indexInChunk];
                var _         = new OptimizedSkeletonAspect(transform, ref socketLookup, blobRef, state, ref bones, ref blends, deps);
            }

            public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                return transformHandle.OnChunkBegin(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }

            public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
            {
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
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
        }

#if LATIOS_BURST_DETERMINISM
        [BurstCompile(FloatMode = FloatMode.Deterministic)]
#else
        [BurstCompile]
#endif
        [WithNone(typeof(OptimizedSkeletonTag))]
        partial struct Job : IJobEntity
        {
            public void Execute(in Unity.Transforms.LocalToWorld ltw,
                                ref DynamicBuffer<OptimizedBoneTransform>          bones,
                                ref DynamicBuffer<OptimizedBoneInertialBlendState> blends,
                                RefRW<OptimizedSkeletonState>                      state,
                                RefRO<OptimizedSkeletonHierarchyBlobReference>     blobRef,
                                in DynamicBuffer<DependentSkinnedMesh>             deps)
            {
                var _ = new OptimizedSkeletonAspect(ltw, blobRef, state, ref bones, ref blends, deps);
            }
        }
    }
#endif
}

