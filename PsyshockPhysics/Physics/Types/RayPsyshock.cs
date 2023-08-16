using Latios.Transforms;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A ray that is used for raycasts. It is better to use a Ray instance when
    /// raycasting multiple elements using the same start and end points.
    /// </summary>
    public struct Ray
    {
        /// <summary>
        /// The start point of the ray
        /// </summary>
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

        /// <summary>
        /// The end point of the ray
        /// </summary>
        public float3 end
        {
            get { return m_origin + m_displacement; }
            set
            {
                m_displacement           = value - m_origin;
                m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float3.zero);
            }
        }

        /// <summary>
        /// The difference between the start and the end point of the ray
        /// </summary>
        public float3 displacement => m_displacement;
        /// <summary>
        /// The reciprocal of the difference between the start and the end point of the ray.
        /// It is useful in various algorithms.
        /// </summary>
        public float3 reciprocalDisplacement => m_reciprocalDisplacement;

        private float3 m_origin;
        private float3 m_displacement;
        private float3 m_reciprocalDisplacement;

        /// <summary>
        /// Create a new Ray
        /// </summary>
        /// <param name="start">The start point of the ray</param>
        /// <param name="end">The end point of the ray</param>
        public Ray(float3 start, float3 end)
        {
            m_origin                 = start;
            m_displacement           = end - start;
            m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float3.zero);
        }

        /// <summary>
        /// Create a new Ray
        /// </summary>
        /// <param name="start">The start point of the ray</param>
        /// <param name="direction">The direction the ray travels. The constructor will normalize this value.</param>
        /// <param name="length">The distance the ray is allowed to travel.</param>
        public Ray(float3 start, float3 direction, float length) : this(start, start + math.normalizesafe(direction) * length)
        {
        }

        /// <summary>
        /// Creates a new Ray by reorienting an existing ray in a new coordinate space
        /// </summary>
        /// <param name="transform">The position and rotation of the old coordinate space relative to the new coordinate space</param>
        /// <param name="ray">The original Ray</param>
        /// <returns>A new Ray in the new coordinate space</returns>
        public static Ray TransformRay(in RigidTransform transform, in Ray ray)
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

        /// <summary>
        /// Creates a new Ray by reorienting an existing ray in a new coordinate space
        /// </summary>
        /// <param name="transform">The QVVS of the old coordinate space relative to the new coordinate space</param>
        /// <param name="ray">The original Ray</param>
        /// <returns>A new Ray in the new coordinate space</returns>
        public static Ray TransformRay(in TransformQvvs transform, in Ray ray)
        {
            var newStart = qvvs.TransformPoint(in transform, ray.start);
            var newEnd   = qvvs.TransformPoint(in transform, ray.end);

            return new Ray(newStart, newEnd);
        }
    }

    /// <summary>
    /// A ray that is used for primitive raycasts in 2D space. It is better to use a Ray2d instance when
    /// raycasting multiple elements using the same start and end points.
    /// </summary>
    public struct Ray2d
    {
        /// <summary>
        /// The start point of the ray
        /// </summary>
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

        /// <summary>
        /// The end point of the ray
        /// </summary>
        public float2 end
        {
            get { return m_origin + m_displacement; }
            set
            {
                m_displacement           = value - m_origin;
                m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float2.zero);
            }
        }

        /// <summary>
        /// The difference between the start and the end point of the ray
        /// </summary>
        public float2 displacement => m_displacement;
        /// <summary>
        /// The reciprocal of the difference between the start and the end point of the ray.
        /// It is useful in various algorithms.
        /// </summary>
        public float2 reciprocalDisplacement => m_reciprocalDisplacement;

        private float2 m_origin;
        private float2 m_displacement;
        private float2 m_reciprocalDisplacement;

        /// <summary>
        /// Create a new Ray2d
        /// </summary>
        /// <param name="start">The start point of the ray</param>
        /// <param name="end">The end point of the ray</param>
        public Ray2d(float2 start, float2 end)
        {
            m_origin                 = start;
            m_displacement           = end - start;
            m_reciprocalDisplacement = math.select(math.rcp(m_displacement), math.sqrt(float.MaxValue), m_displacement == float2.zero);
        }

        /// <summary>
        /// Create a new Ray2d
        /// </summary>
        /// <param name="start">The start point of the ray</param>
        /// <param name="direction">The direction the ray travels. The constructor will normalize this value.</param>
        /// <param name="length">The distance the ray is allowed to travel.</param>
        public Ray2d(float2 start, float2 direction, float length) : this(start, start + math.normalizesafe(direction) * length)
        {
        }
    }
}

