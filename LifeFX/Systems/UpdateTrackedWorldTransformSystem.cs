using System;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.LifeFX.Systems
{
    [UpdateInGroup(typeof(Kinemation.Systems.KinemationCustomGraphicsSetupSuperSystem), OrderFirst = true)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateTrackedWorldTransformSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery               m_query;
        NativeList<TransformQvvs> m_trackedTransforms;
        NativeList<Entity>        m_trackedEntities;
        NativeList<int>           m_freeList;

        WorldTransformReadOnlyTypeHandle m_worldTransformHandle;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_query = state.Fluent().With<TrackedWorldTransform>(false).WithWorldTransformReadOnly().Build();

            m_worldTransformHandle = new WorldTransformReadOnlyTypeHandle(ref state);

            m_trackedTransforms = new NativeList<TransformQvvs>(1024, Allocator.Persistent);
            m_trackedEntities   = new NativeList<Entity>(1024, Allocator.Persistent);
            m_freeList          = new NativeList<int>(128, Allocator.Persistent);

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new TrackedTransformUploadList());
        }

        public void OnDestroy(ref SystemState state)
        {
            if (state.EntityManager.Exists(latiosWorld.worldBlackboardEntity))
            {
                latiosWorld.GetCollectionComponent<TrackedTransformUploadList>(latiosWorld.worldBlackboardEntity, out var jh);
                jh.Complete();
            }
            state.CompleteDependency();
            m_trackedTransforms.Dispose();
            m_trackedEntities.Dispose();
            m_freeList.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var chunkCount    = m_query.CalculateChunkCountWithoutFiltering();
            var newChunks     = new NativeList<DeferredChunk>(chunkCount, state.WorldUpdateAllocator);
            var uploadIndices = new UnsafeParallelBlockList<int>(1024, state.WorldUpdateAllocator);
            var aliveByThread = CollectionHelper.CreateNativeArray<UnsafeBitArray>(JobsUtility.ThreadIndexCount, state.WorldUpdateAllocator, NativeArrayOptions.ClearMemory);
            int reapCapacity  = m_trackedEntities.Length;
            var reaped        = new NativeList<int>(reapCapacity, state.WorldUpdateAllocator);

            m_worldTransformHandle.Update(ref state);

            var jh = new UpdateJob
            {
                aliveByThread          = aliveByThread,
                allocator              = state.WorldUpdateAllocator,
                enabledFlagHandle      = GetComponentTypeHandle<TrackedWorldTransformEnableFlag>(true),
                entityHandle           = GetEntityTypeHandle(),
                lastSystemVersion      = state.LastSystemVersion,
                newChunks              = newChunks.AsParallelWriter(),
                trackedEntities        = m_trackedEntities.AsDeferredJobArray(),
                trackedTransformHandle = GetComponentTypeHandle<TrackedWorldTransform>(false),
                trackedTransforms      = m_trackedTransforms.AsDeferredJobArray(),
                uploadIndices          = uploadIndices,
                worldTransformHandle   = m_worldTransformHandle
            }.ScheduleParallel(m_query, state.Dependency);

            jh = new ReapJob
            {
                aliveByThread     = aliveByThread,
                reaped            = reaped.AsParallelWriter(),
                trackedEntities   = m_trackedEntities.AsDeferredJobArray(),
                trackedTransforms = m_trackedTransforms.AsDeferredJobArray(),
                uploadIndices     = uploadIndices,
            }.ScheduleParallel(reapCapacity, 256, jh);

            state.Dependency = new AllocateNewJob
            {
                entityHandle           = GetEntityTypeHandle(),
                freelist               = m_freeList,
                newChunks              = newChunks.AsDeferredJobArray(),
                reaped                 = reaped,
                trackedEntities        = m_trackedEntities,
                trackedTransforms      = m_trackedTransforms,
                trackedTransformHandle = GetComponentTypeHandle<TrackedWorldTransform>(false),
                uploadIndices          = uploadIndices,
                worldTransformHandle   = m_worldTransformHandle
            }.Schedule(jh);

            latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new TrackedTransformUploadList
            {
                trackedTransforms = m_trackedTransforms,
                uploadIndices     = uploadIndices
            });
        }

        struct DeferredChunk : IComparable<DeferredChunk>
        {
            public ArchetypeChunk chunk;
            public BitField64     lower;
            public BitField64     upper;
            public int            chunkIndexInQuery;

            public int CompareTo(DeferredChunk other) => chunkIndexInQuery.CompareTo(other.chunkIndexInQuery);
        }

        [BurstCompile]
        struct UpdateJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                                     entityHandle;
            [ReadOnly] public WorldTransformReadOnlyTypeHandle                     worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<TrackedWorldTransformEnableFlag> enabledFlagHandle;
            [ReadOnly] public NativeArray<Entity>                                  trackedEntities;

            public ComponentTypeHandle<TrackedWorldTransform>                        trackedTransformHandle;
            [NativeDisableParallelForRestriction] public NativeArray<TransformQvvs>  trackedTransforms;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeBitArray> aliveByThread;
            public NativeList<DeferredChunk>.ParallelWriter                          newChunks;
            public UnsafeParallelBlockList<int>                                      uploadIndices;
            public AllocatorManager.AllocatorHandle                                  allocator;
            public uint                                                              lastSystemVersion;

            [NativeSetThreadIndex]
            int threadIndex;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!aliveByThread[threadIndex].IsCreated)
                    aliveByThread[threadIndex] = new UnsafeBitArray(trackedEntities.Length, allocator, NativeArrayOptions.ClearMemory);

                var alive = aliveByThread[threadIndex];

                chunk.SetComponentEnabledForAll(ref trackedTransformHandle, false);

                bool changed =
                    worldTransformHandle.DidChange(in chunk, lastSystemVersion) || (chunk.Has(ref enabledFlagHandle) && chunk.DidChange(ref enabledFlagHandle, lastSystemVersion));
                if (!changed && !chunk.Has(ref enabledFlagHandle))
                {
                    // We only need to mark indices.
                    var indices = (int*)chunk.GetRequiredComponentDataPtrRO(ref trackedTransformHandle);
                    for (int i = 0; i < chunk.Count; i++)
                        alive.Set(indices[i], true);
                    return;
                }
                else if (!changed)
                {
                    // We need to mark indices, but need to watch out for yet-to-be-tracked entities.
                    var enabledMask = chunk.GetEnabledMask(ref enabledFlagHandle);
                    var entities    = chunk.GetEntityDataPtrRO(entityHandle);
                    var indices     = (int*)chunk.GetRequiredComponentDataPtrRO(ref trackedTransformHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (!enabledMask[i])
                        {
                            if (math.clamp(indices[i], 0, trackedEntities.Length) != indices[i] || trackedEntities[indices[i]] != entities[i])
                                continue;
                        }
                        alive.Set(indices[i], true);
                    }
                }
                else
                {
                    var enabledMask   = chunk.GetEnabledMask(ref enabledFlagHandle);
                    var entities      = chunk.GetEntityDataPtrRO(entityHandle);
                    var indices       = (int*)chunk.GetRequiredComponentDataPtrRO(ref trackedTransformHandle);
                    var transforms    = worldTransformHandle.Resolve(in chunk);
                    var deferredChunk = new DeferredChunk { chunk = chunk };
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (trackedEntities.Length == 0 || math.clamp(indices[i], 0, trackedEntities.Length - 1) != indices[i] || trackedEntities[indices[i]] != entities[i])
                        {
                            if (i < 64)
                                deferredChunk.lower.SetBits(i, true);
                            else
                                deferredChunk.upper.SetBits(i - 64, true);
                            continue;
                        }

                        alive.Set(indices[i], true);
                        var transform = transforms[i].worldTransformQvvs;
                        var mask      = math.select(2, 3, !enabledMask.EnableBit.IsValid || enabledMask[i]);
                        Bits.SetBits(ref transform.worldIndex, 30, 2, mask);
                        if (!AreQvvsEqual(in transform, trackedTransforms[indices[i]]))
                        {
                            trackedTransforms[indices[i]] = transform;
                            uploadIndices.Write(indices[i], threadIndex);
                        }
                    }
                    if ((deferredChunk.lower.Value | deferredChunk.upper.Value) != 0)
                        newChunks.AddNoResize(deferredChunk);
                }
            }

            static bool AreQvvsEqual(in TransformQvvs a, in TransformQvvs b)
            {
                var q = a.rotation.value == b.rotation.value;
                var v = new uint4(math.asuint(a.position), math.asuint(a.worldIndex)) == new uint4(math.asuint(b.position), math.asuint(b.worldIndex));
                var s = new float4(a.stretch, a.scale) == new float4(b.stretch, b.scale);
                return math.all(q & v & s);
            }
        }

        [BurstCompile]
        struct ReapJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<UnsafeBitArray>                               aliveByThread;
            public UnsafeParallelBlockList<int>                                         uploadIndices;
            [NativeDisableParallelForRestriction] public NativeArray<Entity>            trackedEntities;
            [NativeDisableParallelForRestriction] public NativeArray<TransformQvvs>     trackedTransforms;
            [NativeDisableParallelForRestriction] public NativeList<int>.ParallelWriter reaped;

            [NativeSetThreadIndex]
            int threadIndex;

            public unsafe void Execute(int startIndex, int count)
            {
                var         offset = startIndex / 64;
                var         stride = CollectionHelper.Align(count, 64) / 64;
                Span<ulong> bits   = stackalloc ulong[stride];
                bits.Clear();
                for (int threadIndex = 0; threadIndex < aliveByThread.Length; threadIndex++)
                {
                    if (!aliveByThread[threadIndex].IsCreated)
                        continue;
                    var ptr = aliveByThread[threadIndex].Ptr + offset;
                    for (int i = 0; i < stride; i++)
                        bits[i] |= ptr[i];
                }
                for (int i = 0; i < count; i++)
                {
                    if (Bits.GetBit(bits[i >> 6], i & 0x3f))
                        continue;

                    var dead = startIndex + i;
                    if (trackedEntities[dead] == default)
                        continue;

                    reaped.AddNoResize(dead);
                    uploadIndices.Write(dead, threadIndex);
                    trackedEntities[dead]    = default;
                    var transform            = trackedTransforms[dead];
                    transform.worldIndex    &= 0x7fffffff;
                    trackedTransforms[dead]  = transform;
                }
            }
        }

        [BurstCompile]
        struct AllocateNewJob : IJob
        {
            [ReadOnly] public EntityTypeHandle                 entityHandle;
            [ReadOnly] public WorldTransformReadOnlyTypeHandle worldTransformHandle;

            public ComponentTypeHandle<TrackedWorldTransform> trackedTransformHandle;
            public NativeList<Entity>                         trackedEntities;
            public NativeList<TransformQvvs>                  trackedTransforms;
            public NativeList<int>                            freelist;
            public NativeArray<DeferredChunk>                 newChunks;
            public UnsafeParallelBlockList<int>               uploadIndices;
            public NativeList<int>                            reaped;

            [NativeSetThreadIndex]
            int threadIndex;

            public unsafe void Execute()
            {
                // We care about ECS storage and persistent buffer storage determinism, but not upload determinism.
                newChunks.Sort();

                foreach (var chunk in newChunks)
                {
                    var entities   = chunk.chunk.GetEntityDataPtrRO(entityHandle);
                    var indices    = (int*)chunk.chunk.GetRequiredComponentDataPtrRW(ref trackedTransformHandle);
                    var mask       = chunk.chunk.GetEnabledMask(ref trackedTransformHandle);
                    var transforms = worldTransformHandle.Resolve(in chunk.chunk);

                    var enumerator = new ChunkEntityEnumerator(true, new v128(chunk.lower.Value, chunk.upper.Value), chunk.chunk.Count);
                    while (enumerator.NextEntityIndex(out var i))
                    {
                        mask[i] = true;

                        var transform = transforms[i].worldTransformQvvs;
                        var worldmask = 3;
                        Bits.SetBits(ref transform.worldIndex, 30, 2, worldmask);

                        if (freelist.IsEmpty)
                        {
                            indices[i] = trackedEntities.Length;
                            trackedEntities.Add(entities[i]);
                            trackedTransforms.Add(transform);
                        }
                        else
                        {
                            var dst = freelist[freelist.Length - 1];
                            freelist.Length--;
                            indices[i]             = dst;
                            trackedEntities[dst]   = entities[i];
                            trackedTransforms[dst] = transform;
                        }
                        uploadIndices.Write(indices[i], threadIndex);
                    }
                }

                reaped.Sort();
                freelist.AddRange(reaped.AsArray());
            }
        }
    }
}

