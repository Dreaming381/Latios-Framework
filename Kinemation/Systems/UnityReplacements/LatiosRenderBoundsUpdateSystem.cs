using Latios.Kinemation.InternalSourceGen;
using Latios.Psyshock;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    /// <summary>
    /// A system that updates the WorldRenderBounds for entities that have both a WorldTransform and RenderBounds component.
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [UpdateBefore(typeof(UpdateSceneBoundingVolumeFromRendererBounds))]  // UpdateSceneBoundingVolumeFromRendererBounds has an UpdateAfter dependency
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.EntitySceneOptimizations | WorldSystemFilterFlags.Editor)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LatiosRenderBoundsUpdateSystem : ISystem
    {
        EntityQuery                      m_WorldRenderBounds;
        WorldTransformReadOnlyTypeHandle m_worldTransformHandle;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_WorldRenderBounds = state.Fluent().With<ChunkWorldRenderBounds>(false, true).With<WorldRenderBounds>(false).With<RenderBounds>(true)
                                  .WithWorldTransformReadOnly().Build();

            m_worldTransformHandle = new WorldTransformReadOnlyTypeHandle(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_worldTransformHandle.Update(ref state);

            var boundsJob = new BoundsJob
            {
                renderBoundsHandle                           = GetComponentTypeHandle<RenderBounds>(true),
                worldTransformHandle                         = m_worldTransformHandle,
                postProcessMatrixHandle                      = GetComponentTypeHandle<PostProcessMatrix>(true),
                worldRenderBoundsHandle                      = GetComponentTypeHandle<WorldRenderBounds>(),
                chunkWorldRenderBoundsHandle                 = GetComponentTypeHandle<ChunkWorldRenderBounds>(),
                shaderEffectRadialBoundsHandle               = GetComponentTypeHandle<ShaderEffectRadialBounds>(true),
                copyDeformFromEntityHandle                   = GetComponentTypeHandle<CopyDeformFromEntity>(true),
                disableComputeTagHandle                      = GetComponentTypeHandle<DisableComputeShaderProcessingTag>(true),
                esil                                         = GetEntityStorageInfoLookup(),
                lastSystemVersion                            = state.LastSystemVersion,
                skeletonDependentHandle                      = GetComponentTypeHandle<SkeletonDependent>(true),
                skeletonWorldBoundsOffsetsFromPositionLookup = GetComponentLookup<SkeletonWorldBoundsOffsetsFromPosition>(true),
            };
            state.Dependency = boundsJob.ScheduleParallel(m_WorldRenderBounds, state.Dependency);
        }

        [BurstCompile]
        unsafe struct BoundsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<RenderBounds>                       renderBoundsHandle;
            [ReadOnly] public WorldTransformReadOnlyTypeHandle                        worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>                  postProcessMatrixHandle;
            [ReadOnly] public ComponentTypeHandle<ShaderEffectRadialBounds>           shaderEffectRadialBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>                  skeletonDependentHandle;
            [ReadOnly] public ComponentTypeHandle<CopyDeformFromEntity>               copyDeformFromEntityHandle;
            [ReadOnly] public ComponentTypeHandle<DisableComputeShaderProcessingTag>  disableComputeTagHandle;
            [ReadOnly] public ComponentLookup<SkeletonWorldBoundsOffsetsFromPosition> skeletonWorldBoundsOffsetsFromPositionLookup;
            [ReadOnly] public EntityStorageInfoLookup                                 esil;
            public ComponentTypeHandle<WorldRenderBounds>                             worldRenderBoundsHandle;
            public ComponentTypeHandle<ChunkWorldRenderBounds>                        chunkWorldRenderBoundsHandle;
            public uint                                                               lastSystemVersion;

            [NoAlias, NativeDisableUnsafePtrRestriction] RenderBounds*                           tempBoundsBuffer;
            [NoAlias, NativeDisableUnsafePtrRestriction] SkeletonWorldBoundsOffsetsFromPosition* tempSkeletonOffsetsBuffer;
            bool                                                                                 skeletonBufferFilled;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var worldTransformChanged = worldTransformHandle.DidChange(in chunk, lastSystemVersion);
                var renderBoundsChanged   = chunk.DidChange(ref renderBoundsHandle, lastSystemVersion);
                var skeletonDependents    = chunk.GetComponentDataPtrRO(ref skeletonDependentHandle);
                var copyDeforms           = chunk.GetComponentDataPtrRO(ref copyDeformFromEntityHandle);
                if (chunk.Has(ref disableComputeTagHandle))
                {
                    skeletonDependents = null;
                    copyDeforms        = null;
                }
                if (!worldTransformChanged && !renderBoundsChanged)
                {
                    bool requiresUpdate = false;
                    if (skeletonDependents != null)
                    {
                        var otherWorldTransformHandle = worldTransformHandle;
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            if (DidOtherTransformChange(ref otherWorldTransformHandle, skeletonDependents[i].root))
                            {
                                requiresUpdate = true;
                                break;
                            }
                        }
                    }
                    else if (copyDeforms != null)
                    {
                        var otherWorldTransformHandle = worldTransformHandle;
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            if (DidOtherDependentsTransformChange(ref otherWorldTransformHandle, copyDeforms[i].sourceDeformedEntity))
                            {
                                requiresUpdate = true;
                                break;
                            }
                        }
                    }
                    if (!requiresUpdate)
                    {
                        if (chunk.DidOrderChange(lastSystemVersion))
                        {
                            ComputeChunkAabbOnly(in chunk);
                        }
                        return;
                    }
                }

                var worldBounds     = (WorldRenderBounds*)chunk.GetRequiredComponentDataPtrRW(ref worldRenderBoundsHandle);
                var localBounds     = (RenderBounds*)chunk.GetRequiredComponentDataPtrRO(ref renderBoundsHandle);
                var worldTransforms = worldTransformHandle.Resolve(chunk);
                var shaderBounds    = chunk.GetComponentDataPtrRO(ref shaderEffectRadialBoundsHandle);

                if (shaderBounds != null)
                {
                    if (tempBoundsBuffer == null)
                        tempBoundsBuffer = (RenderBounds*)new NativeArray<RenderBounds>(128, Allocator.Temp, NativeArrayOptions.UninitializedMemory).GetUnsafePtr();
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var bounds            = localBounds[i];
                        bounds.Value.Extents += shaderBounds[i].radialBounds;
                        tempBoundsBuffer[i]   = bounds;
                    }
                    localBounds = tempBoundsBuffer;
                }

                if (tempSkeletonOffsetsBuffer == null)
                    tempSkeletonOffsetsBuffer = (SkeletonWorldBoundsOffsetsFromPosition*)new NativeArray<SkeletonWorldBoundsOffsetsFromPosition>(128,
                                                                                                                                                 Allocator.Temp,
                                                                                                                                                 NativeArrayOptions.ClearMemory).
                                                GetUnsafePtr();
                if (skeletonDependents != null || copyDeforms != null)
                {
                    if (skeletonDependents != null)
                    {
                        for (int i = 0; i < chunk.Count; i++)
                            tempSkeletonOffsetsBuffer[i] = skeletonWorldBoundsOffsetsFromPositionLookup[skeletonDependents[i].root];
                    }
                    else
                    {
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var dependentInfo = esil[copyDeforms[i].sourceDeformedEntity];
                            var dependents    = dependentInfo.Chunk.GetComponentDataPtrRO(ref skeletonDependentHandle);
                            if (dependents == null)
                                tempSkeletonOffsetsBuffer[i] = default; // We are missing the dependent, so ignore the offset.
                            tempSkeletonOffsetsBuffer[i]     = skeletonWorldBoundsOffsetsFromPositionLookup[dependents[dependentInfo.IndexInChunk].root];
                        }
                    }
                }
                else if (skeletonBufferFilled)
                {
                    UnsafeUtility.MemClear(tempSkeletonOffsetsBuffer, 128 * UnsafeUtility.SizeOf<SkeletonWorldBoundsOffsetsFromPosition>());
                    skeletonBufferFilled = false;
                }

                var chunkAabb = new Aabb(float.MaxValue, float.MinValue);

                if (chunk.Has(ref postProcessMatrixHandle))
                {
                    // Only applies to QVVS Transforms
                    var matrices = chunk.GetNativeArray(ref postProcessMatrixHandle);
                    for (int i = 0; i != chunk.Count; i++)
                    {
                        var worldAabb  = Physics.TransformAabb(worldTransforms[i].worldTransformQvvs, new Aabb(localBounds[i].Value.Min, localBounds[i].Value.Max));
                        worldAabb.min += tempSkeletonOffsetsBuffer[i].minOffset;
                        worldAabb.max += tempSkeletonOffsetsBuffer[i].maxOffset;
                        Physics.GetCenterExtents(worldAabb, out var center, out var extents);
                        var matrix = new float4x4(new float4(matrices[i].postProcessMatrix.c0, 0f),
                                                  new float4(matrices[i].postProcessMatrix.c1, 0f),
                                                  new float4(matrices[i].postProcessMatrix.c2, 0f),
                                                  new float4(matrices[i].postProcessMatrix.c3, 1f));
                        worldBounds[i] = new WorldRenderBounds { Value = AABB.Transform(matrix, new AABB { Center = center, Extents = extents } )};
                        chunkAabb                                                                                                   =
                            Physics.CombineAabb(chunkAabb, new Aabb(worldBounds[i].Value.Min, worldBounds[i].Value.Max));
                    }
                }
                else
                {
                    for (int i = 0; i != chunk.Count; i++)
                    {
                        var worldAabb  = Physics.TransformAabb(worldTransforms[i], new Aabb(localBounds[i].Value.Min, localBounds[i].Value.Max));
                        worldAabb.min += tempSkeletonOffsetsBuffer[i].minOffset;
                        worldAabb.max += tempSkeletonOffsetsBuffer[i].maxOffset;
                        chunkAabb      = Physics.CombineAabb(chunkAabb, worldAabb);
                        Physics.GetCenterExtents(worldAabb, out var center, out var extents);
                        worldBounds[i] = new WorldRenderBounds { Value = new AABB { Center = center, Extents = extents } };
                    }
                }
                {
                    Physics.GetCenterExtents(chunkAabb, out var center, out var extents);
                    chunk.SetChunkComponentData(ref chunkWorldRenderBoundsHandle, new ChunkWorldRenderBounds { Value = new AABB { Center = center, Extents = extents } });
                }
            }

            bool DidOtherTransformChange(ref WorldTransformReadOnlyTypeHandle handle, Entity entity)
            {
                return handle.DidChange(esil[entity].Chunk, lastSystemVersion);
            }

            bool DidOtherDependentsTransformChange(ref WorldTransformReadOnlyTypeHandle handle, Entity dependent)
            {
                var dependentInfo = esil[dependent];
                var dependents    = dependentInfo.Chunk.GetComponentDataPtrRO(ref skeletonDependentHandle);
                if (dependents == null)
                    return false; // We are missing the dependent, so just keep things as-is.
                return DidOtherDependentsTransformChange(ref handle, dependents[dependentInfo.IndexInChunk].root);
            }

            void ComputeChunkAabbOnly(in ArchetypeChunk chunk)
            {
                var bounds = chunk.GetComponentDataPtrRO(ref worldRenderBoundsHandle);
                var aabb = new Aabb(float.MaxValue, float.MinValue);
                for (int i = 0; i < chunk.Count; i++)
                    aabb = Physics.CombineAabb(aabb, new Aabb(bounds[i].Value.Min, bounds[i].Value.Max));
                Physics.GetCenterExtents(aabb, out var center, out var extents);
                chunk.SetChunkComponentData(ref chunkWorldRenderBoundsHandle, new ChunkWorldRenderBounds { Value = new AABB { Center = center, Extents = extents } });
            }
        }
    }

    // This system is here to fix a bug in Entities Graphics. However, in the future, it may be worth rewriting
    // so that deforming entities are included in the scene bounding volume.
    [WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    [UpdateAfter(typeof(LatiosRenderBoundsUpdateSystem))]
    [DisableAutoCreation]
    partial class LatiosUpdateSceneBoundingVolumeFromRendererBounds : SystemBase
    {
        [BurstCompile]
        struct CollectSceneBoundsJob : IJob
        {
            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<WorldRenderBounds> RenderBounds;

            public Entity                                            SceneBoundsEntity;
            public ComponentLookup<Unity.Scenes.SceneBoundingVolume> SceneBounds;

            public void Execute()
            {
                var minMaxAabb = MinMaxAABB.Empty;
                for (int i = 0; i != RenderBounds.Length; i++)
                {
                    var aabb = RenderBounds[i].Value;

                    // MUST BE FIXED BY DOTS-2518
                    //
                    // Avoid empty RenderBounds AABB because is means it hasn't been computed yet
                    // There are some unfortunate cases where RenderBoundsUpdateSystem is executed after this system
                    //  and a bad Scene AABB is computed if we consider these empty RenderBounds AABB.
                    if (math.lengthsq(aabb.Center) != 0.0f || math.lengthsq(aabb.Extents) != 0.0f)
                    // D381: I changed the line above from an && to an ||.
                    {
                        minMaxAabb.Encapsulate(aabb);
                    }
                }
                SceneBounds[SceneBoundsEntity] = new Unity.Scenes.SceneBoundingVolume { Value = minMaxAabb };
            }
        }

        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            //@TODO: API does not allow me to use ChunkComponentData.
            //Review with simon how we can improve it.

            var query = GetEntityQuery(typeof(WorldRenderBounds), typeof(SceneSection));

            EntityManager.GetAllUniqueSharedComponents<SceneSection>(out var sections, Allocator.Temp);
            foreach (var section in sections)
            {
                if (section.Equals(default(SceneSection)))
                    continue;

                query.SetSharedComponentFilter(section);

                var entity = EntityManager.CreateEntity(typeof(Unity.Scenes.SceneBoundingVolume));
                EntityManager.AddSharedComponent(entity, section);

                var job               = new CollectSceneBoundsJob();
                job.RenderBounds      = query.ToComponentDataArray<WorldRenderBounds>(Allocator.TempJob);
                job.SceneBoundsEntity = entity;
                job.SceneBounds       = GetComponentLookup<Unity.Scenes.SceneBoundingVolume>();
                job.Run();
            }

            query.ResetFilter();
        }
    }
}

