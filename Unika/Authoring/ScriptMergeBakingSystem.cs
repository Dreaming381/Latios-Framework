using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Unika.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [BurstCompile]
    public partial struct ScriptMergeBakingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var count = QueryBuilder().WithAll<BakedScriptMetadata>().WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                        .Build().CalculateEntityCountWithoutFiltering();
            var hashmap = new NativeParallelMultiHashMap<Entity, Entity>(count, state.WorldUpdateAllocator);

            new FindJob { hashmap = hashmap.AsParallelWriter() }.ScheduleParallel();
            new MergeJob
            {
                hashmap        = hashmap,
                metadataLookup = GetComponentLookup<BakedScriptMetadata>(true),
                bytesLookup    = GetBufferLookup<BakedScriptByte>(true)
            }.ScheduleParallel();
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct FindJob : IJobEntity
        {
            public NativeParallelMultiHashMap<Entity, Entity>.ParallelWriter hashmap;

            public void Execute(Entity entity, [EntityIndexInQuery] int entityIndexInQuery, ref BakedScriptMetadata metadata)
            {
                hashmap.Add(metadata.scriptRef.m_entity, entity);
                if (metadata.scriptRef.m_cachedHeaderIndex < 0)
                    metadata.scriptRef.m_instanceId = entityIndexInQuery;
            }
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct MergeJob : IJobEntity
        {
            [ReadOnly] public NativeParallelMultiHashMap<Entity, Entity> hashmap;
            [ReadOnly] public ComponentLookup<BakedScriptMetadata>       metadataLookup;
            [ReadOnly] public BufferLookup<BakedScriptByte>              bytesLookup;

            UnsafeList<BakedData> combineTargetsCache;

            public unsafe void Execute(Entity entity, ref DynamicBuffer<UnikaScripts> scriptsBuffer)
            {
                if (!combineTargetsCache.IsCreated)
                    combineTargetsCache = new UnsafeList<BakedData>(64, Allocator.Temp);
                combineTargetsCache.Clear();

                if (!hashmap.TryGetFirstValue(entity, out var bakingEntity, out var iter))
                    return;

                do
                {
                    combineTargetsCache.Add(new BakedData
                    {
                        bytes    = bytesLookup[bakingEntity],
                        metadata = metadataLookup[bakingEntity]
                    });
                }
                while(hashmap.TryGetNextValue(out bakingEntity, ref iter));

                combineTargetsCache.Sort();
                scriptsBuffer.Clear();
                var scripts = scriptsBuffer.Reinterpret<ScriptHeader>();
                scripts.Add(new ScriptHeader
                {
                    bloomMask          = 0,
                    instanceCount      = combineTargetsCache.Length,
                    lastUsedInstanceId = combineTargetsCache.Length,
                });

                ulong combinedMask  = 0;
                int   currentIndex  = 0;
                int   bytesRequired = 0;
                foreach (var target in combineTargetsCache)
                {
                    if (target.metadata.scriptRef.m_cachedHeaderIndex >= 0 && target.metadata.scriptRef.m_cachedHeaderIndex != currentIndex)
                        throw new InvalidOperationException("Unika buffer merging was corrupted.");

                    var id = target.metadata.scriptRef.m_instanceId;
                    if (target.metadata.scriptRef.m_cachedHeaderIndex < 0)
                        id = currentIndex + 1;

                    short scriptType = (short)target.metadata.scriptType;
                    var   alignment  = ScriptTypeInfoManager.GetSizeAndAlignement(scriptType).y;
                    UnityEngine.Assertions.Assert.IsTrue(alignment <= UnsafeUtility.SizeOf<ScriptHeader>());
                    var offsetFromBase  = CollectionHelper.Align(bytesRequired, alignment);
                    bytesRequired       = offsetFromBase + target.bytes.Length;
                    var mask            = ScriptTypeInfoManager.GetBloomMask(scriptType);
                    combinedMask       |= mask;

                    scripts.Add(new ScriptHeader
                    {
                        bloomMask  = mask,
                        scriptType = scriptType,
                        instanceId = id,
                        byteOffset = offsetFromBase,
                        userByte   = target.metadata.userByte,
                        userFlagA  = target.metadata.userFlagA,
                        userFlagB  = target.metadata.userFlagB,
                    });

                    currentIndex++;
                }
                scripts.ElementAt(0).bloomMask = combinedMask;

                UnityEngine.Assertions.Assert.IsTrue((ulong)bytesRequired <= ScriptHeader.kMaxByteOffset);

                var elementsNeeded  = math.ceilpow2(combineTargetsCache.Length) - combineTargetsCache.Length;
                elementsNeeded     += CollectionHelper.Align(bytesRequired, UnsafeUtility.SizeOf<ScriptHeader>()) / UnsafeUtility.SizeOf<ScriptHeader>();
                scripts.Resize(scripts.Length + elementsNeeded, NativeArrayOptions.ClearMemory);

                currentIndex = 0;
                foreach (var s in scriptsBuffer.AllScripts(default))
                {
                    var bytes = combineTargetsCache[currentIndex].bytes;
                    UnsafeUtility.MemCpy(s.GetUnsafePtrAsBytePtr(), bytes.GetUnsafeReadOnlyPtr(), bytes.Length);
                    currentIndex++;
                }
            }

            struct BakedData : IComparable<BakedData>
            {
                public BakedScriptMetadata            metadata;
                public DynamicBuffer<BakedScriptByte> bytes;

                public int CompareTo(BakedData other)
                {
                    if (metadata.scriptRef.m_cachedHeaderIndex >= 0 && other.metadata.scriptRef.m_cachedHeaderIndex >= 0)
                        return metadata.scriptRef.m_cachedHeaderIndex.CompareTo(other.metadata.scriptRef.m_cachedHeaderIndex);
                    if (metadata.scriptRef.m_cachedHeaderIndex < 0 && other.metadata.scriptRef.m_cachedHeaderIndex < 0)
                        return metadata.scriptRef.m_instanceId.CompareTo(other.metadata.scriptRef.m_instanceId);
                    if (metadata.scriptRef.m_cachedHeaderIndex < 0)
                        return 1.CompareTo(0);
                    return 0.CompareTo(1);
                }
            }
        }
    }
}

