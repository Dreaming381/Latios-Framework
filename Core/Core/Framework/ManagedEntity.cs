using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    public struct ManagedEntity
    {
        private Entity        entity;
        private EntityManager em;

        public ManagedEntity(Entity singleton, EntityManager entityManager)
        {
            entity = singleton;
            em     = entityManager;
        }

        public static implicit operator Entity(ManagedEntity singleton)
        {
            return singleton.entity;
        }

        public void AddComponentData<T>(T data) where T : struct, IComponentData
        {
            em.AddComponentData(entity, data);
        }

        public void SetComponentData<T>(T data) where T : struct, IComponentData
        {
            em.SetComponentData(entity, data);
        }

        public void AddOrSetComponentData<T>(T data) where T : struct, IComponentData
        {
            if (em.HasComponent<T>(entity))
            {
                SetComponentData(data);
            }
            else
            {
                AddComponentData(data);
            }
        }

        public T GetComponentData<T>() where T : struct, IComponentData
        {
            return em.GetComponentData<T>(entity);
        }

        public bool HasComponentData<T>() where T : struct, IComponentData
        {
            return em.HasComponent<T>(entity);
        }

        public bool HasComponent(ComponentType componentType)
        {
            return em.HasComponent(entity, componentType);
        }

        public void AddSharedComponentData<T>(T data) where T : struct, ISharedComponentData
        {
            em.AddSharedComponentData(entity, data);
        }

        public void SetSharedComponentData<T>(T data) where T : struct, ISharedComponentData
        {
            em.SetSharedComponentData(entity, data);
        }

        public void AddOrSetSharedComponentData<T>(T data) where T : struct, ISharedComponentData
        {
            em.SetSharedComponentData(entity, data);
        }

        public T GetSharedComponentData<T>() where T : struct, ISharedComponentData
        {
            return em.GetSharedComponentData<T>(entity);
        }

        public void AddCollectionComponent<T>(T value) where T : struct, ICollectionComponent
        {
            em.AddCollectionComponent<T>(entity, value);
        }

        public T GetCollectionComponent<T>(bool readOnly, out JobHandle handle) where T : struct, ICollectionComponent
        {
            return em.GetCollectionComponent<T>(entity, readOnly, out handle);
        }

        public T GetCollectionComponent<T>(bool readOnly = false) where T : struct, ICollectionComponent
        {
            return em.GetCollectionComponent<T>(entity, readOnly);
        }

        public bool HasCollectionComponent<T>() where T : struct, ICollectionComponent
        {
            return em.HasCollectionComponent<T>(entity);
        }

        public void RemoveCollectionComponentAndDispose<T>() where T : struct, ICollectionComponent
        {
            em.RemoveCollectionComponentAndDispose<T>(entity);
        }

        public void SetCollectionComponentAndDisposeOld<T>(T value) where T : struct, ICollectionComponent
        {
            em.SetCollectionComponentAndDisposeOld(entity, value);
        }

        public void UpdateJobDependency<T>(JobHandle handle, bool wasReadOnly) where T : struct, ICollectionComponent
        {
            em.UpdateCollectionComponentDependency<T>(entity, handle, wasReadOnly);
        }
    }
}

