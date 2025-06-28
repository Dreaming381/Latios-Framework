using Unity.Mathematics;
using UnityEngine;

// May God bless the first poor soul who runs into performance problems while trying to do something with this code.

namespace Latios.Psyshock
{
    internal static class TerrainTerrain
    {
        public static bool DistanceBetween(in TerrainCollider terrainA,
                                           in RigidTransform aTransform,
                                           in TerrainCollider terrainB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit             = false;
            result               = default;
            result.distance      = float.MaxValue;
            ref var blobB        = ref terrainB.terrainColliderBlob.Value;
            var     trianglesInB = blobB.quadsPerRow * blobB.quadRows * 2;
            for (int i = 0; i < trianglesInB; i++)
            {
                var triangleIndices = blobB.GetTriangle(i);
                var triangle        = PointRayTerrain.CreateLocalTriangle(ref blobB, triangleIndices, terrainB.baseHeightOffset, terrainB.scale);

                bool newHit = TriangleTerrain.DistanceBetween(in terrainA,
                                                              aTransform,
                                                              in triangle,
                                                              bTransform,
                                                              maxDistance,
                                                              out var newResult);

                newResult.subColliderIndexB  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static void DistanceBetweenAll<T>(in TerrainCollider terrainA,
                                                 in RigidTransform aTransform,
                                                 in TerrainCollider terrainB,
                                                 in RigidTransform bTransform,
                                                 float maxDistance,
                                                 ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            ref var blobB        = ref terrainB.terrainColliderBlob.Value;
            var     trianglesInB = blobB.quadsPerRow * blobB.quadRows * 2;
            for (int i = 0; i < trianglesInB; i++)
            {
                var triangleIndices = blobB.GetTriangle(i);
                var triangle        = PointRayTerrain.CreateLocalTriangle(ref blobB, triangleIndices, terrainB.baseHeightOffset, terrainB.scale);

                bool newHit = TriangleTerrain.DistanceBetween(in terrainA,
                                                              aTransform,
                                                              in triangle,
                                                              bTransform,
                                                              maxDistance,
                                                              out var newResult);

                newResult.subColliderIndexB = i;

                if (newHit)
                    processor.Execute(in newResult);
            }
        }

        public static bool ColliderCast(in TerrainCollider terrainToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TerrainCollider targetTerrain,
                                        in RigidTransform targetTerrainTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in terrainToCast, in castStart, in targetTerrain, in targetTerrainTransform, 0f, out _))
            {
                return false;
            }
            ref var targetBlob      = ref targetTerrain.terrainColliderBlob.Value;
            var     targetTriangles = targetBlob.quadsPerRow * targetBlob.quadRows * 2;
            for (int i = 0; i < targetTriangles; i++)
            {
                var triangleIndices = targetBlob.GetTriangle(i);
                var triangle        = PointRayTerrain.CreateLocalTriangle(ref targetBlob, triangleIndices, targetTerrain.baseHeightOffset, targetTerrain.scale);

                bool newHit = TriangleTerrain.ColliderCast(in terrainToCast,
                                                           castStart,
                                                           castEnd,
                                                           in triangle,
                                                           targetTerrainTransform,
                                                           out var newResult);

                newResult.subColliderIndexOnTarget  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TerrainCollider terrainA,
                                                                          in RigidTransform aTransform,
                                                                          in TerrainCollider terrainB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            var triangleIndices = terrainB.terrainColliderBlob.Value.GetTriangle(distanceResult.subColliderIndexA);
            var triangle        = PointRayTerrain.CreateLocalTriangle(ref terrainB.terrainColliderBlob.Value, triangleIndices, terrainB.baseHeightOffset, terrainB.scale);
            return TriangleTerrain.UnityContactsBetween(in terrainA, in aTransform, in triangle, in bTransform, distanceResult.ToFlipped()).ToFlipped();
        }
    }
}

