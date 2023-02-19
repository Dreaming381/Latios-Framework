using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        /// <summary>
        /// Gets an Aabb from transforming a center and extents representation by a matrix transform
        /// </summary>
        /// <param name="transform">The matrix transform to apply to the center and extents representation</param>
        /// <param name="center">The center of some bounds, prior to transformation</param>
        /// <param name="extents">The extents of some bounds, prior to transformation</param>
        /// <returns></returns>
        public static Aabb TransformAabb(float4x4 transform, float3 center, float3 extents)
        {
            float3 worldCenter  = math.transform(transform, center);
            float3 worldExtents = LatiosMath.RotateExtents(extents, transform.c0.xyz, transform.c1.xyz, transform.c2.xyz);
            return new Aabb(worldCenter - worldExtents, worldCenter + worldExtents);
        }

        /// <summary>
        /// Gets a center and extents representation of the Aabb
        /// </summary>
        /// <param name="aabb">The Aabb to get the alternate representation of</param>
        /// <param name="center">The center of the Aabb</param>
        /// <param name="extents">The extents of the Aabb</param>
        public static void GetCenterExtents(Aabb aabb, out float3 center, out float3 extents)
        {
            center  = (aabb.min + aabb.max) / 2f;
            extents = aabb.max - center;
        }
    }
}

