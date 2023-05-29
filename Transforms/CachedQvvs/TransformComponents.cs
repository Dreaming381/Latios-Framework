#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
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
    public struct WorldTransform : IComponentData
    {
        /// <summary>
        /// The actual TransformQvvs representing the world-space transform of the entity.
        /// Directly writing to this value is heavily discouraged.
        /// </summary>
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
    /// A tag that specifies the parent's WorldTransform should be copied onto this Entity's WorldTransform exactly.
    /// With this component, an Entity does not need LocalTransform nor ParentToWorldTransform, saving memory.
    /// </summary>
    public struct CopyParentWorldTransformTag : IComponentData { }

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

