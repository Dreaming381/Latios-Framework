using Latios.Authoring;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ResolveSkeletonAndSkinnedMeshBlobsSystem : ISystem
    {
        SmartBlobberResolverLookup m_lookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_lookup = state.GetSmartBlobberResolverLookup();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_lookup.Update(ref state);

            var ecb                                 = new EntityCommandBuffer(Allocator.TempJob);
            var writer                              = ecb.AsParallelWriter();
            new SkeletonBindingPathsJob { ecb       = writer, resolverLookup = m_lookup }.ScheduleParallel();
            new OptimizedSkeletonHierarchyJob { ecb = writer, resolverLookup = m_lookup }.ScheduleParallel();
            new MeshBindingPathsJob { ecb           = writer, resolverLookup = m_lookup }.ScheduleParallel();
            new MeshSkinningJob { ecb               = writer, resolverLookup = m_lookup }.ScheduleParallel();

            state.CompleteDependency();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct SkeletonBindingPathsJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ecb;
            public SmartBlobberResolverLookup         resolverLookup;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref SkeletonBindingPathsBlobReference reference,
                                in PendingSkeletonBindingPathsBlob handle)
            {
                var blob = handle.blobHandle.Resolve(ref resolverLookup);
                if (blob == BlobAssetReference<SkeletonBindingPathsBlob>.Null)
                    ecb.RemoveComponent<SkeletonBindingPathsBlobReference>(chunkIndexInQuery, entity);
                else
                    reference.blob = blob;
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct OptimizedSkeletonHierarchyJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ecb;
            public SmartBlobberResolverLookup         resolverLookup;

            public void Execute(Entity entity,
                                [ChunkIndexInQuery] int chunkIndexInQuery,
                                ref OptimizedSkeletonHierarchyBlobReference reference,
                                in PendingOptimizedSkeletonHierarchyBlob handle)
            {
                var blob = handle.blobHandle.Resolve(ref resolverLookup);
                if (blob == BlobAssetReference<OptimizedSkeletonHierarchyBlob>.Null)
                    ecb.RemoveComponent<OptimizedSkeletonHierarchyBlobReference>(chunkIndexInQuery, entity);
                else
                    reference.blob = blob;
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct MeshBindingPathsJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ecb;
            public SmartBlobberResolverLookup         resolverLookup;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref MeshBindingPathsBlobReference reference, in PendingMeshBindingPathsBlob handle)
            {
                var blob = handle.blobHandle.Resolve(ref resolverLookup);
                if (blob == BlobAssetReference<MeshBindingPathsBlob>.Null)
                    ecb.RemoveComponent<MeshBindingPathsBlobReference>(chunkIndexInQuery, entity);
                else
                    reference.blob = blob;
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct MeshSkinningJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ecb;
            public SmartBlobberResolverLookup         resolverLookup;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref MeshDeformDataBlobReference reference, in PendingMeshDeformDataBlob handle)
            {
                var blob = handle.blobHandle.Resolve(ref resolverLookup);
                if (blob == BlobAssetReference<MeshDeformDataBlob>.Null)
                    ecb.RemoveComponent<MeshDeformDataBlobReference>(chunkIndexInQuery, entity);
                else
                    reference.blob = blob;
            }
        }
    }
}

