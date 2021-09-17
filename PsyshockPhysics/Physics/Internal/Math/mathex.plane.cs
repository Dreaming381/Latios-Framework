using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class mathex
    {
        // Returns the distance from the point to the plane, positive if the point is on the side of
        // the plane on which the plane normal points, zero if the point is on the plane, negative otherwise.
        public static float signedDistance(Plane plane, float3 point)
        {
            return math.dot(plane, point.xyz1());
        }

        public static float3 projectPoint(Plane plane, float3 point)
        {
            return point - plane.normal * signedDistance(plane, point);
        }

        public static Plane planeFrom(float3 relativeOrigin, float3 edgeA, float3 edgeB)
        {
            var normal = math.normalize(math.cross(edgeA, edgeB));
            return new Plane(normal, -math.dot(normal, relativeOrigin));
        }

        public static Plane flip(Plane plane)
        {
            float4 v = plane;
            v        = -v;
            return new Plane(v.xyz, v.w);
        }
    }
}

