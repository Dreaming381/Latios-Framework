using System;
using System.Collections.Generic;
using Latios.Psyshock;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

namespace Latios.Kinemation
{
    // Meshes
    internal struct MatrixPreviousCache : IComponentData
    {
        public float2x4 cachedFirstTwoRows;
    }

    [WriteGroup(typeof(LocalToParent))]
    internal struct SkeletonDependent : ICleanupComponentData
    {
        public EntityWith<SkeletonRootTag>                  root;
        public BlobAssetReference<MeshSkinningBlob>         skinningBlob;
        public BlobAssetReference<MeshBindingPathsBlob>     meshBindingBlob;
        public BlobAssetReference<SkeletonBindingPathsBlob> skeletonBindingBlob;
        public int                                          meshEntryIndex;
        public int                                          boneOffsetEntryIndex;
        public float                                        shaderEffectRadialBounds;
    }

    [MaterialProperty("_ComputeMeshIndex")]
    internal struct ComputeDeformShaderIndex : IComponentData
    {
        public uint firstVertexIndex;
    }

    [WriteGroup(typeof(ChunkPerCameraCullingMask))]
    internal struct ChunkComputeDeformMemoryMetadata : IComponentData
    {
        public int vertexStartPrefixSum;
    }

    [MaterialProperty("_SkinMatrixIndex")]
    internal struct LinearBlendSkinningShaderIndex : IComponentData
    {
        public int firstMatrixIndex;
    }

    [WriteGroup(typeof(ChunkPerCameraCullingMask))]
    internal struct ChunkLinearBlendSkinningMemoryMetadata : IComponentData
    {
        public int bonesStartPrefixSum;
    }

    [WriteGroup(typeof(ChunkPerCameraCullingMask))]
    internal struct ChunkCopySkinShaderData : IComponentData
    {
        // Todo: Can chunk components be tags?
        //internal byte dummy;
    }

    // All skeletons
    // This is system state to prevent copies on instantiate
    [InternalBufferCapacity(1)]
    internal struct DependentSkinnedMesh : ICleanupBufferElementData
    {
        public EntityWith<SkeletonDependent> skinnedMesh;
        public int                           meshVerticesStart;
        public int                           meshVerticesCount;
        public int                           meshWeightsStart;
        public int                           meshBindPosesStart;
        public int                           meshBindPosesCount;
        public int                           boneOffsetsStart;
    }

    [MaximumChunkCapacity(128)]
    internal struct PerFrameSkeletonBufferMetadata : IComponentData
    {
        public int bufferId;
        public int startIndexInBuffer;
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
        public float radialOffsetInWorldSpace;
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

    internal struct SkeletonShaderBoundsOffset : IComponentData
    {
        public float radialBoundsInWorldSpace;
    }

    internal struct SkeletonWorldBounds : IComponentData
    {
        public AABB bounds;
    }

    // The length of this should be 0 when no meshes are bound.
    [InternalBufferCapacity(0)]
    internal struct OptimizedBoneBounds : IBufferElementData
    {
        public float radialOffsetInBoneSpace;
    }

    internal struct ChunkSkeletonWorldBounds : IComponentData
    {
        public AABB chunkBounds;
    }

    #region Blackboard
    internal struct ExposedCullingIndexManagerTag : IComponentData { }

    internal struct ExposedCullingIndexManager : ICollectionComponent
    {
        public NativeHashMap<Entity, int>                                  skeletonToCullingIndexMap;
        public NativeReference<int>                                        maxIndex;
        public NativeList<int>                                             indexFreeList;
        public NativeHashMap<int, EntityWithBuffer<DependentSkinnedMesh> > cullingIndexToSkeletonMap;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<ExposedCullingIndexManagerTag>();

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

    internal struct MeshGpuManagerTag : IComponentData { }

    internal struct MeshGpuUploadCommand
    {
        public BlobAssetReference<MeshSkinningBlob> blob;
        public int                                  verticesIndex;
        public int                                  weightsIndex;
        public int                                  bindPosesIndex;
    }

    internal struct MeshGpuEntry
    {
        public BlobAssetReference<MeshSkinningBlob> blob;
        public int                                  referenceCount;
        public int                                  verticesStart;
        public int                                  weightsStart;
        public int                                  bindPosesStart;
        // Blob Assets can disappear before cleanup (thanks subscenes).
        // So we cache the sizes here since that's all we need for cleanup.
        public int verticesCount;
        public int weightsCount;
        public int bindPosesCount;
    }

    internal struct MeshGpuRequiredSizes
    {
        public int requiredVertexBufferSize;
        public int requiredWeightBufferSize;
        public int requiredBindPoseBufferSize;
        public int requiredVertexUploadSize;
        public int requiredWeightUploadSize;
        public int requiredBindPoseUploadSize;
    }

    internal struct MeshGpuManager : ICollectionComponent
    {
        public NativeHashMap<BlobAssetReference<MeshSkinningBlob>, int> blobIndexMap;

        public NativeList<MeshGpuEntry> entries;
        public NativeList<int>          indexFreeList;
        public NativeList<int2>         verticesGaps;
        public NativeList<int2>         weightsGaps;
        public NativeList<int2>         bindPosesGaps;

        public NativeList<MeshGpuUploadCommand>      uploadCommands;
        public NativeReference<MeshGpuRequiredSizes> requiredBufferSizes;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<MeshGpuManagerTag>();

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
            inputDeps = uploadCommands.Dispose(inputDeps);
            inputDeps = requiredBufferSizes.Dispose(inputDeps);
            return inputDeps;
        }
    }

    internal struct BoneOffsetsGpuManagerTag : IComponentData { }

    internal struct BoneOffsetsEntry
    {
        public uint2 hash;
        public int   pathsReferences;
        public int   overridesReferences;
        public int   start;
        public short count;
        public short gpuCount;
        public bool  isValid;
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

    internal struct BoneOffsetsGpuManager : ICollectionComponent
    {
        public NativeList<BoneOffsetsEntry> entries;
        public NativeList<short>            offsets;
        public NativeList<int>              indexFreeList;
        public NativeList<int2>             gaps;
        public NativeReference<bool>        isDirty;

        public NativeHashMap<uint2, int>           hashToEntryMap;
        public NativeHashMap<PathMappingPair, int> pathPairToEntryMap;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<BoneOffsetsGpuManagerTag>();

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

    internal struct GpuUploadBuffersTag : IComponentData { }

    internal struct GpuUploadBuffers : IManagedStructComponent
    {
        // Not owned by this
        public UnityEngine.ComputeBuffer verticesBuffer;
        public UnityEngine.ComputeBuffer weightsBuffer;
        public UnityEngine.ComputeBuffer bindPosesBuffer;
        public UnityEngine.ComputeBuffer boneOffsetsBuffer;
        public UnityEngine.ComputeBuffer verticesUploadBuffer;
        public UnityEngine.ComputeBuffer weightsUploadBuffer;
        public UnityEngine.ComputeBuffer bindPosesUploadBuffer;
        public UnityEngine.ComputeBuffer boneOffsetsUploadBuffer;
        public UnityEngine.ComputeBuffer verticesUploadMetaBuffer;
        public UnityEngine.ComputeBuffer weightsUploadMetaBuffer;
        public UnityEngine.ComputeBuffer bindPosesUploadMetaBuffer;
        public UnityEngine.ComputeBuffer boneOffsetsUploadMetaBuffer;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<GpuUploadBuffersTag>();
    }

    internal struct GpuUploadBuffersMappedTag : IComponentData { }

    internal struct GpuUploadBuffersMapped : ICollectionComponent
    {
        // No actual containers here, but represents the mappings of the compute buffers.
        public int  verticesUploadBufferWriteCount;
        public int  weightsUploadBufferWriteCount;
        public int  bindPosesUploadBufferWriteCount;
        public int  boneOffsetsUploadBufferWriteCount;
        public int  verticesUploadMetaBufferWriteCount;
        public int  weightsUploadMetaBufferWriteCount;
        public int  bindPosesUploadMetaBufferWriteCount;
        public int  boneOffsetsUploadMetaBufferWriteCount;
        public bool needsMeshCommitment;
        public bool needsBoneOffsetCommitment;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<GpuUploadBuffersMappedTag>();

        public JobHandle TryDispose(JobHandle inputDeps) => inputDeps;
    }

    internal struct ComputeBufferManagerTag : IComponentData { }

    internal struct ComputeBufferManager : IManagedStructComponent
    {
        public ComputeBufferTrackingPool pool;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<ComputeBufferManagerTag>();
    }

    internal struct BrgCullingContextTag : IComponentData { }

    internal unsafe struct BrgCullingContext : ICollectionComponent
    {
        //public BatchCullingContext cullingContext;
        //public NativeArray<int>    internalToExternalMappingIds;
        public ThreadLocalAllocator                            cullingThreadLocalAllocator;
        public BatchCullingOutput                              batchCullingOutput;
        public NativeParallelHashMap<int, BatchFilterSettings> batchFilterSettingsByRenderFilterSettingsSharedIndex;
#if UNITY_EDITOR
        public NativeParallelHashMap<int, BatchEditorRenderData> batchEditorSharedIndexToSceneMaskMap;
        public IncludeExcludeListFilter includeExcludeListFilter;
#endif

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<BrgCullingContextTag>();
        public JobHandle TryDispose(JobHandle inputDeps)
        {
            // We don't own this data
            return inputDeps;
        }
    }

    internal struct PackedCullingSplitsTag : IComponentData { }

    internal struct PackedCullingSplits : ICollectionComponent
    {
        public NativeReference<CullingSplits> packedSplits;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<PackedCullingSplitsTag>();
        public JobHandle TryDispose(JobHandle inputDeps)
        {
            // The collections inside packedSplits is managed by a RewindableAllocator,
            // but the NativeReference is allocated persistently.
            if (packedSplits.IsCreated)
                return packedSplits.Dispose(inputDeps);
            return inputDeps;
        }
    }

    internal struct BoneMatricesPerFrameBuffersManagerTag : IComponentData { }

    internal struct BoneMatricesPerFrameBuffersManager : IManagedStructComponent
    {
        public List<UnityEngine.ComputeBuffer> boneMatricesBuffers;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<BoneMatricesPerFrameBuffersManagerTag>();
    }

    internal struct MaxRequiredDeformVertices : IComponentData
    {
        public int verticesCount;
    }

    internal struct MaxRequiredLinearBlendMatrices : IComponentData
    {
        public int matricesCount;
    }

    internal struct MaterialPropertiesUploadContextTag : IComponentData { }

    internal struct MaterialPropertiesUploadContext : ICollectionComponent
    {
        public NativeList<ValueBlitDescriptor> valueBlits;

        public int                        hybridRenderedChunkCount;
        public NativeArray<ChunkProperty> chunkProperties;

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<MaterialPropertiesUploadContextTag>();
        public JobHandle TryDispose(JobHandle inputDeps)
        {
            // We don't own this data
            return inputDeps;
        }
    }

    internal struct ExposedSkeletonBoundsArraysTag : IComponentData { }

    internal struct ExposedSkeletonBoundsArrays : ICollectionComponent
    {
        public NativeList<AABB> allAabbs;
        public NativeList<AABB> batchedAabbs;
        public const int        kCountPerBatch = 32;  // Todo: Is there a better size?

        public ComponentType AssociatedComponentType => ComponentType.ReadWrite<ExposedSkeletonBoundsArraysTag>();
        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (!allAabbs.IsCreated)
                return inputDeps;

            inputDeps = allAabbs.Dispose(inputDeps);
            return batchedAabbs.Dispose(inputDeps);
        }
    }
    #endregion
}

