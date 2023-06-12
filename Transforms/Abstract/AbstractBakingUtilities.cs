using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Transforms.Authoring.Abstract
{
    public static class AbstractBakingUtilities
    {
        public static TransformQvvs ExtractWorldTransform(UnityEngine.Transform engineTransform)
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            return engineTransform.GetWorldSpaceQvvs();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            return new TransformQvvs(engineTransform.position, engineTransform.rotation);
#else
            throw new System.NotImplementedException();
#endif
        }

        public static TransformQvvs ExtractTransformRelativeTo(UnityEngine.Transform transform, UnityEngine.Transform relativeToTransform)
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            return transform.GetQvvsRelativeTo(relativeToTransform);
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            float4x4 current         = transform.localToWorldMatrix;
            float4x4 relativeInverse = relativeToTransform.worldToLocalMatrix;
            var prod            = math.mul(relativeInverse, current);
            return new TransformQvvs(prod.Translation(), prod.Rotation());
#else
            throw new System.NotImplementedException();
#endif
        }

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        public static Latios.Transforms.LocalTransform CreateLocalTransform(in TransformQvs qvs)
        {
            return new Latios.Transforms.LocalTransform { localTransform = qvs };
        }
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        public static Unity.Transforms.LocalTransform CreateLocalTransform(in TransformQvs qvs)
        {
            return new LocalTransform
            {
                Position = qvs.position,
                Rotation = qvs.rotation,
                Scale    = qvs.scale,
            };
        }
#endif
    }
}

