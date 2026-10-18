#if !LATIOS_TRANSFORMS_UNITY
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    /// <summary>
    /// A representation of a transform in which reading is immediate, but writing may be deferred if the entity belongs to a hierarchy.
    /// Otherwise, writing is also applied immediately.
    /// </summary>
    [IJobEach.ParameterHandle(typeof(TickedTransformDeferableAspectParameterHandle), IJobEach.ScheduleModeMask.ScheduleParallel)]
    [IJobEach.ParameterHandle(typeof(TickedTransformAspectRootHandle), IJobEach.ScheduleModeMask.All, typeof(RootOnlyAttribute))]
    public unsafe struct TickedTransformDeferableAspect : IJobEach.IParameter
    {
        internal TickedTransformAspect                        transform;
        internal NativeList<TickedTransformBatchWriteCommand> commands;

        #region Read/Write Properties
        /// <summary>
        /// The world-space position of the entity that can be read or modified.
        /// If the entity has a parent, the localPosition and worldPosition are synchronized using the parent's WorldTransform.
        /// </summary>
        public float3 worldPosition
        {
            get => transform.worldPosition;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.worldPosition = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetWorldPosition(transform, value));
            }
        }

        /// <summary>
        /// The world-space rotation of the entity that can be read or modified.
        /// If the entity has a parent, the localRotation and worldRotation are synchronized using the parent's WorldTransform.
        /// </summary>
        public quaternion worldRotation
        {
            get => transform.worldRotation;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.worldRotation = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetWorldRotation(transform, value));
            }
        }

        /// <summary>
        /// The world-space scale of the entity that can be read or modified.
        /// If the entity has a parent, the localScale and worldScale are synchronized using the parent's WorldTransform.
        /// </summary>
        public float worldScale
        {
            get => transform.worldScale;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.worldScale = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetWorldScale(transform, value));
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
            get => transform.localPosition;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.localPosition = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetLocalPosition(transform, value));
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
            get => transform.localRotation;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.localRotation = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetLocalRotation(transform, value));
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
            get => transform.localScale;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.localScale = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetLocalScale(transform, value));
            }
        }

        /// <summary>
        /// The stretch of the entity that can be read or modified.
        /// This value affects children's positions but nothing else.
        /// </summary>
        public float3 stretch
        {
            get => transform.stretch;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.stretch = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetStretch(transform, value));
            }
        }

        /// <summary>
        /// The context32 of the entity that can be read or modified.
        /// It is a user value (do what you want with it) and not used directly by Latios TickedTransforms (though other modules may support specific use cases).
        /// NOTE: This is always applied immediately, even in hierarchies!
        /// </summary>
        public int context32
        {
            get => transform.context32;
            set => transform.context32 = value;
        }

        /// <summary>
        /// The world-space QVVS transform of the entity that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// </summary>
        public TransformQvvs worldTransform
        {
            get => transform.worldTransform;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.worldTransform = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetWorldTransform(transform, in value));
            }
        }

        /// <summary>
        /// The local-space QVS transform of the entity that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// Otherwise, this reads/writes the position, rotation, and scale properties of the worldTransform.
        /// When reading, TickedTransformQvs.identity is returned if the entity has an CopyParentWorldTransformTag.
        /// </summary>
        public TransformQvs localTransform
        {
            get => transform.localTransform;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.localTransform = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetLocalTransform(transform, in value));
            }
        }

        /// <summary>
        /// The local-space transform in full QVVS representation that can be read or modified.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's WorldTransform.
        /// Otherwise, this reads/writes to the worldTransform.
        /// </summary>
        public TransformQvvs localTransformQvvs
        {
            get => transform.localTransformQvvs;
            set
            {
                if (transform.entityInHierarchyHandle.isNull)
                    transform.localTransformQvvs = value;
                else
                    commands.Add(TickedTransformBatchWriteCommand.SetLocalTransformQvvs(transform, in value));
            }
        }
        #endregion

        #region ReadOnly Properties
        /// <summary>
        /// Retrieves the EntityInHierarchyHandle of this TickedTransformDeferableAspect. Note that if the entity
        /// does not belong to a hierarchy, EntityInHierarchyHandle.isNull will be true.
        /// </summary>
        public EntityInHierarchyHandle entityInHierarchyHandle => transform.entityInHierarchyHandle;

        /// <summary>
        /// Retrieves the read-only form of this TickedTransformDeferableAspect. The read-only form can be used in
        /// methods that require it, or to read other transforms in the hierarchy without dirtying
        /// change filters.
        /// </summary>
        public TickedTransformReadAspect asRO => transform.asRO;

        /// <summary>
        /// Retrieves the TickedTransformDeferableAspect for the specified handle belonging to the same hierarchy
        /// as this TickedTransformDeferableAspect. When safety checks exist, this method throws if the specifed
        /// handle comes from another hierarchy or if either its handle or this TickedTransformDeferableAspect's
        /// handle is null.
        /// </summary>
        public TickedTransformDeferableAspect this[EntityInHierarchyHandle otherHandle]
        {
            get => new TickedTransformDeferableAspect
            {
                transform = transform[otherHandle],
                commands  = commands
            };
        }
        /// <summary>
        /// Attempts to retrieve the TickedTransformDeferableAspect for the specified handle belonging to the same
        /// hierarchy as this TickedTransformDeferableAspect. When safety checks exist, this method throws if the
        /// specifed handle comes from another hierarchy. This method returns false if the transform
        /// is not present.
        /// </summary>
        public bool TryGetAspect(in EntityInHierarchyHandle otherHandle, out TickedTransformDeferableAspect otherTickedTransformDeferableAspect)
        {
            var result                          = transform.TryGetAspect(in otherHandle, out var otherTickedTransform);
            otherTickedTransformDeferableAspect = new TickedTransformDeferableAspect
            {
                transform = otherTickedTransform,
                commands  = commands
            };
            return result;
        }
        /// <summary>
        /// True if the entity has a parent and not a CopyParent inheritance flag.
        /// </summary>
        public bool hasMutableLocalTransform => transform.hasMutableLocalTransform;
        /// <summary>
        /// True if the entity has a parent
        /// </summary>
        public bool hasParent => transform.hasParent;

        /// <summary>
        /// The unit forward vector (local Z+) of the entity in world-space
        /// </summary>
        public float3 forwardDirection => transform.forwardDirection;
        /// <summary>
        /// The unit backward vector (local Z-) of the entity in world-space
        /// </summary>
        public float3 backwardDirection => transform.backwardDirection;
        /// <summary>
        /// The unit left vector (local X-) of the entity in world-space
        /// </summary>
        public float3 leftDirection => transform.leftDirection;
        /// <summary>
        /// The unit right vector (local X+) of the entity in world-space
        /// </summary>
        public float3 rightDirection => transform.rightDirection;
        /// <summary>
        /// The unit up vector (local Y+) of the entity in world-space
        /// </summary>
        public float3 upDirection => transform.upDirection;
        /// <summary>
        /// The unit down vector (local Y-) of the entity in world-space
        /// </summary>
        public float3 downDirection => transform.downDirection;

        /// <summary>
        /// The matrix that represents the transformation of the entity from local-space to world-space including stretch.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 worldMatrix3x4 => transform.worldMatrix3x4;
        /// <summary>
        /// The matrix that represents the transformation of the entity from local-space to world-space including stretch.
        /// </summary>
        public float4x4 worldMatrix4x4 => transform.worldMatrix4x4;
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to local-space including stretch.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseWorldMatrix3x4 => transform.inverseWorldMatrix3x4;
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to local-space including stretch.
        /// </summary>
        public float4x4 inverseWorldMatrix4x4 => transform.inverseWorldMatrix4x4;
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to local-space ignoring stretch.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseWorldMatrix3x4IgnoreStretch => transform.inverseWorldMatrix3x4IgnoreStretch;
        /// <summary>
        /// The matrix that represents the transformation of the entity from world-space to local-space ignoring stretch.
        /// </summary>
        public float4x4 inverseWorldMatrix4x4IgnoreStretch => transform.inverseWorldMatrix4x4IgnoreStretch;

        /// <summary>
        /// The matrix that represent's the entity's local transform relative to its parent, or relative to the world if it does not have a parent.
        /// Stretch is included.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 localMatrix3x4 => transform.localMatrix3x4;
        /// <summary>
        /// The matrix that represent's the entity's local transform relative to its parent, or relative to the world if it does not have a parent.
        /// Stretch is included.
        /// </summary>
        public float4x4 localMatrix4x4 => transform.localMatrix4x4;
        /// <summary>
        /// The inverse of localMatrix3x4, computed directly from the QVS or QVVS data. Stretch is included.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseLocalMatrix3x4 => transform.inverseLocalMatrix3x4;
        /// <summary>
        /// The inverse of localMatrix4x4, computed directly from the QVS or QVVS data. Stretch is included.
        /// </summary>
        public float4x4 inverseLocalMatrix4x4 => transform.inverseLocalMatrix4x4;
        /// <summary>
        /// The inverse of localMatrix3x4, computed directly from the QVS or QVVS data, except stretch is ignored.
        /// This version discards the bottom row of a typical 4x4 matrix as that row is assumed to be (0, 0, 0, 1).
        /// </summary>
        public float3x4 inverseLocalMatrix3x4IgnoreStretch => transform.inverseLocalMatrix3x4IgnoreStretch;
        /// <summary>
        /// The inverse of localMatrix4x4, computed directly from the QVS or QVVS data, except stretch is ignored.
        /// </summary>
        public float4x4 inverseLocalMatrix4x4IgnoreStretch => transform.inverseLocalMatrix4x4IgnoreStretch;
        #endregion

        #region Modification Methods
        /// <summary>
        /// Moves the entity by the amount specified in translation along the world-space axes.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform,
        /// and the actual application of the translation is deferred.
        /// </summary>
        /// <param name="translation">The world-space x, y, and z signed amounts to move the entity</param>
        public void TranslateWorld(float3 translation)
        {
            if (transform.entityInHierarchyHandle.isNull)
                transform.worldPosition += translation;
            else
                commands.Add(TickedTransformBatchWriteCommand.TranslateWorld(transform, translation));
        }
        /// <summary>
        /// Moves the entity by the amount specified in x, y, and z along the world-space axes.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform.
        /// </summary>
        /// <param name="x">The signed amount to move the entity along the positive x axis</param>
        /// <param name="y">The signed amount to move the entity along the positive y axis</param>
        /// <param name="z">The signed amount to move the entity along the positive z axis</param>
        public void TranslateWorld(float x, float y, float z) => TranslateWorld(new float3(x, y, z));
        /// <summary>
        /// Moves the entity by the amount specified in translation along the Entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform.
        /// If the entity does not have a parent, this is equivalent to TranslateWorld().
        /// </summary>
        /// <param name="translation">The local-space x, y, and z signed amounts to move the entity</param>
        public void TranslateLocal(float3 translation)
        {
            if (transform.entityInHierarchyHandle.isNull)
                transform.worldPosition += translation;
            else
                commands.Add(TickedTransformBatchWriteCommand.TranslateLocal(transform, translation));
        }
        /// <summary>
        /// Moves the entity by the amount specified in x, y, and z along the Entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform.
        /// If the entity does not have a parent, this is equivalent to TranslateWorld().
        /// </summary>
        /// <param name="x">The signed amount to move the entity along the positive x axis</param>
        /// <param name="y">The signed amount to move the entity along the positive y axis</param>
        /// <param name="z">The signed amount to move the entity along the positive z axis</param>
        public void TranslateLocal(float x, float y, float z) => TranslateLocal(new float3(x, y, z));

        /// <summary>
        /// Rotates the entity's orientation by the specified rotation, where the axis of rotation is defined in world-space.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform,
        /// and the actual application of the rotation delta is deferred.
        /// </summary>
        /// <param name="rotation">The amount to rotate by, where the innate axis of rotation of the quaternion is specified relative to world-space.</param>
        public void RotateWorld(quaternion rotation)
        {
            if (transform.entityInHierarchyHandle.isNull)
                transform.worldRotation = math.normalize(math.mul(rotation, transform.worldRotation));
            else
                commands.Add(TickedTransformBatchWriteCommand.RotateWorld(transform, rotation));
        }

        /// <summary>
        /// Rotates the entity's orientation by the specified rotation, where the axis of rotation is defined in the entity's local-space axes relative to its parent.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform.
        /// If the entity does not have a parent, this is equivalent to RotateWorld().
        /// </summary>
        /// <param name="rotation">The amount to rotate by, where the innate axis of rotation of the quaternion is specified in the entity's local space relative to its parent.</param>
        public void RotateLocal(quaternion rotation)
        {
            if (transform.entityInHierarchyHandle.isNull)
                transform.worldRotation = math.normalize(math.mul(rotation, transform.worldRotation));
            else
                commands.Add(TickedTransformBatchWriteCommand.RotateLocal(transform, rotation));
        }

        /// <summary>
        /// Sets both the world-space position and rotation of the entity in a single operation.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform.
        /// </summary>
        /// <param name="worldPosition">The new world-space position to apply</param>
        /// <param name="worldRotation">The new world-space rotation to apply</param>
        public void SetWorldPositionAndRotation(float3 worldPosition, quaternion worldRotation)
        {
            if (transform.entityInHierarchyHandle.isNull)
                transform.SetWorldPositionAndRotation(worldPosition, worldRotation);
            else
                commands.Add(TickedTransformBatchWriteCommand.SetWorldPositionAndRotation(transform, worldPosition, worldRotation));
        }

        /// <summary>
        /// Sets both the local-space position and rotation of the entity relative to its parent in a single operation.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform.
        /// If the entity does not have a parent, this is equivalent to SetWorldPositionAndRotation().
        /// </summary>
        /// <param name="localPosition">The new local-space position to apply</param>
        /// <param name="localRotation">The new local-space rotation to apply</param>
        public void SetLocalPositionAndRotation(float3 localPosition, quaternion localRotation)
        {
            if (transform.entityInHierarchyHandle.isNull)
                transform.SetLocalPositionAndRotation(localPosition, localRotation);
            else
                commands.Add(TickedTransformBatchWriteCommand.SetLocalPositionAndRotation(transform, localPosition, localRotation));
        }

        /// <summary>
        /// Moves and rotates the entity by the specified deltas in a single operation, where both the translation and the
        /// axis of rotation are defined in world-space. This is equivalent to calling TranslateWorld() and RotateWorld()
        /// separately, but propagates to children only once instead of twice.
        /// If the entity has a parent, the localTransform and worldTransform are synchronized using the parent's TickedWorldTransform.
        /// </summary>
        /// <param name="translation">The world-space x, y, and z signed amounts to move the entity</param>
        /// <param name="rotation">The amount to rotate by, where the innate axis of rotation of the quaternion is specified relative to world-space.</param>
        public void TranslateRotateWorld(float3 translation, quaternion rotation)
        {
            if (transform.entityInHierarchyHandle.isNull)
                transform.TranslateRotateWorld(translation, rotation);
            else
                commands.Add(TickedTransformBatchWriteCommand.TranslateRotateWorld(transform, translation, rotation));
        }

        /// <summary>
        /// Moves and rotates the entity by the specified deltas in a single operation, where both the translation and the
        /// axis of rotation are defined in the entity's local-space axes relative to its parent. This is equivalent to calling
        /// TranslateLocal() and RotateLocal() separately, but propagates to children only once instead of twice.
        /// If the entity does not have a parent, this is equivalent to TranslateRotateWorld().
        /// </summary>
        /// <param name="translation">The local-space x, y, and z signed amounts to move the entity</param>
        /// <param name="rotation">The amount to rotate by, where the innate axis of rotation of the quaternion is specified in the entity's local space relative to its parent.</param>
        public void TranslateRotateLocal(float3 translation, quaternion rotation)
        {
            if (transform.entityInHierarchyHandle.isNull)
                transform.TranslateRotateLocal(translation, rotation);
            else
                commands.Add(TickedTransformBatchWriteCommand.TranslateRotateLocal(transform, translation, rotation));
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

        #region ReadOnly TickedTransformation Methods
        /// <summary>Transform a point from local space into world space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TickedTransformPointLocalToWorld(float3 point) => transform.TransformPointLocalToWorld(point);

        /// <summary>Transform a point from world space into local space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TickedTransformPointWorldToLocal(float3 point) => transform.TransformPointWorldToLocal(point);

        /// <summary>Transforms a direction vector from local space into world space, ignoring the effects of stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TickedTransformDirectionLocalToWorld(float3 direction) => transform.TransformDirectionLocalToWorld(direction);

        /// <summary>Transforms a direction vector from local space into world space, including directional changes caused by stretch while preserving magnitude.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TickedTransformDirectionLocalToWorldWithStretch(float3 direction) => transform.TransformDirectionLocalToWorldWithStretch(direction);

        /// <summary>Transforms a direction vector from local space into world space, including directional and magnitude changes caused by scale and stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TickedTransformDirectionLocalToWorldScaledAndStretched(float3 direction) => transform.TransformDirectionLocalToWorldScaledAndStretched(direction);

        /// <summary>Transforms a direction vector from world space into local space, ignoring the effects of stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TickedTransformDirectionWorldToLocal(float3 direction) => transform.TransformDirectionWorldToLocal(direction);

        /// <summary>Transforms a direction vector from world space into local space, including directional changes caused by stretch while preserving magnitude.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TickedTransformDirectionWorldToLocalWithStretch(float3 direction) => transform.TransformDirectionWorldToLocalWithStretch(direction);

        /// <summary>Transforms a direction vector from world space into local space, including directional and magnitude changes caused by scale and stretch.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TickedTransformDirectionWorldToLocalScaledAndStretched(float3 direction) => transform.TransformDirectionWorldToLocalScaledAndStretched(direction);
        #endregion
    }
}
#endif

