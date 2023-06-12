using Unity.Entities;

namespace Latios.Transforms.Abstract
{
    public readonly partial struct ParentReadOnlyAspect : IAspect
    {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        readonly RefRO<Latios.Transforms.Parent> m_parent;

        public Entity parent => m_parent.ValueRO.parent;

        public static ComponentType componentType => ComponentType.ReadOnly<Latios.Transforms.Parent>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        readonly RefRO<Unity.Transforms.Parent> m_parent;

        public Entity parent => m_parent.ValueRO.Value;

        public static ComponentType componentType => ComponentType.ReadOnly<Unity.Transforms.Parent>();
#endif
    }

    public readonly partial struct ParentReadWriteAspect : IAspect
    {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        readonly RefRW<Latios.Transforms.Parent> m_parent;

        public Entity parent
        {
            get => m_parent.ValueRO.parent;
            set => m_parent.ValueRW.parent = value;
        }

        public static ComponentType componentType => ComponentType.ReadWrite<Latios.Transforms.Parent>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        readonly RefRW<Unity.Transforms.Parent> m_parent;

        public Entity parent
        {
            get => m_parent.ValueRO.Value;
            set => m_parent.ValueRW.Value = value;
        }

        public static ComponentType componentType => ComponentType.ReadWrite<Unity.Transforms.Parent>();
#endif
    }
}

