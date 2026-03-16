/*using Unity.Entities;

namespace Latios.Transforms.Abstract
{
#pragma warning disable CS0618
    public readonly partial struct LocalTransformQvsReadOnlyAspect : IAspect
#pragma warning restore CS0618
    {
#if !LATIOS_TRANSFORMS_UNITY
        readonly RefRO<Latios.Transforms.LocalTransform> m_localTransform;

        public TransformQvs localTransform => m_localTransform.ValueRO.localTransform;

        public ComponentType componentType => ComponentType.ReadOnly<Latios.Transforms.LocalTransform>();
#elif LATIOS_TRANSFORMS_UNITY
        readonly RefRO<Unity.Transforms.LocalTransform> m_localTransform;

        public TransformQvs localTransform => new TransformQvs(m_localTransform.ValueRO.Position, m_localTransform.ValueRO.Rotation, m_localTransform.ValueRO.Scale);

        public ComponentType componentType => ComponentType.ReadOnly<Unity.Transforms.LocalTransform>();
#endif
    }

#pragma warning disable CS0618
    public readonly partial struct LocalTransformQvvsReadWriteAspect : IAspect
#pragma warning restore CS0618
    {
#if !LATIOS_TRANSFORMS_UNITY
        readonly TransformAspect m_transform;

        public TransformQvvs localTransform
        {
            get => m_transform.localTransformQvvs;
            set => m_transform.localTransformQvvs = value;
        }
#elif LATIOS_TRANSFORMS_UNITY
        readonly RefRW<Unity.Transforms.LocalTransform> m_localTransform;

        public TransformQvvs localTransform
        {
            get => new TransformQvvs(m_localTransform.ValueRO.Position, m_localTransform.ValueRO.Rotation, m_localTransform.ValueRO.Scale, 1f);
            set => m_localTransform.ValueRW = new Unity.Transforms.LocalTransform { Position = value.position, Rotation = value.rotation, Scale = value.scale };
        }
#endif
    }
   }
 */

