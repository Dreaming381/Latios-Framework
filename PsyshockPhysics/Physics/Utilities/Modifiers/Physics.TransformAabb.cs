using Latios;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        public static Aabb TransformAabb(float4x4 transform, float3 center, float3 extents)
        {
            float3 worldCenter  = math.transform(transform, center);
            float3 worldExtents = LatiosMath.RotateExtents(extents, transform.c0.xyz, transform.c1.xyz, transform.c2.xyz);
            return new Aabb(worldCenter - worldExtents, worldCenter + worldExtents);
        }

        public static void GetCenterExtents(Aabb aabb, out float3 center, out float3 extents)
        {
            center  = (aabb.min + aabb.max) / 2f;
            extents = aabb.max - center;
        }
    }
}

