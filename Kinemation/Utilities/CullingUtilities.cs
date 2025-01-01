using System;
using Latios.Psyshock;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

using FrustumPlanes = Unity.Rendering.FrustumPlanes;

namespace Latios.Kinemation
{
    public static class CullingUtilities
    {
        /// <summary>
        /// Add these Components to any bone you want to participate in culling prior to KinemationRenderSyncPointSuperSystem
        /// or a custom-placed SkeletonMeshBindingReactiveSystem prior to rendering.
        /// </summary>
        public static ComponentTypeSet GetBoneCullingComponentTypes()
        {
            var boneTypes = new FixedList128Bytes<ComponentType>();
            boneTypes.Add(ComponentType.ReadWrite<BoneOwningSkeletonReference>());
            boneTypes.Add(ComponentType.ReadWrite<BoneIndex>());
            boneTypes.Add(ComponentType.ReadWrite<BoneCullingIndex>());
            boneTypes.Add(ComponentType.ReadWrite<BoneBounds>());
            boneTypes.Add(ComponentType.ReadWrite<BoneWorldBounds>());
            boneTypes.Add(ComponentType.ChunkComponent<ChunkBoneWorldBounds>());
            return new ComponentTypeSet(boneTypes);
        }

        public static NativeArray<FrustumPlanes.PlanePacket4> BuildSOAPlanePackets(DynamicBuffer<CullingPlane> cullingPlanes, ref WorldUnmanaged world)
        {
            return BuildSOAPlanePackets(cullingPlanes.Reinterpret<UnityEngine.Plane>().AsNativeArray(), ref world);
        }

        public static NativeArray<FrustumPlanes.PlanePacket4> BuildSOAPlanePackets(NativeArray<UnityEngine.Plane> cullingPlanes, ref WorldUnmanaged world)
        {
            int cullingPlaneCount = cullingPlanes.Length;
            int packetCount       = (cullingPlaneCount + 3) >> 2;
            var planes            = world.UpdateAllocator.AllocateNativeArray<FrustumPlanes.PlanePacket4>(packetCount);

            for (int i = 0; i < cullingPlaneCount; i++)
            {
                var p              = planes[i >> 2];
                p.Xs[i & 3]        = cullingPlanes[i].normal.x;
                p.Ys[i & 3]        = cullingPlanes[i].normal.y;
                p.Zs[i & 3]        = cullingPlanes[i].normal.z;
                p.Distances[i & 3] = cullingPlanes[i].distance;
                planes[i >> 2]     = p;
            }

            // Populate the remaining planes with values that are always "in"
            for (int i = cullingPlaneCount; i < 4 * packetCount; ++i)
            {
                var p       = planes[i >> 2];
                p.Xs[i & 3] = 1.0f;
                p.Ys[i & 3] = 0.0f;
                p.Zs[i & 3] = 0.0f;

                // This value was before hardcoded to 32786.0f.
                // It was causing the culling system to discard the rendering of entities having a X coordinate approximately less than -32786.
                // We could not find anything relying on this number, so the value has been increased to 1 billion
                p.Distances[i & 3] = 1e9f;

                planes[i >> 2] = p;
            }

            return planes;
        }

        /// <summary>
        /// Sets the bounds of the RenderBounds using a Psyshock Aabb
        /// </summary>
        public static void Set(this ref RenderBounds bounds, Aabb aabb)
        {
            Physics.GetCenterExtents(aabb, out var c, out var e);
            bounds.Value = new AABB { Center = c, Extents = e };
        }

        /// <summary>
        /// Sets the bounds of the RenderBounds using a pair of min and max extents.
        /// </summary>
        public static void SetMinMax(this ref RenderBounds bounds, float3 min, float3 max)
        {
            bounds.Set(new Aabb(min, max));
        }

        /// <summary>
        /// Sets the bounds of the RenderBounds using a center and extents from the center
        /// </summary>
        /// <param name="bounds"></param>
        /// <param name="center"></param>
        /// <param name="extents"></param>
        public static void SetCenterExtents(this ref RenderBounds bounds, float3 center, float3 extents)
        {
            bounds.Value = new AABB { Center = center, Extents = extents };
        }
    }
}

