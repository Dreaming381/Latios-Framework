#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Transforms.Authoring
{
    public static partial class TransformBakeUtils
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
            if (current == targetSpace)
                return TransformQvvs.identity;

            var original = current;
            GetScaleAndStretch(current.localScale, out var scale, out var stretch);
            var currentQvvs = new TransformQvvs(current.localPosition, current.localRotation, scale, stretch);

            while (current.parent != null && current.parent != targetSpace)
            {
                current = current.parent;
                GetScaleAndStretch(current.localScale, out scale, out stretch);
                var parentQvvs = new TransformQvvs(current.localPosition, current.localRotation, scale, stretch);
                currentQvvs    = qvvs.mul(in parentQvvs, in currentQvvs);
            }

            if (current.parent == targetSpace)
                return currentQvvs;

            GetScaleAndStretch(targetSpace.localScale, out scale, out stretch);
            var targetToWorldQvvs = new TransformQvvs(targetSpace.localPosition, targetSpace.localRotation, scale, stretch);

            while (targetSpace.parent != null && targetSpace != original)
            {
                targetSpace = targetSpace.parent;
                GetScaleAndStretch(targetSpace.localScale, out scale, out stretch);
                var parentQvvs    = new TransformQvvs(targetSpace.localPosition, targetSpace.localRotation, scale, stretch);
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
    }
}
#endif

