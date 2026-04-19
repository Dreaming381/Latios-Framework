#if !LATIOS_TRANSFORMS_UNITY
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Properties;

namespace Latios.Transforms
{
    #region Standard Components
    /// <summary>
    /// The world-space transform for all entities. It is also the local-space transform for root entities.
    /// This component is always present regardless of role in the hierarchy, and the only required
    /// transform component for rendering with Kinemation.
    /// Usage: ReadOnly
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
        /// The context32 whose purpose is up to the user
        /// </summary>
        public int context32 => worldTransform.context32;

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
    }

    /// <summary>
    /// A reference to the root of the hierarchy. If this component is present, then this entity belongs to a hierarchy but is not the root.
    /// </summary>
    public struct RootReference : IComponentData
    {
        internal EntityWithBuffer<EntityInHierarchy> m_rootEntity;
        internal int                                 m_indexInHierarchy;

        [CreateProperty]
        public Entity rootEntity => m_rootEntity;
        [CreateProperty]
        public int indexInHierarchy => m_indexInHierarchy;
    }

    /// <summary>
    /// An element in a transform hierarchy. This buffer belongs to the root of the hierarchy.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct EntityInHierarchy : IBufferElementData
    {
        internal EntityWith<RootReference> m_descendantEntity;
        internal int                       m_parentIndex;
        internal int                       m_firstChildIndex;
        internal int                       m_childCount;
        internal InheritanceFlags          m_flags;
        internal float3                    m_localPosition;
        internal float                     m_localScale;
        internal float3                    m_tickedLocalPosition;
        internal float                     m_tickedLocalScale;

        [CreateProperty] public Entity entity => m_descendantEntity;
        [CreateProperty] public int parentIndex => m_parentIndex;
        [CreateProperty] public int childCount => m_childCount;
        [CreateProperty] public int firstChildIndex => m_firstChildIndex;
        [CreateProperty] public float4 embeddedLocalHint => new float4(m_localPosition, m_localScale);
    }

    /// <summary>
    /// A copy of EntityInHierarchy, in case the root is destroyed.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct EntityInHierarchyCleanup : ICleanupBufferElementData
    {
        public EntityInHierarchy entityInHierarchy;
    }
    #endregion

    #region Ticked Components
    /// <summary>
    /// The ticked world-space transform for all entities. It is also the local-space transform for root entities.
    /// This component is always present regardless of role in the hierarchy.
    /// Usage: ReadOnly.
    /// </summary>
#if UNITY_NETCODE
    [StructLayout(LayoutKind.Explicit)]
#endif
    public struct TickedWorldTransform : IComponentData
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
        /// The context32 whose purpose is up to the user
        /// </summary>
        public int worldIndex => worldTransform.context32;

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
    /// The TickedWorldTransform from the previous tick. This may be read for gameplay purposes.
    /// </summary>
    public struct TickedPreviousTransform : IComponentData
    {
        public TransformQvvs worldTransform;

        public float3 position => worldTransform.position;
        public quaternion rotation => worldTransform.rotation;
        public float scale => worldTransform.scale;
        public float3 stretch => worldTransform.stretch;
        public float3 nonUniformScale => scale * stretch;
    }

    /// <summary>
    /// The TickedWorldTransform from two ticks ago. This may be read for gameplay purposes.
    /// </summary>
    public struct TickedTwoAgoTransform : IComponentData
    {
        public TransformQvvs worldTransform;

        public float3 position => worldTransform.position;
        public quaternion rotation => worldTransform.rotation;
        public float scale => worldTransform.scale;
        public float3 stretch => worldTransform.stretch;
        public float3 nonUniformScale => scale * stretch;
    }
    #endregion

    #region Flags
    /// <summary>
    /// The mode flags that the transforms use to update the hierarchy.
    /// When the hierarchy updates, each entity can choose to keep the local transform properties
    /// and update the world transform properties, or keep the world transform properties and update
    /// the local transform properties. The entity may also choose a mix of the two.
    /// </summary>
    public enum InheritanceFlags : byte
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
        /// When present, the entire WorldTransform is copied as-is from the parent. Other flags are ignored.
        /// </summary>
        CopyParent = 0x80,
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

    public static class InheritanceFlagsExtensions
    {
        public static bool HasCopyParent(this InheritanceFlags flags) => (flags & InheritanceFlags.CopyParent) == InheritanceFlags.CopyParent;
    }
    #endregion

    #region Queries
    public static class TransformQueryExtensions
    {
        /// <summary>
        /// Ensures only entities that do not have a parent are included in the query
        /// </summary>
        public static FluentQuery RootsOnly(this FluentQuery query) => query.Without<RootReference>();
        /// <summary>
        /// Ensures only alive entities that do not have a parent but do have children are included in the query
        /// </summary>
        public static FluentQuery AliveRootsWithBloodChildren(this FluentQuery query) => query.With<EntityInHierarchy>();
        /// <summary>
        /// Ensures only dead root entities pending alive blood descendants are included in the query
        /// </summary>
        public static FluentQuery DeadRootsWithBloodChildren(this FluentQuery query) => query.With<EntityInHierarchyCleanup>().Without<EntityInHierarchy>();
        /// <summary>
        /// Ensures only entities which have blood parents are included in the query
        /// </summary>
        public static FluentQuery WithBloodParent(this FluentQuery query) => query.With<RootReference>();
        /// <summary>
        /// Ensures only entities that do not have parents nor children are included in the query
        /// </summary>
        public static FluentQuery WithoutHierarchy(this FluentQuery query) => query.Without<RootReference, EntityInHierarchy, EntityInHierarchyCleanup>();
    }
    #endregion

    #region Live Baking
    internal struct LiveAddedParentTag : IComponentData { }

    internal struct LiveRemovedParentTag : IComponentData { }

    internal unsafe partial struct LiveTransformCapture : ICollectionComponent
    {
        public struct Root
        {
            public Entity                                                 entity;
            [NativeDisableUnsafePtrRestriction] public EntityInHierarchy* hierarchyStart;
            public int                                                    hierarchyCount;
            public bool                                                   runtimeRemovedParent;
            public bool                                                   hasWorldTransform;
            public bool                                                   hasTickedWorldTransform;
            public TransformQvvs                                          worldTransform;
            public TransformQvvs                                          tickedWorldTransform;
        }

        public struct Child
        {
            public Entity        entity;
            public Entity        parent;
            public int           siblingIndex;
            public RootReference rootRef;
            public bool          parentIsBlood;
            public bool          runtimeAddedParent;
            public bool          hasWorldTransform;
            public bool          hasTickedWorldTransform;
            public TransformQvs  localTransform;
            public TransformQvvs worldTransform;
            public TransformQvs  tickedLocalTransform;
            public TransformQvvs tickedWorldTransform;
        }

        public NativeArray<Root>  roots;
        public NativeArray<Child> children;
        public uint               changeVersion;
        public int                rootsOrderVersion;
        public int                childrenOrderVersion;
        public int                worldTransformOrderVersion;
        public int                tickedWorldTransformOrderVersion;
        public bool               cleanEditorWorld;

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            return inputDeps;  // We use WorldUpdateAllocator
        }
    }
    #endregion
}
#endif

