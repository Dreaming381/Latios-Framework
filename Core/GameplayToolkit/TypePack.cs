using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    public struct TypePack<T>
    {
        public static implicit operator FixedList128Bytes<ComponentType>(TypePack<T> pack)
        {
            return new FixedList128Bytes<ComponentType>()
            {
                ComponentType.ReadOnly<T>()
            };
        }
        public static implicit operator ComponentTypeSet(TypePack<T> pack) => new ComponentTypeSet(ComponentType.ReadOnly<T>());
    }

    public struct TypePack<T0, T1>
    {
        public static implicit operator FixedList128Bytes<ComponentType>(TypePack<T0, T1> pack)
        {
            return new FixedList128Bytes<ComponentType>()
            {
                ComponentType.ReadOnly<T0>(),
                ComponentType.ReadOnly<T1>()
            };
        }
        public static implicit operator ComponentTypeSet(TypePack<T0, T1> pack)
        {
            return new ComponentTypeSet(ComponentType.ReadOnly<T0>(), ComponentType.ReadOnly<T1>());
        }
    }

    public struct TypePack<T0, T1, T2>
    {
        public static implicit operator FixedList128Bytes<ComponentType>(TypePack<T0, T1, T2> pack)
        {
            return new FixedList128Bytes<ComponentType>()
            {
                ComponentType.ReadOnly<T0>(),
                ComponentType.ReadOnly<T1>(),
                ComponentType.ReadOnly<T2>()
            };
        }
        public static implicit operator ComponentTypeSet(TypePack<T0, T1, T2> pack)
        {
            return new ComponentTypeSet(ComponentType.ReadOnly<T0>(), 
                ComponentType.ReadOnly<T1>(),
                ComponentType.ReadOnly<T2>());
        }
    }

    public struct TypePack<T0, T1, T2, T3>
    {
        public static implicit operator FixedList128Bytes<ComponentType>(TypePack<T0, T1, T2, T3> pack)
        {
            return new FixedList128Bytes<ComponentType>()
            {
                ComponentType.ReadOnly<T0>(),
                ComponentType.ReadOnly<T1>(),
                ComponentType.ReadOnly<T2>(),
                ComponentType.ReadOnly<T3>()
            };
        }
        public static implicit operator ComponentTypeSet(TypePack<T0, T1, T2, T3> pack)
        {
            return new ComponentTypeSet(ComponentType.ReadOnly<T0>(),
                ComponentType.ReadOnly<T1>(),
                ComponentType.ReadOnly<T2>(),
                ComponentType.ReadOnly<T3>());
        }
    }

    public struct TypePack<T0, T1, T2, T3, T4>
    {
        public static implicit operator FixedList128Bytes<ComponentType>(TypePack<T0, T1, T2, T3, T4> pack)
        {
            return new FixedList128Bytes<ComponentType>()
            {
                ComponentType.ReadOnly<T0>(),
                ComponentType.ReadOnly<T1>(),
                ComponentType.ReadOnly<T2>(),
                ComponentType.ReadOnly<T3>(),
                ComponentType.ReadOnly<T4>()
            };
        }
        public static implicit operator ComponentTypeSet(TypePack<T0, T1, T2, T3, T4> pack)
        {
            return new ComponentTypeSet(ComponentType.ReadOnly<T0>(),
                ComponentType.ReadOnly<T1>(),
                ComponentType.ReadOnly<T2>(),
                ComponentType.ReadOnly<T3>(),
                ComponentType.ReadOnly<T4>());
        }
    }

    public static class AddComponentExtensions
    {
        public static void AddComponents<T0, T1>(this EntityManager entityManager, Entity entity, in T0 c0, in T1 c1)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
        {
            entityManager.AddComponent(entity, new TypePack<T0, T1>());
            entityManager.SetComponentData(entity, c0);
            entityManager.SetComponentData(entity, c1);
        }

        public static void AddComponents<T0, T1, T2>(this EntityManager entityManager, Entity entity, in T0 c0, in T1 c1, in T2 c2)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
        {
            entityManager.AddComponent(entity, new TypePack<T0, T1, T2>());
            entityManager.SetComponentData(entity, c0);
            entityManager.SetComponentData(entity, c1);
            entityManager.SetComponentData(entity, c2);
        }

        public static void AddComponents<T0, T1, T2, T3>(this EntityManager entityManager, Entity entity, in T0 c0, in T1 c1, in T2 c2, in T3 c3)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
        {
            entityManager.AddComponent(entity, new TypePack<T0, T1, T2, T3>());
            entityManager.SetComponentData(entity, c0);
            entityManager.SetComponentData(entity, c1);
            entityManager.SetComponentData(entity, c2);
            entityManager.SetComponentData(entity, c3);
        }

        public static void AddComponents<T0, T1, T2, T3, T4>(this EntityManager entityManager, Entity entity, in T0 c0, in T1 c1, in T2 c2, in T3 c3, in T4 c4)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
            where T4 : unmanaged, IComponentData
        {
            entityManager.AddComponent(entity, new TypePack<T0, T1, T2, T3, T4>());
            entityManager.SetComponentData(entity, c0);
            entityManager.SetComponentData(entity, c1);
            entityManager.SetComponentData(entity, c2);
            entityManager.SetComponentData(entity, c3);
            entityManager.SetComponentData(entity, c4);
        }

        public static void AddComponents<T0, T1>(this EntityCommandBuffer entityCommandBuffer, Entity entity, in T0 c0, in T1 c1)
    where T0 : unmanaged, IComponentData
    where T1 : unmanaged, IComponentData
        {
            entityCommandBuffer.AddComponent(entity, new TypePack<T0, T1>());
            entityCommandBuffer.SetComponent(entity, c0);
            entityCommandBuffer.SetComponent(entity, c1);
        }

        public static void AddComponents<T0, T1, T2>(this EntityCommandBuffer entityCommandBuffer, Entity entity, in T0 c0, in T1 c1, in T2 c2)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
        {
            entityCommandBuffer.AddComponent(entity, new TypePack<T0, T1, T2>());
            entityCommandBuffer.SetComponent(entity, c0);
            entityCommandBuffer.SetComponent(entity, c1);
            entityCommandBuffer.SetComponent(entity, c2);
        }

        public static void AddComponents<T0, T1, T2, T3>(this EntityCommandBuffer entityCommandBuffer, Entity entity, in T0 c0, in T1 c1, in T2 c2, in T3 c3)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
        {
            entityCommandBuffer.AddComponent(entity, new TypePack<T0, T1, T2, T3>());
            entityCommandBuffer.SetComponent(entity, c0);
            entityCommandBuffer.SetComponent(entity, c1);
            entityCommandBuffer.SetComponent(entity, c2);
            entityCommandBuffer.SetComponent(entity, c3);
        }

        public static void AddComponents<T0, T1, T2, T3, T4>(this EntityCommandBuffer entityCommandBuffer, Entity entity, in T0 c0, in T1 c1, in T2 c2, in T3 c3, in T4 c4)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
            where T4 : unmanaged, IComponentData
        {
            entityCommandBuffer.AddComponent(entity, new TypePack<T0, T1, T2, T3, T4>());
            entityCommandBuffer.SetComponent(entity, c0);
            entityCommandBuffer.SetComponent(entity, c1);
            entityCommandBuffer.SetComponent(entity, c2);
            entityCommandBuffer.SetComponent(entity, c3);
            entityCommandBuffer.SetComponent(entity, c4);
        }

        public static void AddComponents<T0, T1>(this EntityCommandBuffer.ParallelWriter entityCommandBuffer, int sortKey, Entity entity, in T0 c0, in T1 c1)
where T0 : unmanaged, IComponentData
where T1 : unmanaged, IComponentData
        {
            entityCommandBuffer.AddComponent(sortKey, entity, new TypePack<T0, T1>());
            entityCommandBuffer.SetComponent(sortKey, entity, c0);
            entityCommandBuffer.SetComponent(sortKey, entity, c1);
        }

        public static void AddComponents<T0, T1, T2>(this EntityCommandBuffer.ParallelWriter entityCommandBuffer, int sortKey, Entity entity, in T0 c0, in T1 c1, in T2 c2)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
        {
            entityCommandBuffer.AddComponent(sortKey, entity, new TypePack<T0, T1, T2>());
            entityCommandBuffer.SetComponent(sortKey, entity, c0);
            entityCommandBuffer.SetComponent(sortKey, entity, c1);
            entityCommandBuffer.SetComponent(sortKey, entity, c2);
        }

        public static void AddComponents<T0, T1, T2, T3>(this EntityCommandBuffer.ParallelWriter entityCommandBuffer, int sortKey, Entity entity, in T0 c0, in T1 c1, in T2 c2, in T3 c3)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
        {
            entityCommandBuffer.AddComponent(sortKey, entity, new TypePack<T0, T1, T2, T3>());
            entityCommandBuffer.SetComponent(sortKey, entity, c0);
            entityCommandBuffer.SetComponent(sortKey, entity, c1);
            entityCommandBuffer.SetComponent(sortKey, entity, c2);
            entityCommandBuffer.SetComponent(sortKey, entity, c3);
        }

        public static void AddComponents<T0, T1, T2, T3, T4>(this EntityCommandBuffer.ParallelWriter entityCommandBuffer, int sortKey, Entity entity, in T0 c0, in T1 c1, in T2 c2, in T3 c3, in T4 c4)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
            where T4 : unmanaged, IComponentData
        {
            entityCommandBuffer.AddComponent(sortKey, entity, new TypePack<T0, T1, T2, T3, T4>());
            entityCommandBuffer.SetComponent(sortKey, entity, c0);
            entityCommandBuffer.SetComponent(sortKey, entity, c1);
            entityCommandBuffer.SetComponent(sortKey, entity, c2);
            entityCommandBuffer.SetComponent(sortKey, entity, c3);
            entityCommandBuffer.SetComponent(sortKey, entity, c4);
        }

        public static void AddComponents<T0, T1>(this BlackboardEntity blackboardEntity, in T0 c0, in T1 c1)
    where T0 : unmanaged, IComponentData
    where T1 : unmanaged, IComponentData
        {
            blackboardEntity.em.AddComponent(blackboardEntity, new TypePack<T0, T1>());
            blackboardEntity.em.SetComponentData(blackboardEntity, c0);
            blackboardEntity.em.SetComponentData(blackboardEntity, c1);
        }

        public static void AddComponents<T0, T1, T2>(this BlackboardEntity blackboardEntity, in T0 c0, in T1 c1, in T2 c2)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
        {
            blackboardEntity.em.AddComponent(blackboardEntity, new TypePack<T0, T1, T2>());
            blackboardEntity.em.SetComponentData(blackboardEntity, c0);
            blackboardEntity.em.SetComponentData(blackboardEntity, c1);
            blackboardEntity.em.SetComponentData(blackboardEntity, c2);
        }

        public static void AddComponents<T0, T1, T2, T3>(this BlackboardEntity blackboardEntity, in T0 c0, in T1 c1, in T2 c2, in T3 c3)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
        {
            blackboardEntity.em.AddComponent(blackboardEntity, new TypePack<T0, T1, T2, T3>());
            blackboardEntity.em.SetComponentData(blackboardEntity, c0);
            blackboardEntity.em.SetComponentData(blackboardEntity, c1);
            blackboardEntity.em.SetComponentData(blackboardEntity, c2);
            blackboardEntity.em.SetComponentData(blackboardEntity, c3);
        }

        public static void AddComponents<T0, T1, T2, T3, T4>(this BlackboardEntity blackboardEntity, in T0 c0, in T1 c1, in T2 c2, in T3 c3, in T4 c4)
            where T0 : unmanaged, IComponentData
            where T1 : unmanaged, IComponentData
            where T2 : unmanaged, IComponentData
            where T3 : unmanaged, IComponentData
            where T4 : unmanaged, IComponentData
        {
            blackboardEntity.em.AddComponent(blackboardEntity, new TypePack<T0, T1, T2, T3, T4>());
            blackboardEntity.em.SetComponentData(blackboardEntity, c0);
            blackboardEntity.em.SetComponentData(blackboardEntity, c1);
            blackboardEntity.em.SetComponentData(blackboardEntity, c2);
            blackboardEntity.em.SetComponentData(blackboardEntity, c3);
            blackboardEntity.em.SetComponentData(blackboardEntity, c4);
        }
    }
}