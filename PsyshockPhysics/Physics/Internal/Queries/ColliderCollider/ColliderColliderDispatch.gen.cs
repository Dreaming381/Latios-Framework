using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class ColliderColliderDispatch
    {
        public static bool DistanceBetween(in Collider colliderA,
                                           in RigidTransform aTransform,
                                           in Collider colliderB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            switch ((colliderA.type, colliderB.type))
            {
                case (ColliderType.Sphere, ColliderType.Sphere):
                    return SphereSphere.DistanceBetween(in colliderA.m_sphere, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Sphere, ColliderType.Capsule):
                {
                    var r = SphereCapsule.DistanceBetween(in colliderB.m_capsule, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Sphere, ColliderType.Box):
                {
                    var r = SphereBox.DistanceBetween(in colliderB.m_box, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Sphere, ColliderType.Triangle):
                {
                    var r = SphereTriangle.DistanceBetween(in colliderB.m_triangle, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Sphere, ColliderType.Convex):
                {
                    var r = SphereConvex.DistanceBetween(in colliderB.m_convex, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Sphere, ColliderType.TriMesh):
                {
                    var r = SphereTriMesh.DistanceBetween(in colliderB.m_triMesh, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Sphere, ColliderType.Compound):
                {
                    var r = SphereCompound.DistanceBetween(in colliderB.m_compound, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Capsule, ColliderType.Sphere):
                    return SphereCapsule.DistanceBetween(in colliderA.m_capsule, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Capsule, ColliderType.Capsule):
                    return CapsuleCapsule.DistanceBetween(in colliderA.m_capsule, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                case (ColliderType.Capsule, ColliderType.Box):
                {
                    var r = CapsuleBox.DistanceBetween(in colliderB.m_box, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Capsule, ColliderType.Triangle):
                {
                    var r = CapsuleTriangle.DistanceBetween(in colliderB.m_triangle, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Capsule, ColliderType.Convex):
                {
                    var r = CapsuleConvex.DistanceBetween(in colliderB.m_convex, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Capsule, ColliderType.TriMesh):
                {
                    var r = CapsuleTriMesh.DistanceBetween(in colliderB.m_triMesh, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Capsule, ColliderType.Compound):
                {
                    var r = CapsuleCompound.DistanceBetween(in colliderB.m_compound, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Box, ColliderType.Sphere):
                    return SphereBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Box, ColliderType.Capsule):
                    return CapsuleBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                case (ColliderType.Box, ColliderType.Box):
                    return BoxBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                case (ColliderType.Box, ColliderType.Triangle):
                {
                    var r = BoxTriangle.DistanceBetween(in colliderB.m_triangle, in bTransform, in colliderA.m_box, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Box, ColliderType.Convex):
                {
                    var r = BoxConvex.DistanceBetween(in colliderB.m_convex, in bTransform, in colliderA.m_box, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Box, ColliderType.TriMesh):
                {
                    var r = BoxTriMesh.DistanceBetween(in colliderB.m_triMesh, in bTransform, in colliderA.m_box, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Box, ColliderType.Compound):
                {
                    var r = BoxCompound.DistanceBetween(in colliderB.m_compound, in bTransform, in colliderA.m_box, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Triangle, ColliderType.Sphere):
                    return SphereTriangle.DistanceBetween(in colliderA.m_triangle, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Triangle, ColliderType.Capsule):
                    return CapsuleTriangle.DistanceBetween(in colliderA.m_triangle, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                case (ColliderType.Triangle, ColliderType.Box):
                    return BoxTriangle.DistanceBetween(in colliderA.m_triangle, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                case (ColliderType.Triangle, ColliderType.Triangle):
                    return TriangleTriangle.DistanceBetween(in colliderA.m_triangle, in aTransform, in colliderB.m_triangle, in bTransform, maxDistance, out result);
                case (ColliderType.Triangle, ColliderType.Convex):
                {
                    var r = TriangleConvex.DistanceBetween(in colliderB.m_convex, in bTransform, in colliderA.m_triangle, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Triangle, ColliderType.TriMesh):
                {
                    var r = TriangleTriMesh.DistanceBetween(in colliderB.m_triMesh, in bTransform, in colliderA.m_triangle, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Triangle, ColliderType.Compound):
                {
                    var r = TriangleCompound.DistanceBetween(in colliderB.m_compound, in bTransform, in colliderA.m_triangle, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Convex, ColliderType.Sphere):
                    return SphereConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Convex, ColliderType.Capsule):
                    return CapsuleConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                case (ColliderType.Convex, ColliderType.Box):
                    return BoxConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                case (ColliderType.Convex, ColliderType.Triangle):
                    return TriangleConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_triangle, in bTransform, maxDistance, out result);
                case (ColliderType.Convex, ColliderType.Convex):
                    return ConvexConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_convex, in bTransform, maxDistance, out result);
                case (ColliderType.Convex, ColliderType.TriMesh):
                {
                    var r = ConvexTriMesh.DistanceBetween(in colliderB.m_triMesh, in bTransform, in colliderA.m_convex, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Convex, ColliderType.Compound):
                {
                    var r = ConvexCompound.DistanceBetween(in colliderB.m_compound, in bTransform, in colliderA.m_convex, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.TriMesh, ColliderType.Sphere):
                    return SphereTriMesh.DistanceBetween(in colliderA.m_triMesh, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.TriMesh, ColliderType.Capsule):
                    return CapsuleTriMesh.DistanceBetween(in colliderA.m_triMesh, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                case (ColliderType.TriMesh, ColliderType.Box):
                    return BoxTriMesh.DistanceBetween(in colliderA.m_triMesh, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                case (ColliderType.TriMesh, ColliderType.Triangle):
                    return TriangleTriMesh.DistanceBetween(in colliderA.m_triMesh, in aTransform, in colliderB.m_triangle, in bTransform, maxDistance, out result);
                case (ColliderType.TriMesh, ColliderType.Convex):
                    return ConvexTriMesh.DistanceBetween(in colliderA.m_triMesh, in aTransform, in colliderB.m_convex, in bTransform, maxDistance, out result);
                case (ColliderType.TriMesh, ColliderType.TriMesh):
                    return TriMeshTriMesh.DistanceBetween(in colliderA.m_triMesh, in aTransform, in colliderB.m_triMesh, in bTransform, maxDistance, out result);
                case (ColliderType.TriMesh, ColliderType.Compound):
                {
                    var r = TriMeshCompound.DistanceBetween(in colliderB.m_compound, in bTransform, in colliderA.m_triMesh, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    return r;
                }
                case (ColliderType.Compound, ColliderType.Sphere):
                    return SphereCompound.DistanceBetween(in colliderA.m_compound, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Compound, ColliderType.Capsule):
                    return CapsuleCompound.DistanceBetween(in colliderA.m_compound, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                case (ColliderType.Compound, ColliderType.Box):
                    return BoxCompound.DistanceBetween(in colliderA.m_compound, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                case (ColliderType.Compound, ColliderType.Triangle):
                    return TriangleCompound.DistanceBetween(in colliderA.m_compound, in aTransform, in colliderB.m_triangle, in bTransform, maxDistance, out result);
                case (ColliderType.Compound, ColliderType.Convex):
                    return ConvexCompound.DistanceBetween(in colliderA.m_compound, in aTransform, in colliderB.m_convex, in bTransform, maxDistance, out result);
                case (ColliderType.Compound, ColliderType.TriMesh):
                    return TriMeshCompound.DistanceBetween(in colliderA.m_compound, in aTransform, in colliderB.m_triMesh, in bTransform, maxDistance, out result);
                case (ColliderType.Compound, ColliderType.Compound):
                    return CompoundCompound.DistanceBetween(in colliderA.m_compound, in aTransform, in colliderB.m_compound, in bTransform, maxDistance, out result);
                default:
                    result = default;
                    return false;
            }
        }

        unsafe struct DistanceAllResultFlipper<T> : Physics.IDistanceBetweenAllProcessor where T : unmanaged, Physics.IDistanceBetweenAllProcessor
        {
            public T* processor;

            public void Execute(in ColliderDistanceResult result)
            {
                var result2                                            = result;
                (result2.hitpointA, result2.hitpointB)                 = (result.hitpointB, result.hitpointA);
                (result2.normalA, result2.normalB)                     = (result.normalB, result.normalA);
                (result2.subColliderIndexA, result2.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                (result2.featureCodeA, result2.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                processor->Execute(in result2);
            }
        }

        public static unsafe void DistanceBetweenAll<T>(in Collider colliderA,
                                                        in RigidTransform aTransform,
                                                        in Collider colliderB,
                                                        in RigidTransform bTransform,
                                                        float maxDistance,
                                                        ref T processor) where T : unmanaged, Physics.IDistanceBetweenAllProcessor
        {
            var                    flipper = new DistanceAllResultFlipper<T> { processor = (T*)UnsafeUtility.AddressOf(ref processor) };
            ColliderDistanceResult result                                                = default;

            switch ((colliderA.type, colliderB.type))
            {
                case (ColliderType.Sphere, ColliderType.Sphere):
                {
                    SphereSphere.DistanceBetween(in colliderA.m_sphere, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Sphere, ColliderType.Capsule):
                {
                    SphereCapsule.DistanceBetween(in colliderB.m_capsule, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Sphere, ColliderType.Box):
                {
                    SphereBox.DistanceBetween(in colliderB.m_box, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Sphere, ColliderType.Triangle):
                {
                    SphereTriangle.DistanceBetween(in colliderB.m_triangle, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Sphere, ColliderType.Convex):
                {
                    SphereConvex.DistanceBetween(in colliderB.m_convex, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Sphere, ColliderType.TriMesh):
                    SphereTriMesh.DistanceBetweenAll(in colliderB.m_triMesh, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Sphere, ColliderType.Compound):
                    SphereCompound.DistanceBetweenAll(in colliderB.m_compound, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Capsule, ColliderType.Sphere):
                {
                    SphereCapsule.DistanceBetween(in colliderA.m_capsule, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Capsule, ColliderType.Capsule):
                {
                    CapsuleCapsule.DistanceBetween(in colliderA.m_capsule, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Capsule, ColliderType.Box):
                {
                    CapsuleBox.DistanceBetween(in colliderB.m_box, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Capsule, ColliderType.Triangle):
                {
                    CapsuleTriangle.DistanceBetween(in colliderB.m_triangle, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Capsule, ColliderType.Convex):
                {
                    CapsuleConvex.DistanceBetween(in colliderB.m_convex, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Capsule, ColliderType.TriMesh):
                    CapsuleTriMesh.DistanceBetweenAll(in colliderB.m_triMesh, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Capsule, ColliderType.Compound):
                    CapsuleCompound.DistanceBetweenAll(in colliderB.m_compound, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Box, ColliderType.Sphere):
                {
                    SphereBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Box, ColliderType.Capsule):
                {
                    CapsuleBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Box, ColliderType.Box):
                {
                    BoxBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Box, ColliderType.Triangle):
                {
                    BoxTriangle.DistanceBetween(in colliderB.m_triangle, in bTransform, in colliderA.m_box, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Box, ColliderType.Convex):
                {
                    BoxConvex.DistanceBetween(in colliderB.m_convex, in bTransform, in colliderA.m_box, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Box, ColliderType.TriMesh):
                    BoxTriMesh.DistanceBetweenAll(in colliderB.m_triMesh, in bTransform, in colliderA.m_box, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Box, ColliderType.Compound):
                    BoxCompound.DistanceBetweenAll(in colliderB.m_compound, in bTransform, in colliderA.m_box, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Triangle, ColliderType.Sphere):
                {
                    SphereTriangle.DistanceBetween(in colliderA.m_triangle, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Triangle, ColliderType.Capsule):
                {
                    CapsuleTriangle.DistanceBetween(in colliderA.m_triangle, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Triangle, ColliderType.Box):
                {
                    BoxTriangle.DistanceBetween(in colliderA.m_triangle, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Triangle, ColliderType.Triangle):
                {
                    TriangleTriangle.DistanceBetween(in colliderA.m_triangle, in aTransform, in colliderB.m_triangle, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Triangle, ColliderType.Convex):
                {
                    TriangleConvex.DistanceBetween(in colliderB.m_convex, in bTransform, in colliderA.m_triangle, in aTransform, maxDistance, out result);

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    (result.featureCodeA, result.featureCodeB)           = (result.featureCodeB, result.featureCodeA);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Triangle, ColliderType.TriMesh):
                    TriangleTriMesh.DistanceBetweenAll(in colliderB.m_triMesh, in bTransform, in colliderA.m_triangle, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Triangle, ColliderType.Compound):
                    TriangleCompound.DistanceBetweenAll(in colliderB.m_compound, in bTransform, in colliderA.m_triangle, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Convex, ColliderType.Sphere):
                {
                    SphereConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Convex, ColliderType.Capsule):
                {
                    CapsuleConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Convex, ColliderType.Box):
                {
                    BoxConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Convex, ColliderType.Triangle):
                {
                    TriangleConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_triangle, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Convex, ColliderType.Convex):
                {
                    ConvexConvex.DistanceBetween(in colliderA.m_convex, in aTransform, in colliderB.m_convex, in bTransform, maxDistance, out result);
                    processor.Execute(in result);
                    break;
                }
                case (ColliderType.Convex, ColliderType.TriMesh):
                    ConvexTriMesh.DistanceBetweenAll(in colliderB.m_triMesh, in bTransform, in colliderA.m_convex, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Convex, ColliderType.Compound):
                    ConvexCompound.DistanceBetweenAll(in colliderB.m_compound, in bTransform, in colliderA.m_convex, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.TriMesh, ColliderType.Sphere):
                    SphereTriMesh.DistanceBetweenAll(in colliderA.m_triMesh, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.TriMesh, ColliderType.Capsule):
                    CapsuleTriMesh.DistanceBetweenAll(in colliderA.m_triMesh, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.TriMesh, ColliderType.Box):
                    BoxTriMesh.DistanceBetweenAll(in colliderA.m_triMesh, in aTransform, in colliderB.m_box, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.TriMesh, ColliderType.Triangle):
                    TriangleTriMesh.DistanceBetweenAll(in colliderA.m_triMesh, in aTransform, in colliderB.m_triangle, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.TriMesh, ColliderType.Convex):
                    ConvexTriMesh.DistanceBetweenAll(in colliderA.m_triMesh, in aTransform, in colliderB.m_convex, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.TriMesh, ColliderType.TriMesh):
                    TriMeshTriMesh.DistanceBetweenAll(in colliderA.m_triMesh, in aTransform, in colliderB.m_triMesh, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.TriMesh, ColliderType.Compound):
                    TriMeshCompound.DistanceBetweenAll(in colliderB.m_compound, in bTransform, in colliderA.m_triMesh, in aTransform, maxDistance, ref flipper);
                    break;
                case (ColliderType.Compound, ColliderType.Sphere):
                    SphereCompound.DistanceBetweenAll(in colliderA.m_compound, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.Compound, ColliderType.Capsule):
                    CapsuleCompound.DistanceBetweenAll(in colliderA.m_compound, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.Compound, ColliderType.Box):
                    BoxCompound.DistanceBetweenAll(in colliderA.m_compound, in aTransform, in colliderB.m_box, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.Compound, ColliderType.Triangle):
                    TriangleCompound.DistanceBetweenAll(in colliderA.m_compound, in aTransform, in colliderB.m_triangle, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.Compound, ColliderType.Convex):
                    ConvexCompound.DistanceBetweenAll(in colliderA.m_compound, in aTransform, in colliderB.m_convex, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.Compound, ColliderType.TriMesh):
                    TriMeshCompound.DistanceBetweenAll(in colliderA.m_compound, in aTransform, in colliderB.m_triMesh, in bTransform, maxDistance, ref processor);
                    break;
                case (ColliderType.Compound, ColliderType.Compound):
                    CompoundCompound.DistanceBetweenAll(in colliderA.m_compound, in aTransform, in colliderB.m_compound, in bTransform, maxDistance, ref processor);
                    break;
            }
        }

        public static bool ColliderCast(in Collider colliderToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in Collider target,
                                        in RigidTransform targetTransform,
                                        out ColliderCastResult result)
        {
            switch ((colliderToCast.type, target.type))
            {
                case (ColliderType.Sphere, ColliderType.Sphere):
                    return SphereSphere.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Sphere, ColliderType.Capsule):
                    return SphereCapsule.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Sphere, ColliderType.Box):
                    return SphereBox.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.Sphere, ColliderType.Triangle):
                    return SphereTriangle.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_triangle, in targetTransform, out result);
                case (ColliderType.Sphere, ColliderType.Convex):
                    return SphereConvex.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_convex, in targetTransform, out result);
                case (ColliderType.Sphere, ColliderType.TriMesh):
                    return SphereTriMesh.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_triMesh, in targetTransform, out result);
                case (ColliderType.Sphere, ColliderType.Compound):
                    return SphereCompound.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_compound, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Sphere):
                    return SphereCapsule.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Capsule):
                    return CapsuleCapsule.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Box):
                    return CapsuleBox.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Triangle):
                    return CapsuleTriangle.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_triangle, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Convex):
                    return CapsuleConvex.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_convex, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.TriMesh):
                    return CapsuleTriMesh.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_triMesh, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Compound):
                    return CapsuleCompound.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_compound, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Sphere):
                    return SphereBox.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Capsule):
                    return CapsuleBox.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Box):
                    return BoxBox.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Triangle):
                    return BoxTriangle.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_triangle, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Convex):
                    return BoxConvex.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_convex, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.TriMesh):
                    return BoxTriMesh.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_triMesh, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Compound):
                    return BoxCompound.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_compound, in targetTransform, out result);
                case (ColliderType.Triangle, ColliderType.Sphere):
                    return SphereTriangle.ColliderCast(in colliderToCast.m_triangle, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Triangle, ColliderType.Capsule):
                    return CapsuleTriangle.ColliderCast(in colliderToCast.m_triangle, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Triangle, ColliderType.Box):
                    return BoxTriangle.ColliderCast(in colliderToCast.m_triangle, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.Triangle, ColliderType.Triangle):
                    return TriangleTriangle.ColliderCast(in colliderToCast.m_triangle, in castStart, castEnd, in target.m_triangle, in targetTransform, out result);
                case (ColliderType.Triangle, ColliderType.Convex):
                    return TriangleConvex.ColliderCast(in colliderToCast.m_triangle, in castStart, castEnd, in target.m_convex, in targetTransform, out result);
                case (ColliderType.Triangle, ColliderType.TriMesh):
                    return TriangleTriMesh.ColliderCast(in colliderToCast.m_triangle, in castStart, castEnd, in target.m_triMesh, in targetTransform, out result);
                case (ColliderType.Triangle, ColliderType.Compound):
                    return TriangleCompound.ColliderCast(in colliderToCast.m_triangle, in castStart, castEnd, in target.m_compound, in targetTransform, out result);
                case (ColliderType.Convex, ColliderType.Sphere):
                    return SphereConvex.ColliderCast(in colliderToCast.m_convex, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Convex, ColliderType.Capsule):
                    return CapsuleConvex.ColliderCast(in colliderToCast.m_convex, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Convex, ColliderType.Box):
                    return BoxConvex.ColliderCast(in colliderToCast.m_convex, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.Convex, ColliderType.Triangle):
                    return TriangleConvex.ColliderCast(in colliderToCast.m_convex, in castStart, castEnd, in target.m_triangle, in targetTransform, out result);
                case (ColliderType.Convex, ColliderType.Convex):
                    return ConvexConvex.ColliderCast(in colliderToCast.m_convex, in castStart, castEnd, in target.m_convex, in targetTransform, out result);
                case (ColliderType.Convex, ColliderType.TriMesh):
                    return ConvexTriMesh.ColliderCast(in colliderToCast.m_convex, in castStart, castEnd, in target.m_triMesh, in targetTransform, out result);
                case (ColliderType.Convex, ColliderType.Compound):
                    return ConvexCompound.ColliderCast(in colliderToCast.m_convex, in castStart, castEnd, in target.m_compound, in targetTransform, out result);
                case (ColliderType.TriMesh, ColliderType.Sphere):
                    return SphereTriMesh.ColliderCast(in colliderToCast.m_triMesh, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.TriMesh, ColliderType.Capsule):
                    return CapsuleTriMesh.ColliderCast(in colliderToCast.m_triMesh, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.TriMesh, ColliderType.Box):
                    return BoxTriMesh.ColliderCast(in colliderToCast.m_triMesh, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.TriMesh, ColliderType.Triangle):
                    return TriangleTriMesh.ColliderCast(in colliderToCast.m_triMesh, in castStart, castEnd, in target.m_triangle, in targetTransform, out result);
                case (ColliderType.TriMesh, ColliderType.Convex):
                    return ConvexTriMesh.ColliderCast(in colliderToCast.m_triMesh, in castStart, castEnd, in target.m_convex, in targetTransform, out result);
                case (ColliderType.TriMesh, ColliderType.TriMesh):
                    return TriMeshTriMesh.ColliderCast(in colliderToCast.m_triMesh, in castStart, castEnd, in target.m_triMesh, in targetTransform, out result);
                case (ColliderType.TriMesh, ColliderType.Compound):
                    return TriMeshCompound.ColliderCast(in colliderToCast.m_triMesh, in castStart, castEnd, in target.m_compound, in targetTransform, out result);
                case (ColliderType.Compound, ColliderType.Sphere):
                    return SphereCompound.ColliderCast(in colliderToCast.m_compound, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Compound, ColliderType.Capsule):
                    return CapsuleCompound.ColliderCast(in colliderToCast.m_compound, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Compound, ColliderType.Box):
                    return BoxCompound.ColliderCast(in colliderToCast.m_compound, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.Compound, ColliderType.Triangle):
                    return TriangleCompound.ColliderCast(in colliderToCast.m_compound, in castStart, castEnd, in target.m_triangle, in targetTransform, out result);
                case (ColliderType.Compound, ColliderType.Convex):
                    return ConvexCompound.ColliderCast(in colliderToCast.m_compound, in castStart, castEnd, in target.m_convex, in targetTransform, out result);
                case (ColliderType.Compound, ColliderType.TriMesh):
                    return TriMeshCompound.ColliderCast(in colliderToCast.m_compound, in castStart, castEnd, in target.m_triMesh, in targetTransform, out result);
                case (ColliderType.Compound, ColliderType.Compound):
                    return CompoundCompound.ColliderCast(in colliderToCast.m_compound, in castStart, castEnd, in target.m_compound, in targetTransform, out result);
                default:
                    result = default;
                    return false;
            }
        }
    }
}

