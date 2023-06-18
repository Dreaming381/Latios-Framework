#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using System;
using Latios.Psyshock;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine.Rendering;

namespace Latios.Kinemation
{
    #region Meshes
    internal struct SkeletonDependent : ICleanupComponentData
    {
        public EntityWith<SkeletonRootTag>                  root;
        public BlobAssetReference<MeshBindingPathsBlob>     meshBindingBlob;
        public BlobAssetReference<SkeletonBindingPathsBlob> skeletonBindingBlob;
        public int                                          boneOffsetEntryIndex;
        public int                                          indexInDependentSkinnedMeshesBuffer;
    }

    internal struct BoundMesh : ICleanupComponentData
    {
        public BlobAssetReference<MeshDeformDataBlob> meshBlob;
        public int                                    meshEntryIndex;
    }

    [WriteGroup(typeof(ChunkPerCameraCullingMask))]
    internal struct ChunkSkinningCullingTag : IComponentData { }

    [MaterialProperty("_ComputeMeshIndex")]
    internal struct LegacyComputeDeformShaderIndex : IComponentData
    {
        public uint firstVertexIndex;
    }

    [MaterialProperty("_DOTSDeformationParams")]
    internal struct LegacyDotsDeformParamsShaderIndex : IComponentData
    {
        public uint4 parameters;
        // x = current,
        // y = previous,
        // z = 0
        // w = unused
    }

    [MaterialProperty("_SkinMatrixIndex")]
    internal struct LegacyLinearBlendSkinningShaderIndex : IComponentData
    {
        public uint firstMatrixIndex;
    }

    internal struct ChunkDeformPrefixSums : IComponentData
    {
        public uint currentMatrix;
        public uint previousMatrix;
        public uint twoAgoMatrix;
        public uint currentDqs;
        public uint previousDqs;
        public uint twoAgoDqs;
        public uint currentDeform;
        public uint previousDeform;
        public uint twoAgoDeform;
        // Legacy are aliases/combinations of these
    }

    [WriteGroup(typeof(ChunkPerCameraCullingMask))]
    internal struct ChunkCopyDeformTag : IComponentData { }
    #endregion

    #region Skeletons
    // All skeletons
    // This is system state to prevent copies on instantiate
    [InternalBufferCapacity(1)]
    internal struct DependentSkinnedMesh : ICleanupBufferElementData
    {
        public EntityWith<SkeletonDependent> skinnedMesh;
        // Todo: Store entry indices instead?
        public uint  meshVerticesStart;
        public uint  meshWeightsStart;
        public uint  meshBindPosesStart;
        public uint  boneOffsetsCount;
        public uint  boneOffsetsStart;
        public float meshRadialOffset;
    }

    internal struct SkeletonBoundsOffsetFromMeshes : IComponentData
    {
        public float radialBoundsInWorldSpace;
    }

    // Exposed skeletons
    internal struct ExposedSkeletonCullingIndex : ICleanupComponentData
    {
        public int cullingIndex;
    }

    internal struct BoneCullingIndex : IComponentData
    {
        public int cullingIndex;
    }

    internal struct BoneBounds : IComponentData
    {
        public float radialOffsetInBoneSpace;
    }

    internal struct BoneWorldBounds : IComponentData
    {
        public Aabb bounds;
    }

    internal struct ChunkBoneWorldBounds : IComponentData
    {
        public AABB chunkBounds;
    }

    // Optimized skeletons

    // There's currently no other system state for optimized skeletons, so we need something
    // to track conversions between skeleton types.
    internal struct OptimizedSkeletonTag : ICleanupComponentData { }

    internal struct OptimizedSkeletonWorldBounds : IComponentData
    {
        public AABB bounds;
    }

    // The length of this should be 0 when no meshes are bound.
    [InternalBufferCapacity(0)]
    internal struct OptimizedBoneBounds : IBufferElementData
    {
        public float radialOffsetInBoneSpace;
    }

    internal struct ChunkOptimizedSkeletonWorldBounds : IComponentData
    {
        public AABB chunkBounds;
    }
    #endregion

    #region Blackboard
    internal partial struct ExposedCullingIndexManager : ICollectionComponent
    {
        public NativeHashMap<Entity, int>                                  skeletonToCullingIndexMap;
        public NativeReference<int>                                        maxIndex;
        public NativeList<int>                                             indexFreeList;
        public NativeHashMap<int, EntityWithBuffer<DependentSkinnedMesh> > cullingIndexToSkeletonMap;

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (!skeletonToCullingIndexMap.IsCreated)
                return inputDeps;

            inputDeps = skeletonToCullingIndexMap.Dispose(inputDeps);
            inputDeps = maxIndex.Dispose(inputDeps);
            inputDeps = indexFreeList.Dispose(inputDeps);
            inputDeps = cullingIndexToSkeletonMap.Dispose(inputDeps);
            return inputDeps;
        }
    }

    internal struct MeshGpuUploadCommand
    {
        public BlobAssetReference<MeshDeformDataBlob> blob;
        public uint                                   verticesIndex;
        public uint                                   weightsIndex;
        public uint                                   bindPosesIndex;
        public uint                                   blendShapesIndex;
    }

    internal struct MeshGpuEntry
    {
        public BlobAssetReference<MeshDeformDataBlob> blob;
        public int                                    referenceCount;
        public uint                                   verticesStart;
        public uint                                   weightsStart;
        public uint                                   bindPosesStart;
        public uint                                   blendShapesStart;
        // Blob Assets can disappear before cleanup (thanks subscenes).
        // So we cache the sizes here since that's all we need for cleanup.
        public uint verticesCount;
        public uint weightsCount;
        public uint bindPosesCount;
        public uint blendShapesCount;
    }

    internal struct MeshGpuRequiredSizes
    {
        public uint requiredVertexBufferSize;
        public uint requiredWeightBufferSize;
        public uint requiredBindPoseBufferSize;
        public uint requiredBlendShapesBufferSize;
        public uint requiredVertexUploadSize;
        public uint requiredWeightUploadSize;
        public uint requiredBindPoseUploadSize;
        public uint requiredBlendShapesUploadSize;
    }

    internal partial struct MeshGpuManager : ICollectionComponent
    {
        public NativeHashMap<BlobAssetReference<MeshDeformDataBlob>, int> blobIndexMap;

        public NativeList<MeshGpuEntry> entries;
        public NativeList<int>          indexFreeList;
        public NativeList<uint2>        verticesGaps;
        public NativeList<uint2>        weightsGaps;
        public NativeList<uint2>        bindPosesGaps;
        public NativeList<uint2>        blendShapesGaps;

        public NativeList<MeshGpuUploadCommand>      uploadCommands;
        public NativeReference<MeshGpuRequiredSizes> requiredBufferSizes;

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (!blobIndexMap.IsCreated)
                return inputDeps;

            inputDeps = blobIndexMap.Dispose(inputDeps);
            inputDeps = entries.Dispose(inputDeps);
            inputDeps = indexFreeList.Dispose(inputDeps);
            inputDeps = verticesGaps.Dispose(inputDeps);
            inputDeps = weightsGaps.Dispose(inputDeps);
            inputDeps = bindPosesGaps.Dispose(inputDeps);
            inputDeps = blendShapesGaps.Dispose(inputDeps);
            inputDeps = uploadCommands.Dispose(inputDeps);
            inputDeps = requiredBufferSizes.Dispose(inputDeps);
            return inputDeps;
        }
    }

    internal struct BoneOffsetsEntry
    {
        public uint2  hash;
        public int    pathsReferences;
        public int    overridesReferences;
        public uint   start;
        public ushort count;
        public ushort gpuCount;
        public bool   isValid;
    }

    internal struct PathMappingPair : IEquatable<PathMappingPair>
    {
        public BlobAssetReference<SkeletonBindingPathsBlob> skeletonPaths;
        public BlobAssetReference<MeshBindingPathsBlob>     meshPaths;

        public bool Equals(PathMappingPair other)
        {
            return skeletonPaths.Equals(other.skeletonPaths) && meshPaths.Equals(other.meshPaths);
        }

        public override int GetHashCode()
        {
            return new int2(skeletonPaths.GetHashCode(), meshPaths.GetHashCode()).GetHashCode();
        }
    }

    internal partial struct BoneOffsetsGpuManager : ICollectionComponent
    {
        public NativeList<BoneOffsetsEntry> entries;
        public NativeList<short>            offsets;
        public NativeList<int>              indexFreeList;
        public NativeList<uint2>            gaps;
        public NativeReference<bool>        isDirty;

        public NativeHashMap<uint2, int>           hashToEntryMap;
        public NativeHashMap<PathMappingPair, int> pathPairToEntryMap;

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (!entries.IsCreated)
                return inputDeps;

            inputDeps = entries.Dispose(inputDeps);
            inputDeps = offsets.Dispose(inputDeps);
            inputDeps = indexFreeList.Dispose(inputDeps);
            inputDeps = gaps.Dispose(inputDeps);
            inputDeps = isDirty.Dispose(inputDeps);
            inputDeps = hashToEntryMap.Dispose(inputDeps);
            inputDeps = pathPairToEntryMap.Dispose(inputDeps);
            return inputDeps;
        }
    }

    internal partial struct MeshGpuUploadBuffers : IManagedStructComponent
    {
        // Not owned by this
        public UnityEngine.GraphicsBuffer verticesBuffer;
        public UnityEngine.GraphicsBuffer weightsBuffer;
        public UnityEngine.GraphicsBuffer bindPosesBuffer;
        public UnityEngine.GraphicsBuffer blendShapesBuffer;
        public UnityEngine.GraphicsBuffer boneOffsetsBuffer;
        public UnityEngine.GraphicsBuffer verticesUploadBuffer;
        public UnityEngine.GraphicsBuffer weightsUploadBuffer;
        public UnityEngine.GraphicsBuffer bindPosesUploadBuffer;
        public UnityEngine.GraphicsBuffer blendShapesUploadBuffer;
        public UnityEngine.GraphicsBuffer boneOffsetsUploadBuffer;
        public UnityEngine.GraphicsBuffer verticesUploadMetaBuffer;
        public UnityEngine.GraphicsBuffer weightsUploadMetaBuffer;
        public UnityEngine.GraphicsBuffer bindPosesUploadMetaBuffer;
        public UnityEngine.GraphicsBuffer blendShapesUploadMetaBuffer;
        public UnityEngine.GraphicsBuffer boneOffsetsUploadMetaBuffer;
    }

    internal partial struct MeshGpuUploadBuffersMapped : ICollectionComponent
    {
        // No actual containers here, but represents the mappings of the compute buffers.
        public uint verticesUploadBufferWriteCount;
        public uint weightsUploadBufferWriteCount;
        public uint bindPosesUploadBufferWriteCount;
        public uint blendShapesUploadBufferWriteCount;
        public uint boneOffsetsUploadBufferWriteCount;
        public uint verticesUploadMetaBufferWriteCount;
        public uint weightsUploadMetaBufferWriteCount;
        public uint bindPosesUploadMetaBufferWriteCount;
        public uint blendShapesUploadMetaBufferWriteCount;
        public uint boneOffsetsUploadMetaBufferWriteCount;
        public bool needsMeshCommitment;
        public bool needsBoneOffsetCommitment;

        public JobHandle TryDispose(JobHandle inputDeps) => inputDeps;
    }

    internal partial struct GraphicsBufferManager : IManagedStructComponent
    {
        public GraphicsBufferTrackingPool pool;
    }

    internal unsafe partial struct BrgCullingContext : ICollectionComponent
    {
        //public BatchCullingContext cullingContext;
        //public NativeArray<int>    internalToExternalMappingIds;
        public ThreadLocalAllocator                            cullingThreadLocalAllocator;
        public BatchCullingOutput                              batchCullingOutput;
        public NativeParallelHashMap<int, BatchFilterSettings> batchFilterSettingsByRenderFilterSettingsSharedIndex;
        public NativeParallelHashMap<int, BRGRenderMeshArray>  brgRenderMeshArrays;
#if UNITY_EDITOR
        public IncludeExcludeListFilter includeExcludeListFilter;
#endif

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            // We don't own this data
            return inputDeps;
        }
    }

    internal partial struct PackedCullingSplits : ICollectionComponent
    {
        public NativeReference<CullingSplits> packedSplits;

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            // The collections inside packedSplits is managed by a RewindableAllocator,
            // but the NativeReference is allocated persistently.
            if (packedSplits.IsCreated)
                return packedSplits.Dispose(inputDeps);
            return inputDeps;
        }
    }

    internal struct MaxRequiredDeformData : IComponentData
    {
        public uint maxRequiredBoneTransformsForVertexSkinning;
        public uint maxRequiredDeformVertices;
    }

    internal partial struct MaterialPropertiesUploadContext : ICollectionComponent
    {
        public NativeList<ValueBlitDescriptor> valueBlits;

        public int                        hybridRenderedChunkCount;
        public NativeArray<ChunkProperty> chunkProperties;

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            // We don't own this data
            return inputDeps;
        }
    }

    internal partial struct ExposedSkeletonBoundsArrays : ICollectionComponent
    {
        public NativeList<AABB>  allAabbs;
        public NativeList<AABB>  batchedAabbs;
        public NativeList<AABB>  allAabbsPreOffset;
        public NativeList<float> meshOffsets;
        public const int         kCountPerBatch = 1 << 32;  // Todo: Is there a better size?

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (!allAabbs.IsCreated)
                return inputDeps;

            inputDeps = allAabbs.Dispose(inputDeps);
            inputDeps = allAabbsPreOffset.Dispose(inputDeps);
            inputDeps = meshOffsets.Dispose(inputDeps);
            return batchedAabbs.Dispose(inputDeps);
        }
    }

    internal struct BrgAabb : IComponentData
    {
        public Aabb aabb;
    }

    // Int because this will grow in the future and it would be great to not have a regression
    internal enum DeformClassification : int
    {
        None = 0,
        CurrentDeform = 1 << 0,
        PreviousDeform = 1 << 1,
        TwoAgoDeform = 1 << 2,
        CurrentVertexMatrix = 1 << 3,
        PreviousVertexMatrix = 1 << 4,
        TwoAgoVertexMatrix = 1 << 5,
        CurrentVertexDqs = 1 << 6,
        PreviousVertexDqs = 1 << 7,
        TwoAgoVertexDqs = 1 << 8,
        LegacyLbs = 1 << 9,
        LegacyCompute = 1 << 10,
        LegacyDotsDefom = 1 << 11,
        RequiresUploadDynamicMesh = 1 << 12,
        RequiresGpuComputeBlendShapes = 1 << 13,
        RequiresGpuComputeMatrixSkinning = 1 << 14,
        RequiresGpuComputeDqsSkinning = 1 << 15,
        AnyCurrentDeform = CurrentDeform | LegacyCompute | LegacyDotsDefom,
        AnyPreviousDeform = PreviousDeform | LegacyDotsDefom,
    }

    internal partial struct DeformClassificationMap : ICollectionComponent
    {
        public NativeParallelHashMap<ArchetypeChunk, DeformClassification> deformClassificationMap;

        // The data is owned by a world or system rewindable allocator.
        public JobHandle TryDispose(JobHandle inputDeps) => inputDeps;
    }
    #endregion
}
#endif

