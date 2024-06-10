using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayCapsule
    {
        public static bool DistanceBetween(float3 point, in CapsuleCollider capsule, in RigidTransform capsuleTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInCapSpace = math.transform(math.inverse(capsuleTransform), point);
            bool hit             = PointCapsuleDistance(pointInCapSpace, in capsule, maxDistance, out var localResult);
            result               = new PointDistanceResult
            {
                hitpoint = math.transform(capsuleTransform, localResult.hitpoint),
                normal   = math.rotate(capsuleTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool Raycast(in Ray ray, in CapsuleCollider capsule, in RigidTransform capsuleTransform, out RaycastResult result)
        {
            var  rayInCapsuleSpace  = Ray.TransformRay(math.inverse(capsuleTransform), ray);
            bool hit                = RaycastCapsule(rayInCapsuleSpace, capsule, out float fraction, out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(capsuleTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        internal static bool PointCapsuleDistance(float3 point, in CapsuleCollider capsule, float maxDistance, out PointDistanceResultInternal result)
        {
            //Strategy: Project p onto the capsule's line clamped to the segment. Then inflate point on line as sphere
            float3 edge                   = capsule.pointB - capsule.pointA;
            float3 ap                     = point - capsule.pointA;
            float  dot                    = math.dot(ap, edge);
            float  edgeLengthSq           = math.lengthsq(edge);
            dot                           = math.clamp(dot, 0f, edgeLengthSq);
            float3         pointOnSegment = capsule.pointA + edge * dot / edgeLengthSq;
            SphereCollider sphere         = new SphereCollider(pointOnSegment, capsule.radius);
            var            hit            = PointRaySphere.PointSphereDistance(point, in sphere, maxDistance, out result, out bool degenerate);

            result.featureCode = 0x4000;
            result.featureCode = (ushort)math.select(result.featureCode, 0, dot == 0f);
            result.featureCode = (ushort)math.select(result.featureCode, 1, dot == edgeLengthSq);
            if (Hint.Likely(!degenerate))
                return hit;

            if (math.all(edge == 0f))
                return hit;

            mathex.GetDualPerpendicularNormalized(edge, out var capsuleNormal, out _);
            result.normal   = capsuleNormal;
            result.hitpoint = pointOnSegment;
            result.distance = 0f;
            return hit;
        }

        internal static bool RaycastCapsule(in Ray ray, in CapsuleCollider capsule, out float fraction, out float3 normal)
        {
            float          axisLength = mathex.GetLengthAndNormal(capsule.pointB - capsule.pointA, out float3 axis);
            SphereCollider sphere1    = new SphereCollider(capsule.pointA, capsule.radius);

            // Ray vs infinite cylinder
            {
                float  directionDotAxis  = math.dot(ray.displacement, axis);
                float  originDotAxis     = math.dot(ray.start - capsule.pointA, axis);
                float3 rayDisplacement2D = ray.displacement - axis * directionDotAxis;
                float3 rayOrigin2D       = ray.start - axis * originDotAxis;
                Ray    rayIn2d           = new Ray(rayOrigin2D, rayOrigin2D + rayDisplacement2D);

                if (PointRaySphere.RaycastSphere(in rayIn2d, in sphere1, out float cylinderFraction, out normal))
                {
                    float t = originDotAxis + cylinderFraction * directionDotAxis;  // distance of the hit from pointA along axis
                    if (t >= 0.0f && t <= axisLength)
                    {
                        fraction = cylinderFraction;
                        return true;
                    }
                }
            }

            //Ray vs caps
            SphereCollider sphere2 = new SphereCollider(capsule.pointB, capsule.radius);
            bool           hit1    = PointRaySphere.RaycastSphere(in ray, in sphere1, out float fraction1, out float3 normal1);
            bool           hit2    = PointRaySphere.RaycastSphere(in ray, in sphere2, out float fraction2, out float3 normal2);
            fraction1              = math.select(2f, fraction1, hit1);
            fraction2              = math.select(2f, fraction2, hit2);
            fraction               = math.select(fraction2, fraction1, fraction1 < fraction2);
            normal                 = math.select(normal2, normal1, fraction1 < fraction2);
            return hit1 | hit2;
        }

        internal static bool4 Raycast4Capsules(in Ray ray, in simdFloat3 capA, in simdFloat3 capB, float4 capRadius, out float4 fraction, out simdFloat3 normal)
        {
            float4 axisLength = mathex.GetLengthAndNormal(capB - capA, out simdFloat3 axis);
            // Ray vs infinite cylinder
            float4     directionDotAxis  = simd.dot(ray.displacement, axis);
            float4     originDotAxis     = simd.dot(ray.start - capA, axis);
            simdFloat3 rayDisplacement2D = ray.displacement - axis * directionDotAxis;
            simdFloat3 rayOrigin2D       = ray.start - axis * originDotAxis;
            bool4      hitCylinder       = PointRaySphere.Raycast4Spheres(in rayOrigin2D,
                                                                     in rayDisplacement2D,
                                                                     in capA,
                                                                     capRadius,
                                                                     out float4 cylinderFraction,
                                                                     out simdFloat3 cylinderNormal);
            float4 t     = originDotAxis + cylinderFraction * directionDotAxis;
            hitCylinder &= t >= 0f & t <= axisLength;

            // Ray vs caps
            bool4 hitCapA = PointRaySphere.Raycast4Spheres(new simdFloat3(ray.start),
                                                           new simdFloat3(ray.displacement),
                                                           in capA,
                                                           capRadius,
                                                           out float4 capAFraction,
                                                           out simdFloat3 capANormal);
            bool4 hitCapB = PointRaySphere.Raycast4Spheres(new simdFloat3(ray.start),
                                                           new simdFloat3(ray.displacement),
                                                           in capB,
                                                           capRadius,
                                                           out float4 capBFraction,
                                                           out simdFloat3 capBNormal);

            // Find best result
            cylinderFraction = math.select(2f, cylinderFraction, hitCylinder);
            capAFraction     = math.select(2f, capAFraction, hitCapA);
            capBFraction     = math.select(2f, capBFraction, hitCapB);

            normal   = simd.select(cylinderNormal, capANormal, capAFraction < cylinderFraction);
            fraction = math.select(cylinderFraction, capAFraction, capAFraction < cylinderFraction);
            normal   = simd.select(normal, capBNormal, capBFraction < fraction);
            fraction = math.select(fraction, capBFraction, capBFraction < fraction);
            return fraction <= 1f;
        }

        /*public static bool PointCylinderDistance(float3 point, CylinderCollider cylinder, float maxDistance, out PointDistanceResultInternal result)
           {
            //Strategy: Project p onto the capsule's line.
            //If on the segment, do point vs sphere.
            //Otherwise do point vs disk
            float3 edge = cylinder.pointB - cylinder.pointA;
            float3 ap = point - cylinder.pointA;
            float3 unitEdge = math.normalize(edge);
            float dot = math.dot(ap, unitEdge);  //dot is distance of projected point from pointA
            float3 pointOnLine = cylinder.pointA + unitEdge * dot;
            if (dot < 0f)
            {
                //Todo: Optimize math
                float3 pointOnLineToPoint = point - pointOnLine;  //This gives us our direction from the center of the cap to the edge towards the query point.
                if (math.lengthsq(pointOnLineToPoint) > cylinder.radius * cylinder.radius)
                {
                    float3 roundNormal = math.normalize(pointOnLineToPoint);
                    float3 capNormal = -unitEdge;
                    result.normal = (roundNormal + capNormal) / math.SQRT2;  //Summing orthogonal unit vectors has a length of sqrt2
                    result.hitpoint = cylinder.pointA + roundNormal * cylinder.radius;
                    result.distance = math.distance(point, result.hitpoint);
                }
                else
                {
                    result.normal = -unitEdge;
                    result.distance = -dot;
                    result.hitpoint = point + unitEdge * result.distance;
                }
                return result.distance <= maxDistance;
            }
            if (dot * dot > math.lengthsq(edge))
            {
                //Todo: Optimize math
                float3 pointOnLineToPoint = point - pointOnLine;  //This gives us our direction from the center of the cap to the edge towards the query point.
                if (math.lengthsq(pointOnLineToPoint) > cylinder.radius * cylinder.radius)
                {
                    float3 roundNormal = math.normalize(pointOnLineToPoint);
                    float3 capNormal = unitEdge;
                    result.normal = (roundNormal + capNormal) / math.SQRT2;  //Summing orthogonal unit vectors has a length of sqrt2
                    result.hitpoint = cylinder.pointB + roundNormal * cylinder.radius;
                    result.distance = math.distance(point, result.hitpoint);
                }
                else
                {
                    result.normal = unitEdge;
                    result.distance = math.distance(pointOnLine, cylinder.pointB);
                    result.hitpoint = point - unitEdge * result.distance;
                }
                return result.distance <= maxDistance;
            }
            else
            {
                SphereCollider sphere = new SphereCollider(pointOnLine, cylinder.radius);
                return DistanceBetween(point, sphere, maxDistance, out result);
            }
           }*/

        internal static float3 CapsuleNormalFromFeatureCode(ushort featureCode, in CapsuleCollider capsule, float3 csoOutwardDir)
        {
            if (featureCode < 0x4000)
                return math.normalize(csoOutwardDir);

            var axis = capsule.pointB - capsule.pointA;
            return math.mul(quaternion.LookRotationSafe(axis, csoOutwardDir), math.up());
        }

        internal static ushort FeatureCodeFromGjk(byte count, byte a)
        {
            return count switch
                   {
                       1 => a,
                       2 => 0x4000,
                       _ => a
                   };
        }
    }
}

