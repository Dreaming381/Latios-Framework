#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios.Psyshock;
using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateSkeletonBoundsSystem : ISystem
    {
        EntityQuery m_skeletonsQuery;
        EntityQuery m_exposedBonesQuery;
        EntityQuery m_optimizedSkeletonsQuery;

        public void OnCreate(ref SystemState state)
        {
            m_skeletonsQuery = state.Fluent().WithAll<DependentSkinnedMesh>(true).WithAll<SkeletonBoundsOffsetFromMeshes>(false).Build();

            m_exposedBonesQuery = state.Fluent().WithAll<BoneBounds>(true).WithAll<BoneWorldBounds>(false).WithAll<ChunkBoneWorldBounds>(false, true)
                                  .WithAll<WorldTransform>(                true).Build();

            m_optimizedSkeletonsQuery = state.Fluent().WithAll<OptimizedBoneBounds>(true).WithAll<OptimizedBoneTransform>(false).WithAll<OptimizedSkeletonWorldBounds>(false)
                                        .WithAll<OptimizedSkeletonState>(        false).WithAll<ChunkOptimizedSkeletonWorldBounds>(false, true)
                                        .WithAll<SkeletonBoundsOffsetFromMeshes>(true).WithAll<WorldTransform>(true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var lastSystemVersion = state.LastSystemVersion;

            var skeletonsJh = new RadialBoundsJob
            {
                depsHandle        = GetBufferTypeHandle<DependentSkinnedMesh>(true),
                offsetHandle      = GetComponentTypeHandle<SkeletonBoundsOffsetFromMeshes>(false),
                lastSystemVersion = state.LastSystemVersion
            }.ScheduleParallel(m_skeletonsQuery, state.Dependency);

            // Todo: Increase batches per chunk?
            // Right now we are instead leveraging parallelism of other jobs.
            var optimizedJh = new OptimizedBoneBoundsJob
            {
                boneBoundsHandle                  = GetBufferTypeHandle<OptimizedBoneBounds>(true),
                boneTransformHandle               = GetBufferTypeHandle<OptimizedBoneTransform>(true),
                stateHandle                       = GetComponentTypeHandle<OptimizedSkeletonState>(true),
                worldTransformHandle              = GetComponentTypeHandle<WorldTransform>(true),
                skeletonWorldBoundsReadOnlyHandle = GetComponentTypeHandle<OptimizedSkeletonWorldBounds>(true),
                skeletonWorldBoundsHandle         = GetComponentTypeHandle<OptimizedSkeletonWorldBounds>(false),
                chunkSkeletonWorldBoundsHandle    = GetComponentTypeHandle<ChunkOptimizedSkeletonWorldBounds>(false),
                meshBoundsHandle                  = GetComponentTypeHandle<SkeletonBoundsOffsetFromMeshes>(false),
                lastSystemVersion                 = lastSystemVersion
            }.ScheduleParallel(m_optimizedSkeletonsQuery, skeletonsJh);

            var exposedBonesJh = new ExposedBoneBoundsJob
            {
                boneBoundsHandle              = GetComponentTypeHandle<BoneBounds>(true),
                worldTransformHandle          = GetComponentTypeHandle<WorldTransform>(true),
                boneWorldBoundsReadOnlyHandle = GetComponentTypeHandle<BoneWorldBounds>(true),
                boneWorldBoundsHandle         = GetComponentTypeHandle<BoneWorldBounds>(false),
                chunkBoneWorldBoundsHandle    = GetComponentTypeHandle<ChunkBoneWorldBounds>(false),
                lastSystemVersion             = lastSystemVersion
            }.ScheduleParallel(m_exposedBonesQuery, state.Dependency);

            state.Dependency = JobHandle.CombineDependencies(optimizedJh, exposedBonesJh);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct RadialBoundsJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh>   depsHandle;
            public ComponentTypeHandle<SkeletonBoundsOffsetFromMeshes> offsetHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.DidChange(ref depsHandle, lastSystemVersion))
                {
                    var depsAccessor = chunk.GetBufferAccessor(ref depsHandle);
                    var offsets      = chunk.GetNativeArray(ref offsetHandle).Reinterpret<float>();

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var   deps = depsAccessor[i].AsNativeArray();
                        float temp = 0f;
                        foreach (var d in deps)
                            temp   += d.meshRadialOffset;
                        offsets[i]  = temp;
                    }
                }
            }
        }

        [BurstCompile]
        struct ExposedBoneBoundsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<BoneBounds>                                     boneBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<WorldTransform>                                 worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<BoneWorldBounds>                                boneWorldBoundsReadOnlyHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<BoneWorldBounds> boneWorldBoundsHandle;
            public ComponentTypeHandle<ChunkBoneWorldBounds>                                      chunkBoneWorldBoundsHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsUpdate  = chunk.DidChange(ref boneBoundsHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidChange(ref worldTransformHandle, lastSystemVersion);
                if (!needsUpdate && chunk.DidOrderChange(lastSystemVersion))
                {
                    //A structural change happened but no component of concern changed, meaning we need to recalculate the chunk component but nothing else (rare).
                    var worldBoundsRO = chunk.GetNativeArray(ref boneWorldBoundsReadOnlyHandle);
                    var aabb          = worldBoundsRO[0].bounds;
                    for (int i = 1; i < chunk.Count; i++)
                        aabb =
                            Physics.CombineAabb(aabb, worldBoundsRO[i].bounds);
                    chunk.SetChunkComponentData(ref chunkBoneWorldBoundsHandle, new ChunkBoneWorldBounds { chunkBounds = FromAabb(aabb) });
                    return;
                }
                if (!needsUpdate)
                    return;
                var boneBounds      = chunk.GetNativeArray(ref boneBoundsHandle);
                var worldTransforms = chunk.GetNativeArray(ref worldTransformHandle);
                var worldBounds     = chunk.GetNativeArray(ref boneWorldBoundsHandle);

                Aabb chunkBounds = ComputeBounds(boneBounds[0].radialOffsetInBoneSpace, worldTransforms[0].worldTransform);
                worldBounds[0]   = new BoneWorldBounds { bounds = chunkBounds };
                for (int i = 1; i < chunk.Count; i++)
                {
                    var newBounds  = ComputeBounds(boneBounds[i].radialOffsetInBoneSpace, worldTransforms[i].worldTransform);
                    worldBounds[i] = new BoneWorldBounds { bounds = newBounds };
                    chunkBounds    = Physics.CombineAabb(chunkBounds, newBounds);
                }

                chunk.SetChunkComponentData(ref chunkBoneWorldBoundsHandle, new ChunkBoneWorldBounds { chunkBounds = FromAabb(chunkBounds) });
            }
        }

        [BurstCompile]
        struct OptimizedBoneBoundsJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<OptimizedBoneBounds>                                            boneBoundsHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneTransform>                                         boneTransformHandle;
            [ReadOnly] public ComponentTypeHandle<OptimizedSkeletonState>                                      stateHandle;
            [ReadOnly] public ComponentTypeHandle<WorldTransform>                                              worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonBoundsOffsetFromMeshes>                              meshBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<OptimizedSkeletonWorldBounds>                                skeletonWorldBoundsReadOnlyHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<OptimizedSkeletonWorldBounds> skeletonWorldBoundsHandle;
            public ComponentTypeHandle<ChunkOptimizedSkeletonWorldBounds>                                      chunkSkeletonWorldBoundsHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsUpdate  = chunk.DidChange(ref boneBoundsHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidChange(ref boneTransformHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidChange(ref worldTransformHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidChange(ref meshBoundsHandle, lastSystemVersion);
                if (!needsUpdate && chunk.DidOrderChange(lastSystemVersion))
                {
                    //A structural change happened but no component of concern changed, meaning we need to recalculate the chunk component but nothing else (rare).
                    var worldBoundsRO = chunk.GetNativeArray(ref skeletonWorldBoundsReadOnlyHandle);
                    var aabb          = new Aabb( worldBoundsRO[0].bounds.Min, worldBoundsRO[0].bounds.Max);
                    for (int i = 1; i < chunk.Count; i++)
                        aabb =
                            Physics.CombineAabb(aabb, new Aabb(worldBoundsRO[i].bounds.Min, worldBoundsRO[i].bounds.Max));
                    chunk.SetChunkComponentData(ref chunkSkeletonWorldBoundsHandle, new ChunkOptimizedSkeletonWorldBounds { chunkBounds = FromAabb(aabb) });
                    return;
                }
                if (!needsUpdate)
                    return;

                var boneBounds      = chunk.GetBufferAccessor(ref boneBoundsHandle);
                var boneTransforms  = chunk.GetBufferAccessor(ref boneTransformHandle);
                var states          = chunk.GetNativeArray(ref stateHandle);
                var worldTransforms = chunk.GetNativeArray(ref worldTransformHandle);
                var shaderBounds    = chunk.GetNativeArray(ref meshBoundsHandle);
                var worldBounds     = chunk.GetNativeArray(ref skeletonWorldBoundsHandle);

                Aabb chunkBounds = ComputeBufferBounds(boneBounds[0], boneTransforms[0], worldTransforms[0].worldTransform, states[0]);
                worldBounds[0]   = new OptimizedSkeletonWorldBounds { bounds = FromAabb(chunkBounds) };
                for (int i = 1; i < chunk.Count; i++)
                {
                    var newBounds   = ComputeBufferBounds(boneBounds[i], boneTransforms[i], worldTransforms[i].worldTransform, states[i]);
                    newBounds.min  -= shaderBounds[i].radialBoundsInWorldSpace;
                    newBounds.max  += shaderBounds[i].radialBoundsInWorldSpace;
                    worldBounds[i]  = new OptimizedSkeletonWorldBounds { bounds = FromAabb(newBounds) };
                    chunkBounds     = Physics.CombineAabb(chunkBounds, newBounds);
                }

                chunk.SetChunkComponentData(ref chunkSkeletonWorldBoundsHandle, new ChunkOptimizedSkeletonWorldBounds { chunkBounds = FromAabb(chunkBounds) });
            }

            Aabb ComputeBufferBounds(DynamicBuffer<OptimizedBoneBounds>    bounds,
                                     DynamicBuffer<OptimizedBoneTransform> boneTransforms,
                                     in TransformQvvs worldTransform,
                                     OptimizedSkeletonState state)
            {
                var boundsArray             = bounds.Reinterpret<float>().AsNativeArray();
                var boneTransformsArrayFull = boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray();
                var mask                    = state.state & OptimizedSkeletonState.Flags.RotationMask;
                var currentRotation         = OptimizedSkeletonState.CurrentFromMask[(byte)mask];
                var previousRotation        = OptimizedSkeletonState.PreviousFromMask[(byte)mask];
                currentRotation             = (state.state & OptimizedSkeletonState.Flags.IsDirty) == OptimizedSkeletonState.Flags.IsDirty ? currentRotation : previousRotation;
                var boneTransformsArray     = boneTransformsArrayFull.GetSubArray(boundsArray.Length * 2 * currentRotation, boundsArray.Length);

                // Todo: Assert that buffers are not empty?
                var aabb = new Aabb(float.MaxValue, float.MinValue);
                for (int i = 0; i < boundsArray.Length; i++)
                {
                    aabb = Physics.CombineAabb(aabb, ComputeBounds(boundsArray[i], qvvs.mul(worldTransform, boneTransformsArray[i])));
                }
                return aabb;
            }
        }

        static AABB FromAabb(Aabb aabb)
        {
            Physics.GetCenterExtents(aabb, out float3 center, out float3 extents);
            return new AABB { Center = center, Extents = extents };
        }

        static Aabb ComputeBounds(float radius, in TransformQvvs worldTransform)
        {
            var aabb = new Aabb(-radius, radius);
            return Physics.TransformAabb(in worldTransform, in aabb);
        }
    }
}
#endif

