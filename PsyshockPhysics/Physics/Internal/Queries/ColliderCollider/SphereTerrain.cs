using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereTerrain
    {
        public static bool DistanceBetween(in TerrainCollider terrain,
                                           in RigidTransform terrainTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var sphereCenterInWorld = math.transform(sphereTransform, sphere.center);
            var hit                 = PointRayTerrain.DistanceBetween(sphereCenterInWorld, in terrain, in terrainTransform, maxDistance + sphere.radius, out var pointResult);
            if (!hit)
            {
                result = default;
                return false;
            }

            var triangleIndices      = terrain.terrainColliderBlob.Value.GetTriangle(pointResult.subColliderIndex);
            var triangle             = PointRayTerrain.CreateLocalTriangle(ref terrain.terrainColliderBlob.Value, triangleIndices, terrain.baseHeightOffset, terrain.scale);
            hit                      = SphereTriangle.DistanceBetween(in triangle, in terrainTransform, in sphere, in sphereTransform, maxDistance, out result);
            result.subColliderIndexA = pointResult.subColliderIndex;
            return hit;
        }

        public static unsafe void DistanceBetweenAll<T>(in TerrainCollider terrain,
                                                        in RigidTransform terrainTransform,
                                                        in SphereCollider sphere,
                                                        in RigidTransform sphereTransform,
                                                        float maxDistance,
                                                        ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            var point            = math.transform(sphereTransform, sphere.center);
            var pointMaxDistance = maxDistance + sphere.radius;

            var pointInScaledTerrainSpace = math.transform(math.inverse(terrainTransform), point);
            var inverseScale              = math.rcp(terrain.scale);
            var validAxes                 = math.isfinite(inverseScale);

            // If we have a zero scale, and the point is too far away from the plane, return early.
            if (math.any((math.abs(pointInScaledTerrainSpace) > pointMaxDistance) & !validAxes))
                return;

            var center      = pointInScaledTerrainSpace * inverseScale;
            var extents     = pointMaxDistance * inverseScale;
            var min         = (int3)math.floor(center - extents);
            var centerPlus  = center + extents;
            var max         = (int3) new float3(math.floor(centerPlus.xz), math.ceil(centerPlus.y)).xzy;
            min.y          -= terrain.baseHeightOffset;
            max.y          -= terrain.baseHeightOffset;
            var minInt      = math.select(short.MinValue, min, validAxes);
            var maxInt      = math.select(short.MaxValue, max, validAxes);

            if (minInt.y > terrain.terrainColliderBlob.Value.maxHeight || maxInt.y < terrain.terrainColliderBlob.Value.minHeight)
                return;

            var terrainProcessor = new DistanceBetweenAllProcessor<T>
            {
                sphere           = sphere,
                sphereTransform  = sphereTransform,
                maxDistance      = maxDistance,
                minHeight        = (short)minInt.y,
                maxHeight        = (short)maxInt.y,
                heightOffset     = terrain.baseHeightOffset,
                scale            = terrain.scale,
                terrainTransform = terrainTransform,
                processor        = (T*)UnsafeUtility.AddressOf(ref processor)
            };
            terrain.terrainColliderBlob.Value.FindTriangles(minInt.x, minInt.z, maxInt.x, maxInt.z, ref terrainProcessor);
        }

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TerrainCollider targetTerrain,
                                        in RigidTransform targetTerrainTransform,
                                        out ColliderCastResult result)
        {
            var targetTerrainTransformInverse  = math.inverse(targetTerrainTransform);
            var casterInTargetSpace            = math.mul(targetTerrainTransformInverse, castStart);
            var start                          = math.transform(casterInTargetSpace, sphereToCast.center);
            var ray                            = new Ray(start, start + math.rotate(targetTerrainTransformInverse, castEnd - castStart.pos));
            var aabb                           = Physics.AabbFrom(ray.start, ray.end);
            aabb.min                          -= sphereToCast.radius;
            aabb.max                          += sphereToCast.radius;

            var inverseScale = math.rcp(targetTerrain.scale);
            var validAxes    = math.isfinite(inverseScale);
            result           = default;
            var crosses      = (aabb.min < 0f) & (aabb.max > 0f);
            if (math.any(!crosses & !validAxes))
                return false;

            Physics.GetCenterExtents(aabb, out var center, out var extents);
            center  *= inverseScale;
            extents *= inverseScale;

            var min         = (int3)math.floor(center - extents);
            var centerPlus  = center + extents;
            var max         = (int3) new float3(math.floor(centerPlus.xz), math.ceil(centerPlus.y)).xzy;
            min.y          -= targetTerrain.baseHeightOffset;
            max.y          -= targetTerrain.baseHeightOffset;
            var minInt      = math.select(short.MinValue, min, validAxes);
            var maxInt      = math.select(short.MaxValue, max, validAxes);

            if (minInt.y > targetTerrain.terrainColliderBlob.Value.maxHeight || maxInt.y < targetTerrain.terrainColliderBlob.Value.minHeight)
                return false;

            var processor = new CastProcessor
            {
                bestFraction            = float.MaxValue,
                bestIndex               = -1,
                found                   = false,
                invalid                 = false,
                radius                  = sphereToCast.radius,
                rayInScaledTerrainSpace = ray,
                minHeight               = (short)minInt.y,
                maxHeight               = (short)maxInt.y,
                heightOffset            = targetTerrain.baseHeightOffset,
                scale                   = targetTerrain.scale,
            };
            targetTerrain.terrainColliderBlob.Value.FindTriangles(minInt.x, minInt.z, maxInt.x, maxInt.z, ref processor);

            if (processor.invalid || !processor.found)
                return false;

            var hitTransform    = castStart;
            hitTransform.pos    = math.lerp(castStart.pos, castEnd, processor.bestFraction);
            var triangleIndices = targetTerrain.terrainColliderBlob.Value.GetTriangle(processor.bestIndex);
            var triangle        = PointRayTerrain.CreateLocalTriangle(ref targetTerrain.terrainColliderBlob.Value,
                                                                      triangleIndices,
                                                                      targetTerrain.baseHeightOffset,
                                                                      targetTerrain.scale);
            SphereTriangle.DistanceBetween(in triangle,
                                           in targetTerrainTransform,
                                           in sphereToCast,
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
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            var castReverse         = castStart.pos - castEnd;
            var worldToCasterSpace  = math.inverse(castStart);
            var start               = math.transform(targetSphereTransform, targetSphere.center);
            var ray                 = new Ray(math.transform(worldToCasterSpace, start), math.transform(worldToCasterSpace, start + castReverse));
            var aabb                = Physics.AabbFrom(ray.start, ray.end);
            aabb.min               -= targetSphere.radius;
            aabb.max               += targetSphere.radius;

            var inverseScale = math.rcp(terrainToCast.scale);
            var validAxes    = math.isfinite(inverseScale);
            result           = default;
            var crosses      = (aabb.min < 0f) & (aabb.max > 0f);
            if (math.any(!crosses & !validAxes))
                return false;

            Physics.GetCenterExtents(aabb, out var center, out var extents);
            center  *= inverseScale;
            extents *= inverseScale;

            var min         = (int3)math.floor(center - extents);
            var centerPlus  = center + extents;
            var max         = (int3) new float3(math.floor(centerPlus.xz), math.ceil(centerPlus.y)).xzy;
            min.y          -= terrainToCast.baseHeightOffset;
            max.y          -= terrainToCast.baseHeightOffset;
            var minInt      = math.select(short.MinValue, min, validAxes);
            var maxInt      = math.select(short.MaxValue, max, validAxes);

            if (minInt.y > terrainToCast.terrainColliderBlob.Value.maxHeight || maxInt.y < terrainToCast.terrainColliderBlob.Value.minHeight)
                return false;

            var processor = new CastProcessor
            {
                bestFraction            = float.MaxValue,
                bestIndex               = -1,
                found                   = false,
                invalid                 = false,
                radius                  = targetSphere.radius,
                rayInScaledTerrainSpace = ray,
                minHeight               = (short)minInt.y,
                maxHeight               = (short)maxInt.y,
                heightOffset            = terrainToCast.baseHeightOffset,
                scale                   = terrainToCast.scale,
            };
            terrainToCast.terrainColliderBlob.Value.FindTriangles(minInt.x, minInt.z, maxInt.x, maxInt.z, ref processor);

            if (processor.invalid || !processor.found)
            {
                result = default;
                return false;
            }

            var hitTransform    = castStart;
            hitTransform.pos    = math.lerp(castStart.pos, castEnd, processor.bestFraction);
            var triangleIndices = terrainToCast.terrainColliderBlob.Value.GetTriangle(processor.bestIndex);
            var triangle        = PointRayTerrain.CreateLocalTriangle(ref terrainToCast.terrainColliderBlob.Value,
                                                                      triangleIndices,
                                                                      terrainToCast.baseHeightOffset,
                                                                      terrainToCast.scale);
            SphereTriangle.DistanceBetween(in triangle,
                                           in hitTransform,
                                           in targetSphere,
                                           in targetSphereTransform,
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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TerrainCollider triMesh,
                                                                          in RigidTransform triMeshTransform,
                                                                          in SphereCollider sphere,
                                                                          in RigidTransform sphereTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        unsafe struct DistanceBetweenAllProcessor<T> : TerrainColliderBlob.IFindTrianglesProcessor where T : unmanaged, IDistanceBetweenAllProcessor
        {
            public SphereCollider sphere;
            public RigidTransform sphereTransform;
            public float          maxDistance;
            public short          minHeight;
            public short          maxHeight;

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
                var hit                      = SphereTriangle.DistanceBetween(in triangle, in terrainTransform, in sphere, in sphereTransform, maxDistance, out var tempResult);
                tempResult.subColliderIndexA = triangleIndex;
                if (hit)
                {
                    processor->Execute(in tempResult);
                }
            }
        }

        struct CastProcessor : TerrainColliderBlob.IFindTrianglesProcessor
        {
            public Ray   rayInScaledTerrainSpace;
            public float radius;
            public short minHeight;
            public short maxHeight;

            public int    heightOffset;
            public float3 scale;

            public float bestFraction;
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
                if (PointRayTriangle.PointTriangleDistance(rayInScaledTerrainSpace.start, in triangle, radius, out _))
                {
                    invalid = true;
                    return;
                }
                if (PointRayTriangle.RaycastRoundedTriangle(in rayInScaledTerrainSpace, triangle.AsSimdFloat3(), radius, out var fraction, out _))
                {
                    if (!found || fraction < bestFraction)
                    {
                        found        = true;
                        bestFraction = fraction;
                        bestIndex    = triangleIndex;
                    }
                }
            }
        }
    }
}

