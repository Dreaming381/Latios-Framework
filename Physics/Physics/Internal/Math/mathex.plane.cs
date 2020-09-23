using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    internal static partial class mathex
    {
        //Positive if point is on front side of plane
        public static float signedDistance(Plane plane, float3 point)
        {
            return math.dot(plane, point.xyz1());
        }

        public static float3 projectPoint(Plane plane, float3 point)
        {
            return point - plane.normal * signedDistance(plane, point);
        }
    }
}

