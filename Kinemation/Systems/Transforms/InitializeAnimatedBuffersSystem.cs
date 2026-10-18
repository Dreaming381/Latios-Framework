using Latios.Systems;
using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Kinemation.Systems
{
    // This system must update before TransformInitializeSuperSystem, because sockets rely on this before the
    // early transform system update. Also, MotionHistoryInitializeSuperSystem doesn't exist in Unity Transforms.
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PostSyncPointGroup), OrderFirst = true)]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct InitializeAnimatedBuffersSystem : ISystem, ILatiosApi
    {
        EntityQuery m_initSkeletonsQuery;
        EntityQuery m_initBlendShapesQuery;
        EntityQuery m_initDynamicMeshesQuery;

        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);
            m_initSkeletonsQuery = state.Fluent().With<OptimizedSkeletonState>(true).With<OptimizedBoneTransform>(false)
                                   .With<OptimizedSkeletonHierarchyBlobReference>(true).IncludeDisabledEntities().Build();
            m_initBlendShapesQuery   = state.Fluent().With<BlendShapeState>(true).With<BlendShapeWeight>(false).With<BoundMesh>(true).Build();
            m_initDynamicMeshesQuery = state.Fluent().With<DynamicMeshState>(true).With<DynamicMeshVertex>(false).With<BoundMesh>(true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api               = this.GetApi(ref state);
            var lastSystemVersion = state.LastSystemVersion;
            var skeletonJh        = new InitSkeletonJob
            {
                lastSystemVersion = lastSystemVersion,
            }.Inject(api).ScheduleParallel(m_initSkeletonsQuery, state.Dependency);

            var blendShapeJh = new InitBlendShapesJob
            {
                lastSystemVersion = lastSystemVersion,
            }.Inject(api).ScheduleParallel(m_initBlendShapesQuery, state.Dependency);

            var meshJh = new InitMeshJob
            {
                lastSystemVersion = lastSystemVersion,
            }.Inject(api).ScheduleParallel(m_initDynamicMeshesQuery, state.Dependency);

            state.Dependency = JobHandle.CombineDependencies(skeletonJh, blendShapeJh, meshJh);
        }

#if LATIOS_BURST_DETERMINISM
        [BurstCompile(FloatMode = FloatMode.Deterministic)]
#else
        [BurstCompile]
#endif
        partial struct InitSkeletonJob : IJobChunk, IInjectable
        {
            [Inject] BufferTypeHandle<OptimizedBoneTransform>                               bonesHandle;
            [Inject] ComponentTypeHandle<OptimizedSkeletonState>                            stateHandle;
            [ReadOnly, Inject] ComponentTypeHandle<OptimizedSkeletonHierarchyBlobReference> blobHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
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
        }

        [BurstCompile]
        partial struct InitBlendShapesJob : IJobChunk, IInjectable
        {
            [Inject] BufferTypeHandle<BlendShapeWeight>       weightsHandle;
            [ReadOnly, Inject] ComponentTypeHandle<BoundMesh> blobHandle;

            public uint lastSystemVersion;

            HasChecker<LiveBakedTag> liveBakedChecker;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidOrderChange(lastSystemVersion) && (!liveBakedChecker[chunk] || !chunk.DidChange(ref weightsHandle, lastSystemVersion)))
                    return;

                var  blobs      = chunk.GetNativeArray(ref blobHandle);
                var  weights    = chunk.GetBufferAccessorRO(ref weightsHandle);
                bool needsWrite = false;

                for (int i = chunk.Count - 1; i >= 0; i--)
                {
                    if (weights[i].Length != blobs[i].meshBlob.Value.blendShapesData.shapes.Length * 3)
                    {
                        needsWrite = true;
                        break;
                    }
                }

                if (!needsWrite)
                    return;

                weights = chunk.GetBufferAccessorRW(ref weightsHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    int shapes = blobs[i].meshBlob.Value.blendShapesData.shapeNames.Length;
                    if (shapes == 0)
                    {
                        UnityEngine.Debug.LogWarning($"Mesh {blobs[i].meshBlob.Value.name} does not have blend shapes!");
                        weights[i].Clear();
                    }
                    else if (weights[i].Length == shapes * 3)
                        continue;
                    else if (weights[i].Length == shapes)
                    {
                        weights[i].Resize(shapes * 3, NativeArrayOptions.UninitializedMemory);
                        var array       = weights[i].AsNativeArray();
                        var subArraySrc = array.GetSubArray(0, shapes);
                        var subArrayDst = array.GetSubArray(shapes, shapes);
                        subArrayDst.CopyFrom(subArraySrc);
                        subArrayDst = array.GetSubArray(shapes * 2, shapes);
                        subArrayDst.CopyFrom(subArraySrc);
                    }
                    else
                        weights[i].Resize(blobs[i].meshBlob.Value.blendShapesData.shapes.Length * 3, NativeArrayOptions.ClearMemory);
                }
            }
        }

        [BurstCompile]
        partial struct InitMeshJob : IJobChunk, IInjectable
        {
            [Inject] BufferTypeHandle<DynamicMeshVertex>      verticesHandle;
            [ReadOnly, Inject] ComponentTypeHandle<BoundMesh> blobHandle;

            public uint lastSystemVersion;

            HasChecker<LiveBakedTag> liveBakedChecker;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidOrderChange(lastSystemVersion) && (!liveBakedChecker[chunk] || !chunk.DidChange(ref verticesHandle, lastSystemVersion)))
                    return;

                var  blobs      = chunk.GetNativeArray(ref blobHandle);
                var  vertices   = chunk.GetBufferAccessorRO(ref verticesHandle);
                bool needsWrite = false;

                for (int i = chunk.Count - 1; i >= 0; i--)
                {
                    if (vertices[i].Length != blobs[i].meshBlob.Value.undeformedVertices.Length * 3)
                    {
                        needsWrite = true;
                        break;
                    }
                }

                if (!needsWrite)
                    return;

                vertices = chunk.GetBufferAccessorRW(ref verticesHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (vertices[i].Length != blobs[i].meshBlob.Value.undeformedVertices.Length * 3)
                    {
                        vertices[i].Resize(blobs[i].meshBlob.Value.undeformedVertices.Length * 3, NativeArrayOptions.UninitializedMemory);
                        UnsafeUtility.MemCpyReplicate(vertices[i].GetUnsafePtr(),
                                                      blobs[i].meshBlob.Value.undeformedVertices.GetUnsafePtr(),
                                                      sizeof(UndeformedVertex) * blobs[i].meshBlob.Value.undeformedVertices.Length,
                                                      3);
                    }
                }
            }
        }
    }
}

