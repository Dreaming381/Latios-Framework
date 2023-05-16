#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

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
            var componentsToAdd = new ComponentTypeSet(ComponentType.ReadWrite<CopyLocalToParentFromBone>(),
                                                       ComponentType.ReadWrite<BoneOwningSkeletonReference>());

            new ClearJob().ScheduleParallel();

            var ecbAdd                          = new EntityCommandBuffer(Allocator.TempJob);
            var skeletonReferenceLookup         = GetComponentLookup<BoneOwningSkeletonReference>(false);
            var copyLocalToParentFromBoneLookup = GetComponentLookup<CopyLocalToParentFromBone>(false);
            var parentLookup                    = GetComponentLookup<Parent>(false);
            new ApplySkeletonsToBonesJob
            {
                componentTypesToAdd             = componentsToAdd,
                ecb                             = ecbAdd.AsParallelWriter(),
                skeletonReferenceLookup         = skeletonReferenceLookup,
                copyLocalToParentFromBoneLookup = copyLocalToParentFromBoneLookup,
                parentLookup                    = parentLookup,
                transformAuthoringLookup        = GetComponentLookup<TransformAuthoring>(true),
                localTransformLookup            = GetComponentLookup<LocalTransform>(false)
            }.ScheduleParallel();

            var ecbRemove                        = new EntityCommandBuffer(Allocator.TempJob);
            new RemoveDisconnectedBonesJob { ecb = ecbRemove.AsParallelWriter(), componentTypesToRemove = componentsToAdd }.ScheduleParallel();

            state.CompleteDependency();

            ecbAdd.Playback(state.EntityManager);
            ecbRemove.Playback(state.EntityManager);

            ecbAdd.Dispose();
            ecbRemove.Dispose();
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
        partial struct ApplySkeletonsToBonesJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform>              localTransformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<CopyLocalToParentFromBone>   copyLocalToParentFromBoneLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneOwningSkeletonReference> skeletonReferenceLookup;
            [ReadOnly] public ComponentLookup<Parent>                                                 parentLookup;
            public EntityCommandBuffer.ParallelWriter                                                 ecb;
            public ComponentTypeSet                                                                   componentTypesToAdd;

            [ReadOnly] public ComponentLookup<TransformAuthoring> transformAuthoringLookup;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref DynamicBuffer<OptimizedSkeletonExportedBone> bones,
                                in DynamicBuffer<OptimizedBoneTransform> boneTransforms)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i].boneIndex == 0)
                    {
                        // If the exported bone is still parented to the root, it is not actually an exported bone.
                        bones.RemoveAt(i);
                        i--;
                    }
                }

                foreach (var bone in bones)
                {
                    var root                              = ComputeRootTransformOfBone(bone.boneIndex, in boneTransforms);
                    localTransformLookup[bone.boneEntity] = new LocalTransform
                    {
                        localTransform = new TransformQvs(root.position, root.rotation, root.scale)
                    };

                    if (copyLocalToParentFromBoneLookup.HasComponent(bone.boneEntity))
                    {
                        skeletonReferenceLookup[bone.boneEntity]         = new BoneOwningSkeletonReference { skeletonRoot = entity };
                        copyLocalToParentFromBoneLookup[bone.boneEntity]                                                  = new CopyLocalToParentFromBone {
                            boneIndex                                                                                     = (short)bone.boneIndex
                        };
                    }
                    else
                    {
                        ecb.AddComponent( chunkIndexInQuery, bone.boneEntity, componentTypesToAdd);
                        ecb.SetComponent(chunkIndexInQuery, bone.boneEntity, new BoneOwningSkeletonReference { skeletonRoot = entity });
                        ecb.SetComponent(chunkIndexInQuery, bone.boneEntity, new CopyLocalToParentFromBone { boneIndex      = (short)bone.boneIndex });
                    }
                }
            }

            TransformQvvs ComputeRootTransformOfBone(int index, in DynamicBuffer<OptimizedBoneTransform> transforms)
            {
                var result = transforms[index].boneTransform;
                var parent = result.worldIndex;
                while (parent > 0)
                {
                    var parentTransform = transforms[parent].boneTransform;
                    parent              = parentTransform.worldIndex;
                    result              = qvvs.mul(parentTransform, result);
                }
                return result;
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
    }
}
#endif

