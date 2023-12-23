using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class ExpandingPolygonBuilder2D
    {
        public static int Build(ref Span<byte> finalVertexIndices, ReadOnlySpan<float3> projectedVertices, float3 normal)
        {
            Span<UnhomedVertex> unhomed       = stackalloc UnhomedVertex[projectedVertices.Length];
            int                 unhomedCount  = projectedVertices.Length;
            Span<Plane>         edges         = stackalloc Plane[finalVertexIndices.Length];
            int                 foundVertices = 0;

            float bestDistance = 0f;
            byte  bestIndex    = 0;
            unhomed[0]         = new UnhomedVertex { vertexIndex = 0 };
            for (byte i = 1; i < projectedVertices.Length; i++)
            {
                unhomed[i] = new UnhomedVertex { vertexIndex = i };
                var newDist                                  = math.distancesq(projectedVertices[0], projectedVertices[i]);
                if (newDist > bestDistance)
                {
                    bestDistance = newDist;
                    bestIndex    = i;
                }
            }

            finalVertexIndices[0] = bestIndex;
            foundVertices++;
            unhomedCount--;
            unhomed[bestIndex] = unhomed[unhomedCount];

            bestDistance = 0f;
            bestIndex    = 0;
            for (byte i = 0; i < unhomedCount; i++)
            {
                var newDist = math.distancesq(projectedVertices[unhomed[i].vertexIndex], projectedVertices[finalVertexIndices[0]]);
                if (newDist > bestDistance)
                {
                    bestDistance = newDist;
                    bestIndex    = i;
                }
            }

            finalVertexIndices[1] = bestIndex;
            foundVertices++;
            unhomedCount--;
            unhomed[bestIndex] = unhomed[unhomedCount];

            edges[0] = mathex.PlaneFrom(projectedVertices[finalVertexIndices[0]], projectedVertices[finalVertexIndices[1]] - projectedVertices[finalVertexIndices[0]], normal);
            edges[1] = mathex.Flip(edges[0]);

            for (byte i = 0; i < unhomedCount; i++)
            {
                ref var u      = ref unhomed[i];
                var     vertex = projectedVertices[u.vertexIndex];
                var     dist   = mathex.SignedDistance(edges[0], vertex);
                if (dist > 0f)
                {
                    u.distanceToEdge   = dist;
                    u.closestEdgeIndex = 0;
                }
                else if (dist < 0f)
                {
                    u.distanceToEdge   = -dist;
                    u.closestEdgeIndex = 1;
                }
                else  // Colinear points are removed
                {
                    unhomedCount--;
                    if (i < unhomedCount)
                    {
                        unhomed[i] = unhomed[unhomedCount];
                        i--;
                    }
                }
            }

            while (foundVertices < finalVertexIndices.Length && unhomedCount > 0)
            {
                bestIndex    = 0;
                bestDistance = unhomed[0].distanceToEdge;
                for (byte i = 1; i < unhomedCount; i++)
                {
                    var u = unhomed[i];
                    if (u.distanceToEdge > bestDistance)
                    {
                        bestDistance = u.distanceToEdge;
                        bestIndex    = i;
                    }
                }

                var bestU       = unhomed[bestIndex];
                var targetIndex = bestU.closestEdgeIndex + 1;
                for (int i = foundVertices - 1; i >= targetIndex; i--)
                {
                    finalVertexIndices[i + 1] = finalVertexIndices[i];
                    edges[i + 1]              = edges[i];
                }

                finalVertexIndices[targetIndex] = bestU.vertexIndex;
                float3 before                   = projectedVertices[finalVertexIndices[bestU.closestEdgeIndex]];
                float3 current                  = projectedVertices[bestU.vertexIndex];
                float3 after                    = projectedVertices[finalVertexIndices[math.select(targetIndex + 1, 0, targetIndex >= foundVertices)]];
                edges[targetIndex]              = mathex.PlaneFrom(current, after - current, normal);
                edges[bestU.closestEdgeIndex]   = mathex.PlaneFrom(before, current - before, normal);
                foundVertices++;
                unhomedCount--;
                unhomed[bestIndex] = unhomed[unhomedCount];

                for (byte i = 0; i < unhomedCount; i++)
                {
                    ref var u = ref unhomed[i];
                    if (u.closestEdgeIndex > bestU.closestEdgeIndex)
                        u.closestEdgeIndex++;
                    else if (u.closestEdgeIndex == bestU.closestEdgeIndex)
                    {
                        var distA = mathex.SignedDistance(edges[u.closestEdgeIndex], projectedVertices[u.vertexIndex]);
                        var distB = mathex.SignedDistance(edges[targetIndex], projectedVertices[u.vertexIndex]);
                        if (distA <= 0f && distB <= 0f)
                        {
                            unhomedCount--;
                            if (i < unhomedCount)
                            {
                                unhomed[i] = unhomed[unhomedCount];
                                i--;
                            }
                        }
                        else if (distB > distA)
                        {
                            u.distanceToEdge   = distB;
                            u.closestEdgeIndex = (byte)targetIndex;
                        }
                        else
                            u.distanceToEdge = distA;
                    }
                }
            }

            return foundVertices;
        }

        struct UnhomedVertex
        {
            public float distanceToEdge;
            public byte  vertexIndex;
            public byte  closestEdgeIndex;
        }
    }
}

