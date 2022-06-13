using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// This system uses PreviousParent in all cases because it is guaranteed to be updated
// (ParentSystem just ran) and it is updated when the entity is enabled so change filters
// work correctly.
namespace Latios.Systems
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TRSToLocalToParentSystem))]
    [UpdateAfter(typeof(TRSToLocalToWorldSystem))]
    [UpdateBefore(typeof(LocalToParentSystem))]
    [BurstCompile]
    public partial struct ImprovedLocalToParentSystem : ISystem
    {
        private EntityQuery     m_RootsQuery;
        private EntityQueryMask m_LocalToWorldWriteGroupMask;

        // LocalToWorld = Parent.LocalToWorld * LocalToParent
        [BurstCompile]
        struct UpdateHierarchy : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>       LocalToWorldTypeHandle;
            [ReadOnly] public BufferTypeHandle<Child>                 ChildTypeHandle;
            [ReadOnly] public BufferFromEntity<Child>                 ChildFromEntity;
            [ReadOnly] public ComponentDataFromEntity<PreviousParent> ParentFromEntity;
            [ReadOnly] public ComponentDataFromEntity<LocalToParent>  LocalToParentFromEntity;
            [ReadOnly] public EntityQueryMask                         LocalToWorldWriteGroupMask;
            public uint                                               LastSystemVersion;

            [NativeDisableContainerSafetyRestriction]
            public ComponentDataFromEntity<LocalToWorld> LocalToWorldFromEntity;

            void ChildLocalToWorld(ref float4x4 parentLocalToWorld,
                                   Entity entity,
                                   bool updateChildrenTransform,
                                   Entity parent,
                                   ref bool parentLtwValid,
                                   bool parentsChildBufferChanged)
            {
                updateChildrenTransform = updateChildrenTransform || LocalToParentFromEntity.DidChange(entity, LastSystemVersion);
                updateChildrenTransform = updateChildrenTransform || (parentsChildBufferChanged && ParentFromEntity.DidChange(entity, LastSystemVersion));

                float4x4 localToWorldMatrix = default;
                bool     ltwIsValid         = false;

                bool isDependent = LocalToWorldWriteGroupMask.Matches(entity);
                if (updateChildrenTransform && isDependent)
                {
                    if (!parentLtwValid)
                    {
                        parentLocalToWorld = LocalToWorldFromEntity[parent].Value;
                        parentLtwValid     = true;
                    }
                    var localToParent              = LocalToParentFromEntity[entity];
                    localToWorldMatrix             = math.mul(parentLocalToWorld, localToParent.Value);
                    ltwIsValid                     = true;
                    LocalToWorldFromEntity[entity] = new LocalToWorld { Value = localToWorldMatrix };
                }
                else if (!isDependent)  //This entity has a component with the WriteGroup(LocalToWorld)
                {
                    updateChildrenTransform = updateChildrenTransform || LocalToWorldFromEntity.DidChange(entity, LastSystemVersion);
                }
                if (ChildFromEntity.HasComponent(entity))
                {
                    var children        = ChildFromEntity[entity];
                    var childrenChanged = updateChildrenTransform || ChildFromEntity.DidChange(entity, LastSystemVersion);
                    for (int i = 0; i < children.Length; i++)
                    {
                        ChildLocalToWorld(ref localToWorldMatrix, children[i].Value, updateChildrenTransform, entity, ref ltwIsValid, childrenChanged);
                    }
                }
            }

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                bool updateChildrenTransform =
                    batchInChunk.DidChange<LocalToWorld>(LocalToWorldTypeHandle, LastSystemVersion) ||
                    batchInChunk.DidChange<Child>(ChildTypeHandle, LastSystemVersion);

                var  chunkLocalToWorld = batchInChunk.GetNativeArray(LocalToWorldTypeHandle);
                var  chunkChildren     = batchInChunk.GetBufferAccessor(ChildTypeHandle);
                bool ltwIsValid        = true;
                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var localToWorldMatrix = chunkLocalToWorld[i].Value;
                    var children           = chunkChildren[i];
                    for (int j = 0; j < children.Length; j++)
                    {
                        ChildLocalToWorld(ref localToWorldMatrix, children[j].Value, updateChildrenTransform, Entity.Null, ref ltwIsValid,
                                          batchInChunk.DidChange(ChildTypeHandle, LastSystemVersion));
                    }
                }
            }
        }

        //burst disabled pending burstable entityquerydesc
        //[BurstCompile]
        public unsafe void OnCreate(ref SystemState state)
        {
            state.WorldUnmanaged.ResolveSystemState(state.WorldUnmanaged.GetExistingUnmanagedSystem<LocalToParentSystem>().Handle)->Enabled = false;

            m_RootsQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<LocalToWorld>(),
                    ComponentType.ReadOnly<Child>()
                },
                None = new ComponentType[]
                {
                    typeof(Parent)
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });

            m_LocalToWorldWriteGroupMask = state.EntityManager.GetEntityQueryMask(state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(LocalToWorld),
                    ComponentType.ReadOnly<LocalToParent>(),
                    ComponentType.ReadOnly<Parent>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            }));
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        //disabling burst in dotsrt until burstable scheduling works
#if !UNITY_DOTSRUNTIME
        [BurstCompile]
#endif
        public void OnUpdate(ref SystemState state)
        {
            var localToWorldType        = state.GetComponentTypeHandle<LocalToWorld>(true);
            var childType               = state.GetBufferTypeHandle<Child>(true);
            var childFromEntity         = state.GetBufferFromEntity<Child>(true);
            var parentFromEntity        = state.GetComponentDataFromEntity<PreviousParent>(true);
            var localToParentFromEntity = state.GetComponentDataFromEntity<LocalToParent>(true);
            var localToWorldFromEntity  = state.GetComponentDataFromEntity<LocalToWorld>();

            var updateHierarchyJob = new UpdateHierarchy
            {
                LocalToWorldTypeHandle     = localToWorldType,
                ChildTypeHandle            = childType,
                ChildFromEntity            = childFromEntity,
                ParentFromEntity           = parentFromEntity,
                LocalToParentFromEntity    = localToParentFromEntity,
                LocalToWorldFromEntity     = localToWorldFromEntity,
                LocalToWorldWriteGroupMask = m_LocalToWorldWriteGroupMask,
                LastSystemVersion          = state.LastSystemVersion
            };
            state.Dependency = updateHierarchyJob.ScheduleParallel(m_RootsQuery, state.Dependency);
        }
    }
}

