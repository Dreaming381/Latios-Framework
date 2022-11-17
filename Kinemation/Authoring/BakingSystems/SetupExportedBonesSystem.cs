using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct SetupExportedBonesSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var transformComponentsToRemove = new ComponentTypeSet(ComponentType.ReadWrite<Translation>(),
                                                                   ComponentType.ReadWrite<Rotation>(),
                                                                   ComponentType.ReadWrite<NonUniformScale>());
            var componentsToAdd = new ComponentTypeSet(ComponentType.ReadWrite<Parent>(),
                                                       ComponentType.ReadWrite<LocalToParent>(),
                                                       ComponentType.ReadWrite<LocalToWorld>(),
                                                       ComponentType.ReadWrite<CopyLocalToParentFromBone>(),
                                                       ComponentType.ReadWrite<BoneOwningSkeletonReference>());
            var componentsToRemove = new ComponentTypeSet(ComponentType.ReadWrite<CopyLocalToParentFromBone>(),
                                                          ComponentType.ReadWrite<BoneOwningSkeletonReference>());

            new ClearJob().ScheduleParallel();

            var ecbAdd                          = new EntityCommandBuffer(Allocator.TempJob);
            var skeletonReferenceLookup         = GetComponentLookup<BoneOwningSkeletonReference>(false);
            var copyLocalToParentFromBoneLookup = GetComponentLookup<CopyLocalToParentFromBone>(false);
            var localToParentLookup             = GetComponentLookup<LocalToParent>(false);
            var parentLookup                    = GetComponentLookup<Parent>(false);
            new ApplySkeletonsToBonesJob
            {
                componentTypesToAdd             = componentsToAdd,
                componentTypesToRemove          = transformComponentsToRemove,
                ecb                             = ecbAdd.AsParallelWriter(),
                skeletonReferenceLookup         = skeletonReferenceLookup,
                copyLocalToParentFromBoneLookup = copyLocalToParentFromBoneLookup,
                localToParentLookup             = localToParentLookup,
                parentLookup                    = parentLookup
            }.ScheduleParallel();

            var ecbRemove                        = new EntityCommandBuffer(Allocator.TempJob);
            new RemoveDisconnectedBonesJob { ecb = ecbRemove.AsParallelWriter(), componentTypesToRemove = componentsToRemove }.ScheduleParallel();

            state.CompleteDependency();

            ecbAdd.Playback(state.EntityManager);
            ecbRemove.Playback(state.EntityManager);

            ecbAdd.Dispose();
            ecbRemove.Dispose();
        }

        [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(CopyLocalToParentFromBone))]
        [BurstCompile]
        partial struct ClearJob : IJobEntity
        {
            public void Execute(ref BoneOwningSkeletonReference boneReference)
            {
                boneReference.skeletonRoot = Entity.Null;
            }
        }

        [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct ApplySkeletonsToBonesJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<CopyLocalToParentFromBone>   copyLocalToParentFromBoneLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneOwningSkeletonReference> skeletonReferenceLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToParent>               localToParentLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Parent>                      parentLookup;
            public EntityCommandBuffer.ParallelWriter                                                 ecb;
            public ComponentTypeSet                                                                   componentTypesToAdd;
            public ComponentTypeSet                                                                   componentTypesToRemove;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, in DynamicBuffer<OptimizedSkeletonExportedBone> bones,
                                in DynamicBuffer<OptimizedBoneToRoot> boneToRoots)
            {
                foreach (var bone in bones)
                {
                    if (copyLocalToParentFromBoneLookup.HasComponent(bone.boneEntity))
                    {
                        skeletonReferenceLookup[bone.boneEntity]         = new BoneOwningSkeletonReference { skeletonRoot = entity };
                        copyLocalToParentFromBoneLookup[bone.boneEntity]                                                  = new CopyLocalToParentFromBone {
                            boneIndex                                                                                     = (short)bone.boneIndex
                        };
                        localToParentLookup[bone.boneEntity] = new LocalToParent {
                            Value                            = boneToRoots[bone.boneIndex].boneToRoot
                        };
                        parentLookup[bone.boneEntity] = new Parent { Value = entity };
                    }
                    else
                    {
                        ecb.RemoveComponent(chunkIndexInQuery, bone.boneEntity, componentTypesToRemove);
                        ecb.AddComponent( chunkIndexInQuery, bone.boneEntity, componentTypesToAdd);
                        ecb.SetComponent(chunkIndexInQuery, bone.boneEntity, new BoneOwningSkeletonReference { skeletonRoot = entity });
                        ecb.SetComponent(chunkIndexInQuery, bone.boneEntity, new CopyLocalToParentFromBone { boneIndex      = (short)bone.boneIndex });
                        ecb.SetComponent(chunkIndexInQuery, bone.boneEntity, new LocalToParent { Value                      = boneToRoots[bone.boneIndex].boneToRoot });
                        ecb.SetComponent(chunkIndexInQuery, bone.boneEntity, new Parent { Value                             = entity });
                    }
                }
            }
        }

        [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(CopyLocalToParentFromBone))]
        [BurstCompile]
        partial struct RemoveDisconnectedBonesJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ecb;
            public ComponentTypeSet                   componentTypesToRemove;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref BoneOwningSkeletonReference boneReference)
            {
                if (boneReference.skeletonRoot == Entity.Null)
                    ecb.RemoveComponent(chunkIndexInQuery, entity, componentTypesToRemove);
            }
        }
    }
}

