﻿using System.Collections.Generic;
using Latios.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

using UnityCollider = UnityEngine.Collider;

namespace Latios.Psyshock.Authoring
{
    public enum AuthoringColliderTypes
    {
        None,
        Compound
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Physics (Psyshock)/Custom Collider")]
    public class ColliderAuthoring : MonoBehaviour
    {
        private void OnEnable()
        {
            // Exists to make this enableable in inspector
        }

        public AuthoringColliderTypes colliderType = AuthoringColliderTypes.None;

        //Compound Data
        public bool generateFromChildren;

        public List<UnityCollider> colliders;
    }

    [TemporaryBakingType]
    public struct ColliderBakerWorker : ISmartBakeItem<ColliderAuthoring>
    {
        SmartBlobberHandle<CompoundColliderBlob> m_handle;

        public bool Bake(ColliderAuthoring authoring, IBaker baker)
        {
            if (!authoring.enabled)
                return false;
            if (authoring.colliderType == AuthoringColliderTypes.None)
                return false;

            var smartBaker = baker as ColliderBaker;
            smartBaker.m_compoundList.Clear();
            if (authoring.generateFromChildren)
            {
                smartBaker.GetComponentsInChildren(smartBaker.m_compoundList);
                if (smartBaker.m_compoundList.Count == 0)
                    return false;
            }
            else
            {
                if (authoring.colliders == null)
                    return false;

                foreach (var colliderGO in authoring.colliders)
                {
                    smartBaker.m_colliderCache.Clear();
                    smartBaker.GetComponents(colliderGO, smartBaker.m_colliderCache);
                    smartBaker.m_compoundList.AddRange(smartBaker.m_colliderCache);
                }
            }
            var transform = smartBaker.GetComponent<Transform>();
            var entity    = baker.GetEntity(TransformUsageFlags.Renderable);
            baker.AddComponent<Collider>(entity);
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            m_handle = smartBaker.RequestCreateBlobAsset(smartBaker.m_compoundList, transform);
            return true;
#else
            UnityEngine.Debug.LogWarning("The current transform system in use does not support baking compound colliders.");
            return false;
#endif
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            Collider collider = new CompoundCollider
            {
                compoundColliderBlob = m_handle.Resolve(entityManager),
                scale                = 1f,
                stretch              = 1f,
                stretchMode          = CompoundCollider.StretchMode.RotateStretchLocally
            };
            entityManager.SetComponentData(entity, collider);
        }
    }

    public class ColliderBaker : SmartBaker<ColliderAuthoring, ColliderBakerWorker>
    {
        internal List<UnityCollider> m_colliderCache = new List<UnityCollider>();
        internal List<UnityCollider> m_compoundList  = new List<UnityCollider>();
    }
}

