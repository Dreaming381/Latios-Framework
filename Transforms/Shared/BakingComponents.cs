using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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

    public static partial class TransformBakeUtils
    {
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

