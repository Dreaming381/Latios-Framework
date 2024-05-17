using System;
using System.Diagnostics;
using Unity.Collections;

namespace Unity.Entities.Exposed
{
    public static unsafe class EntityManagerExposed
    {
        // Todo: Make a Burst-Compatible version.
        public static void CopySharedComponent(this EntityManager entityManager, Entity src, Entity dst, ComponentType componentType)
        {
            CheckComponentTypeIsSharedComponent(componentType);

            entityManager.AddComponent(dst, componentType);
            var handle = entityManager.GetDynamicSharedComponentTypeHandle(componentType);
            var chunk  = entityManager.GetStorageInfo(src);
            var index  = chunk.Chunk.GetSharedComponentIndex(ref handle);
            var box    = index != 0 ? chunk.Chunk.GetSharedComponentDataBoxed(ref handle, entityManager) : null;
            entityManager.SetSharedComponentDataBoxedDefaultMustBeNull(dst, componentType.TypeIndex, box);
        }

        public static void MoveManagedComponent(this EntityManager entityManager, Entity src, Entity dst, ComponentType componentType)
        {
            var access  = entityManager.GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->MoveComponentObjectDuringStructuralChange(src, dst, componentType);
            access->EndStructuralChanges(ref changes);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckComponentTypeIsSharedComponent(ComponentType type)
        {
            if (!type.IsSharedComponent)
                throw new ArgumentException($"Attempted to call EntityManager.CopySharedComponent on {type} which is not an ISharedComponentData");
        }

        // Todo: Find a better home
        public static int InstanceID<T>(this UnityObjectRef<T> unityObjectRef) where T : UnityEngine.Object => unityObjectRef.Id.instanceId;
    }
}

