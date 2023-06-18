#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Transforms.Authoring
{
    // You must add [BakingType] to inheriting types
    [BakingType]
    public interface IRequestPreviousTransform : IComponentData
    {
    }

    // You must add [BakingType] to inheriting types
    [BakingType]
    public interface IRequestTwoAgoTransform : IComponentData
    {
    }

    // You must add [BakingType] to inheriting types
    [BakingType]
    public interface IRequestCopyParentTransform : IComponentData
    {
    }

    public static class TransformBakeUtils
    {
        public static TransformQvvs GetWorldSpaceQvvs(this Transform current)
        {
            GetScaleAndStretch(current.localScale, out var scale, out var stretch);
            var currentQvvs = new TransformQvvs(current.position, current.rotation, scale, stretch);

            while (current.parent != null)
            {
                current = current.parent;
                GetScaleAndStretch(current.localScale, out scale, out stretch);
                var parentQvvs = new TransformQvvs(current.position, current.rotation, scale, stretch);
                currentQvvs    = qvvs.mul(in parentQvvs, in currentQvvs);
            }
            return currentQvvs;
        }

        public static TransformQvvs GetQvvsRelativeTo(this Transform current, Transform targetSpace)
        {
            var original = current;
            GetScaleAndStretch(current.localScale, out var scale, out var stretch);
            var currentQvvs = new TransformQvvs(current.position, current.rotation, scale, stretch);

            while (current != targetSpace && current.parent != null)
            {
                current = current.parent;
                GetScaleAndStretch(current.localScale, out scale, out stretch);
                var parentQvvs = new TransformQvvs(current.position, current.rotation, scale, stretch);
                currentQvvs    = qvvs.mul(in parentQvvs, in currentQvvs);
            }

            if (current.parent == targetSpace)
                return currentQvvs;

            GetScaleAndStretch(targetSpace.localScale, out scale, out stretch);
            var targetToWorldQvvs = new TransformQvvs(targetSpace.position, targetSpace.rotation, scale, stretch);

            while (targetSpace != original && targetSpace.parent != null)
            {
                targetSpace = targetSpace.parent;
                GetScaleAndStretch(targetSpace.localScale, out scale, out stretch);
                var parentQvvs    = new TransformQvvs(targetSpace.position, targetSpace.rotation, scale, stretch);
                targetToWorldQvvs = qvvs.mul(in parentQvvs, in targetToWorldQvvs);
            }

            if (targetSpace == original)
            {
                var qvs = qvvs.inversemul(in targetToWorldQvvs, in TransformQvvs.identity);
                return new TransformQvvs(qvs.position, qvs.rotation, qvs.scale, targetToWorldQvvs.stretch);
            }
            else
            {
                var qvs = qvvs.inversemul(in targetToWorldQvvs, in currentQvvs);
                return new TransformQvvs(qvs.position, qvs.rotation, qvs.scale, currentQvvs.stretch);
            }
        }

        public static void GetScaleAndStretch(float3 localScale, out float scale, out float3 stretch)
        {
            // Todo: Make this configurable?
            bool  isUniformScale  = math.abs(math.cmax(localScale) - math.cmin(localScale)) < math.EPSILON;
            bool  isIdentityScale = isUniformScale && math.abs(1f - localScale.x) < math.EPSILON;
            float uniformScale    = math.select(localScale.x, 1f, isIdentityScale);
            scale                 = math.select(1f, uniformScale, isUniformScale);
            stretch               = math.select(localScale, 1f, isUniformScale);
        }
    }
}
#endif

