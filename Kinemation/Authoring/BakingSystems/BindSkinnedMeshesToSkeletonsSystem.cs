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
    public partial struct BindSkinnedMeshesToSkeletonsSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new ClearJob().ScheduleParallel();

            var ecbAdd = new EntityCommandBuffer(state.WorldUpdateAllocator);
            new BindJob
            {
                ecb                  = ecbAdd.AsParallelWriter(),
                rootLookup           = GetComponentLookup<BindSkeletonRoot>(false),
                parentOverrideLookup = GetComponentLookup<BakedParentOverride>(false)
            }.ScheduleParallel();

            var ecbRemove       = new EntityCommandBuffer(state.WorldUpdateAllocator);
            new UnbindJob { ecb = ecbRemove.AsParallelWriter() }.ScheduleParallel();

            state.CompleteDependency();

            ecbAdd.Playback(state.EntityManager);
            ecbRemove.Playback(state.EntityManager);
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct ClearJob : IJobEntity
        {
            public void Execute(ref BindSkeletonRoot root)
            {
                root.root = Entity.Null;
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct BindJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<BindSkeletonRoot>    rootLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BakedParentOverride> parentOverrideLookup;
            public EntityCommandBuffer.ParallelWriter                                         ecb;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, in DynamicBuffer<AutoBindSkinnedMeshToSkeleton> skinnedMeshes)
            {
                foreach (var skinnedMesh in skinnedMeshes)
                {
                    if (rootLookup.HasComponent(skinnedMesh.skinnedMeshEntity))
                    {
                        rootLookup[skinnedMesh.skinnedMeshEntity] = new BindSkeletonRoot { root = entity };
                    }
                    else
                    {
                        ecb.AddComponent(chunkIndexInQuery, skinnedMesh.skinnedMeshEntity, new BindSkeletonRoot { root = entity });
                    }

                    if (parentOverrideLookup.HasComponent(skinnedMesh.skinnedMeshEntity))
                    {
                        // Only write if this changed, to avoid triggering the change filter later down the line.
                        if (parentOverrideLookup[skinnedMesh.skinnedMeshEntity].parent != entity)
                            parentOverrideLookup[skinnedMesh.skinnedMeshEntity] = new BakedParentOverride { parent = entity };
                    }
                    else
                        ecb.AddComponent(chunkIndexInQuery, skinnedMesh.skinnedMeshEntity, new BakedParentOverride { parent = entity });
                }
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct UnbindJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ecb;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref BindSkeletonRoot root)
            {
                if (root.root == Entity.Null)
                {
                    ecb.RemoveComponent<BindSkeletonRoot>(    chunkIndexInQuery, entity);
                    ecb.RemoveComponent<BakedParentOverride>( chunkIndexInQuery, entity);
                }
            }
        }
    }
}

