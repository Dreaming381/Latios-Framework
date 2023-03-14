using Latios.Transforms;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayDispatch
    {
        public static bool DistanceBetween(float3 point, in Collider collider, in TransformQvvs transform, float maxDistance, out PointDistanceResult result)
        {
            var rigidTransform = new RigidTransform(transform.rotation, transform.position);
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    var sphere = collider.m_sphere;
                    Physics.ScaleStretchCollider(ref sphere, transform.scale, transform.stretch);
                    return PointRaySphere.DistanceBetween(point, in sphere, in rigidTransform, maxDistance, out result);
                case ColliderType.Capsule:
                    var capsule = collider.m_capsule;
                    Physics.ScaleStretchCollider(ref capsule, transform.scale, transform.stretch);
                    return PointRayCapsule.DistanceBetween(point, in capsule, in rigidTransform, maxDistance, out result);
                case ColliderType.Box:
                    var box = collider.m_box;
                    Physics.ScaleStretchCollider(ref box, transform.scale, transform.stretch);
                    return PointRayBox.DistanceBetween(point, in box, in rigidTransform, maxDistance, out result);
                case ColliderType.Triangle:
                    var triangle = collider.m_triangle;
                    Physics.ScaleStretchCollider(ref triangle, transform.scale, transform.stretch);
                    return PointRayTriangle.DistanceBetween(point, in triangle, in rigidTransform, maxDistance, out result);
                case ColliderType.Convex:
                    var convex = collider.m_convex;
                    Physics.ScaleStretchCollider(ref convex, transform.scale, transform.stretch);
                    return PointRayConvex.DistanceBetween(point, in convex, in rigidTransform, maxDistance, out result);
                case ColliderType.Compound:
                    var compound = collider.m_compound;
                    Physics.ScaleStretchCollider(ref compound, transform.scale, transform.stretch);
                    return PointRayCompound.DistanceBetween(point, in compound, in rigidTransform, maxDistance, out result);
                default:
                    result = default;
                    return false;
            }
        }

        public static bool DistanceBetween(float3 point, in Collider collider, in RigidTransform transform, float maxDistance, out PointDistanceResult result)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return PointRaySphere.DistanceBetween(point, in collider.m_sphere, in transform, maxDistance, out result);
                case ColliderType.Capsule:
                    return PointRayCapsule.DistanceBetween(point, in collider.m_capsule, in transform, maxDistance, out result);
                case ColliderType.Box:
                    return PointRayBox.DistanceBetween(point, in collider.m_box, in transform, maxDistance, out result);
                case ColliderType.Triangle:
                    return PointRayTriangle.DistanceBetween(point, in collider.m_triangle, in transform, maxDistance, out result);
                case ColliderType.Convex:
                    return PointRayConvex.DistanceBetween(point, in collider.m_convex, in transform, maxDistance, out result);
                case ColliderType.Compound:
                    return PointRayCompound.DistanceBetween(point, in collider.m_compound, in transform, maxDistance, out result);
                default:
                    result = default;
                    return false;
            }
        }

        public static bool Raycast(in Ray ray, in Collider collider, in TransformQvvs transform, out RaycastResult result)
        {
            var rigidTransform = new RigidTransform(transform.rotation, transform.position);
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    var sphere = collider.m_sphere;
                    Physics.ScaleStretchCollider(ref sphere, transform.scale, transform.stretch);
                    return PointRaySphere.Raycast(in ray, in sphere, in rigidTransform, out result);
                case ColliderType.Capsule:
                    var capsule = collider.m_capsule;
                    Physics.ScaleStretchCollider(ref capsule, transform.scale, transform.stretch);
                    return PointRayCapsule.Raycast(in ray, in capsule, in rigidTransform, out result);
                case ColliderType.Box:
                    var box = collider.m_box;
                    Physics.ScaleStretchCollider(ref box, transform.scale, transform.stretch);
                    return PointRayBox.Raycast(in ray, in box, in rigidTransform, out result);
                case ColliderType.Triangle:
                    var triangle = collider.m_triangle;
                    Physics.ScaleStretchCollider(ref triangle, transform.scale, transform.stretch);
                    return PointRayTriangle.Raycast(in ray, in triangle, in rigidTransform, out result);
                case ColliderType.Convex:
                    var convex = collider.m_convex;
                    Physics.ScaleStretchCollider(ref convex, transform.scale, transform.stretch);
                    return PointRayConvex.Raycast(in ray, in convex, in rigidTransform, out result);
                case ColliderType.Compound:
                    var compound = collider.m_compound;
                    Physics.ScaleStretchCollider(ref compound, transform.scale, transform.stretch);
                    return PointRayCompound.Raycast(in ray, in compound, in rigidTransform, out result);
                default:
                    result = default;
                    return false;
            }
        }

        public static bool Raycast(in Ray ray, in Collider collider, in RigidTransform transform, out RaycastResult result)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return PointRaySphere.Raycast(in ray, in collider.m_sphere, in transform, out result);
                case ColliderType.Capsule:
                    return PointRayCapsule.Raycast(in ray, in collider.m_capsule, in transform, out result);
                case ColliderType.Box:
                    return PointRayBox.Raycast(in ray, in collider.m_box, in transform, out result);
                case ColliderType.Triangle:
                    return PointRayTriangle.Raycast(in ray, in collider.m_triangle, in transform, out result);
                case ColliderType.Convex:
                    return PointRayConvex.Raycast(in ray, in collider.m_convex, in transform, out result);
                case ColliderType.Compound:
                    return PointRayCompound.Raycast(in ray, in collider.m_compound, in transform, out result);
                default:
                    result = default;
                    return false;
            }
        }
    }
}

