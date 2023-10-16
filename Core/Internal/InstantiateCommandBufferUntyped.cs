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
    internal unsafe struct InstantiateCommandBufferUntyped : INativeDisposable
    {
        #region Structure
        [NativeDisableUnsafePtrRestriction]
        private UnsafeParallelBlockList* m_prefabSortkeyBlockList;
        [NativeDisableUnsafePtrRestriction]
        private UnsafeParallelBlockList* m_componentDataBlockList;

        [NativeDisableUnsafePtrRestriction]
        private State* m_state;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        //Unfortunately this name is hardcoded into Unity. No idea how EntityCommandBuffer gets away with multiple safety handles.
        AtomicSafetyHandle m_Safety;

        static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<InstantiateCommandBufferUntyped>();
#endif

        private struct State
        {
            public ComponentTypeSet                 tagsToAdd;
            public FixedList64Bytes<int>            typesWithData;
            public FixedList64Bytes<int>            typesSizes;
            public AllocatorManager.AllocatorHandle allocator;
            public bool                             playedBack;
        }

        private struct PrefabSortkey : IRadixSortableInt3
        {
            public Entity prefab;
            public int    sortKey;

            public int3 GetKey3()
            {
                return new int3(prefab.Index, prefab.Version, sortKey);
            }
        }
        #endregion

        #region CreateDestroy
        public InstantiateCommandBufferUntyped(AllocatorManager.AllocatorHandle allocator, FixedList64Bytes<ComponentType> typesWithData) : this(allocator, typesWithData, 1)
        {
        }

        internal InstantiateCommandBufferUntyped(AllocatorManager.AllocatorHandle allocator, FixedList64Bytes<ComponentType> componentTypesWithData, int disposeSentinalStackDepth)
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
            CheckComponentTypesValid(BuildComponentTypesFromFixedList(typesWithData));
            m_prefabSortkeyBlockList  = AllocatorManager.Allocate<UnsafeParallelBlockList>(allocator, 1);
            m_componentDataBlockList  = AllocatorManager.Allocate<UnsafeParallelBlockList>(allocator, 1);
            m_state                   = AllocatorManager.Allocate<State>(allocator, 1);
            *m_prefabSortkeyBlockList = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<PrefabSortkey>(), 256, allocator);
            *m_componentDataBlockList = new UnsafeParallelBlockList(dataPayloadSize, 256, allocator);
            *m_state                  = new State
            {
                typesWithData = typesWithData,
                tagsToAdd     = default,
                typesSizes    = typesSizes,
                allocator     = allocator,
                playedBack    = false
            };
        }

        [BurstCompile]
        private struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public State* state;

            [NativeDisableUnsafePtrRestriction]
            public UnsafeParallelBlockList* prefabSortkeyBlockList;
            [NativeDisableUnsafePtrRestriction]
            public UnsafeParallelBlockList* componentDataBlockList;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public void Execute()
            {
                Deallocate(state, prefabSortkeyBlockList, componentDataBlockList);
            }
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jobHandle = new DisposeJob
            {
                prefabSortkeyBlockList = m_prefabSortkeyBlockList,
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
            m_prefabSortkeyBlockList = null;
            return jobHandle;
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif
            Deallocate(m_state, m_prefabSortkeyBlockList, m_componentDataBlockList);
        }

        private static void Deallocate(State* state, UnsafeParallelBlockList* prefabSortkeyBlockList, UnsafeParallelBlockList* componentDataBlockList)
        {
            var allocator = state->allocator;
            prefabSortkeyBlockList->Dispose();
            componentDataBlockList->Dispose();
            AllocatorManager.Free(allocator, prefabSortkeyBlockList, 1);
            AllocatorManager.Free(allocator, componentDataBlockList, 1);
            AllocatorManager.Free(allocator, state,                  1);
            //UnsafeUtility.Free(prefabSortkeyBlockList, allocator);
            //UnsafeUtility.Free(componentDataBlockList, allocator);
            //UnsafeUtility.Free(state,                  allocator);
        }
        #endregion

        #region API
        [WriteAccessRequired]
        public void Add<T0>(Entity prefab, T0 c0, int sortKey = int.MaxValue) where T0 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(prefab);
            m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1>(Entity prefab, T0 c0, T1 c1, int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(prefab);
            m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            ptr += m_state->typesSizes[0];
            UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1, T2>(Entity prefab, T0 c0, T1 c1, T2 c2, int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(prefab);
            m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            ptr += m_state->typesSizes[0];
            UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
            ptr += m_state->typesSizes[1];
            UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1, T2, T3>(Entity prefab, T0 c0, T1 c1, T2 c2, T3 c3,
                                        int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(prefab);
            m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, 0);
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
        public void Add<T0, T1, T2, T3, T4>(Entity prefab, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4,
                                            int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(prefab);
            m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, 0);
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
            return m_prefabSortkeyBlockList->Count();
        }

        public void Playback(EntityManager entityManager)
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            Playbacker.Playback((InstantiateCommandBufferUntyped*)UnsafeUtility.AddressOf(ref this), (EntityManager*)UnsafeUtility.AddressOf(ref entityManager));
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
            var writer = new ParallelWriter(m_prefabSortkeyBlockList, m_componentDataBlockList, m_state);
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
            public static void Playback(InstantiateCommandBufferUntyped* icb, EntityManager* em)
            {
                var chunkRanges       = new NativeList<int2>(Allocator.Temp);
                var chunks            = new NativeList<ArchetypeChunk>(Allocator.Temp);
                var indicesInChunks   = new NativeList<int>(Allocator.Temp);
                var componentDataPtrs = new NativeList<UnsafeParallelBlockList.ElementPtr>(Allocator.Temp);
                em->CompleteAllTrackedJobs();

                var job0 = new InstantiateAndBuildListsJob
                {
                    icb               = *icb,
                    em                = *em,
                    chunks            = chunks,
                    chunkRanges       = chunkRanges,
                    indicesInChunks   = indicesInChunks,
                    componentDataPtrs = componentDataPtrs
                };
                job0.Execute();

                var chunkJob = new WriteComponentDataJob
                {
                    icb               = *icb,
                    chunks            = chunks.AsArray(),
                    chunkRanges       = chunkRanges.AsArray(),
                    indicesInChunks   = indicesInChunks.AsArray(),
                    componentDataPtrs = componentDataPtrs.AsArray(),
                    entityHandle      = em->GetEntityTypeHandle(),
                    t0                = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb->m_state->typesWithData[0]))
                };
                if (icb->m_state->typesWithData.Length > 1)
                    chunkJob.t1 = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb->m_state->typesWithData[1]));
                if (icb->m_state->typesWithData.Length > 2)
                    chunkJob.t2 = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb->m_state->typesWithData[2]));
                if (icb->m_state->typesWithData.Length > 3)
                    chunkJob.t3 = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb->m_state->typesWithData[3]));
                if (icb->m_state->typesWithData.Length > 4)
                    chunkJob.t4 = em->GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb->m_state->typesWithData[4]));
                //chunkJob.ScheduleParallel(chunks.Length, 1, default).Complete();
                for (int i = 0; i < chunks.Length; i++)
                    chunkJob.Execute(i);
                icb->m_state->playedBack = true;
            }

            private struct InstantiateAndBuildListsJob
            {
                [ReadOnly] public InstantiateCommandBufferUntyped icb;
                public EntityManager                              em;

                public NativeList<ArchetypeChunk>                     chunks;
                public NativeList<int2>                               chunkRanges;
                public NativeList<int>                                indicesInChunks;
                public NativeList<UnsafeParallelBlockList.ElementPtr> componentDataPtrs;

                public void Execute()
                {
                    //Step 1: Get the prefabs and sort keys
                    int count              = icb.Count();
                    var prefabSortkeyArray = new NativeArray<PrefabSortkey>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    icb.m_prefabSortkeyBlockList->GetElementValues(prefabSortkeyArray);
                    //Step 2: Get the componentData pointers
                    var unsortedComponentDataPtrs = new NativeArray<UnsafeParallelBlockList.ElementPtr>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    icb.m_componentDataBlockList->GetElementPtrs(unsortedComponentDataPtrs);
                    //Step 3: Sort the arrays by sort key and collapse unique entities
                    var ranks = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    RadixSort.RankSortInt3(ranks, prefabSortkeyArray);
                    var    sortedPrefabs           = new NativeList<Entity>(count, Allocator.Temp);
                    var    sortedPrefabCounts      = new NativeList<int>(count, Allocator.Temp);
                    var    sortedComponentDataPtrs = new NativeArray<UnsafeParallelBlockList.ElementPtr>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    Entity lastEntity              = Entity.Null;
                    for (int i = 0; i < count; i++)
                    {
                        var entity                 = prefabSortkeyArray[ranks[i]].prefab;
                        sortedComponentDataPtrs[i] = unsortedComponentDataPtrs[ranks[i]];
                        if (entity != lastEntity)
                        {
                            sortedPrefabs.AddNoResize(entity);
                            sortedPrefabCounts.AddNoResize(1);
                            lastEntity = entity;
                        }
                        else
                        {
                            ref var c = ref sortedPrefabCounts.ElementAt(sortedPrefabCounts.Length - 1);
                            c++;
                        }
                    }
                    //Step 4: Instantiate the prefabs
                    var instantiatedEntities = new NativeArray<Entity>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    var typesWithDataToAdd   = BuildComponentTypesFromFixedList(icb.m_state->typesWithData);
                    int startIndex           = 0;
                    for (int i = 0; i < sortedPrefabs.Length; i++)
                    {
                        //var firstEntity = eet.Instantiate(sortedPrefabs[i]);
                        //eet.EntityManager.AddComponents(firstEntity, typesWithDataToAdd);
                        //eet.EntityManager.AddComponents(firstEntity, icb.m_state->tagsToAdd);
                        var firstEntity = em.Instantiate(sortedPrefabs[i]);
                        em.AddComponent(firstEntity, typesWithDataToAdd);
                        em.AddComponent(firstEntity, icb.m_state->tagsToAdd);
                        instantiatedEntities[startIndex] = firstEntity;
                        startIndex++;

                        if (sortedPrefabCounts[i] - 1 > 0)
                        {
                            var subArray = instantiatedEntities.GetSubArray(startIndex, sortedPrefabCounts[i] - 1);
                            //eet.Instantiate(firstEntity, subArray);
                            em.Instantiate(firstEntity, subArray);
                            startIndex += subArray.Length;
                        }
                    }
                    //Step 5: Get locations of new entities
                    var locations = new NativeArray<EntityStorageInfo>(count, Allocator.Temp);
                    for (int i = 0; i < count; i++)
                    {
                        //locations[i] = eet.EntityManager.GetEntityLocationInChunk(instantiatedEntities[i]);
                        locations[i] = em.GetStorageInfo(instantiatedEntities[i]);
                    }
                    //Step 6: Sort chunks and build final lists
                    RadixSort.RankSortInt3(ranks, locations.Reinterpret<WrappedEntityLocationInChunk>());
                    chunks.Capacity      = count;
                    chunkRanges.Capacity = count;
                    indicesInChunks.ResizeUninitialized(count);
                    componentDataPtrs.ResizeUninitialized(count);
                    ArchetypeChunk lastChunk = default;
                    for (int i = 0; i < count; i++)
                    {
                        var loc              = locations[ranks[i]];
                        indicesInChunks[i]   = loc.IndexInChunk;
                        componentDataPtrs[i] = sortedComponentDataPtrs[ranks[i]];
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
            }

            private struct WriteComponentDataJob
            {
                [ReadOnly] public InstantiateCommandBufferUntyped                 icb;
                [ReadOnly] public NativeArray<ArchetypeChunk>                     chunks;
                [ReadOnly] public NativeArray<int2>                               chunkRanges;
                [ReadOnly] public NativeArray<int>                                indicesInChunks;
                [ReadOnly] public NativeArray<UnsafeParallelBlockList.ElementPtr> componentDataPtrs;
                [ReadOnly] public EntityTypeHandle                                entityHandle;
                public DynamicComponentTypeHandle                                 t0;
                public DynamicComponentTypeHandle                                 t1;
                public DynamicComponentTypeHandle                                 t2;
                public DynamicComponentTypeHandle                                 t3;
                public DynamicComponentTypeHandle                                 t4;

                public void Execute(int i)
                {
                    var chunk   = chunks[i];
                    var range   = chunkRanges[i];
                    var indices = indicesInChunks.GetSubArray(range.x, range.y);
                    var ptrs    = componentDataPtrs.GetSubArray(range.x, range.y);
                    switch (icb.m_state->typesSizes.Length)
                    {
                        case 1: DoT0(chunk, indices, ptrs); return;
                        case 2: DoT1(chunk, indices, ptrs); return;
                        case 3: DoT2(chunk, indices, ptrs); return;
                        case 4: DoT3(chunk, indices, ptrs); return;
                        case 5: DoT4(chunk, indices, ptrs); return;
                    }
                }

                void DoT0(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeParallelBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = icb.m_state->typesSizes[0];
                    var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size);
                    byte* t0Ptr    = (byte*)t0Array.GetUnsafePtr();
                    for (int i = 0; i < indices.Length; i++)
                    {
                        var index   = indices[i];
                        var dataPtr = dataPtrs[i].ptr;
                        UnsafeUtility.MemCpy(t0Ptr + index * t0Size, dataPtr, t0Size);
                    }
                }

                void DoT1(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeParallelBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = icb.m_state->typesSizes[0];
                    var   t1Size   = icb.m_state->typesSizes[1];
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

                void DoT2(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeParallelBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = icb.m_state->typesSizes[0];
                    var   t1Size   = icb.m_state->typesSizes[1];
                    var   t2Size   = icb.m_state->typesSizes[2];
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

                void DoT3(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeParallelBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = icb.m_state->typesSizes[0];
                    var   t1Size   = icb.m_state->typesSizes[1];
                    var   t2Size   = icb.m_state->typesSizes[2];
                    var   t3Size   = icb.m_state->typesSizes[3];
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

                void DoT4(ArchetypeChunk chunk, NativeArray<int> indices, NativeArray<UnsafeParallelBlockList.ElementPtr> dataPtrs)
                {
                    var   entities = chunk.GetNativeArray(entityHandle);
                    var   t0Size   = icb.m_state->typesSizes[0];
                    var   t1Size   = icb.m_state->typesSizes[1];
                    var   t2Size   = icb.m_state->typesSizes[2];
                    var   t3Size   = icb.m_state->typesSizes[3];
                    var   t4Size   = icb.m_state->typesSizes[4];
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

        static ComponentTypeSet BuildComponentTypesFromFixedList(FixedList64Bytes<int> types)
        {
            switch (types.Length)
            {
                case 1: return new ComponentTypeSet(ComponentType.ReadWrite(types[0]));
                case 2: return new ComponentTypeSet(ComponentType.ReadWrite(types[0]), ComponentType.ReadWrite(types[1]));
                case 3: return new ComponentTypeSet(ComponentType.ReadWrite(types[0]), ComponentType.ReadWrite(types[1]), ComponentType.ReadWrite(types[2]));
                case 4: return new ComponentTypeSet(ComponentType.ReadWrite(types[0]), ComponentType.ReadWrite(types[1]), ComponentType.ReadWrite(types[2]),
                                                    ComponentType.ReadWrite(types[3]));
                case 5: return new ComponentTypeSet(ComponentType.ReadWrite(types[0]), ComponentType.ReadWrite(types[1]), ComponentType.ReadWrite(types[2]),
                                                    ComponentType.ReadWrite(types[3]), ComponentType.ReadWrite(types[4]));
                default: return default;
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
                        "InstantiateCommandBuffer cannot be created with zero-sized component types. You can instead add such types using AddTagComponent() after creation.");
            }
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckEntityValid(Entity entity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (entity == Entity.Null)
                throw new InvalidOperationException("A null entity was added to the InstantiateCommandBuffer. This is not currently supported.");
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckHasNotPlayedBack()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_state->playedBack)
                throw new InvalidOperationException(
                    "InstantiateCommandBuffer has already been played back.");
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
            private UnsafeParallelBlockList* m_prefabSortkeyBlockList;
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

            internal ParallelWriter(UnsafeParallelBlockList* prefabSortkeyBlockList, UnsafeParallelBlockList* componentDataBlockList, void* state)
            {
                m_prefabSortkeyBlockList = prefabSortkeyBlockList;
                m_componentDataBlockList = componentDataBlockList;
                m_state                  = (State*)state;
                m_ThreadIndex            = 0;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = default;
#endif
            }

            public void Add<T0>(Entity prefab, T0 c0, int sortKey) where T0 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(prefab);
                m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            }
            public void Add<T0, T1>(Entity prefab, T0 c0, T1 c1, int sortKey) where T0 : unmanaged where T1 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(prefab);
                m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
                ptr += m_state->typesSizes[0];
                UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
            }
            public void Add<T0, T1, T2>(Entity prefab, T0 c0, T1 c1, T2 c2, int sortKey) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(prefab);
                m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
                ptr += m_state->typesSizes[0];
                UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
                ptr += m_state->typesSizes[1];
                UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
            }
            public void Add<T0, T1, T2, T3>(Entity prefab, T0 c0, T1 c1, T2 c2, T3 c3,
                                            int sortKey) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(prefab);
                m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, m_ThreadIndex);
                byte* ptr                                                                    = (byte*)m_componentDataBlockList->Allocate(m_ThreadIndex);
                UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
                ptr += m_state->typesSizes[0];
                UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
                ptr += m_state->typesSizes[1];
                UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
                ptr += m_state->typesSizes[2];
                UnsafeUtility.CopyStructureToPtr(ref c3, ptr);
            }
            public void Add<T0, T1, T2, T3, T4>(Entity prefab, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4,
                                                int sortKey) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
            {
                CheckWriteAccess();
                CheckHasNotPlayedBack();
                CheckEntityValid(prefab);
                m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, m_ThreadIndex);
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
                    throw new InvalidOperationException("A null entity was added to the InstantiateCommandBuffer. This is not currently supported.");
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckHasNotPlayedBack()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (m_state->playedBack)
                    throw new InvalidOperationException(
                        "InstantiateCommandBuffer has already been played back.");
#endif
            }
        }
        #endregion
    }
}

