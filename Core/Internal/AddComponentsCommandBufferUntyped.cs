using System;
using System.Diagnostics;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios
{
    [NativeContainer]
    [BurstCompile]
    internal unsafe struct AddComponentsCommandBufferUntyped : INativeDisposable
    {
        #region Structure
        [NativeDisableUnsafePtrRestriction]
        private UnsafeParallelBlockList* m_targetSortkeyBlockList;
        [NativeDisableUnsafePtrRestriction]
        private UnsafeParallelBlockList* m_componentDataBlockList;

        [NativeDisableUnsafePtrRestriction]
        private State* m_state;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        //Unfortunately this name is hardcoded into Unity. No idea how EntityCommandBuffer gets away with multiple safety handles.
        AtomicSafetyHandle m_Safety;

        static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<AddComponentsCommandBufferUntyped>();
#endif

        private struct State
        {
            public ComponentTypeSet                       tagsToAdd;
            public FixedList64Bytes<int>                  typesWithData;
            public FixedList64Bytes<int>                  typesSizes;
            public AddComponentsDestroyedEntityResolution destroyedEntityResolution;
            public AllocatorManager.AllocatorHandle       allocator;
            public bool                                   playedBack;
        }

        private struct TargetSortkey : IRadixSortableInt
        {
            public Entity target;
            public int    sortKey;

            public int GetKey()
            {
                return sortKey;
            }
        }
        #endregion

        #region CreateDestroy
        public AddComponentsCommandBufferUntyped(AllocatorManager.AllocatorHandle allocator, FixedList128Bytes<ComponentType> typesWithData,
                                                 AddComponentsDestroyedEntityResolution destroyedEntityResolution) : this(allocator, typesWithData, destroyedEntityResolution, 1)
        {
        }

        internal AddComponentsCommandBufferUntyped(AllocatorManager.AllocatorHandle allocator, FixedList128Bytes<ComponentType> componentTypesWithData,
                                                   AddComponentsDestroyedEntityResolution destroyedEntityResolution, int disposeSentinalStackDepth)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CheckAllocator(allocator);

            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
            CollectionHelper.SetStaticSafetyId<EntityOperationCommandBuffer>(ref m_Safety, ref s_staticSafetyId.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            int                   dataPayloadSize = 0;
            FixedList64Bytes<int> typesSizes      = new FixedList64Bytes<int>();
            FixedList64Bytes<int> typesWithData   = new FixedList64Bytes<int>();
            for (int i = 0; i < componentTypesWithData.Length; i++)
            {
                var size         = TypeManager.GetTypeInfo(componentTypesWithData[i].TypeIndex).ElementSize;
                dataPayloadSize += size;
                typesSizes.Add(size);
                typesWithData.Add(componentTypesWithData[i].TypeIndex);
            }
            {
                FixedList128Bytes<ComponentType> list = default;
                for (int i = 0; i < typesWithData.Length; i++)
                {
                    list.Add(ComponentType.FromTypeIndex(typesWithData[i]));
                }
                var set = new ComponentTypeSet(list);
                CheckComponentTypesValid(set);
            }
            m_targetSortkeyBlockList  = AllocatorManager.Allocate<UnsafeParallelBlockList>(allocator, 1);
            m_componentDataBlockList  = AllocatorManager.Allocate<UnsafeParallelBlockList>(allocator, 1);
            m_state                   = AllocatorManager.Allocate<State>(allocator, 1);
            *m_targetSortkeyBlockList = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<TargetSortkey>(), 256, allocator);
            *m_componentDataBlockList = new UnsafeParallelBlockList(dataPayloadSize, 256, allocator);
            *m_state                  = new State
            {
                typesWithData             = typesWithData,
                tagsToAdd                 = default,
                typesSizes                = typesSizes,
                destroyedEntityResolution = destroyedEntityResolution,
                allocator                 = allocator,
                playedBack                = false
            };
        }

        [BurstCompile]
        private struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public State* state;

            [NativeDisableUnsafePtrRestriction]
            public UnsafeParallelBlockList* targetSortkeyBlockList;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeParallelBlockList* componentDataBlockList;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public void Execute()
            {
                Deallocate(state, targetSortkeyBlockList, componentDataBlockList);
            }
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jobHandle = new DisposeJob
            {
                targetSortkeyBlockList = m_targetSortkeyBlockList,
                componentDataBlockList = m_componentDataBlockList,
                state                  = m_state,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = m_Safety
#endif
            }.Schedule(inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            m_state                  = null;
            m_componentDataBlockList = null;
            m_targetSortkeyBlockList = null;
            return jobHandle;
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif
            Deallocate(m_state, m_targetSortkeyBlockList, m_componentDataBlockList);
        }

        private static void Deallocate(State* state, UnsafeParallelBlockList* targetSortkeyBlockList, UnsafeParallelBlockList* componentDataBlockList)
        {
            var allocator = state->allocator;
            targetSortkeyBlockList->Dispose();
            componentDataBlockList->Dispose();
            AllocatorManager.Free(allocator, targetSortkeyBlockList, 1);
            AllocatorManager.Free(allocator, componentDataBlockList, 1);
            AllocatorManager.Free(allocator, state,                  1);
            //UnsafeUtility.Free(targetSortkeyBlockList, allocator);
            //UnsafeUtility.Free(componentDataBlockList, allocator);
            //UnsafeUtility.Free(state,                  allocator);
        }
        #endregion

        #region API
        [WriteAccessRequired]
        public void Add<T0>(Entity target, T0 c0, int sortKey = int.MaxValue) where T0 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(target);
            m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1>(Entity target, T0 c0, T1 c1, int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(target);
            m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            ptr += m_state->typesSizes[0];
            UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1, T2>(Entity target, T0 c0, T1 c1, T2 c2, int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(target);
            m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            ptr += m_state->typesSizes[0];
            UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
            ptr += m_state->typesSizes[1];
            UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1, T2, T3>(Entity target, T0 c0, T1 c1, T2 c2, T3 c3,
                                        int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(target);
            m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            ptr += m_state->typesSizes[0];
            UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
            ptr += m_state->typesSizes[1];
            UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
            ptr += m_state->typesSizes[2];
            UnsafeUtility.CopyStructureToPtr(ref c3, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1, T2, T3, T4>(Entity target, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4,
                                            int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(target);
            m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            ptr += m_state->typesSizes[0];
            UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
            ptr += m_state->typesSizes[1];
            UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
            ptr += m_state->typesSizes[2];
            UnsafeUtility.CopyStructureToPtr(ref c3, ptr);
            ptr += m_state->typesSizes[3];
            UnsafeUtility.CopyStructureToPtr(ref c4, ptr);
        }

        public int Count()
        {
            CheckReadAccess();
            return m_targetSortkeyBlockList->Count();
        }

        public void Playback(EntityManager entityManager)
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            Playbacker.Playback((AddComponentsCommandBufferUntyped*)UnsafeUtility.AddressOf(ref this), (EntityManager*)UnsafeUtility.AddressOf(ref entityManager));
        }

        public void SetTags(ComponentTypeSet types)
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            m_state->tagsToAdd = types;
        }

        public void AddTag(ComponentType type)
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            switch (m_state->tagsToAdd.Length)
            {
                case 0: m_state->tagsToAdd = new ComponentTypeSet(type); break;
                case 1: m_state->tagsToAdd = new ComponentTypeSet(m_state->tagsToAdd.GetComponentType(0), type); break;
                case 2: m_state->tagsToAdd = new ComponentTypeSet(m_state->tagsToAdd.GetComponentType(0), m_state->tagsToAdd.GetComponentType(1), type); break;
                case 3: m_state->tagsToAdd =
                    new ComponentTypeSet(m_state->tagsToAdd.GetComponentType(0), m_state->tagsToAdd.GetComponentType(1), m_state->tagsToAdd.GetComponentType(2), type); break;
                case 4: m_state->tagsToAdd =
                    new ComponentTypeSet(m_state->tagsToAdd.GetComponentType(0),
                                         m_state->tagsToAdd.GetComponentType(1),
                                         m_state->tagsToAdd.GetComponentType(2),
                                         m_state->tagsToAdd.GetComponentType(3),
                                         type); break;
                case var n when n >= 5 && n < 15:
                {
                    FixedList128Bytes <ComponentType> types = default;
                    for (int i = 0; i < m_state->tagsToAdd.Length; i++)
                        types.Add(m_state->tagsToAdd.GetComponentType(i));
                    types.Add(type);
                    m_state->tagsToAdd = new ComponentTypeSet(types);
                    break;
                }
                default: ThrowTooManyTags(); break;
            }
        }

        public ParallelWriter AsParallelWriter()
        {
            var writer = new ParallelWriter(m_targetSortkeyBlockList, m_componentDataBlockList, m_state);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            writer.m_Safety = m_Safety;
            CollectionHelper.SetStaticSafetyId<ParallelWriter>(ref writer.m_Safety, ref ParallelWriter.s_staticSafetyId.Data);
#endif
            return writer;
        }
        #endregion

        #region Implementation
        [BurstCompile]
        static class Playbacker
        {
            [BurstCompile]
            public static void Playback(AddComponentsCommandBufferUntyped* accb, EntityManager* em)
            {
                var chunkRanges       = new NativeList<int2>(Allocator.Temp);
                var chunks            = new NativeList<ArchetypeChunk>(Allocator.Temp);
                var indicesInChunks   = new NativeList<int>(Allocator.Temp);
                var componentDataPtrs = new NativeList<UnsafeIndexedBlockList.ElementPtr>(Allocator.Temp);
                var entitiesToDestroy = new NativeList<Entity>(Allocator.Temp);
                em->CompleteAllTrackedJobs();

                var job0 = new AddComponentsAndBuildListsJob
                {
                    accb              = *accb,
                    em                = *em,
                    chunks            = chunks,
                    chunkRanges       = chunkRanges,
                    indicesInChunks   = indicesInChunks,
                    componentDataPtrs = componentDataPtrs,
                    entitiesToDestroy = entitiesToDestroy
                };
                job0.Execute();

                var chunkJob = new WriteComponentDataJob
                {
                    accb              = *accb,
                    chunks            = chunks.AsArray(),
                    chunkRanges       = chunkRanges.AsArray(),
                    indicesInChunks   = indicesInChunks.AsArray(),
                    componentDataPtrs = componentDataPtrs.AsArray(),
                    entityHandle      = em->GetEntityTypeHandle(),
                    t0                = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(accb->m_state->typesWithData[0]))
                };
                if (accb->m_state->typesWithData.Length > 1)
                    chunkJob.t1 = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(accb->m_state->typesWithData[1]));
                if (accb->m_state->typesWithData.Length > 2)
                    chunkJob.t2 = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(accb->m_state->typesWithData[2]));
                if (accb->m_state->typesWithData.Length > 3)
                    chunkJob.t3 = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(accb->m_state->typesWithData[3]));
                if (accb->m_state->typesWithData.Length > 4)
                    chunkJob.t4 = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(accb->m_state->typesWithData[4]));
                //chunkJob.ScheduleParallel(chunks.Length, 1, default).Complete();
                for (int i = 0; i < chunks.Length; i++)
                    chunkJob.Execute(i);
                em->DestroyEntity(entitiesToDestroy.AsArray());
                accb->m_state->playedBack = true;
            }

            private struct AddComponentsAndBuildListsJob
            {
                [ReadOnly] public AddComponentsCommandBufferUntyped accb;
                public EntityManager                                em;

                public NativeList<ArchetypeChunk>                    chunks;
                public NativeList<int2>                              chunkRanges;
                public NativeList<int>                               indicesInChunks;
                public NativeList<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs;
                public NativeList<Entity>                            entitiesToDestroy;

                public void Execute()
                {
                    // Step 1: Get the targets and sort keys
                    int count              = accb.Count();
                    var targetSortkeyArray = new NativeArray<TargetSortkey>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    accb.m_targetSortkeyBlockList->GetElementValues(targetSortkeyArray);
                    // Step 2: Get the componentData pointers
                    var unsortedComponentDataPtrs = new NativeArray<UnsafeIndexedBlockList.ElementPtr>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    accb.m_componentDataBlockList->GetElementPtrs(unsortedComponentDataPtrs);
                    // Step 3: Sort the arrays by sort key, handling the destroyed entity resolution strategy
                    var ranks = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    RadixSort.RankSortInt(ranks, targetSortkeyArray);
                    var             sortedTargets = new NativeList<Entity>(count, Allocator.Temp);
                    NativeList<int> pruneIndices  = default;
                    if (accb.m_state->destroyedEntityResolution == AddComponentsDestroyedEntityResolution.DropData)
                        pruneIndices = new NativeList<int>(64, Allocator.Temp);
                    for (int i = 0; i < count; i++)
                    {
                        var entity = targetSortkeyArray[ranks[i]].target;
                        if (em.Exists(entity))
                            sortedTargets.Add(entity);
                        else if (accb.m_state->destroyedEntityResolution == AddComponentsDestroyedEntityResolution.DropData)
                            pruneIndices.Add(i);
                        else if (accb.m_state->destroyedEntityResolution == AddComponentsDestroyedEntityResolution.ThrowException)
                        {
                            ThrowDestroyed(entity);
                        }
                        else
                        {
                            var newEntity = em.CreateEntity();
                            sortedTargets.Add(newEntity);
                            entitiesToDestroy.Add(newEntity);
                        }
                    }
                    if (accb.m_state->destroyedEntityResolution == AddComponentsDestroyedEntityResolution.DropData && !pruneIndices.IsEmpty)
                    {
                        var newPtrs    = new NativeArray<UnsafeIndexedBlockList.ElementPtr>(count - pruneIndices.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                        int pruneIndex = 0;
                        for (int i = 0; i < count; i++)
                        {
                            if (i == pruneIndices[pruneIndex])
                                pruneIndex++;
                            else
                            {
                                newPtrs[i - pruneIndex] = unsortedComponentDataPtrs[ranks[i]];
                                ranks[i - pruneIndex]   = i - pruneIndex;
                            }
                        }
                        unsortedComponentDataPtrs = newPtrs;
                    }

                    // Step 4: Add components to the targets
                    if (accb.m_state->typesWithData.Length + accb.m_state->tagsToAdd.Length < 16)
                    {
                        FixedList128Bytes<ComponentType> combined = default;
                        for (int i = 0; i < accb.m_state->typesWithData.Length; i++)
                        {
                            combined.Add(ComponentType.FromTypeIndex(accb.m_state->typesWithData[i]));
                        }
                        for (int i = 0; i < accb.m_state->tagsToAdd.Length; i++)
                        {
                            combined.Add(ComponentType.FromTypeIndex(accb.m_state->tagsToAdd.GetTypeIndex(i)));
                        }
                        var set = new ComponentTypeSet(combined);
                        em.AddComponent(sortedTargets.AsArray(), in set);
                    }
                    else
                    {
                        FixedList128Bytes<ComponentType> list = default;
                        for (int i = 0; i < accb.m_state->typesWithData.Length; i++)
                        {
                            list.Add(ComponentType.FromTypeIndex(accb.m_state->typesWithData[i]));
                        }
                        var set = new ComponentTypeSet(list);
                        em.AddComponent(sortedTargets.AsArray(), accb.m_state->tagsToAdd);
                        em.AddComponent(sortedTargets.AsArray(), in set);
                    }
                    // Step 5: Get locations of new entities
                    var locations = new NativeArray<EntityStorageInfo>(count, Allocator.Temp);
                    for (int i = 0; i < count; i++)
                    {
                        locations[i] = em.GetStorageInfo(sortedTargets[i]);
                    }
                    // Step 6: Sort chunks and build final lists
                    var chunkRanks = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    RadixSort.RankSortInt3(chunkRanks, locations.Reinterpret<WrappedEntityLocationInChunk>());
                    chunks.Capacity      = count;
                    chunkRanges.Capacity = count;
                    indicesInChunks.ResizeUninitialized(count);
                    componentDataPtrs.ResizeUninitialized(count);
                    ArchetypeChunk lastChunk = default;
                    for (int i = 0; i < count; i++)
                    {
                        var loc              = locations[chunkRanks[i]];
                        indicesInChunks[i]   = loc.IndexInChunk;
                        componentDataPtrs[i] = unsortedComponentDataPtrs[ranks[chunkRanks[i]]];
                        if (loc.Chunk != lastChunk)
                        {
                            chunks.AddNoResize(loc.Chunk);
                            chunkRanges.AddNoResize(new int2(i, 1));
                            lastChunk = loc.Chunk;
                        }
                        else
                        {
                            ref var c = ref chunkRanges.ElementAt(chunkRanges.Length - 1);
                            c.y++;
                        }
                    }
                }

                struct WrappedEntityLocationInChunk : IRadixSortableInt3
                {
                    public EntityStorageInfo elic;

                    public int3 GetKey3()
                    {
                        var c = elic.Chunk.GetChunkIndexAsUint();
                        int x = 0;  // Todo: Optimize this.
                        int y = (int)(c & 0xFFFFFFFF);
                        int z = elic.IndexInChunk;
                        return new int3(x, y, z);
                    }
                }

                struct AliasChecker
                {
                    NativeHashSet<Entity> m_entities;

                    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
                    public void Init(int count)
                    {
                        m_entities = new NativeHashSet<Entity>(count, Allocator.Temp);
                    }

                    [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
                    public void AddAndCheck(Entity entity)
                    {
                        if (!m_entities.Add(entity))
                            throw new InvalidOperationException($"An entity {entity} was added to the same AddComponentsCommandBuffer multiple times.");
                    }
                }

                [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
                static void ThrowDestroyed(Entity entity)
                {
                    throw new InvalidOperationException($"An entity {entity} added to the AddComponentsCommandBuffer has already been destroyed.");
                }
            }

            private struct WriteComponentDataJob
            {
                [ReadOnly] public AddComponentsCommandBufferUntyped              accb;
                [ReadOnly] public NativeArray<ArchetypeChunk>                    chunks;
                [ReadOnly] public NativeArray<int2>                              chunkRanges;
                [ReadOnly] public NativeArray<int>                               indicesInChunks;
                [ReadOnly] public NativeArray<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs;
                [ReadOnly] public EntityTypeHandle                               entityHandle;
                public DynamicComponentTypeHandle                                t0;
                public DynamicComponentTypeHandle                                t1;
                public DynamicComponentTypeHandle                                t2;
                public DynamicComponentTypeHandle                                t3;
                public DynamicComponentTypeHandle                                t4;

                public void Execute(int i)
                {
                    var chunk   = chunks[i];
                    var range   = chunkRanges[i];
                    var indices = indicesInChunks.GetSubArray(range.x, range.y);
                    var ptrs    = componentDataPtrs.GetSubArray(range.x, range.y);
                    switch (accb.m_state->typesSizes.Length)
                    {
                        case 1: DoT0(chunk, indices, ptrs); return;
                        case 2: DoT1(chunk, indices, ptrs); return;
                        case 3: DoT2(chunk, indices, ptrs); return;
                        case 4: DoT3(chunk, indices, ptrs); return;
                        case 5: DoT4(chunk, indices, ptrs); return;
                    }
                }

                void DoT0(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeIndexedBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = accb.m_state->typesSizes[0];
                    var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size);
                    byte* t0Ptr    = (byte*)t0Array.GetUnsafePtr();
                    for (int i = 0; i < indices.Length; i++)
                    {
                        var index   = indices[i];
                        var dataPtr = dataPtrs[i].ptr;
                        UnsafeUtility.MemCpy(t0Ptr + index * t0Size, dataPtr, t0Size);
                    }
                }

                void DoT1(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeIndexedBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = accb.m_state->typesSizes[0];
                    var   t1Size   = accb.m_state->typesSizes[1];
                    var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size);
                    var   t1Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t1, t1Size);
                    byte* t0Ptr    = (byte*)t0Array.GetUnsafePtr();
                    byte* t1Ptr    = (byte*)t1Array.GetUnsafePtr();
                    for (int i = 0; i < indices.Length; i++)
                    {
                        var index   = indices[i];
                        var dataPtr = dataPtrs[i].ptr;
                        UnsafeUtility.MemCpy(t0Ptr + index * t0Size, dataPtr, t0Size);
                        dataPtr += t0Size;
                        UnsafeUtility.MemCpy(t1Ptr + index * t1Size, dataPtr, t1Size);
                    }
                }

                void DoT2(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeIndexedBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = accb.m_state->typesSizes[0];
                    var   t1Size   = accb.m_state->typesSizes[1];
                    var   t2Size   = accb.m_state->typesSizes[2];
                    var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size);
                    var   t1Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t1, t1Size);
                    var   t2Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t2, t2Size);
                    byte* t0Ptr    = (byte*)t0Array.GetUnsafePtr();
                    byte* t1Ptr    = (byte*)t1Array.GetUnsafePtr();
                    byte* t2Ptr    = (byte*)t2Array.GetUnsafePtr();

                    for (int i = 0; i < indices.Length; i++)
                    {
                        var index   = indices[i];
                        var dataPtr = dataPtrs[i].ptr;
                        UnsafeUtility.MemCpy(t0Ptr + index * t0Size, dataPtr, t0Size);
                        dataPtr += t0Size;
                        UnsafeUtility.MemCpy(t1Ptr + index * t1Size, dataPtr, t1Size);
                        dataPtr += t1Size;
                        UnsafeUtility.MemCpy(t2Ptr + index * t2Size, dataPtr, t2Size);
                    }
                }

                void DoT3(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeIndexedBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = accb.m_state->typesSizes[0];
                    var   t1Size   = accb.m_state->typesSizes[1];
                    var   t2Size   = accb.m_state->typesSizes[2];
                    var   t3Size   = accb.m_state->typesSizes[3];
                    var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size);
                    var   t1Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t1, t1Size);
                    var   t2Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t2, t2Size);
                    var   t3Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t3, t3Size);
                    byte* t0Ptr    = (byte*)t0Array.GetUnsafePtr();
                    byte* t1Ptr    = (byte*)t1Array.GetUnsafePtr();
                    byte* t2Ptr    = (byte*)t2Array.GetUnsafePtr();
                    byte* t3Ptr    = (byte*)t3Array.GetUnsafePtr();
                    for (int i = 0; i < indices.Length; i++)
                    {
                        var index   = indices[i];
                        var dataPtr = dataPtrs[i].ptr;
                        UnsafeUtility.MemCpy(t0Ptr + index * t0Size, dataPtr, t0Size);
                        dataPtr += t0Size;
                        UnsafeUtility.MemCpy(t1Ptr + index * t1Size, dataPtr, t1Size);
                        dataPtr += t1Size;
                        UnsafeUtility.MemCpy(t2Ptr + index * t2Size, dataPtr, t2Size);
                        dataPtr += t2Size;
                        UnsafeUtility.MemCpy(t3Ptr + index * t3Size, dataPtr, t3Size);
                    }
                }

                void DoT4(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeIndexedBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = accb.m_state->typesSizes[0];
                    var   t1Size   = accb.m_state->typesSizes[1];
                    var   t2Size   = accb.m_state->typesSizes[2];
                    var   t3Size   = accb.m_state->typesSizes[3];
                    var   t4Size   = accb.m_state->typesSizes[4];
                    var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size);
                    var   t1Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t1, t1Size);
                    var   t2Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t2, t2Size);
                    var   t3Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t3, t3Size);
                    var   t4Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t4, t4Size);
                    byte* t0Ptr    = (byte*)t0Array.GetUnsafePtr();
                    byte* t1Ptr    = (byte*)t1Array.GetUnsafePtr();
                    byte* t2Ptr    = (byte*)t2Array.GetUnsafePtr();
                    byte* t3Ptr    = (byte*)t3Array.GetUnsafePtr();
                    byte* t4Ptr    = (byte*)t4Array.GetUnsafePtr();
                    for (int i = 0; i < indices.Length; i++)
                    {
                        var index   = indices[i];
                        var dataPtr = dataPtrs[i].ptr;
                        UnsafeUtility.MemCpy(t0Ptr + index * t0Size, dataPtr, t0Size);
                        dataPtr += t0Size;
                        UnsafeUtility.MemCpy(t1Ptr + index * t1Size, dataPtr, t1Size);
                        dataPtr += t1Size;
                        UnsafeUtility.MemCpy(t2Ptr + index * t2Size, dataPtr, t2Size);
                        dataPtr += t2Size;
                        UnsafeUtility.MemCpy(t3Ptr + index * t3Size, dataPtr, t3Size);
                        dataPtr += t3Size;
                        UnsafeUtility.MemCpy(t4Ptr + index * t4Size, dataPtr, t4Size);
                    }
                }
            }
        }
        #endregion

        #region Checks
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckWriteAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckReadAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckComponentTypesValid(ComponentTypeSet types)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            for (int i = 0; i < types.Length; i++)
            {
                var t = types.GetComponentType(i);
                if (t.IsZeroSized)
                    throw new InvalidOperationException(
                        "AddComponentsCommandBuffer cannot be created with zero-sized component types. You can instead add such types using AddTagComponent() after creation.");
            }
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckEntityValid(Entity entity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (entity == Entity.Null)
                throw new InvalidOperationException("A null entity was added to the AddComponentsCommandBuffer. This is not currently supported.");
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckHasNotPlayedBack()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_state->playedBack)
                throw new InvalidOperationException(
                    "AddComponentsCommandBuffer has already been played back.");
#endif
        }
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void ThrowTooManyTags()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            throw new InvalidOperationException(
                "At least 15 tags have already been added and adding more is not supported.");
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckAllocator(AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (allocator.ToAllocator <= Allocator.None)
                throw new System.InvalidOperationException("Allocator cannot be Invalid or None");
#endif
        }
        #endregion

        #region ParallelWriter
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction]
            private UnsafeParallelBlockList* m_targetSortkeyBlockList;
            [NativeDisableUnsafePtrRestriction]
            private UnsafeParallelBlockList* m_componentDataBlockList;

            [NativeDisableUnsafePtrRestriction]
            private State* m_state;

            [NativeSetThreadIndex]
            private int m_ThreadIndex;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            //More ugly Unity naming
            internal AtomicSafetyHandle m_Safety;
            internal static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<ParallelWriter>();
#endif

            internal ParallelWriter(UnsafeParallelBlockList* targetSortkeyBlockList, UnsafeParallelBlockList* componentDataBlockList, void* state)
            {
                m_targetSortkeyBlockList = targetSortkeyBlockList;
                m_componentDataBlockList = componentDataBlockList;
                m_state                  = (State*)state;
                m_ThreadIndex            = 0;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = default;
#endif
            }

            public void Add<T0>(Entity target, T0 c0, int sortKey) where T0 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(target);
                m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            }
            public void Add<T0, T1>(Entity target, T0 c0, T1 c1, int sortKey) where T0 : unmanaged where T1 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(target);
                m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
                ptr += m_state->typesSizes[0];
                UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
            }
            public void Add<T0, T1, T2>(Entity target, T0 c0, T1 c1, T2 c2, int sortKey) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(target);
                m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
                ptr += m_state->typesSizes[0];
                UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
                ptr += m_state->typesSizes[1];
                UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
            }
            public void Add<T0, T1, T2, T3>(Entity target, T0 c0, T1 c1, T2 c2, T3 c3,
                                            int sortKey) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(target);
                m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
                ptr += m_state->typesSizes[0];
                UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
                ptr += m_state->typesSizes[1];
                UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
                ptr += m_state->typesSizes[2];
                UnsafeUtility.CopyStructureToPtr(ref c3, ptr);
            }
            public void Add<T0, T1, T2, T3, T4>(Entity target, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4,
                                                int sortKey) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(target);
                m_targetSortkeyBlockList->Write(new TargetSortkey { target = target, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
                ptr += m_state->typesSizes[0];
                UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
                ptr += m_state->typesSizes[1];
                UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
                ptr += m_state->typesSizes[2];
                UnsafeUtility.CopyStructureToPtr(ref c3, ptr);
                ptr += m_state->typesSizes[3];
                UnsafeUtility.CopyStructureToPtr(ref c4, ptr);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckWriteAccess()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckEntityValid(Entity entity)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (entity == Entity.Null)
                    throw new InvalidOperationException("A null entity was added to the AddComponentsCommandBuffer. This is not currently supported.");
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckHasNotPlayedBack()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (m_state->playedBack)
                    throw new InvalidOperationException(
                        "AddComponentsCommandBuffer has already been played back.");
#endif
            }
        }
        #endregion
    }
}

