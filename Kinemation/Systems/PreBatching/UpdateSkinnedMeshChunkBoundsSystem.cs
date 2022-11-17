using Latios.Psyshock;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Kinemation.Systems
{
    // This exists because setting the chunk bounds to an extreme value breaks shadows.
    // Instead we calculate the combined chunk bounds for all skeletons and then write them to all skinned mesh chunk bounds.
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateSkinnedMeshChunkBoundsSystem : ISystem
    {
        EntityQuery m_exposedMetaQuery;
        EntityQuery m_optimizedMetaQuery;
        EntityQuery m_skinnedMeshMetaQuery;

        ComponentTypeHandle<ChunkBoneWorldBounds>     m_chunkBoneWorldBoundsHandle;
        ComponentTypeHandle<ChunkSkeletonWorldBounds> m_chunkSkeletonWorldBoundsHandle;
        ComponentTypeHandle<ChunkWorldRenderBounds>   m_chunkWorldRenderBoundsHandle;

        public void OnCreate(ref SystemState state)
        {
            m_exposedMetaQuery     = state.Fluent().WithAll<ChunkHeader>(true).WithAll<ChunkBoneWorldBounds>(true).Build();
            m_optimizedMetaQuery   = state.Fluent().WithAll<ChunkHeader>(true).WithAll<ChunkSkeletonWorldBounds>(true).Build();
            m_skinnedMeshMetaQuery = state.Fluent().WithAll<ChunkHeader>(true).WithAll<ChunkWorldRenderBounds>(false)
                                     .WithAny<ChunkComputeDeformMemoryMetadata>(true).WithAny<ChunkLinearBlendSkinningMemoryMetadata>(true).Build();

            m_chunkBoneWorldBoundsHandle     = state.GetComponentTypeHandle<ChunkBoneWorldBounds>(true);
            m_chunkSkeletonWorldBoundsHandle = state.GetComponentTypeHandle<ChunkSkeletonWorldBounds>(true);
            m_chunkWorldRenderBoundsHandle   = state.GetComponentTypeHandle<ChunkWorldRenderBounds>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_chunkBoneWorldBoundsHandle.Update(ref state);
            m_chunkSkeletonWorldBoundsHandle.Update(ref state);
            m_chunkWorldRenderBoundsHandle.Update(ref state);

            var combinedBounds   = new NativeReference<Aabb>(state.WorldUpdateAllocator);
            combinedBounds.Value = new Aabb(float.MaxValue, float.MinValue);

            state.Dependency = new CombineExposedJob
            {
                handle         = m_chunkBoneWorldBoundsHandle,
                combinedBounds = combinedBounds
            }.Schedule(m_exposedMetaQuery, state.Dependency);

            state.Dependency = new CombineOptimizedJob
            {
                handle         = m_chunkSkeletonWorldBoundsHandle,
                combinedBounds = combinedBounds
            }.Schedule(m_optimizedMetaQuery, state.Dependency);

            state.Dependency = new ApplyChunkBoundsToSkinnedMeshesJob
            {
                handle         = m_chunkWorldRenderBoundsHandle,
                combinedBounds = combinedBounds
            }.Schedule(m_skinnedMeshMetaQuery, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state) {
        }

        [BurstCompile]
        struct CombineExposedJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkBoneWorldBounds> handle;
            public NativeReference<Aabb>                                combinedBounds;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Aabb aabb   = new Aabb(float.MaxValue, float.MinValue);
                var  bounds = chunk.GetNativeArray(handle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var b = bounds[i].chunkBounds;
                    aabb  = Physics.CombineAabb(aabb, new Aabb(b.Min, b.Max));
                }
                combinedBounds.Value = Physics.CombineAabb(combinedBounds.Value, aabb);
            }
        }

        [BurstCompile]
        struct CombineOptimizedJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkSkeletonWorldBounds> handle;
            public NativeReference<Aabb>                                    combinedBounds;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Aabb aabb   = new Aabb(float.MaxValue, float.MinValue);
                var  bounds = chunk.GetNativeArray(handle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var b = bounds[i].chunkBounds;
                    aabb  = Physics.CombineAabb(aabb, new Aabb(b.Min, b.Max));
                }
                combinedBounds.Value = Physics.CombineAabb(combinedBounds.Value, aabb);
            }
        }

        [BurstCompile]
        struct ApplyChunkBoundsToSkinnedMeshesJob : IJobChunk
        {
            [ReadOnly] public NativeReference<Aabb>            combinedBounds;
            public ComponentTypeHandle<ChunkWorldRenderBounds> handle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var aabb   = new ChunkWorldRenderBounds { Value = FromAabb(combinedBounds.Value) };
                var bounds                                      = chunk.GetNativeArray(handle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    bounds[i] = aabb;
                }
            }

            public static AABB FromAabb(Aabb aabb)
            {
                Physics.GetCenterExtents(aabb, out float3 center, out float3 extents);
                return new AABB { Center = center, Extents = extents };
            }
        }
    }
}

