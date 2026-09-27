using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Latios.Kinemation
{
    /// <summary>
    /// An optional component that when present will be enabled for the duration of the frame
    /// following a frame it was rendered by some view (including shadows), and disabled otherwise.
    /// Usage: Add, remove, and read the enabled state.
    /// </summary>
    public struct RenderVisibilityFeedbackFlag : IComponentData, IEnableableComponent { }

    /// <summary>
    /// A struct which defines features that can be optionally enabled to run prior to culling in
    /// order to support custom graphics operations which require the outputs of these features.
    /// By default, all of these features are disabled during custom graphics for performance reasons.
    /// These features are still (additionally) ran after culling for normal rendering purposes.
    /// </summary>
    public struct EnableUpdatingInCustomGraphics : IComponentData
    {
        byte packed;
        /// <summary>
        /// Enable the system that uploads Dynamic Meshes to also run during the custom graphics phase
        /// </summary>
        public bool dynamicMeshes
        {
            get => Bits.GetBit(packed, 0);
            set => Bits.SetBit(ref packed, 0, value);
        }
        /// <summary>
        /// Enable the system that processes blend shapes to also run during the custom graphics phase
        /// </summary>
        public bool blendShapes
        {
            get => Bits.GetBit(packed, 1);
            set => Bits.SetBit(ref packed, 1, value);
        }
        /// <summary>
        /// Enable the system that processes skinned mesh skeletal deformations to also run during the custom graphics phase
        /// </summary>
        public bool skinning
        {
            get => Bits.GetBit(packed, 2);
            set => Bits.SetBit(ref packed, 2, value);
        }
        /// <summary>
        /// Enable the system that uploads material properties to also run during the custom graphics phase
        /// </summary>
        public bool materialProperties
        {
            get => Bits.GetBit(packed, 3);
            set => Bits.SetBit(ref packed, 3, value);
        }
        /// <summary>
        /// Enable the system that processes Calligraphics text to also run during the custom graphics phase
        /// </summary>
        public bool text
        {
            get => Bits.GetBit(packed, 4);
            set => Bits.SetBit(ref packed, 4, value);
        }
    }

    /// <summary>
    /// Add to a rendered entity such as a deforming mesh if you only intend to use it for custom graphics rendering.
    /// For example, you might use this for a custom skinned mesh that is only used to spawn particles in VFX Graph.
    /// </summary>
    public struct UsedOnlyForCustomGraphicsTag : IComponentData { }

    /// <summary>
    /// Added to the worldBlackboardEntity by KinemationBootstrap when the platform cannot support
    /// rendering, such as when running with -nographics, without a Scriptable Render Pipeline, or
    /// on a device without compute shader support. Kinemation, other framework modules, and add-ons
    /// check for this and skip creating any system that needs a graphics device, so animation and
    /// simulation still run. Check for it in your own installers if you add such systems.
    /// </summary>
    public struct NoGraphicsTag : IComponentData { }

    /// <summary>
    /// Add this component to entities when you can promise that all entities within the chunk will use the exact
    /// same MaterialMeshInfo values (excluding LOD Pack). This can usually be enforced through the use of
    /// ISharedComponentData. Promising this may unlock some optimizations with rendering a large amount of
    /// distant entities using the same MaterialMeshInfo data.
    /// </summary>
    public struct PromiseAllEntitiesInChunkUseSameMaterialMeshInfoTag : IComponentData { }

    /// <summary>
    /// Contains the visibility mask for the current camera culling pass
    /// Usage: Read or Write
    /// This is a chunk component and also a WriteGroup target.
    /// To iterate these, you must include ChunkHeader in your query.
    /// Every mesh entity has one of these as a chunk component,
    /// with a max of 128 mesh instances per chunk.
    /// A true value for a bit will cause the mesh at that index to be rendered
    /// by the current camera. This must happen inside the KinemationCullingSuperSystem.
    /// </summary>
    public struct ChunkPerCameraCullingMask : IComponentData
    {
        public BitField64 lower;
        public BitField64 upper;

        public ulong GetUlongFromIndex(int index) => index == 0 ? lower.Value : upper.Value;
        public void ClearBitAtIndex(int index)
        {
            if (index < 64)
                lower.SetBits(index, false);
            else
                upper.SetBits(index - 64, false);
        }
    }

    /// <summary>
    /// Contains shadow split mask for the current light culling pass.
    /// Usage: Read or Write
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct ChunkPerCameraCullingSplitsMask : IComponentData
    {
        [FieldOffset(0)] public fixed byte  splitMasks[128];
        [FieldOffset(0)] public fixed ulong ulongMasks[16];  // Ensures 8 byte alignment which is helpful (16 would be better)
    }

    /// <summary>
    /// Contains the bitwise ORed visibility mask for all previous camera culling passes leading up to this dispatch.
    /// Usage: Write in KinemationCustomGraphicsSetupSuperSystem. Read everywhere else.
    /// In a system that updates inside KinemationCustomGraphicsSetupSuperSystem, you can set bits to true to enable processing for
    /// custom effects.
    /// You can read from this to figure out if an entity requires GPU data dispatches.
    /// </summary>
    public struct ChunkPerDispatchCullingMask : IComponentData
    {
        public BitField64 lower;
        public BitField64 upper;

        internal void ClearBitAtIndex(int index)
        {
            if (index < 64)
                lower.SetBits(index, false);
            else
                upper.SetBits(index - 64, false);
        }
    }

    /// <summary>
    /// Contains the bitwise ORed visibility mask for all previous dispatches this frame.
    /// Usage: Read Only (No exceptions!)
    /// You can read from this to figure out if a previous culling dispatch rendered an entity.
    /// Bitwise OR with ChunkPerDispatchCullingMask to obtain whether a previous camera pass rendered an entity.
    /// </summary>
    [WriteGroup(typeof(ChunkPerCameraCullingMask))]
    public struct ChunkPerFrameCullingMask : IComponentData
    {
        public BitField64 lower;
        public BitField64 upper;
    }

    /// <summary>
    /// The culling planes of the camera for the current culling pass
    /// Usage: Read Only (No exceptions!)
    /// This lives on the worldBlackboardEntity and is set on the main thread for each camera.
    /// For SIMD culling, use Kinemation.CullingUtilities and Unity.Rendering.FrustumPlanes.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct CullingPlane : IBufferElementData
    {
        public UnityEngine.Plane plane;
    }

    /// <summary>
    /// The culling splits of the shadow-casting light for the current culling pass
    /// Usage: Read Only (No exceptions!)
    /// This lives on the worldBlackboardEntity and is set on the main thread for each camera.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct CullingSplitElement : IBufferElementData
    {
        public CullingSplit split;
    }

    /// <summary>
    /// Useful culling paramaters of the camera for the current culling pass
    /// Usage: Read Only (No exceptions!)
    /// This lives on the worldBlackboardEntity and is set on the main thread for each camera.
    /// </summary>
    public struct CullingContext : IComponentData
    {
        public LODParameters              lodParameters;
        public float4x4                   localToWorldMatrix;
        public BatchCullingViewType       viewType;
        public BatchCullingProjectionType projectionType;
        public BatchCullingFlags          cullingFlags;
        public BatchPackedCullingViewID   viewID;
        public ulong                      sceneCullingMask;
        public uint                       cullingLayerMask;
        public int                        receiverPlaneOffset;
        public int                        receiverPlaneCount;
        public int                        cullIndexThisFrame;
    }

    /// <summary>
    /// Useful GPU dispatch parameters for the current dispatch pass
    /// </summary>
    public struct DispatchContext : IComponentData
    {
        public uint globalSystemVersionOfLatiosEntitiesGraphics;
        public uint lastSystemVersionOfLatiosEntitiesGraphics;
        public int  dispatchIndexThisFrame;

        public bool isCustomGraphicsDispatch => dispatchIndexThisFrame == 0;
    }

    /// <summary>
    /// Mask of components in a chunk which are dirty and need to be uploaded
    /// if an entity in the chunk is rendered
    /// Usage: Write only if necessary
    /// Change filtering on material properties does not work during culling.
    /// Instead, you can write a true to the material property index instead.
    /// Be careful, because changing a property of an entity rendered by a
    /// previous camera is a race condition.
    /// </summary>
    public struct ChunkMaterialPropertyDirtyMask : IComponentData
    {
        public BitField64 lower;
        public BitField64 upper;
    }

    /// <summary>
    /// The types of components that the ChunkMaterialPropertyDirtyMask corresponds to.
    /// Usage: Read Only (No exceptions!)
    /// This lives on the worldBlackboardEntity and is set whenever the global list of instanced
    /// material properties changes. Search through this buffer to find the correct bit to set in
    /// the ChunkMaterialPropertyDirtyMask. All types are created via ComponentType.ReadOnly().
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct MaterialPropertyComponentType : IBufferElementData
    {
        public ComponentType type;
    }

    /// <summary>
    /// Provides unmanaged RenderMeshArray view access to evaluate occluder candidates
    /// </summary>
    public struct OcclusionCullingContextAspect : ICollectionAspect<OcclusionCullingContextAspect>
    {
        [ReadOnly] internal NativeParallelHashMap<int, BRGRenderMeshArray>      brgRenderMeshArrays;
        [ReadOnly] internal NativeHashMap<int, BrgRenderMeshArrayIdToIndexMaps> brgRenderMeshArraysIdToIndexMaps;
        HasChecker<UseMmiRangeLodTag>                                           useMmiRangeLodChecker;
        HasChecker<OverrideMeshInRangeTag>                                      overrideMeshInRangeChecker;

        /// <summary>
        /// Gets a resolver for the chunk that can evaluate MaterialMeshInfo instances within the chunk
        /// </summary>
        /// <param name="chunk">The chunk to evaluate</param>
        /// <param name="renderMeshArrayTypeHandle"></param>
        /// <returns></returns>
        public ChunkMaterialMeshInfoResolver GetResolver(in ArchetypeChunk chunk, ref SharedComponentTypeHandle<RenderMeshArray> renderMeshArrayTypeHandle)
        {
            BRGRenderMeshArray              rma         = default;
            BrgRenderMeshArrayIdToIndexMaps maps        = default;
            var                             sharedIndex = chunk.GetSharedComponentIndex(renderMeshArrayTypeHandle);
            var                             valid       = sharedIndex != -1 &&
                              brgRenderMeshArrays.TryGetValue(sharedIndex, out rma) && brgRenderMeshArraysIdToIndexMaps.TryGetValue(sharedIndex,
                                                                                                                                    out maps);
            return new ChunkMaterialMeshInfoResolver
            {
                rma                 = rma,
                idToIndexMaps       = maps,
                valid               = valid,
                useMmiRangeLod      = useMmiRangeLodChecker[chunk],
                overrideMeshInRange = overrideMeshInRangeChecker[chunk],
            };
        }

        /// <summary>
        /// A per-chunk structure that can identify all meshes, materials, and submeshes within a MaterialMeshInfo
        /// that are about to be rendered
        /// </summary>
        public struct ChunkMaterialMeshInfoResolver
        {
            internal BRGRenderMeshArray              rma;
            internal BrgRenderMeshArrayIdToIndexMaps idToIndexMaps;
            internal bool                            valid;
            internal bool                            useMmiRangeLod;
            internal bool                            overrideMeshInRange;

            /// <summary>
            /// Gets the meshes, materials, and submeshes as well as their RenderMeshArray indices and appends them
            /// to the resultsList
            /// </summary>
            /// <param name="materialMeshInfo">The MaterialMeshInfo to extract from</param>
            /// <param name="resultsList">The list that results are appended to. This method does not clear this list.</param>
            public void Resolve(MaterialMeshInfo materialMeshInfo, ref UnsafeList<MaterialMeshSubmesh> resultsList)
            {
                if (!valid)
                {
                    if (materialMeshInfo.HasMaterialMeshIndexRange)
                        return;
                    if (!materialMeshInfo.IsRuntimeMesh)
                        return;
                    if (!materialMeshInfo.IsRuntimeMaterial)
                        return;
                    resultsList.Add(new MaterialMeshSubmesh
                    {
                        meshID           = materialMeshInfo.MeshID,
                        meshRmaIndex     = -1,
                        materialID       = materialMeshInfo.MaterialID,
                        materialRmaIndex = -1,
                        submeshIndex     = materialMeshInfo.SubMesh
                    });
                    return;
                }
                if (!materialMeshInfo.HasMaterialMeshIndexRange)
                {
                    var meshRmaIndex = materialMeshInfo.MeshArrayIndex;
                    var meshID       = meshRmaIndex == -1 ? materialMeshInfo.MeshID : rma.GetMeshID(materialMeshInfo);
                    if (meshID == BatchMeshID.Null)
                        return;
                    var materialRmaIndex = materialMeshInfo.MaterialArrayIndex;
                    var materialID       = materialRmaIndex == -1 ? materialMeshInfo.MaterialID : rma.GetMaterialID(materialMeshInfo);
                    if (materialID == BatchMaterialID.Null)
                        return;
                    resultsList.Add(new MaterialMeshSubmesh
                    {
                        meshID           = meshID,
                        meshRmaIndex     = meshRmaIndex,
                        materialID       = materialID,
                        materialRmaIndex = materialRmaIndex,
                        submeshIndex     = materialMeshInfo.SubMesh
                    });
                }
                else
                {
                    RangeInt matMeshIndexRange = materialMeshInfo.MaterialMeshIndexRange;
                    if (matMeshIndexRange.length == 127)
                    {
                        int newLength             = (rma.MaterialMeshSubMeshes[matMeshIndexRange.start + 1].SubMeshIndex >> 16) & 0xff;
                        newLength                |= (rma.MaterialMeshSubMeshes[matMeshIndexRange.start + 2].SubMeshIndex >> 8) & 0xff00;
                        newLength                |= rma.MaterialMeshSubMeshes[matMeshIndexRange.start + 3].SubMeshIndex & 0xff0000;
                        matMeshIndexRange.length  = newLength;
                    }

                    int hiResMask = 0;
                    if (useMmiRangeLod)
                    {
                        materialMeshInfo.GetCurrentLodRegion(out var hiResLodIndex, out var isMmiCrossfading);
                        if (isMmiCrossfading)
                            return;
                        hiResMask = 1 << hiResLodIndex;

                        // Late check if any of the elements are in the LOD. We'd prefer to filter these out sooner, but it is still good to check here.
                        if (matMeshIndexRange.length > 0)
                        {
                            var combinedMask = (rma.MaterialMeshSubMeshes[matMeshIndexRange.start].SubMeshIndex >> 16) & 0xff;
                            if ((combinedMask & hiResMask) == 0)
                                return;
                        }
                    }

                    BatchMeshID overrideMesh = default;
                    if (overrideMeshInRange)
                        overrideMesh = materialMeshInfo.IsRuntimeMesh ? materialMeshInfo.MeshID : rma.GetMeshID(materialMeshInfo);

                    for (int i = 0; i < matMeshIndexRange.length; i++)
                    {
                        int matMeshSubMeshIndex = matMeshIndexRange.start + i;

                        // Drop the draw command if OOB. Errors should have been reported already so no need to log anything
                        if (matMeshSubMeshIndex >= rma.MaterialMeshSubMeshes.Length)
                            continue;

                        BatchMaterialMeshSubMesh matMeshSubMesh = rma.MaterialMeshSubMeshes[matMeshSubMeshIndex];

                        if (useMmiRangeLod)
                        {
                            var  mmsmMask = matMeshSubMesh.SubMeshIndex >> 24;
                            bool isHi     = (mmsmMask & hiResMask) != 0;
                            if (!isHi)
                                continue;
                        }

                        int meshRmaIndex = -1;
                        if (overrideMeshInRange)
                            matMeshSubMesh.Mesh = overrideMesh;
                        if (!idToIndexMaps.meshIdToRmaIndex.TryGetValue(matMeshSubMesh.Mesh, out meshRmaIndex))
                            continue;

                        if (!idToIndexMaps.materialIdToRmaIndex.TryGetValue(matMeshSubMesh.Material, out var materialRmaIndex))
                            continue;

                        resultsList.Add(new MaterialMeshSubmesh
                        {
                            meshID           = matMeshSubMesh.Mesh,
                            meshRmaIndex     = meshRmaIndex,
                            materialID       = matMeshSubMesh.Material,
                            materialRmaIndex = materialRmaIndex,
                            submeshIndex     = (ushort)(matMeshSubMesh.SubMeshIndex & 0xffff)
                        });
                    }
                }
            }
        }

        public struct MaterialMeshSubmesh
        {
            /// <summary>
            /// The BatchMeshID being used
            /// </summary>
            public BatchMeshID meshID;
            /// <summary>
            /// The index of the mesh in the RenderMeshArray. -1 if not present in the RenderMeshArray.
            /// </summary>
            public int meshRmaIndex;
            /// <summary>
            /// The BatchMaterialID being used
            /// </summary>
            public BatchMaterialID materialID;
            /// <summary>
            /// The index of the material in the RenderMeshArray. -1 if not present in the RenderMeshArray.
            /// </summary>
            public int materialRmaIndex;
            /// <summary>
            /// The index of the submesh within the mesh
            /// </summary>
            public ushort submeshIndex;
        }

        FluentQuery ICollectionAspect<OcclusionCullingContextAspect>.AppendToQuery(FluentQuery query)
        {
            // Todo: Can't query for the Exists component directly since it is in the same assembly.
            return query.With<CullingContext, WorldBlackboardTag>(true);
        }

        OcclusionCullingContextAspect ICollectionAspect<OcclusionCullingContextAspect>.CreateCollectionAspect(LatiosWorldUnmanaged latiosWorld,
                                                                                                              EntityManager entityManager,
                                                                                                              Entity entity)
        {
            var context = latiosWorld.GetCollectionComponent<BrgCullingContext>(latiosWorld.worldBlackboardEntity, true);
            return new OcclusionCullingContextAspect
            {
                brgRenderMeshArrays              = context.brgRenderMeshArrays,
                brgRenderMeshArraysIdToIndexMaps = context.brgRenderMeshArrayIdToIndexMaps
            };
        }
    }
}

