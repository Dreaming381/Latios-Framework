using Unity.Mathematics;

namespace Latios.Psyshock
{
    public struct Ray
    {
        public float3 start
        {
            get { return m_origin; }
            set
            {
                float3 e                 = end;
                m_origin                 = value;
                m_displacement           = e - value;
                m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float3.zero);
            }
        }

        public float3 end
        {
            get { return m_origin + m_displacement; }
            set
            {
                m_displacement           = value - m_origin;
                m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float3.zero);
            }
        }

        public float3 displacement => m_displacement;
        public float3 reciprocalDisplacement => m_reciprocalDisplacement;

        private float3 m_origin;
        private float3 m_displacement;
        private float3 m_reciprocalDisplacement;

        public Ray(float3 start, float3 end)
        {
            m_origin                 = start;
            m_displacement           = end - start;
            m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float3.zero);
        }

        public Ray(float3 start, float3 direction, float length) : this(start, start + math.normalizesafe(direction) * length)
        {
        }

        public static Ray TransformRay(RigidTransform transform, Ray ray)
        {
            var disp   = math.rotate(transform, ray.m_displacement);
            Ray newRay = new Ray
            {
                m_origin                 = math.transform(transform, ray.m_origin),
                m_displacement           = disp,
                m_reciprocalDisplacement = math.select(math.rcp(disp), math.sqrt(float.MaxValue), disp == float3.zero),
            };
            return newRay;
        }
    }

    public struct Ray2d
    {
        public float2 start
        {
            get { return m_origin; }
            set
            {
                float2 e                 = end;
                m_origin                 = value;
                m_displacement           = e - value;
                m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float2.zero);
            }
        }

        public float2 end
        {
            get { return m_origin + m_displacement; }
            set
            {
                m_displacement           = value - m_origin;
                m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float2.zero);
            }
        }

        public float2 displacement => m_displacement;
        public float2 reciprocalDisplacement => m_reciprocalDisplacement;

        private float2 m_origin;
        private float2 m_displacement;
        private float2 m_reciprocalDisplacement;

        public Ray2d(float2 start, float2 end)
        {
            m_origin                 = start;
            m_displacement           = end - start;
            m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float2.zero);
        }

        public Ray2d(float2 start, float2 direction, float length) : this(start, start + math.normalizesafe(direction) * length)
        {
        }
    }
}

