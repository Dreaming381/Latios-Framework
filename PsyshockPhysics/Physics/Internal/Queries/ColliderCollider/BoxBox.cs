using System;
using System.Runtime.CompilerServices;
using Latios.Calci;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class BoxBox
    {
        public static bool AreOverlapping(in BoxCollider boxA,
                                          in RigidTransform aTransform,
                                          in BoxCollider boxB,
                                          in RigidTransform bTransform)
        {
            var aOffsetTransform  = aTransform;
            aOffsetTransform.pos += math.rotate(aTransform.rot, boxA.center);
            var bOffsetTransform  = bTransform;
            bOffsetTransform.pos += math.rotate(bTransform.rot, boxB.center);
            var bInASpace         = new AffineTransform(math.InverseTransformFast(in aOffsetTransform, in bOffsetTransform));
            return SAT.AreOverlapping(boxA.halfSize, boxB.halfSize, in bInASpace);
        }

        public static bool WithinDistance(in BoxCollider boxA,
                                          in RigidTransform aTransform,
                                          in BoxCollider boxB,
                                          in RigidTransform bTransform,
                                          float maxDistance)
        {
            var aOffsetTransform  = aTransform;
            aOffsetTransform.pos += math.rotate(aTransform.rot, boxA.center);
            var bOffsetTransform  = bTransform;
            bOffsetTransform.pos += math.rotate(bTransform.rot, boxB.center);
            var bInASpace         = new AffineTransform(math.InverseTransformFast(in aOffsetTransform, in bOffsetTransform));

            var sat = SAT.TestAllAxes(boxA.halfSize, boxB.halfSize, in bInASpace);
            sat.LargestAxisAndIndex(out float separation, out int separationAxisIndex);
            if (separation > maxDistance)
            {
                return false;
            }
            // 1e-4f is well above the rounding we typically encounter, so it is safe. However, during development, it was discovered that this
            // can actually cull less-correct ties and produce more accurate pairs. Refining this constant further is something that can be explored later.
            float cullingThreshold = -1e-4f * math.cmax(math.max(boxA.halfSize, boxB.halfSize));
            if (separation >= 0f)
            {
                var mask = sat.Cull(cullingThreshold);
                return BoxBoxDistanceSeparated(boxA.halfSize, boxB.halfSize, bInASpace, maxDistance, mask, out _, true);
            }
            else
            {
                var separationAxis  = SAT.GetSeparationAxis(separationAxisIndex, in bInASpace);
                var bShift          = -separation * separationAxis;
                bInASpace.t        += bShift;
                sat                 = SAT.TestAllAxes(boxA.halfSize, boxB.halfSize, bInASpace);
                var mask            = sat.Cull(cullingThreshold);
                BoxBoxDistanceSeparated(boxA.halfSize, boxB.halfSize, bInASpace, float.MaxValue, mask, out var localResult, true);
                localResult.distance += separation;
                return localResult.distance <= maxDistance;
            }
        }

        public static bool DistanceBetween(in BoxCollider boxA,
                                           in RigidTransform aTransform,
                                           in BoxCollider boxB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var aOffsetTransform  = aTransform;
            aOffsetTransform.pos += math.rotate(aTransform.rot, boxA.center);
            var bOffsetTransform  = bTransform;
            bOffsetTransform.pos += math.rotate(bTransform.rot, boxB.center);
            var bInASpace         = new AffineTransform(math.InverseTransformFast(in aOffsetTransform, in bOffsetTransform));

            var sat = SAT.TestAllAxes(boxA.halfSize, boxB.halfSize, in bInASpace);
            sat.LargestAxisAndIndex(out float separation, out int separationAxisIndex);
            if (separation > maxDistance)
            {
                result = default;
                return false;
            }
            // 1e-4f is well above the rounding we typically encounter, so it is safe. However, during development, it was discovered that this
            // can actually cull less-correct ties and produce more accurate pairs. Refining this constant further is something that can be explored later.
            float cullingThreshold = -1e-4f * math.cmax(math.max(boxA.halfSize, boxB.halfSize));
            if (separation >= 0f)
            {
                var  mask              = sat.Cull(cullingThreshold);
                bool hit               = BoxBoxDistanceSeparated(boxA.halfSize, boxB.halfSize, bInASpace, maxDistance, mask, out var localResult);
                localResult.hitpointA += boxA.center;
                localResult.hitpointB += boxA.center;
                result                 = InternalQueryTypeUtilities.BinAResultToWorld(in localResult, in aTransform);
                return hit;
            }
            else
            {
                var separationAxis  = SAT.GetSeparationAxis(separationAxisIndex, in bInASpace);
                var bShift          = -separation * separationAxis;
                bInASpace.t        += bShift;
                sat                 = SAT.TestAllAxes(boxA.halfSize, boxB.halfSize, bInASpace);
                var mask            = sat.Cull(cullingThreshold);
                BoxBoxDistanceSeparated(boxA.halfSize, boxB.halfSize, bInASpace, float.MaxValue, mask, out var localResult);
                localResult.hitpointA += boxA.center;
                localResult.hitpointB += boxA.center - bShift;
                localResult.distance  += separation;
                result                 = InternalQueryTypeUtilities.BinAResultToWorld(in localResult, in aTransform);
                return localResult.distance <= maxDistance;
            }
        }

        public static bool ColliderCast(in BoxCollider boxToCast, in RigidTransform castStart, float3 castEnd, in BoxCollider targetBox, in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            var aOffsetTransform             = castStart;
            aOffsetTransform.pos            += math.rotate(castStart.rot, boxToCast.center);
            var bOffsetTransform             = targetBoxTransform;
            bOffsetTransform.pos            += math.rotate(targetBoxTransform.rot, targetBox.center);
            var bInASpace                    = new AffineTransform(math.InverseTransformFast(in aOffsetTransform, in bOffsetTransform));
            var castDisplacement             = castEnd - castStart.pos;
            var transformedCastDisplacement  = math.InverseRotateFast(castStart.rot, castDisplacement);

            if (!SAT.CastAllAxes(boxToCast.halfSize, targetBox.halfSize, in bInASpace, transformedCastDisplacement, out float fraction))
            {
                result = default;
                return false;
            }

            var casterHitTransform  = castStart;
            casterHitTransform.pos += castDisplacement * fraction;
            DistanceBetween(in boxToCast, in casterHitTransform, in targetBox, in targetBoxTransform, float.MaxValue, out var distanceResult);

            result = new ColliderCastResult
            {
                distance                 = fraction * math.length(castDisplacement),
                hitpoint                 = distanceResult.hitpointA,
                normalOnCaster           = distanceResult.normalA,
                normalOnTarget           = distanceResult.normalB,
                subColliderIndexOnCaster = 0,
                subColliderIndexOnTarget = 0
            };

            return true;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in BoxCollider boxA,
                                                                          in RigidTransform aTransform,
                                                                          in BoxCollider boxB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            UnitySim.ContactsBetweenResult result        = default;
            var                            bInATransform = math.mul(math.inverse(aTransform), bTransform);

            // Unity Physics prefers to use the SAT axes for the contact normal if it can.
            // We attempt to recover the best SAT axis here.
            var featureTypes  = (distanceResult.featureCodeA >> 12) & 0x0c;
            featureTypes     |= distanceResult.featureCodeB >> 14;
            float3 aLocalContactNormal;
            bool   usesContactDir = false;
            switch (featureTypes)
            {
                case 0:  // A point and B point
                case 1:  // A point and B edge
                case 4:  // A edge and B point
                {
                    // In the case of a point and an edge, I believe only one of three things will happen:
                    // 1) The SAT axis is between two edges, and the planes won't project onto each other at all.
                    // 2) The SAT axis will be a face axis, in which the closest point will be clipped off.
                    // 3) The SAT axis is identical to the direction between the closest points.
                    //
                    // The first two options in Unity Physics will trigger the FaceFace() method to fail.
                    // The failure will result in Unity completely nuking its in-progress manifold and using
                    // the general-purpose algorithm that relies on GJK, which uses a contact normal based on
                    // the axis between the closest points. The last option is effectively the same result.
                    aLocalContactNormal =
                        math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
                    // Yes, normalizesafe has produced "valid" normals on floating point errors before. The result can get pretty noisy, so the tolerance is high.
                    aLocalContactNormal = math.select(aLocalContactNormal, float3.zero, math.abs(distanceResult.distance) < 1e-3f);
                    if (aLocalContactNormal.Equals(float3.zero))
                    {
                        aLocalContactNormal = math.normalize(distanceResult.normalA - distanceResult.normalB);
                    }
                    aLocalContactNormal = math.InverseRotateFast(aTransform.rot, aLocalContactNormal);
                    usesContactDir      = true;
                    break;
                }
                case 5:  // A edge and B edge
                {
                    var edgeDirectionIndexA = (distanceResult.featureCodeA >> 2) & 0xff;
                    var edgeDirectionA      = edgeDirectionIndexA switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
                        _ => default
                    };
                    var edgeDirectionIndexB = (distanceResult.featureCodeB >> 2) & 0xff;
                    var edgeDirectionB      = edgeDirectionIndexB switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
                        _ => default
                    };
                    edgeDirectionB      = math.rotate(bInATransform.rot, edgeDirectionB);
                    aLocalContactNormal = math.normalize(math.cross(edgeDirectionA, edgeDirectionB));
                    aLocalContactNormal = math.select(aLocalContactNormal,
                                                      -aLocalContactNormal,
                                                      math.dot(math.rotate(aTransform.rot, aLocalContactNormal), distanceResult.normalA) < 0f);
                    break;
                }
                case 2:  // A point and B face
                case 6:  // A edge and B face
                {
                    // For A edge, this can only happen due to some bizarre precision issues.
                    // But we'll handle it anyways by just using the face normal of B.
                    var faceIndex       = distanceResult.featureCodeB & 0xff;
                    aLocalContactNormal = faceIndex switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
                        3 => new float3(-1f, 0f, 0f),
                        4 => new float3(0f, -1f, 0f),
                        5 => new float3(0f, 0f, -1f),
                        _ => default
                    };
                    aLocalContactNormal = -math.rotate(bInATransform.rot, aLocalContactNormal);
                    break;
                }
                case 8:  // A face and B point
                case 9:  // A face and B edge
                case 10:  // A face and B face
                {
                    // For B edge and face, this can only happen due to some bizarre precision issues.
                    // But we'll handle it anyways by just using the face normal of A.
                    var faceIndex       = distanceResult.featureCodeA & 0xff;
                    aLocalContactNormal = faceIndex switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
                        3 => new float3(-1f, 0f, 0f),
                        4 => new float3(0f, -1f, 0f),
                        5 => new float3(0f, 0f, -1f),
                        _ => default
                    };
                    break;
                }
                default:
                    aLocalContactNormal = default;
                    break;
            }

            for (int iteration = math.select(0, 1, usesContactDir); iteration < 2; iteration++)
            {
                result.contactNormal = math.rotate(aTransform, -aLocalContactNormal);

                var bLocalContactNormal = math.InverseRotateFast(bInATransform.rot, -aLocalContactNormal);
                PointRayBox.BestFacePlanesAndVertices(in boxA, aLocalContactNormal, out var aEdgePlaneNormals, out var aEdgePlaneDistances, out var aPlane, out var aVertices);
                PointRayBox.BestFacePlanesAndVertices(in boxB, bLocalContactNormal, out var bEdgePlaneNormals, out _,                       out var bPlane, out var bVertices);
                bPlane                                 = mathex.TransformPlane(bInATransform, bPlane);
                bVertices                              = simd.transform(bInATransform, bVertices);
                bEdgePlaneNormals                      = simd.rotate(bInATransform.rot, bEdgePlaneNormals);
                var  bEdgePlaneDistances               = simd.dot(bEdgePlaneNormals, bVertices.bcda);
                bool needsClosestPoint                 = true;
                var  distanceScalarAlongContactNormalB = math.rcp(math.dot(-aLocalContactNormal, bPlane.normal));

                // Project and clip edges of A onto the face of B.
                for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                {
                    var rayStart          = aVertices.a;
                    var rayDisplacement   = aVertices.b - rayStart;
                    var rayRelativeStarts = simd.dot(rayStart, bEdgePlaneNormals) - bEdgePlaneDistances;
                    var relativeDiffs     = simd.dot(rayDisplacement, bEdgePlaneNormals);
                    var rayRelativeEnds   = rayRelativeStarts + relativeDiffs;
                    var rayFractions      = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
                    var startsInside      = rayRelativeStarts <= 0f;
                    var endsInside        = rayRelativeEnds <= 0f;
                    var projectsOnFace    = startsInside | endsInside;
                    var enterFractions    = math.select(float4.zero, rayFractions, !startsInside & rayFractions > float4.zero);
                    var exitFractions     = math.select(1f, rayFractions, !endsInside & rayFractions < 1f);
                    var fractionA         = math.cmax(enterFractions);
                    var fractionB         = math.cmin(exitFractions);

                    if (math.all(projectsOnFace) && fractionA < fractionB)
                    {
                        // Add the two contacts from the possibly clipped segment
                        var clippedSegmentA = rayStart + fractionA * rayDisplacement;
                        var aDistance       = mathex.SignedDistance(bPlane, clippedSegmentA) * distanceScalarAlongContactNormalB;
                        result.Add(math.transform(aTransform, clippedSegmentA + aLocalContactNormal * aDistance), aDistance);
                        needsClosestPoint &= aDistance > distanceResult.distance + 1e-4f;
                        if (fractionB < 1f)  // Avoid duplication when vertex is not clipped
                        {
                            var clippedSegmentB = rayStart + fractionB * rayDisplacement;
                            var bDistance       = mathex.SignedDistance(bPlane, clippedSegmentB) * distanceScalarAlongContactNormalB;
                            var bContact        = math.transform(aTransform, clippedSegmentB + aLocalContactNormal * bDistance);
                            result.Add(bContact, bDistance);
                            needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                        }
                    }
                    aVertices = aVertices.bcda;
                }

                // Project vertices of B onto the face of A
                var distanceScalarAlongContactNormalA = math.rcp(math.dot(aLocalContactNormal, aPlane.normal));
                for (int i = 0; i < 4; i++)
                {
                    var vertex = bVertices[i];
                    if (math.all(simd.dot(aEdgePlaneNormals, vertex) < aEdgePlaneDistances))
                    {
                        var distance = mathex.SignedDistance(aPlane, vertex) * distanceScalarAlongContactNormalA;
                        var bContact = math.transform(aTransform, vertex);
                        result.Add(bContact, distance);
                        needsClosestPoint &= distance > distanceResult.distance + 1e-4f;
                    }
                }

                if (!needsClosestPoint)
                {
                    return result;
                }
                else if (iteration == 0)
                {
                    // We missed using the SAT axis. Unity falls back to a more generalized algorithm,
                    // but it effectively simplifies to the same loop except using a contact normal based
                    // on the axis of the closest points.
                    aLocalContactNormal =
                        math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
                    if (aLocalContactNormal.Equals(float3.zero))
                    {
                        aLocalContactNormal = math.normalize(distanceResult.normalA - distanceResult.normalB);
                    }
                    aLocalContactNormal = math.InverseRotateFast(aTransform.rot, aLocalContactNormal);
                    result              = default;
                }
            }

            result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }

        static class SAT
        {
            public unsafe struct SatResult
            {
                fixed float axes[15];
                public float this[int index]
                {
                    get => axes[index];
                    set => axes[index] = value;
                }

                public void LargestAxisAndIndex(out float separation, out int index)
                {
                    float4 a           = new float4(axes[0], axes[1], axes[2], axes[3]);
                    float4 b           = new float4(axes[4], axes[5], axes[6], axes[7]);
                    float4 c           = new float4(axes[8], axes[9], axes[10], axes[11]);
                    float4 d           = new float4(axes[12], axes[13], axes[14], axes[14]);
                    int4   ai          = new int4(0, 1, 2, 3);
                    int4   bi          = new int4(4, 5, 6, 7);
                    int4   ci          = new int4(8, 9, 10, 11);
                    int4   di          = new int4(12, 13, 14, 14);
                    bool4  bBeatsA     = b > a;
                    float4 bestAB      = math.select(a, b, bBeatsA);
                    int4   bestIndexAB = math.select(ai, bi, bBeatsA);
                    bool4  dBeatsC     = d > c;
                    float4 bestCD      = math.select(c, d, dBeatsC);
                    int4   bestIndexCD = math.select(ci, di, dBeatsC);
                    bool4  cdBeatsAb   = bestCD > bestAB;
                    float4 best4       = math.select(bestAB, bestCD, cdBeatsAb);
                    int4   bestIndex4  = math.select(bestIndexAB, bestIndexCD, cdBeatsAb);
                    separation         = math.cmax(best4);
                    index              = bestIndex4[math.tzcnt(math.bitmask(separation == best4))];
                }

                public BitField32 Cull(float threshold)
                {
                    float4 a      = new float4(axes[0], axes[1], axes[2], axes[3]);
                    float4 b      = new float4(axes[4], axes[5], axes[6], axes[7]);
                    float4 c      = new float4(axes[8], axes[9], axes[10], axes[11]);
                    float4 d      = new float4(axes[12], axes[13], axes[14], axes[14]);
                    int4   ai     = math.select(0, new int4(1, 2, 4, 8), a >= threshold);
                    int4   bi     = math.select(0, new int4(0x10, 0x20, 0x40, 0x80), b >= threshold);
                    int4   ci     = math.select(0, new int4(0x100, 0x200, 0x400, 0x800), c >= threshold);
                    int4   di     = math.select(0, new int4(0x1000, 0x2000, 0x4000, 0x8000), d >= threshold);
                    int    result = math.csum(ai | bi | ci | di);
                    return new BitField32(math.asuint(result));
                }
            }

            public static SatResult TestAllAxes(float3 halfSizeA, float3 halfSizeB, in AffineTransform bInASpace)
            {
                static void Assign(ref SatResult result, int offset, float3 axes)
                {
                    result[offset]     = axes.x;
                    result[offset + 1] = axes.y;
                    result[offset + 2] = axes.z;
                }
                static float3 ScaleValidateEdges(float4 satEdgesResult, simdFloat3 axes)
                {
                    float4 axesLengthSq  = simd.lengthsq(axes);
                    bool4  valid         = axesLengthSq > PointRaySphere.kUnmeasurableLengthSqThreshold;
                    satEdgesResult      *= math.rsqrt(axesLengthSq);
                    return math.select(float.MinValue, satEdgesResult, valid).xyz;
                }
                var       faceAxesBinA = new simdFloat3(bInASpace.rs.c0, bInASpace.rs.c1, bInASpace.rs.c2, bInASpace.rs.c0);
                SatResult result       = default;
                Assign(ref result, 0,  SatA(halfSizeA, halfSizeB, in bInASpace).xyz);
                Assign(ref result, 3,  SatB(halfSizeA, halfSizeB, in faceAxesBinA, bInASpace.t).xyz);
                simdFloat3 rightAxes = simd.rightCross(faceAxesBinA);
                Assign(ref result, 6,  ScaleValidateEdges(SatEdges(rightAxes, halfSizeA, halfSizeB, in bInASpace), rightAxes));
                simdFloat3 upAxes = simd.upCross(faceAxesBinA);
                Assign(ref result, 9,  ScaleValidateEdges(SatEdges(upAxes, halfSizeA, halfSizeB, in bInASpace), upAxes));
                simdFloat3 forwardAxes = simd.forwardCross(faceAxesBinA);
                Assign(ref result, 12, ScaleValidateEdges(SatEdges(forwardAxes, halfSizeA, halfSizeB, in bInASpace), forwardAxes));
                return result;
            }

            public static bool AreOverlapping(float3 halfSizeA, float3 halfSizeB, in AffineTransform bInASpace)
            {
                var faceAxesBinA = new simdFloat3(bInASpace.rs.c0, bInASpace.rs.c1, bInASpace.rs.c2, bInASpace.rs.c0);
                if (math.any(SatA(halfSizeA, halfSizeB, in bInASpace) > 0f))
                    return false;
                if (math.any(SatB(halfSizeA, halfSizeB, in faceAxesBinA, bInASpace.t) > 0f))
                    return false;
                if (math.any(SatEdges(simd.rightCross(faceAxesBinA), halfSizeA, halfSizeB, in bInASpace) > 0f))
                    return false;
                if (math.any(SatEdges(simd.upCross(faceAxesBinA), halfSizeA, halfSizeB, in bInASpace) > 0f))
                    return false;
                return !math.any(SatEdges(simd.forwardCross(faceAxesBinA), halfSizeA, halfSizeB, in bInASpace) > 0f);
            }

            public static float3 GetSeparationAxis(int axisIndex, in AffineTransform bInASpace)
            {
                float3 axis;
                if (axisIndex < 3)
                    axis = math.select(0f, 1f, axisIndex == new int3(0, 1, 2));
                else if (axisIndex < 6)
                    axis = bInASpace.rs[axisIndex - 3];
                else
                {
                    int    edgeIndex = axisIndex - 6;
                    int    aIndex    = edgeIndex / 3;
                    int    bIndex    = edgeIndex % 3;
                    float3 a         = math.select(0f, 1f, aIndex == new int3(0, 1, 2));
                    float3 b         = bInASpace.rs[bIndex];
                    axis             = math.normalize(math.cross(a, b));
                }
                return math.chgsign0Positive(axis, math.dot(axis, bInASpace.t));
            }

            static float4 SatA(float3 halfSizeA, float3 halfSizeB, in AffineTransform bInASpace)
            {
                float4     boxARadiusOnAxes = halfSizeA.xyzx;
                simdFloat3 axesInBSpace     = simd.inverseRotateRightUpForwardRight(bInASpace.rs);
                float4     boxBRadiusOnAxes = simd.dot(simd.abs(axesInBSpace), halfSizeB);
                return math.abs(bInASpace.t.xyzx) - boxARadiusOnAxes - boxBRadiusOnAxes;
            }

            static float4 SatB(float3 halfSizeA, float3 halfSizeB, in simdFloat3 faceAxesBinA, float3 bInAPosition)
            {
                float4 boxARadiusOnAxes = simd.dot(simd.abs(faceAxesBinA), halfSizeA);
                float4 boxBRadiusOnAxes = halfSizeB.xyzx;
                return math.abs(simd.dot(faceAxesBinA, bInAPosition)) - boxARadiusOnAxes - boxBRadiusOnAxes;
            }

            static float4 SatEdges(simdFloat3 axes, float3 halfSizeA, float3 halfSizeB, in AffineTransform bInASpace)
            {
                float4     boxARadiusOnAxes = simd.dot(simd.abs(axes), halfSizeA);
                simdFloat3 axesInBSpace     = simd.inverseRotate(bInASpace.rs, axes);
                float4     boxBRadiusOnAxes = simd.dot(simd.abs(axesInBSpace), halfSizeB);
                // When the distance B's center is from A's along the axis is greater than the combined box extents along the axis, there is separation.
                return math.abs(simd.dot(axes, bInASpace.t)) - boxARadiusOnAxes - boxBRadiusOnAxes;
            }

            // With SAT, we are comparing the projection of box B's center relative to the radii of the two boxes as projected along the axis,
            // assuming A's center is at the origin. Interestingly, this means we can define box B's center as a parameter function and solve
            // for that parameter when the axis detects overlap. By making this parameter function describe the collider cast sweeping position,
            // and computing this overlap region for all 15 axes, we can solve the ColliderCast hit fraction.
            public static bool CastAllAxes(float3 halfSizeA, float3 halfSizeB, in AffineTransform bInASpace, float3 castDisplacement, out float fraction)
            {
                var faceAxesBinA = new simdFloat3(bInASpace.rs.c0, bInASpace.rs.c1, bInASpace.rs.c2, bInASpace.rs.c0);
                SatCastA(halfSizeA, halfSizeB, in bInASpace, castDisplacement, out var minFractionA, out var maxFractionA);
                SatCastB(halfSizeA, halfSizeB, in faceAxesBinA, bInASpace.t, castDisplacement, out var minFractionB, out var maxFractionB);
                SatCastEdges(simd.rightCross(faceAxesBinA),   halfSizeA, halfSizeB, in bInASpace, castDisplacement, out var minFractionX, out var maxFractionX);
                SatCastEdges(simd.upCross(faceAxesBinA),      halfSizeA, halfSizeB, in bInASpace, castDisplacement, out var minFractionY, out var maxFractionY);
                SatCastEdges(simd.forwardCross(faceAxesBinA), halfSizeA, halfSizeB, in bInASpace, castDisplacement, out var minFractionZ, out var maxFractionZ);
                float min = math.cmax(math.max(math.max(math.max(minFractionA, minFractionB), math.max(minFractionX, minFractionY)), minFractionZ));
                float max = math.cmin(math.min(math.min(math.min(maxFractionA, maxFractionB), math.min(maxFractionX, maxFractionY)), maxFractionZ));
                fraction  = min;
                return min > 0f && min <= math.min(max, 1f);
            }

            static void GetCastInterval(float4 centerProjected, float4 castProjection, float4 sumRadii, out float4 minFraction, out float4 maxFraction)
            {
                // The boxes overlap when abs(centerProjected - fraction * castProjection) <= radiusA + radiusB
                // Therefore, our fraction interval is (centerProjected - radiusA - radiusB) / castProjection
                // and (centerProjected + radiusA + radiusB) / castProjection
                float4 low                = (centerProjected - sumRadii) / castProjection;
                float4 high               = (centerProjected + sumRadii) / castProjection;
                bool4  castOrthogonal     = castProjection == 0f;
                float4 orthogonalFraction = math.select(float.MaxValue, float.MinValue, math.abs(centerProjected) <= sumRadii);
                minFraction               = math.select(math.min(low, high), orthogonalFraction, castOrthogonal);
                maxFraction               = math.select(math.max(low, high), -orthogonalFraction, castOrthogonal);
            }

            static void SatCastA(float3 halfSizeA, float3 halfSizeB, in AffineTransform bInASpace, float3 castDisplacement, out float4 minFraction, out float4 maxFraction)
            {
                float4     boxARadiusOnAxes = halfSizeA.xyzx;
                simdFloat3 axesInBSpace     = simd.inverseRotateRightUpForwardRight(bInASpace.rs);
                float4     boxBRadiusOnAxes = simd.dot(simd.abs(axesInBSpace), halfSizeB);
                float4     castProjection   = castDisplacement.xyzx;
                GetCastInterval(bInASpace.t.xyzx, castProjection, boxARadiusOnAxes + boxBRadiusOnAxes, out minFraction, out maxFraction);
            }

            static void SatCastB(float3 halfSizeA,
                                 float3 halfSizeB,
                                 in simdFloat3 faceAxesBinA,
                                 float3 bInAPosition,
                                 float3 castDisplacement,
                                 out float4 minFraction,
                                 out float4 maxFraction)
            {
                float4 boxARadiusOnAxes = simd.dot(simd.abs(faceAxesBinA), halfSizeA);
                float4 boxBRadiusOnAxes = halfSizeB.xyzx;
                float4 centerProjected  = simd.dot(faceAxesBinA, bInAPosition);
                float4 castProjection   = simd.dot(faceAxesBinA, castDisplacement);
                GetCastInterval(centerProjected, castProjection, boxARadiusOnAxes + boxBRadiusOnAxes, out minFraction, out maxFraction);
            }

            static void SatCastEdges(simdFloat3 axes,
                                     float3 halfSizeA,
                                     float3 halfSizeB,
                                     in AffineTransform bInASpace,
                                     float3 castDisplacement,
                                     out float4 minFraction,
                                     out float4 maxFraction)
            {
                float4     boxARadiusOnAxes = simd.dot(simd.abs(axes), halfSizeA);
                simdFloat3 axesInBSpace     = simd.inverseRotate(bInASpace.rs, axes);
                float4     boxBRadiusOnAxes = simd.dot(simd.abs(axesInBSpace), halfSizeB);
                float4     centerProjected  = simd.dot(axes, bInASpace.t);
                float4     castProjection   = simd.dot(axes, castDisplacement);
                GetCastInterval(centerProjected, castProjection, boxARadiusOnAxes + boxBRadiusOnAxes, out minFraction, out maxFraction);
            }
        }

        // We use this separated path both to handle overlapping and separated queries. For overlapping, we use the SAT to separate them first, then run this algorithm.
        private static bool BoxBoxDistanceSeparated(float3 halfSizeA,
                                                    float3 halfSizeB,
                                                    AffineTransform bInASpace,
                                                    float maxDistance,
                                                    BitField32 validAxesBitfield,
                                                    out ColliderDistanceResultInternal result,
                                                    bool distanceOnly = false)
        {
            // Edge pairs.
            simdFloat3 bAxes = new simdFloat3(bInASpace.rs.c0, bInASpace.rs.c1, bInASpace.rs.c2, bInASpace.rs.c0);

            // Local Edge Helpers. These are needed because Burst doesn't like to unroll the loops.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void SelectEdgeEdgeSupports(simdFloat3 crossAxes,
                                        bool3 aAxisMask,
                                        out simdFloat3 aEdgeSupportCenters,
                                        out simdFloat3 aAlternateSupportCenters,
                                        out simdFloat3 bEdgeSupportCentersInB,
                                        out simdFloat3 bAlternateSupportCentersInB)
            {
                simdFloat3 crossAxesInB = simd.inverseRotate(bInASpace.rs, crossAxes);
                aEdgeSupportCenters     = simd.select(simd.chgsign0Positive(halfSizeA, crossAxes), default, aAxisMask);
                bEdgeSupportCentersInB  = simd.chgsign0Positive(halfSizeB, -crossAxesInB);
                bEdgeSupportCentersInB  = simd.select(bEdgeSupportCentersInB,
                                                      default,
                                                      new bool4(true, false, false, true),
                                                      new bool4(false, true, false, false),
                                                      new bool4(false, false, true, false));

                simdFloat3 aAlternateSupportMagnitudes = simd.select(simd.abs(crossAxes), new simdFloat3(float.MaxValue), aAxisMask);
                bool4      aFlipX                      = aAlternateSupportMagnitudes.x <= aAlternateSupportMagnitudes.y & aAlternateSupportMagnitudes.x <=
                                                    aAlternateSupportMagnitudes.z;
                bool4 aFlipY             = !aFlipX & aAlternateSupportMagnitudes.y <= aAlternateSupportMagnitudes.z;
                bool4 aFlipZ             = !aFlipX & !aFlipY;
                aAlternateSupportCenters = simd.select(aEdgeSupportCenters, -aEdgeSupportCenters, aFlipX, aFlipY, aFlipZ);

                simdFloat3 bAlternateSupportMagnitudes =
                    simd.select(simd.abs(crossAxesInB), new simdFloat3(float.MaxValue), new bool4(true, false, false, true), new bool4(false, true, false, false),
                                new bool4(false, false, true, false));
                bool4 bFlipX = bAlternateSupportMagnitudes.x <= bAlternateSupportMagnitudes.y & bAlternateSupportMagnitudes.x <=
                               bAlternateSupportMagnitudes.z;
                bool4 bFlipY                = !bFlipX & bAlternateSupportMagnitudes.y <= bAlternateSupportMagnitudes.z;
                bool4 bFlipZ                = !bFlipX & !bFlipY;
                bAlternateSupportCentersInB = simd.select(bEdgeSupportCentersInB, -bEdgeSupportCentersInB, bFlipX, bFlipY, bFlipZ);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static float3x4 EdgeEdgePackConfig(float axisHalfSizeA, float axisHalfSizeB, float3 crossAxis, float3 crossAxisCrossADir, float3 crossAxisCrossBDir)
            {
                return new float3x4(crossAxis, crossAxisCrossADir, crossAxisCrossBDir, new float3(axisHalfSizeA, axisHalfSizeB, 0f));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float4 EdgeEdgeDistanceSqsOnAxis(float3x4 config, simdFloat3 edgeCenters, out bool absoluteClosestFound)
            {
                float3 crossAxis          = config.c0;
                float3 crossAxisCrossADir = config.c1;
                float3 crossAxisCrossBDir = config.c2;
                float  axisHalfSizeA      = config.c3.x;
                float  axisHalfSizeB      = config.c3.y;

                simdFloat3 aEdgeCenters     = edgeCenters.abab;
                simdFloat3 bEdgeCentersInB  = edgeCenters.ccdd;
                simdFloat3 bEdgeCenters     = simd.transform(bInASpace, bEdgeCentersInB);
                simdFloat3 centerAToCenterB = bEdgeCenters - aEdgeCenters;

                // closestA = centerA + distanceAlongA * edgeDirectionA
                // closestB = centerB + distanceAlongB * edgeDirectionB
                // Traveling from centerA to centerB can be imagined in three segments: centerA to closestA, closestA to closestB, and closestB to centerB.
                // Therefore centerAToCenterB = distanceAlongA * edgeDirectionA + closestAToClosestB - distanceAlongB * edgeDirectionB
                // closestAToClosestB occurs on the crossAxis direction. It's squared magnitude is what we are after.
                // But we also want to know if distanceAlongA and distanceAlongB stay within their respective edges.
                // edgeDirectionB and crossAxis lie in the same plane as the latter two travel segments. The first travel segment must also reach this plane.
                // We find the distance from the center to this plane from centerA. Same for B.
                float4 distanceAlongAScaled = simd.dot(centerAToCenterB, crossAxisCrossBDir);
                float4 distanceAlongBScaled = simd.dot(centerAToCenterB, crossAxisCrossADir);
                float4 separation           = simd.dot(centerAToCenterB, crossAxis);
                // Because B's edge direction is unit length and perpendicular to crossAxis, the magnitude of crossAxisCrossBDir matches the magnitude of crossAxis.
                // Because both planeNormal and A's edge direction are perpendicular to crossAxis, A's edge direction is composed by some factor along the B's edge direction,
                // and some factor along the planeNormal. The angle along the planeNormal is sin(angleBetweenEdgeDirections).
                // The magnitude of crossAxis is also sin(angleBetweenEdgeDirections). Therefore the magnitude of the projection of A's edge along the planeNormal is
                // the magnitude of crossAxis.
                // The scale of the dot product is the magnitude of A's edge projected onto the scaled planeNormal, where the plane normal is scaled by the magnitude of
                // crossAxisCrossBDir. That is, scale = dot(aEdgeDir, planeNormal) * length(crossAxisCrossBDir) = math.length(crossAxis) * math.length(crossAxis).
                // This derivation also works with A's and B's edge roles swapped.
                // Another way to think about it is that the scale is dot(aEdgeDir, crossAxisCrossBDir) = dot(aEdgeDir, cross(crossAxis, bEdgeDir)) which via triple
                // product rule equals dot(crossAxis, cross(aEdgeDir, bEdgeDir)) where the cross product has a known magnitude of being the dot product.
                float4 scale = math.lengthsq(crossAxis);
                // With the scale, we can compare against the actual edge lengths
                bool4 isNotEndpoint  = math.abs(distanceAlongAScaled) < axisHalfSizeA * scale;
                isNotEndpoint       &= math.abs(distanceAlongBScaled) < axisHalfSizeB * scale;
                // If the closest points are not endpoints, and there's a positive separation axis, then the edge pair is guaranteed the closest, and closer than any vertex-box pair.
                // This only applies to the primary supports.
                absoluteClosestFound = isNotEndpoint.x & (separation.x > 0f);
                float4 distanceSq    = separation * separation / scale;
                return math.select(float.MaxValue, distanceSq, isNotEndpoint);
            }

            // Ax vs Bs
            float4 edgeEdgeXXDistSq       = float.MaxValue;
            float4 edgeEdgeXYDistSq       = float.MaxValue;
            float4 edgeEdgeXZDistSq       = float.MaxValue;
            bool   absoluteClosestFoundXX = false;
            bool   absoluteClosestFoundXY = false;
            bool   absoluteClosestFoundXZ = false;
            if (validAxesBitfield.TestAny(6, 3))
            {
                simdFloat3 aXCrossBs = simd.rightCross(bAxes);
                // Ensure each axis points such that B's center is ahead of A's center
                aXCrossBs = simd.chgsign0Positive(aXCrossBs, simd.dot(aXCrossBs, bInASpace.t));
                simdFloat3 aXCrossAXCrossBs = simd.rightCross(aXCrossBs);
                simdFloat3 bCrossAXCrossBs  = simd.cross(bAxes, aXCrossBs);
                SelectEdgeEdgeSupports(aXCrossBs,
                                       new bool3(true, false, false),
                                       out simdFloat3 aEdgeSupportCentersX,
                                       out simdFloat3 aEdgeAltSupportCentersX,
                                       out simdFloat3 bEdgeSupportCentersX,
                                       out simdFloat3 bEdgeAltSupportCentersX);

                if (validAxesBitfield.IsSet(6))
                    edgeEdgeXXDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.x, halfSizeB.x, aXCrossBs.a, aXCrossAXCrossBs.a, bCrossAXCrossBs.a),
                                                                 new simdFloat3(aEdgeSupportCentersX.a, aEdgeAltSupportCentersX.a, bEdgeSupportCentersX.a,
                                                                                bEdgeAltSupportCentersX.a),
                                                                 out absoluteClosestFoundXX);
                if (validAxesBitfield.IsSet(7))
                    edgeEdgeXYDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.x, halfSizeB.y, aXCrossBs.b, aXCrossAXCrossBs.b, bCrossAXCrossBs.b),
                                                                 new simdFloat3(aEdgeSupportCentersX.b, aEdgeAltSupportCentersX.b, bEdgeSupportCentersX.b,
                                                                                bEdgeAltSupportCentersX.b),
                                                                 out absoluteClosestFoundXY);
                if (validAxesBitfield.IsSet(8))
                    edgeEdgeXZDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.x, halfSizeB.z, aXCrossBs.c, aXCrossAXCrossBs.c, bCrossAXCrossBs.c),
                                                                 new simdFloat3(aEdgeSupportCentersX.c, aEdgeAltSupportCentersX.c, bEdgeSupportCentersX.c,
                                                                                bEdgeAltSupportCentersX.c),
                                                                 out absoluteClosestFoundXZ);
            }

            // Ay vs Bs
            float4 edgeEdgeYXDistSq       = float.MaxValue;
            float4 edgeEdgeYYDistSq       = float.MaxValue;
            float4 edgeEdgeYZDistSq       = float.MaxValue;
            bool   absoluteClosestFoundYX = false;
            bool   absoluteClosestFoundYY = false;
            bool   absoluteClosestFoundYZ = false;
            if (validAxesBitfield.TestAny(9, 3))
            {
                simdFloat3 aYCrossBs = simd.upCross(bAxes);
                aYCrossBs = simd.chgsign0Positive(aYCrossBs, simd.dot(aYCrossBs, bInASpace.t));
                simdFloat3 aYCrossAYCrossBs = simd.upCross(aYCrossBs);
                simdFloat3 bCrossAYCrossBs  = simd.cross(bAxes, aYCrossBs);
                SelectEdgeEdgeSupports(aYCrossBs,
                                       new bool3(false, true, false),
                                       out simdFloat3 aEdgeSupportCentersY,
                                       out simdFloat3 aEdgeAltSupportCentersY,
                                       out simdFloat3 bEdgeSupportCentersY,
                                       out simdFloat3 bEdgeAltSupportCentersY);
                if (validAxesBitfield.IsSet(9))
                    edgeEdgeYXDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.y, halfSizeB.x, aYCrossBs.a, aYCrossAYCrossBs.a, bCrossAYCrossBs.a),
                                                                 new simdFloat3(aEdgeSupportCentersY.a, aEdgeAltSupportCentersY.a, bEdgeSupportCentersY.a,
                                                                                bEdgeAltSupportCentersY.a),
                                                                 out absoluteClosestFoundYX);
                if (validAxesBitfield.IsSet(10))
                    edgeEdgeYYDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.y, halfSizeB.y, aYCrossBs.b, aYCrossAYCrossBs.b, bCrossAYCrossBs.b),
                                                                 new simdFloat3(aEdgeSupportCentersY.b, aEdgeAltSupportCentersY.b, bEdgeSupportCentersY.b,
                                                                                bEdgeAltSupportCentersY.b),
                                                                 out absoluteClosestFoundYY);
                if (validAxesBitfield.IsSet(11))
                    edgeEdgeYZDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.y, halfSizeB.z, aYCrossBs.c, aYCrossAYCrossBs.c, bCrossAYCrossBs.c),
                                                                 new simdFloat3(aEdgeSupportCentersY.c, aEdgeAltSupportCentersY.c, bEdgeSupportCentersY.c,
                                                                                bEdgeAltSupportCentersY.c),
                                                                 out absoluteClosestFoundYZ);
            }

            // Az vs Bs
            float4 edgeEdgeZXDistSq       = float.MaxValue;
            float4 edgeEdgeZYDistSq       = float.MaxValue;
            float4 edgeEdgeZZDistSq       = float.MaxValue;
            bool   absoluteClosestFoundZX = false;
            bool   absoluteClosestFoundZY = false;
            bool   absoluteClosestFoundZZ = false;
            if (validAxesBitfield.TestAny(12, 3))
            {
                simdFloat3 aZCrossBs = simd.forwardCross(bAxes);
                aZCrossBs = simd.chgsign0Positive(aZCrossBs, simd.dot(aZCrossBs, bInASpace.t));
                simdFloat3 aZCrossAZCrossBs = simd.forwardCross(aZCrossBs);
                simdFloat3 bCrossAZCrossBs  = simd.cross(bAxes, aZCrossBs);
                SelectEdgeEdgeSupports(aZCrossBs,
                                       new bool3(false, false, true),
                                       out simdFloat3 aEdgeSupportCentersZ,
                                       out simdFloat3 aEdgeAltSupportCentersZ,
                                       out simdFloat3 bEdgeSupportCentersZ,
                                       out simdFloat3 bEdgeAltSupportCentersZ);
                if (validAxesBitfield.IsSet(12))
                    edgeEdgeZXDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.z, halfSizeB.x, aZCrossBs.a, aZCrossAZCrossBs.a, bCrossAZCrossBs.a),
                                                                 new simdFloat3(aEdgeSupportCentersZ.a, aEdgeAltSupportCentersZ.a, bEdgeSupportCentersZ.a,
                                                                                bEdgeAltSupportCentersZ.a),
                                                                 out absoluteClosestFoundZX);
                if (validAxesBitfield.IsSet(13))
                    edgeEdgeZYDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.z, halfSizeB.y, aZCrossBs.b, aZCrossAZCrossBs.b, bCrossAZCrossBs.b),
                                                                 new simdFloat3(aEdgeSupportCentersZ.b, aEdgeAltSupportCentersZ.b, bEdgeSupportCentersZ.b,
                                                                                bEdgeAltSupportCentersZ.b),
                                                                 out absoluteClosestFoundZY);
                if (validAxesBitfield.IsSet(14))
                    edgeEdgeZZDistSq = EdgeEdgeDistanceSqsOnAxis(EdgeEdgePackConfig(halfSizeA.z, halfSizeB.z, aZCrossBs.c, aZCrossAZCrossBs.c, bCrossAZCrossBs.c),
                                                                 new simdFloat3(aEdgeSupportCentersZ.c, aEdgeAltSupportCentersZ.c, bEdgeSupportCentersZ.c,
                                                                                bEdgeAltSupportCentersZ.c),
                                                                 out absoluteClosestFoundZZ);
            }

            // Reduce edges
            bool4  axbyBeatsX   = edgeEdgeXYDistSq < edgeEdgeXXDistSq;
            float4 bestAxDistSq = math.select(edgeEdgeXXDistSq, edgeEdgeXYDistSq, axbyBeatsX);
            int4   bestAxCode   = math.select(int4.zero, 1, axbyBeatsX);
            bool4  axbzWins     = edgeEdgeXZDistSq < bestAxDistSq;
            bestAxDistSq = math.select(bestAxDistSq, edgeEdgeXZDistSq, axbzWins);
            bestAxCode   = math.select(bestAxCode, 2, axbzWins);

            bool4  aybyBeatsX   = edgeEdgeYYDistSq < edgeEdgeYXDistSq;
            float4 bestAyDistSq = math.select(edgeEdgeYXDistSq, edgeEdgeYYDistSq, aybyBeatsX);
            int4   bestAyCode   = math.select(int4.zero, 1, aybyBeatsX);
            bool4  aybzWins     = edgeEdgeYZDistSq < bestAyDistSq;
            bestAyDistSq = math.select(bestAyDistSq, edgeEdgeYZDistSq, aybzWins);
            bestAyCode   = math.select(bestAyCode, 2, aybzWins);
            bestAyCode  += 4;

            bool4  azbyBeatsX   = edgeEdgeZYDistSq < edgeEdgeZXDistSq;
            float4 bestAzDistSq = math.select(edgeEdgeZXDistSq, edgeEdgeZYDistSq, azbyBeatsX);
            int4   bestAzCode   = math.select(int4.zero, 1, azbyBeatsX);
            bool4  azbzWins     = edgeEdgeZZDistSq < bestAzDistSq;
            bestAzDistSq = math.select(bestAzDistSq, edgeEdgeZZDistSq, azbzWins);
            bestAzCode   = math.select(bestAzCode, 2, azbzWins);
            bestAzCode  += 8;

            bool4  ayBeatsX        = bestAyDistSq < bestAxDistSq;
            float4 edgesBestDistSq = math.select(bestAxDistSq, bestAyDistSq, ayBeatsX);
            int4   edgesBestCodes  = math.select(bestAxCode, bestAyCode, ayBeatsX);
            bool4  azWins          = bestAzDistSq < edgesBestDistSq;
            edgesBestDistSq = math.select(edgesBestDistSq, bestAzDistSq, azWins);
            edgesBestCodes  = math.select(edgesBestCodes, bestAzCode, azWins);

            float bestEdgeDistSq             = math.cmin(edgesBestDistSq);
            bool  absoluteClosestFoundIsEdge = absoluteClosestFoundXX | absoluteClosestFoundXY | absoluteClosestFoundXZ |
                                              absoluteClosestFoundYX | absoluteClosestFoundYY | absoluteClosestFoundYZ |
                                              absoluteClosestFoundZX | absoluteClosestFoundZY | absoluteClosestFoundZZ;

            // Vertices vs boxes unless we know we found the edge already
            float      bestVertexDistSq    = float.MaxValue;
            float4     verticesBestDistSq  = float.MaxValue;
            simdFloat3 verticesClosestA    = default;
            simdFloat3 verticesClosestBInB = default;
            if (!absoluteClosestFoundIsEdge)
            {
                simdFloat3 verticesSigns03 = new simdFloat3(new float3(1f, 1f, 1f), new float3(-1f, 1f, 1f), new float3(1f, -1f, 1f), new float3(-1f, -1f, 1f));
                simdFloat3 verticesSigns47 = verticesSigns03;
                verticesSigns47.z = -verticesSigns47.z;

                // Test B's vertices against box A
                simdFloat3 bVertices03InB     = simd.chgsign(halfSizeB, verticesSigns03);
                simdFloat3 bVertices47InB     = simd.chgsign(halfSizeB, verticesSigns47);
                simdFloat3 bVertices03        = simd.transform(bInASpace, bVertices03InB);
                simdFloat3 bVertices47        = simd.transform(bInASpace, bVertices47InB);
                simdFloat3 bVertices03Clamped = simd.clamp(bVertices03, -halfSizeA, halfSizeA);
                simdFloat3 bVertices47Clamped = simd.clamp(bVertices47, -halfSizeA, halfSizeA);
                float4     bVerticesDistSq03  = simd.distancesq(bVertices03, bVertices03Clamped);
                float4     bVerticesDistSq47  = simd.distancesq(bVertices47, bVertices47Clamped);
                bool4      bVertices47Wins    = bVerticesDistSq47 < bVerticesDistSq03;
                verticesBestDistSq = math.select(bVerticesDistSq03, bVerticesDistSq47, bVertices47Wins);
                // Todo: Would it be better to remember vertex indices rather than simd select the actual vertices?
                verticesClosestA    = simd.select(bVertices03Clamped, bVertices47Clamped, bVertices47Wins);
                verticesClosestBInB = simd.select(bVertices03InB, bVertices47InB, bVertices47Wins);

                // Test A's vertices against box B
                simdFloat3 aVertices03           = simd.chgsign(halfSizeA, verticesSigns03);
                simdFloat3 aVertices47           = simd.chgsign(halfSizeA, verticesSigns47);
                simdFloat3 aVertices03InB        = simd.inverseTransformIdentityScale(bInASpace, aVertices03);
                simdFloat3 aVertices47InB        = simd.inverseTransformIdentityScale(bInASpace, aVertices47);
                simdFloat3 aVertices03InBClamped = simd.clamp(aVertices03InB, -halfSizeB, halfSizeB);
                simdFloat3 aVertices47InBClamped = simd.clamp(aVertices47InB, -halfSizeB, halfSizeB);
                float4     aVerticesDistSq03     = simd.distancesq(aVertices03InB, aVertices03InBClamped);
                float4     aVerticesDistSq47     = simd.distancesq(aVertices47InB, aVertices47InBClamped);
                bool4      aVertices03Wins       = aVerticesDistSq03 < verticesBestDistSq;
                verticesBestDistSq  = math.select(verticesBestDistSq, aVerticesDistSq03, aVertices03Wins);
                verticesClosestA    = simd.select(verticesClosestA, aVertices03, aVertices03Wins);
                verticesClosestBInB = simd.select(verticesClosestBInB, aVertices03InBClamped, aVertices03Wins);
                bool4 aVertices47Wins = aVerticesDistSq47 < verticesBestDistSq;
                verticesBestDistSq  = math.select(verticesBestDistSq, aVerticesDistSq47, aVertices47Wins);
                verticesClosestA    = simd.select(verticesClosestA, aVertices47, aVertices47Wins);
                verticesClosestBInB = simd.select(verticesClosestBInB, aVertices47InBClamped, aVertices47Wins);
                bestVertexDistSq    = math.cmin(verticesBestDistSq);
            }

            // If we couldn't find a good result, return early. Note that the vertex path can detect penetration if its squared distance is 0, so we have to ensure it is above that.
            if (math.min(bestVertexDistSq, bestEdgeDistSq) > math.max(0f, maxDistance * math.abs(maxDistance)))
            {
                result = default;
                return false;
            }
            // Finalize the winner.
            float  distSq;
            float3 closestA, closestBInB;
            float3 normalA, normalBInB;
            ushort featureCodeA, featureCodeB;
            if (bestVertexDistSq <= bestEdgeDistSq)
            {
                int bestLane = math.tzcnt(math.bitmask(bestVertexDistSq == verticesBestDistSq));
                distSq      = bestVertexDistSq;
                closestA    = verticesClosestA[bestLane];
                closestBInB = verticesClosestBInB[bestLane];
                if (Hint.Unlikely(distSq == 0f))
                {
                    // If distSq is 0, then neither point was moved by clamping, which means one vertex must be inside the other box.
                    float3 depthA = halfSizeA - math.abs(closestA);
                    float3 depthB = halfSizeB - math.abs(closestBInB);
                    if (math.all(depthA > 0f))
                    {
                        int axis = math.tzcnt(math.bitmask(new bool4(depthA == math.cmin(depthA), false)));
                        closestA[axis] = math.chgsign(halfSizeA[axis], closestA[axis]);
                        float             depth = depthA[axis];
                        distSq = -depth * depth;
                    }
                    else if (math.all(depthB > 0f))
                    {
                        int axis = math.tzcnt(math.bitmask(new bool4(depthB == math.cmin(depthB), false)));
                        closestBInB[axis] = math.chgsign(halfSizeB[axis], closestBInB[axis]);
                        float             depth = depthB[axis];
                        distSq = -depth * depth;
                    }
                }
                if (distanceOnly)
                {
                    result          = default;
                    result.distance = math.chgsign(math.sqrt(math.abs(distSq)), distSq);
                    return result.distance <= maxDistance;
                }

                featureCodeA = PointRayBox.FeatureCodeAndNormalFromCenterRelativeSurfacePoint(closestA, halfSizeA, out normalA);
                featureCodeB = PointRayBox.FeatureCodeAndNormalFromCenterRelativeSurfacePoint(closestBInB, halfSizeB, out normalBInB);
            }
            else
            {
                distSq = bestEdgeDistSq;
                if (distanceOnly)
                {
                    result          = default;
                    result.distance = math.sqrt(distSq);
                    return result.distance <= maxDistance;
                }

                int    bestLane            = math.tzcnt(math.bitmask(bestEdgeDistSq == edgesBestDistSq));
                int    crossAxisIndex      = edgesBestCodes[bestLane];
                int    aAxisIndex          = crossAxisIndex / 4;
                int    bAxisIndex          = crossAxisIndex % 4;
                bool3  aAxisMask           = aAxisIndex == new int3(0, 1, 2);
                bool3  bAxisMask           = bAxisIndex == new int3(0, 1, 2);
                float3 aAxis               = math.select(0f, 1f, aAxisMask);
                float3 bAxis               = bInASpace.rs[bAxisIndex];
                float3 crossAxis           = math.cross(aAxis, bAxis);
                float3 crossAxisSupportDir = math.chgsign0Positive(crossAxis, math.dot(crossAxis, bInASpace.t));

                float3 crossAxisInB          = mathex.InverseRotate(bAxes, crossAxisSupportDir);
                float3 aEdgeSupportCenter    = math.select(math.chgsign0Positive(halfSizeA, crossAxisSupportDir), default, aAxisMask);
                float3 bEdgeSupportCenterInB = math.select(math.chgsign0Positive(halfSizeB, -crossAxisInB), default, bAxisMask);
                if ((bestLane & 1) != 0)
                {
                    float3 aAlternateSupportMagnitudes = math.select(math.abs(crossAxisSupportDir), float.MaxValue, aAxisMask);
                    bool   aFlipX                      = aAlternateSupportMagnitudes.x <= aAlternateSupportMagnitudes.y & aAlternateSupportMagnitudes.x <=
                                                       aAlternateSupportMagnitudes.z;
                    bool aFlipY = !aFlipX & aAlternateSupportMagnitudes.y <= aAlternateSupportMagnitudes.z;
                    bool aFlipZ = !aFlipX & !aFlipY;
                    aEdgeSupportCenter = math.select(aEdgeSupportCenter, -aEdgeSupportCenter, new bool3(aFlipX, aFlipY, aFlipZ));
                }
                if ((bestLane & 2) != 0)
                {
                    float3 bAlternateSupportMagnitudes = math.select(math.abs(crossAxisInB), float.MaxValue, bAxisMask);
                    bool   bFlipX                      = bAlternateSupportMagnitudes.x <= bAlternateSupportMagnitudes.y & bAlternateSupportMagnitudes.x <=
                                                       bAlternateSupportMagnitudes.z;
                    bool bFlipY = !bFlipX & bAlternateSupportMagnitudes.y <= bAlternateSupportMagnitudes.z;
                    bool bFlipZ = !bFlipX & !bFlipY;
                    bEdgeSupportCenterInB = math.select(bEdgeSupportCenterInB, -bEdgeSupportCenterInB, new bool3(bFlipX, bFlipY, bFlipZ));
                }
                float  crossAxisLengthSq = math.lengthsq(crossAxis);
                float3 centerAToCenterB  = math.transform(bInASpace, bEdgeSupportCenterInB) - aEdgeSupportCenter;
                closestA                = aEdgeSupportCenter;
                closestA[aAxisIndex]    = math.dot(centerAToCenterB, math.cross(bAxis, crossAxis)) / crossAxisLengthSq;
                closestBInB             = bEdgeSupportCenterInB;
                closestBInB[bAxisIndex] = math.dot(centerAToCenterB, math.cross(aAxis, crossAxis)) / crossAxisLengthSq;
                float root2over2 = math.SQRT2 / 2f;
                normalA      = math.select(math.chgsign(root2over2, aEdgeSupportCenter), 0f, aAxisMask);
                normalBInB   = math.select(math.chgsign(root2over2, bEdgeSupportCenterInB), 0f, bAxisMask);
                featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(normalA);
                featureCodeB = PointRayBox.FeatureCodeFromBoxNormal(normalBInB);
            }
            result = new ColliderDistanceResultInternal
            {
                hitpointA    = closestA,
                hitpointB    = math.transform(bInASpace, closestBInB),
                normalA      = normalA,
                normalB      = math.mul(bInASpace.rs, normalBInB),
                distance     = math.chgsign(math.sqrt(math.abs(distSq)), distSq),
                featureCodeA = featureCodeA,
                featureCodeB = featureCodeB,
            };
            // In the case of A projecting into B's space, transforming back into A's space can skew the difference between the two.
            // We refine the result here.
            if (featureCodeA < 8 && (featureCodeB >= 0x8000))
                result.hitpointB = result.hitpointA - result.normalB * result.distance;
            return result.distance <= maxDistance;
        }
    }
}

