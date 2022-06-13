using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class SpatialInternal
    {
        public static bool PointConvexDistance(float3 point, ConvexCollider convex, float maxDistance, out PointDistanceResultInternal result)
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
                    var localHit    = localNormal * -maxSignedDistance + scaledPoint;
                    result.hitpoint = localHit * convex.scale;
                    result.distance = math.distance(result.hitpoint, point);
                    result.normal   = math.normalize(localNormal * invScale);
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
                    var localHit    = localNormal * -maxSignedDistance + scaledPoint;
                    result.hitpoint = localHit * convex.scale;
                    result.distance = math.distance(result.hitpoint, point);
                    result.normal   = math.normalize(localNormal * invScale);
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
                    result.hitpoint = vertexA * convex.scale;
                    result.distance = math.distance(result.hitpoint, point);
                    result.normal   = math.normalize(blob.vertexNormals[edgeVertices.x] * invScale);
                    return result.distance <= maxDistance;
                }

                float edgeLengthSq = math.lengthsq(ab);

                if (edgeDot >= edgeLengthSq)
                {
                    result.hitpoint = vertexB * convex.scale;
                    result.distance = math.distance(result.hitpoint, point);
                    result.normal   = math.normalize(blob.vertexNormals[edgeVertices.y] * invScale);
                    return result.distance <= maxDistance;
                }

                result.hitpoint = (vertexA + ab * edgeDot / edgeLengthSq) * convex.scale;
                result.distance = math.distance(result.hitpoint, point);
                result.normal   = math.normalize(blob.edgeNormals[blob.edgeIndicesInFaces[bestFaceEdgeIndex + edgeRange.x]] * invScale);
                return result.distance <= maxDistance;
            }
            else if (dimensions == 0)
            {
                result.hitpoint = 0f;
                result.distance = math.length(point);
                result.normal   = math.normalizesafe(point, new float3(0f, 1f, 0f));
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
                    result.hitpoint = min * mask;
                    result.distance = math.distance(point, result.hitpoint);
                    result.normal   = -mask;
                    return result.distance <= maxDistance;
                }
                if (position >= max)
                {
                    result.hitpoint = max * mask;
                    result.distance = math.distance(point, result.hitpoint);
                    result.normal   = mask;
                    return result.distance <= maxDistance;
                }
                result.hitpoint = position * mask;
                result.distance = math.distance(point, result.hitpoint);
                result.normal   = math.normalizesafe(point - result.hitpoint, math.select(-mask, mask, position >= (min + mask) / 2f));
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
                if (RaycastConvex(inflateRay, inflateConvex, out _, out _))
                {
                    result.hitpoint = hitPoint;
                    result.distance = math.abs(math.csum(point * mask));
                    result.normal   = math.select(-1f, 1f, point >= 0f) * mask;
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
                        result.distance = newDistance;
                        result.hitpoint = pointOnSegment;
                    }
                }

                if (result.distance <= maxDistance)
                {
                    result.normal = math.select(-1f, 1f, point >= 0f) * mask;
                    return true;
                }
                return false;
            }
        }
    }
}

