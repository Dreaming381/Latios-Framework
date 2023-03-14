using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    // World space transform for all entities. Is also local space for root entities. It is always present, and is the only required transform component for rendering.
    public struct WorldTransform : IComponentData
    {
        public TransformQvvs worldTransform;

        public float3 position => worldTransform.position;
        public quaternion rotation => worldTransform.rotation;
        public float scale => worldTransform.scale;
        public float3 stretch => worldTransform.stretch;
        public float3 nonUniformScale => scale * stretch;
        public int worldIndex => worldTransform.worldIndex;
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

