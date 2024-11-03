using System;
using Latios.Transforms;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        #region Point vs Collider
        /// <summary>
        /// Checks if the closest surface point on the collider to the query point is within the signed maxDistance, where the sign is positive if the
        /// query point is outside the collider, and negative if the point is inside the collider. If closest surface point is within maxDistance,
        /// info about the surface point is generated.
        /// </summary>
        /// <param name="point">The query point</param>
        /// <param name="collider">The collider on which a surface point should be found</param>
        /// <param name="transform">The transform of the collider</param>
        /// <param name="maxDistance">A signed distance the distance between the surface point and the query point must be less or equal to for a "hit"
        /// to be registered. A value less than 0 requires that the point be inside the collider.</param>
        /// <param name="result">Info about the surface point found if it is close enough to the query point</param>
        /// <returns>Returns true if the closest surface point is within maxDistance of the query point</returns>
        /// <remarks>In the case of composite colliders, it is possible that the found surface point may be inside of one of the other subcolliders in the composite.
        /// This can only happen when the query point is inside the collider. This may cause problems for algorithms which intend to place objects on the surface
        /// of the composite collider. Also, in the case of composites, the closest surface point of each subcollider is found, and the subcollider with the smallest
        /// signed distance to the query point is reported (most negative if the query point is inside), even if it is technically not the closest surface point across
        /// all the composites.</remarks>
        public static bool DistanceBetween(float3 point, in Collider collider, in TransformQvvs transform, float maxDistance, out PointDistanceResult result)
        {
            return PointRayDispatch.DistanceBetween(point, in collider, in transform, maxDistance, out result);
        }
        #endregion

        #region Point vs Layer
        /// <summary>
        /// Checks if the closest surface point across all the colliders in the CollisionLayer to the query point is within the signed maxDistance,
        /// where the sign is positive if the query point is outside the collider, and negative if the point is inside the collider. If the closest surface
        /// point is within maxDistance, info about the surface point is generated.
        /// </summary>
        /// <param name="point">The query point</param>
        /// <param name="layer">The layer containing the colliders to query against</param>
        /// <param name="maxDistance">A signed distance the distance between the surface point and the query point must be less or equal to for a "hit"
        /// to be registered. A value less than 0 requires that the point be inside the collider.</param>
        /// <param name="result">Info about the surface point found if it is close enough to the query point</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>Returns true if the closest surface point is within maxDistance of the query point</returns>
        /// <remarks>In the case of composite colliders, it is possible that the found surface point may be inside of one of the other subcolliders in the composite.
        /// This can only happen when the query point is inside the collider. This may cause problems for algorithms which intend to place objects on the surface
        /// of the composite collider. Also, in the case of composites, the closest surface point of each subcollider is found, and the subcollider with the smallest
        /// signed distance to the query point is reported (most negative if the query point is inside), even if it is technically not the closest surface point across
        /// all the composites.
        /// These same rules also apply to colliders in CollisionLayer. A reported surface point may be inside another collider inside the CollisionLayer. And the
        /// collider with the smallest signed distance to the query point is reported. If you want to find all hits, use FindObjects instead.</remarks>
        public static bool DistanceBetween(float3 point, in CollisionLayer layer, float maxDistance, out PointDistanceResult result, out LayerBodyInfo layerBodyInfo)
        {
            result             = default;
            layerBodyInfo      = default;
            var processor      = new LayerQueryProcessors.PointDistanceClosestImmediateProcessor(point, maxDistance, ref result, ref layerBodyInfo);
            var offsetDistance = math.max(maxDistance, 0f);
            FindObjects(AabbFrom(point - offsetDistance, point + offsetDistance), layer, processor).RunImmediate();
            var hit                 = result.subColliderIndex >= 0;
            result.subColliderIndex = math.max(result.subColliderIndex, 0);
            return hit;
        }

        /// <summary>
        /// Checks if the closest surface point across all the colliders in all CollisionLayers to the query point is within the signed maxDistance,
        /// where the sign is positive if the query point is outside the collider, and negative if the point is inside the collider. If the closest surface
        /// point is within maxDistance, info about the surface point is generated.
        /// </summary>
        /// <param name="point">The query point</param>
        /// <param name="layers">The layers containing the colliders to query against</param>
        /// <param name="maxDistance">A signed distance the distance between the surface point and the query point must be less or equal to for a "hit"
        /// to be registered. A value less than 0 requires that the point be inside the collider.</param>
        /// <param name="result">Info about the surface point found if it is close enough to the query point</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>Returns true if the closest surface point is within maxDistance of the query point</returns>
        /// <remarks>In the case of composite colliders, it is possible that the found surface point may be inside of one of the other subcolliders in the composite.
        /// This can only happen when the query point is inside the collider. This may cause problems for algorithms which intend to place objects on the surface
        /// of the composite collider. Also, in the case of composites, the closest surface point of each subcollider is found, and the subcollider with the smallest
        /// signed distance to the query point is reported (most negative if the query point is inside), even if it is technically not the closest surface point across
        /// all the composites.
        /// These same rules also apply to colliders in CollisionLayer. A reported surface point may be inside another collider inside the CollisionLayer. And the
        /// collider with the smallest signed distance to the query point is reported. If you want to find all hits, use FindObjects instead.</remarks>
        public static bool DistanceBetween(float3 point, ReadOnlySpan<CollisionLayer> layers, float maxDistance, out PointDistanceResult result, out LayerBodyInfo layerBodyInfo)
        {
            bool found    = false;
            result        = default;
            layerBodyInfo = default;
            int i         = 0;
            foreach (var layer in layers)
            {
                if (DistanceBetween(point, in layer, maxDistance, out var newResult, out var newLayerBodyInfo))
                {
                    if (!found || newResult.distance < result.distance)
                    {
                        found                    = true;
                        result                   = newResult;
                        layerBodyInfo            = newLayerBodyInfo;
                        layerBodyInfo.layerIndex = i;
                        maxDistance              = result.distance;
                    }
                }
                i++;
            }
            return found;
        }

        /// <summary>
        /// Checks if a closest surface point across any of the colliders in the CollisionLayer to the query point is within the signed maxDistance,
        /// where the sign is positive if the query point is outside the collider, and negative if the point is inside the collider. If a closest surface
        /// point is within maxDistance, info about the surface point is generated.
        /// </summary>
        /// <param name="point">The query point</param>
        /// <param name="layer">The layer containing the colliders to query against</param>
        /// <param name="maxDistance">A signed distance the distance between the surface point and the query point must be less or equal to for a "hit"
        /// to be registered. A value less than 0 requires that the point be inside the collider.</param>
        /// <param name="result">Info about the surface point found if it is close enough to the query point</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>Returns true if the closest surface point is within maxDistance of the query point</returns>
        /// <remarks>In the case of composite colliders, it is possible that the found surface point may be inside of one of the other subcolliders in the composite.
        /// This can only happen when the query point is inside the collider. This may cause problems for algorithms which intend to place objects on the surface
        /// of the composite collider. Also, in the case of composites, the closest surface point of each subcollider is found, and the subcollider with the smallest
        /// signed distance to the query point is reported (most negative if the query point is inside), even if it is technically not the closest surface point across
        /// all the composites.
        /// Similar rules also apply to colliders in CollisionLayer. A reported surface point may be inside another collider inside the CollisionLayer. However, only
        /// the first surface point within maxDistance found by the algorithm is reported.</remarks>
        public static bool DistanceBetweenAny(float3 point, in CollisionLayer layer, float maxDistance, out PointDistanceResult result, out LayerBodyInfo layerBodyInfo)
        {
            result             = default;
            layerBodyInfo      = default;
            var processor      = new LayerQueryProcessors.PointDistanceAnyImmediateProcessor(point, maxDistance, ref result, ref layerBodyInfo);
            var offsetDistance = math.max(maxDistance, 0f);
            FindObjects(AabbFrom(point - offsetDistance, point + offsetDistance), layer, processor).RunImmediate();
            var hit                 = result.subColliderIndex >= 0;
            result.subColliderIndex = math.max(result.subColliderIndex, 0);
            return hit;
        }

        /// <summary>
        /// Checks if a closest surface point across any of the colliders in any of the CollisionLayers to the query point is within the signed maxDistance,
        /// where the sign is positive if the query point is outside the collider, and negative if the point is inside the collider. If a closest surface
        /// point is within maxDistance, info about the surface point is generated.
        /// </summary>
        /// <param name="point">The query point</param>
        /// <param name="layer">The layer containing the colliders to query against</param>
        /// <param name="maxDistance">A signed distance the distance between the surface point and the query point must be less or equal to for a "hit"
        /// to be registered. A value less than 0 requires that the point be inside the collider.</param>
        /// <param name="result">Info about the surface point found if it is close enough to the query point</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>Returns true if the closest surface point is within maxDistance of the query point</returns>
        /// <remarks>In the case of composite colliders, it is possible that the found surface point may be inside of one of the other subcolliders in the composite.
        /// This can only happen when the query point is inside the collider. This may cause problems for algorithms which intend to place objects on the surface
        /// of the composite collider. Also, in the case of composites, the closest surface point of each subcollider is found, and the subcollider with the smallest
        /// signed distance to the query point is reported (most negative if the query point is inside), even if it is technically not the closest surface point across
        /// all the composites.
        /// Similar rules also apply to colliders in CollisionLayer. A reported surface point may be inside another collider inside the CollisionLayer. However, only
        /// the first surface point within maxDistance found by the algorithm is reported.</remarks>
        public static bool DistanceBetweenAny(float3 point, ReadOnlySpan<CollisionLayer> layers, float maxDistance, out PointDistanceResult result, out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            int i         = 0;
            foreach (var layer in layers)
            {
                if (DistanceBetweenAny(point, in layer, maxDistance, out result, out layerBodyInfo))
                {
                    layerBodyInfo.layerIndex = i;
                    return true;
                }
                i++;
            }
            return false;
        }
        #endregion

        #region Collider vs Collider
        /// <summary>
        /// Checks if the distance between the surfaces of two colliders are within maxDistance. If the colliders are overlapping, the determined distance is negative,
        /// and the magnitude represents the distance of penetration, that is, the minimum distance required to fully separate the two closest subcolliders.
        /// If the signed distance is less than maxDistance, info about the pair of surface points, one for each collider, is generated and reported.
        /// </summary>
        /// <param name="colliderA">The first of the two colliders to test for distance</param>
        /// <param name="transformA">The transform of the first of the two colliders</param>
        /// <param name="colliderB">The second of the two colliders to test for distance</param>
        /// <param name="transformB">The transform of the second of the two colliders</param>
        /// <param name="maxDistance">The signed distance the surface points must be less than in order for a "hit" to be registered. A value less than 0 requires that
        /// the colliders be overlapping.</param>
        /// <param name="result">Info about the surface points found that are within the required distance of each other. Any field suffixed 'A' in the result
        /// corresponds to the first collider, and any field suffixed 'B' in the result corresponds to the second collider. If the method returns false, the
        /// contents of this result are undefined.</param>
        /// <returns>Returns true if the signed distance of the surface points are less than maxDistance, false otherwise</returns>
        /// <remarks>If either colliderA or colliderB is a composite collider, the distance tests are performed individually across all subcolliders, and the subcollider
        /// with the smallest (or most negative in the case of overlaps) distance is used for the overall evaluation. The surface points reported may be inside other
        /// subcolliders. This may cause problems if this algorithm is used for depenetration, as only the individual subcolliders reported will be depenetrated, rather
        /// than the entire composite.</remarks>
        public static bool DistanceBetween(in Collider colliderA,
                                           in TransformQvvs transformA,
                                           in Collider colliderB,
                                           in TransformQvvs transformB,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var scaledColliderA = colliderA;
            var scaledColliderB = colliderB;

            ScaleStretchCollider(ref scaledColliderA, transformA.scale, transformA.stretch);
            ScaleStretchCollider(ref scaledColliderB, transformB.scale, transformB.stretch);
            return ColliderColliderDispatch.DistanceBetween(in scaledColliderA,
                                                            new RigidTransform(transformA.rotation, transformA.position),
                                                            in scaledColliderB,
                                                            new RigidTransform(transformB.rotation, transformB.position),
                                                            maxDistance,
                                                            out result);
        }

        /// <summary>
        /// Checks if the distance between the surfaces of all subcolliders between two colliders are within maxDistance. If the colliders are overlapping, the determined
        /// distance is negative. If the signed distance is less than maxDistance, info about the pair of surface points, one for each subcollider, is generated and
        /// dispatched to the processor.
        /// </summary>
        /// <param name="colliderA">The first of the two colliders to test for distance</param>
        /// <param name="transformA">The transform of the first of the two colliders</param>
        /// <param name="colliderB">The second of the two colliders to test for distance</param>
        /// <param name="transformB">The transform of the second of the two colliders</param>
        /// <param name="maxDistance">The signed distance the surface points must be less than in order for a "hit" to be registered. A value less than 0 requires that
        /// the colliders be overlapping.</param>
        /// <param name="processor">The processor that will receive a ColliderDistanceResult for each found pair of subcolliders.
        /// Use DistanceBetweenAllCache if you need a simple collector.</param>
        public static void DistanceBetweenAll<T>(in Collider colliderA,
                                                 in TransformQvvs transformA,
                                                 in Collider colliderB,
                                                 in TransformQvvs transformB,
                                                 float maxDistance,
                                                 ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            var scaledColliderA = colliderA;
            var scaledColliderB = colliderB;

            var context = new DistanceBetweenAllContext
            {
                numSubcollidersA = InternalQueryTypeUtilities.GetSubcolliders(in colliderA),
                numSubcollidersB = InternalQueryTypeUtilities.GetSubcolliders(in colliderB)
            };

            ScaleStretchCollider(ref scaledColliderA, transformA.scale, transformA.stretch);
            ScaleStretchCollider(ref scaledColliderB, transformB.scale, transformB.stretch);
            processor.Begin(in context);
            ColliderColliderDispatch.DistanceBetweenAll(in scaledColliderA,
                                                        new RigidTransform(transformA.rotation, transformA.position),
                                                        in scaledColliderB,
                                                        new RigidTransform(transformB.rotation, transformB.position),
                                                        maxDistance,
                                                        ref processor);
            processor.End(in context);
        }
        #endregion

        #region Collider vs Layer
        /// <summary>
        /// Checks if the surface of the collider is within maxDistance of any of the surfaces of the colliders in the CollisionLayer. If the collider is overlapping a
        /// collider in the CollisionLayer, the determined distance is negative. If the signed distance is less than maxDistance, info about the pair with the smallest
        /// (or most negative in the case of overlaps) is generated and reported.
        /// </summary>
        /// <param name="collider">The collider to test for distance against the CollisionLayer</param>
        /// <param name="transform">The transform of the collider to be tested</param>
        /// <param name="layer">The CollisionLayer containing colliders to test against</param>
        /// <param name="maxDistance">The signed distance the surface points must be less than in order for a "hit" to be registered. A value less than 0 requires that
        /// the colliders be overlapping.</param>
        /// <param name="result">Info about the surface points found that are within the required distance of each other. Any field suffixed 'A' in the result
        /// corresponds to the collider, and any field suffixed 'B' in the result corresponds to a collider in the CollisionLayer. If the method returns false, the
        /// contents of this result are undefined.</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>Returns true if the signed distance of the surface points are less than maxDistance, false otherwise</returns>
        /// <remarks>If either the test collider or the collider in the CollisionLayer is a composite collider, the distance tests are performed individually across all
        /// subcolliders, and the subcollider with the smallest (or most negative in the case of overlaps) distance is used for the overall evaluation. The surface points
        /// reported may be inside other subcolliders. This may cause problems if this algorithm is used for depenetration, as only the individual subcolliders reported
        /// will be depenetrated, rather than the entire composite.
        /// The same rule applies for colliders in the CollisionLayer. The surface points reported may be inside other colliders in the CollisionLayer. If you want to
        /// find all hits, use FindObjects instead.</remarks>
        public static bool DistanceBetween(in Collider collider,
                                           in TransformQvvs transform,
                                           in CollisionLayer layer,
                                           float maxDistance,
                                           out ColliderDistanceResult result,
                                           out LayerBodyInfo layerBodyInfo)
        {
            var scaledTransform = new RigidTransform(transform.rotation, transform.position);
            var scaledCollider  = collider;
            ScaleStretchCollider(ref scaledCollider, transform.scale, transform.stretch);

            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.ColliderDistanceClosestImmediateProcessor(in scaledCollider,
                                                                                               in scaledTransform,
                                                                                               maxDistance,
                                                                                               ref result,
                                                                                               ref layerBodyInfo);
            var aabb            = AabbFrom(in scaledCollider, in scaledTransform);
            var offsetDistance  = math.max(maxDistance, 0f);
            aabb.min           -= offsetDistance;
            aabb.max           += offsetDistance;
            FindObjects(aabb, in layer, in processor).RunImmediate();
            var hit                  = result.subColliderIndexB >= 0;
            result.subColliderIndexB = math.max(result.subColliderIndexB, 0);
            return hit;
        }

        /// <summary>
        /// Checks if the surface of the collider is within maxDistance of any of the surfaces of the colliders in all the CollisionLayers. If the collider is overlapping a
        /// collider in the CollisionLayers, the determined distance is negative. If the signed distance is less than maxDistance, info about the pair with the smallest
        /// (or most negative in the case of overlaps) is generated and reported.
        /// </summary>
        /// <param name="collider">The collider to test for distance against the CollisionLayer</param>
        /// <param name="transform">The transform of the collider to be tested</param>
        /// <param name="layers">The CollisionLayers containing colliders to test against</param>
        /// <param name="maxDistance">The signed distance the surface points must be less than in order for a "hit" to be registered. A value less than 0 requires that
        /// the colliders be overlapping.</param>
        /// <param name="result">Info about the surface points found that are within the required distance of each other. Any field suffixed 'A' in the result
        /// corresponds to the collider, and any field suffixed 'B' in the result corresponds to a collider in the CollisionLayer. If the method returns false, the
        /// contents of this result are undefined.</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>Returns true if the signed distance of the surface points are less than maxDistance, false otherwise</returns>
        /// <remarks>If either the test collider or the collider in the CollisionLayer is a composite collider, the distance tests are performed individually across all
        /// subcolliders, and the subcollider with the smallest (or most negative in the case of overlaps) distance is used for the overall evaluation. The surface points
        /// reported may be inside other subcolliders. This may cause problems if this algorithm is used for depenetration, as only the individual subcolliders reported
        /// will be depenetrated, rather than the entire composite.
        /// The same rule applies for colliders in the CollisionLayer. The surface points reported may be inside other colliders in the CollisionLayer. If you want to
        /// find all hits, use FindObjects instead.</remarks>
        public static bool DistanceBetween(in Collider collider,
                                           in TransformQvvs transform,
                                           ReadOnlySpan<CollisionLayer> layers,
                                           float maxDistance,
                                           out ColliderDistanceResult result,
                                           out LayerBodyInfo layerBodyInfo)
        {
            bool found    = false;
            result        = default;
            layerBodyInfo = default;
            int i         = 0;
            foreach (var layer in layers)
            {
                if (DistanceBetween(in collider, in transform, in layer, maxDistance, out var newResult, out var newLayerBodyInfo))
                {
                    if (!found || newResult.distance < result.distance)
                    {
                        found                    = true;
                        result                   = newResult;
                        layerBodyInfo            = newLayerBodyInfo;
                        layerBodyInfo.layerIndex = i;
                        maxDistance              = result.distance;
                    }
                }
                i++;
            }
            return found;
        }

        /// <summary>
        /// Checks if the surface of the collider is within maxDistance of any of the surfaces of the colliders the CollisionLayer. If the collider is overlapping a
        /// collider in the CollisionLayer, the determined distance is negative. If the signed distance is less than maxDistance, info about the first pair found is
        /// generated and reported.
        /// </summary>
        /// <param name="collider">The collider to test for distance against the CollisionLayer</param>
        /// <param name="transform">The transform of the collider to be tested</param>
        /// <param name="layer">The CollisionLayer containing colliders to test against</param>
        /// <param name="maxDistance">The signed distance the surface points must be less than in order for a "hit" to be registered. A value less than 0 requires that
        /// the colliders be overlapping.</param>
        /// <param name="result">Info about the surface points found that are within the required distance of each other. Any field suffixed 'A' in the result
        /// corresponds to the collider, and any field suffixed 'B' in the result corresponds to a collider in the CollisionLayer. If the method returns false, the
        /// contents of this result are undefined.</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>Returns true if the signed distance of the surface points are less than maxDistance, false otherwise</returns>
        /// <remarks>If either the test collider or the collider in the CollisionLayer is a composite collider, the distance tests are performed individually across all
        /// subcolliders, and the subcollider with the smallest (or most negative in the case of overlaps) distance is used for the overall evaluation. The surface points
        /// reported may be inside other subcolliders. This may cause problems if this algorithm is used for depenetration, as only the individual subcolliders reported
        /// will be depenetrated, rather than the entire composite.
        /// Similar rules apply for colliders in the CollisionLayer. The surface points reported may be inside other colliders in the CollisionLayer. However, only the
        /// first result found by the algorithm is reported.</remarks>
        public static bool DistanceBetweenAny(Collider collider,
                                              in TransformQvvs transform,
                                              in CollisionLayer layer,
                                              float maxDistance,
                                              out ColliderDistanceResult result,
                                              out LayerBodyInfo layerBodyInfo)
        {
            var scaledTransform = new RigidTransform(transform.rotation, transform.position);
            var scaledCollider  = collider;
            ScaleStretchCollider(ref scaledCollider, transform.scale, transform.stretch);

            result              = default;
            layerBodyInfo       = default;
            var processor       = new LayerQueryProcessors.ColliderDistanceAnyImmediateProcessor(in scaledCollider, in scaledTransform, maxDistance, ref result, ref layerBodyInfo);
            var aabb            = AabbFrom(in scaledCollider, in scaledTransform);
            var offsetDistance  = math.max(maxDistance, 0f);
            aabb.min           -= offsetDistance;
            aabb.max           += offsetDistance;
            FindObjects(aabb, in layer, in processor).RunImmediate();
            var hit                  = result.subColliderIndexB >= 0;
            result.subColliderIndexB = math.max(result.subColliderIndexB, 0);
            return hit;
        }

        /// <summary>
        /// Checks if the surface of the collider is within maxDistance of any of the surfaces of the colliders in any of the CollisionLayers. If the collider is overlapping a
        /// collider in the CollisionLayer, the determined distance is negative. If the signed distance is less than maxDistance, info about the first pair found is
        /// generated and reported.
        /// </summary>
        /// <param name="collider">The collider to test for distance against the CollisionLayer</param>
        /// <param name="transform">The transform of the collider to be tested</param>
        /// <param name="layers">The CollisionLayers containing colliders to test against</param>
        /// <param name="maxDistance">The signed distance the surface points must be less than in order for a "hit" to be registered. A value less than 0 requires that
        /// the colliders be overlapping.</param>
        /// <param name="result">Info about the surface points found that are within the required distance of each other. Any field suffixed 'A' in the result
        /// corresponds to the collider, and any field suffixed 'B' in the result corresponds to a collider in the CollisionLayer. If the method returns false, the
        /// contents of this result are undefined.</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>Returns true if the signed distance of the surface points are less than maxDistance, false otherwise</returns>
        /// <remarks>If either the test collider or the collider in the CollisionLayer is a composite collider, the distance tests are performed individually across all
        /// subcolliders, and the subcollider with the smallest (or most negative in the case of overlaps) distance is used for the overall evaluation. The surface points
        /// reported may be inside other subcolliders. This may cause problems if this algorithm is used for depenetration, as only the individual subcolliders reported
        /// will be depenetrated, rather than the entire composite.
        /// Similar rules apply for colliders in the CollisionLayer. The surface points reported may be inside other colliders in the CollisionLayer. However, only the
        /// first result found by the algorithm is reported.</remarks>
        public static bool DistanceBetweenAny(in Collider collider,
                                              in TransformQvvs transform,
                                              ReadOnlySpan<CollisionLayer> layers,
                                              float maxDistance,
                                              out ColliderDistanceResult result,
                                              out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            int i         = 0;
            foreach (var layer in layers)
            {
                if (DistanceBetweenAny(collider, in transform, in layer, maxDistance, out result, out layerBodyInfo))
                {
                    layerBodyInfo.layerIndex = i;
                    return true;
                }
                i++;
            }
            return false;
        }
        #endregion
    }

    #region Interfaces
    /// <summary>
    /// An interface whose Execute method is invoked for each primitive pair found in a DistanceBetweenAll operation.
    /// </summary>
    public interface IDistanceBetweenAllProcessor
    {
        /// <summary>
        /// Called whenever a pair is found between any two subcolliders from each collider
        /// </summary>
        void Execute(in ColliderDistanceResult result);

        /// <summary>
        /// Called before any Execute() call. Used to initialize or reset resources.
        /// </summary>
        public void Begin(in DistanceBetweenAllContext context)
        {
        }

        /// <summary>
        /// Called after all Execute() calls. Used to finalize and reorganize any backing resources.
        /// </summary>
        public void End(in DistanceBetweenAllContext context)
        {
        }
    }

    /// <summary>
    /// A context used to provide additional info about the colliders being processed in a DistanceBetweenAll() operation.
    /// </summary>
    public struct DistanceBetweenAllContext
    {
        public int numSubcollidersA;
        public int numSubcollidersB;
    }
    #endregion
}

