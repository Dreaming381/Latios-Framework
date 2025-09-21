using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayTerrain
    {
        public static bool DistanceBetween(float3 point, in TerrainCollider terrain, in RigidTransform terrainTransform, float maxDistance, out PointDistanceResult result)
        {
            var pointInScaledTerrainSpace = math.transform(math.inverse(terrainTransform), point);
            var inverseScale              = math.rcp(terrain.scale);
            var validAxes                 = math.isfinite(inverseScale);

            result = default;

            // If we have a zero scale, and the point is too far away from the plane, return early.
            if (math.any((math.abs(pointInScaledTerrainSpace) > maxDistance) & !validAxes))
                return false;

            var center   = pointInScaledTerrainSpace * inverseScale;
            var extents  = maxDistance * inverseScale;
            var min      = (int3)math.floor(center - extents);
            var max      = (int3)math.ceil(center + extents);
            min.y       -= terrain.baseHeightOffset;
            max.y       -= terrain.baseHeightOffset;
            var minInt   = math.select(short.MinValue, min, validAxes);
            var maxInt   = math.select(short.MaxValue, max, validAxes);

            if (minInt.y > terrain.terrainColliderBlob.Value.maxHeight || maxInt.y < terrain.terrainColliderBlob.Value.minHeight)
                return false;

            var processor = new PointProcessor
            {
                point            = point,
                maxDistance      = maxDistance,
                minHeight        = (short)minInt.y,
                maxHeight        = (short)minInt.y,
                heightOffset     = terrain.baseHeightOffset,
                scale            = terrain.scale,
                terrainTransform = terrainTransform,
                found            = false,
                result           = default
            };
            terrain.terrainColliderBlob.Value.FindTriangles(minInt.x, minInt.z, maxInt.x, maxInt.z, ref processor);
            result = processor.result;
            return processor.found;
        }

        public static bool Raycast(in Ray ray, in TerrainCollider terrain, in RigidTransform terrainTransform, out RaycastResult result)
        {
            var rayInScaledTerrainSpace = Ray.TransformRay(math.inverse(terrainTransform), in ray);

            var inverseScale = math.rcp(terrain.scale);
            var validAxes    = math.isfinite(inverseScale);

            result = default;

            // If we have a zero scale, and the ray does not cross the plane, return early.
            var rayMin  = math.min(rayInScaledTerrainSpace.start, rayInScaledTerrainSpace.end);
            var rayMax  = math.max(rayInScaledTerrainSpace.start, rayInScaledTerrainSpace.end);
            var crosses = (rayMin < 0f) & (rayMax > 0f);
            if (math.any(!crosses & !validAxes))
                return false;

            var rayAabbInScaledTerrainSpace = Physics.AabbFrom(in rayInScaledTerrainSpace);
            Physics.GetCenterExtents(rayAabbInScaledTerrainSpace, out var center, out var extents);
            center  *= inverseScale;
            extents *= inverseScale;

            var min     = (int3)math.floor(center - extents);
            var max     = (int3)math.ceil(center + extents);
            min.y      -= terrain.baseHeightOffset;
            max.y      -= terrain.baseHeightOffset;
            var minInt  = math.select(short.MinValue, min, validAxes);
            var maxInt  = math.select(short.MaxValue, max, validAxes);

            if (minInt.y > terrain.terrainColliderBlob.Value.maxHeight || maxInt.y < terrain.terrainColliderBlob.Value.minHeight)
                return false;

            var processor = new RayProcessor
            {
                ray              = ray,
                minHeight        = (short)minInt.y,
                maxHeight        = (short)minInt.y,
                heightOffset     = terrain.baseHeightOffset,
                scale            = terrain.scale,
                terrainTransform = terrainTransform,
                found            = false,
                result           = default
            };
            terrain.terrainColliderBlob.Value.FindTriangles(minInt.x, minInt.z, maxInt.x, maxInt.z, ref processor);
            result = processor.result;
            return processor.found;
        }

        struct PointProcessor : TerrainColliderBlob.IFindTrianglesProcessor
        {
            public float3 point;
            public float  maxDistance;
            public short  minHeight;
            public short  maxHeight;

            public int            heightOffset;
            public float3         scale;
            public RigidTransform terrainTransform;

            public bool                found;
            public PointDistanceResult result;

            public ulong FilterPatch(ref TerrainColliderBlob.Patch patch, ulong borderMask, short quadsPerBit)
            {
                var mask  = patch.GetFilteredQuadMaskFromHeights(minHeight, maxHeight);
                mask     &= borderMask;
                return mask;
            }

            public void Execute(ref TerrainColliderBlob blob, int3 triangleHeightIndices, int triangleIndex)
            {
                var triangle = CreateLocalTriangle(ref blob, triangleHeightIndices, heightOffset, scale);
                var hit      = PointRayTriangle.DistanceBetween(point, in triangle, in terrainTransform, maxDistance, out var tempResult);
                if (hit && (!found || tempResult.distance < result.distance))
                {
                    found                   = true;
                    result                  = tempResult;
                    result.subColliderIndex = triangleIndex;
                }
            }
        }

        struct RayProcessor : TerrainColliderBlob.IFindTrianglesProcessor
        {
            public Ray   ray;
            public short minHeight;
            public short maxHeight;

            public int            heightOffset;
            public float3         scale;
            public RigidTransform terrainTransform;

            public bool          found;
            public RaycastResult result;

            public ulong FilterPatch(ref TerrainColliderBlob.Patch patch, ulong borderMask, short quadsPerBit)
            {
                var mask  = patch.GetFilteredQuadMaskFromHeights(minHeight, maxHeight);
                mask     &= borderMask;
                return mask;
            }

            public void Execute(ref TerrainColliderBlob blob, int3 triangleHeightIndices, int triangleIndex)
            {
                var triangle = CreateLocalTriangle(ref blob, triangleHeightIndices, heightOffset, scale);
                var hit      = PointRayTriangle.Raycast(ray, in triangle, in terrainTransform, out var tempResult);
                if (hit && (!found || tempResult.distance < result.distance))
                {
                    found                   = true;
                    result                  = tempResult;
                    result.subColliderIndex = triangleIndex;
                }
            }
        }

        internal static TriangleCollider CreateLocalTriangle(ref TerrainColliderBlob blob, int3 triangleHeightIndices, int heightOffset, float3 scale)
        {
            var    heightsPerRow  = blob.quadsPerRow + 1;
            float3 xs             = triangleHeightIndices % heightsPerRow;
            float3 zs             = triangleHeightIndices / heightsPerRow;
            float3 ys             = new int3(blob.heights[triangleHeightIndices.x], blob.heights[triangleHeightIndices.y], blob.heights[triangleHeightIndices.z]) + heightOffset;
            var    triangle       = new TriangleCollider(new float3(xs.x, ys.x, zs.x), new float3(xs.y, ys.y, zs.y), new float3(xs.z, ys.z, zs.z));
            triangle.pointA      *= scale;
            triangle.pointB      *= scale;
            triangle.pointC      *= scale;
            return triangle;
        }

        internal static float3 CreateLocalVertex(ref TerrainColliderBlob blob, int2 heightCoordinate, int heightOffset, float3 scale)
        {
            var vertex = new float3(heightCoordinate.x, blob.heights[blob.ToHeight1D(heightCoordinate)] + heightOffset, heightCoordinate.y);
            return vertex * scale;
        }
    }
}

