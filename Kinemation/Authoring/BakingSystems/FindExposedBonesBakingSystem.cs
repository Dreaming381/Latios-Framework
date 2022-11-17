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
    public partial struct FindExposedBonesBakingSystem : ISystem
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
            ComponentTypeSet exposedBoneTypes = CullingUtilities.GetBoneCullingComponentTypes();

            new ClearJob().ScheduleParallel();

            var ecbAdd                   = new EntityCommandBuffer(Allocator.TempJob);
            var transformAuthoringLookup = GetComponentLookup<TransformAuthoring>(true);
            var cullingIndexLookup       = GetComponentLookup<BoneCullingIndex>(true);
            var skeletonReferenceLookup  = GetComponentLookup<BoneOwningSkeletonReference>(false);
            var boneIndexLookup          = GetComponentLookup<BoneIndex>(false);
            new ApplySkeletonsToBonesJob
            {
                componentTypesToAdd      = exposedBoneTypes,
                ecb                      = ecbAdd.AsParallelWriter(),
                transformAuthoringLookup = transformAuthoringLookup,
                cullingIndexLookup       = cullingIndexLookup,
                skeletonReferenceLookup  = skeletonReferenceLookup,
                boneIndexLookup          = boneIndexLookup
            }.ScheduleParallel();

            var ecbRemove                        = new EntityCommandBuffer(Allocator.TempJob);
            new RemoveDisconnectedBonesJob { ecb = ecbRemove.AsParallelWriter(), componentTypesToRemove = exposedBoneTypes }.ScheduleParallel();

            state.CompleteDependency();

            ecbAdd.Playback(state.EntityManager);
            ecbRemove.Playback(state.EntityManager);

            ecbAdd.Dispose();
            ecbRemove.Dispose();
        }

        [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(BoneCullingIndex))]
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
            [ReadOnly] public ComponentLookup<BoneCullingIndex>                                       cullingIndexLookup;
            [ReadOnly] public ComponentLookup<TransformAuthoring>                                     transformAuthoringLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneOwningSkeletonReference> skeletonReferenceLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneIndex>                   boneIndexLookup;
            public EntityCommandBuffer.ParallelWriter                                                 ecb;
            public ComponentTypeSet                                                                   componentTypesToAdd;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, in DynamicBuffer<BoneReference> bones)
            {
                int i = 0;
                foreach (var bone in bones)
                {
                    if (cullingIndexLookup.HasComponent(bone.bone))
                    {
                        skeletonReferenceLookup[bone.bone] = new BoneOwningSkeletonReference { skeletonRoot = entity };
                        boneIndexLookup[bone.bone]                                                          = new BoneIndex { index = (short)i };
                    }
                    else
                    {
                        ecb.AddComponent( chunkIndexInQuery, bone.bone, componentTypesToAdd);
                        ecb.AddComponent( chunkIndexInQuery, bone.bone, new NonUniformScale { Value                   = transformAuthoringLookup[bone.bone].LocalScale });
                        ecb.SetComponent(chunkIndexInQuery, bone.bone, new BoneOwningSkeletonReference { skeletonRoot = entity });
                        ecb.SetComponent(chunkIndexInQuery, bone.bone, new BoneIndex { index                          = (short)i });
                    }
                    i++;
                }
            }
        }

        [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(BoneCullingIndex))]
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

