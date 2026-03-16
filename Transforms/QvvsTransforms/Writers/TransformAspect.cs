#if !LATIOS_TRANSFORMS_UNITY

using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Transforms
{
    [NativeContainer]
    public unsafe struct TransformAspect
    {
        internal RefRW<WorldTransform>   m_worldTransform;
        internal EntityInHierarchyHandle m_handle;
        internal void*                   m_access;
        internal EntityStorageInfoLookup m_esil;
        internal enum AccessType
        {
            EntityManager,
            ComponentBroker,
            ComponentBrokerKeyed,
            ComponentLookup
        }
        internal AccessType m_accessType;

        #region Read/Write Properties
        /// <summary>
        /// The world-space position of the entity that can be read or modified.
        /// If the entity has a parent, the localPosition and worldPosition are synchronized using the parent's WorldTransform.
        /// </summary>
        public float3 worldPosition
        {
            get => m_worldTransform.ValueRO.position;
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform.position = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetWorldPosition(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetWorldPosition(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetWorldPosition(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetWorldPosition(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The world-space rotation of the entity that can be read or modified.
        /// If the entity has a parent, the localRotation and worldRotation are synchronized using the parent's WorldTransform.
        /// </summary>
        public quaternion worldRotation
        {
            get => m_worldTransform.ValueRO.rotation;
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform.rotation = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetWorldRotation(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetWorldRotation(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetWorldRotation(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetWorldRotation(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The world-space scale of the entity that can be read or modified.
        /// If the entity has a parent, the localScale and worldScale are synchronized using the parent's WorldTransform.
        /// </summary>
        public float worldScale
        {
            get => m_worldTransform.ValueRO.scale;
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform.scale = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetWorldScale(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetWorldScale(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetWorldScale(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetWorldScale(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The local-space position of the entity that can be read or modified.
        /// If the entity has a parent, the localPosition and worldPosition are synchronized using the parent's WorldTransform.
        /// Otherwise, this reads/writes the worldPosition.
        /// When reading, float3.zero is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        public float3 localPosition
        {
            get => localTransform.position;
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform.position = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetLocalPosition(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetLocalPosition(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetLocalPosition(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetLocalPosition(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The local-space rotation of the entity that can be read or modified.
        /// If the entity has a parent, the localRotation and worldRotation are synchronized using the parent's WorldTransform.
        /// Otherwise, this reads/writes the worldRotation.
        /// When reading, quaternion.identity is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        public quaternion localRotation
        {
            get => localTransform.rotation;
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform.rotation = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetLocalRotation(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetLocalRotation(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetLocalRotation(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetLocalRotation(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The local-space scale of the entity that can be read or modified.
        /// If the entity has a parent, the localScale and worldScale are synchronized using the parent's WorldTransform.
        /// Otherwise, this reads/writes the worldScale.
        /// When reading, 1f is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        public float localScale
        {
            get => localTransform.scale;
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform.scale = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetLocalScale(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetLocalScale(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetLocalScale(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetLocalScale(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The stretch of the entity that can be read or modified.
        /// This value affects children's positions but nothing else.
        /// </summary>
        public float3 stretch
        {
            get => m_worldTransform.ValueRO.stretch;
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform.stretch = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetStretch(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetStretch(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetStretch(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetStretch(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The context32 of the entity that can be read or modified.
        /// It is a user value (do what you want with it) and not used directly by Latios Transforms (though other modules may support specific use cases).
        /// </summary>
        public int context32
        {
            get => m_worldTransform.ValueRO.context32;
            set => m_worldTransform.ValueRW.worldTransform.context32 = value;
        }

        /// <summary>
        /// The world-space QVVS transform of the entity that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// </summary>
        public TransformQvvs worldTransform
        {
            get => m_worldTransform.ValueRO.worldTransform;
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetWorldTransform(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetWorldTransform(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetWorldTransform(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetWorldTransform(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The local-space QVS transform of the entity that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// Otherwise, this reads/writes the position, rotation, and scale properties of the worldTransform.
        /// When reading, TransformQvs.identity is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        public TransformQvs localTransform
        {
            get
            {
                if (m_handle.isNull || m_handle.isRoot)
                {
                    var transform = m_worldTransform.ValueRO.worldTransform;
                    return new TransformQvs(transform.position, transform.rotation, transform.scale);
                }
                switch (m_accessType)
                {
                    case AccessType.EntityManager:
                        return TransformTools.Unsafe.LocalTransformFrom(m_handle, in m_worldTransform.ValueRO, *(EntityManager*)m_access, out _);
                    case AccessType.ComponentBroker:
                        return TransformTools.Unsafe.LocalTransformFrom(m_handle, in m_worldTransform.ValueRO, ref *(ComponentBroker*)m_access, out _);
                    case AccessType.ComponentBrokerKeyed:
                        var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                        return TransformTools.Unsafe.LocalTransformFrom(m_handle, in m_worldTransform.ValueRO, key, ref *(ComponentBroker*)m_access, out _);
                    case AccessType.ComponentLookup:
                        return TransformTools.Unsafe.LocalTransformFrom(m_handle, in m_worldTransform.ValueRO, m_esil, ref *(ComponentLookup<WorldTransform>*)m_access, out _);
                    default: return default;
                }
            }
            set
            {
                if (m_handle.isNull)
                {
                    ref var t  = ref m_worldTransform.ValueRW.worldTransform;
                    t.position = value.position;
                    t.rotation = value.rotation;
                    t.scale    = value.scale;
                }
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetLocalTransform(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetLocalTransform(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetLocalTransform(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetLocalTransform(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// The local-space transform in full QVVS representation that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// Otherwise, this reads/writes to the worldTransform.
        /// </summary>
        public TransformQvvs localTransformQvvs
        {
            get
            {
                if (m_handle.isNull || m_handle.isRoot)
                    return m_worldTransform.ValueRO.worldTransform;
                var local          = localTransform;
                var transform      = m_worldTransform.ValueRO.worldTransform;
                transform.position = local.position;
                transform.rotation = local.rotation;
                transform.scale    = local.scale;
                return transform;
            }
            set
            {
                if (m_handle.isNull)
                    m_worldTransform.ValueRW.worldTransform = value;
                else
                {
                    switch (m_accessType)
                    {
                        case AccessType.EntityManager:
                            TransformTools.SetLocalTransform(m_handle, value, *(EntityManager*)m_access);
                            break;
                        case AccessType.ComponentBroker:
                            TransformTools.SetLocalTransform(m_handle, value, ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentBrokerKeyed:
                            var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                            TransformTools.SetLocalTransform(m_handle, value, key,                                             ref *(ComponentBroker*)m_access);
                            break;
                        case AccessType.ComponentLookup:
                            TransformTools.SetLocalTransform(m_handle, value, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                            break;
                    }
                }
            }
        }
        #endregion

        #region ReadOnly Properties
        /// <summary>
        /// Retrieves the EntityInHierarchyHandle of this TransformAspect. Note that if the entity
        /// does not belong to a hierarchy, EntityInHierarchyHandle.isNull will be true.
        /// </summary>
        public EntityInHierarchyHandle entityInHierarchyHandle => m_handle;

        /// <summary>
        /// Retrieves the TransformAspect for the specified handle belonging to the same hierarchy
        /// as this TransformAspect. When safety checks exist, this method throws if the specifed
        /// handle comes from another hierarchy or if either its handle or this TransformAspect's
        /// handle is null.
        /// </summary>
        public TransformAspect this[EntityInHierarchyHandle otherHandle]
        {
            get
            {
                CheckBelongsToSameHierarchy(in otherHandle);
                var result      = this;
                result.m_handle = otherHandle;
                switch (m_accessType)
                {
                    case AccessType.EntityManager:
                        result.m_worldTransform = ((EntityManager*)m_access)->GetComponentDataRW<WorldTransform>(otherHandle.entity);
                        break;
                    case AccessType.ComponentBroker:
                        result.m_worldTransform = ((ComponentBroker*)m_access)->GetRW<WorldTransform>(otherHandle.entity);
                        break;
                    case AccessType.ComponentBrokerKeyed:
                        result.m_worldTransform = ((ComponentBroker*)m_access)->GetRWIgnoreParallelSafety<WorldTransform>(otherHandle.entity);
                        break;
                    case AccessType.ComponentLookup:
                        result.m_worldTransform = ((ComponentLookup<WorldTransform>*)m_access)->GetRefRW(otherHandle.entity);
                        break;
                    default:
                        result.m_worldTransform = default;
                        break;
                }
                CheckWorldTransformIsValid(in result.m_worldTransform);
                return result;
            }
        }
        /// <summary>
        /// True if the entity has a parent and not a CopyParent inheritance flag.
        /// </summary>
        public bool hasMutableLocalTransform => hasParent && m_handle.isCopyParent;
        /// <summary>
        /// True if the entity has a parent
        /// </summary>
        public bool hasParent => !m_handle.isNull && !m_handle.isRoot;

        /// <summary>
        /// The unit forward vector (local Z+) of the entity in world-space
        /// </summary>
        public float3 forwardDirection => math.rotate(worldRotation, new float3(0f, 0f, 1f));
        /// <summary>
        /// The unit backward vector (local Z-) of the entity in world-space
        /// </summary>
        public float3 backwardDirection => math.rotate(worldRotation, new float3(0f, 0f, -1f));
        /// <summary>
        /// The unit left vector (local X-) of the entity in world-space
        /// </summary>
        public float3 leftDirection => math.rotate(worldRotation, new float3(-1f, 0f, 0f));
        /// <summary>
        /// The unit right vector (local X+) of the entity in world-space
        /// </summary>
        public float3 rightDirection => math.rotate(worldRotation, new float3(1f, 0f, 0f));
        /// <summary>
        /// The unit up vector (local Y+) of the entity in world-space
        /// </summary>
        public float3 upDirection => math.rotate(worldRotation, new float3(0f, 1f, 0f));
        /// <summary>
        /// The unit down vector (local Y-) of the entity in world-space
        /// </summary>
        public float3 downDirection => math.rotate(worldRotation, new float3(0f, -1f, 0f));

        /// <summary>
        /// The matrix that represents the transformation of the entity from local-space to world-space including stretch.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 worldMatrix3x4 => m_worldTransform.ValueRO.worldTransform.ToMatrix3x4();
        /// <summary>
        /// The matrix that represents the transformation of the entity from local-space to world-space including stretch.
        /// </summary>
        public float4x4 worldMatrix4x4 => m_worldTransform.ValueRO.worldTransform.ToMatrix4x4();
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to local-space including stretch.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseWorldMatrix3x4 => m_worldTransform.ValueRO.worldTransform.ToInverseMatrix3x4();
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to local-space including stretch.
        /// </summary>
        public float4x4 inverseWorldMatrix4x4 => m_worldTransform.ValueRO.worldTransform.ToInverseMatrix4x4();
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to local-space ignoring stretch.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseWorldMatrix3x4IgnoreStretch => m_worldTransform.ValueRO.worldTransform.ToInverseMatrix3x4IgnoreStretch();
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to local-space ignoring stretch.
        /// </summary>
        public float4x4 inverseWorldMatrix4x4IgnoreStretch => m_worldTransform.ValueRO.worldTransform.ToInverseMatrix4x4IgnoreStretch();

        /// <summary>
        /// The matrix that represent's the entity's local transform relative to its parent, or relative to the world if it does not have a parent.
        /// Stretch is included.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 localMatrix3x4 => hasMutableLocalTransform? localTransform.ToMatrix3x4(stretch) :
            hasParent? float3x4.Scale(stretch) : worldMatrix3x4;
        /// <summary>
        /// The matrix that represent's the entity's local transform relative to its parent, or relative to the world if it does not have a parent.
        /// Stretch is included.
        /// </summary>
        public float4x4 localMatrix4x4 => hasMutableLocalTransform? localTransform.ToMatrix4x4(stretch) :
            hasParent? float4x4.Scale(stretch) : worldMatrix4x4;
        /// <summary>
        /// The inverse of localMatrix3x4, computed directly from the QVS or QVVS data. Stretch is included.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseLocalMatrix3x4 => hasMutableLocalTransform? localTransform.ToInverseMatrix3x4(stretch) :
            hasParent? float3x4.Scale(math.rcp(stretch)) : inverseWorldMatrix3x4;
        /// <summary>
        /// The inverse of localMatrix4x4, computed directly from the QVS or QVVS data. Stretch is included.
        /// </summary>
        public float4x4 inverseLocalMatrix4x4 => hasMutableLocalTransform? localTransform.ToInverseMatrix4x4(stretch) :
            hasParent? float4x4.Scale(math.rcp(stretch)) : inverseWorldMatrix4x4;
        /// <summary>
        /// The inverse of localMatrix3x4, computed directly from the QVS or QVVS data, except stretch is ignored.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseLocalMatrix3x4IgnoreStretch => hasMutableLocalTransform? localTransform.ToInverseMatrix3x4() :
            hasParent ? float3x4.identity : inverseWorldMatrix3x4IgnoreStretch;
        /// <summary>
        /// The inverse of localMatrix4x4, computed directly from the QVS or QVVS data, except stretch is ignored.
        /// </summary>
        public float4x4 inverseLocalMatrix4x4IgnoreStretch => hasMutableLocalTransform? localTransform.ToInverseMatrix4x4() :
            hasParent ? float4x4.identity : inverseWorldMatrix4x4IgnoreStretch;
        #endregion

        #region Modification Methods
        /// <summary>
        /// Moves the entity by the amount specified in translation along the world-space axes.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// </summary>
        /// <param name="translation">The world-space x, y, and z signed amounts to move the entity</param>
        public void TranslateWorld(float3 translation) => worldPosition += translation;
        /// <summary>
        /// Moves the entity by the amount specified in x, y, and z along the world-space axes.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// </summary>
        /// <param name="x">The signed amount to move the entity along the positive x axis</param>
        /// <param name="y">The signed amount to move the entity along the positive y axis</param>
        /// <param name="z">The signed amount to move the entity along the positive z axis</param>
        public void TranslateWorld(float x, float y, float z) => TranslateWorld(new float3(x, y, z));
        /// <summary>
        /// Moves the entity by the amount specified in translation along the Entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// If the entity does not have a parent, this is equivalent to TranslateWorld().
        /// </summary>
        /// <param name="translation">The local-space x, y, and z signed amounts to move the entity</param>
        public void TranslateLocal(float3 translation)
        {
            if (m_handle.isNull)
                m_worldTransform.ValueRW.worldTransform.position += translation;
            else
            {
                switch (m_accessType)
                {
                    case AccessType.EntityManager:
                        TransformTools.TranslateLocal(m_handle, translation, *(EntityManager*)m_access);
                        break;
                    case AccessType.ComponentBroker:
                        TransformTools.TranslateLocal(m_handle, translation, ref *(ComponentBroker*)m_access);
                        break;
                    case AccessType.ComponentBrokerKeyed:
                        var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                        TransformTools.TranslateLocal(m_handle, translation, key,                                             ref *(ComponentBroker*)m_access);
                        break;
                    case AccessType.ComponentLookup:
                        TransformTools.TranslateLocal(m_handle, translation, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                        break;
                }
            }
        }
        /// <summary>
        /// Moves the entity by the amount specified in x, y, and z along the Entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// If the entity does not have a parent, this is equivalent to TranslateWorld().
        /// </summary>
        /// <param name="x">The signed amount to move the entity along the positive x axis</param>
        /// <param name="y">The signed amount to move the entity along the positive y axis</param>
        /// <param name="z">The signed amount to move the entity along the positive z axis</param>
        public void TranslateLocal(float x, float y, float z) => TranslateLocal(new float3(x, y, z));

        /// <summary>
        /// Rotates the entity's orientation by the specified rotation, where the axis of rotation is defined in world-space.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// </summary>
        /// <param name="rotation">The amount to rotate by, where the innate axis of rotation of the quaternion is specified relative to world-space.</param>
        public void RotateWorld(quaternion rotation) => worldRotation = math.normalize(math.mul(rotation, worldRotation));
        /// <summary>
        /// Without changing its position or rotation, pretends as if the entity is parented to a pivot object at the location specified by pivot in world-space.
        /// Then, the entity is rotated and repositioned by rotating the pivot object by the specified rotation in world-space.
        /// If the entity has a real parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// </summary>
        /// <param name="rotation">The amount to rotate the pivot by, where the innate axis of the rotation of the quaternion is specified relative to world-space.</param>
        /// <param name="pivot">A world space position from which the entity is moved and rotated around in a circular arc.</param>
        public void RotateWorldAbout(quaternion rotation, float3 pivot)
        {
            var transform           = worldTransform;
            var oldPosition         = transform.position;
            var pivotToOldPosition  = oldPosition - pivot;
            var pivotToNewPosition  = math.rotate(rotation, pivotToOldPosition);
            transform.rotation      = math.normalize(math.mul(rotation, transform.rotation));
            transform.position     += pivotToNewPosition - pivotToOldPosition;
            worldTransform          = transform;
        }
        /// <summary>
        /// Rotates the entity's orientation by the specified rotation, where the axis of rotation is defined in the entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// If the entity does not have a parent, this is equivalent to RotateWorld().
        /// </summary>
        /// <param name="rotation">The amount to rotate by, where the innate axis of rotation of the quaternion is specified in the entity's local space relative to its parent.</param>
        public void RotateLocal(quaternion rotation)
        {
            if (m_handle.isNull)
            {
                ref var dst = ref m_worldTransform.ValueRW.worldTransform.rotation;
                dst         = math.normalize(math.mul(rotation, dst));
            }
            else
            {
                switch (m_accessType)
                {
                    case AccessType.EntityManager:
                        TransformTools.RotateLocal(m_handle, rotation, *(EntityManager*)m_access);
                        break;
                    case AccessType.ComponentBroker:
                        TransformTools.RotateLocal(m_handle, rotation, ref *(ComponentBroker*)m_access);
                        break;
                    case AccessType.ComponentBrokerKeyed:
                        var key = TransformsKey.CreateFromExclusivelyAccessedRoot(m_handle.root.entity, m_esil);
                        TransformTools.RotateLocal(m_handle, rotation, key,                                             ref *(ComponentBroker*)m_access);
                        break;
                    case AccessType.ComponentLookup:
                        TransformTools.RotateLocal(m_handle, rotation, ref *(ComponentLookup<WorldTransform>*)m_access, ref m_esil);
                        break;
                }
            }
        }
        /// <summary>
        /// Without changing its position or rotation, pretends as if the entity is parented to a pivot object at the location specified by pivot
        /// in the entity's local-space axes relative to its parent. Then, the entity is rotated and repositioned by rotating the pivot object
        /// by the specified rotation in the entity's local-space axes relative to its parent.
        /// If the entity has a real parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// If the entity does not have a parent, this is equivalent to RotateWorldAbout().
        /// </summary>
        /// <param name="rotation">The amount to rotate the pivot by, where the innate axis of the rotation of the quaternion is specified in the entity's local space relative to its parent.</param>
        /// <param name="pivot">A local-space position relative to the entity's parent from which the entity is moved and rotated around in a circular arc.</param>
        public void RotateLocalAbout(quaternion rotation, float3 pivot)
        {
            var local               = localTransform;
            var oldPosition         = local.position;
            var pivotToOldPosition  = oldPosition - pivot;
            var pivotToNewPosition  = math.rotate(rotation, pivotToOldPosition);
            local.rotation          = math.normalize(math.mul(rotation, local.rotation));
            local.position         += pivotToNewPosition - pivotToOldPosition;
            localTransform          = local;
        }

        /// <summary>
        /// Computes the rotation so that the forward vector points to the target.
        /// The up vector is assumed to be world up.
        ///</summary>
        /// <param name="targetPosition">The world space point to look at</param>
        public void LookAt(float3 targetPosition)
        {
            LookAt(targetPosition, math.up());
        }

        /// <summary>
        /// Computes the rotation so that the forward vector points to the target.
        /// This version takes an up vector.
        ///</summary>
        /// <param name="targetPosition">The world space point to look at</param>
        /// <param name="up">The up vector</param>
        public void LookAt(float3 targetPosition, float3 up)
        {
            var targetDir = targetPosition - worldPosition;
            worldRotation = quaternion.LookRotationSafe(targetDir, up);
        }
        #endregion

        #region ReadOnly Transformation Methods
        /// <summary>Transform a point from local space into world space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointLocalToWorld(float3 point)
        {
            return qvvs.TransformPoint(worldTransform, point);
        }

        /// <summary>Transform a point from world space into local space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointWorldToLocal(float3 point)
        {
            return qvvs.InverseTransformPoint(worldTransform, point);
        }

        /// <summary>Transforms a direction vector from local space into world space, ignoring the effects of stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionLocalToWorld(float3 direction)
        {
            return qvvs.TransformDirection(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from local space into world space, including directional changes caused by stretch while preserving magnitude.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionLocalToWorldWithStretch(float3 direction)
        {
            return qvvs.TransformDirectionWithStretch(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from local space into world space, including directional and magnitude changes caused by scale and stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionLocalToWorldScaledAndStretched(float3 direction)
        {
            return qvvs.TransformDirectionScaledAndStretched(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into local space, ignoring the effects of stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToLocal(float3 direction)
        {
            return qvvs.TransformDirection(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into local space, including directional changes caused by stretch while preserving magnitude.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToLocalWithStretch(float3 direction)
        {
            return qvvs.TransformDirectionWithStretch(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into local space, including directional and magnitude changes caused by scale and stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToLocalScaledAndStretched(float3 direction)
        {
            return qvvs.TransformDirectionScaledAndStretched(worldTransform, direction);
        }
        #endregion

        #region Safety
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckBelongsToSameHierarchy(in EntityInHierarchyHandle otherHandle)
        {
            if (m_handle.isNull || otherHandle.isNull || m_handle.m_hierarchy != otherHandle.m_hierarchy)
                throw new System.ArgumentException("The EntityInHierarchyHandle does not belong to the same hierarchy as this TransformAspect.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckWorldTransformIsValid(in RefRW<WorldTransform> transform)
        {
            if (!transform.IsValid)
                throw new System.ArgumentException("The Entity did not have a WorldTransform, either because it is ticking only or because it is no longer alive.");
        }
        #endregion
    }
}
#endif

