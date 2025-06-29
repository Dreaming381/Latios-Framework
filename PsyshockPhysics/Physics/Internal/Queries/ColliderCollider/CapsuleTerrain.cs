using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleTerrain
    {
        public static bool DistanceBetween(in TerrainCollider terrain,
                                           in RigidTransform terrainTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var processor = new DistanceBetweenClosestProcessor { bestIndex = -1, bestDistance = float.MaxValue };
            DistanceBetweenAll(in terrain, in terrainTransform, in capsule, in capsuleTransform, maxDistance, ref processor);
            var hit = processor.bestIndex >= 0;

            if (!hit)
            {
                result = default;
                return false;
            }

            var triangleIndices      = terrain.terrainColliderBlob.Value.GetTriangle(processor.bestIndex);
            var triangle             = PointRayTerrain.CreateLocalTriangle(ref terrain.terrainColliderBlob.Value, triangleIndices, terrain.baseHeightOffset, terrain.scale);
            hit                      = CapsuleTriangle.DistanceBetween(in triangle, in terrainTransform, in capsule, in capsuleTransform, maxDistance, out result);
            result.subColliderIndexA = processor.bestIndex;
            return hit;
        }

        public static unsafe void DistanceBetweenAll<T>(in TerrainCollider terrain,
                                                        in RigidTransform terrainTransform,
                                                        in CapsuleCollider capsule,
                                                        in RigidTransform capsuleTransform,
                                                        float maxDistance,
                                                        ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            var aabb         = Physics.AabbFrom(capsule, math.mul(math.inverse(terrainTransform), capsuleTransform));
            var inverseScale = math.rcp(terrain.scale);
            var validAxes    = math.isfinite(inverseScale);

            var crosses = (aabb.min < 0f) & (aabb.max > 0f);
            if (math.any(!crosses & !validAxes))
                return;

            Physics.GetCenterExtents(aabb, out var center, out var extents);
            center     *= inverseScale;
            extents    *= inverseScale;
            var min     = (int3)math.floor(center - extents);
            var max     = (int3)math.ceil(center + extents);
            min.y      -= terrain.baseHeightOffset;
            max.y      -= terrain.baseHeightOffset;
            var minInt  = math.select(short.MinValue, min, validAxes);
            var maxInt  = math.select(short.MaxValue, max, validAxes);

            if (minInt.y > terrain.terrainColliderBlob.Value.maxHeight || maxInt.y < terrain.terrainColliderBlob.Value.minHeight)
                return;

            var terrainProcessor = new DistanceBetweenAllProcessor<T>
            {
                capsule          = capsule,
                capsuleTransform = capsuleTransform,
                maxDistance      = maxDistance,
                minHeight        = (short)minInt.y,
                maxHeight        = (short)minInt.y,
                heightOffset     = terrain.baseHeightOffset,
                scale            = terrain.scale,
                terrainTransform = terrainTransform,
                processor        = (T*)UnsafeUtility.AddressOf(ref processor)
            };
            terrain.terrainColliderBlob.Value.FindTriangles(minInt.x, minInt.z, maxInt.x, maxInt.z, ref terrainProcessor);
        }

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TerrainCollider targetTerrain,
                                        in RigidTransform targetTerrainTransform,
                                        out ColliderCastResult result)
        {
            var targetTerrainTransformInverse = math.inverse(targetTerrainTransform);
            var casterInTargetSpace           = math.mul(targetTerrainTransformInverse, castStart);
            var aabb                          =
                Physics.AabbFrom(capsuleToCast, in casterInTargetSpace, casterInTargetSpace.pos + math.rotate(targetTerrainTransformInverse, castEnd - castStart.pos));

            var inverseScale = math.rcp(targetTerrain.scale);
            var validAxes    = math.isfinite(inverseScale);
            result           = default;
            var crosses      = (aabb.min < 0f) & (aabb.max > 0f);
            if (math.any(!crosses & !validAxes))
                return false;

            Physics.GetCenterExtents(aabb, out var center, out var extents);
            center  *= inverseScale;
            extents *= inverseScale;

            var min     = (int3)math.floor(center - extents);
            var max     = (int3)math.ceil(center + extents);
            min.y      -= targetTerrain.baseHeightOffset;
            max.y      -= targetTerrain.baseHeightOffset;
            var minInt  = math.select(short.MinValue, min, validAxes);
            var maxInt  = math.select(short.MaxValue, max, validAxes);

            if (minInt.y > targetTerrain.terrainColliderBlob.Value.maxHeight || maxInt.y < targetTerrain.terrainColliderBlob.Value.minHeight)
                return false;

            var processor = new CastProcessor
            {
                bestDistance     = float.MaxValue,
                bestIndex        = -1,
                found            = false,
                invalid          = false,
                capsule          = capsuleToCast,
                castStart        = castStart,
                castEnd          = castEnd,
                terrainTransform = targetTerrainTransform,
                minHeight        = (short)minInt.y,
                maxHeight        = (short)minInt.y,
                heightOffset     = targetTerrain.baseHeightOffset,
                scale            = targetTerrain.scale,
            };
            targetTerrain.terrainColliderBlob.Value.FindTriangles(minInt.x, minInt.z, maxInt.x, maxInt.z, ref processor);

            if (processor.invalid || !processor.found)
                return false;

            var hitTransform     = castStart;
            hitTransform.pos    += math.normalize(castEnd - castStart.pos) * processor.bestDistance;
            var triangleIndices  = targetTerrain.terrainColliderBlob.Value.GetTriangle(processor.bestIndex);
            var triangle         = PointRayTerrain.CreateLocalTriangle(ref targetTerrain.terrainColliderBlob.Value,
                                                                       triangleIndices,
                                                                       targetTerrain.baseHeightOffset,
                                                                       targetTerrain.scale);
            CapsuleTriangle.DistanceBetween(in triangle,
                                            in targetTerrainTransform,
                                            in capsuleToCast,
                                            in hitTransform,
                                            1f,
                                            out var distanceResult);
            result = new ColliderCastResult
            {
                hitpoint                 = distanceResult.hitpointB,
                normalOnCaster           = distanceResult.normalB,
                normalOnTarget           = distanceResult.normalA,
                subColliderIndexOnCaster = distanceResult.subColliderIndexB,
                subColliderIndexOnTarget = processor.bestIndex,
                distance                 = math.distance(hitTransform.pos, castStart.pos)
            };
            return true;
        }

        public static bool ColliderCast(in TerrainCollider terrainToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            var castReverse         = castStart.pos - castEnd;
            var worldToCasterSpace  = math.inverse(castStart);
            var targetInCasterSpace = math.mul(worldToCasterSpace, targetCapsuleTransform);
            var reverseCastEnd      = targetInCasterSpace.pos + math.rotate(worldToCasterSpace, castReverse);
            var aabb                = Physics.AabbFrom(targetCapsule, in targetInCasterSpace, reverseCastEnd);

            var inverseScale = math.rcp(terrainToCast.scale);
            var validAxes    = math.isfinite(inverseScale);
            result           = default;
            var crosses      = (aabb.min < 0f) & (aabb.max > 0f);
            if (math.any(!crosses & !validAxes))
                return false;

            Physics.GetCenterExtents(aabb, out var center, out var extents);
            center  *= inverseScale;
            extents *= inverseScale;

            var min     = (int3)math.floor(center - extents);
            var max     = (int3)math.ceil(center + extents);
            min.y      -= terrainToCast.baseHeightOffset;
            max.y      -= terrainToCast.baseHeightOffset;
            var minInt  = math.select(short.MinValue, min, validAxes);
            var maxInt  = math.select(short.MaxValue, max, validAxes);

            if (minInt.y > terrainToCast.terrainColliderBlob.Value.maxHeight || maxInt.y < terrainToCast.terrainColliderBlob.Value.minHeight)
                return false;

            var processor = new CastProcessor
            {
                bestDistance     = float.MaxValue,
                bestIndex        = -1,
                found            = false,
                invalid          = false,
                capsule          = targetCapsule,
                castStart        = castStart,
                castEnd          = castEnd,
                terrainTransform = RigidTransform.identity,
                minHeight        = (short)minInt.y,
                maxHeight        = (short)minInt.y,
                heightOffset     = terrainToCast.baseHeightOffset,
                scale            = terrainToCast.scale,
            };
            terrainToCast.terrainColliderBlob.Value.FindTriangles(minInt.x, minInt.z, maxInt.x, maxInt.z, ref processor);

            if (processor.invalid || !processor.found)
            {
                result = default;
                return false;
            }

            var hitTransform     = castStart;
            hitTransform.pos    += math.normalize(castEnd - castStart.pos) * processor.bestDistance;
            var triangleIndices  = terrainToCast.terrainColliderBlob.Value.GetTriangle(processor.bestIndex);
            var triangle         = PointRayTerrain.CreateLocalTriangle(ref terrainToCast.terrainColliderBlob.Value,
                                                                       triangleIndices,
                                                                       terrainToCast.baseHeightOffset,
                                                                       terrainToCast.scale);
            CapsuleTriangle.DistanceBetween(in triangle,
                                            in hitTransform,
                                            in targetCapsule,
                                            in targetCapsuleTransform,
                                            1f,
                                            out var distanceResult);
            result = new ColliderCastResult
            {
                hitpoint                 = distanceResult.hitpointA,
                normalOnCaster           = distanceResult.normalA,
                normalOnTarget           = distanceResult.normalB,
                subColliderIndexOnCaster = processor.bestIndex,
                subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                distance                 = math.distance(hitTransform.pos, castStart.pos)
            };
            return true;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TerrainCollider terrain,
                                                                          in RigidTransform terrainTransform,
                                                                          in CapsuleCollider capsule,
                                                                          in RigidTransform capsuleTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            var triangleIndices = terrain.terrainColliderBlob.Value.GetTriangle(distanceResult.subColliderIndexA);
            var triangle        = PointRayTerrain.CreateLocalTriangle(ref terrain.terrainColliderBlob.Value, triangleIndices, terrain.baseHeightOffset, terrain.scale);
            return CapsuleTriangle.UnityContactsBetween(in triangle, in terrainTransform, in capsule, in capsuleTransform, in distanceResult);
        }

        struct DistanceBetweenClosestProcessor : IDistanceBetweenAllProcessor
        {
            public int   bestIndex;
            public float bestDistance;

            public void Execute(in ColliderDistanceResult result)
            {
                if (bestIndex < 0 || result.distance < bestDistance)
                {
                    bestIndex    = result.subColliderIndexA;
                    bestDistance = result.distance;
                }
            }
        }

        unsafe struct DistanceBetweenAllProcessor<T> : TerrainColliderBlob.IFindTrianglesProcessor where T : unmanaged, IDistanceBetweenAllProcessor
        {
            public CapsuleCollider capsule;
            public RigidTransform  capsuleTransform;
            public float           maxDistance;
            public short           minHeight;
            public short           maxHeight;

            public int            heightOffset;
            public float3         scale;
            public RigidTransform terrainTransform;

            public T* processor;

            public ulong FilterPatch(ref TerrainColliderBlob.Patch patch, ulong borderMask, short quadsPerBit)
            {
                var mask  = patch.GetFilteredQuadMaskFromHeights(minHeight, maxHeight);
                mask     &= borderMask;
                return mask;
            }

            public void Execute(ref TerrainColliderBlob blob, int3 triangleHeightIndices, int triangleIndex)
            {
                var triangle                 = PointRayTerrain.CreateLocalTriangle(ref blob, triangleHeightIndices, heightOffset, scale);
                var hit                      = CapsuleTriangle.DistanceBetween(in triangle, in terrainTransform, in capsule, in capsuleTransform, maxDistance, out var tempResult);
                tempResult.subColliderIndexA = triangleIndex;
                if (hit)
                {
                    processor->Execute(in tempResult);
                }
            }
        }

        struct CastProcessor : TerrainColliderBlob.IFindTrianglesProcessor
        {
            public CapsuleCollider capsule;
            public RigidTransform  castStart;
            public float3          castEnd;
            public RigidTransform  terrainTransform;
            public short           minHeight;
            public short           maxHeight;

            public int    heightOffset;
            public float3 scale;

            public float bestDistance;
            public int   bestIndex;
            public bool  found;
            public bool  invalid;

            public ulong FilterPatch(ref TerrainColliderBlob.Patch patch, ulong borderMask, short quadsPerBit)
            {
                if (invalid)
                    return 0;
                var mask  = patch.GetFilteredQuadMaskFromHeights(minHeight, maxHeight);
                mask     &= borderMask;
                return mask;
            }

            public void Execute(ref TerrainColliderBlob blob, int3 triangleHeightIndices, int triangleIndex)
            {
                if (invalid)
                    return;

                var triangle = PointRayTerrain.CreateLocalTriangle(ref blob, triangleHeightIndices, heightOffset, scale);
                Physics.ScaleStretchCollider(ref triangle, 1f, scale);
                // Check that we don't start already intersecting.
                if (CapsuleTriangle.DistanceBetween(in triangle, in terrainTransform, in capsule, in castStart, 0f, out _))
                {
                    invalid = true;
                    return;
                }
                if (CapsuleTriangle.ColliderCast(in capsule, in castStart, castEnd, in triangle, in terrainTransform, out var hit))
                {
                    if (!found || hit.distance < bestDistance)
                    {
                        found        = true;
                        bestDistance = hit.distance;
                        bestIndex    = triangleIndex;
                    }
                }
            }
        }
    }
}

