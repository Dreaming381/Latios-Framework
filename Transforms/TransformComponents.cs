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

    // Typically read-only by user code
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
    public struct LocalTransform : IComponentData
    {
        // Stretch comes from the WorldTransform and is not duplicated here so-as to improve chunk occupancy
        public TransformQvs localTransform;

        public float3 position => localTransform.position;
        public quaternion rotation => localTransform.rotation;
        public float scale => localTransform.scale;
    }

    // Can replace LocalTransform and ParentToWorldTransform to improve chunk occupancy if the entity copies the parent's transform exactly
    public struct CopyParentWorldTransformTag : IComponentData { }

    public struct Parent : IComponentData
    {
        public Entity parent;
    }
    // Usually doesn't need to be touched by user code
    public struct PreviousParent : ICleanupComponentData
    {
        public Entity previousParent;
    }

    [InternalBufferCapacity(0)]
    public struct Child : ICleanupBufferElementData
    {
        public EntityWith<Parent> child;
    }

    // Part of Motion History, used for motion vectors
    public struct TickStartingTransform : IComponentData
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

    // Part of Motion History, used for Inertial Blending
    public struct PreviousTickStartingTransform : IComponentData
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
}

