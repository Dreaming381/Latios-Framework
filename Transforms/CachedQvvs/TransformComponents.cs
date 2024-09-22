#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    // World space transform for all entities. Is also local space for root entities. It is always present, and is the only required transform component for rendering.
    /// <summary>
    /// The world-space transform for all entities. It is also the local-space transform for root entities.
    /// This component is always present, and the only required transform component for rendering with Kinemation.
    /// Usage: Typically ReadOnly. It is strongly recommended you use TransformAspect for modifying the transform in world-space.
    /// </summary>
#if UNITY_NETCODE
    [StructLayout(LayoutKind.Explicit)]
#endif
    public struct WorldTransform : IComponentData
    {
        /// <summary>
        /// The actual TransformQvvs representing the world-space transform of the entity.
        /// Directly writing to this value is heavily discouraged.
        /// </summary>
#if UNITY_NETCODE
        [FieldOffset(0)]
#endif
        public TransformQvvs worldTransform;

        /// <summary>
        /// The world-space position of the entity
        /// </summary>
        public float3 position => worldTransform.position;
        /// <summary>
        /// The world-space rotation of the entity
        /// </summary>
        public quaternion rotation => worldTransform.rotation;
        /// <summary>
        /// The world-space uniform scale of the entity
        /// </summary>
        public float scale => worldTransform.scale;
        /// <summary>
        /// The stretch of the entity relative to its coordinate space prior to local rotation and translation
        /// </summary>
        public float3 stretch => worldTransform.stretch;
        /// <summary>
        /// Convenience scale * stretch property
        /// </summary>
        public float3 nonUniformScale => scale * stretch;
        /// <summary>
        /// The worldIndex whose purpose is up to the user
        /// </summary>
        public int worldIndex => worldTransform.worldIndex;

        /// <summary>
        /// The unit forward vector (local Z+) of the entity in world-space
        /// </summary>
        public float3 forwardDirection => math.rotate(rotation, new float3(0f, 0f, 1f));
        /// <summary>
        /// The unit backward vector (local Z-) of the entity in world-space
        /// </summary>
        public float3 backwardDirection => math.rotate(rotation, new float3(0f, 0f, -1f));
        /// <summary>
        /// The unit left vector (local X-) of the entity in world-space
        /// </summary>
        public float3 leftDirection => math.rotate(rotation, new float3(-1f, 0f, 0f));
        /// <summary>
        /// The unit right vector (local X+) of the entity in world-space
        /// </summary>
        public float3 rightDirection => math.rotate(rotation, new float3(1f, 0f, 0f));
        /// <summary>
        /// The unit up vector (local Y+) of the entity in world-space
        /// </summary>
        public float3 upDirection => math.rotate(rotation, new float3(0f, 1f, 0f));
        /// <summary>
        /// The unit down vector (local Y-) of the entity in world-space
        /// </summary>
        public float3 downDirection => math.rotate(rotation, new float3(0f, -1f, 0f));

#if UNITY_NETCODE
        [FieldOffset(16)] public float3 __position;
        [FieldOffset(0)]  public quaternion __rotation;
        [FieldOffset(44)] public float __scale;
        [FieldOffset(32)] public float3 __stretch;
        [FieldOffset(28)] public int __worldIndex;
#endif
    }

    /// <summary>
    /// A cached copy of the parent's WorldTransform used internally by TransformAspect.
    /// </summary>
    public struct ParentToWorldTransform : IComponentData
    {
        public TransformQvvs parentToWorldTransform;

        public float3 position => parentToWorldTransform.position;
        public quaternion rotation => parentToWorldTransform.rotation;
        public float scale => parentToWorldTransform.scale;
        public float3 stretch => parentToWorldTransform.stretch;
        public float3 nonUniformScale => scale * stretch;
        public int worldIndex => parentToWorldTransform.worldIndex;
    }

    // Local space transform relative to the parent, only valid if parent exists
    /// <summary>
    /// A local space transform (excluding stretch) relative to the parent. It should only exist if the entity has a parent.
    /// </summary>
    public struct LocalTransform : IComponentData
    {
        /// <summary>
        /// The actual TransformQvs of the local transform. Stretch is omitted here as it is owned by WorldTransform.
        /// </summary>
        public TransformQvs localTransform;

        /// <summary>
        /// The local-space position of the entity
        /// </summary>
        public float3 position => localTransform.position;
        /// <summary>
        /// The local-space rotation of the entity
        /// </summary>
        public quaternion rotation => localTransform.rotation;
        /// <summary>
        /// The local-space uniform scale of the entity prior to inherited scale values from ancestors
        /// </summary>
        public float scale => localTransform.scale;
    }

    /// <summary>
    /// The desired Parent of the entity. Modify this to change the entity's parent.
    /// </summary>
    public struct Parent : IComponentData
    {
        public Entity parent;
    }

    /// <summary>
    /// The actual parent of the entity as last seen by the Transform System. Do not modify.
    /// </summary>
    public struct PreviousParent : ICleanupComponentData
    {
        public Entity previousParent;
    }

    /// <summary>
    /// The list of children of this entity as last seen by the Transform System. Do not modify.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct Child : ICleanupBufferElementData
    {
        public EntityWith<Parent> child;
    }

    /// <summary>
    /// The WorldTransform from the previous frame. This gets used for motion vectors, but may also be read for gameplay purposes.
    /// </summary>
    public struct PreviousTransform : IComponentData
    {
        public TransformQvvs worldTransform;

        public float3 position => worldTransform.position;
        public quaternion rotation => worldTransform.rotation;
        public float scale => worldTransform.scale;
        public float3 stretch => worldTransform.stretch;
        public float3 nonUniformScale => scale * stretch;
        public int version => worldTransform.worldIndex;
        public bool isInitialized => version != 0;
    }

    /// <summary>
    /// The WorldTransform from two frames ago. This may be read for gameplay purposes.
    /// </summary>
    public struct TwoAgoTransform : IComponentData
    {
        public TransformQvvs worldTransform;

        public float3 position => worldTransform.position;
        public quaternion rotation => worldTransform.rotation;
        public float scale => worldTransform.scale;
        public float3 stretch => worldTransform.stretch;
        public float3 nonUniformScale => scale * stretch;
        public int version => worldTransform.worldIndex;
        public bool isInitialized => version != 0;
    }

    /// <summary>
    /// The mode flags that the transforms use to update the hierarchy.
    /// When the hierarchy updates, each entity can choose to keep the local transform properties
    /// and update the world transform properties, or keep the world transform properties and update
    /// the local transform properties. The entity may also choose a mix of the two.
    /// If this component is not present, the hierarchy updates as if no flags are set.
    /// </summary>
    public struct HierarchyUpdateMode : IComponentData
    {
        public enum Flags : byte
        {
            /// <summary>
            /// If the enum is equal to this, then normal hierarchy update rules apply, and all values are affected by the parent.
            /// </summary>
            Normal = 0x0,
            /// <summary>
            /// When present, the world-space x-axis position is preserved. Otherwise, the value is affected by the parent.
            /// </summary>
            WorldX = 0x1,
            /// <summary>
            /// When present, the world-space y-axis position is preserved. Otherwise, the value is affected by the parent.
            /// </summary>
            WorldY = 0x2,
            /// <summary>
            /// When present, the world-space z-axis position is preserved. Otherwise, the value is affected by the parent.
            /// </summary>
            WorldZ = 0x4,
            /// <summary>
            /// When present, the world-space forward direction is preserved. Otherwise, the value is affected by the parent.
            /// </summary>
            WorldForward = 0x8,
            /// <summary>
            /// When present, the world-space up direction is preserved. Otherwise, the value is affected by the parent.
            /// </summary>
            WorldUp = 0x10,
            /// <summary>
            /// If only one of WorldForward or WorldUp is set, then if this flag is set, the up-direction is used and
            /// the forward-direction is approximated as best as possible. Otherwise, the forward-direction is used and
            /// the up-direction is approximated as best as possible.
            /// </summary>
            StrictUp = 0x20,
            /// <summary>
            /// When present, the world-space scale is preserved. Otherwise, the value is affected by the parent.
            /// </summary>
            WorldScale = 0x40,
            /// <summary>
            /// When present, the world-space position is preserved. Otherwise, the value is affected by the parent.
            /// </summary>
            WorldPosition = WorldX | WorldY | WorldZ,
            /// <summary>
            /// When present, the world-space orientation is preserved. Otherwise, the value is affected by the parent.
            /// </summary>
            WorldRotation = WorldForward | WorldUp,
            /// <summary>
            /// When present, the world-space transform is preserved and the local-space transform is updated.
            /// </summary>
            WorldAll = WorldPosition | WorldRotation | WorldScale,
        }

        public Flags modeFlags;
    }

    internal struct Depth : IComponentData
    {
        public byte depth;
    }

    internal struct ChunkDepthMask : IComponentData
    {
        public BitField32 chunkDepthMask;
    }

    internal struct RuntimeFeatureFlags : IComponentData
    {
        public enum Flags : byte
        {
            None = 0,
            ExtremeTransforms = 1
        }
        public Flags flags;
    }
}
#endif

