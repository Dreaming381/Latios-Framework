using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayConvex
    {
        public static bool DistanceBetween(float3 point, in ConvexCollider convex, in RigidTransform convexTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInConvexSpace = math.transform(math.inverse(convexTransform), point);
            bool hit                = PointConvexDistance(pointInConvexSpace, in convex, maxDistance, out var localResult);
            result                  = new PointDistanceResult
            {
                hitpoint = math.transform(convexTransform, localResult.hitpoint),
                normal   = math.rotate(convexTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool Raycast(in Ray ray, in ConvexCollider convex, in RigidTransform convexTransform, out RaycastResult result)
        {
            var  rayInConvexSpace   = Ray.TransformRay(math.inverse(convexTransform), ray);
            bool hit                = RaycastConvex(in rayInConvexSpace, in convex, out float fraction, out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(convexTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        public static bool PointConvexDistance(float3 point, in ConvexCollider convex, float maxDistance, out PointDistanceResultInternal result)
        {
            ref var blob       = ref convex.convexColliderBlob.Value;
            float3  invScale   = math.rcp(convex.scale);
            var     dimensions = math.countbits(math.bitmask(new bool4(math.isfinite(invScale), false)));

            if (dimensions == 3)
            {
                float  maxSignedDistance = float.MinValue;
                int    bestPlaneIndex    = 0;
                float3 scaledPoint       = point * invScale;

                for (int i = 0; i < blob.facePlaneX.Length; i++)
                {
                    float dot = scaledPoint.x * blob.facePlaneX[i] + scaledPoint.y * blob.facePlaneY[i] + scaledPoint.z * blob.facePlaneZ[i] + blob.facePlaneDist[i];
                    if (dot > maxSignedDistance)
                    {
                        maxSignedDistance = dot;
                        bestPlaneIndex    = i;
                    }
                }

                var localNormal = new float3(blob.facePlaneX[bestPlaneIndex], blob.facePlaneY[bestPlaneIndex], blob.facePlaneZ[bestPlaneIndex]);
                if (maxSignedDistance < 0f)
                {
                    var localHit       = localNormal * -maxSignedDistance + scaledPoint;
                    result.hitpoint    = localHit * convex.scale;
                    result.distance    = math.distance(result.hitpoint, point);
                    result.normal      = math.normalize(localNormal * invScale);
                    result.featureCode = (ushort)(0x8000 + bestPlaneIndex);
                    return result.distance <= maxDistance;
                }

                var   edgeRange             = blob.edgeIndicesInFacesStartsAndCounts[bestPlaneIndex];
                float maxEdgeSignedDistance = float.MinValue;
                int   bestFaceEdgeIndex     = 0;
                for (int i = 0; i < edgeRange.y; i++)
                {
                    float dot = math.dot(scaledPoint.xyz1(), blob.faceEdgeOutwardPlanes[i + edgeRange.x]);
                    if (dot > maxEdgeSignedDistance)
                    {
                        maxEdgeSignedDistance = dot;
                        bestFaceEdgeIndex     = i;
                    }
                }

                if (maxEdgeSignedDistance < 0f)
                {
                    var localHit       = localNormal * -maxSignedDistance + scaledPoint;
                    result.hitpoint    = localHit * convex.scale;
                    result.distance    = math.distance(result.hitpoint, point);
                    result.normal      = math.normalize(localNormal * invScale);
                    result.featureCode = (ushort)(0x8000 + bestPlaneIndex);
                    return result.distance <= maxDistance;
                }

                var    edgeVertices = blob.vertexIndicesInEdges[blob.edgeIndicesInFaces[bestFaceEdgeIndex + edgeRange.x]];
                float3 vertexA      = new float3(blob.verticesX[edgeVertices.x], blob.verticesY[edgeVertices.x], blob.verticesZ[edgeVertices.x]);
                float3 vertexB      = new float3(blob.verticesX[edgeVertices.y], blob.verticesY[edgeVertices.y], blob.verticesZ[edgeVertices.y]);

                float3 ab      = vertexB - vertexA;
                float3 ap      = scaledPoint - vertexA;
                float  edgeDot = math.dot(ap, ab);

                if (edgeDot <= 0f)
                {
                    result.hitpoint    = vertexA * convex.scale;
                    result.distance    = math.distance(result.hitpoint, point);
                    result.normal      = math.normalize(blob.vertexNormals[edgeVertices.x] * invScale);
                    result.featureCode = (ushort)edgeVertices.x;
                    return result.distance <= maxDistance;
                }

                float edgeLengthSq = math.lengthsq(ab);

                if (edgeDot >= edgeLengthSq)
                {
                    result.hitpoint    = vertexB * convex.scale;
                    result.distance    = math.distance(result.hitpoint, point);
                    result.normal      = math.normalize(blob.vertexNormals[edgeVertices.y] * invScale);
                    result.featureCode = (ushort)edgeVertices.y;
                    return result.distance <= maxDistance;
                }

                result.hitpoint    = (vertexA + ab * edgeDot / edgeLengthSq) * convex.scale;
                result.distance    = math.distance(result.hitpoint, point);
                result.normal      = math.normalize(blob.edgeNormals[blob.edgeIndicesInFaces[bestFaceEdgeIndex + edgeRange.x]] * invScale);
                result.featureCode = (ushort)(0x4000 + blob.edgeIndicesInFaces[bestFaceEdgeIndex + edgeRange.x]);
                return result.distance <= maxDistance;
            }
            else if (dimensions == 0)
            {
                result.hitpoint    = 0f;
                result.distance    = math.length(point);
                result.normal      = math.normalizesafe(point, new float3(0f, 1f, 0f));
                result.featureCode = 0;
                return result.distance <= maxDistance;
            }
            else if (dimensions == 1)
            {
                float min        = float.MaxValue;
                float max        = float.MinValue;
                float position   = 0f;
                Aabb  scaledAabb = new Aabb(blob.localAabb.min * convex.scale, blob.localAabb.max * convex.scale);

                if (math.isfinite(invScale).x)
                {
                    min      = scaledAabb.min.x;
                    max      = scaledAabb.max.x;
                    position = point.x;
                }
                else if (math.isfinite(invScale).y)
                {
                    min      = scaledAabb.min.y;
                    max      = scaledAabb.max.y;
                    position = point.y;
                }
                else if (math.isfinite(invScale).z)
                {
                    min      = scaledAabb.min.z;
                    max      = scaledAabb.max.z;
                    position = point.z;
                }

                float3 mask = math.select(0f, 1f, math.isfinite(invScale));
                if (position <= min)
                {
                    result.hitpoint    = min * mask;
                    result.distance    = math.distance(point, result.hitpoint);
                    result.normal      = -mask;
                    result.featureCode = 0;
                    return result.distance <= maxDistance;
                }
                if (position >= max)
                {
                    result.hitpoint    = max * mask;
                    result.distance    = math.distance(point, result.hitpoint);
                    result.normal      = mask;
                    result.featureCode = 1;
                    return result.distance <= maxDistance;
                }
                result.hitpoint    = position * mask;
                result.distance    = math.distance(point, result.hitpoint);
                result.normal      = math.normalizesafe(point - result.hitpoint, math.select(-mask, mask, position >= (min + mask) / 2f));
                result.featureCode = 0x4000;
                return result.distance <= maxDistance;
            }
            else  //if (dimensions == 2)
            {
                //Todo:
                var mask = math.select(1f, 0f, math.isfinite(invScale));
                if (math.abs(math.csum(point * mask)) > maxDistance)
                {
                    result = default;
                    return false;
                }

                var flipMask  = 1f - mask;
                var diff      = blob.localAabb.max - blob.localAabb.min;
                diff         *= mask;
                var hitPoint  = flipMask * point;

                var inflateRay      = new Ray(hitPoint - diff, hitPoint + diff);
                var inflateConvex   = convex;
                inflateConvex.scale = math.select(1f, convex.scale, math.isfinite(invScale));
                if (RaycastConvex(in inflateRay, in inflateConvex, out _, out _))
                {
                    result.hitpoint    = hitPoint;
                    result.distance    = math.abs(math.csum(point * mask));
                    result.normal      = math.select(-1f, 1f, point >= 0f) * mask;
                    result.featureCode = 0x8000;
                    return true;
                }

                result          = default;
                result.distance = float.MaxValue;

                for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
                {
                    var    indices  = blob.vertexIndicesInEdges[i];
                    float3 vertexA  = new float3(blob.verticesX[indices.x], blob.verticesY[indices.x], blob.verticesZ[indices.x]);
                    float3 vertexB  = new float3(blob.verticesX[indices.y], blob.verticesY[indices.y], blob.verticesZ[indices.y]);
                    vertexA        *= flipMask;
                    vertexB        *= flipMask;

                    float3 edge           = vertexB - vertexA;
                    float3 ap             = point - vertexA;
                    float  dot            = math.dot(ap, edge);
                    float  edgeLengthSq   = math.lengthsq(edge);
                    dot                   = math.clamp(dot, 0f, edgeLengthSq);
                    float3 pointOnSegment = vertexA + edge * dot / edgeLengthSq;
                    float  newDistance    = math.distance(pointOnSegment, point);

                    if (newDistance < result.distance)
                    {
                        result.distance    = newDistance;
                        result.hitpoint    = pointOnSegment;
                        result.featureCode = (ushort)math.select(math.select(0x4000 + i, indices.x, dot == 0f), indices.y, dot == edgeLengthSq);
                    }
                }

                if (result.distance <= maxDistance)
                {
                    var normalScale = math.select(0f, invScale, flipMask == 1f);
                    if (result.featureCode >= 0x4000)
                    {
                        result.normal = math.normalize(normalScale * blob.edgeNormals[result.featureCode & 0x7fff]);
                    }
                    else
                    {
                        result.normal = math.normalize(normalScale * blob.vertexNormals[result.featureCode & 0x7fff]);
                    }
                    return true;
                }
                return false;
            }
        }

        internal static bool RaycastConvex(in Ray ray, in ConvexCollider convex, out float fraction, out float3 normal)
        {
            ref var blob       = ref convex.convexColliderBlob.Value;
            var     scaledAabb = new Aabb(blob.localAabb.min * convex.scale, blob.localAabb.max * convex.scale);

            if (!PointRayBox.RaycastAabb(in ray, in scaledAabb, out float aabbFraction))
            {
                if (!math.all(ray.start >= scaledAabb.min & ray.end <= scaledAabb.max))
                {
                    fraction = 2f;
                    normal   = default;
                    return false;
                }
            }

            float3 invScale   = math.rcp(convex.scale);
            var    dimensions = math.countbits(math.bitmask(new bool4(math.isfinite(invScale), false)));

            if (dimensions == 3)
            {
                fraction           = -2f;
                float exitFraction = 2f;
                int   bestIndex    = 0;
                bool  inside       = true;
                var   scaledRay    = new Ray(ray.start * invScale, ray.end * invScale);

                for (int i = 0; i < blob.facePlaneX.Length; i++)
                {
                    // These are signed distances to the plane from start/end points respectively
                    float startDot = scaledRay.start.x * blob.facePlaneX[i] + scaledRay.start.y * blob.facePlaneY[i] + scaledRay.start.z * blob.facePlaneZ[i] +
                                     blob.facePlaneDist[i];
                    float endDot = scaledRay.end.x * blob.facePlaneX[i] + scaledRay.end.y * blob.facePlaneY[i] + scaledRay.end.z * blob.facePlaneZ[i] + blob.facePlaneDist[i];

                    // If the ray is completely outside the plane or starts on the plane and moves away, then it misses.
                    if (startDot >= 0f && endDot >= 0f)
                    {
                        // If the ray is coplaner, just skip
                        if (startDot == 0f && endDot == 0f)
                            continue;

                        normal   = default;
                        fraction = 2f;
                        return false;
                    }

                    // This is the distance of the ray start to the plane divided by the length of the ray projected onto the plane's normal.
                    float newFraction = startDot / (startDot - endDot);

                    if (newFraction > fraction && startDot > 0f)
                    {
                        fraction  = newFraction;
                        bestIndex = i;
                    }
                    else if (newFraction < exitFraction && endDot > 0f)
                    {
                        exitFraction = newFraction;
                    }
                    inside &= startDot < 0f;
                }
                if (inside || exitFraction < fraction)
                {
                    normal   = default;
                    fraction = 2f;
                    return false;
                }

                normal = new float3(blob.facePlaneX[bestIndex], blob.facePlaneY[bestIndex], blob.facePlaneZ[bestIndex]);
                return true;
            }
            else if (dimensions == 0)
            {
                SphereCollider sphere = new SphereCollider(0f, 0f);
                return PointRaySphere.RaycastSphere(in ray, in sphere, out fraction, out normal);
            }
            else if (dimensions == 1)
            {
                CapsuleCollider capsule = new CapsuleCollider(scaledAabb.min, scaledAabb.max, 0f);
                return PointRayCapsule.RaycastCapsule(in ray, in capsule, out fraction, out normal);
            }
            else if (dimensions == 2)
            {
                // From the AABB check we know the ray crosses the plane. So now we just need to figure out if the ray hits
                // the geometry.

                // If the ray comes in colinear with the plane, the following won't work.
                // So we check to make sure that doesn't happen

                var mask = math.select(1f, 0f, math.isfinite(invScale));
                if (!math.all(mask * ray.displacement < math.EPSILON))
                {
                    var hitPoint = ray.start + ray.displacement * aabbFraction;

                    var diff      = blob.localAabb.max - blob.localAabb.min;
                    diff         *= mask;
                    var rayStart  = hitPoint - diff + blob.localAabb.min * mask;

                    var inflateRay      = new Ray(rayStart, rayStart + diff * 3f);
                    var inflateConvex   = convex;
                    inflateConvex.scale = math.select(1f, convex.scale, math.isfinite(invScale));
                    if (RaycastConvex(in inflateRay, in inflateConvex, out _, out _))
                    {
                        fraction = aabbFraction;
                        normal   = math.normalize(mask * ray.displacement);
                        return true;
                    }
                }

                // Backup is to inflate the convex collider by a small radius and try again.
                var hit        = RaycastRoundedConvex(in ray, in convex, math.EPSILON, out fraction);
                var queryPoint = ray.start + ray.displacement * (fraction - 1e-4f);
                PointConvexDistance(queryPoint, in convex, 1f, out var normalResult);
                normal = normalResult.normal;
                return hit;
            }
            fraction = 2f;
            normal   = default;
            return false;
        }

        // Scale is applied before radius
        internal static bool RaycastRoundedConvex(in Ray ray, in ConvexCollider convex, float radius, out float fraction)
        {
            ref var blob       = ref convex.convexColliderBlob.Value;
            var     scale      = convex.scale;
            var     scaledAabb = new Aabb(blob.localAabb.min * scale - radius, blob.localAabb.max * scale + radius);

            if (!PointRayBox.RaycastAabb(in ray, in scaledAabb, out _))
            {
                if (!math.all(ray.start >= scaledAabb.min & ray.end <= scaledAabb.max))
                {
                    fraction = 2f;
                    return false;
                }
            }

            float3 invScale   = math.rcp(scale);
            var    dimensions = math.countbits(math.bitmask(new bool4(math.isfinite(invScale), false)));

            if (dimensions == 3)
            {
                fraction           = -2f;
                float exitFraction = 2f;
                int   bestIndex    = 0;
                bool  inside       = true;
                var   scaledRay    = new Ray(ray.start * invScale, ray.end * invScale);

                for (int i = 0; i < blob.facePlaneX.Length; i++)
                {
                    float startDot = scaledRay.start.x * blob.facePlaneX[i] + scaledRay.start.y * blob.facePlaneY[i] + scaledRay.start.z * blob.facePlaneZ[i] +
                                     blob.facePlaneDist[i] - radius;
                    float endDot = scaledRay.end.x * blob.facePlaneX[i] + scaledRay.end.y * blob.facePlaneY[i] + scaledRay.end.z * blob.facePlaneZ[i] + blob.facePlaneDist[i] -
                                   radius;
                    // If the ray is completely outside the plane or starts on the plane and moves away, then it misses.
                    if (startDot >= 0f && endDot >= 0f)
                    {
                        // If the ray is coplaner, just skip
                        if (startDot == 0f && endDot == 0f)
                            continue;

                        fraction = 2f;
                        return false;
                    }

                    // This is the distance of the ray start to the plane divided by the length of the ray projected onto the plane's normal.
                    float newFraction = startDot / (startDot - endDot);

                    if (newFraction > fraction && startDot > 0f)
                    {
                        fraction  = newFraction;
                        bestIndex = i;
                    }
                    else if (newFraction < exitFraction && endDot > 0f)
                    {
                        exitFraction = newFraction;
                    }
                    inside &= startDot < 0f;
                }

                if (inside || exitFraction < fraction)
                {
                    fraction = 2f;
                    return false;
                }

                // We know the inflated hit face, but we don't know if it hit the rounded part or not yet.
                float3 scaledPoint = scaledRay.start + scaledRay.displacement * fraction;
                var    edgeRange   = blob.edgeIndicesInFacesStartsAndCounts[bestIndex];
                bool   hitEdge     = false;
                bool   nearsEdge   = false;
                for (int i = 0; i < edgeRange.y; i++)
                {
                    float dot = math.dot(scaledPoint.xyz1(), blob.faceEdgeOutwardPlanes[i + edgeRange.x]);
                    if (dot > 0f)
                    {
                        nearsEdge   = true;
                        var indices = blob.vertexIndicesInEdges[blob.edgeIndicesInFaces[i + edgeRange.x]];
                        var cap     = new CapsuleCollider(new float3(blob.verticesX[indices.x], blob.verticesY[indices.x], blob.verticesZ[indices.x]),
                                                          new float3(blob.verticesX[indices.y], blob.verticesY[indices.y], blob.verticesZ[indices.y]), radius);
                        if (PointRayCapsule.RaycastCapsule(in scaledRay, in cap, out float newFraction, out _))
                        {
                            if (!hitEdge)
                            {
                                fraction = newFraction;
                                hitEdge  = true;
                            }
                            fraction = math.min(fraction, newFraction);
                        }
                    }
                }

                return nearsEdge == hitEdge;
            }
            else if (dimensions == 0)
            {
                SphereCollider sphere = new SphereCollider(0f, radius);
                return PointRaySphere.RaycastSphere(in ray, in sphere, out fraction, out _);
            }
            else if (dimensions == 1)
            {
                CapsuleCollider capsule = new CapsuleCollider(scaledAabb.min + radius, scaledAabb.max - radius, 0f);
                return PointRayCapsule.RaycastCapsule(in ray, in capsule, out fraction, out _);
            }
            else if (dimensions == 2)
            {
                // We need to identify if the ray hits one of the planar surfaces.
                var   mask     = math.select(1f, 0f, math.isfinite(invScale));
                float maxStart = math.dot(ray.start.xyz1(), new float4(mask, -radius));
                float maxEnd   = math.dot(ray.end.xyz1(), new float4(mask, -radius));
                float minStart = math.dot(ray.start.xyz1(), new float4(-mask, radius));
                float minEnd   = math.dot(ray.end.xyz1(), new float4(-mask, radius));
                if (maxStart > 0f && maxEnd <= 0f)
                {
                    // We might have a planar hit on the max side of the AABB, so find the plane hit and raycast the original object.
                    float  planarFraction = maxStart / (maxStart - maxEnd);
                    float3 hitPoint       = ray.start + ray.displacement * planarFraction;

                    var diff      = blob.localAabb.max - blob.localAabb.min + 2f * radius;
                    diff         *= mask;
                    var rayStart  = hitPoint - diff + blob.localAabb.min * mask;

                    var inflateRay      = new Ray(rayStart, rayStart + diff * 3f);
                    var inflateConvex   = convex;
                    inflateConvex.scale = math.select(1f, convex.scale, math.isfinite(invScale));
                    if (RaycastConvex(in inflateRay, in inflateConvex, out _, out _))
                    {
                        fraction = planarFraction;
                        return true;
                    }
                }
                else if (minStart > 0f && minEnd <= 0f)
                {
                    // We might have a planar hit on the max side of the AABB, so find the plane hit and raycast the original object.
                    float  planarFraction = minStart / (minStart - minEnd);
                    float3 hitPoint       = ray.start + ray.displacement * planarFraction;

                    var diff      = blob.localAabb.max - blob.localAabb.min + 2f * radius;
                    diff         *= mask;
                    var rayStart  = hitPoint - diff + blob.localAabb.min * mask;

                    var inflateRay      = new Ray(rayStart, rayStart + diff * 3f);
                    var inflateConvex   = convex;
                    inflateConvex.scale = math.select(1f, convex.scale, math.isfinite(invScale));
                    if (RaycastConvex(in inflateRay, in inflateConvex, out _, out _))
                    {
                        fraction = planarFraction;
                        return true;
                    }
                }

                fraction = 2f;
                bool hit = false;
                for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
                {
                    var indices = blob.vertexIndicesInEdges[i];
                    var cap     = new CapsuleCollider(new float3(blob.verticesX[indices.x], blob.verticesY[indices.x], blob.verticesZ[indices.x]) * scale,
                                                      new float3(blob.verticesX[indices.y], blob.verticesY[indices.y], blob.verticesZ[indices.y]) * scale, radius);
                    if (PointRayCapsule.RaycastCapsule(in ray, in cap, out float newFraction, out _))
                    {
                        hit      = true;
                        fraction = math.min(fraction, newFraction);
                    }
                }
                return hit;
            }
            fraction = 2f;
            return false;
        }

        internal static void BestFacePlane(ref ConvexColliderBlob blob, float3 localDirectionToAlign, ushort featureCode, out Plane facePlane, out int faceIndex, out int edgeCount)
        {
            var featureType = featureCode >> 14;
            if (featureType == 2)
            {
                // Feature is face. Grab the face.
                faceIndex = featureCode & 0x3fff;
                facePlane = new Plane(new float3(blob.facePlaneX[faceIndex], blob.facePlaneY[faceIndex], blob.facePlaneZ[faceIndex]), blob.facePlaneDist[faceIndex]);
                edgeCount = blob.edgeIndicesInFacesStartsAndCounts[faceIndex].y;
            }
            else if (featureType == 1)
            {
                // Feature is edge. One of two faces is best.
                var edgeIndex   = featureCode & 0x3fff;
                var faceIndices = blob.faceIndicesByEdge[edgeIndex];
                var facePlaneA  =
                    new Plane(new float3(blob.facePlaneX[faceIndices.x], blob.facePlaneY[faceIndices.x], blob.facePlaneZ[faceIndices.x]), blob.facePlaneDist[faceIndices.x]);
                var facePlaneB =
                    new Plane(new float3(blob.facePlaneX[faceIndices.y], blob.facePlaneY[faceIndices.y], blob.facePlaneZ[faceIndices.y]), blob.facePlaneDist[faceIndices.y]);
                if (math.dot(localDirectionToAlign, facePlaneA.normal) >= math.dot(localDirectionToAlign, facePlaneB.normal))
                {
                    faceIndex = faceIndices.x;
                    facePlane = facePlaneA;
                    edgeCount = blob.edgeIndicesInFacesStartsAndCounts[faceIndex].y;
                }
                else
                {
                    faceIndex = faceIndices.y;
                    facePlane = facePlaneB;
                    edgeCount = blob.edgeIndicesInFacesStartsAndCounts[faceIndex].y;
                }
            }
            else
            {
                // Feature is vertex. One of adjacent faces is best.
                var vertexIndex        = featureCode & 0x3fff;
                var facesStartAndCount = blob.faceIndicesByVertexStartsAndCounts[vertexIndex];
                faceIndex              = blob.faceIndicesByVertex[facesStartAndCount.x];
                facePlane              = new Plane(new float3(blob.facePlaneX[faceIndex], blob.facePlaneY[faceIndex], blob.facePlaneZ[faceIndex]), blob.facePlaneDist[faceIndex]);
                float bestDot          = math.dot(localDirectionToAlign, facePlane.normal);
                for (int i = 1; i < facesStartAndCount.y; i++)
                {
                    var otherFaceIndex = blob.faceIndicesByVertex[facesStartAndCount.x + i];
                    var otherPlane     = new Plane(new float3(blob.facePlaneX[otherFaceIndex], blob.facePlaneY[otherFaceIndex], blob.facePlaneZ[otherFaceIndex]),
                                                   blob.facePlaneDist[otherFaceIndex]);
                    var otherDot = math.dot(localDirectionToAlign, otherPlane.normal);
                    if (otherDot > bestDot)
                    {
                        bestDot   = otherDot;
                        faceIndex = otherFaceIndex;
                        facePlane = otherPlane;
                    }
                }
                edgeCount = blob.edgeIndicesInFacesStartsAndCounts[faceIndex].y;
            }
        }
    }
}

