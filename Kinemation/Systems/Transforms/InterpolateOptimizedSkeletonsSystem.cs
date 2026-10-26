using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation.Systems
{
#if !LATIOS_TRANSFORMS_UNITY
    [UpdateInGroup(typeof(Latios.Systems.TickedInterpolateSuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct InterpolateOptimizedSkeletonsSystem : ISystem, ILatiosApi
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);
            m_query = state.Fluent().With<InterpolateOptimizedSkeletonTag, TickedOptimizedSkeletonState, TickedOptimizedBoneTransform>(true)
                      .With<OptimizedSkeletonHierarchyBlobReference>(true).With<OptimizedSkeletonState, OptimizedBoneTransform, WorldTransform>(false).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api = this.GetApi(ref state);
            var job = new Job
            {
                factor = api.worldBlackboardEntity.GetComponentData<TickingState>().finalTickFraction,
            }.Inject(api);
            var jh           = job.transformAspectHandleAccess.ScheduleChunkCaptureForQuery(m_query, state.Dependency);
            jh               = job.transformAspectHandleAccess.ScheduleChunkGrouping(jh);
            state.Dependency = job.GetTransformsScheduler().ScheduleParallel(jh);
        }

        [BurstCompile]
        partial struct Job : IJobChunk, IJobChunkParallelTransform, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<TickedOptimizedSkeletonState>            tickedStateHandle;
            [ReadOnly, Inject] BufferTypeHandle<TickedOptimizedBoneTransform>               tickedBonesHandle;
            [ReadOnly, Inject] ComponentTypeHandle<OptimizedSkeletonHierarchyBlobReference> hierarchyHandle;
            [ReadOnly, Inject] BufferTypeHandle<DependentSkinnedMesh>                       skinnedMeshesHandle;
            [ReadOnly, Inject] ComponentLookup<Socket>                                      socketLookup;
            [Inject] ComponentTypeHandle<OptimizedSkeletonState>                            stateHandle;
            [Inject] BufferTypeHandle<OptimizedBoneTransform>                               boneTransformHandle;
            [Inject] TransformAspectParallelChunkHandle                                     transformHandle;
            public float                                                                    factor;

            public ref TransformAspectParallelChunkHandle transformAspectHandleAccess => ref transformHandle.RefAccess();

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                transformHandle.OnChunkBegin(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);

                var hierarchies        = chunk.GetNativeArray(ref hierarchyHandle);
                var boneBuffers        = chunk.GetBufferAccessor(ref boneTransformHandle);
                var states             = chunk.GetNativeArray(ref stateHandle);
                var skinnedMeshBuffers = chunk.GetBufferAccessor(ref skinnedMeshesHandle);

                var tickedStates      = chunk.GetComponentDataPtrRO(ref tickedStateHandle);
                var tickedBoneBuffers = chunk.GetBufferAccessor(ref tickedBonesHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var                                            boneBuffer       = boneBuffers[i];
                    DynamicBuffer<OptimizedBoneInertialBlendState> dummyBlendBuffer = default;

                    var skeletonAspect = new OptimizedSkeletonAspect(transformHandle[i],
                                                                     ref socketLookup,
                                                                     new RefRO<OptimizedSkeletonHierarchyBlobReference>(hierarchies, i),
                                                                     new RefRW<OptimizedSkeletonState>(states, i),
                                                                     ref boneBuffer,
                                                                     ref dummyBlendBuffer,
                                                                     skinnedMeshBuffers.Length > 0 ? skinnedMeshBuffers[i] : default);
                    var dstBones = skeletonAspect.rawLocalTransformsRW;

                    var tickedState = tickedStates[i];
                    var tickedBones = tickedBoneBuffers[i].AsNativeArray();
                    if (tickedBones.Length != dstBones.Length * 6)
                    {
                        if (tickedBones.Length == dstBones.Length)
                            continue;

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        throw new System.InvalidOperationException(
                            $"TickedOptimizedBoneTransform buffer length {tickedBones.Length} does not match OptimizedBoneTransform buffer length {dstBones.Length * 6}");
#endif
                    }

                    var mask          = (byte)(tickedState.state & OptimizedSkeletonState.Flags.RotationMask);
                    var rotation      = OptimizedSkeletonState.PreviousFromMask[mask];
                    var previousBones = tickedBones.GetSubArray(rotation * 2 * dstBones.Length + dstBones.Length, dstBones.Length);
                    var currentBones  = previousBones;
                    if ((tickedState.state & OptimizedSkeletonState.Flags.IsDirty) == OptimizedSkeletonState.Flags.IsDirty)
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        if ((tickedState.state & OptimizedSkeletonState.Flags.NeedsSync) == OptimizedSkeletonState.Flags.NeedsSync)
                        {
                            throw new System.InvalidOperationException(
                                "Ticked optimized skeleton has not had its bones synced yet. You must call EndSamplingAndSync() prior to interpolation.");
                        }
#endif
                        rotation     = OptimizedSkeletonState.CurrentFromMask[mask];
                        currentBones = tickedBones.GetSubArray(rotation * 2 * dstBones.Length + dstBones.Length, dstBones.Length);
                    }

                    for (int j = 0; j < dstBones.Length; j++)
                    {
                        var prev      = previousBones[j];
                        var curr      = currentBones[j];
                        var rot       = math.nlerp(prev.boneTransform.rotation, curr.boneTransform.rotation, factor);
                        var pos       = math.lerp(prev.boneTransform.position, curr.boneTransform.position, factor);
                        var scale     = math.lerp(prev.boneTransform.scale, curr.boneTransform.scale, factor);
                        var stretch   = math.lerp(prev.boneTransform.stretch, curr.boneTransform.stretch, factor);
                        var context32 = curr.boneTransform.context32;
                        dstBones[j]   = new TransformQvvs(pos, rot, scale, stretch, context32);
                    }
                    skeletonAspect.EndSamplingAndSync();
                }
            }
        }
    }
#else
    [UpdateInGroup(typeof(Latios.Systems.TickedInterpolateSuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct InterpolateOptimizedSkeletonsSystem : ISystem, ILatiosApi
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);
            m_query = state.Fluent().With<InterpolateOptimizedSkeletonTag, TickedOptimizedSkeletonState, TickedOptimizedBoneTransform>(true)
                      .With<OptimizedSkeletonHierarchyBlobReference, DependentSkinnedMesh, Unity.Transforms.LocalToWorld>(true).With<OptimizedSkeletonState,
                                                                                                                                     OptimizedBoneTransform>(false).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api = this.GetApi(ref state);
            var job = new Job
            {
                factor = api.worldBlackboardEntity.GetComponentData<TickingState>().finalTickFraction,
            }.Inject(api);
            state.Dependency = job.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        partial struct Job : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<TickedOptimizedSkeletonState> tickedStateHandle;
            [ReadOnly, Inject] BufferTypeHandle<TickedOptimizedBoneTransform> tickedBonesHandle;
            [ReadOnly, Inject] ComponentTypeHandle<OptimizedSkeletonHierarchyBlobReference> hierarchyHandle;
            [ReadOnly, Inject] ComponentTypeHandle<Unity.Transforms.LocalToWorld> transformHandle;
            [Inject] ComponentTypeHandle<OptimizedSkeletonState> stateHandle;
            [Inject] BufferTypeHandle<OptimizedBoneTransform> boneTransformHandle;
            public float factor;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var ltws        = chunk.GetComponentDataPtrRO(ref transformHandle);
                var hierarchies = chunk.GetNativeArray(ref hierarchyHandle);
                var boneBuffers = chunk.GetBufferAccessor(ref boneTransformHandle);
                var states      = chunk.GetNativeArray(ref stateHandle);

                var tickedStates      = chunk.GetComponentDataPtrRO(ref tickedStateHandle);
                var tickedBoneBuffers = chunk.GetBufferAccessor(ref tickedBonesHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var boneBuffer       = boneBuffers[i];
                    DynamicBuffer<OptimizedBoneInertialBlendState> dummyBlendBuffer = default;
                    var skeletonAspect   = new OptimizedSkeletonAspect(in ltws[i],
                                                                       new RefRO<OptimizedSkeletonHierarchyBlobReference>(hierarchies,
                                                                                                                          i),
                                                                       new RefRW<OptimizedSkeletonState>(states, i),
                                                                       ref boneBuffer,
                                                                       ref dummyBlendBuffer,
                                                                       default);
                    var dstBones = skeletonAspect.rawLocalTransformsRW;

                    var tickedState = tickedStates[i];
                    var tickedBones = tickedBoneBuffers[i].AsNativeArray();
                    if (tickedBones.Length != dstBones.Length * 6)
                    {
                        if (tickedBones.Length == dstBones.Length)
                            continue;

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        throw new System.InvalidOperationException(
                            $"TickedOptimizedBoneTransform buffer length {tickedBones.Length} does not match OptimizedBoneTransform buffer length {dstBones.Length * 6}");
#endif
                    }

                    var mask          = (byte)(tickedState.state & OptimizedSkeletonState.Flags.RotationMask);
                    var rotation      = OptimizedSkeletonState.PreviousFromMask[mask];
                    var previousBones = tickedBones.GetSubArray(rotation * 2 * dstBones.Length + dstBones.Length, dstBones.Length);
                    var currentBones  = previousBones;
                    if ((tickedState.state & OptimizedSkeletonState.Flags.IsDirty) == OptimizedSkeletonState.Flags.IsDirty)
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                        if ((tickedState.state & OptimizedSkeletonState.Flags.NeedsSync) == OptimizedSkeletonState.Flags.NeedsSync)
                        {
                            throw new System.InvalidOperationException(
                                "Ticked optimized skeleton has not had its bones synced yet. You must call EndSamplingAndSync() prior to interpolation.");
                        }
#endif
                        rotation     = OptimizedSkeletonState.CurrentFromMask[mask];
                        currentBones = tickedBones.GetSubArray(rotation * 2 * dstBones.Length + dstBones.Length, dstBones.Length);
                    }

                    for (int j = 0; j < dstBones.Length; j++)
                    {
                        var prev      = previousBones[j];
                        var curr      = currentBones[j];
                        var rot       = math.nlerp(prev.boneTransform.rotation, curr.boneTransform.rotation, factor);
                        var pos       = math.lerp(prev.boneTransform.position, curr.boneTransform.position, factor);
                        var scale     = math.lerp(prev.boneTransform.scale, curr.boneTransform.scale, factor);
                        var stretch   = math.lerp(prev.boneTransform.stretch, curr.boneTransform.stretch, factor);
                        var context32 = curr.boneTransform.context32;
                        dstBones[j] = new TransformQvvs(pos, rot, scale, stretch, context32);
                    }
                    skeletonAspect.EndSamplingAndSync();
                }
            }
        }
    }
#endif
}

