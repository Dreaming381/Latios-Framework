using System;
using Latios.Transforms;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        /// <summary>
        /// Raycasts against the collider using a ray defined by the start and stop points
        /// </summary>
        /// <param name="start">The start of the ray</param>
        /// <param name="end">The end of the ray</param>
        /// <param name="collider">The collider to test against</param>
        /// <param name="transform">The transform of the collider tested</param>
        /// <param name="result">If the ray hits the collider, this is populated with info about the hit,
        /// otherwise its contents are undefined</param>
        /// <returns>True if the ray hit the collider, false otherwise</returns>
        public static bool Raycast(float3 start, float3 end, Collider collider, in TransformQvvs transform, out RaycastResult result)
        {
            return PointRayDispatch.Raycast(new Ray(start, end), in collider, in transform, out result);
        }

        /// <summary>
        /// Raycasts against the collider
        /// </summary>
        /// <param name="ray">The ray to cast against the collider</param>
        /// <param name="collider">The collider to test against</param>
        /// <param name="transform">The transform of the collider tested</param>
        /// <param name="result">If the ray hits the collider, this is populated with info about the hit,
        /// otherwise its contents are undefined</param>
        /// <returns>True if the ray hit the collider, false otherwise</returns>
        public static bool Raycast(in Ray ray, Collider collider, in TransformQvvs transform, out RaycastResult result)
        {
            return PointRayDispatch.Raycast(ray, in collider, in transform, out result);
        }

        /// <summary>
        /// Raycasts against the CollisionLayer using a ray defined by the start and stop points, and returns the closest hit if found
        /// </summary>
        /// <param name="start">The start of the ray</param>
        /// <param name="end">The end of the ray</param>
        /// <param name="layer">The CollisionLayer containing the colliders to test against</param>
        /// <param name="result">If a ray hits a collider in the CollisionLayer, this is populated with info about the closest hit
        /// found as the ray traverses the CollisionLayer, otherwise its contents are undefined</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>True if the ray hit a collider in the CollisionLayer, false otherwise</returns>
        public static bool Raycast(float3 start, float3 end, in CollisionLayer layer, out RaycastResult result, out LayerBodyInfo layerBodyInfo)
        {
            return Raycast(new Ray(start, end), in layer, out result, out layerBodyInfo);
        }

        /// <summary>
        /// Raycasts against the CollisionLayer, and returns the closest hit if found
        /// </summary>
        /// <param name="ray">The ray to cast against the CollisionLayer</param>
        /// <param name="layer">The CollisionLayer containing the colliders to test against</param>
        /// <param name="result">If a ray hits a collider in the CollisionLayer, this is populated with info about the closest hit
        /// found as the ray traverses the CollisionLayer, otherwise its contents are undefined</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>True if the ray hit a collider in the CollisionLayer, false otherwise</returns>
        public static bool Raycast(in Ray ray, in CollisionLayer layer, out RaycastResult result, out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.RaycastClosestImmediateProcessor(ray, ref result, ref layerBodyInfo);
            FindObjects(AabbFrom(ray), in layer, in processor).RunImmediate();
            var hit                 = result.subColliderIndex >= 0;
            result.subColliderIndex = math.max(result.subColliderIndex, 0);
            return hit;
        }

        /// <summary>
        /// Raycasts against the CollisionLayer using a ray defined by the start and stop points, and returns the first hit the algorithm finds, if any
        /// </summary>
        /// <param name="start">The start of the ray</param>
        /// <param name="end">The end of the ray</param>
        /// <param name="layer">The CollisionLayer containing the colliders to test against</param>
        /// <param name="result">If a ray hits a collider in the CollisionLayer, this is populated with info about the first hit
        /// found (which may not necessarily be the closest), otherwise its contents are undefined</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>True if the ray hit a collider in the CollisionLayer, false otherwise</returns>
        public static bool RaycastAny(float3 start, float3 end, in CollisionLayer layer, out RaycastResult result, out LayerBodyInfo layerBodyInfo)
        {
            return RaycastAny(new Ray(start, end), in layer, out result, out layerBodyInfo);
        }

        /// <summary>
        /// Raycasts against the CollisionLayer, and returns the first hit the algorithm finds, if any
        /// </summary>
        /// <param name="ray">The ray to cast against the CollisionLayer</param>
        /// <param name="layer">The CollisionLayer containing the colliders to test against</param>
        /// <param name="result">If a ray hits a collider in the CollisionLayer, this is populated with info about the first hit
        /// found (which may not necessarily be the closest), otherwise its contents are undefined</param>
        /// <param name="layerBodyInfo">Additional info as to which collider in the CollisionLayer was hit</param>
        /// <returns>True if the ray hit a collider in the CollisionLayer, false otherwise</returns>
        public static bool RaycastAny(in Ray ray, in CollisionLayer layer, out RaycastResult result, out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.RaycastAnyImmediateProcessor(ray, ref result, ref layerBodyInfo);
            FindObjects(AabbFrom(ray), in layer, in processor).RunImmediate();
            var hit                 = result.subColliderIndex >= 0;
            result.subColliderIndex = math.max(result.subColliderIndex, 0);
            return hit;
        }
    }
}

