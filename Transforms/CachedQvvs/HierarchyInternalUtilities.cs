#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static class HierarchyInternalUtilities
    {
        public static void UpdateTransform(ref TransformQvvs worldTransform, ref TransformQvs localTransform, in TransformQvvs parentTransform, HierarchyUpdateMode.Flags flags)
        {
            if (flags == HierarchyUpdateMode.Flags.Normal)
            {
                qvvs.mul(ref worldTransform, in parentTransform, in localTransform);
                return;
            }
            if (flags == HierarchyUpdateMode.Flags.WorldAll)
            {
                localTransform = qvvs.inversemul(in parentTransform, in worldTransform);
                return;
            }

            var originalWorldTransform = worldTransform;
            qvvs.mul(ref worldTransform, in parentTransform, in localTransform);

            if ((flags & HierarchyUpdateMode.Flags.WorldRotation) == HierarchyUpdateMode.Flags.WorldRotation)
                worldTransform.rotation = originalWorldTransform.rotation;
            else if ((flags & HierarchyUpdateMode.Flags.WorldRotation) != HierarchyUpdateMode.Flags.Normal)
                worldTransform.rotation = ComputeMixedRotation(originalWorldTransform.rotation, worldTransform.rotation, flags);
            if ((flags & HierarchyUpdateMode.Flags.WorldX) == HierarchyUpdateMode.Flags.WorldX)
                worldTransform.position.x = originalWorldTransform.position.x;
            if ((flags & HierarchyUpdateMode.Flags.WorldY) == HierarchyUpdateMode.Flags.WorldY)
                worldTransform.position.y = originalWorldTransform.position.y;
            if ((flags & HierarchyUpdateMode.Flags.WorldZ) == HierarchyUpdateMode.Flags.WorldZ)
                worldTransform.position.z = originalWorldTransform.position.z;
            if ((flags & HierarchyUpdateMode.Flags.WorldScale) == HierarchyUpdateMode.Flags.WorldScale)
                worldTransform.scale = originalWorldTransform.scale;

            localTransform = qvvs.inversemul(in parentTransform, in worldTransform);
        }

        static quaternion ComputeMixedRotation(quaternion originalWorldRotation, quaternion hierarchyWorldRotation, HierarchyUpdateMode.Flags flags)
        {
            var forward = math.select(math.forward(hierarchyWorldRotation),
                                      math.forward(originalWorldRotation),
                                      (flags & HierarchyUpdateMode.Flags.WorldForward) == HierarchyUpdateMode.Flags.WorldForward);
            var up = math.select(math.rotate(hierarchyWorldRotation, math.up()),
                                 math.rotate(originalWorldRotation, math.up()),
                                 (flags & HierarchyUpdateMode.Flags.WorldUp) == HierarchyUpdateMode.Flags.WorldUp);

            if ((flags & HierarchyUpdateMode.Flags.StrictUp) == HierarchyUpdateMode.Flags.StrictUp)
            {
                float3 right = math.normalizesafe(math.cross(up, forward), float3.zero);
                if (right.Equals(float3.zero))
                    return math.select(hierarchyWorldRotation.value, originalWorldRotation.value, (flags & HierarchyUpdateMode.Flags.WorldUp) == HierarchyUpdateMode.Flags.WorldUp);
                var newForward = math.cross(right, up);
                return new quaternion(new float3x3(right, up, newForward));
            }
            else
            {
                float3 right = math.normalizesafe(math.cross(up, forward), float3.zero);
                if (right.Equals(float3.zero))
                    return math.select(hierarchyWorldRotation.value,
                                       originalWorldRotation.value,
                                       (flags & HierarchyUpdateMode.Flags.WorldForward) == HierarchyUpdateMode.Flags.WorldForward);
                var newUp = math.cross(forward, right);
                return new quaternion(new float3x3(right, newUp, forward));
            }
        }
    }
}
#endif

