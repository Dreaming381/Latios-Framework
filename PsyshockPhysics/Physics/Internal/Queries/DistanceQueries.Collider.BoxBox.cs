using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class DistanceQueries
    {
        public static bool DistanceBetween(BoxCollider boxA,
                                           BoxCollider boxB,
                                           RigidTransform bInASpace,
                                           RigidTransform aInBSpace,
                                           float maxDistance,
                                           out ColliderDistanceResultInternal result)
        {
            //Step 1: Points vs faces
            simdFloat3 bTopPoints     = default;
            simdFloat3 bBottomPoints  = default;
            bTopPoints.x              = math.select(-boxB.halfSize.x, boxB.halfSize.x, new bool4(true, true, false, false));
            bBottomPoints.x           = bTopPoints.x;
            bBottomPoints.y           = -boxB.halfSize.y;
            bTopPoints.y              = boxB.halfSize.y;
            bTopPoints.z              = math.select(-boxB.halfSize.z, boxB.halfSize.z, new bool4(true, false, true, false));
            bBottomPoints.z           = bTopPoints.z;
            bTopPoints               += boxB.center;
            bBottomPoints            += boxB.center;
            var bTopPointsInAOS       = simd.transform(bInASpace, bTopPoints) - boxA.center;  //OS = origin space
            var bBottomPointsInAOS    = simd.transform(bInASpace, bBottomPoints) - boxA.center;

            QueriesLowLevelUtils.OriginAabb8Points(boxA.halfSize,
                                                   bTopPointsInAOS,
                                                   bBottomPointsInAOS,
                                                   out float3 pointsClosestAInA,
                                                   out float3 pointsClosestBInA,
                                                   out float pointsAxisDistanceInA);
            float pointsSignedDistanceSqInA = math.distancesq(pointsClosestAInA, pointsClosestBInA);
            pointsSignedDistanceSqInA       = math.select(pointsSignedDistanceSqInA, -pointsSignedDistanceSqInA, pointsAxisDistanceInA <= 0f);
            bool4 bTopMatch                 = bTopPointsInAOS == pointsClosestBInA;
            bool4 bBottomMatch              = bBottomPointsInAOS == pointsClosestBInA;
            int   bInABIndex                = math.tzcnt((math.bitmask(bBottomMatch) << 4) | math.bitmask(bTopMatch));

            simdFloat3 aTopPoints     = default;
            simdFloat3 aBottomPoints  = default;
            aTopPoints.x              = math.select(-boxA.halfSize.x, boxA.halfSize.x, new bool4(true, true, false, false));
            aBottomPoints.x           = aTopPoints.x;
            aBottomPoints.y           = -boxA.halfSize.y;
            aTopPoints.y              = boxA.halfSize.y;
            aTopPoints.z              = math.select(-boxA.halfSize.z, boxA.halfSize.z, new bool4(true, false, true, false));
            aBottomPoints.z           = aTopPoints.z;
            aTopPoints               += boxA.center;
            aBottomPoints            += boxA.center;
            var aTopPointsInBOS       = simd.transform(aInBSpace, aTopPoints) - boxB.center;
            var aBottomPointsInBOS    = simd.transform(aInBSpace, aBottomPoints) - boxB.center;

            QueriesLowLevelUtils.OriginAabb8Points(boxB.halfSize,
                                                   aTopPointsInBOS,
                                                   aBottomPointsInBOS,
                                                   out float3 pointsClosestBInB,
                                                   out float3 pointsClosestAInB,
                                                   out float pointsAxisDistanceInB);
            float pointsSignedDistanceSqInB = math.distancesq(pointsClosestAInB, pointsClosestBInB);
            pointsSignedDistanceSqInB       = math.select(pointsSignedDistanceSqInB, -pointsSignedDistanceSqInB, pointsAxisDistanceInB <= 0f);
            bool4 aTopMatch                 = aTopPointsInBOS == pointsClosestAInB;
            bool4 aBottomMatch              = aBottomPointsInBOS == pointsClosestAInB;
            int   aInBAIndex                = math.tzcnt((math.bitmask(aBottomMatch) << 4) | math.bitmask(aTopMatch));

            //Step 2: Edges vs edges

            //For any pair of normals, if the normals are colinear, then there must also exist a point-face pair that is equidistant.
            //However, for a pair of normals, up to two edges from each box can be valid.
            //For box A, assemble the points and edges procedurally using the box dimensions.
            //For box B, use a simd dot product and mask it against the best result. The first 1 index and last 1 index are taken. In most cases, these are the same, which is fine.
            //It is also worth noting that unlike a true SAT, directionality matters here, so we want to find the separating axis directionally oriented from a to b to get the correct closest features.
            //That's the max dot for a and the min dot for b.
            //For edges vs edges, in which the edges are intersecting, one of the closest points must be inside the other box.
            float3     bCenterInASpace  = math.transform(bInASpace, boxB.center) - boxA.center;
            simdFloat3 faceNormalsBoxA  = new simdFloat3(new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), new float3(0f, 0f, 1f), new float3(1f, 0f, 0f));
            simdFloat3 faceNormalsBoxB  = simd.mul(bInASpace.rot, faceNormalsBoxA);
            simdFloat3 edgeAxes03       = simd.cross(faceNormalsBoxA.aaab, faceNormalsBoxB);  //normalsB is already .abca
            simdFloat3 edgeAxes47       = simd.cross(faceNormalsBoxA.bbcc, faceNormalsBoxB.bcab);
            float3     edgeAxes8        = math.cross(faceNormalsBoxB.c, faceNormalsBoxB.c);
            edgeAxes03                  = simd.select(-edgeAxes03, edgeAxes03, simd.dot(edgeAxes03, bCenterInASpace) >= 0f);
            edgeAxes47                  = simd.select(-edgeAxes47, edgeAxes47, simd.dot(edgeAxes47, bCenterInASpace) >= 0f);
            edgeAxes8                   = math.select(-edgeAxes8, edgeAxes8, math.dot(edgeAxes8, bCenterInASpace) >= 0f);
            bool4      edgeInvalids03   = edgeAxes03 == 0f;
            bool4      edgeInvalids47   = edgeAxes47 == 0f;
            bool       edgeInvalids8    = edgeAxes8.Equals(float3.zero);
            simdFloat3 bLeftPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                       bBottomPointsInAOS,
                                                       math.ShuffleComponent.LeftZ,
                                                       math.ShuffleComponent.LeftW,
                                                       math.ShuffleComponent.RightZ,
                                                       math.ShuffleComponent.RightW);
            simdFloat3 bRightPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                        bBottomPointsInAOS,
                                                        math.ShuffleComponent.LeftX,
                                                        math.ShuffleComponent.LeftY,
                                                        math.ShuffleComponent.RightX,
                                                        math.ShuffleComponent.RightY);
            simdFloat3 bFrontPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                        bBottomPointsInAOS,
                                                        math.ShuffleComponent.LeftY,
                                                        math.ShuffleComponent.LeftW,
                                                        math.ShuffleComponent.RightY,
                                                        math.ShuffleComponent.RightW);
            simdFloat3 bBackPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                       bBottomPointsInAOS,
                                                       math.ShuffleComponent.LeftX,
                                                       math.ShuffleComponent.LeftZ,
                                                       math.ShuffleComponent.RightX,
                                                       math.ShuffleComponent.RightZ);
            simdFloat3 bNormalsX = new simdFloat3(new float3(0f, math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                  new float3(0f, math.SQRT2 / 2f, -math.SQRT2 / 2f),
                                                  new float3(0f, -math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                  new float3(0f, -math.SQRT2 / 2f, -math.SQRT2 / 2f));
            simdFloat3 bNormalsY = new simdFloat3(new float3(math.SQRT2 / 2f, 0f, math.SQRT2 / 2f),
                                                  new float3(math.SQRT2 / 2f, 0f, -math.SQRT2 / 2f),
                                                  new float3(-math.SQRT2 / 2f, 0f, math.SQRT2 / 2f),
                                                  new float3(-math.SQRT2 / 2f, 0f, -math.SQRT2 / 2f));
            simdFloat3 bNormalsZ = new simdFloat3(new float3(math.SQRT2 / 2f, math.SQRT2 / 2f, 0f),
                                                  new float3(-math.SQRT2 / 2f, math.SQRT2 / 2f, 0f),
                                                  new float3(math.SQRT2 / 2f, -math.SQRT2 / 2f, 0f),
                                                  new float3(-math.SQRT2 / 2f, -math.SQRT2 / 2f, 0f));
            float3 bCenterPlaneDistancesInA = simd.dot(bCenterInASpace, faceNormalsBoxB).xyz;

            //x vs x
            float3 axisXX        = edgeAxes03.a;
            float3 aXX           = math.select(-boxA.halfSize, boxA.halfSize, axisXX > 0f);
            float3 aExtraXX      = math.select(aXX, boxA.halfSize, axisXX == 0f);
            aExtraXX.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXX = new simdFloat3(aXX, aXX, aExtraXX, aExtraXX);
            simdFloat3 aEdgesXX  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXX        = simd.dot(bLeftPointsInAOS, axisXX);
            float                 bestDotXX     = math.cmin(dotsXX);
            int                   dotsMaskXX    = math.bitmask(dotsXX == bestDotXX);
            math.ShuffleComponent bIndexXX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXX), 0, 3);
            math.ShuffleComponent bExtraIndexXX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXX) - 28), 0, 3);
            simdFloat3            bPointsXX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX);
            simdFloat3            bEdgesXX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX) - bPointsXX;
            simdFloat3            bNormalsXX    = simd.shuffle(bNormalsX, bNormalsX, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX);
            QueriesLowLevelUtils.SegmentSegment(aPointsXX, aEdgesXX, bPointsXX, bEdgesXX, out simdFloat3 closestAsXX, out simdFloat3 closestBsXX);
            bool4 insideXX =
                (math.abs(closestBsXX.x) < boxA.halfSize.x) & (math.abs(closestBsXX.y) < boxA.halfSize.y) & (math.abs(closestBsXX.z) < boxA.halfSize.z);
            insideXX                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsXX, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqXX  = simd.distancesq(closestAsXX, closestBsXX);
            signedDistanceSqXX         = math.select(signedDistanceSqXX, -signedDistanceSqXX, insideXX);

            //x vs y
            float3 axisXY        = edgeAxes03.b;
            float3 aXY           = math.select(-boxA.halfSize, boxA.halfSize, axisXY > 0f);
            float3 aExtraXY      = math.select(aXY, boxA.halfSize, axisXY == 0f);
            aExtraXY.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXY = new simdFloat3(aXY, aXY, aExtraXY, aExtraXY);
            simdFloat3 aEdgesXY  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXY        = simd.dot(bBottomPointsInAOS, axisXY);
            float                 bestDotXY     = math.cmin(dotsXY);
            int                   dotsMaskXY    = math.bitmask(dotsXY == bestDotXY);
            math.ShuffleComponent bIndexXY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXY), 0, 3);
            math.ShuffleComponent bExtraIndexXY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXY) - 28), 0, 3);
            simdFloat3            bPointsXY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY);
            simdFloat3            bEdgesXY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY) - bPointsXY;
            simdFloat3            bNormalsXY    = simd.shuffle(bNormalsY, bNormalsY, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY);
            QueriesLowLevelUtils.SegmentSegment(aPointsXY, aEdgesXY, bPointsXY, bEdgesXY, out simdFloat3 closestAsXY, out simdFloat3 closestBsXY);
            bool4 insideXY =
                (math.abs(closestBsXY.x) < boxA.halfSize.x) & (math.abs(closestBsXY.y) < boxA.halfSize.y) & (math.abs(closestBsXY.z) < boxA.halfSize.z);
            insideXY                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsXY, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqXY  = simd.distancesq(closestAsXY, closestBsXY);
            signedDistanceSqXY         = math.select(signedDistanceSqXY, -signedDistanceSqXY, insideXY);

            //x vs z
            float3 axisXZ        = edgeAxes03.c;
            float3 aXZ           = math.select(-boxA.halfSize, boxA.halfSize, axisXZ > 0f);
            float3 aExtraXZ      = math.select(aXZ, boxA.halfSize, axisXZ == 0f);
            aExtraXZ.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXZ = new simdFloat3(aXZ, aXZ, aExtraXZ, aExtraXZ);
            simdFloat3 aEdgesXZ  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXZ        = simd.dot(bFrontPointsInAOS, axisXZ);
            float                 bestDotXZ     = math.cmin(dotsXZ);
            int                   dotsMaskXZ    = math.bitmask(dotsXZ == bestDotXZ);
            math.ShuffleComponent bIndexXZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXZ), 0, 3);
            math.ShuffleComponent bExtraIndexXZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXZ) - 28), 0, 3);
            simdFloat3            bPointsXZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ);
            simdFloat3            bEdgesXZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ) - bPointsXZ;
            simdFloat3            bNormalsXZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ);
            QueriesLowLevelUtils.SegmentSegment(aPointsXZ, aEdgesXZ, bPointsXZ, bEdgesXZ, out simdFloat3 closestAsXZ, out simdFloat3 closestBsXZ);
            bool4 insideXZ =
                (math.abs(closestBsXZ.x) < boxA.halfSize.x) & (math.abs(closestBsXZ.y) < boxA.halfSize.y) & (math.abs(closestBsXZ.z) < boxA.halfSize.z);
            insideXZ                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsXZ, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqXZ  = simd.distancesq(closestAsXZ, closestBsXZ);
            signedDistanceSqXZ         = math.select(signedDistanceSqXZ, -signedDistanceSqXZ, insideXZ);

            //y
            //y vs x
            float3 axisYX        = edgeAxes03.d;
            float3 aYX           = math.select(-boxA.halfSize, boxA.halfSize, axisYX > 0f);
            float3 aExtraYX      = math.select(aYX, boxA.halfSize, axisYX == 0f);
            aExtraYX.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYX = new simdFloat3(aYX, aYX, aExtraYX, aExtraYX);
            simdFloat3 aEdgesYX  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYX        = simd.dot(bLeftPointsInAOS, axisYX);
            float                 bestDotYX     = math.cmin(dotsYX);
            int                   dotsMaskYX    = math.bitmask(dotsYX == bestDotYX);
            math.ShuffleComponent bIndexYX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYX), 0, 3);
            math.ShuffleComponent bExtraIndexYX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYX) - 28), 0, 3);
            simdFloat3            bPointsYX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX);
            simdFloat3            bEdgesYX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX) - bPointsYX;
            simdFloat3            bNormalsYX    = simd.shuffle(bNormalsX, bNormalsX, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX);
            QueriesLowLevelUtils.SegmentSegment(aPointsYX, aEdgesYX, bPointsYX, bEdgesYX, out simdFloat3 closestAsYX, out simdFloat3 closestBsYX);
            bool4 insideYX =
                (math.abs(closestBsYX.x) < boxA.halfSize.x) & (math.abs(closestBsYX.y) < boxA.halfSize.y) & (math.abs(closestBsYX.z) < boxA.halfSize.z);
            insideYX                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsYX, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqYX  = simd.distancesq(closestAsYX, closestBsYX);
            signedDistanceSqYX         = math.select(signedDistanceSqYX, -signedDistanceSqYX, insideYX);

            //y vs y
            float3 axisYY        = edgeAxes47.a;
            float3 aYY           = math.select(-boxA.halfSize, boxA.halfSize, axisYY > 0f);
            float3 aExtraYY      = math.select(aYY, boxA.halfSize, axisYY == 0f);
            aExtraYY.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYY = new simdFloat3(aYY, aYY, aExtraYY, aExtraYY);
            simdFloat3 aEdgesYY  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYY        = simd.dot(bBottomPointsInAOS, axisYY);
            float                 bestDotYY     = math.cmin(dotsYY);
            int                   dotsMaskYY    = math.bitmask(dotsYY == bestDotYY);
            math.ShuffleComponent bIndexYY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYY), 0, 3);
            math.ShuffleComponent bExtraIndexYY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYY) - 28), 0, 3);
            simdFloat3            bPointsYY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY);
            simdFloat3            bEdgesYY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY) - bPointsYY;
            simdFloat3            bNormalsYY    = simd.shuffle(bNormalsY, bNormalsY, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY);
            QueriesLowLevelUtils.SegmentSegment(aPointsYY, aEdgesYY, bPointsYY, bEdgesYY, out simdFloat3 closestAsYY, out simdFloat3 closestBsYY);
            bool4 insideYY =
                (math.abs(closestBsYY.x) < boxA.halfSize.x) & (math.abs(closestBsYY.y) < boxA.halfSize.y) & (math.abs(closestBsYY.z) < boxA.halfSize.z);
            insideYY                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsYY, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqYY  = simd.distancesq(closestAsYY, closestBsYY);
            signedDistanceSqYY         = math.select(signedDistanceSqYY, -signedDistanceSqYY, insideYY);

            //y vs z
            float3 axisYZ        = edgeAxes47.b;
            float3 aYZ           = math.select(-boxA.halfSize, boxA.halfSize, axisYZ > 0f);
            float3 aExtraYZ      = math.select(aYZ, boxA.halfSize, axisYZ == 0f);
            aExtraYZ.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYZ = new simdFloat3(aYZ, aYZ, aExtraYZ, aExtraYZ);
            simdFloat3 aEdgesYZ  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYZ        = simd.dot(bFrontPointsInAOS, axisYZ);
            float                 bestDotYZ     = math.cmin(dotsYZ);
            int                   dotsMaskYZ    = math.bitmask(dotsYZ == bestDotYZ);
            math.ShuffleComponent bIndexYZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYZ), 0, 3);
            math.ShuffleComponent bExtraIndexYZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYZ) - 28), 0, 3);
            simdFloat3            bPointsYZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ);
            simdFloat3            bEdgesYZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ) - bPointsYZ;
            simdFloat3            bNormalsYZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ);
            QueriesLowLevelUtils.SegmentSegment(aPointsYZ, aEdgesYZ, bPointsYZ, bEdgesYZ, out simdFloat3 closestAsYZ, out simdFloat3 closestBsYZ);
            bool4 insideYZ =
                (math.abs(closestBsYZ.x) < boxA.halfSize.x) & (math.abs(closestBsYZ.y) < boxA.halfSize.y) & (math.abs(closestBsYZ.z) < boxA.halfSize.z);
            insideYZ                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsYZ, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqYZ  = simd.distancesq(closestAsYZ, closestBsYZ);
            signedDistanceSqYZ         = math.select(signedDistanceSqYZ, -signedDistanceSqYZ, insideYZ);

            //z
            //z vs x
            float3 axisZX        = edgeAxes47.c;
            float3 aZX           = math.select(-boxA.halfSize, boxA.halfSize, axisZX > 0f);
            float3 aExtraZX      = math.select(aZX, boxA.halfSize, axisZX == 0f);
            aExtraZX.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZX = new simdFloat3(aZX, aZX, aExtraZX, aExtraZX);
            simdFloat3 aEdgesZX  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZX        = simd.dot(bLeftPointsInAOS, axisZX);
            float                 bestDotZX     = math.cmin(dotsZX);
            int                   dotsMaskZX    = math.bitmask(dotsZX == bestDotZX);
            math.ShuffleComponent bIndexZX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZX), 0, 3);
            math.ShuffleComponent bExtraIndexZX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZX) - 28), 0, 3);
            simdFloat3            bPointsZX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX);
            simdFloat3            bEdgesZX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX) - bPointsZX;
            simdFloat3            bNormalsZX    = simd.shuffle(bNormalsX, bNormalsX, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX);
            QueriesLowLevelUtils.SegmentSegment(aPointsZX, aEdgesZX, bPointsZX, bEdgesZX, out simdFloat3 closestAsZX, out simdFloat3 closestBsZX);
            bool4 insideZX =
                (math.abs(closestBsZX.x) < boxA.halfSize.x) & (math.abs(closestBsZX.y) < boxA.halfSize.y) & (math.abs(closestBsZX.z) < boxA.halfSize.z);
            insideZX                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsZX, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqZX  = simd.distancesq(closestAsZX, closestBsZX);
            signedDistanceSqZX         = math.select(signedDistanceSqZX, -signedDistanceSqZX, insideZX);

            //z vs y
            float3 axisZY        = edgeAxes47.d;
            float3 aZY           = math.select(-boxA.halfSize, boxA.halfSize, axisZY > 0f);
            float3 aExtraZY      = math.select(aZY, boxA.halfSize, axisZY == 0f);
            aExtraZY.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZY = new simdFloat3(aZY, aZY, aExtraZY, aExtraZY);
            simdFloat3 aEdgesZY  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZY        = simd.dot(bBottomPointsInAOS, axisZY);
            float                 bestDotZY     = math.cmin(dotsZY);
            int                   dotsMaskZY    = math.bitmask(dotsZY == bestDotZY);
            math.ShuffleComponent bIndexZY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZY), 0, 3);
            math.ShuffleComponent bExtraIndexZY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZY) - 28), 0, 3);
            simdFloat3            bPointsZY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY);
            simdFloat3            bEdgesZY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY) - bPointsZY;
            simdFloat3            bNormalsZY    = simd.shuffle(bNormalsY, bNormalsY, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY);
            QueriesLowLevelUtils.SegmentSegment(aPointsZY, aEdgesZY, bPointsZY, bEdgesZY, out simdFloat3 closestAsZY, out simdFloat3 closestBsZY);
            bool4 insideZY =
                (math.abs(closestBsZY.x) < boxA.halfSize.x) & (math.abs(closestBsZY.y) < boxA.halfSize.y) & (math.abs(closestBsZY.z) < boxA.halfSize.z);
            insideZY                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsZY, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqZY  = simd.distancesq(closestAsZY, closestBsZY);
            signedDistanceSqZY         = math.select(signedDistanceSqZY, -signedDistanceSqZY, insideZY);

            //z vs z
            float3 axisZZ        = edgeAxes8;
            float3 aZZ           = math.select(-boxA.halfSize, boxA.halfSize, axisZZ > 0f);
            float3 aExtraZZ      = math.select(aZZ, boxA.halfSize, axisZZ == 0f);
            aExtraZZ.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZZ = new simdFloat3(aZZ, aZZ, aExtraZZ, aExtraZZ);
            simdFloat3 aEdgesZZ  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZZ        = simd.dot(bFrontPointsInAOS, axisZZ);
            float                 bestDotZZ     = math.cmin(dotsZZ);
            int                   dotsMaskZZ    = math.bitmask(dotsZZ == bestDotZZ);
            math.ShuffleComponent bIndexZZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZZ), 0, 3);
            math.ShuffleComponent bExtraIndexZZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZZ) - 28), 0, 3);
            simdFloat3            bPointsZZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ);
            simdFloat3            bEdgesZZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ) - bPointsZZ;
            simdFloat3            bNormalsZZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ);
            QueriesLowLevelUtils.SegmentSegment(aPointsZZ, aEdgesZZ, bPointsZZ, bEdgesZZ, out simdFloat3 closestAsZZ, out simdFloat3 closestBsZZ);
            bool4 insideZZ =
                (math.abs(closestBsZZ.x) < boxA.halfSize.x) & (math.abs(closestBsZZ.y) < boxA.halfSize.y) & (math.abs(closestBsZZ.z) < boxA.halfSize.z);
            insideZZ                  |= QueriesLowLevelUtils.ArePointsInsideObb(closestAsZZ, faceNormalsBoxB, bCenterPlaneDistancesInA, boxB.halfSize);
            float4 signedDistanceSqZZ  = simd.distancesq(closestAsZZ, closestBsZZ);
            signedDistanceSqZZ         = math.select(signedDistanceSqZZ, -signedDistanceSqZZ, insideZZ);

            //Step 3: Find the best result.
            float4     bestEdgeSignedDistancesSq = math.select(signedDistanceSqXX, float.MaxValue, edgeInvalids03.x);
            simdFloat3 bestEdgeClosestAs         = closestAsXX;
            simdFloat3 bestEdgeClosestBs         = closestBsXX;
            simdFloat3 bestNormalBs              = bNormalsXX;
            //int4       bestEdgeIds         = 0;

            bool4 newEdgeIsBetters     = (signedDistanceSqXY < bestEdgeSignedDistancesSq) ^ ((signedDistanceSqXY < 0f) & (bestEdgeSignedDistancesSq < 0f));
            newEdgeIsBetters          &= !edgeInvalids03.y;
            bestEdgeSignedDistancesSq  = math.select(bestEdgeSignedDistancesSq, signedDistanceSqXY, newEdgeIsBetters);
            bestEdgeClosestAs          = simd.select(bestEdgeClosestAs, closestAsXY, newEdgeIsBetters);
            bestEdgeClosestBs          = simd.select(bestEdgeClosestBs, closestBsXY, newEdgeIsBetters);
            bestNormalBs               = simd.select(bestNormalBs, bNormalsXY, newEdgeIsBetters);
            //bestEdgeIds            = math.select(bestEdgeIds, 1, newEdgeIsBetter);

            newEdgeIsBetters           = (signedDistanceSqXZ < bestEdgeSignedDistancesSq) ^ ((signedDistanceSqXZ < 0f) & (bestEdgeSignedDistancesSq < 0f));
            newEdgeIsBetters          &= !edgeInvalids03.z;
            bestEdgeSignedDistancesSq  = math.select(bestEdgeSignedDistancesSq, signedDistanceSqXZ, newEdgeIsBetters);
            bestEdgeClosestAs          = simd.select(bestEdgeClosestAs, closestAsXZ, newEdgeIsBetters);
            bestEdgeClosestBs          = simd.select(bestEdgeClosestBs, closestBsXZ, newEdgeIsBetters);
            bestNormalBs               = simd.select(bestNormalBs, bNormalsXZ, newEdgeIsBetters);
            //bestEdgeIds          = math.select(bestEdgeIds, 2, newEdgeIsBetter);

            newEdgeIsBetters           = (signedDistanceSqYX < bestEdgeSignedDistancesSq) ^ ((signedDistanceSqYX < 0f) & (bestEdgeSignedDistancesSq < 0f));
            newEdgeIsBetters          &= !edgeInvalids03.w;
            bestEdgeSignedDistancesSq  = math.select(bestEdgeSignedDistancesSq, signedDistanceSqYX, newEdgeIsBetters);
            bestEdgeClosestAs          = simd.select(bestEdgeClosestAs, closestAsYX, newEdgeIsBetters);
            bestEdgeClosestBs          = simd.select(bestEdgeClosestBs, closestBsYX, newEdgeIsBetters);
            bestNormalBs               = simd.select(bestNormalBs, bNormalsYX, newEdgeIsBetters);
            //bestEdgeIds          = math.select(bestEdgeIds, 3, newEdgeIsBetter);

            newEdgeIsBetters           = (signedDistanceSqYY < bestEdgeSignedDistancesSq) ^ ((signedDistanceSqYY < 0f) & (bestEdgeSignedDistancesSq < 0f));
            newEdgeIsBetters          &= !edgeInvalids47.x;
            bestEdgeSignedDistancesSq  = math.select(bestEdgeSignedDistancesSq, signedDistanceSqYY, newEdgeIsBetters);
            bestEdgeClosestAs          = simd.select(bestEdgeClosestAs, closestAsYY, newEdgeIsBetters);
            bestEdgeClosestBs          = simd.select(bestEdgeClosestBs, closestBsYY, newEdgeIsBetters);
            bestNormalBs               = simd.select(bestNormalBs, bNormalsYY, newEdgeIsBetters);
            //bestEdgeIds          = math.select(bestEdgeIds, 4, newEdgeIsBetter);

            newEdgeIsBetters           = (signedDistanceSqYZ < bestEdgeSignedDistancesSq) ^ ((signedDistanceSqYZ < 0f) & (bestEdgeSignedDistancesSq < 0f));
            newEdgeIsBetters          &= !edgeInvalids47.y;
            bestEdgeSignedDistancesSq  = math.select(bestEdgeSignedDistancesSq, signedDistanceSqYZ, newEdgeIsBetters);
            bestEdgeClosestAs          = simd.select(bestEdgeClosestAs, closestAsYZ, newEdgeIsBetters);
            bestEdgeClosestBs          = simd.select(bestEdgeClosestBs, closestBsYZ, newEdgeIsBetters);
            bestNormalBs               = simd.select(bestNormalBs, bNormalsYZ, newEdgeIsBetters);
            //bestEdgeIds          = math.select(bestEdgeIds, 5, newEdgeIsBetter);

            newEdgeIsBetters           = (signedDistanceSqZX < bestEdgeSignedDistancesSq) ^ ((signedDistanceSqZX < 0f) & (bestEdgeSignedDistancesSq < 0f));
            newEdgeIsBetters          &= !edgeInvalids47.z;
            bestEdgeSignedDistancesSq  = math.select(bestEdgeSignedDistancesSq, signedDistanceSqZX, newEdgeIsBetters);
            bestEdgeClosestAs          = simd.select(bestEdgeClosestAs, closestAsZX, newEdgeIsBetters);
            bestEdgeClosestBs          = simd.select(bestEdgeClosestBs, closestBsZX, newEdgeIsBetters);
            bestNormalBs               = simd.select(bestNormalBs, bNormalsZX, newEdgeIsBetters);
            //bestEdgeIds          = math.select(bestEdgeIds, 6, newEdgeIsBetter);

            newEdgeIsBetters           = (signedDistanceSqZY < bestEdgeSignedDistancesSq) ^ ((signedDistanceSqZY < 0f) & (bestEdgeSignedDistancesSq < 0f));
            newEdgeIsBetters          &= !edgeInvalids47.w;
            bestEdgeSignedDistancesSq  = math.select(bestEdgeSignedDistancesSq, signedDistanceSqZY, newEdgeIsBetters);
            bestEdgeClosestAs          = simd.select(bestEdgeClosestAs, closestAsZY, newEdgeIsBetters);
            bestEdgeClosestBs          = simd.select(bestEdgeClosestBs, closestBsZY, newEdgeIsBetters);
            bestNormalBs               = simd.select(bestNormalBs, bNormalsZY, newEdgeIsBetters);
            //bestEdgeIds          = math.select(bestEdgeIds, 7, newEdgeIsBetter);

            newEdgeIsBetters           = (signedDistanceSqZZ < bestEdgeSignedDistancesSq) ^ ((signedDistanceSqZZ < 0f) & (bestEdgeSignedDistancesSq < 0f));
            newEdgeIsBetters          &= !edgeInvalids8;
            bestEdgeSignedDistancesSq  = math.select(bestEdgeSignedDistancesSq, signedDistanceSqZZ, newEdgeIsBetters);
            bestEdgeClosestAs          = simd.select(bestEdgeClosestAs, closestAsZZ, newEdgeIsBetters);
            bestEdgeClosestBs          = simd.select(bestEdgeClosestBs, closestBsZZ, newEdgeIsBetters);
            bestNormalBs               = simd.select(bestNormalBs, bNormalsZZ, newEdgeIsBetters);
            //bestEdgeIds          = math.select(bestEdgeIds, 8, newEdgeIsBetter);

            float  bestEdgeSignedDistanceSq = math.cmin(bestEdgeSignedDistancesSq);
            int    bestEdgeIndex            = math.tzcnt(math.bitmask(bestEdgeSignedDistanceSq == bestEdgeSignedDistancesSq));
            float3 bestEdgeClosestA         = bestEdgeClosestAs[bestEdgeIndex];
            float3 bestEdgeClosestB         = bestEdgeClosestBs[bestEdgeIndex];
            float3 bestEdgeNormalB          = bestNormalBs[bestEdgeIndex];
            //int    bestId                   = bestEdgeIds[bestIndex];

            simdFloat3 topUnnormals    = default;
            simdFloat3 bottomUnnormals = default;
            topUnnormals.x             = math.select(-1f, 1f, new bool4(true, true, false, false));
            bottomUnnormals.x          = topUnnormals.x;
            bottomUnnormals.y          = -1f;
            topUnnormals.y             = 1f;
            topUnnormals.z             = math.select(-1f, 1f, new bool4(true, false, true, false));
            bottomUnnormals.z          = topUnnormals.z;

            float3 pointsNormalBFromBInA = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)bInABIndex);
            float3 pointsNormalAFromAInB = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)aInBAIndex);
            pointsNormalBFromBInA        = math.normalize(math.rotate(bInASpace, pointsNormalBFromBInA));
            pointsNormalAFromAInB        = math.normalize(pointsNormalAFromAInB);
            float3 pointsNormalBFromAInB = math.select(0f, 1f, pointsClosestBInB == boxB.halfSize) + math.select(0f, -1f, pointsClosestBInB == -boxB.halfSize);
            pointsNormalBFromAInB        = math.normalize(math.rotate(bInASpace, pointsNormalBFromAInB));
            float3 pointsNormalAFromBInA =
                math.normalize(math.select(0f, 1f, pointsClosestAInA == boxA.halfSize) + math.select(0f, -1f, pointsClosestAInA == -boxA.halfSize));
            float3 bestEdgeNormalA             = math.normalize(math.select(0f, 1f, bestEdgeClosestA == boxA.halfSize) + math.select(0f, -1f, bestEdgeClosestA == -boxA.halfSize));
            int    matchedBIndexFromEdgeTop    = math.tzcnt(math.bitmask(bestEdgeClosestB == bTopPointsInAOS));
            int    matchedBIndexFromEdgeBottom = math.tzcnt(math.bitmask((bestEdgeClosestB == bBottomPointsInAOS))) + 4;
            int    matchedIndexBFromEdge       = math.select(matchedBIndexFromEdgeTop, matchedBIndexFromEdgeBottom, matchedBIndexFromEdgeBottom < 8);
            float3 edgeNormalBAsCorner         = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)math.clamp(matchedIndexBFromEdge, 0, 7));
            edgeNormalBAsCorner                = math.normalize(edgeNormalBAsCorner);
            bestEdgeNormalB                    = math.select(bestEdgeNormalB, edgeNormalBAsCorner, matchedIndexBFromEdge < 8);

            bool bInAIsBetter  = math.sign(pointsAxisDistanceInA) > math.sign(pointsAxisDistanceInB);
            bInAIsBetter      |=
                (math.sign(pointsAxisDistanceInA) == math.sign(pointsAxisDistanceInB)) & (math.abs(pointsSignedDistanceSqInA) <= math.abs(pointsSignedDistanceSqInB));
            float  pointsSignedDistanceSq = math.select(pointsSignedDistanceSqInB, pointsSignedDistanceSqInA, bInAIsBetter);
            float3 pointsClosestA         = math.select(math.transform(bInASpace, pointsClosestAInB + boxB.center), pointsClosestAInA + boxA.center, bInAIsBetter);
            float3 pointsClosestB         = math.select(math.transform(bInASpace, pointsClosestBInB + boxB.center), pointsClosestBInA + boxA.center, bInAIsBetter);
            float3 pointsNormalA          = math.select(pointsNormalAFromAInB, pointsNormalAFromBInA, bInAIsBetter);
            float3 pointsNormalB          = math.select(pointsNormalBFromAInB, pointsNormalBFromBInA, bInAIsBetter);
            //int    pointsBestId           = math.select(10, 9, bInAIsBetter);

            //This might be an optimization for computing the sign of edge vs edge queries
            //float3 bestEdgeAxis         = simd.shuffle(axes03, axes47, (math.ShuffleComponent)(bestId & 7));
            //bestEdgeAxis                = math.select(bestEdgeAxis, axes8, bestId == 8);
            //float4 satEdgeDots          = simd.dot(bestEdgeAxis, new simdFloat3(0f, bCenterInASpace, bestEdgeClosestA, bestEdgeClosestB));
            //float  bestEdgeAxisDistance = -( satEdgeDots.z - satEdgeDots.w);  //Huh. That simplified. Defs optimization potential here.

            bool pointsIsBetter          = (pointsSignedDistanceSq >= bestEdgeSignedDistanceSq) & (pointsSignedDistanceSq < 0f);
            pointsIsBetter              |= (pointsSignedDistanceSq <= bestEdgeSignedDistanceSq + math.EPSILON) & (pointsSignedDistanceSq >= 0f);  //Bias by epsilon because edges are prone to precision issues
            float  bestSignedDistanceSq  = math.select(bestEdgeSignedDistanceSq, pointsSignedDistanceSq, pointsIsBetter);
            float3 bestClosestA          = math.select(bestEdgeClosestA + boxA.center, pointsClosestA, pointsIsBetter);
            float3 bestClosestB          = math.select(bestEdgeClosestB + boxA.center, pointsClosestB, pointsIsBetter);
            float3 bestNormalA           = math.select(bestEdgeNormalA, pointsNormalA, pointsIsBetter);
            float3 bestNormalB           = math.select(math.rotate(bInASpace, bestEdgeNormalB), pointsNormalB, pointsIsBetter);
            //bestId                      = math.select(bestId, pointsBestId, pointsIsBetter);

            //Step 4: Build result
            result = new ColliderDistanceResultInternal
            {
                hitpointA = bestClosestA,
                hitpointB = bestClosestB,
                normalA   = bestNormalA,
                normalB   = bestNormalB,
                distance  = math.sign(bestSignedDistanceSq) * math.sqrt(math.abs(bestSignedDistanceSq))
            };
            return result.distance <= maxDistance;
        }
    }
}

