using System;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;

// Todo: Merge Enabled bits

namespace Latios.Systems
{
    /// <summary>
    /// A system which combines entities with the BlackboardEntityData components into the worldBlackboardEntity and sceneBlackboardEntity.
    /// The entities with BlackboardEntityData are destroyed.
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosWorldSyncGroup), OrderFirst = true)]
    //[UpdateBefore(typeof(ManagedComponentsReactiveSystemGroup))] // Constrained in ManagedComponentsReactiveSystemGroup.
    public partial class MergeBlackboardsSystem : SubSystem
    {
        private EntityDataCopyKit m_copyKit;
        private EntityQuery       m_query;

        private NativeList<ComponentType> m_sceneTypes;

        protected override void OnCreate()
        {
            m_copyKit    = new EntityDataCopyKit(EntityManager);
            m_sceneTypes = new NativeList<ComponentType>(Allocator.Persistent);

            foreach (var type in TypeManager.AllTypes)
            {
                if (type.Type != null && type.Type.Namespace != null)
                    if (type.Type.Namespace == "Unity.Entities")
                        m_sceneTypes.Add(ComponentType.FromTypeIndex(type.TypeIndex));
            }
        }

        protected override void OnDestroy()
        {
            m_sceneTypes.Dispose();
        }

        protected override void OnUpdate()
        {
            Entities.WithStoreEntityQueryInField(ref m_query).WithStructuralChanges().ForEach((Entity entity, ref BlackboardEntityData blackboardEntityData) =>
            {
                var types        = EntityManager.GetComponentTypes(entity, Unity.Collections.Allocator.TempJob);
                var targetEntity = blackboardEntityData.blackboardScope == BlackboardScope.World ? worldBlackboardEntity : sceneBlackboardEntity;
                var sceneTypes   = m_sceneTypes;

                ComponentType errorType = default;
                bool          error     = false;
                foreach (var type in types)
                {
                    if (type.TypeIndex == ComponentType.ReadWrite<BlackboardEntityData>().TypeIndex)
                        continue;
                    bool skip = false;
                    for (int i = 0; i < sceneTypes.Length; i++)
                    {
                        if (sceneTypes[i].TypeIndex == type.TypeIndex)
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip)
                        continue;

                    if (blackboardEntityData.mergeMethod == MergeMethod.Overwrite || !targetEntity.HasComponent(type))
                        MoveComponent(entity, targetEntity, type);
                    else if (blackboardEntityData.mergeMethod == MergeMethod.ErrorOnConflict)
                    {
                        errorType = type;
                        error     = true;
                    }
                }
                types.Dispose();
                if (error)
                {
                    throw new InvalidOperationException(
                        $"Entity {entity} could not copy component {errorType.GetManagedType()} onto {(blackboardEntityData.blackboardScope == BlackboardScope.World ? "world" : "scene")} entity because the component already exists and the MergeMethod was set to ErrorOnConflict.");
                }
            }).Run();
            EntityManager.DestroyEntity(m_query);
        }

        void MoveComponent(Entity srcEntity, Entity dstEntity, ComponentType type)
        {
            if (type.IsManagedComponent)
            {
                EntityManager.MoveManagedComponent(srcEntity, dstEntity, type);
            }
            else
            {
                m_copyKit.CopyData(srcEntity, dstEntity, type);
            }
        }

        /*private EntityQuery group;

           protected override void OnCreate()
           {
            group = GetEntityQuery(typeof(BlackboardEntityData));
           }

           protected override void OnUpdate()
           {
            int length = group.CalculateEntityCount();
            length     = group.CalculateEntityCount();
            if (length <= 1)
                return;
            var array = group.ToEntityArray(Unity.Collections.Allocator.TempJob);

            UnityEngine.Debug.Log("Merging singletons, found entities to merge: " + array.Length);
            for (int a = 0; a < array.Length; a++)
            {
                if (array[a] == SingletonEntity)
                    continue;
                Entity   singletonEntity = SingletonEntity;
                Entity   entity          = array[a];
                object[] entityArg       = new object[] { entity };
                var      types           = EntityManager.GetComponentTypes(entity);
                for (int i = 0; i < types.Length; i++)
                {
                    if (EntityManager.HasComponent(SingletonEntity, types[i]))
                        continue;

                    var type = types[i].GetManagedType();
                    if (typeof(IComponentData).IsAssignableFrom(type) || typeof(ISystemStateComponentData).IsAssignableFrom(type))
                    {
                        var     getter  = typeof(EntityManager).GetMethod("GetComponentData");
                        var     gGetter = getter.MakeGenericMethod(type);
                        var     setter  = typeof(EntityManager).GetMethod("AddComponentData");
                        var     gSetter = setter.MakeGenericMethod(type);
                        dynamic icd     = gGetter.Invoke(EntityManager, entityArg);
                        gSetter.Invoke(EntityManager, new object[] { singletonEntity, icd });
                    }
                    else if (typeof(ISharedComponentData).IsAssignableFrom(type) || typeof(ISystemStateSharedComponentData).IsAssignableFrom(type))
                    {
                        var     getter  = typeof(InitMergeSingletonsSystem).GetMethod("GetSharedComponentData");
                        var     gGetter = getter.MakeGenericMethod(type);
                        var     setter  = typeof(InitMergeSingletonsSystem).GetMethod("AddOrSetSharedComponentData");
                        var     gSetter = setter.MakeGenericMethod(type);
                        dynamic iscd    = gGetter.Invoke(EntityManager, entityArg);
                        gSetter.Invoke(EntityManager, new object[] { singletonEntity, iscd });
                    }
                }
                EntityManager.DestroyEntity(entity);
            }
            array.Dispose();
           }*/
    }
}

