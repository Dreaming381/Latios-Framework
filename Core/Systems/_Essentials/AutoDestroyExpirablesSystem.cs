using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

// The strategy for this system is to use an Any query to find all entities
// which should be kept alive, stored in a NativeParallelHashMap<ArchetypeChunk, v128>.
// Then, using a job that ignores enabled states to get all the candidate chunks,
// we use the map to filter out the entities we don't want to destroy.
// Note: If we had the ability to get all chunks from a query without filtering,
// we could replace the hash map with an array, which would be a lot faster.

namespace Latios.Systems
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(SyncPointPlaybackSystemDispatch))]
    [BurstCompile]
    public unsafe partial struct AutoDestroyExpirablesSystem : ISystem
    {
        private LatiosWorldUnmanaged m_latiosWorld;
        private EntityQuery          m_withAnyEnabledQuery;
        private EntityQuery          m_withAnyIgnoreComponentEnabledStatusQuery;

        public void OnCreate(ref SystemState state)
        {
            NativeList<ComponentType> candidateTypes = default;
            NativeList<ComponentType> expirableTypes = default;
            GetAllUnmangedEnableableComponentTypes(ref candidateTypes, ref expirableTypes, TypeManager.GetTypeCount());
            var expirableType = typeof(IAutoDestroyExpirable);
            foreach (var type in candidateTypes)
            {
                if (expirableType.IsAssignableFrom(type.GetManagedType()))
                    expirableTypes.Add(type);
            }

            OnCreateBurst(ref state, (AutoDestroyExpirablesSystem*)UnsafeUtility.AddressOf(ref this), ref expirableTypes);
        }

        [BurstCompile]
        static void GetAllUnmangedEnableableComponentTypes(ref NativeList<ComponentType> types, ref NativeList<ComponentType> allocateToCapacity, int typeCount)
        {
            types = new NativeList<ComponentType>(Allocator.Temp);

            for (int i = 0; i < typeCount; i++)
            {
                var typeIndex = TypeManager.GetTypeInfo(new TypeIndex { Value = i }).TypeIndex;
                if (!typeIndex.IsBakingOnlyType && !typeIndex.IsTemporaryBakingType && !typeIndex.IsManagedType && typeIndex.IsEnableable)
                {
                    types.Add(ComponentType.ReadOnly(typeIndex));
                }
            }
            allocateToCapacity = new NativeList<ComponentType>(types.Length, Allocator.Temp);
        }

        [BurstCompile]
        static void OnCreateBurst(ref SystemState state, AutoDestroyExpirablesSystem* thisPtr, ref NativeList<ComponentType> expirableTypes)
        {
            thisPtr->m_latiosWorld = state.GetLatiosWorldUnmanaged();
            thisPtr->m_latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new AutoDestroyExpirationJournal { });

            var builder                    = new EntityQueryBuilder(Allocator.Temp);
            thisPtr->m_withAnyEnabledQuery = builder.WithAny(ref expirableTypes).Build(ref state);
            builder.Reset();
            // The separate options here is enough to prevent the query from being treated the same as the previous
            thisPtr->m_withAnyIgnoreComponentEnabledStatusQuery = builder.WithAny(ref expirableTypes).WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(ref state);

            // For some reason, using Any enableable types don't get registered as dependencies. This is a workaround.
            foreach (var type in expirableTypes)
                state.GetDynamicComponentTypeHandle(type);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var chunkCount = m_withAnyEnabledQuery.CalculateChunkCountWithoutFiltering();
            if (chunkCount == 0)
            {
                m_latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new AutoDestroyExpirationJournal());
                return;
            }

            var chunkMasksMap           = new NativeParallelHashMap<ArchetypeChunk, v128>(chunkCount, state.WorldUpdateAllocator);
            var destroyCommandBuffer    = m_latiosWorld.syncPoint.CreateDestroyCommandBuffer().AsParallelWriter();
            var destroyedEntitiesStream = new NativeStream(chunkCount, state.WorldUpdateAllocator);
            var removedFromLegStream    = new NativeStream(chunkCount, state.WorldUpdateAllocator);

            BuildChunkMasksJob buildJob = new BuildChunkMasksJob
            {
                chunkMasksMap = chunkMasksMap.AsParallelWriter(),
            };
            var buildDependency = buildJob.ScheduleParallel(m_withAnyEnabledQuery, state.Dependency);

            DestroyJob destroyJob = new DestroyJob
            {
                dcb                     = destroyCommandBuffer,
                chunkMasksMap           = chunkMasksMap,
                entityHandle            = GetEntityTypeHandle(),
                legHandle               = GetBufferTypeHandle<LinkedEntityGroup>(false),
                esil                    = GetEntityStorageInfoLookup(),
                destroyedEntitiesStream = destroyedEntitiesStream.AsWriter(),
                removedFromLegStream    = removedFromLegStream.AsWriter()
            };
            state.Dependency = destroyJob.ScheduleParallel(m_withAnyIgnoreComponentEnabledStatusQuery, buildDependency);

            m_latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new AutoDestroyExpirationJournal
            {
                destroyedEntitiesStream            = destroyedEntitiesStream,
                removedFromLinkedEntityGroupStream = removedFromLegStream,
            });
        }

        [BurstCompile]
        public struct BuildChunkMasksJob : IJobChunk
        {
            public NativeParallelHashMap<ArchetypeChunk, v128>.ParallelWriter chunkMasksMap;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (Hint.Unlikely(useEnabledMask))
                    chunkMasksMap.TryAdd(chunk, chunkEnabledMask);
                else
                {
                    // All the entities in the chunk need to be kept alive. Write out all ones, which we'll use to early out later.
                    ulong ones = ~0ul;
                    chunkMasksMap.TryAdd(chunk, new v128(ones, ones));
                }
            }
        }

        [BurstCompile]
        public struct DestroyJob : IJobChunk
        {
            [ReadOnly] public NativeParallelHashMap<ArchetypeChunk, v128> chunkMasksMap;
            [ReadOnly] public EntityTypeHandle                            entityHandle;
            [ReadOnly] public EntityStorageInfoLookup                     esil;

            public BufferTypeHandle<LinkedEntityGroup> legHandle;
            public DestroyCommandBuffer.ParallelWriter dcb;
            public NativeStream.Writer                 destroyedEntitiesStream;
            public NativeStream.Writer                 removedFromLegStream;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                destroyedEntitiesStream.BeginForEachIndex(unfilteredChunkIndex);
                removedFromLegStream.BeginForEachIndex(unfilteredChunkIndex);
                ulong ones     = ~0ul;
                var   entities = chunk.GetNativeArray(entityHandle);
                if (Hint.Unlikely(!chunkMasksMap.TryGetValue(chunk, out var enabledMask)))
                {
                    if (!chunk.Has(ref legHandle))
                    {
                        // If the chunk isn't in the map, then all entities in the chunk are expired.
                        for (int i = 0; i < entities.Length; i++)
                        {
                            dcb.Add(entities[i], unfilteredChunkIndex);
                            destroyedEntitiesStream.Write(entities[i]);
                        }
                    }
                    else
                    {
                        var legs = chunk.GetBufferAccessor(ref legHandle);
                        for (int i = 0; i < entities.Length; i++)
                        {
                            RemoveExpirablesFromLinkedEntityGroup(legs[i]);
                            dcb.Add(entities[i], unfilteredChunkIndex);
                            destroyedEntitiesStream.Write(entities[i]);
                        }
                    }
                }
                else if (Hint.Likely(chunkEnabledMask.ULong0 == ones && chunkEnabledMask.ULong1 == ones))
                {
                    // All the entities in the chunk need to be kept alive. Early out.
                    return;
                }
                else if (!chunk.Has(ref legHandle))
                {
                    BitField64 enabledMask1 = new BitField64(~enabledMask.ULong0);
                    BitField64 enabledMask2 = new BitField64(~enabledMask.ULong1);
                    // Otherwise, check each entity individually
                    DestroyUnsetEntities(enabledMask1, ref entities, unfilteredChunkIndex, 0);
                    DestroyUnsetEntities(enabledMask2, ref entities, unfilteredChunkIndex, 64);
                }
                else
                {
                    var        legs         = chunk.GetBufferAccessor(ref legHandle);
                    BitField64 enabledMask1 = new BitField64(~enabledMask.ULong0);
                    BitField64 enabledMask2 = new BitField64(~enabledMask.ULong1);
                    DestroyUnsetEntities(enabledMask1, ref entities, unfilteredChunkIndex, 0,  legs);
                    DestroyUnsetEntities(enabledMask2, ref entities, unfilteredChunkIndex, 64, legs);
                }
                destroyedEntitiesStream.EndForEachIndex();
                removedFromLegStream.EndForEachIndex();
            }

            void DestroyUnsetEntities(BitField64 disabledBitMask, ref NativeArray<Entity> entities, int unfilteredChunkIndex, int offset)
            {
                var tzcnt     = disabledBitMask.CountTrailingZeros();
                var threshold = math.min(64, entities.Length - offset);
                while (tzcnt < threshold)
                {
                    dcb.Add(entities[tzcnt + offset], unfilteredChunkIndex);
                    destroyedEntitiesStream.Write(entities[tzcnt + offset]);
                    disabledBitMask.SetBits(tzcnt, false);
                    tzcnt = disabledBitMask.CountTrailingZeros();
                }
            }

            void DestroyUnsetEntities(BitField64 disabledBitMask, ref NativeArray<Entity> entities, int unfilteredChunkIndex, int offset,
                                      BufferAccessor<LinkedEntityGroup> accessor)
            {
                var tzcnt     = disabledBitMask.CountTrailingZeros();
                var threshold = math.min(64, entities.Length - offset);
                while (tzcnt < threshold)
                {
                    RemoveExpirablesFromLinkedEntityGroup(accessor[tzcnt + offset]);
                    dcb.Add(entities[tzcnt + offset], unfilteredChunkIndex);
                    destroyedEntitiesStream.Write(entities[tzcnt + offset]);
                    disabledBitMask.SetBits(tzcnt, false);
                    tzcnt = disabledBitMask.CountTrailingZeros();
                }
            }

            void RemoveExpirablesFromLinkedEntityGroup(DynamicBuffer<LinkedEntityGroup> linkedEntities)
            {
                if (linkedEntities.IsEmpty)
                    return;
                var legOwner = linkedEntities[0].Value;
                for (int i = 1; i < linkedEntities.Length; i++)
                {
                    var linked = linkedEntities[i].Value;
                    var info   = esil[linked];
                    if (chunkMasksMap.TryGetValue(info.Chunk, out var mask))
                    {
                        if (info.IndexInChunk >= 64 && new BitField64(mask.ULong1).IsSet(info.IndexInChunk - 64))
                        {
                            linkedEntities.RemoveAtSwapBack(i);
                            removedFromLegStream.Write(new AutoDestroyExpirationJournal.RemovedFromLinkedEntityGroup
                            {
                                entityRemoved          = linked,
                                linkedEntityGroupOwner = legOwner
                            });
                            i--;
                        }
                        else if (new BitField64(mask.ULong0).IsSet(info.IndexInChunk))
                        {
                            linkedEntities.RemoveAtSwapBack(i);
                            removedFromLegStream.Write(new AutoDestroyExpirationJournal.RemovedFromLinkedEntityGroup
                            {
                                entityRemoved          = linked,
                                linkedEntityGroupOwner = legOwner
                            });
                            i--;
                        }
                    }
                }
            }
        }
    }
}

