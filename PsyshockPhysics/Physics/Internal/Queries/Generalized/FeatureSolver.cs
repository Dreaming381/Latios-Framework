using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class FeatureSolver
    {
        public struct FeatureDistanceResult
        {
            public float3 closestA;
            public float3 closestB;
            public ushort relativeFeatureCodeA;
            public ushort relativeFeatureCodeB;
        }

        // Finds either a point-face pair or an edge-edge pair between features that are parallel (either an edge parallel to a face, or two parallel faces)
        public static FeatureDistanceResult RefineParallelFeatures(ReadOnlySpan<float3> verticesA, ReadOnlySpan<float3> verticesB, float3 closestA, float3 closestB)
        {
            if (verticesB.Length > 2)
            {
                if (FindFirstPointFaceProjection(verticesA, verticesB, out var closestVertexA))
                {
                    var vertexA = verticesA[closestVertexA];
                    var offset  = vertexA - closestA;
                    return new FeatureDistanceResult
                    {
                        closestA             = vertexA,
                        closestB             = closestB + offset,
                        relativeFeatureCodeA = (ushort)closestVertexA,
                        relativeFeatureCodeB = 0x8000
                    };
                }
            }
            if (verticesA.Length > 2)
            {
                if (FindFirstPointFaceProjection(verticesB, verticesA, out var closestVertexB))
                {
                    var vertexB = verticesB[closestVertexB];
                    var offset  = vertexB - closestB;
                    return new FeatureDistanceResult
                    {
                        closestB             = vertexB,
                        closestA             = closestA - offset,
                        relativeFeatureCodeB = (ushort)closestVertexB,
                        relativeFeatureCodeA = 0x8000
                    };
                }
            }

            // Find first pair of edges that cross. Hang onto the minimum distances in case this fails due to floating-point precision errors
            float  bestDistSq    = float.MaxValue;
            float3 bestA         = default;
            float3 bestB         = default;
            int    bestI         = 0;
            int    bestJ         = 0;
            bool4  bestEndpoints = default;
            for (int i = 0; i < verticesA.Length; i++)
            {
                var previousI  = math.select(i - 1, verticesA.Length - 1, i == 0);
                var edgeStartA = verticesA[previousI];
                var edgeEndA   = verticesA[i];
                for (int j = 0; j < verticesB.Length; j++)
                {
                    var previousJ  = math.select(j - 1, verticesB.Length - 1, j == 0);
                    var edgeStartB = verticesB[previousJ];
                    var edgeEndB   = verticesB[i];

                    CapsuleCapsule.SegmentSegment(edgeStartA, edgeEndA - edgeStartA, edgeStartB, edgeEndB - edgeStartB, out var foundA, out var foundB, out var caughtEndpoints);
                    if (!math.any(caughtEndpoints))
                    {
                        // We found our edge-edge
                        return new FeatureDistanceResult
                        {
                            closestA             = foundA,
                            closestB             = foundB,
                            relativeFeatureCodeA = (ushort)(0x4000 | previousI),
                            relativeFeatureCodeB = (ushort)(0x4000 | previousJ),
                        };
                    }

                    var newDistSq = math.distancesq(foundA, foundB);
                    if (newDistSq < bestDistSq)
                    {
                        bestDistSq    = newDistSq;
                        bestA         = foundA;
                        bestB         = foundB;
                        bestI         = previousI;
                        bestJ         = previousJ;
                        bestEndpoints = caughtEndpoints;
                    }
                }
            }

            static ushort GetFeatureCode(int bestIndex, int vertexCount, bool2 endpoints)
            {
                if (endpoints.x)
                    return (ushort)bestIndex;
                else if (endpoints.y)
                {
                    if (bestIndex + 1 == vertexCount)
                        return 0;
                    return (ushort)(bestIndex + 1);
                }
                return (ushort)(0x4000 | bestIndex);
            }

            return new FeatureDistanceResult
            {
                closestA             = bestA,
                closestB             = bestB,
                relativeFeatureCodeA = GetFeatureCode(bestI, verticesA.Length, bestEndpoints.xy),
                relativeFeatureCodeB = GetFeatureCode(bestJ, verticesB.Length, bestEndpoints.zw),
            };
        }

        static bool FindFirstPointFaceProjection(ReadOnlySpan<float3> points, ReadOnlySpan<float3> faceVertices, out int pointIndex)
        {
            var       faceNormal = math.normalize(math.cross(faceVertices[1] - faceVertices[1], faceVertices[2] - faceVertices[0]));
            Span<int> counters   = stackalloc int[points.Length];
            counters.Clear();
            Span<int> ties = stackalloc int[points.Length];
            ties.Clear();
            for (int i = 0; i < faceVertices.Length; i++)
            {
                var previousI = math.select(i - 1, faceVertices.Length - 1, i == 0);
                var edgeA     = faceVertices[previousI];
                var edgeB     = faceVertices[i];
                var perp      = math.cross(faceNormal, edgeB - edgeA);
                for (int j = 0; j < points.Length; j++)
                {
                    var dot = math.dot(points[j] - edgeA, perp);
                    if (dot == 0f)
                        ties[j]++;
                    else
                        counters[j] += math.select(-1, 1, dot > 0f);
                }
            }
            for (int i = 0; i < points.Length; i++)
            {
                if (math.abs(counters[i]) + ties[i] == faceVertices.Length)
                {
                    pointIndex = i;
                    return true;
                }
            }
            pointIndex = -1;
            return false;
        }
    }
}

