using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace Latios.Kinemation
{
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
    /// Contains the bitwise ORed visibility mask for all previous camera culling passes this frame
    /// Usage: Read Only (No exceptions!)
    /// You can read from this to figure out if a previous culling pass rendered an entity.
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
        public BatchPackedCullingViewID   viewID;
        public ulong                      sceneCullingMask;
        public uint                       cullingLayerMask;
        public int                        receiverPlaneOffset;
        public int                        receiverPlaneCount;
        public uint                       globalSystemVersionOfLatiosEntitiesGraphics;
        public uint                       lastSystemVersionOfLatiosEntitiesGraphics;
        public int                        cullIndexThisFrame;
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
}

