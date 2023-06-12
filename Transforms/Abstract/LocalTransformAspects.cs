using Unity.Entities;

namespace Latios.Transforms.Abstract
{
    public readonly partial struct LocalTransformReadOnlyAspect : IAspect
    {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        readonly RefRO<Latios.Transforms.LocalTransform> m_localTransform;

        public TransformQvs localTransform => m_localTransform.ValueRO.localTransform;

        public ComponentType componentType => ComponentType.ReadOnly<Latios.Transforms.LocalTransform>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        readonly RefRO<Unity.Transforms.LocalTransform> m_localTransform;

        public TransformQvs localTransform => new TransformQvs(m_localTransform.ValueRO.Position, m_localTransform.ValueRO.Rotation, m_localTransform.ValueRO.Scale);

        public ComponentType componentType => ComponentType.ReadOnly<Unity.Transforms.LocalTransform>();
#endif
    }

    public readonly partial struct LocalTransformReadWriteAspect : IAspect
    {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        readonly RefRW<Latios.Transforms.LocalTransform> m_localTransform;

        public TransformQvs localTransform
        {
            get => m_localTransform.ValueRO.localTransform;
            set => m_localTransform.ValueRW.localTransform = value;
        }

        public ComponentType componentType => ComponentType.ReadWrite<Latios.Transforms.LocalTransform>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        readonly RefRW<Unity.Transforms.LocalTransform> m_localTransform;

        public TransformQvs localTransform
        {
            get => new TransformQvs(m_localTransform.ValueRO.Position, m_localTransform.ValueRO.Rotation, m_localTransform.ValueRO.Scale);
            set => m_localTransform.ValueRW = new Unity.Transforms.LocalTransform { Position = value.position, Rotation = value.rotation, Scale = value.scale };
        }

        public ComponentType componentType => ComponentType.ReadWrite<Unity.Transforms.LocalTransform>();
#endif
    }
}

