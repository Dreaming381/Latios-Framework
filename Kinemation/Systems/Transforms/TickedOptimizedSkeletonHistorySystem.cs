using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Kinemation.Systems
{
    [UpdateInGroup(typeof(Latios.Systems.TickedUpdateHistorySuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct TickedOptimizedSkeletonHistorySystem : ISystem, ILatiosApi
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);
            m_query = state.Fluent().With<TickedOptimizedBoneTransform, TickedOptimizedSkeletonState>(false).With<OptimizedSkeletonHierarchyBlobReference>(true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api          = this.GetApi(ref state);
            state.Dependency = new Job
            {
                lastSystemVersion = state.LastSystemVersion,
                advance           = !api.worldBlackboardEntity.GetComponentData<TickingState>().discardPreviousTick
            }.Inject(api).ScheduleParallel(m_query, state.Dependency);
        }

#if LATIOS_BURST_DETERMINISM
        [BurstCompile(FloatMode = FloatMode.Deterministic)]
#else
        [BurstCompile]
#endif
        partial struct Job : IJobChunk, IInjectable
        {
            [Inject] BufferTypeHandle<TickedOptimizedBoneTransform>                         bonesHandle;
            [Inject] ComponentTypeHandle<TickedOptimizedSkeletonState>                      stateHandle;
            [ReadOnly, Inject] ComponentTypeHandle<OptimizedSkeletonHierarchyBlobReference> blobHandle;

            public uint lastSystemVersion;
            public bool advance;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Initialize(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
                Update(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
            }

            public void Initialize(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidOrderChange(lastSystemVersion))
                    return;

                var  blobs      = chunk.GetNativeArray(ref blobHandle);
                var  bones      = chunk.GetBufferAccessorRO(ref bonesHandle);
                bool needsWrite = false;

                // Much more likely for there to be a new entity at the end of a chunk.
                for (int i = chunk.Count - 1; i >= 0; i--)
                {
                    if (bones[i].Length != blobs[i].blob.Value.parentIndices.Length * 6)
                    {
                        needsWrite = true;
                        break;
                    }
                }

                if (!needsWrite)
                    return;

                bones      = chunk.GetBufferAccessorRW(ref bonesHandle);
                var states = chunk.GetNativeArray(ref stateHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var boneCount = blobs[i].blob.Value.parentIndices.Length;
                    var buffer    = bones[i];
                    if (buffer.Length == boneCount * 6)
                        continue;

                    if (buffer.Length == boneCount)
                    {
                        // Buffer only contains local transforms.
                        buffer.Resize(boneCount * 6, NativeArrayOptions.UninitializedMemory);
                        var     bufferAsArray   = buffer.Reinterpret<TransformQvvs>().AsNativeArray();
                        ref var parentIndices   = ref blobs[i].blob.Value.parentIndices;
                        var     rootTransforms  = bufferAsArray.GetSubArray(0, boneCount);
                        var     localTransforms = bufferAsArray.GetSubArray(boneCount, boneCount);
                        localTransforms.CopyFrom(rootTransforms);
                        rootTransforms[0] = TransformQvvs.identity;
                        for (int j = 1; j < boneCount; j++)
                        {
                            var parent           = math.max(0, parentIndices[j]);
                            var local            = localTransforms[j];
                            local.rotation.value = math.normalize(local.rotation.value);
                            rootTransforms[j]    = qvvs.mul(rootTransforms[parent], in local);
                        }
                        {
                            var local            = localTransforms[0];
                            local.rotation.value = math.normalize(local.rotation.value);
                            localTransforms[0]   = local;
                            rootTransforms[0]    = local;
                        }
                        bufferAsArray.GetSubArray(boneCount * 2, boneCount * 2).CopyFrom(bufferAsArray.GetSubArray(0, boneCount * 2));
                        bufferAsArray.GetSubArray(boneCount * 4, boneCount * 2).CopyFrom(bufferAsArray.GetSubArray(0, boneCount * 2));
                    }
                    else if (buffer.Length < boneCount * 6)  // Typically (buffer.Length == 0)
                    {
                        // Todo: Should we leave this uninitialized instead?
                        buffer.Resize(boneCount * 6, NativeArrayOptions.ClearMemory);
                        var s      = states[i];
                        s.state   |= OptimizedSkeletonState.Flags.NeedsHistorySync;
                        states[i]  = s;
                    }
                }
            }

            public void Update(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var states = chunk.GetNativeArray(ref stateHandle);

                if (advance)
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var  state    = states[i].state;
                        var  rotation = (byte)state & 0x3;
                        bool wasDirty = (state & OptimizedSkeletonState.Flags.IsDirty) == OptimizedSkeletonState.Flags.IsDirty;
                        if (wasDirty)
                        {
                            rotation++;
                            if (rotation >= 3)
                                rotation = 0;
                        }
                        state = (OptimizedSkeletonState.Flags)rotation;
                        if (wasDirty)
                            state |= OptimizedSkeletonState.Flags.WasPreviousDirty;
                        states[i]  = new TickedOptimizedSkeletonState { state = state };
                    }
                }
                else
                {
                    // Note: No need to concern ourselves with sockets here, because QVVS Transforms already handles restoring those.
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var state  = states[i].state;
                        state     &= ~(OptimizedSkeletonState.Flags.IsDirty | OptimizedSkeletonState.Flags.NeedsSync | OptimizedSkeletonState.Flags.NextSampleShouldAdd);
                        states[i]  = new TickedOptimizedSkeletonState { state = state };
                    }
                }
            }
        }
    }
}

