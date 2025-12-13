#region Header
using Latios.Transforms;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
#endregion

namespace Latios.Kinemation.Systems
{
    public partial class LatiosEntitiesGraphicsSystem
    {
        [BurstCompile]
        internal struct ClassifyNewChunksJobLatiosVersion : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               ChunkHeader;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
            [ReadOnly] public EntityQueryMask                                chunkValidityMask;

            [NativeDisableParallelForRestriction]
            public NativeArray<ArchetypeChunk> NewChunks;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> NumNewChunks;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var chunkHeaders               = metaChunk.GetNativeArray(ref ChunkHeader);
                var entitiesGraphicsChunkInfos = metaChunk.GetNativeArray(ref EntitiesGraphicsChunkInfo);

                for (int i = 0, chunkEntityCount = metaChunk.Count; i < chunkEntityCount; i++)
                {
                    var chunkInfo   = entitiesGraphicsChunkInfos[i];
                    var chunkHeader = chunkHeaders[i];

                    if (ShouldCountAsNewChunk(chunkInfo, chunkHeader.ArchetypeChunk))
                    {
                        bool skip = false;
                        ValidateChunkArchetype(chunkHeader.ArchetypeChunk, ref skip);
                        if (skip)
                            continue;
                        ClassifyNewChunk(chunkHeader.ArchetypeChunk);
                    }
                }
            }

            [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void ValidateChunkArchetype(ArchetypeChunk chunk, ref bool skip)
            {
                if (!chunkValidityMask.MatchesIgnoreFilter(chunk))
                {
                    FixedString4096Bytes badArchetype  = default;
                    var                  badComponents = chunk.Archetype.GetComponentTypes();
                    foreach (var bad in badComponents)
                    {
                        var name = bad.ToFixedString();
                        if (badArchetype.Length + name.Length > 4000)
                        {
                            for (int i = 0; i < 3; i++)
                                badArchetype.Append('.');
                            break;
                        }
                        badArchetype.Append(name);
                        badArchetype.Append('\n');
                    }
                    UnityEngine.Debug.LogError(
                        $"An invalid archetype was detected in a renderable entity with the EntitiesGraphicsChunkInfo chunk component. The most common cause for this is attempting to use Unity Physics with QVVS Transforms. Invalid archetype: {badArchetype}");
                }
            }

            bool ShouldCountAsNewChunk(in EntitiesGraphicsChunkInfo chunkInfo, in ArchetypeChunk chunk)
            {
                return !chunkInfo.Valid && !chunk.Archetype.Prefab && !chunk.Archetype.Disabled;
            }

            public unsafe void ClassifyNewChunk(ArchetypeChunk chunk)
            {
                int* numNewChunks = (int*)NumNewChunks.GetUnsafePtr();
                int  iPlus1       = System.Threading.Interlocked.Add(ref numNewChunks[0], 1);
                int  i            = iPlus1 - 1;  // C# Interlocked semantics are weird
                Assert.IsTrue(i < NewChunks.Length, "Out of space in the NewChunks buffer");
                NewChunks[i] = chunk;
            }
        }

        [BurstCompile]
        internal struct UpdateOldEntitiesGraphicsChunksJob : IJobChunk
        {
            public ComponentTypeHandle<EntitiesGraphicsChunkInfo>   EntitiesGraphicsChunkInfo;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>      ChunkHeader;
            [ReadOnly] public DynamicComponentTypeHandle            WorldTransform;
            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo> MaterialMeshInfo;
            public EntitiesGraphicsChunkUpdater                     EntitiesGraphicsChunkUpdater;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                // metaChunk is the chunk which contains the meta entities (= entities holding the chunk components) for the actual chunks

                var entitiesGraphicsChunkInfos = metaChunk.GetNativeArray(ref EntitiesGraphicsChunkInfo);
                var chunkHeaders               = metaChunk.GetNativeArray(ref ChunkHeader);

                for (int i = 0, chunkEntityCount = metaChunk.Count; i < chunkEntityCount; i++)
                {
                    var chunkInfo   = entitiesGraphicsChunkInfos[i];
                    var chunkHeader = chunkHeaders[i];
                    var chunk       = chunkHeader.ArchetypeChunk;

                    // Skip chunks that for some reason have EntitiesGraphicsChunkInfo, but don't have the
                    // other required components. This should normally not happen, but can happen
                    // if the user manually deletes some components after the fact.
                    bool hasMaterialMeshInfo = chunk.Has(ref MaterialMeshInfo);
                    bool hasWorldTransform   = chunk.Has(ref WorldTransform);

                    if (!math.all(new bool2(hasMaterialMeshInfo, hasWorldTransform)))
                        continue;

                    // When LOD ranges change, we must reset the movement grace to avoid using stale data
                    //bool lodRangeChange =
                    //    chunkHeader.ArchetypeChunk.DidOrderChange(EntitiesGraphicsChunkUpdater.lastSystemVersion) |
                    //    chunkHeader.ArchetypeChunk.DidChange(ref LodRange, EntitiesGraphicsChunkUpdater.lastSystemVersion) |
                    //    chunkHeader.ArchetypeChunk.DidChange(ref RootLodRange, EntitiesGraphicsChunkUpdater.lastSystemVersion);
                    //
                    //if (lodRangeChange)
                    //{
                    //    chunkInfo.CullingData.MovementGraceFixed16 = 0;
                    //    entitiesGraphicsChunkInfos[i]              = chunkInfo;
                    //}

                    EntitiesGraphicsChunkUpdater.ProcessChunk(in chunkInfo, in chunk);
                }
            }
        }

        [BurstCompile]
        internal struct UpdateNewEntitiesGraphicsChunksJob : IJobParallelFor
        {
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;

            public NativeArray<ArchetypeChunk>  NewChunks;
            public EntitiesGraphicsChunkUpdater EntitiesGraphicsChunkUpdater;

            public void Execute(int index)
            {
                var chunk     = NewChunks[index];
                var chunkInfo = chunk.GetChunkComponentData(ref EntitiesGraphicsChunkInfo);

                Assert.IsTrue(chunkInfo.Valid, "Attempted to process a chunk with uninitialized Hybrid chunk info");
                EntitiesGraphicsChunkUpdater.ProcessValidChunk(in chunkInfo, in chunk, true);
            }
        }

#if !LATIOS_TRANSFORMS_UNITY
        [BurstCompile]
        internal unsafe struct UpdateDrawCommandFlagsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<WorldTransform>             WorldTransform;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>          PostProcessMatrix;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> RenderFilterSettings;
            public ComponentTypeHandle<EntitiesGraphicsChunkInfo>             EntitiesGraphicsChunkInfo;

            [ReadOnly] public NativeParallelHashMap<int, BatchFilterSettings> FilterSettings;
            public BatchFilterSettings                                        DefaultFilterSettings;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool hasPostProcess  = chunk.Has(ref PostProcessMatrix);
                var  changed         = chunk.DidChange(ref WorldTransform, lastSystemVersion);
                changed             |= chunk.DidOrderChange(lastSystemVersion);
                changed             |= hasPostProcess && chunk.DidChange(ref PostProcessMatrix, lastSystemVersion);
                if (!changed)
                    return;

                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var chunkInfo = chunk.GetChunkComponentData(ref EntitiesGraphicsChunkInfo);
                Assert.IsTrue(chunkInfo.Valid, "Attempted to process a chunk with uninitialized Hybrid chunk info");

                // This job runs for all chunks that have structural changes, so if different
                // RenderFilterSettings get set on entities, they should be picked up by
                // the order change filter.
                int filterIndex = chunk.GetSharedComponentIndex(RenderFilterSettings);
                if (!FilterSettings.TryGetValue(filterIndex, out var filterSettings))
                    filterSettings = DefaultFilterSettings;

                bool hasPerObjectMotion = filterSettings.motionMode != MotionVectorGenerationMode.Camera;
                if (hasPerObjectMotion)
                    chunkInfo.CullingData.Flags |= EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion;
                else
                    chunkInfo.CullingData.Flags &= unchecked ((byte)~EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion);

                var worldTransforms = chunk.GetNativeArray(ref WorldTransform);

                if (hasPostProcess)
                {
                    var postProcessTransforms = chunk.GetNativeArray(ref PostProcessMatrix);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        bool flippedWinding = RequiresFlippedWinding(worldTransforms[i], postProcessTransforms[i]);

                        int   qwordIndex = i / 64;
                        int   bitIndex   = i % 64;
                        ulong mask       = 1ul << bitIndex;

                        if (flippedWinding)
                            chunkInfo.CullingData.FlippedWinding[qwordIndex] |= mask;
                        else
                            chunkInfo.CullingData.FlippedWinding[qwordIndex] &= ~mask;
                    }
                }
                else
                {
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        bool flippedWinding = RequiresFlippedWinding(worldTransforms[i]);

                        int   qwordIndex = i / 64;
                        int   bitIndex   = i % 64;
                        ulong mask       = 1ul << bitIndex;

                        if (flippedWinding)
                            chunkInfo.CullingData.FlippedWinding[qwordIndex] |= mask;
                        else
                            chunkInfo.CullingData.FlippedWinding[qwordIndex] &= ~mask;
                    }
                }

                chunk.SetChunkComponentData(ref EntitiesGraphicsChunkInfo, chunkInfo);
            }

            private bool RequiresFlippedWinding(in WorldTransform worldTransform)
            {
                var isNegative = worldTransform.nonUniformScale < 0f;
                return (math.countbits(math.bitmask(new bool4(isNegative, false))) & 1) == 1;
            }

            private bool RequiresFlippedWinding(in WorldTransform worldTransform, in PostProcessMatrix postProcessMatrix)
            {
                var wt4x4  = worldTransform.worldTransform.ToMatrix4x4();
                var ppm4x4 = new float4x4(new float4(postProcessMatrix.postProcessMatrix.c0, 0f),
                                          new float4(postProcessMatrix.postProcessMatrix.c1, 0f),
                                          new float4(postProcessMatrix.postProcessMatrix.c2, 0f),
                                          new float4(postProcessMatrix.postProcessMatrix.c3, 1f));
                var product = math.mul(ppm4x4, wt4x4);
                return math.determinant(product) < 0f;
            }
        }
#elif LATIOS_TRANSFORMS_UNITY
        [BurstCompile]
        internal unsafe struct UpdateDrawCommandFlagsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> WorldTransform;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> RenderFilterSettings;
            public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;

            [ReadOnly] public NativeParallelHashMap<int, BatchFilterSettings> FilterSettings;
            public BatchFilterSettings DefaultFilterSettings;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var changed = chunk.DidChange(ref WorldTransform, lastSystemVersion);
                changed |= chunk.DidOrderChange(lastSystemVersion);
                if (!changed)
                    return;

                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var chunkInfo = chunk.GetChunkComponentData(ref EntitiesGraphicsChunkInfo);
                Assert.IsTrue(chunkInfo.Valid, "Attempted to process a chunk with uninitialized Hybrid chunk info");

                // This job runs for all chunks that have structural changes, so if different
                // RenderFilterSettings get set on entities, they should be picked up by
                // the order change filter.
                int filterIndex = chunk.GetSharedComponentIndex(RenderFilterSettings);
                if (!FilterSettings.TryGetValue(filterIndex, out var filterSettings))
                    filterSettings = DefaultFilterSettings;

                bool hasPerObjectMotion = filterSettings.motionMode != MotionVectorGenerationMode.Camera;
                if (hasPerObjectMotion)
                    chunkInfo.CullingData.Flags |= EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion;
                else
                    chunkInfo.CullingData.Flags &= unchecked ((byte)~EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion);

                var worldTransforms = chunk.GetNativeArray(ref WorldTransform);

                for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                {
                    bool flippedWinding = RequiresFlippedWinding(worldTransforms[i]);

                    int qwordIndex = i / 64;
                    int bitIndex   = i % 64;
                    ulong mask       = 1ul << bitIndex;

                    if (flippedWinding)
                        chunkInfo.CullingData.FlippedWinding[qwordIndex] |= mask;
                    else
                        chunkInfo.CullingData.FlippedWinding[qwordIndex] &= ~mask;
                }

                chunk.SetChunkComponentData(ref EntitiesGraphicsChunkInfo, chunkInfo);
            }

            private bool RequiresFlippedWinding(in LocalToWorld worldTransform)
            {
                return math.determinant(worldTransform.Value) < 0f;
            }
        }
#endif
    }
}

