#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY

using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms.Abstract
{
    public readonly partial struct WorldTransformReadOnlyAspect : IAspect, IAbstractWorldTransformReadOnlyAspect
    {
        readonly RefRO<WorldTransform> worldTransform;

        public TransformQvvs worldTransformQvvs => worldTransform.ValueRO.worldTransform;

        public quaternion rotation => worldTransform.ValueRO.rotation;
        public float3 position => worldTransform.ValueRO.position;

        public bool isNativeQvvs => true;
        public float4x4 matrix4x4 => worldTransform.ValueRO.worldTransform.ToMatrix4x4();
    }
}

#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Transforms.Abstract
{
    public readonly partial struct WorldTransformReadOnlyAspect : IAspect, IAbstractWorldTransformReadOnlyAspect
    {
        readonly RefRO<LocalToWorld> localToWorld;

        public TransformQvvs worldTransformQvvs
        {
            get
            {
                ref readonly float4x4 ltw = ref localToWorld.ValueRO.Value;
                return new TransformQvvs(ltw.Translation(), ltw.Rotation());
            }
        }

        public quaternion rotation => localToWorld.ValueRO.Rotation;
        public float3 position => localToWorld.ValueRO.Position;

        public bool isNativeQvvs => false;
        public float4x4 matrix4x4 => localToWorld.ValueRO.Value;
    }
}
#endif

namespace Latios.Transforms.Abstract
{
    // This helps detect if the implementations are in-sync with utility methods.
    public interface IAbstractWorldTransformReadOnlyAspect
    {
        public TransformQvvs worldTransformQvvs { get; }

        public quaternion rotation { get; }
        public float3 position { get; }

        public bool isNativeQvvs { get; }
        public float4x4 matrix4x4 {  get; }
    }

    public static class QueryExtensions
    {
        public static FluentQuery WithWorldTransformReadOnlyAspectWeak(this FluentQuery query)
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            return query.WithAllWeak<WorldTransform>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            return query.WithAllWeak<LocalToWorld>();
#else
            throw new System.NotImplementedException();
#endif
        }

        public static void AddWorldTranformChangeFilter(this EntityQuery query)
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            query.AddChangedVersionFilter(ComponentType.ReadOnly<WorldTransform>());
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            query.AddChangedVersionFilter(ComponentType.ReadOnly<LocalToWorld>());
#else
            throw new System.NotImplementedException();
#endif
        }

        public static ComponentType GetAbstractWorldTransformROComponentType()
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            return ComponentType.ReadOnly<WorldTransform>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            return ComponentType.ReadOnly<LocalToWorld>();
#else
            throw new System.NotImplementedException();
#endif
        }

        public static ComponentType GetAbstractWorldTransformRWComponentType()
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            return ComponentType.ReadWrite<WorldTransform>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            return ComponentType.ReadWrite<LocalToWorld>();
#else
            throw new System.NotImplementedException();
#endif
        }
    }
}

