using System;
using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Kinemation
{
    #region All Meshes
    /// <summary>
    /// An optional component that allows for quickly toggling the visibility of the entity
    /// from all cameras, including probes and shadows. If you need control over the visibility
    /// for cameras, probes, and shadows individually, it is recommended you build your own
    /// flags and culling systems using the culling mask components.
    /// Usage: Add, remove, or change the enabled state as you deem necessary.
    /// </summary>
    public struct RenderQuickToggleEnableFlag : IComponentData, IEnableableComponent { }

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
    internal struct DualQuaternionSkinningDeformTag : IComponentData { }

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
    /// </summary>
    public struct DynamicMeshMaxVertexDisplacement : IComponentData
    {
        public float maxDisplacement;
    }

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
    /// Combined skinning and blend shape blob data for a Mesh
    /// </summary>
    public struct MeshDeformDataBlob
    {
        public BlobArray<UndeformedVertex> undeformedVertices;
        public MeshSkinningBlob            skinningData;
        public MeshBlendShapesBlob         blendShapesData;
        public Psyshock.Aabb               undeformedAabb;
        public FixedString128Bytes         name;
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

