#if !LATIOS_TRANSFORMS_UNITY
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static class WorldLocalOps
    {
        public static float3 GetLocalPositionRO(in TransformQvvs parent,
                                                in TransformQvvs child,
                                                in EntityInHierarchyHandle parentHandle,
                                                in EntityInHierarchyHandle childHandle,
                                                bool isTicked)
        {
            var (pos, _) = ReadLocal(in childHandle, isTicked);
            if (parentHandle.indexInHierarchy != childHandle.bloodParent.indexInHierarchy)
            {
                // First, try and see if we can reproduce the world transform from the saved local transform properties.
                // If so, then the local transform properties can be considered more accurate. Note that for a deleted parent
                // with a zero-scale ancestor, the local position will be taken, even though the local position relative to
                // the former parent may be different than the local position relative to the new parent. Specifically, the
                // rotation of the new parent in world-space may be different than the rotation of the former parent. So it
                // will be as if the local position was rotated by the difference of these rotations.
                var testPosition = qvvs.TransformPoint(in parent, pos);
                if (!math.all(math.abs(child.position - testPosition) < 1e-5f))
                {
                    // We got a mismatch. Try to compute the local position. This can be somewhat lossy over many frames.
                    // If we get a bad calculation, fall back to whatever we had previously.
                    var newPosition = qvvs.InverseTransformPoint(in parent, pos);
                    pos             = math.select(pos, newPosition, math.isfinite(newPosition));
                }
            }
            return pos;
        }

        public static float GetLocalScaleRO(in TransformQvvs parent,
                                            in TransformQvvs child,
                                            in EntityInHierarchyHandle parentHandle,
                                            in EntityInHierarchyHandle childHandle,
                                            bool isTicked)
        {
            var (_, scale) = ReadLocal(in childHandle, isTicked);
            if (parentHandle.indexInHierarchy != childHandle.bloodParent.indexInHierarchy)
            {
                // First, try and see if we can reproduce the world transform from the saved local transform properties.
                // If so, then the local transform properties can be considered more accurate.
                if (math.distance(child.scale, qvvs.TransformScale(in parent, scale)) >= 1e-5f)
                {
                    // We got a scale mismatch.
                    var newScale = qvvs.InverseTransformScale(in parent, scale);
                    scale        = math.select(scale, newScale, math.isfinite(newScale));
                }
            }
            return scale;
        }

        public static TransformQvs GetLocalTransformRO(in TransformQvvs parent,
                                                       in TransformQvvs child,
                                                       in EntityInHierarchyHandle parentHandle,
                                                       in EntityInHierarchyHandle childHandle,
                                                       bool isTicked)
        {
            var (pos, scale) = ReadLocal(in childHandle, isTicked);
            var rot          = math.normalize(math.mul(math.conjugate(parent.rotation), child.rotation));
            if (parentHandle.indexInHierarchy != childHandle.bloodParent.indexInHierarchy)
            {
                // First, try and see if we can reproduce the world transform from the saved local transform properties.
                // If so, then the local transform properties can be considered more accurate. Note that for a deleted parent
                // with a zero-scale ancestor, the local position will be taken, even though the local position relative to
                // the former parent may be different than the local position relative to the new parent. Specifically, the
                // rotation of the new parent in world-space may be different than the rotation of the former parent. So it
                // will be as if the local position was rotated by the difference of these rotations.
                var testPosition = qvvs.TransformPoint(in parent, pos);
                if (!math.all(math.abs(child.position - testPosition) < 1e-5f))
                {
                    // We got a mismatch. Try to compute the local position. This can be somewhat lossy over many frames.
                    // If we get a bad calculation, fall back to whatever we had previously.
                    var newPosition = qvvs.InverseTransformPoint(in parent, pos);
                    pos             = math.select(pos, newPosition, math.isfinite(newPosition));
                }
                if (math.distance(child.scale, qvvs.TransformScale(in parent, scale)) >= 1e-5f)
                {
                    // We got a scale mismatch.
                    var newScale = qvvs.InverseTransformScale(in parent, scale);
                    scale        = math.select(scale, newScale, math.isfinite(newScale));
                }
            }
            return new TransformQvs(pos, rot, scale);
        }

        public static void SetLocalPosition(float3 localPosition, in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle, bool isTicked)
        {
            WriteLocalPosition(in childHandle, isTicked, localPosition);
            child.position = qvvs.TransformPoint(in parent, localPosition);
        }

        public static void SetLocalRotation(quaternion localRotation, in TransformQvvs parent, ref TransformQvvs child)
        {
            child.rotation = math.normalize(math.mul(parent.rotation, localRotation));
        }

        public static void SetLocalScale(float localScale, in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle, bool isTicked)
        {
            WriteLocalScale(in childHandle, isTicked, localScale);
            child.scale = qvvs.TransformScale(in parent, localScale);
        }

        public static void SetLocalTransform(in TransformQvs localTransform, in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle,
                                             bool isTicked)
        {
            WriteLocal(in childHandle, isTicked, localTransform.position, localTransform.scale);
            qvvs.mulclean(ref child, in parent, in localTransform);
        }
        public static void SetLocalTransformQvvs(in TransformQvvs localTransform, in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle,
                                                 bool isTicked)
        {
            WriteLocal(in childHandle, isTicked, localTransform.position, localTransform.scale);
            child = qvvs.mulclean(in parent, in localTransform);
        }

        public static void SetStretch(float3 stretch, ref TransformQvvs child) => child.stretch = stretch;

        public static void SetWorldPosition(float3 worldPosition, in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle, bool isTicked)
        {
            var newLocalPosition = qvvs.InverseTransformPoint(in parent, worldPosition);
            var bad              = !math.isfinite(newLocalPosition);
            if (math.any(bad))
            {
                // We have zero scale. Unity's behavior is to wipe the local position whenever scale is less than 1e-9f.
                // While we don't use the same constant, we use the same behavior, unless we have a world position inheritance mode.
                // In that case, we snap the world position to the parent's for any inherited axes, mix with the world position mode,
                // and then compute the local position assuming a scale of 1f.
                var   flags = childHandle.inheritanceFlags;
                bool3 keepWorld;
                keepWorld.x              = (flags & InheritanceFlags.WorldX) != InheritanceFlags.Normal;
                keepWorld.y              = (flags & InheritanceFlags.WorldY) != InheritanceFlags.Normal;
                keepWorld.z              = (flags & InheritanceFlags.WorldZ) != InheritanceFlags.Normal;
                var originalWorldOffset  = worldPosition - parent.position;
                var parentRelativeOffset = math.InverseRotateFast(parent.rotation, originalWorldOffset);
                parentRelativeOffset     = math.select(parentRelativeOffset, float3.zero, bad);
                var worldOffset          = math.rotate(parent.rotation, parentRelativeOffset);
                worldOffset              = math.select(worldOffset, originalWorldOffset, keepWorld);
                newLocalPosition         = math.InverseRotateFast(parent.rotation, worldOffset);
                // This select here avoids precision loss for the axes we want to keep.
                worldPosition = math.select(parent.position + worldOffset, worldOffset, keepWorld);
            }
            child.position = worldPosition;
            WriteLocalPosition(in childHandle, isTicked, newLocalPosition);
        }

        public static void SetWorldRotation(quaternion worldRotation, ref TransformQvvs child) => child.rotation = worldRotation;

        public static void SetWorldScale(float worldScale, in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle, bool isTicked)
        {
            var newLocalScale = qvvs.InverseTransformScale(in parent, worldScale);
            if (!math.isfinite(newLocalScale))
            {
                // We have zero scale. Unity's behavior is to wipe the local scale whenever scale is less than 1e-9f.
                // While we don't use the same constant, we use the same behavior, unless we have a world scale inheritance mode.
                // In that case, we set the local scale to the world scale.
                bool keepWorld = (childHandle.inheritanceFlags & InheritanceFlags.WorldScale) != InheritanceFlags.Normal;
                worldScale     = math.select(0f, worldScale, keepWorld);
                newLocalScale  = worldScale;
            }
            child.scale = worldScale;
            WriteLocalScale(in childHandle, isTicked, newLocalScale);
        }

        public static void SetWorldTransform(TransformQvvs worldTransform, in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle, bool isTicked)
        {
            var newLocalPosition = qvvs.InverseTransformPoint(in parent, worldTransform.position);
            var newLocalScale    = qvvs.InverseTransformScale(in parent, worldTransform.scale);
            var bad              = !math.isfinite(newLocalPosition);
            if (math.any(bad))
            {
                // We have zero scale. Unity's behavior is to wipe the local position whenever scale is less than 1e-9f.
                // While we don't use the same constant, we use the same behavior, unless we have a world position inheritance mode.
                // In that case, we snap the world position to the parent's for any inherited axes, mix with the world position mode,
                // and then compute the local position assuming a scale of 1f.
                var   flags = childHandle.inheritanceFlags;
                bool3 keepWorldPosition;
                keepWorldPosition.x      = (flags & InheritanceFlags.WorldX) != InheritanceFlags.Normal;
                keepWorldPosition.y      = (flags & InheritanceFlags.WorldY) != InheritanceFlags.Normal;
                keepWorldPosition.z      = (flags & InheritanceFlags.WorldZ) != InheritanceFlags.Normal;
                var originalWorldOffset  = worldTransform.position - parent.position;
                var parentRelativeOffset = math.InverseRotateFast(parent.rotation, originalWorldOffset);
                parentRelativeOffset     = math.select(parentRelativeOffset, float3.zero, bad);
                var worldOffset          = math.rotate(parent.rotation, parentRelativeOffset);
                worldOffset              = math.select(worldOffset, originalWorldOffset, keepWorldPosition);
                newLocalPosition         = math.InverseRotateFast(parent.rotation, worldOffset);
                // This select here avoids precision loss for the axes we want to keep.
                worldTransform.position = math.select(parent.position + worldOffset, worldOffset, keepWorldPosition);

                // There is a likely but not certain probability that scale is zero.
                if (!math.isfinite(newLocalScale))
                {
                    // We have zero scale. Unity's behavior is to wipe the local scale whenever scale is less than 1e-9f.
                    // While we don't use the same constant, we use the same behavior, unless we have a world scale inheritance mode.
                    // In that case, we set the local scale to the world scale.
                    bool keepWorldScale  = (childHandle.inheritanceFlags & InheritanceFlags.WorldScale) != InheritanceFlags.Normal;
                    worldTransform.scale = math.select(0f, worldTransform.scale, keepWorldScale);
                    newLocalScale        = worldTransform.scale;
                }
            }
            child = worldTransform;
            WriteLocal(in childHandle, isTicked, newLocalPosition, newLocalScale);
        }

        public static void TranslateLocal(float3 localPositionDelta,
                                          in TransformQvvs parent,
                                          ref TransformQvvs child,
                                          in EntityInHierarchyHandle parentHandle,
                                          in EntityInHierarchyHandle childHandle,
                                          bool isTicked)
        {
            var localPosition  = GetLocalPositionRO(in parent, in child, in parentHandle, in childHandle, isTicked);
            localPosition     += localPositionDelta;
            WriteLocalPosition(in childHandle, isTicked, localPosition);
            child.position = qvvs.TransformPoint(in parent, localPosition);
        }

        public static void RotateLocal(quaternion rotationDelta, in TransformQvvs parent, ref TransformQvvs child)
        {
            child.rotation = math.normalize(math.mul(parent.rotation, math.mul(rotationDelta, math.mul(math.conjugate(parent.rotation), child.rotation))));
        }

        public static void TransformLocal(in TransformQvvs transform,
                                          in TransformQvvs parent,
                                          ref TransformQvvs child,
                                          in EntityInHierarchyHandle parentHandle,
                                          in EntityInHierarchyHandle childHandle,
                                          bool isTicked)
        {
            var local   = GetLocalTransformRO(in parent, in child, in parentHandle, in childHandle, isTicked);
            var applied = child;
            qvvs.mul(ref applied, in transform, in local);
            WriteLocal(in childHandle, isTicked, applied.position, applied.scale);
            child = qvvs.mulclean(in parent, in applied);
        }

        public static void InverseTransformLocal(in TransformQvvs transform,
                                                 in TransformQvvs parent,
                                                 ref TransformQvvs child,
                                                 in EntityInHierarchyHandle parentHandle,
                                                 in EntityInHierarchyHandle childHandle,
                                                 bool isTicked)
        {
            var local       = GetLocalTransformRO(in parent, in child, in parentHandle, in childHandle, isTicked);
            var localAsQvvs = new TransformQvvs(local.position, local.rotation, local.scale, child.stretch, child.context32);
            var newLocal    = qvvs.inversemul(in transform, in localAsQvvs);
            // If the passed in transform had zero scaling, we just set the local positions and scales to 0.
            newLocal.position = math.select(float3.zero, newLocal.position, math.isfinite(newLocal.position));
            newLocal.scale    = math.select(0f, newLocal.scale, math.isfinite(newLocal.scale));
            WriteLocal(in childHandle, isTicked, newLocal.position, newLocal.scale);
            qvvs.mulclean(ref child, in parent, in newLocal);
        }

        public static void ScaleScale(float scale, ref TransformQvvs child, in EntityInHierarchyHandle childHandle, bool isTicked)
        {
            // We just multiply both the world and local scales by the scale factor. Even if the local scale is stale,
            // updating the world scale will still ensure we recover in roughly the same was as in the future. This way,
            // we can skip accessing the parent transform.
            child.scale         *= scale;
            var (_, localScale)  = ReadLocal(in childHandle, isTicked);
            localScale          *= scale;
            WriteLocalScale(in childHandle, isTicked, localScale);
        }

        public static void StretchStretch(float3 stretch, ref TransformQvvs child) => child.stretch *= stretch;

        public static void TranslateWorld(float3 translation, in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle, bool isTicked)
        {
            var newPosition = child.position + translation;
            SetWorldPosition(newPosition, in parent, ref child, in childHandle, isTicked);
        }

        public static void RotateWorld(quaternion rotationDelta, ref TransformQvvs child)
        {
            child.rotation = math.normalize(math.mul(rotationDelta, child.rotation));
        }

        public static void TransformWorld(in TransformQvvs transform,
                                          in TransformQvvs parent,
                                          ref TransformQvvs child,
                                          in EntityInHierarchyHandle parentHandle,
                                          in EntityInHierarchyHandle childHandle,
                                          bool isTicked)
        {
            var newWorld = qvvs.mulclean(transform, child);
            SetWorldTransform(newWorld, in parent, ref child, in childHandle, isTicked);
        }

        public static void InverseTransformWorld(in TransformQvvs transform,
                                                 in TransformQvvs parent,
                                                 ref TransformQvvs child,
                                                 in EntityInHierarchyHandle parentHandle,
                                                 in EntityInHierarchyHandle childHandle,
                                                 bool isTicked)
        {
            var newWorld = qvvs.inversemulqvvsclean(in transform, in child);
            // If the passed in transform had zero scaling, we just set the world positions and scales to 0.
            newWorld.position = math.select(float3.zero, newWorld.position, math.isfinite(newWorld.position));
            newWorld.scale    = math.select(0f, newWorld.scale, math.isfinite(newWorld.scale));
        }

        public static void SetCopyParentTransform(in TransformQvvs parent, ref TransformQvvs child, in EntityInHierarchyHandle childHandle, bool isTicked)
        {
            child = parent;
            WriteLocal(in childHandle, isTicked, float3.zero, 1f);
        }

        public static void PropagateTransform(in TransformQvvs newParent,
                                              in TransformQvvs oldParent,
                                              ref TransformQvvs child,
                                              in EntityInHierarchyHandle parentHandle,
                                              in EntityInHierarchyHandle childHandle,
                                              bool isTicked)
        {
            var flags = childHandle.inheritanceFlags;
            if (flags.HasCopyParent())
            {
                // This case doesn't need the local transform at all.
                SetCopyParentTransform(in newParent, ref child, in childHandle, isTicked);
                return;
            }
            var localTransform = GetLocalTransformRO(in oldParent, in child, in parentHandle, in childHandle, isTicked);
            if (parentHandle.indexInHierarchy != childHandle.bloodParent.indexInHierarchy)
                WriteLocal(in childHandle, isTicked, localTransform.position, localTransform.scale);

            if (flags == InheritanceFlags.Normal)
            {
                // Most common case. We just update the child transform.
                qvvs.mulclean(ref child, in newParent, in localTransform);
                return;
            }

            var propagatedWorld = child;
            qvvs.mulclean(ref propagatedWorld, in newParent, in localTransform);
            var newWorld = propagatedWorld;
            if ((flags & InheritanceFlags.WorldRotation) == InheritanceFlags.WorldRotation)
                newWorld.rotation = child.rotation;
            else if ((flags & InheritanceFlags.WorldRotation) != InheritanceFlags.Normal)
                newWorld.rotation = ComputeMixedRotation(child.rotation, propagatedWorld.rotation, flags);
            bool hasWorldPosition = (flags & InheritanceFlags.WorldPosition) != InheritanceFlags.Normal;
            if (hasWorldPosition)
            {
                if ((flags & InheritanceFlags.WorldX) == InheritanceFlags.WorldX)
                    newWorld.position.x = child.position.x;
                if ((flags & InheritanceFlags.WorldY) == InheritanceFlags.WorldY)
                    newWorld.position.y = child.position.y;
                if ((flags & InheritanceFlags.WorldZ) == InheritanceFlags.WorldZ)
                    newWorld.position.z = child.position.z;
            }
            bool hasWorldScale = (flags & InheritanceFlags.WorldScale) != InheritanceFlags.Normal;
            newWorld.scale     = math.select(newWorld.scale, child.scale, hasWorldScale);
            if (hasWorldPosition && hasWorldScale)
            {
                SetWorldTransform(newWorld, in newParent, ref propagatedWorld, in childHandle, isTicked);
            }
            else if (hasWorldPosition)
            {
                SetWorldPosition(newWorld.position, in newParent, ref propagatedWorld, in childHandle, isTicked);
                SetWorldRotation(newWorld.rotation, ref propagatedWorld);
            }
            else if (hasWorldScale)
            {
                SetWorldScale(newWorld.scale, in newParent, ref propagatedWorld, in childHandle, isTicked);
                SetWorldRotation(newWorld.rotation, ref propagatedWorld);
            }
            child = propagatedWorld;

            static quaternion ComputeMixedRotation(quaternion originalWorldRotation, quaternion hierarchyWorldRotation, InheritanceFlags flags)
            {
                var forward = math.select(math.forward(hierarchyWorldRotation),
                                          math.forward(originalWorldRotation),
                                          (flags & InheritanceFlags.WorldForward) == InheritanceFlags.WorldForward);
                var up = math.select(math.rotate(hierarchyWorldRotation, math.up()),
                                     math.rotate(originalWorldRotation, math.up()),
                                     (flags & InheritanceFlags.WorldUp) == InheritanceFlags.WorldUp);

                if ((flags & InheritanceFlags.StrictUp) == InheritanceFlags.StrictUp)
                {
                    float3 right = math.normalizesafe(math.cross(up, forward), float3.zero);
                    if (right.Equals(float3.zero))
                        return math.select(hierarchyWorldRotation.value, originalWorldRotation.value, (flags & InheritanceFlags.WorldUp) == InheritanceFlags.WorldUp);
                    var newForward = math.cross(right, up);
                    return new quaternion(new float3x3(right, up, newForward));
                }
                else
                {
                    float3 right = math.normalizesafe(math.cross(up, forward), float3.zero);
                    if (right.Equals(float3.zero))
                        return math.select(hierarchyWorldRotation.value,
                                           originalWorldRotation.value,
                                           (flags & InheritanceFlags.WorldForward) == InheritanceFlags.WorldForward);
                    var newUp = math.cross(forward, right);
                    return new quaternion(new float3x3(right, newUp, forward));
                }
            }
        }

        public static void UpdateLocalTransformForCleanedParent(in TransformQvvs newParent, in TransformQvvs child, ref EntityInHierarchy localInHierarchy, bool isTicked)
        {
            ref var pos   = ref isTicked ? ref localInHierarchy.m_tickedLocalPosition : ref localInHierarchy.m_localPosition;
            ref var scale = ref isTicked ? ref localInHierarchy.m_tickedLocalScale : ref localInHierarchy.m_localScale;
            // First, try and see if we can reproduce the world transform from the saved local transform properties.
            // If so, then the local transform properties can be considered more accurate. Note that for a deleted parent
            // with a zero-scale ancestor, the local position will be taken, even though the local position relative to
            // the former parent may be different than the local position relative to the new parent. Specifically, the
            // rotation of the new parent in world-space may be different than the rotation of the former parent. So it
            // will be as if the local position was rotated by the difference of these rotations.
            var testPosition = qvvs.TransformPoint(in newParent, pos);
            if (!math.all(math.abs(child.position - testPosition) < 1e-5f))
            {
                // We got a mismatch. Try to compute the local position. This can be somewhat lossy over many frames.
                // If we get a bad calculation, fall back to whatever we had previously.
                var newPosition = qvvs.InverseTransformPoint(in newParent, pos);
                pos             = math.select(pos, newPosition, math.isfinite(newPosition));
            }
            if (math.distance(child.scale, qvvs.TransformScale(in newParent, scale)) >= 1e-5f)
            {
                // We got a scale mismatch.
                var newScale = qvvs.InverseTransformScale(in newParent, scale);
                scale        = math.select(scale, newScale, math.isfinite(newScale));
            }
        }

        static (float3, float) ReadLocal(in EntityInHierarchyHandle handle, bool isTicked)
        {
            ref readonly var element = ref handle.m_hierarchy.AsReadOnlySpan()[handle.indexInHierarchy];
            if (isTicked)
                return (element.m_tickedLocalPosition, element.m_tickedLocalScale);
            else
                return (element.m_localPosition, element.m_localScale);
        }

        static unsafe void WriteLocal(in EntityInHierarchyHandle handle, bool isTicked, float3 position, float scale)
        {
            ref var element = ref ((EntityInHierarchy*)handle.m_hierarchy.GetUnsafeReadOnlyPtr())[handle.indexInHierarchy];
            if (isTicked)
            {
                element.m_tickedLocalPosition = position;
                element.m_tickedLocalScale    = scale;
            }
            else
            {
                element.m_localPosition = position;
                element.m_localScale    = scale;
            }

            if (handle.m_extraHierarchy != null)
            {
                ref var extra = ref handle.m_extraHierarchy[handle.indexInHierarchy];
                if (isTicked)
                {
                    extra.m_tickedLocalPosition = position;
                    extra.m_tickedLocalScale    = scale;
                }
                else
                {
                    extra.m_localPosition = position;
                    extra.m_localScale    = scale;
                }
            }
        }

        static unsafe void WriteLocalPosition(in EntityInHierarchyHandle handle, bool isTicked, float3 position)
        {
            ref var element = ref ((EntityInHierarchy*)handle.m_hierarchy.GetUnsafeReadOnlyPtr())[handle.indexInHierarchy];
            if (isTicked)
                element.m_tickedLocalPosition = position;
            else
                element.m_localPosition = position;

            if (handle.m_extraHierarchy != null)
            {
                ref var extra = ref handle.m_extraHierarchy[handle.indexInHierarchy];
                if (isTicked)
                    extra.m_tickedLocalPosition = position;
                else
                    extra.m_localPosition = position;
            }
        }

        static unsafe void WriteLocalScale(in EntityInHierarchyHandle handle, bool isTicked, float scale)
        {
            ref var element = ref ((EntityInHierarchy*)handle.m_hierarchy.GetUnsafeReadOnlyPtr())[handle.indexInHierarchy];
            if (isTicked)
                element.m_tickedLocalScale = scale;
            else
                element.m_localScale = scale;

            if (handle.m_extraHierarchy != null)
            {
                ref var extra = ref handle.m_extraHierarchy[handle.indexInHierarchy];
                if (isTicked)
                    extra.m_tickedLocalScale = scale;
                else
                    extra.m_localScale = scale;
            }
        }
    }
}
#endif

