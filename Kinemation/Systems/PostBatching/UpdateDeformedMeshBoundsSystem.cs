using Latios.Psyshock;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateDeformedMeshBoundsSystem : ISystem, ILatiosApi
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);
            m_query = state.Fluent().With<BoundMesh>(true).With<RenderBounds>(false).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api          = this.GetApi(ref state);
            state.Dependency = new Job
            {
                lastSystemVersion = state.LastSystemVersion
            }.Inject(api).ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        partial struct Job : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<BoundMesh>                        blobHandle;
            [ReadOnly, Inject] ComponentTypeHandle<DynamicMeshMaxVertexDisplacement> dynamicMeshMaxVertexDisplacementHandle;
            [ReadOnly, Inject] BufferTypeHandle<BlendShapeWeight>                    blendShapeWeightsHandle;
            [ReadOnly, Inject] ComponentTypeHandle<BlendShapeState>                  blendShapeStateHandle;
            [ReadOnly, Inject] ComponentTypeHandle<ShaderEffectRadialBounds>         shaderBoundsHandle;

            [Inject] ComponentTypeHandle<RenderBounds> localBoundsHandle;

            public uint lastSystemVersion;

            [NoAlias, NativeDisableContainerSafetyRestriction] NativeArray<float> tempFloatBuffer;
            bool                                                                  radialBoundsDirty;

            HasChecker<SkeletonDependent> skeletonDependentChecker;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool hasDynamicMesh  = chunk.Has(ref dynamicMeshMaxVertexDisplacementHandle);
                bool hasBlendShapes  = chunk.Has(ref blendShapeWeightsHandle) && chunk.Has(ref blendShapeStateHandle);
                bool hasShaderBounds = chunk.Has(ref shaderBoundsHandle);
                bool hasSkeleton     = skeletonDependentChecker[chunk];
                bool hasLocalBounds  = chunk.Has(ref localBoundsHandle);
                bool didOrderChange  = chunk.DidOrderChange(lastSystemVersion);

                // We only care about didOrderChange if the chunk is missing a component, because that component could have been removed.
                // This isn't much of an optimization, but is still relatively cheap to compute.
                bool renderBoundsChanged        = !hasSkeleton && hasLocalBounds && chunk.DidChange(ref localBoundsHandle, lastSystemVersion);
                bool meshNeedsUpdate            = hasDynamicMesh && chunk.DidChange(ref dynamicMeshMaxVertexDisplacementHandle, lastSystemVersion);
                meshNeedsUpdate                |= (!hasDynamicMesh && didOrderChange);
                meshNeedsUpdate                |= renderBoundsChanged;
                bool blendShapesNeedsUpdate     = hasBlendShapes && chunk.DidChange(ref blendShapeWeightsHandle, lastSystemVersion);
                blendShapesNeedsUpdate         |= (!hasBlendShapes && didOrderChange);
                blendShapesNeedsUpdate         |= renderBoundsChanged;
                bool skeletonShaderNeedsUpdate  = hasSkeleton && hasShaderBounds && chunk.DidChange(ref shaderBoundsHandle, lastSystemVersion);
                skeletonShaderNeedsUpdate      |= (!hasShaderBounds && didOrderChange && hasSkeleton);

                if (!(meshNeedsUpdate || blendShapesNeedsUpdate || skeletonShaderNeedsUpdate))
                    return;

                if (!tempFloatBuffer.IsCreated)
                    tempFloatBuffer = new NativeArray<float>(128, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                var localRadialBounds = tempFloatBuffer.GetSubArray(0, chunk.Count);

                bool previousRadialBoundsDirty = radialBoundsDirty;
                radialBoundsDirty              = true;
                if (hasDynamicMesh && hasShaderBounds && hasSkeleton)
                {
                    var mesh   = chunk.GetNativeArray(ref dynamicMeshMaxVertexDisplacementHandle);
                    var shader = chunk.GetNativeArray(ref shaderBoundsHandle);
                    for (int i = 0; i < chunk.Count; i++)
                        localRadialBounds[i] = mesh[i].maxDisplacement + shader[i].radialBounds;
                }
                else if (hasDynamicMesh)
                    localRadialBounds.CopyFrom(chunk.GetNativeArray(ref dynamicMeshMaxVertexDisplacementHandle).Reinterpret<float>());
                else if (hasShaderBounds && hasSkeleton)
                    localRadialBounds.CopyFrom(chunk.GetNativeArray(ref shaderBoundsHandle).Reinterpret<float>());
                else if (hasSkeleton || hasBlendShapes || previousRadialBoundsDirty)
                {
                    UnsafeUtility.MemClear(localRadialBounds.GetUnsafePtr(), sizeof(float) * chunk.Count);
                    radialBoundsDirty = false;
                }
                else
                    radialBoundsDirty = false;

                if (hasBlendShapes)
                {
                    radialBoundsDirty   = true;
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
                        var     weightsBase  = (dirty ? BlendShapeState.CurrentFromMask[mask] : BlendShapeState.PreviousFromMask[mask]) * shapeOffsets.Length;
                        if (weights.Length < weightsBase + shapeOffsets.Length)
                        {
                            // We are unitialized, so just grab all the weights we do have.
                            for (int j = 0; j < math.min(weights.Length, shapeOffsets.Length); j++)
                            {
                                localRadialBounds[i] += math.abs(weights[j]) * shapeOffsets[j];
                            }
                        }
                        else
                        {
                            weights = weights.GetSubArray(weightsBase, shapeOffsets.Length);
                            for (int j = 0; j < shapeOffsets.Length; j++)
                            {
                                localRadialBounds[i] += math.abs(weights[j]) * shapeOffsets[j];
                            }
                        }
                    }
                }

                if (hasSkeleton)
                {
                    // If we have a skeleton, we want the local RenderBounds to just be radial expansions, as this will be added
                    // to the skeleton bounds to create the WorldRenderBounds.
                    var bounds = chunk.GetNativeArray(ref localBoundsHandle);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        bounds[i] = new RenderBounds { Value = new AABB { Center = float3.zero, Extents = localRadialBounds[i] } };
                    }
                }
                else if (hasLocalBounds)
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

