using System.Runtime.InteropServices;
using Latios.Psyshock;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    // This exists because setting the chunk bounds to an extreme value breaks shadows.
    // Instead we calculate the combined chunk bounds for all skeletons and then write them to all skinned mesh chunk bounds.
    [DisableAutoCreation]
    public partial class UpdateSkinnedMeshChunkBoundsSystem : SubSystem
    {
        EntityQuery m_exposedMetaQuery;
        EntityQuery m_optimizedMetaQuery;
        EntityQuery m_skinnedMeshMetaQuery;

        protected override void OnCreate()
        {
            m_exposedMetaQuery     = Fluent.WithAll<ChunkHeader>(true).WithAll<ChunkBoneWorldBounds>(true).Build();
            m_optimizedMetaQuery   = Fluent.WithAll<ChunkHeader>(true).WithAll<ChunkSkeletonWorldBounds>(true).Build();
            m_skinnedMeshMetaQuery = Fluent.WithAll<ChunkHeader>(true).WithAll<ChunkWorldRenderBounds>(false)
                                     .WithAny<ChunkComputeDeformMemoryMetadata>(true).WithAny<ChunkLinearBlendSkinningMemoryMetadata>(true).Build();
        }

        protected override void OnUpdate()
        {
            var combinedBounds   = new NativeReference<Aabb>(World.UpdateAllocator.ToAllocator);
            combinedBounds.Value = new Aabb(float.MaxValue, float.MinValue);

            Dependency = new CombineExposedJob
            {
                handle         = GetComponentTypeHandle<ChunkBoneWorldBounds>(true),
                combinedBounds = combinedBounds
            }.Schedule(m_exposedMetaQuery, Dependency);

            Dependency = new CombineOptimizedJob
            {
                handle         = GetComponentTypeHandle<ChunkSkeletonWorldBounds>(true),
                combinedBounds = combinedBounds
            }.Schedule(m_optimizedMetaQuery, Dependency);

            Dependency = new ApplyChunkBoundsToSkinnedMeshesJob
            {
                handle         = GetComponentTypeHandle<ChunkWorldRenderBounds>(),
                combinedBounds = combinedBounds
            }.Schedule(m_skinnedMeshMetaQuery, Dependency);
        }

        [BurstCompile]
        struct CombineExposedJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<ChunkBoneWorldBounds> handle;
            public NativeReference<Aabb>                                combinedBounds;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                Aabb aabb   = new Aabb(float.MaxValue, float.MinValue);
                var  bounds = batchInChunk.GetNativeArray(handle);
                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var b = bounds[i].chunkBounds;
                    aabb  = Physics.CombineAabb(aabb, new Aabb(b.Min, b.Max));
                }
                combinedBounds.Value = Physics.CombineAabb(combinedBounds.Value, aabb);
            }
        }

        [BurstCompile]
        struct CombineOptimizedJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<ChunkSkeletonWorldBounds> handle;
            public NativeReference<Aabb>                                    combinedBounds;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                Aabb aabb   = new Aabb(float.MaxValue, float.MinValue);
                var  bounds = batchInChunk.GetNativeArray(handle);
                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var b = bounds[i].chunkBounds;
                    aabb  = Physics.CombineAabb(aabb, new Aabb(b.Min, b.Max));
                }
                combinedBounds.Value = Physics.CombineAabb(combinedBounds.Value, aabb);
            }
        }

        [BurstCompile]
        struct ApplyChunkBoundsToSkinnedMeshesJob : IJobEntityBatch
        {
            [ReadOnly] public NativeReference<Aabb>            combinedBounds;
            public ComponentTypeHandle<ChunkWorldRenderBounds> handle;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var aabb   = new ChunkWorldRenderBounds { Value = FromAabb(combinedBounds.Value) };
                var bounds                                      = batchInChunk.GetNativeArray(handle);
                for (int i = 0; i < batchInChunk.Count; i++)
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

