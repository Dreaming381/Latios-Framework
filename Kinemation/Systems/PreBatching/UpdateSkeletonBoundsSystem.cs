using Latios.Psyshock;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateSkeletonBoundsSystem : ISystem
    {
        EntityQuery m_exposedBonesQuery;
        EntityQuery m_optimizedSkeletonsQuery;

        ComponentTypeHandle<LocalToWorld> m_ltwHandle;

        ComponentTypeHandle<BoneBounds>           m_boneBoundsHandle;
        ComponentTypeHandle<BoneWorldBounds>      m_boneWorldBoundsHandleRO;
        ComponentTypeHandle<BoneWorldBounds>      m_boneWorldBoundsHandleRW;
        ComponentTypeHandle<ChunkBoneWorldBounds> m_chunkBoneWorldBoundsHandle;

        BufferTypeHandle<OptimizedBoneBounds>           m_optimizedBoneBoundsHandle;
        BufferTypeHandle<OptimizedBoneToRoot>           m_optimizedBoneToRootHandle;
        ComponentTypeHandle<SkeletonWorldBounds>        m_skeletonWorldBoundsHandleRO;
        ComponentTypeHandle<SkeletonWorldBounds>        m_skeletonWorldBoundsHandleRW;
        ComponentTypeHandle<ChunkSkeletonWorldBounds>   m_chunkSkeletonWorldBoundsHandle;
        ComponentTypeHandle<SkeletonShaderBoundsOffset> m_skeletonShaderBoundsOffsetHandle;

        public void OnCreate(ref SystemState state)
        {
            m_exposedBonesQuery = state.Fluent().WithAll<BoneBounds>(true).WithAll<BoneWorldBounds>(false).WithAll<ChunkBoneWorldBounds>(false, true)
                                  .WithAll<LocalToWorld>(            true).Build();

            m_optimizedSkeletonsQuery = state.Fluent().WithAll<OptimizedBoneBounds>(true).WithAll<OptimizedBoneToRoot>(true).WithAll<SkeletonWorldBounds>(false)
                                        .WithAll<ChunkSkeletonWorldBounds>(false, true).WithAll<LocalToWorld>(true).Build();

            m_ltwHandle = state.GetComponentTypeHandle<LocalToWorld>(true);

            m_boneBoundsHandle           = state.GetComponentTypeHandle<BoneBounds>(          true);
            m_boneWorldBoundsHandleRO    = state.GetComponentTypeHandle<BoneWorldBounds>(     true);
            m_boneWorldBoundsHandleRW    = state.GetComponentTypeHandle<BoneWorldBounds>(     false);
            m_chunkBoneWorldBoundsHandle = state.GetComponentTypeHandle<ChunkBoneWorldBounds>(false);

            m_optimizedBoneBoundsHandle        = state.GetBufferTypeHandle<OptimizedBoneBounds>(true);
            m_optimizedBoneToRootHandle        = state.GetBufferTypeHandle<OptimizedBoneToRoot>(true);
            m_skeletonWorldBoundsHandleRO      = state.GetComponentTypeHandle<SkeletonWorldBounds>(     true);
            m_skeletonWorldBoundsHandleRW      = state.GetComponentTypeHandle<SkeletonWorldBounds>(     false);
            m_chunkSkeletonWorldBoundsHandle   = state.GetComponentTypeHandle<ChunkSkeletonWorldBounds>(false);
            m_skeletonShaderBoundsOffsetHandle = state.GetComponentTypeHandle<SkeletonShaderBoundsOffset>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_ltwHandle.Update(ref state);

            m_boneBoundsHandle.Update(ref state);
            m_boneWorldBoundsHandleRO.Update(ref state);
            m_boneWorldBoundsHandleRW.Update(ref state);
            m_chunkBoneWorldBoundsHandle.Update(ref state);

            m_optimizedBoneBoundsHandle.Update(ref state);
            m_optimizedBoneToRootHandle.Update(ref state);
            m_skeletonWorldBoundsHandleRO.Update(ref state);
            m_skeletonWorldBoundsHandleRW.Update(ref state);
            m_chunkSkeletonWorldBoundsHandle.Update(ref state);
            m_skeletonShaderBoundsOffsetHandle.Update(ref state);

            var lastSystemVersion = state.LastSystemVersion;

            state.Dependency = new ExposedBoneBoundsJob
            {
                boneBoundsHandle              = m_boneBoundsHandle,
                ltwHandle                     = m_ltwHandle,
                boneWorldBoundsReadOnlyHandle = m_boneWorldBoundsHandleRO,
                boneWorldBoundsHandle         = m_boneWorldBoundsHandleRW,
                chunkBoneWorldBoundsHandle    = m_chunkBoneWorldBoundsHandle,
                lastSystemVersion             = lastSystemVersion
            }.ScheduleParallel(m_exposedBonesQuery, state.Dependency);

            // Todo: Increase batches per chunk?
            state.Dependency = new OptimizedBoneBoundsJob
            {
                boneBoundsHandle                  = m_optimizedBoneBoundsHandle,
                boneToRootHandle                  = m_optimizedBoneToRootHandle,
                ltwHandle                         = m_ltwHandle,
                skeletonWorldBoundsReadOnlyHandle = m_skeletonWorldBoundsHandleRO,
                skeletonWorldBoundsHandle         = m_skeletonWorldBoundsHandleRW,
                chunkSkeletonWorldBoundsHandle    = m_chunkSkeletonWorldBoundsHandle,
                shaderBoundsHandle                = m_skeletonShaderBoundsOffsetHandle,
                lastSystemVersion                 = lastSystemVersion
            }.ScheduleParallel(m_optimizedSkeletonsQuery, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct ExposedBoneBoundsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<BoneBounds>                                     boneBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>                                   ltwHandle;
            [ReadOnly] public ComponentTypeHandle<BoneWorldBounds>                                boneWorldBoundsReadOnlyHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<BoneWorldBounds> boneWorldBoundsHandle;
            public ComponentTypeHandle<ChunkBoneWorldBounds>                                      chunkBoneWorldBoundsHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsUpdate  = chunk.DidChange(boneBoundsHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidChange(ltwHandle, lastSystemVersion);
                if (!needsUpdate && chunk.DidOrderChange(lastSystemVersion))
                {
                    //A structural change happened but no component of concern changed, meaning we need to recalculate the chunk component but nothing else (rare).
                    var worldBoundsRO = chunk.GetNativeArray(boneWorldBoundsReadOnlyHandle);
                    var aabb          = worldBoundsRO[0].bounds;
                    for (int i = 1; i < chunk.Count; i++)
                        aabb =
                            Physics.CombineAabb(aabb, worldBoundsRO[i].bounds);
                    chunk.SetChunkComponentData(chunkBoneWorldBoundsHandle, new ChunkBoneWorldBounds { chunkBounds = FromAabb(aabb) });
                    return;
                }
                if (!needsUpdate)
                    return;
                var boneBounds  = chunk.GetNativeArray(boneBoundsHandle);
                var ltws        = chunk.GetNativeArray(ltwHandle);
                var worldBounds = chunk.GetNativeArray(boneWorldBoundsHandle);

                Aabb chunkBounds = ComputeBounds(boneBounds[0].radialOffsetInBoneSpace, ltws[0].Value);
                worldBounds[0]   = new BoneWorldBounds { bounds = chunkBounds };
                for (int i = 1; i < chunk.Count; i++)
                {
                    var newBounds   = ComputeBounds(boneBounds[i].radialOffsetInBoneSpace, ltws[i].Value);
                    newBounds.min  -= boneBounds[i].radialOffsetInWorldSpace;
                    newBounds.max  += boneBounds[i].radialOffsetInWorldSpace;
                    worldBounds[i]  = new BoneWorldBounds { bounds = newBounds };
                    chunkBounds     = Physics.CombineAabb(chunkBounds, newBounds);
                }

                chunk.SetChunkComponentData(chunkBoneWorldBoundsHandle, new ChunkBoneWorldBounds { chunkBounds = FromAabb(chunkBounds) });
            }

            Aabb ComputeBounds(float radius, float4x4 ltw)
            {
                float3 extents = LatiosMath.RotateExtents(radius, ltw.c0.xyz, ltw.c1.xyz, ltw.c2.xyz);
                return new Aabb(ltw.c3.xyz - extents, ltw.c3.xyz + extents);
            }
        }

        [BurstCompile]
        struct OptimizedBoneBoundsJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<OptimizedBoneBounds>                                   boneBoundsHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneToRoot>                                   boneToRootHandle;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>                                       ltwHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonShaderBoundsOffset>                         shaderBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonWorldBounds>                                skeletonWorldBoundsReadOnlyHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<SkeletonWorldBounds> skeletonWorldBoundsHandle;
            public ComponentTypeHandle<ChunkSkeletonWorldBounds>                                      chunkSkeletonWorldBoundsHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsUpdate  = chunk.DidChange(boneBoundsHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidChange(boneToRootHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidChange(ltwHandle, lastSystemVersion);
                if (!needsUpdate && chunk.DidOrderChange(lastSystemVersion))
                {
                    //A structural change happened but no component of concern changed, meaning we need to recalculate the chunk component but nothing else (rare).
                    var worldBoundsRO = chunk.GetNativeArray(skeletonWorldBoundsReadOnlyHandle);
                    var aabb          = new Aabb( worldBoundsRO[0].bounds.Min, worldBoundsRO[0].bounds.Max);
                    for (int i = 1; i < chunk.Count; i++)
                        aabb =
                            Physics.CombineAabb(aabb, new Aabb(worldBoundsRO[i].bounds.Min, worldBoundsRO[i].bounds.Max));
                    chunk.SetChunkComponentData(chunkSkeletonWorldBoundsHandle, new ChunkSkeletonWorldBounds { chunkBounds = FromAabb(aabb) });
                    return;
                }
                if (!needsUpdate)
                    return;

                var boneBounds   = chunk.GetBufferAccessor(boneBoundsHandle);
                var boneToRoots  = chunk.GetBufferAccessor(boneToRootHandle);
                var ltws         = chunk.GetNativeArray(ltwHandle);
                var shaderBounds = chunk.GetNativeArray(shaderBoundsHandle);
                var worldBounds  = chunk.GetNativeArray(skeletonWorldBoundsHandle);

                Aabb chunkBounds = ComputeBounds(boneBounds[0], boneToRoots[0], ltws[0].Value);
                worldBounds[0]   = new SkeletonWorldBounds { bounds = FromAabb(chunkBounds) };
                for (int i = 1; i < chunk.Count; i++)
                {
                    var newBounds   = ComputeBounds(boneBounds[i], boneToRoots[i], ltws[i].Value);
                    newBounds.min  -= shaderBounds[i].radialBoundsInWorldSpace;
                    newBounds.max  += shaderBounds[i].radialBoundsInWorldSpace;
                    worldBounds[i]  = new SkeletonWorldBounds { bounds = FromAabb(newBounds) };
                    chunkBounds     = Physics.CombineAabb(chunkBounds, newBounds);
                }

                chunk.SetChunkComponentData(chunkSkeletonWorldBoundsHandle, new ChunkSkeletonWorldBounds { chunkBounds = FromAabb(chunkBounds) });
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

