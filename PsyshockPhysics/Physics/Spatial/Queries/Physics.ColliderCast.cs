using System;
using System.Diagnostics;
using Latios.Transforms;
using Unity.Mathematics;
using UnityEditor;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        #region Collider vs Collider
        /// <summary>
        /// Sweeps a collider beginning at castStart through to castEnd and checks if the collider hits
        /// another target collider. If so, results about the hit are generated and reported. It is assumed
        /// that rotation, scale, and stretch remain constant throughout the operation. No hit is reported
        /// if the casted collider starts already overlapping the target.
        /// </summary>
        /// <param name="colliderToCast">The casted collider that should sweep through space</param>
        /// <param name="castStart">The transform of the casted collider at the start of the cast</param>
        /// <param name="castEnd">The position of the casted collider at the end of the cast range</param>
        /// <param name="targetCollider">The target collider that the casted collider should check for a hit against</param>
        /// <param name="targetTransform">The target collider's transform</param>
        /// <param name="result">The resulting information about the hit if there is one. If there is no hit,
        /// the contents of the result are undefined.</param>
        /// <returns>True if a hit was found, false otherwise</returns>
        public static bool ColliderCast(in Collider colliderToCast,
                                        in TransformQvvs castStart,
                                        float3 castEnd,
                                        in Collider targetCollider,
                                        in TransformQvvs targetTransform,
                                        out ColliderCastResult result)
        {
            var scaledColliderToCast = colliderToCast;
            var scaledTargetCollider = targetCollider;
            ScaleStretchCollider(ref scaledColliderToCast, castStart.scale,       castStart.stretch);
            ScaleStretchCollider(ref scaledTargetCollider, targetTransform.scale, targetTransform.stretch);
            return ColliderColliderDispatch.ColliderCast(in scaledColliderToCast,
                                                         new RigidTransform(castStart.rotation, castStart.position),
                                                         castEnd,
                                                         in scaledTargetCollider,
                                                         new RigidTransform(targetTransform.rotation, targetTransform.position),
                                                         out result);
        }
        #endregion

        #region Collider vs Layer
        /// <summary>
        /// Sweeps a collider beginning at castStart throught to castEnd and checks if the collider hits
        /// any other collider in the CollisionLayer. If so, results of the hit in which the casted collider
        /// traveled the least is reported. It is assumed that rotation, scale, and stretch remain constant
        /// throughout the operation. Hits where the casted collider starts already overlapping a target in
        /// the CollisionLayer are ignored.
        /// </summary>
        /// <param name="colliderToCast">The casted collider that should be swept through space</param>
        /// <param name="castStart">The transform of the casted collider at the start of the cast</param>
        /// <param name="castEnd">The position of the casted collider at the end of the cast range</param>
        /// <param name="layer">The CollisionLayer containing 0 or more colliders the casted collider should
        /// be tested against</param>
        /// <param name="result">The resulting information about the least-swept hit if there is one. If there
        /// is no hit, the contents of the result are undefined.</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>True if a hit was found, false otherwise</returns>
        public static bool ColliderCast(in Collider colliderToCast,
                                        in TransformQvvs castStart,
                                        float3 castEnd,
                                        in CollisionLayer layer,
                                        out ColliderCastResult result,
                                        out LayerBodyInfo layerBodyInfo)
        {
            var scaledCastStart      = new RigidTransform(castStart.rotation, castStart.position);
            var scaledColliderToCast = colliderToCast;
            ScaleStretchCollider(ref scaledColliderToCast, castStart.scale, castStart.stretch);

            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.ColliderCastClosestImmediateProcessor(in scaledColliderToCast, in scaledCastStart, castEnd, ref result, ref layerBodyInfo);
            FindObjects(AabbFrom(in scaledColliderToCast, in scaledCastStart, castEnd), in layer, in processor).RunImmediate();
            var hit                         = result.subColliderIndexOnTarget >= 0;
            result.subColliderIndexOnTarget = math.max(result.subColliderIndexOnTarget, 0);
            return hit;
        }

        /// <summary>
        /// Sweeps a collider beginning at castStart throught to castEnd and checks if the collider hits
        /// any other collider in any of the CollisionLayers. If so, results of the hit in which the casted collider
        /// traveled the least is reported. It is assumed that rotation, scale, and stretch remain constant
        /// throughout the operation. Hits where the casted collider starts already overlapping a target in
        /// a CollisionLayer are ignored.
        /// </summary>
        /// <param name="colliderToCast">The casted collider that should be swept through space</param>
        /// <param name="castStart">The transform of the casted collider at the start of the cast</param>
        /// <param name="castEnd">The position of the casted collider at the end of the cast range</param>
        /// <param name="layers">The CollisionLayers containing 0 or more colliders the casted collider should
        /// be tested against</param>
        /// <param name="result">The resulting information about the least-swept hit if there is one. If there
        /// is no hit, the contents of the result are undefined.</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in which CollisionLayer was hit</param>
        /// <returns>True if a hit was found, false otherwise</returns>
        public static bool ColliderCast(in Collider colliderToCast,
                                        in TransformQvvs castStart,
                                        float3 castEnd,
                                        ReadOnlySpan<CollisionLayer> layers,
                                        out ColliderCastResult result,
                                        out LayerBodyInfo layerBodyInfo)
        {
            bool found    = false;
            result        = default;
            layerBodyInfo = default;
            int i         = 0;
            foreach (var layer in layers)
            {
                if (ColliderCast(colliderToCast, castStart, castEnd, in layer, out var newResult, out var newLayerBodyInfo))
                {
                    if (!found || newResult.distance < result.distance)
                    {
                        found                    = true;
                        result                   = newResult;
                        layerBodyInfo            = newLayerBodyInfo;
                        layerBodyInfo.layerIndex = i;
                        castEnd                  = result.distance * math.normalize(castEnd - castStart.position) + castStart.position;
                    }
                }
                i++;
            }
            return found;
        }

        /// <summary>
        /// Sweeps a collider beginning at castStart throught to castEnd and checks if the collider hits
        /// any other collider in the CollisionLayer. If so, results of the first hit the algorithm finds
        /// is reported. It is assumed that rotation, scale, and stretch remain constant throughout the operation.
        /// Hits where the casted collider starts already overlapping a target in the CollisionLayer are ignored.
        /// </summary>
        /// <param name="colliderToCast">The casted collider that should be swept through space</param>
        /// <param name="castStart">The transform of the casted collider at the start of the cast</param>
        /// <param name="castEnd">The position of the casted collider at the end of the cast range</param>
        /// <param name="layer">The CollisionLayer containing 0 or more colliders the casted collider should
        /// be tested against</param>
        /// <param name="result">The resulting information about the hit if there is one. If there is no hit,
        /// the contents of the result are undefined.</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>True if a hit was found, false otherwise</returns>
        public static bool ColliderCastAny(Collider colliderToCast,
                                           in TransformQvvs castStart,
                                           float3 castEnd,
                                           in CollisionLayer layer,
                                           out ColliderCastResult result,
                                           out LayerBodyInfo layerBodyInfo)
        {
            var scaledCastStart      = new RigidTransform(castStart.rotation, castStart.position);
            var scaledColliderToCast = colliderToCast;
            ScaleStretchCollider(ref scaledColliderToCast, castStart.scale, castStart.stretch);

            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.ColliderCastAnyImmediateProcessor(in scaledColliderToCast, in scaledCastStart, castEnd, ref result, ref layerBodyInfo);
            FindObjects(AabbFrom(in scaledColliderToCast, in scaledCastStart, castEnd), in layer, in processor).RunImmediate();
            var hit                         = result.subColliderIndexOnTarget >= 0;
            result.subColliderIndexOnTarget = math.max(result.subColliderIndexOnTarget, 0);
            return hit;
        }

        /// <summary>
        /// Sweeps a collider beginning at castStart throught to castEnd and checks if the collider hits
        /// any other collider in any of the CollisionLayers. If so, results of the first hit the algorithm finds
        /// is reported. It is assumed that rotation, scale, and stretch remain constant throughout the operation.
        /// Hits where the casted collider starts already overlapping a target in a CollisionLayer are ignored.
        /// </summary>
        /// <param name="colliderToCast">The casted collider that should be swept through space</param>
        /// <param name="castStart">The transform of the casted collider at the start of the cast</param>
        /// <param name="castEnd">The position of the casted collider at the end of the cast range</param>
        /// <param name="layers">The CollisionLayers containing 0 or more colliders the casted collider should
        /// be tested against</param>
        /// <param name="result">The resulting information about the hit if there is one. If there is no hit,
        /// the contents of the result are undefined.</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in which CollisionLayer was hit</param>
        /// <returns>True if a hit was found, false otherwise</returns>
        public static bool ColliderCastAny(Collider colliderToCast,
                                           in TransformQvvs castStart,
                                           float3 castEnd,
                                           ReadOnlySpan<CollisionLayer> layers,
                                           out ColliderCastResult result,
                                           out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            int i         = 0;
            foreach (var layer in layers)
            {
                if (ColliderCastAny(colliderToCast, castStart, castEnd, in layer, out result, out layerBodyInfo))
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
}

