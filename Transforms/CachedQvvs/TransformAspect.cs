#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY

using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;

namespace Latios.Transforms
{
    public readonly partial struct TransformAspect : IAspect
    {
        readonly RefRW<WorldTransform>                         m_worldTransform;
        [Optional] readonly RefRW<LocalTransform>              m_localTransform;
        [Optional] readonly RefRO<ParentToWorldTransform>      m_parentToWorldTransform;
        [Optional] readonly RefRO<CopyParentWorldTransformTag> m_copyParentWorldTransformTag;

        #region Read/Write Properties
        /// <summary>
        /// The world-space position of the entity that can be read or modified.
        /// If the entity has a parent, the localPosition and worldPosition are synchronized using the cached ParentToWorldTransform.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Throws when writing if the entity has an CopyParentWorldTransformTag</exception>
        [CreateProperty]
        public float3 worldPosition
        {
            get => m_worldTransform.ValueRO.position;
            set
            {
                if (Hint.Unlikely(hasCopyParentWorldTransformTag))
                    ThrowOnWriteToCopyParentWorldTransformTag();

                m_worldTransform.ValueRW.worldTransform.position = value;
                if (hasMutableLocalToParent)
                    m_localTransform.ValueRW.localTransform.position = qvvs.InverseTransformPoint(in parentToWorldInternal, value);
            }
        }

        /// <summary>
        /// The world-space rotation of the entity that can be read or modified.
        /// If the entity has a parent, the localRotation and worldRotation are synchronized using the cached ParentToWorldTransform.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Throws when writing if the entity has an CopyParentWorldTransformTag</exception>
        [CreateProperty]
        public quaternion worldRotation
        {
            get => m_worldTransform.ValueRO.rotation;
            set
            {
                if (Hint.Unlikely(hasCopyParentWorldTransformTag))
                    ThrowOnWriteToCopyParentWorldTransformTag();

                m_worldTransform.ValueRW.worldTransform.rotation = value;
                if (hasMutableLocalToParent)
                    m_localTransform.ValueRW.localTransform.rotation = math.InverseRotateFast(parentToWorldInternal.rotation, value);
            }
        }

        /// <summary>
        /// The world-space scale of the entity that can be read or modified.
        /// If the entity has a parent, the localScale and worldScale are synchronized using the cached ParentToWorldTransform.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Throws when writing if the entity has an CopyParentWorldTransformTag</exception>
        [CreateProperty]
        public float worldScale
        {
            get => m_worldTransform.ValueRO.scale;
            set
            {
                if (Hint.Unlikely(hasCopyParentWorldTransformTag))
                    ThrowOnWriteToCopyParentWorldTransformTag();

                m_worldTransform.ValueRW.worldTransform.scale = value;
                if (hasMutableLocalToParent)
                    m_localTransform.ValueRW.localTransform.scale = value / parentToWorldInternal.scale;
            }
        }

        /// <summary>
        /// The local-space position of the entity that can be read or modified.
        /// If the entity has a parent, the localPosition and worldPosition are synchronized using the cached ParentToWorldTransform.
        /// Otherwise, this reads/writes the worldPosition.
        /// When reading, float3.zero is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Throws when writing if the entity has an CopyParentWorldTransformTag</exception>
        [CreateProperty]
        public float3 localPosition
        {
            get => hasMutableLocalToParent ? m_localTransform.ValueRO.position : hasCopyParentWorldTransformTag ? 0f : m_worldTransform.ValueRO.position;
            set
            {
                if (hasMutableLocalToParent)
                {
                    m_localTransform.ValueRW.localTransform.position = value;
                    m_worldTransform.ValueRW.worldTransform.position = qvvs.TransformPoint(in parentToWorldInternal, value);
                }
                else if (Hint.Unlikely(hasCopyParentWorldTransformTag))
                    ThrowOnWriteToCopyParentWorldTransformTag();
                else
                    m_worldTransform.ValueRW.worldTransform.position = value;
            }
        }

        /// <summary>
        /// The local-space rotation of the entity that can be read or modified.
        /// If the entity has a parent, the localRotation and worldRotation are synchronized using the cached ParentToWorldTransform.
        /// Otherwise, this reads/writes the worldRotation.
        /// When reading, quaternion.identity is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Throws when writing if the entity has an CopyParentWorldTransformTag</exception>
        [CreateProperty]
        public quaternion localRotation
        {
            get => hasMutableLocalToParent ? m_localTransform.ValueRO.rotation : hasCopyParentWorldTransformTag ? quaternion.identity : m_worldTransform.ValueRO.rotation;
            set
            {
                if (hasMutableLocalToParent)
                {
                    m_localTransform.ValueRW.localTransform.rotation = value;
                    m_worldTransform.ValueRW.worldTransform.rotation = math.mul(parentToWorldInternal.rotation, value);
                }
                else if (Hint.Unlikely(hasCopyParentWorldTransformTag))
                    ThrowOnWriteToCopyParentWorldTransformTag();
                else
                    m_worldTransform.ValueRW.worldTransform.rotation = value;
            }
        }

        /// <summary>
        /// The local-space scale of the entity that can be read or modified.
        /// If the entity has a parent, the localScale and worldScale are synchronized using the cached ParentToWorldTransform.
        /// Otherwise, this reads/writes the worldScale.
        /// When reading, 1f is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Throws when writing if the entity has an CopyParentWorldTransformTag</exception>
        [CreateProperty]
        public float localScale
        {
            get => hasMutableLocalToParent ? m_localTransform.ValueRO.scale : hasCopyParentWorldTransformTag ? 1f : m_worldTransform.ValueRO.scale;
            set
            {
                if (hasMutableLocalToParent)
                {
                    m_localTransform.ValueRW.localTransform.scale = value;
                    m_worldTransform.ValueRW.worldTransform.scale = parentToWorldInternal.scale * value;
                }
                else if (Hint.Unlikely(hasCopyParentWorldTransformTag))
                    ThrowOnWriteToCopyParentWorldTransformTag();
                else
                    m_worldTransform.ValueRW.worldTransform.scale = value;
            }
        }

        /// <summary>
        /// The stretch of the entity that can be read or modified.
        /// This value affects children's positions but nothing else.
        /// </summary>
        [CreateProperty]
        public float3 stretch
        {
            get => m_worldTransform.ValueRO.stretch;
            set => m_worldTransform.ValueRW.worldTransform.stretch = value;
        }

        /// <summary>
        /// The worldIndex of the entity that can be read or modified.
        /// It is a user value (do what you want with it) and not used directly by Latios Transforms (though other modules may support specific use cases).
        /// </summary>
        [CreateProperty]
        public int worldIndex
        {
            get => m_worldTransform.ValueRO.worldIndex;
            set => m_worldTransform.ValueRW.worldTransform.worldIndex = value;
        }

        /// <summary>
        /// The world-space QVVS transform of the entity that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Throws when writing if the entity has an CopyParentWorldTransformTag</exception>
        public TransformQvvs worldTransform
        {
            get => m_worldTransform.ValueRO.worldTransform;
            set
            {
                m_worldTransform.ValueRW.worldTransform = value;
                if (hasMutableLocalToParent)
                    m_localTransform.ValueRW.localTransform = qvvs.inversemul(in parentToWorldInternal, value);
            }
        }

        /// <summary>
        /// The local-space QVS transform of the entity that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// Otherwise, this reads/writes the position, rotation, and scale properties of the worldTransform.
        /// When reading, TransformQvs.identity is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Throws when writing if the entity has an CopyParentWorldTransformTag</exception>
        public TransformQvs localTransform
        {
            get => hasMutableLocalToParent ? m_localTransform.ValueRO.localTransform :
            hasCopyParentWorldTransformTag ? TransformQvs.identity : QvsFromQvvs(m_worldTransform.ValueRO.worldTransform);
            set
            {
                if (hasMutableLocalToParent)
                {
                    m_localTransform.ValueRW.localTransform = value;
                    qvvs.mul(ref m_worldTransform.ValueRW.worldTransform, in parentToWorldInternal, value);
                }
                else if (Hint.Unlikely(hasCopyParentWorldTransformTag))
                    ThrowOnWriteToCopyParentWorldTransformTag();
                else
                {
                    ref var t  = ref m_worldTransform.ValueRW.worldTransform;
                    t.position = value.position;
                    t.rotation = value.rotation;
                    t.scale    = value.scale;
                }
            }
        }

        /// <summary>
        /// The local-space transform in full QVVS representation that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// Otherwise, this reads/writes to the worldTransform.
        /// </summary>
        public TransformQvvs localTransformQvvs
        {
            get
            {
                if (hasMutableLocalToParent)
                {
                    ref readonly var local = ref m_localTransform.ValueRO;
                    return new TransformQvvs
                    {
                        rotation   = local.rotation,
                        position   = local.position,
                        worldIndex = worldIndex,
                        stretch    = stretch,
                        scale      = local.scale
                    };
                }
                if (hasCopyParentWorldTransformTag)
                {
                    return new TransformQvvs
                    {
                        rotation   = quaternion.identity,
                        position   = 0f,
                        worldIndex = worldIndex,
                        stretch    = stretch,
                        scale      = 1f
                    };
                }
                return m_worldTransform.ValueRO.worldTransform;
            }
            set
            {
                if (hasMutableLocalToParent)
                {
                    stretch    = value.stretch;
                    worldIndex = value.worldIndex;
                    var local  = new TransformQvs
                    {
                        rotation = value.rotation,
                        position = value.position,
                        scale    = value.scale
                    };
                    m_localTransform.ValueRW.localTransform = local;
                    qvvs.mul(ref m_worldTransform.ValueRW.worldTransform, in parentToWorldInternal, local);
                }
                else if (Hint.Unlikely(hasCopyParentWorldTransformTag))
                    ThrowOnWriteToCopyParentWorldTransformTag();
                else
                {
                    m_worldTransform.ValueRW.worldTransform = value;
                }
            }
        }
        #endregion

        #region ReadOnly Properties
        /// <summary>
        /// The cached ParentToWorldTransform, or identity if the entity does not have a parent
        /// </summary>
        public TransformQvvs parentToWorldTransform => hasMutableLocalToParent ? m_parentToWorldTransform.ValueRO.parentToWorldTransform : qvvs.IdentityWithWorldIndex(worldIndex);

        /// <summary>
        /// True if the entity has a parent and not an CopyParentWorldTransformTag.
        /// </summary>
        public bool hasMutableLocalToParent => m_parentToWorldTransform.IsValid;  // && m_localTransform.IsValid; The latter should be guaranteed.
        /// <summary>
        /// True if the entity has an CopyParentWorldTransformTag, which effectively makes this entity's transforms read-only
        /// </summary>
        public bool hasCopyParentWorldTransformTag => m_copyParentWorldTransformTag.IsValid;
        /// <summary>
        /// True if the entity has a parent
        /// </summary>
        public bool hasLocalToParentOrCopyParent => hasMutableLocalToParent || hasCopyParentWorldTransformTag;

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
        /// The matrix that represents the transformation of the entity from parent-space to world-space including the parent's stretch.
        /// If present, the cached ParentToWorldTransform is used when generating this matrix. Otherwise, WorldTransform is used.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 parentToWorldMatrix3x4 => hasMutableLocalToParent? parentToWorldInternal.ToMatrix3x4() : m_worldTransform.ValueRO.worldTransform.ToMatrix3x4();
        /// <summary>
        /// The matrix that represents the transformation of the entity from parent-space to world-space including the parent's stretch.
        /// If present, the cached ParentToWorldTransform is used when generating this matrix. Otherwise, WorldTransform is used.
        /// </summary>
        public float4x4 parentToWorldMatrix4x4 => hasMutableLocalToParent? parentToWorldInternal.ToMatrix4x4() : m_worldTransform.ValueRO.worldTransform.ToMatrix4x4();
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to parent-space including the parent's stretch.
        /// If present, the cached ParentToWorldTransform is used when generating this matrix. Otherwise, WorldTransform is used.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseParentToWorldMatrix3x4 => hasMutableLocalToParent? parentToWorldInternal.ToInverseMatrix3x4() : m_worldTransform.ValueRO.worldTransform.
            ToInverseMatrix3x4();
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to parent-space including the parent's stretch.
        /// If present, the cached ParentToWorldTransform is used when generating this matrix. Otherwise, WorldTransform is used.
        /// </summary>
        public float4x4 inverseParentToWorldMatrix4x4 => hasMutableLocalToParent? parentToWorldInternal.ToInverseMatrix4x4() : m_worldTransform.ValueRO.worldTransform.
            ToInverseMatrix4x4();
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to parent-space ignoring stretch.
        /// If present, the cached ParentToWorldTransform is used when generating this matrix. Otherwise, WorldTransform is used.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseParentToWorldMatrix3x4IgnoreStretch => hasMutableLocalToParent? parentToWorldInternal.ToInverseMatrix3x4IgnoreStretch() : m_worldTransform.ValueRO.
            worldTransform.ToInverseMatrix3x4IgnoreStretch();
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to parent-space ignoring stretch.
        /// If present, the cached ParentToWorldTransform is used when generating this matrix. Otherwise, WorldTransform is used.
        /// </summary>
        public float4x4 inverseParentToWorldMatrix4x4IgnoreStretch => hasMutableLocalToParent? parentToWorldInternal.ToInverseMatrix4x4IgnoreStretch() : m_worldTransform.ValueRO.
            worldTransform.ToInverseMatrix4x4IgnoreStretch();

        /// <summary>
        /// The matrix that represent's the entity's local transform relative to its parent, or relative to the world if it does not have a parent.
        /// Stretch is included.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 localMatrix3x4 => hasMutableLocalToParent? m_localTransform.ValueRO.localTransform.ToMatrix3x4(stretch) :
            hasCopyParentWorldTransformTag? float3x4.Scale(stretch) : worldMatrix3x4;
        /// <summary>
        /// The matrix that represent's the entity's local transform relative to its parent, or relative to the world if it does not have a parent.
        /// Stretch is included.
        /// </summary>
        public float4x4 localMatrix4x4 => hasMutableLocalToParent? m_localTransform.ValueRO.localTransform.ToMatrix4x4(stretch) :
            hasCopyParentWorldTransformTag? float4x4.Scale(stretch) : worldMatrix4x4;
        /// <summary>
        /// The inverse of localMatrix3x4, computed directly from the QVS or QVVS data. Stretch is included.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseLocalMatrix3x4 => hasMutableLocalToParent? m_localTransform.ValueRO.localTransform.ToInverseMatrix3x4(stretch) :
            hasCopyParentWorldTransformTag? float3x4.Scale(math.rcp(stretch)) : inverseWorldMatrix3x4;
        /// <summary>
        /// The inverse of localMatrix4x4, computed directly from the QVS or QVVS data. Stretch is included.
        /// </summary>
        public float4x4 inverseLocalMatrix4x4 => hasMutableLocalToParent? m_localTransform.ValueRO.localTransform.ToInverseMatrix4x4(stretch) :
            hasCopyParentWorldTransformTag? float4x4.Scale(math.rcp(stretch)) : inverseWorldMatrix4x4;
        /// <summary>
        /// The inverse of localMatrix3x4, computed directly from the QVS or QVVS data, except stretch is ignored.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseLocalMatrix3x4IgnoreStretch => hasMutableLocalToParent? m_localTransform.ValueRO.localTransform.ToInverseMatrix3x4() :
            hasCopyParentWorldTransformTag ? float3x4.identity : inverseWorldMatrix3x4IgnoreStretch;
        /// <summary>
        /// The inverse of localMatrix4x4, computed directly from the QVS or QVVS data, except stretch is ignored.
        /// </summary>
        public float4x4 inverseLocalMatrix4x4IgnoreStretch => hasMutableLocalToParent? m_localTransform.ValueRO.localTransform.ToInverseMatrix4x4() :
            hasCopyParentWorldTransformTag ? float4x4.identity : inverseWorldMatrix4x4IgnoreStretch;
        #endregion

        #region Modification Methods
        /// <summary>
        /// Moves the entity by the amount specified in translation along the world-space axes.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// </summary>
        /// <param name="translation">The world-space x, y, and z signed amounts to move the entity</param>
        public void TranslateWorld(float3 translation) => worldPosition += translation;
        /// <summary>
        /// Moves the entity by the amount specified in x, y, and z along the world-space axes.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// </summary>
        /// <param name="x">The signed amount to move the entity along the positive x axis</param>
        /// <param name="y">The signed amount to move the entity along the positive y axis</param>
        /// <param name="z">The signed amount to move the entity along the positive z axis</param>
        public void TranslateWorld(float x, float y, float z) => TranslateWorld(new float3(x, y, z));
        /// <summary>
        /// Moves the entity by the amount specified in translation along the Entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// If the entity does not have a parent, this is equivalent to TranslateWorld().
        /// </summary>
        /// <param name="translation">The local-space x, y, and z signed amounts to move the entity</param>
        public void TranslateLocal(float3 translation) => localPosition += translation;
        /// <summary>
        /// Moves the entity by the amount specified in x, y, and z along the Entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// If the entity does not have a parent, this is equivalent to TranslateWorld().
        /// </summary>
        /// <param name="x">The signed amount to move the entity along the positive x axis</param>
        /// <param name="y">The signed amount to move the entity along the positive y axis</param>
        /// <param name="z">The signed amount to move the entity along the positive z axis</param>
        public void TranslateLocal(float x, float y, float z) => TranslateLocal(new float3(x, y, z));

        /// <summary>
        /// Rotates the entity's orientation by the specified rotation, where the axis of rotation is defined in world-space.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// </summary>
        /// <param name="rotation">The amount to rotate by, where the innate axis of rotation of the quaternion is specified relative to world-space.</param>
        public void RotateWorld(quaternion rotation) => worldRotation = math.mul(rotation, worldRotation);
        /// <summary>
        /// Without changing its position or rotation, pretends as if the entity is parented to a pivot object at the location specified by pivot in world-space.
        /// Then, the entity is rotated and repositioned by rotating the pivot object by the specified rotation in world-space.
        /// If the entity has a real parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// </summary>
        /// <param name="rotation">The amount to rotate the pivot by, where the innate axis of the rotation of the quaternion is specified relative to world-space.</param>
        /// <param name="pivot">A world space position from which the entity is moved and rotated around in a circular arc.</param>
        public void RotateWorldAbout(quaternion rotation, float3 pivot)
        {
            var oldPosition        = worldPosition;
            var pivotToOldPosition = oldPosition - pivot;
            var pivotToNewPosition = math.rotate(rotation, pivotToOldPosition);
            RotateWorld(rotation);
            TranslateWorld(pivotToNewPosition - pivotToOldPosition);
        }
        /// <summary>
        /// Rotates the entity's orientation by the specified rotation, where the axis of rotation is defined in the entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// If the entity does not have a parent, this is equivalent to RotateWorld().
        /// </summary>
        /// <param name="rotation">The amount to rotate by, where the innate axis of rotation of the quaternion is specified in the entity's local space relative to its parent.</param>
        public void RotateLocal(quaternion rotation) => localRotation = math.mul(rotation, localRotation);
        /// <summary>
        /// Without changing its position or rotation, pretends as if the entity is parented to a pivot object at the location specified by pivot
        /// in the entity's local-space axes relative to its parent. Then, the entity is rotated and repositioned by rotating the pivot object
        /// by the specified rotation in the entity's local-space axes relative to its parent.
        /// If the entity has a real parent, the localTransform and worldTransform are synchronized using the cached ParentToWorldTransform.
        /// If the entity does not have a parent, this is equivalent to RotateWorldAbout().
        /// </summary>
        /// <param name="rotation">The amount to rotate the pivot by, where the innate axis of the rotation of the quaternion is specified in the entity's local space relative to its parent.</param>
        /// <param name="pivot">A local-space position relative to the entity's parent from which the entity is moved and rotated around in a circular arc.</param>
        public void RotateLocalAbout(quaternion rotation, float3 pivot)
        {
            var oldPosition        = localPosition;
            var pivotToOldPosition = oldPosition - pivot;
            var pivotToNewPosition = math.rotate(rotation, pivotToOldPosition);
            RotateLocal(rotation);
            TranslateLocal(pivotToNewPosition - pivotToOldPosition);
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
        /// <summary>Transform a point from parent space into world space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        readonly public float3 TransformPointParentToWorld(float3 point)
        {
            return qvvs.TransformPoint(parentToWorldTransform, point);
        }

        /// <summary>Transform a point from world space into parent space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        readonly public float3 TransformPointWorldToParent(float3 point)
        {
            return qvvs.InverseTransformPoint(parentToWorldTransform, point);
        }

        /// <summary>Transform a point from local space into world space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        readonly public float3 TransformPointLocalToWorld(float3 point)
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

        /// <summary>Transforms a direction vector from parent space into world space, ignoring the effects of stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionParentToWorld(float3 direction)
        {
            return qvvs.TransformDirection(parentToWorldTransform, direction);
        }

        /// <summary>Transforms a direction vector from parent space into world space, including directional changes caused by stretch while preserving magnitude.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionParentToWorldWithStretch(float3 direction)
        {
            return qvvs.TransformDirectionWithStretch(parentToWorldTransform, direction);
        }

        /// <summary>Transforms a direction vector from parent space into world space, including directional and magnitude changes caused by scale and stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionParentToWorldScaledAndStretched(float3 direction)
        {
            return qvvs.TransformDirectionScaledAndStretched(parentToWorldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into parent space, ignoring the effects of stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionWorldToParent(float3 direction)
        {
            return qvvs.TransformDirection(parentToWorldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into parent space, including directional changes caused by stretch while preserving magnitude.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionWorldToParentWithStretch(float3 direction)
        {
            return qvvs.TransformDirectionWithStretch(parentToWorldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into parent space, including directional and magnitude changes caused by scale and stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionWorldToParentScaledAndStretched(float3 direction)
        {
            return qvvs.TransformDirectionScaledAndStretched(parentToWorldTransform, direction);
        }

        /// <summary>Transforms a direction vector from local space into world space, ignoring the effects of stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionLocalToWorld(float3 direction)
        {
            return qvvs.TransformDirection(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from local space into world space, including directional changes caused by stretch while preserving magnitude.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionLocalToWorldWithStretch(float3 direction)
        {
            return qvvs.TransformDirectionWithStretch(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from local space into world space, including directional and magnitude changes caused by scale and stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionLocalToWorldScaledAndStretched(float3 direction)
        {
            return qvvs.TransformDirectionScaledAndStretched(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into local space, ignoring the effects of stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionWorldToLocal(float3 direction)
        {
            return qvvs.TransformDirection(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into local space, including directional changes caused by stretch while preserving magnitude.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionWorldToLocalWithStretch(float3 direction)
        {
            return qvvs.TransformDirectionWithStretch(worldTransform, direction);
        }

        /// <summary>Transforms a direction vector from world space into local space, including directional and magnitude changes caused by scale and stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        readonly public float3 TransformDirectionWorldToLocalScaledAndStretched(float3 direction)
        {
            return qvvs.TransformDirectionScaledAndStretched(worldTransform, direction);
        }
        #endregion

        // Convenient shorthand for member methods and properties
        ref readonly TransformQvvs parentToWorldInternal => ref m_parentToWorldTransform.ValueRO.parentToWorldTransform;

        static TransformQvs QvsFromQvvs(in TransformQvvs qvvs)
        {
            return new TransformQvs
            {
                position = qvvs.position,
                rotation = qvvs.rotation,
                scale    = qvvs.scale
            };
        }

        void ThrowOnWriteToCopyParentWorldTransformTag()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            throw new System.InvalidOperationException("Entity has an CopyParentWorldTransformTag. You cannot write to the postion, rotation, or scale.");
#endif
        }
    }
}
#endif

