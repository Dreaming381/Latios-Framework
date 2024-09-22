using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Kinemation
{
    #region All Meshes
    /// <summary>
    /// An optional component that when present will be enabled for the duration of the frame
    /// following a frame it was rendered by some view (including shadows), and disabled otherwise.
    /// Usage: Add, remove, and read the enabled state.
    /// </summary>
    public struct RenderVisibilityFeedbackFlag : IComponentData, IEnableableComponent { }

    /// <summary>
    /// QVVS Transforms: An optional matrix that is applied after computing the final WorldTransform.
    /// It can be used for additional squash, stretch, and shear effects on a renderer.
    ///
    /// Unity Transforms: An optional matrix used to inform culling that a skinned mesh has
    /// a different transform than the skeleton root. This transform must be assigned such that
    /// the skinned mesh's LocalToWorld = math.mul(PostProcessMatrix, skeleton LocalToWorld)
    /// </summary>
    /// <remarks>
    /// If you remove this component from an entity which also has a PreviousPostProcessMatrix,
    /// you may also want to set PreviousPostProcessMatrix to this value via an ECB obtained
    /// from latiosWorldUnmanaged.syncPoint. Otherwise, there may be motion vector artifacts for
    /// a frame.
    /// </remarks>
    public struct PostProcessMatrix : IComponentData
    {
        public float3x4 postProcessMatrix;
    }

    /// <summary>
    /// The previous frame's PostProcessMatrix used for rendering motion vectors.
    /// </summary>
    public struct PreviousPostProcessMatrix : IComponentData
    {
        public float3x4 postProcessMatrix;
    }

    /// <summary></summary>
    /// An optional flag which specifies when a deformed mesh needs to be rebound
    /// Usage: Add/Enable this component whenever binding needs to occur.
    /// Binding must occur whenever the presence or value of any of the following changes:
    /// - BindSkeletonRoot
    /// - MeshDeformDataBlobReference
    /// - MeshBindingPathsBlobReference
    /// - OverrideSkinningBoneIndex
    /// An initial attempt at binding will be made when the KinemationBindingReactiveSystem
    /// first processes a mesh entity, even without this flag component.
    /// However, if the flag component is present but disabled, no binding
    /// attempt will be made.
    /// </summary>
    public struct NeedsBindingFlag : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// The deformable mesh's deform data to be used for skinning, blend shapes, or other operations on the GPU
    /// Usage: Typically Read Only (Add/Write for procedural meshes)
    /// After binding, if this component is removed, the mesh is assumed to be destroyed.
    /// </summary>
    public struct MeshDeformDataBlobReference : IComponentData
    {
        public BlobAssetReference<MeshDeformDataBlob> blob;
    }

    /// <summary>
    /// An additional offset to the bounds to account for vertex shader effects
    /// Usage: Add/Write to apply additional bounds inflation for a mesh
    /// that modifies vertex positions in its shader (aside from skinning)
    /// </summary>
    public struct ShaderEffectRadialBounds : IComponentData
    {
        public float radialBounds;
    }

    /// <summary>
    /// Instructs this entity to reuse deform data from another entity with the
    /// same mesh rather than perform its own deformations
    /// Usage: Add to share a deformation from another entity.
    /// This is used when an authored Deforming Mesh is decomposed into multiple
    /// RenderMesh instances with different submesh indices.
    /// Entities with this component also inherit the frustum culling status of
    /// their reference.
    /// </summary>
    public struct CopyDeformFromEntity : IComponentData
    {
        public EntityWith<MeshDeformDataBlobReference> sourceDeformedEntity;
    }
    #endregion

    #region Skinned Meshes
    /// <summary>
    /// The skeleton entity this skinned mesh should be bound to
    /// Usage: Add/Write to attach a skinned mesh to a skeleton.
    /// </summary>
    public struct BindSkeletonRoot : IComponentData
    {
        public EntityWith<SkeletonRootTag> root;
    }

    /// <summary>
    /// Authored binding paths used to help a skinned mesh find the correct bones
    /// in the skeleton to bind to
    /// Usage: Typically Read Only (Add/Write for procedural meshes)
    /// After binding, this component can be safely removed.
    /// </summary>
    public struct MeshBindingPathsBlobReference : IComponentData
    {
        public BlobAssetReference<MeshBindingPathsBlob> blob;
    }

    /// <summary>
    /// This buffer allows for manual binding of bone indices with the skeleton
    /// Usage: Add/Write to create a custom mapping from mesh bone indices to
    /// skeleton bone indices. The length of this buffer must match the length
    /// of MeshSkinningBlob.bindPoses.
    /// After binding, this buffer can be safely removed.
    /// </summary>
    public struct OverrideSkinningBoneIndex : IBufferElementData
    {
        public short boneOffset;
    }

    /// <summary>
    /// An entity that becomes the parent target for all skinned mesh entities
    /// which failed to bind to their intended skeleton target
    /// Usage: Query to get the entity where all children are failed bindings.
    /// Currently the transform system (including framework variants) will
    /// crash if a Parent has a Null entity value.
    /// For performance reasons, all bound entities have Parent and LocalToParent
    /// components added even if the bindings failed. So entities with failed
    /// bindings are parented to a singleton entity with this tag instead.
    /// </summary>
    public struct FailedBindingsRootTag : IComponentData { }

    /// <summary>
    /// When an entity is deformed via a "Deform" shader as opposed to a "VertexSkinning"
    /// shader, this tag specifies that skinning should be performed using the
    /// Dual Quaternion Skinning algorithm. The presence of this tag is evaluated every
    /// frame and does not require rebinding.
    /// Usage: Add to an entity to make it use Dual Quaternion Skinning. Remove to return
    /// to matrix skinning.
    /// </summary>
    public struct DualQuaternionSkinningDeformTag : IComponentData { }

    #endregion

    #region Other Mesh Deformations
    /// <summary>
    /// A buffer of vertices for meshes that need to be procedurally deformed on the CPU.
    /// The buffer is actually triple-buffered.
    /// Usage: Prefer to use the DynamicMeshAspect instead of this component directly.
    /// </summary>
    public struct DynamicMeshVertex : IBufferElementData
    {
        public float3 position;
        public float3 normal;
        public float3 tangent;
    }

    /// <summary>
    /// The control state for a DynamicMesh. The state keeps track of the rotation of the
    /// triple buffering as well as whether anything modified the mesh.
    /// Usage: Prefer to use the DynamicMeshAspect instead of this component directly.
    /// </summary>
    public struct DynamicMeshState : IComponentData
    {
        public enum Flags : byte
        {
            RotationMask = 0x07,
            WasPreviousDirty = 0x04,
            IsDirty = 0x20,
        }

        public Flags state;

        // mask & 3 == current write set
        // mask & 4 == wasPreviousDirty
        // mask != 3
        internal static readonly int[] CurrentFromMask  = { 0, 1, 2, 0, 0, 1, 2 };
        internal static readonly int[] PreviousFromMask = { 2, 0, 1, 2, 2, 0, 1 };
        internal static readonly int[] TwoAgoFromMask   = { 2, 0, 1, 1, 1, 2, 0 };
    }

    /// <summary>
    /// Specifies the maximum distance any vertex in the dynamic mesh traveled relative to
    /// the source mesh. This value is used to compute bounds for culling. It may be sufficient
    /// to specify a constant overestimate rather than computing this value each frame.
    /// Usage: You must write to the component for dynamic mesh entities to ensure correct culling.
    /// </summary>
    public struct DynamicMeshMaxVertexDisplacement : IComponentData
    {
        public float maxDisplacement;
    }

    /// <summary>
    /// Specifies that compute shader operations (aside from uploading the DynamicMesh)
    /// should be skipped. Add this if you need to perform skinning or other built-in
    /// compute shader operations on the CPU.
    /// </summary>
    public struct DisableComputeShaderProcessingTag : IComponentData { }

    /// <summary>
    /// A buffer of weights by blend shape. This buffer is actually triple-buffered.
    /// Usage: Prefer to use the BlendShapeAspect instead of this component directly.
    /// </summary>
    public struct BlendShapeWeight : IBufferElementData
    {
        public float weight;
    }

    /// <summary>
    /// The control state for Blend Shapes. The state keeps track of the rotation of the
    /// triple buffering as well as whether anything modified the mesh.
    /// Usage: Prefer to use the BlendShapeAspect instead of this component directly.
    /// </summary>
    public struct BlendShapeState : IComponentData
    {
        public enum Flags : byte
        {
            RotationMask = 0x07,
            WasPreviousDirty = 0x04,
            IsDirty = 0x20,
        }

        public Flags state;

        // mask & 3 == current write set
        // mask & 4 == wasPreviousDirty
        // mask != 3
        internal static readonly int[] CurrentFromMask  = { 0, 1, 2, 0, 0, 1, 2 };
        internal static readonly int[] PreviousFromMask = { 2, 0, 1, 2, 2, 0, 1 };
        internal static readonly int[] TwoAgoFromMask   = { 2, 0, 1, 1, 1, 2, 0 };
    }

    #endregion

    #region Material Properties
    [MaterialProperty("_latiosCurrentVertexSkinningMatrixBase")]
    public struct CurrentMatrixVertexSkinningShaderIndex : IComponentData
    {
        public uint firstMatrixIndex;
    }

    [MaterialProperty("_latiosPreviousVertexSkinningMatrixBase")]
    public struct PreviousMatrixVertexSkinningShaderIndex : IComponentData
    {
        public uint firstMatrixIndex;
    }

    [MaterialProperty("_latiosTwoAgoVertexSkinningMatrixBase")]
    public struct TwoAgoMatrixVertexSkinningShaderIndex : IComponentData
    {
        public uint firstMatrixIndex;
    }

    [MaterialProperty("_latiosCurrentVertexSkinningDqsBase")]
    public struct CurrentDqsVertexSkinningShaderIndex : IComponentData
    {
        public uint firstDqsWorldIndex;
        public uint firstDqsBindposeIndex;
    }

    [MaterialProperty("_latiosPreviousVertexSkinningDqsBase")]
    public struct PreviousDqsVertexSkinningShaderIndex : IComponentData
    {
        public uint firstDqsWorldIndex;
        public uint firstDqsBindposeIndex;
    }

    [MaterialProperty("_latiosTwoAgoVertexSkinningDqsBase")]
    public struct TwoAgoDqsVertexSkinningShaderIndex : IComponentData
    {
        public uint firstDqsWorldIndex;
        public uint firstDqsBindposeIndex;
    }

    [MaterialProperty("_latiosCurrentDeformBase")]
    public struct CurrentDeformShaderIndex : IComponentData
    {
        public uint firstVertexIndex;
    }

    [MaterialProperty("_latiosPreviousDeformBase")]
    public struct PreviousDeformShaderIndex : IComponentData
    {
        public uint firstVertexIndex;
    }

    [MaterialProperty("_latiosTwoAgoDeformBase")]
    public struct TwoAgoDeformShaderIndex : IComponentData
    {
        public uint firstVertexIndex;
    }
    #endregion

    #region BlobData
    /// <summary>
    /// A specialized data structure for compute skinning.
    /// The technique employed was invented for the Latios Framework.
    /// </summary>
    /// <remarks>
    /// Bone transforms are accessed by multiple vertices for a single mesh.
    /// Therefore, they area good candidate to cache in groupshared memory.
    /// Because of this, a single threadgroup is responsible for an entire mesh.
    /// Vertices are packed in batches of 1024. At the start of each batch is
    /// a "fake" header bone weight that specifies the offset to the next batch.
    /// The header also includes the number of vertices in the mesh.
    /// Then follows the first bone weight for each of the 1024 vertices in the batch.
    /// Each bone weight contains the weight and and a packed integer.
    /// The last two bytes of the packed integer contain the bone index.
    /// The first 10 bits of the packed integer contain the offset to the next
    /// bone weight for that index. If the weight is negative, then the bone
    /// weight is the last for the vertex. For debugging and possibly other purposes,
    /// the MeshSkinningBlob has boneWeightBatchStarts which contain the indices
    /// of the header bone weights. The array is not uploaded or used by the GPU.
    /// This algorithm is 50% faster and uses less buffers and mememory compared to
    /// more traditional bone compute skinning algorithms that use a start and count
    /// per vertex.
    /// </remarks>
    public struct BoneWeightLinkedList
    {
        public float weight;
        public uint  next10Lds7Bone15;
    }

    public struct BindPoseDqs
    {
        public quaternion real;
        public quaternion dual;
        public float4     scale;
    }

    /// <summary>
    /// Blob data specific to skinning
    /// </summary>
    public struct MeshSkinningBlob
    {
        public BlobArray<float3x4>             bindPoses;
        public BlobArray<BindPoseDqs>          bindPosesDQ;
        public BlobArray<float>                maxRadialOffsetsInBoneSpaceByBone;
        public BlobArray<BoneWeightLinkedList> boneWeights;
        public BlobArray<uint>                 boneWeightBatchStarts;

        public bool hasBindPoses => bindPoses.Length > 0;
        public bool hasDeformBoneWeights => boneWeights.Length > 0;
    }

    /// <summary>
    /// A single displacement of a vertex in a blend shape
    /// </summary>
    public struct BlendShapeVertexDisplacement
    {
        public uint   targetVertexIndex;
        public float3 positionDisplacement;
        public float3 normalDisplacement;
        public float3 tangentDisplacement;
    }

    /// <summary>
    /// Contains metadata about a blend shape on the GPU.
    /// Blend shapes which target the exact same vertices
    /// in the exact same order share a permutationID.
    /// </summary>
    public struct BlendShapeVertexDisplacementShape
    {
        public uint start;
        public uint count;
        public uint permutationID;
    }

    /// <summary>
    /// Blob data specific to blend shapes
    /// </summary>
    public struct MeshBlendShapesBlob
    {
        public BlobArray<BlendShapeVertexDisplacementShape> shapes;
        public BlobArray<BlendShapeVertexDisplacement>      gpuData;
        public BlobArray<FixedString128Bytes>               shapeNames;
        public BlobArray<float>                             maxRadialOffsets;

        public bool hasBlendShapes => shapes.Length > 0;
    }

    /// <summary>
    /// Blob data specific to vertex normals and tangents and vertex duplication.
    /// Each vertex in a mesh contains multiple attributes, but sometimes a vertex
    /// requires two separate instances of a given attribute. In such cases, the
    /// vertex is duplicated for each divergence.
    /// Smoothing groups are a common example. Hard edges have duplicate vertices
    /// which share the same positions but have different normals and tangents.
    /// UV seams also duplicate vertices which not only create a different set of UVs.
    /// Tangents are computed based on a combination of the surrounding vertex normals
    /// in the same smoothing group and surrounding vertex UV0s in the same island.
    /// If a vertex is a hard edge or a UV0 seam, it will have split tangents.
    /// This complex data structure is designed to represent this complexity for
    /// complex deformation effects. It is typically not recommended you work with this
    /// data structure directly, but rather use the methods inside SkinningAlgorithms.
    /// </summary>
    public unsafe struct MeshNormalizationBlob
    {
        public enum IndicesPackMode : byte
        {
            Bits10,  // 3 indices per uint
            Bits16,  // 2 indices per uint
            Bits21,  // 3 indices per ulong
            Bits32  // 1 index per uint
        }

        /// <summary>
        /// Packed vertex indices triplets per triangle. Prefer to use GetIndicesForTriangle() and triangleCount to iterate.
        /// If any of the submesh topologies are not triangles, this will be empty.
        /// </summary>
        public BlobArray<uint> packedIndicesByTriangle;
        /// <summary>
        /// UV0s of the mesh, used to recalculate tangents. If the mesh does not have UV0s, this will be empty.
        /// </summary>
        public BlobArray<float2> uvs;
        /// <summary>
        /// A compact data structure used to mark which vertices are duplicates of a preceeding vertex's position.
        /// It also contains a prefix sum that combined with some bit math can be used to index
        /// packedPositionDuplicateReferencePairs. Prefer to use IsPositionDuplicate() and
        /// GetDuplicatePositionReferenceIndex() to sample. The "reference" vertex will not be marked.
        /// </summary>
        public BlobArray<BitFieldPrefixSumPair> positionDuplicates;
        /// <summary>
        /// A compact data structure containing pairs of vertex indices where the first in the pair is a
        /// duplicating vertex of position, and the second is a reference vertex with the same position
        /// preceeding in the mesh. Iterate with GetDuplicatePositionAtRawIndex() and duplicatePositionCount.
        /// </summary>
        public BlobArray<uint> packedPositionDuplicateReferencePairs;
        /// <summary>
        /// A compact data structure used to mark which vertices are duplicates of a prior vertex's position and normal.
        /// Such vertices typically belong to the same smoothing group. Prefer to use IsNormalDuplicate()
        /// and GetDuplicateNormalReferenceIndex() to sample. If IsNormalDuplicate() is true,
        /// IsPositionDuplicate() will also be true, though it may have a different reference index.
        /// </summary>
        public BlobArray<BitFieldPrefixSumPair> normalDuplicates;
        /// <summary>
        /// A compact data structure containing pairs of vertex indices where the first in the pair is a
        /// duplicating vertex of position and normal, and the second is a reference vertex with the same position
        /// and normal preceeding in the mesh. Iterate with GetDuplicateNormalAtRawIndex() and duplicateNormalCount.
        /// </summary>
        public BlobArray<uint> packedNormalDuplicateReferencePairs;
        /// <summary>
        /// A compact data structure used to mark which vertices are duplicates of a prior vertex's position, normal and tangent.
        /// Such vertices typically belong to the same smoothing group. Prefer to use IsTangentDuplicate()
        /// and GetDuplicateTangentReferenceIndex() to sample. If IsTangentDuplicate() is true,
        /// IsNormalDuplicate() will also be true, though it may have a different reference index.
        /// </summary>
        public BlobArray<BitFieldPrefixSumPair> tangentDuplicates;
        /// <summary>
        /// A compact data structure containing pairs of vertex indices where the first in the pair is a
        /// duplicating vertex of position, normal, and tangent, and the second is a reference vertex with the same
        /// position, normal, and tangent preceeding in the mesh. Iterate with GetDuplicateTangentAtRawIndex() and
        /// duplicateTangentCount.
        /// </summary>
        public BlobArray<uint> packedTangentDuplicateReferencePairs;
        /// <summary>
        /// The number of triangles whose vertex indices are packed into packedIndicesByTriangle.
        /// </summary>
        public int triangleCount;
        /// <summary>
        /// The number of duplicate index pairs packed into packedPositionDuplicateReferencePairs.
        /// </summary>
        public int duplicatePositionCount;
        /// <summary>
        /// The number of duplicate index pairs packed into packedNormalDuplicateReferencePairs.
        /// </summary>
        public int duplicateNormalCount;
        /// <summary>
        /// The number of duplicate index pairs packed into packedTangentDuplicateReferencePairs.
        /// </summary>
        public int duplicateTangentCount;
        /// <summary>
        /// How indices for triangles and duplicates are packed.
        /// </summary>
        public IndicesPackMode packMode;

        public bool hasMeshNormalizationData => triangleCount > 0;

        /// <summary>
        /// Gets the 3 vertex indices for a given triangle. Duplicates are NOT redirected.
        /// These indices match the source mesh, except base offsets are applied.
        /// </summary>
        /// <param name="triangleIndex">The index of the triangle.</param>
        /// <returns>Three vertex indices</returns>
        public int3 GetIndicesForTriangle(int triangleIndex)
        {
            CheckTriangleIndex(triangleIndex);
            int indexA                     = 0, indexB = 0, indexC = 0;
            var packedIndicesByTrianglePtr = packedIndicesByTriangle.GetUnsafePtr();
            switch (packMode)
            {
                case IndicesPackMode.Bits10:
                {
                    var indices = ((uint*)packedIndicesByTrianglePtr)[triangleIndex];
                    indexA      = (int)(indices & 0x3ff);
                    indexB      = (int)((indices >> 10) & 0x3ff);
                    indexC      = (int)((indices >> 20) & 0x3ff);
                    break;
                }
                case IndicesPackMode.Bits16:
                {
                    var indices = (ushort*)packedIndicesByTrianglePtr;
                    indexA      = indices[triangleIndex * 3];
                    indexB      = indices[triangleIndex * 3 + 1];
                    indexC      = indices[triangleIndex * 3 + 2];
                    break;
                }
                case IndicesPackMode.Bits21:
                {
                    var indices = (ulong*)packedIndicesByTrianglePtr;
                    indexA      = (int)(indices[triangleIndex] & 0x1fffff);
                    indexB      = (int)((indices[triangleIndex] >> 21) & 0x1fffff);
                    indexC      = (int)((indices[triangleIndex] >> 42) & 0x1fffff);
                    break;
                }
                case IndicesPackMode.Bits32:
                {
                    indexA = (int)((uint*)packedIndicesByTrianglePtr)[triangleIndex * 3];
                    indexB = (int)((uint*)packedIndicesByTrianglePtr)[triangleIndex * 3 + 1];
                    indexC = (int)((uint*)packedIndicesByTrianglePtr)[triangleIndex * 3 + 2];
                    break;
                }
            }
            return new int3(indexA, indexB, indexC);
        }
        /// <summary>
        /// True if the vertex is a duplicate of position and has a reference vertex.
        /// The reference vertex will return false for this.
        /// </summary>
        public bool IsPositionDuplicate(int vertexIndex) => positionDuplicates[vertexIndex >> 5].bitfield.IsSet(vertexIndex & 0x1f);
        /// <summary>
        /// True if the vertex is a duplicate of position and normal and has a reference vertex.
        /// The reference vertex will return false for this.
        /// </summary>
        public bool IsNormalDuplicate(int vertexIndex) => normalDuplicates[vertexIndex >> 5].bitfield.IsSet(vertexIndex & 0x1f);
        /// <summary>
        /// True if the vertex is a duplicate of position, normal, and tangent and has a reference vertex.
        /// The reference vertex will return false for this.
        /// </summary>
        public bool IsTangentDuplicate(int vertexIndex) => tangentDuplicates[vertexIndex >> 5].bitfield.IsSet(vertexIndex & 0x1f);
        /// <summary>
        /// Gets the duplicate position vertex index and its reference vertex for the pair specified by the raw pair index.
        /// </summary>
        /// <param name="rawIndex">The raw pair index less than duplicatePositionCount</param>
        /// <param name="duplicateIndex">The duplicate vertex index</param>
        /// <param name="referenceIndex">The reference vertex index</param>
        public void GetDuplicatePositionAtRawIndex(int rawIndex, out int duplicateIndex, out int referenceIndex)
        {
            CheckDuplicateIndex(rawIndex, duplicatePositionCount);
            GetDuplicateAtRawIndex(rawIndex, out duplicateIndex, out referenceIndex, ref packedPositionDuplicateReferencePairs);
        }
        /// <summary>
        /// Gets the duplicate position and normal vertex index and its reference vertex for the pair specified by the raw pair index.
        /// </summary>
        /// <param name="rawIndex">The raw pair index less than duplicateNormalCount</param>
        /// <param name="duplicateIndex">The duplicate vertex index</param>
        /// <param name="referenceIndex">The reference vertex index</param>
        public void GetDuplicateNormalAtRawIndex(int rawIndex, out int duplicateIndex, out int referenceIndex)
        {
            CheckDuplicateIndex(rawIndex, duplicateNormalCount);
            GetDuplicateAtRawIndex(rawIndex, out duplicateIndex, out referenceIndex, ref packedNormalDuplicateReferencePairs);
        }
        /// <summary>
        /// Gets the duplicate position, normal, and tangent vertex index and its reference vertex for the pair specified by the raw pair index.
        /// </summary>
        /// <param name="rawIndex">The raw pair index less than duplicateTangentCount</param>
        /// <param name="duplicateIndex">The duplicate vertex index</param>
        /// <param name="referenceIndex">The reference vertex index</param>
        public void GetDuplicateTangentAtRawIndex(int rawIndex, out int duplicateIndex, out int referenceIndex)
        {
            CheckDuplicateIndex(rawIndex, duplicateTangentCount);
            GetDuplicateAtRawIndex(rawIndex, out duplicateIndex, out referenceIndex, ref packedTangentDuplicateReferencePairs);
        }
        /// <summary>
        /// Gets the duplicate position vertex's reference vertex index.
        /// </summary>
        public int GetDuplicatePositionReferenceIndex(int positionDuplicatingVertex)
        {
            return GetDuplicateReferenceIndex(positionDuplicatingVertex, ref positionDuplicates, ref packedPositionDuplicateReferencePairs);
        }
        /// <summary>
        /// Gets the duplicate position and normal vertex's reference vertex index.
        /// </summary>
        public int GetDuplicateNormalReferenceIndex(int normalDuplicatingVertex)
        {
            return GetDuplicateReferenceIndex(normalDuplicatingVertex, ref normalDuplicates, ref packedNormalDuplicateReferencePairs);
        }
        /// <summary>
        /// Gets the duplicate position, normal, and tangent vertex's reference vertex index.
        /// </summary>
        public int GetDuplicateTangentReferenceIndex(int tangentDuplicatingVertex)
        {
            return GetDuplicateReferenceIndex(tangentDuplicatingVertex, ref tangentDuplicates, ref packedTangentDuplicateReferencePairs);
        }

        private void GetDuplicateAtRawIndex(int rawIndex, out int duplicateIndex, out int referenceIndex, ref BlobArray<uint> packed)
        {
            int2 i         = 2 * rawIndex;
            i.y            = i.x + 1;
            duplicateIndex = 0;
            referenceIndex = 0;
            switch (packMode)
            {
                case IndicesPackMode.Bits10:
                {
                    var loadIndex  = i / 3;
                    var shift      = 10 * (i % 3);
                    duplicateIndex = (int)(packed[loadIndex.x] >> shift.x) & 0x3ff;
                    referenceIndex = (int)(packed[loadIndex.y] >> shift.y) & 0x3ff;
                    break;
                }
                case IndicesPackMode.Bits16:
                {
                    duplicateIndex = ((ushort*)packed.GetUnsafePtr())[i.x];
                    referenceIndex = ((ushort*)packed.GetUnsafePtr())[i.y];
                    break;
                }
                case IndicesPackMode.Bits21:
                {
                    var loadIndex  = i / 3;
                    var shift      = 21 * (i % 3);
                    var ptr        = (ulong*)packed.GetUnsafePtr();
                    duplicateIndex = (int)(ptr[loadIndex.x] >> shift.x) & 0x1fffff;
                    referenceIndex = (int)(ptr[loadIndex.y] >> shift.y) & 0x1fffff;
                    break;
                }
                case IndicesPackMode.Bits32:
                {
                    duplicateIndex = (int)packed[i.x];
                    referenceIndex = (int)packed[i.y];
                    break;
                }
            }
        }
        private int GetDuplicateReferenceIndex(int duplicatingVertex, ref BlobArray<BitFieldPrefixSumPair> lookup, ref BlobArray<uint> packed)
        {
            var element  = lookup[duplicatingVertex >> 5];
            var bit      = duplicatingVertex & 0x1f;
            var mask     = math.select(0xffffffff >> 32 - bit, 0, bit == 0);
            var rawIndex = element.prefixSum + math.countbits(element.bitfield.Value & mask);
            GetDuplicateAtRawIndex(rawIndex, out _, out var result, ref packed);
            return result;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckTriangleIndex(int triangleIndex)
        {
            if (math.clamp(triangleIndex, 0, triangleCount) != triangleIndex)
                throw new ArgumentOutOfRangeException($"Triangle index {triangleIndex} is out of range of MeshNormalizationBlob with {triangleCount} triangles.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckDuplicateIndex(int rawDuplicate, int duplicateCount)
        {
            if (math.clamp(rawDuplicate, 0, duplicateCount) != rawDuplicate)
                throw new ArgumentOutOfRangeException($"Duplicate index {rawDuplicate} is out of range of MeshNormalizationBlob with {duplicateCount} duplicate vertices.");
        }

        public struct BitFieldPrefixSumPair
        {
            public BitField32 bitfield;
            public int        prefixSum;
        }
    }

    /// <summary>
    /// The original vertex data of a mesh prior to deformations.
    /// Used by both skinning and blend shapes.
    /// </summary>
    public struct UndeformedVertex
    {
        public float3 position;
        public float3 normal;
        public float3 tangent;
    }

    /// <summary>
    /// Combined skinning, blend shape, and other deformation blob data for a Mesh.
    /// Some values may not be present, depending on the MeshDeformDataFeatures specified during baking.
    /// </summary>
    public struct MeshDeformDataBlob
    {
        public BlobArray<UndeformedVertex> undeformedVertices;
        public MeshSkinningBlob            skinningData;
        public MeshBlendShapesBlob         blendShapesData;
        public MeshNormalizationBlob       normalizationData;
        public Psyshock.Aabb               undeformedAabb;
        public FixedString128Bytes         name;

        public int uniqueVertexPositionsCount => undeformedVertices.Length - normalizationData.duplicatePositionCount;

        public bool hasUndeformedVertices => undeformedVertices.Length > 0;
    }

    /// <summary>
    /// Contains bone path names for each bone the mesh is bound to
    /// </summary>
    public struct MeshBindingPathsBlob
    {
        // Todo: Make this a BlobArray<BlobString> once supported in Burst
        public BlobArray<BlobArray<byte> > pathsInReversedNotation;
    }
    #endregion
}

