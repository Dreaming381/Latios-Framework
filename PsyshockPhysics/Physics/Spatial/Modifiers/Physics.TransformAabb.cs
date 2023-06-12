using Latios.Transforms;
using Latios.Transforms.Abstract;
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
        /// Gets an Aabb transformed by the TransformQvvs, expanding it if necessary
        /// </summary>
        /// <param name="qvvs">A transform to apply to the Aabb</param>
        /// <param name="localAabb">An Aabb in a local space</param>
        /// <returns>A conservative Aabb around an object in world space that was encapsuled by localAabb in local space</returns>
        public static Aabb TransformAabb(in TransformQvvs qvvs, in Aabb localAabb)
        {
            var newAabb  = localAabb;
            newAabb.min *= qvvs.scale * qvvs.stretch;
            newAabb.max *= qvvs.scale * qvvs.stretch;

            GetCenterExtents(newAabb, out var center, out var extents);
            float3 x = math.rotate(qvvs.rotation, new float3(extents.x, 0, 0));
            float3 y = math.rotate(qvvs.rotation, new float3(0, extents.y, 0));
            float3 z = math.rotate(qvvs.rotation, new float3(0, 0, extents.z));
            extents  = math.abs(x) + math.abs(y) + math.abs(z);
            center   = math.transform(new RigidTransform(qvvs.rotation, qvvs.position), center);

            newAabb.min = center - extents;
            newAabb.max = center + extents;
            return newAabb;
        }

        public static Aabb TransformAabb(in WorldTransformReadOnlyAspect transform, in Aabb localAabb)
        {
            if (transform.isNativeQvvs)
                return TransformAabb(transform.worldTransformQvvs, in localAabb);
            GetCenterExtents(localAabb, out var center, out var extents);
            return TransformAabb(transform.matrix4x4, center, extents);
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

