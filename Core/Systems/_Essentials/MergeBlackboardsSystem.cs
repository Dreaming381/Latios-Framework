using System;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;

namespace Latios.Systems
{
    /// <summary>
    /// A system which combines entities with the BlackboardEntityData components into the worldBlackboardEntity and sceneBlackboardEntity.
    /// The entities with BlackboardEntityData are destroyed.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosWorldSyncGroup), OrderFirst = true)]
    //[UpdateBefore(typeof(ManagedComponentsReactiveSystemGroup))] // Constrained in ManagedComponentsReactiveSystemGroup.
    public partial class MergeBlackboardsSystem : SubSystem
    {
        EntityDataCopyKit m_copyKit;
        EntityQuery       m_query;

        NativeList<ComponentType> m_sceneTypes;

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
            latiosWorld.CreateNewSceneBlackboardEntity();

            if (!m_query.IsEmptyIgnoreFilter)
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

            if (!sceneBlackboardEntity.HasComponent<DispatchedNewSceneTag>())
            {
                latiosWorld.DispatchNewSceneCallbacks();
                sceneBlackboardEntity.AddComponent<DispatchedNewSceneTag>();
            }
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
    }
}

