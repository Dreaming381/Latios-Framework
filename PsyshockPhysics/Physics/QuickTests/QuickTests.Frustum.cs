using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// An optimized structure for performing queries against a view frustum.
    /// </summary>
    public struct FrustumPlanes
    {
        public float4 xa;
        public float4 xb;
        public float4 ya;
        public float4 yb;
        public float4 za;
        public float4 zb;
        public float4 da;
        public float4 db;
        public float4 absXa;
        public float4 absXb;
        public float4 absYa;
        public float4 absYb;
        public float4 absZa;
        public float4 absZb;
    }

    public static partial class QuickTests
    {
        /// <summary>
        /// Converts the result of UnityEngine.GeometryUtility.CalculateFrustumPlanes() to an optimized single structure
        /// </summary>
        public static FrustumPlanes ToOptimized(this UnityEngine.Plane[] enginePlanes)
        {
            FrustumPlanes result = default;
            var           n      = enginePlanes[0].normal;
            result.xa.x          = n.x;
            result.ya.x          = n.y;
            result.za.x          = n.z;
            result.da.x          = enginePlanes[0].distance;

            n           = enginePlanes[1].normal;
            result.xa.y = n.x;
            result.ya.y = n.y;
            result.za.y = n.z;
            result.da.y = enginePlanes[1].distance;

            n           = enginePlanes[2].normal;
            result.xa.z = n.x;
            result.ya.z = n.y;
            result.za.z = n.z;
            result.da.z = enginePlanes[2].distance;

            n           = enginePlanes[3].normal;
            result.xa.w = n.x;
            result.ya.w = n.y;
            result.za.w = n.z;
            result.da.w = enginePlanes[3].distance;

            n           = enginePlanes[4].normal;
            result.xb.x = n.x;
            result.yb.x = n.y;
            result.zb.x = n.z;
            result.db.x = enginePlanes[4].distance;

            n           = enginePlanes[5].normal;
            result.xb.y = n.x;
            result.yb.y = n.y;
            result.zb.y = n.z;
            result.db.y = enginePlanes[5].distance;

            result.xb.zw = result.xb.xy;
            result.yb.zw = result.yb.xy;
            result.zb.zw = result.zb.xy;
            result.db.zw = result.db.xy;

            result.absXa = math.abs(result.xa);
            result.absXb = math.abs(result.xb);
            result.absYa = math.abs(result.ya);
            result.absYb = math.abs(result.yb);
            result.absZa = math.abs(result.za);
            result.absZb = math.abs(result.zb);

            return result;
        }

        /// <summary>
        /// Tests if the AABB is likely intersecting or inside the frustum using the classical frustum culling algorithm.
        /// Note that this algorithm only tests 5 of the 32 possible separating axes, and therefore can produce false positives
        /// when multiple frustum planes intersect the AABB. The algorithm will never produce false negatives.
        /// </summary>
        /// <param name="aabbCenter">The center of the AABB</param>
        /// <param name="aabbExtents">The distance from the AABB's center to the AABB's face along each axis (half-size)</param>
        /// <param name="frustum">The optimized frustum planes for a fast query</param>
        /// <returns>True if the AABB passes the frustum culling test, false if it is definitely outside the frustum</returns>
        public static bool LikelyIntersectsFrustum(float3 aabbCenter, float3 aabbExtents, in FrustumPlanes frustum)
        {
            float4 mx = aabbCenter.xxxx;
            float4 my = aabbCenter.yyyy;
            float4 mz = aabbCenter.zzzz;

            float4 ex = aabbExtents.xxxx;
            float4 ey = aabbExtents.yyyy;
            float4 ez = aabbExtents.zzzz;

            float4 distancesA = mx * frustum.xa + my * frustum.ya + mz * frustum.za + frustum.da;
            float4 distancesB = mx * frustum.xb + my * frustum.yb + mz * frustum.zb + frustum.db;
            float4 radiiA     = ex * frustum.absXa + ey * frustum.absYa + ez * frustum.absZa;
            float4 radiiB     = ex * frustum.absXa + ey * frustum.absYa + ez * frustum.absZa;

            return !math.any((distancesA + radiiA <= 0f) | (distancesB + radiiB <= 0f));
        }

        /// <summary>
        /// Tests if the AABB is likely intersecting or inside the frustum using the classical frustum culling algorithm.
        /// Note that this algorithm only tests 5 of the 32 possible separating axes, and therefore can produce false positives
        /// when multiple frustum planes intersect the AABB. The algorithm will never produce false negatives.
        /// </summary>
        /// <param name="aabb">The AABB to test against the frustum</param>
        /// <param name="frustum">The optimized frustum planes for a fast query</param>
        /// <returns>True if the AABB passes the frustum culling test, false if it is definitely outside the frustum</returns>
        public static bool LikelyIntersectsFrustum(in Aabb aabb, in FrustumPlanes frustum)
        {
            Physics.GetCenterExtents(aabb, out var c, out var e);
            return LikelyIntersectsFrustum(c, e, in frustum);
        }
    }
}

