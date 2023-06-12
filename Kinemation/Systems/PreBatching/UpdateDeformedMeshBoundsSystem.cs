using Latios.Psyshock;
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
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateDeformedMeshBoundsSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().WithAll<BoundMesh>(true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                blobHandle                             = GetComponentTypeHandle<BoundMesh>(true),
                skeletonDependentHandle                = GetComponentTypeHandle<SkeletonDependent>(true),
                dynamicMeshMaxVertexDisplacementHandle = GetComponentTypeHandle<DynamicMeshMaxVertexDisplacement>(true),
                blendShapeWeightsHandle                = GetBufferTypeHandle<BlendShapeWeight>(true),
                blendShapeStateHandle                  = GetComponentTypeHandle<BlendShapeState>(true),
                shaderBoundsHandle                     = GetComponentTypeHandle<ShaderEffectRadialBounds>(true),
                localBoundsHandle                      = GetComponentTypeHandle<RenderBounds>(false),
                dependentSkinnedMeshLookup             = GetBufferLookup<DependentSkinnedMesh>(false),
                lastSystemVersion                      = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<BoundMesh>                        blobHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>                skeletonDependentHandle;
            [ReadOnly] public ComponentTypeHandle<DynamicMeshMaxVertexDisplacement> dynamicMeshMaxVertexDisplacementHandle;
            [ReadOnly] public BufferTypeHandle<BlendShapeWeight>                    blendShapeWeightsHandle;
            [ReadOnly] public ComponentTypeHandle<BlendShapeState>                  blendShapeStateHandle;
            [ReadOnly] public ComponentTypeHandle<ShaderEffectRadialBounds>         shaderBoundsHandle;

            public ComponentTypeHandle<RenderBounds>                                        localBoundsHandle;
            [NativeDisableParallelForRestriction] public BufferLookup<DependentSkinnedMesh> dependentSkinnedMeshLookup;

            public uint lastSystemVersion;

            [NoAlias, NativeDisableContainerSafetyRestriction] NativeArray<float> tempFloatBuffer;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool hasDynamicMesh  = chunk.Has(ref dynamicMeshMaxVertexDisplacementHandle);
                bool hasBlendShapes  = chunk.Has(ref blendShapeWeightsHandle) && chunk.Has(ref blendShapeStateHandle);
                bool hasShaderBounds = chunk.Has(ref shaderBoundsHandle);
                bool didOrderChange  = chunk.DidOrderChange(lastSystemVersion);

                // We only care about didOrderChange if the chunk is missing a component, because that component could have been removed.
                // This isn't much of an optimization, but is still relatively cheap to compute.
                bool meshNeedsUpdate         = hasDynamicMesh && chunk.DidChange(ref dynamicMeshMaxVertexDisplacementHandle, lastSystemVersion);
                meshNeedsUpdate             |= (!hasDynamicMesh && didOrderChange);
                bool blendShapesNeedsUpdate  = hasBlendShapes && chunk.DidChange(ref blendShapeWeightsHandle, lastSystemVersion);
                blendShapesNeedsUpdate      |= (!hasBlendShapes && didOrderChange);
                bool shaderNeedsUpdate       = hasShaderBounds && chunk.DidChange(ref shaderBoundsHandle, lastSystemVersion);
                shaderNeedsUpdate           |= (!hasShaderBounds && didOrderChange);

                if (!(meshNeedsUpdate || blendShapesNeedsUpdate || shaderNeedsUpdate))
                    return;

                if (!tempFloatBuffer.IsCreated)
                    tempFloatBuffer = new NativeArray<float>(128, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                var localRadialBounds = tempFloatBuffer.GetSubArray(0, chunk.Count);

                if (hasDynamicMesh && hasShaderBounds)
                {
                    var mesh   = chunk.GetNativeArray(ref dynamicMeshMaxVertexDisplacementHandle);
                    var shader = chunk.GetNativeArray(ref shaderBoundsHandle);
                    for (int i = 0; i < chunk.Count; i++)
                        localRadialBounds[i] = mesh[i].maxDisplacement + shader[i].radialBounds;
                }
                else if (hasDynamicMesh)
                    localRadialBounds.CopyFrom(chunk.GetNativeArray(ref dynamicMeshMaxVertexDisplacementHandle).Reinterpret<float>());
                else if (hasShaderBounds)
                    localRadialBounds.CopyFrom(chunk.GetNativeArray(ref shaderBoundsHandle).Reinterpret<float>());
                else
                    UnsafeUtility.MemClear(localRadialBounds.GetUnsafePtr(), sizeof(float) * chunk.Count);

                if (hasBlendShapes)
                {
                    var blobs           = chunk.GetNativeArray(ref blobHandle);
                    var weightsAccessor = chunk.GetBufferAccessor(ref blendShapeWeightsHandle);
                    var shapeStates     = chunk.GetNativeArray(ref blendShapeStateHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        ref var shapeOffsets = ref blobs[i].meshBlob.Value.blendShapesData.maxRadialOffsets;
                        var     weights      = weightsAccessor[i].AsNativeArray().Reinterpret<float>();
                        var     state        = shapeStates[i];
                        var     mask         = (int)(state.state & BlendShapeState.Flags.RotationMask);
                        bool    dirty        = (state.state & BlendShapeState.Flags.IsDirty) == BlendShapeState.Flags.IsDirty;
                        var     weightsBase  = dirty ? BlendShapeState.CurrentFromMask[mask] : BlendShapeState.PreviousFromMask[mask];
                        weights              = weights.GetSubArray(weightsBase, shapeOffsets.Length);
                        for (int j = 0; j < shapeOffsets.Length; j++)
                        {
                            localRadialBounds[i] += weights[j] * shapeOffsets[j];
                        }
                    }
                }

                if (chunk.Has(ref skeletonDependentHandle))
                {
                    var skeletonDependents = chunk.GetNativeArray(ref skeletonDependentHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var skeletonDependent = skeletonDependents[i];
                        if (skeletonDependent.root == Entity.Null)
                            continue;
                        dependentSkinnedMeshLookup[skeletonDependent.root].ElementAt(skeletonDependent.indexInDependentSkinnedMeshesBuffer).meshRadialOffset = localRadialBounds[i];
                    }
                }
                else if (chunk.Has(ref localBoundsHandle))
                {
                    var bounds = chunk.GetNativeArray(ref localBoundsHandle);
                    var blobs  = chunk.GetNativeArray(ref blobHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var aabb = blobs[i].meshBlob.Value.undeformedAabb;
                        Physics.GetCenterExtents(aabb, out var center, out var extents);
                        bounds[i] = new RenderBounds { Value = new AABB { Center = center, Extents = extents + localRadialBounds[i] } };
                    }
                }
            }
        }
    }
}

