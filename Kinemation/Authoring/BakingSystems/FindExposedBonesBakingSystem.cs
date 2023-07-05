using Latios.Transforms.Authoring;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

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

        [BakingType]
        struct RequestPreviousTransform : IRequestPreviousTransform { }
        [BakingType]
        struct RequestTwoAgoTransform : IRequestTwoAgoTransform { }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var exposedBoneTypes = new FixedList128Bytes<ComponentType>();
            exposedBoneTypes.Add(ComponentType.ReadWrite<BoneOwningSkeletonReference>());
            exposedBoneTypes.Add(ComponentType.ReadWrite<BoneIndex>());
            exposedBoneTypes.Add(ComponentType.ReadWrite<BoneCullingIndex>());
            exposedBoneTypes.Add(ComponentType.ReadWrite<BoneBounds>());
            exposedBoneTypes.Add(ComponentType.ReadWrite<BoneWorldBounds>());
            exposedBoneTypes.Add(ComponentType.ChunkComponent<ChunkBoneWorldBounds>());
            exposedBoneTypes.Add(ComponentType.ReadWrite<RequestPreviousTransform>());
            exposedBoneTypes.Add(ComponentType.ReadWrite<RequestTwoAgoTransform>());
            ComponentTypeSet exposedBoneTypesToRemove = new ComponentTypeSet(in exposedBoneTypes);
            ComponentTypeSet exposedBoneTypesToAdd    = new ComponentTypeSet(in exposedBoneTypes);

            new ClearJob().ScheduleParallel();

            var ecbAdd                  = new EntityCommandBuffer(Allocator.TempJob);
            var cullingIndexLookup      = GetComponentLookup<BoneCullingIndex>(true);
            var skeletonReferenceLookup = GetComponentLookup<BoneOwningSkeletonReference>(false);
            var boneIndexLookup         = GetComponentLookup<BoneIndex>(false);
            new ApplySkeletonsToBonesJob
            {
                componentTypesToAdd     = exposedBoneTypesToAdd,
                ecb                     = ecbAdd.AsParallelWriter(),
                cullingIndexLookup      = cullingIndexLookup,
                skeletonReferenceLookup = skeletonReferenceLookup,
                boneIndexLookup         = boneIndexLookup
            }.ScheduleParallel();

            var ecbRemove                        = new EntityCommandBuffer(Allocator.TempJob);
            new RemoveDisconnectedBonesJob { ecb = ecbRemove.AsParallelWriter(), componentTypesToRemove = exposedBoneTypesToRemove }.ScheduleParallel();

            state.CompleteDependency();

            ecbAdd.Playback(state.EntityManager);
            ecbRemove.Playback(state.EntityManager);

            ecbAdd.Dispose();
            ecbRemove.Dispose();
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(BoneCullingIndex))]
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
            [ReadOnly] public ComponentLookup<BoneCullingIndex>                                       cullingIndexLookup;
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
                        ecb.SetComponent(chunkIndexInQuery, bone.bone, new BoneOwningSkeletonReference { skeletonRoot = entity });
                        ecb.SetComponent(chunkIndexInQuery, bone.bone, new BoneIndex { index                          = (short)i });
                    }
                    i++;
                }
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(BoneCullingIndex))]
        [BurstCompile]
        partial struct RemoveDisconnectedBonesJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ecb;
            public ComponentTypeSet                   componentTypesToRemove;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref BoneOwningSkeletonReference boneReference)
            {
                if (boneReference.skeletonRoot == Entity.Null)
                {
                    ecb.RemoveComponent( chunkIndexInQuery, entity, componentTypesToRemove);
                }
            }
        }
    }
}

