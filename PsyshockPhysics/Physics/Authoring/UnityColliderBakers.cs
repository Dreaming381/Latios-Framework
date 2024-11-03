using System.Collections.Generic;
using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock.Authoring
{
namespace Systems
{
    [DisableAutoCreation]
    public class SphereColliderBaker : Baker<UnityEngine.SphereCollider>
    {
        public override void Bake(UnityEngine.SphereCollider authoring)
        {
            if (!this.ShouldBakeSingleCollider(authoring))
                return;

            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, (Collider) new SphereCollider
            {
                center      = authoring.center,
                radius      = authoring.radius,
                stretchMode = SphereCollider.StretchMode.StretchCenter
            });
        }
    }

    [DisableAutoCreation]
    public class CapsuleColliderBaker : Baker<UnityEngine.CapsuleCollider>
    {
        public override void Bake(UnityEngine.CapsuleCollider authoring)
        {
            if (!this.ShouldBakeSingleCollider(authoring))
                return;

            float3 dir;
            if (authoring.direction == 0)
            {
                dir = new float3(1f, 0f, 0f);
            }
            else if (authoring.direction == 1)
            {
                dir = new float3(0f, 1, 0f);
            }
            else
            {
                dir = new float3(0f, 0f, 1f);
            }
            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, (Collider) new CapsuleCollider
            {
                pointB      = (float3)authoring.center + ((authoring.height / 2f - authoring.radius) * dir),
                pointA      = (float3)authoring.center - ((authoring.height / 2f - authoring.radius) * dir),
                radius      = authoring.radius,
                stretchMode = CapsuleCollider.StretchMode.StretchPoints
            });
        }
    }

    [DisableAutoCreation]
    public class BoxColliderBaker : Baker<UnityEngine.BoxCollider>
    {
        public override void Bake(UnityEngine.BoxCollider authoring)
        {
            if (!this.ShouldBakeSingleCollider(authoring))
                return;

            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, (Collider) new BoxCollider
            {
                center   = authoring.center,
                halfSize = authoring.size / 2f
            });
        }
    }

    [TemporaryBakingType]
    public struct MeshColliderBakeItem : ISmartBakeItem<UnityEngine.MeshCollider>
    {
        SmartBlobberHandle<ConvexColliderBlob>  m_convexHandle;
        SmartBlobberHandle<TriMeshColliderBlob> m_triMeshHandle;
        bool                                    isConvex;

        public bool Bake(UnityEngine.MeshCollider authoring, IBaker baker)
        {
            if (!baker.ShouldBakeSingleCollider(authoring))
                return false;

            var entity = baker.GetEntity(TransformUsageFlags.Renderable);
            baker.AddComponent<Collider>(entity);
            isConvex = authoring.convex;
            if (isConvex)
                m_convexHandle = baker.RequestCreateConvexBlobAsset(authoring.sharedMesh);
            else
                m_triMeshHandle = baker.RequestCreateTriMeshBlobAsset(authoring.sharedMesh);
            return m_convexHandle.IsValid | m_triMeshHandle.IsValid;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            if (isConvex)
            {
                Collider collider = new ConvexCollider
                {
                    convexColliderBlob = m_convexHandle.Resolve(entityManager),
                    scale              = 1f
                };
                entityManager.SetComponentData(entity, collider);
            }
            else
            {
                Collider collider = new TriMeshCollider
                {
                    triMeshColliderBlob = m_triMeshHandle.Resolve(entityManager),
                    scale               = 1f
                };
                entityManager.SetComponentData(entity, collider);
            }
        }
    }

    [DisableAutoCreation]
    public class MeshColliderBaker : SmartBaker<UnityEngine.MeshCollider, MeshColliderBakeItem>
    {
    }

    [TemporaryBakingType]
    public struct CompoundColliderBakerWorker : ISmartBakeItem<UnityEngine.Collider>
    {
        SmartBlobberHandle<CompoundColliderBlob> m_handle;

        public bool Bake(UnityEngine.Collider authoring, IBaker baker)
        {
            var mode = baker.GetMultiColliderBakeMode(authoring, out var colliders);
            if (mode != MultiColliderBakeMode.PrimaryOfMultiCollider)
                return false;

            var entity = baker.GetEntity(TransformUsageFlags.Renderable);
            baker.AddComponent<Collider>(entity);
            var transform = baker.GetComponent<UnityEngine.Transform>();
            m_handle      = baker.RequestCreateBlobAsset(colliders, transform);
            return true;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            Collider collider = new CompoundCollider
            {
                compoundColliderBlob = m_handle.Resolve(entityManager),
                scale                = 1f,
                stretch              = 1f
            };
            entityManager.SetComponentData(entity, collider);
        }
    }

    [BakeDerivedTypes]
    [DisableAutoCreation]
    public class CompoundColliderBaker : SmartBaker<UnityEngine.Collider, CompoundColliderBakerWorker>
    {
    }
}

    /// <summary>
    /// Determines how the collider will be baked using default collider baking
    /// </summary>
    public enum MultiColliderBakeMode
    {
        SingleCollider = 0,
        PrimaryOfMultiCollider = 1,
        Ignore = 2
    }

    public static class UnityColliderBakerUtiltities
    {
        static List<UnityEngine.Collider> s_colliderCache = new List<UnityEngine.Collider>();
        static List<ColliderAuthoring>    s_compoundCache = new List<ColliderAuthoring>();

        /// <summary>
        /// Returns true if this collider is not part of a compound collider
        /// </summary>
        /// <param name="authoring">The collider to evaluate</param>
        /// <returns>True if the collider is not part of a compound collider, false otherwise</returns>
        public static bool ShouldBakeSingleCollider(this IBaker baker, UnityEngine.Collider authoring)
        {
            return Evaluate(baker, authoring, true) == MultiColliderBakeMode.SingleCollider;
        }

        /// <summary>
        /// Determines if this collider is to be baked as a single non-compound collider,
        /// or if it is the primary collider target from which to assemble a compound collider.
        /// You can check if the result is not Ignore to determine if you should add additional
        /// components like tags to it.
        /// </summary>
        /// <param name="authoring">The collider to consider</param>
        /// <param name="compoundColliderListIfPrimary">If the result is PrimaryOfMultiCollider,
        /// then this list will contain the list of colliders to be baked into the compound.
        /// This list gets reused for each call to this method, so do not hold onto it.</param>
        /// <returns></returns>
        public static MultiColliderBakeMode GetMultiColliderBakeMode(this IBaker baker, UnityEngine.Collider authoring,
                                                                     out List<UnityEngine.Collider> compoundColliderListIfPrimary)
        {
            var evaluation                = Evaluate(baker, authoring, false);
            compoundColliderListIfPrimary = evaluation == MultiColliderBakeMode.PrimaryOfMultiCollider ? s_colliderCache : null;
            return evaluation;
        }

        internal static MultiColliderBakeMode Evaluate(IBaker baker, UnityEngine.Collider authoring, bool ignoreMulti)
        {
            // Reminder: Unity does not bake disabled components!
            s_colliderCache.Clear();
            baker.GetComponents(s_colliderCache);
            int enabledCount = 1;
            if (s_colliderCache.Count > 1)
            {
                enabledCount = 0;
                foreach (var c in s_colliderCache)
                    enabledCount += c.enabled ? 1 : 0;
                if (enabledCount > 1 && ignoreMulti)
                    return MultiColliderBakeMode.Ignore;
            }
            s_compoundCache.Clear();
            baker.GetComponentsInParent(s_compoundCache);
            foreach (var compoundAuthoring in s_compoundCache)
            {
                if (compoundAuthoring.colliderType == AuthoringColliderTypes.None)
                    continue;
                if (!compoundAuthoring.enabled)
                    continue;
                if (compoundAuthoring.generateFromChildren)
                    return MultiColliderBakeMode.Ignore;

                foreach (var child in compoundAuthoring.colliders)
                {
                    if (child.gameObject == authoring.gameObject)
                        return MultiColliderBakeMode.Ignore;
                }
            }
            if (enabledCount == 1)
                return MultiColliderBakeMode.SingleCollider;

            // We only want to process once for all colliders. If any collider is modified, added, or removed,
            // dependencies will trigger a rebake for all colliders due to GetComponents(), so it only matters
            // that one is picked for a single bake but which gets picked doesn't matter.
            // But for consistency, we need to acquire other components to perform additional checks.
            bool foundFirst = false;
            for (int i = 0; i < s_colliderCache.Count; i++)
            {
                if (!s_colliderCache[i].enabled)
                {
                    s_colliderCache.RemoveAtSwapBack(i);
                    i--;
                }
                else if (!foundFirst)
                {
                    if (s_colliderCache[i] != authoring)
                        return MultiColliderBakeMode.Ignore;
                    else
                        foundFirst = true;
                }
            }
            return MultiColliderBakeMode.PrimaryOfMultiCollider;
        }
    }
}

