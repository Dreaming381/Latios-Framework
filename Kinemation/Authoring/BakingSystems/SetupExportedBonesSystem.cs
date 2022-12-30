using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
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

            var boneCount                 = new NativeReference<int>(Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var rechildHashSet            = new NativeParallelHashSet<Entity>(1, Allocator.TempJob);
            new CountBonesJob { boneCount = boneCount }.Schedule();
            state.Dependency              = new ResizeHashsetJob { boneCount = boneCount, bonesToReChild = rechildHashSet}.Schedule(state.Dependency);

            var ecbAdd                          = new EntityCommandBuffer(Allocator.TempJob);
            var skeletonReferenceLookup         = GetComponentLookup<BoneOwningSkeletonReference>(false);
            var copyLocalToParentFromBoneLookup = GetComponentLookup<CopyLocalToParentFromBone>(false);
            var localToParentLookup             = GetComponentLookup<LocalToParent>(false);
            var parentLookup                    = GetComponentLookup<Parent>(false);
            new ApplySkeletonsToBonesJob
            {
                bonesToReChild                  = rechildHashSet.AsParallelWriter(),
                componentTypesToAdd             = componentsToAdd,
                componentTypesToRemove          = transformComponentsToRemove,
                ecb                             = ecbAdd.AsParallelWriter(),
                skeletonReferenceLookup         = skeletonReferenceLookup,
                copyLocalToParentFromBoneLookup = copyLocalToParentFromBoneLookup,
                localToParentLookup             = localToParentLookup,
                parentLookup                    = parentLookup,
                transformAuthoringLookup        = GetComponentLookup<TransformAuthoring>(true)
            }.ScheduleParallel();

            var ecbRemove                        = new EntityCommandBuffer(Allocator.TempJob);
            new RemoveDisconnectedBonesJob { ecb = ecbRemove.AsParallelWriter(), componentTypesToRemove = componentsToRemove }.ScheduleParallel();

            new ReChildExportedBonesJob { bonesToRechild = rechildHashSet }.ScheduleParallel();

            state.CompleteDependency();

            ecbAdd.Playback(state.EntityManager);
            ecbRemove.Playback(state.EntityManager);

            ecbAdd.Dispose();
            ecbRemove.Dispose();
            boneCount.Dispose();
            rechildHashSet.Dispose();
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(CopyLocalToParentFromBone))]
        [BurstCompile]
        partial struct ClearJob : IJobEntity
        {
            public void Execute(ref BoneOwningSkeletonReference boneReference)
            {
                boneReference.skeletonRoot = Entity.Null;
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct CountBonesJob : IJobEntity
        {
            public NativeReference<int> boneCount;

            public void Execute(in DynamicBuffer<OptimizedSkeletonExportedBone> bones)
            {
                boneCount.Value += bones.Length;
            }
        }

        [BurstCompile]
        partial struct ResizeHashsetJob : IJob
        {
            [ReadOnly] public NativeReference<int> boneCount;
            public NativeParallelHashSet<Entity>   bonesToReChild;

            public void Execute()
            {
                bonesToReChild.Capacity = math.max(boneCount.Value * 2, bonesToReChild.Capacity);
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct ApplySkeletonsToBonesJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<CopyLocalToParentFromBone>   copyLocalToParentFromBoneLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneOwningSkeletonReference> skeletonReferenceLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToParent>               localToParentLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Parent>                      parentLookup;
            public NativeParallelHashSet<Entity>.ParallelWriter                                       bonesToReChild;
            public EntityCommandBuffer.ParallelWriter                                                 ecb;
            public ComponentTypeSet                                                                   componentTypesToAdd;
            public ComponentTypeSet                                                                   componentTypesToRemove;

            [ReadOnly] public ComponentLookup<TransformAuthoring> transformAuthoringLookup;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref DynamicBuffer<OptimizedSkeletonExportedBone> bones,
                                in DynamicBuffer<OptimizedBoneToRoot> boneToRoots)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i].boneIndex == 0)
                    {
                        // If the exported bone is still parented to the root, it is not actually an exported bone.
                        // We need to set it up like a local transform.
                        var transformAuthoring                                                           = transformAuthoringLookup[bones[i].boneEntity];
                        ecb.AddComponent(chunkIndexInQuery, bones[i].boneEntity, new Translation { Value = transformAuthoring.LocalPosition });
                        ecb.AddComponent(chunkIndexInQuery, bones[i].boneEntity, new Rotation { Value    = transformAuthoring.LocalRotation });
                        ecb.AddComponent(chunkIndexInQuery, bones[i].boneEntity, new Parent { Value      = entity });
                        ecb.AddComponent(chunkIndexInQuery, bones[i].boneEntity, new LocalToParent {
                            Value = float4x4.TRS(transformAuthoring.LocalPosition, transformAuthoring.LocalRotation, transformAuthoring.LocalScale)
                        });
                        ecb.AddComponent(chunkIndexInQuery, bones[i].boneEntity, new LocalToWorld { Value = transformAuthoring.LocalToWorld });

                        if (math.any(transformAuthoring.LocalScale != new float3(1f, 1f, 1f)))
                            ecb.AddComponent(chunkIndexInQuery, bones[i].boneEntity, new NonUniformScale { Value = transformAuthoring.LocalScale });

                        bones.RemoveAt(i);
                        i--;
                    }
                }

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
                    bonesToReChild.Add(bone.boneEntity);
                }
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
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

        [WithChangeFilter(typeof(TransformAuthoring), typeof(Parent))]
        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct ReChildExportedBonesJob : IJobEntity
        {
            [ReadOnly] public NativeParallelHashSet<Entity> bonesToRechild;

            public void Execute(ref Parent parent, in TransformAuthoring ta)
            {
                if (ta.AuthoringParent != ta.RuntimeParent && bonesToRechild.Contains(ta.AuthoringParent))
                    parent.Value = ta.AuthoringParent;
            }
        }
    }
}

