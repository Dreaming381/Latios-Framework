using System;
using System.Reflection;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    [AlwaysSynchronizeSystem]
    public class MergeGlobalsSystem : JobSubSystem
    {
        private EntityDataCopyKit m_copyKit;

        protected override void OnCreate()
        {
            m_copyKit = new EntityDataCopyKit(EntityManager);
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            inputDeps.Complete();
            Entities.WithStructuralChanges().ForEach((Entity entity, ref GlobalEntityData globalEntityData) =>
            {
                var types        = EntityManager.GetComponentTypes(entity, Unity.Collections.Allocator.TempJob);
                var targetEntity = globalEntityData.globalScope == GlobalScope.World ? worldGlobalEntity : sceneGlobalEntity;

                ComponentType errorType = default;
                bool          error     = false;
                foreach (var type in types)
                {
                    if (globalEntityData.mergeMethod == MergeMethod.Overwrite || !targetEntity.HasComponent(type))
                        m_copyKit.CopyData(entity, targetEntity, type);
                    else if (globalEntityData.mergeMethod == MergeMethod.ErrorOnConflict)
                    {
                        errorType = type;
                        error     = true;
                    }
                }
                types.Dispose();
                if (error)
                {
                    throw new InvalidOperationException(
                        $"Entity {entity} could not copy component {errorType.GetManagedType()} onto {(globalEntityData.globalScope == GlobalScope.World ? "world" : "scene")} entity because the component already exists and the MergeMethod was set to ErrorOnConflict.");
                }
            }).Run();
            return inputDeps;
        }

        /*private EntityQuery group;

           protected override void OnCreate()
           {
            group = GetEntityQuery(typeof(GlobalEntityData));
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

