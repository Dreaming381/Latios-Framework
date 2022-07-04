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
    [DisableAutoCreation]
    public partial class UpdateSkeletonBoundsSystem : SubSystem
    {
        EntityQuery m_exposedBonesQuery;
        EntityQuery m_optimizedSkeletonsQuery;

        protected override void OnCreate()
        {
            m_exposedBonesQuery = Fluent.WithAll<BoneBounds>(true).WithAll<BoneWorldBounds>(false).WithAll<ChunkBoneWorldBounds>(false, true)
                                  .WithAll<LocalToWorld>(            true).Build();

            m_optimizedSkeletonsQuery = Fluent.WithAll<OptimizedBoneBounds>(true).WithAll<OptimizedBoneToRoot>(true).WithAll<SkeletonWorldBounds>(false)
                                        .WithAll<ChunkSkeletonWorldBounds>(false, true).WithAll<LocalToWorld>(true).Build();
        }

        protected override void OnUpdate()
        {
            var ltwHandle         = GetComponentTypeHandle<LocalToWorld>(true);
            var lastSystemVersion = LastSystemVersion;

            Dependency = new ExposedBoneBoundsJob
            {
                boneBoundsHandle              = GetComponentTypeHandle<BoneBounds>(true),
                ltwHandle                     = ltwHandle,
                boneWorldBoundsReadOnlyHandle = GetComponentTypeHandle<BoneWorldBounds>(true),
                boneWorldBoundsHandle         = GetComponentTypeHandle<BoneWorldBounds>(false),
                chunkBoneWorldBoundsHandle    = GetComponentTypeHandle<ChunkBoneWorldBounds>(false),
                lastSystemVersion             = lastSystemVersion
            }.ScheduleParallel(m_exposedBonesQuery, Dependency);

            // Todo: Increase batches per chunk?
            Dependency = new OptimizedBoneBoundsJob
            {
                boneBoundsHandle                  = GetBufferTypeHandle<OptimizedBoneBounds>(true),
                boneToRootHandle                  = GetBufferTypeHandle<OptimizedBoneToRoot>(true),
                ltwHandle                         = ltwHandle,
                skeletonWorldBoundsReadOnlyHandle = GetComponentTypeHandle<SkeletonWorldBounds>(true),
                skeletonWorldBoundsHandle         = GetComponentTypeHandle<SkeletonWorldBounds>(false),
                chunkSkeletonWorldBoundsHandle    = GetComponentTypeHandle<ChunkSkeletonWorldBounds>(false),
                lastSystemVersion                 = lastSystemVersion
            }.ScheduleParallel(m_optimizedSkeletonsQuery, Dependency);
        }

        [BurstCompile]
        struct ExposedBoneBoundsJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<BoneBounds>                                     boneBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>                                   ltwHandle;
            [ReadOnly] public ComponentTypeHandle<BoneWorldBounds>                                boneWorldBoundsReadOnlyHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<BoneWorldBounds> boneWorldBoundsHandle;
            public ComponentTypeHandle<ChunkBoneWorldBounds>                                      chunkBoneWorldBoundsHandle;

            public uint lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                bool needsUpdate  = batchInChunk.DidChange(boneBoundsHandle, lastSystemVersion);
                needsUpdate      |= batchInChunk.DidChange(ltwHandle, lastSystemVersion);
                if (!needsUpdate && batchInChunk.DidOrderChange(lastSystemVersion))
                {
                    //A structural change happened but no component of concern changed, meaning we need to recalculate the chunk component but nothing else (rare).
                    var worldBoundsRO = batchInChunk.GetNativeArray(boneWorldBoundsReadOnlyHandle);
                    var aabb          = worldBoundsRO[0].bounds;
                    for (int i = 1; i < batchInChunk.Count; i++)
                        aabb =
                            Physics.CombineAabb(aabb, worldBoundsRO[i].bounds);
                    batchInChunk.SetChunkComponentData(chunkBoneWorldBoundsHandle, new ChunkBoneWorldBounds { chunkBounds = FromAabb(aabb) });
                    return;
                }
                if (!needsUpdate)
                    return;
                var boneBounds  = batchInChunk.GetNativeArray(boneBoundsHandle);
                var ltws        = batchInChunk.GetNativeArray(ltwHandle);
                var worldBounds = batchInChunk.GetNativeArray(boneWorldBoundsHandle);

                Aabb chunkBounds = ComputeBounds(boneBounds[0].radialOffsetInBoneSpace, ltws[0].Value);
                worldBounds[0]   = new BoneWorldBounds { bounds = chunkBounds };
                for (int i = 1; i < batchInChunk.Count; i++)
                {
                    var newBounds  = ComputeBounds(boneBounds[i].radialOffsetInBoneSpace, ltws[i].Value);
                    newBounds.min -= boneBounds[i].radialOffsetInWorldSpace;
                    newBounds.max += boneBounds[i].radialOffsetInWorldSpace;
                    worldBounds[i] = new BoneWorldBounds { bounds = newBounds };
                    chunkBounds    = Physics.CombineAabb(chunkBounds, newBounds);
                }

                batchInChunk.SetChunkComponentData(chunkBoneWorldBoundsHandle, new ChunkBoneWorldBounds { chunkBounds = FromAabb(chunkBounds) });
            }

            Aabb ComputeBounds(float radius, float4x4 ltw)
            {
                float3 extents = LatiosMath.RotateExtents(radius, ltw.c0.xyz, ltw.c1.xyz, ltw.c2.xyz);
                return new Aabb(ltw.c3.xyz - extents, ltw.c3.xyz + extents);
            }
        }

        [BurstCompile]
        struct OptimizedBoneBoundsJob : IJobEntityBatch
        {
            [ReadOnly] public BufferTypeHandle<OptimizedBoneBounds>                                   boneBoundsHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneToRoot>                                   boneToRootHandle;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>                                       ltwHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonShaderBoundsOffset> shaderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonWorldBounds>                                skeletonWorldBoundsReadOnlyHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<SkeletonWorldBounds> skeletonWorldBoundsHandle;
            public ComponentTypeHandle<ChunkSkeletonWorldBounds>                                      chunkSkeletonWorldBoundsHandle;

            public uint lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                bool needsUpdate  = batchInChunk.DidChange(boneBoundsHandle, lastSystemVersion);
                needsUpdate      |= batchInChunk.DidChange(boneToRootHandle, lastSystemVersion);
                needsUpdate      |= batchInChunk.DidChange(ltwHandle, lastSystemVersion);
                if (!needsUpdate && batchInChunk.DidOrderChange(lastSystemVersion))
                {
                    //A structural change happened but no component of concern changed, meaning we need to recalculate the chunk component but nothing else (rare).
                    var worldBoundsRO = batchInChunk.GetNativeArray(skeletonWorldBoundsReadOnlyHandle);
                    var aabb          = new Aabb( worldBoundsRO[0].bounds.Min, worldBoundsRO[0].bounds.Max);
                    for (int i = 1; i < batchInChunk.Count; i++)
                        aabb =
                            Physics.CombineAabb(aabb, new Aabb(worldBoundsRO[i].bounds.Min, worldBoundsRO[i].bounds.Max));
                    batchInChunk.SetChunkComponentData(chunkSkeletonWorldBoundsHandle, new ChunkSkeletonWorldBounds { chunkBounds = FromAabb(aabb) });
                    return;
                }
                if (!needsUpdate)
                    return;

                var boneBounds  = batchInChunk.GetBufferAccessor(boneBoundsHandle);
                var boneToRoots = batchInChunk.GetBufferAccessor(boneToRootHandle);
                var ltws        = batchInChunk.GetNativeArray(ltwHandle);
                var shaderBounds = batchInChunk.GetNativeArray(shaderBoundsHandle);
                var worldBounds = batchInChunk.GetNativeArray(skeletonWorldBoundsHandle);

                Aabb chunkBounds = ComputeBounds(boneBounds[0], boneToRoots[0], ltws[0].Value);
                worldBounds[0]   = new SkeletonWorldBounds { bounds = FromAabb(chunkBounds) };
                for (int i = 1; i < batchInChunk.Count; i++)
                {
                    var newBounds  = ComputeBounds(boneBounds[i], boneToRoots[i], ltws[i].Value);
                    newBounds.min -= shaderBounds[i].radialBoundsInWorldSpace;
                    newBounds.max += shaderBounds[i].radialBoundsInWorldSpace;
                    worldBounds[i] = new SkeletonWorldBounds { bounds = FromAabb(newBounds) };
                    chunkBounds    = Physics.CombineAabb(chunkBounds, newBounds);
                }

                batchInChunk.SetChunkComponentData(chunkSkeletonWorldBoundsHandle, new ChunkSkeletonWorldBounds { chunkBounds = FromAabb(chunkBounds) });
            }

            Aabb ComputeBounds(DynamicBuffer<OptimizedBoneBounds> bounds, DynamicBuffer<OptimizedBoneToRoot> boneToRoots, float4x4 ltw)
            {
                var boundsArray      = bounds.Reinterpret<float>().AsNativeArray();
                var boneToRootsArray = boneToRoots.Reinterpret<float4x4>().AsNativeArray();

                // Todo: Assert that buffers are not empty?
                var aabb = new Aabb(float.MaxValue, float.MinValue);
                for (int i = 0; i < boundsArray.Length; i++)
                {
                    aabb = Physics.CombineAabb(aabb, ComputeBounds(boundsArray[i], boneToRootsArray[i], ltw));
                }
                return aabb;
            }

            Aabb ComputeBounds(float radius, float4x4 ltr, float4x4 rtw)
            {
                float3 extents = LatiosMath.RotateExtents(radius, ltr.c0.xyz, ltr.c1.xyz, ltr.c2.xyz);
                extents        = LatiosMath.RotateExtents(extents, rtw.c0.xyz, rtw.c1.xyz, rtw.c2.xyz);
                float3 center  = math.transform(rtw, ltr.c3.xyz);
                return new Aabb(center - extents, center + extents);
            }
        }

        public static AABB FromAabb(Aabb aabb)
        {
            Physics.GetCenterExtents(aabb, out float3 center, out float3 extents);
            return new AABB { Center = center, Extents = extents };
        }
    }
}

