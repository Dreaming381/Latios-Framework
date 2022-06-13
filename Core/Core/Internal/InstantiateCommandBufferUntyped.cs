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

        [BurstDiscard]
        static void CreateStaticSafetyId()
        {
            s_staticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<InstantiateCommandBufferUntyped>();
        }

        [NativeSetClassTypeToNullOnSchedule]
        //Unfortunately this name is hardcoded into Unity.
        DisposeSentinel m_DisposeSentinel;
#endif

        private struct State
        {
            public ComponentTypes        tagsToAdd;
            public FixedList64Bytes<int> typesWithData;
            public FixedList64Bytes<int> typesSizes;
            public Allocator             allocator;
            public bool                  playedBack;
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
        public InstantiateCommandBufferUntyped(Allocator allocator, FixedList64Bytes<ComponentType> typesWithData) : this(allocator, typesWithData, 1)
        {
        }

        internal InstantiateCommandBufferUntyped(Allocator allocator, FixedList64Bytes<ComponentType> componentTypesWithData, int disposeSentinalStackDepth)
        {
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
            m_prefabSortkeyBlockList = (UnsafeParallelBlockList*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<UnsafeParallelBlockList>(),
                                                                                      UnsafeUtility.AlignOf<UnsafeParallelBlockList>(),
                                                                                      allocator);
            m_componentDataBlockList = (UnsafeParallelBlockList*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<UnsafeParallelBlockList>(),
                                                                                      UnsafeUtility.AlignOf<UnsafeParallelBlockList>(),
                                                                                      allocator);
            m_state                   = (State*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<State>(), UnsafeUtility.AlignOf<State>(), allocator);
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

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, disposeSentinalStackDepth, allocator);

            if (s_staticSafetyId.Data == 0)
            {
                CreateStaticSafetyId();
            }
            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, s_staticSafetyId.Data);
#endif
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

            public void Execute()
            {
                Deallocate(state, prefabSortkeyBlockList, componentDataBlockList);
            }
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // [DeallocateOnJobCompletion] is not supported, but we want the deallocation
            // to happen in a thread. DisposeSentinel needs to be cleared on main thread.
            // AtomicSafetyHandle can be destroyed after the job was scheduled (Job scheduling
            // will check that no jobs are writing to the container).
            DisposeSentinel.Clear(ref m_DisposeSentinel);
#endif
            var jobHandle = new DisposeJob
            {
                prefabSortkeyBlockList = m_prefabSortkeyBlockList,
                componentDataBlockList = m_componentDataBlockList,
                state                  = m_state
            }.Schedule(inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            return jobHandle;
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            Deallocate(m_state, m_prefabSortkeyBlockList, m_componentDataBlockList);
        }

        private static void Deallocate(State* state, UnsafeParallelBlockList* prefabSortkeyBlockList, UnsafeParallelBlockList* componentDataBlockList)
        {
            var allocator = state->allocator;
            prefabSortkeyBlockList->Dispose();
            componentDataBlockList->Dispose();
            UnsafeUtility.Free(prefabSortkeyBlockList, allocator);
            UnsafeUtility.Free(componentDataBlockList, allocator);
            UnsafeUtility.Free(state,                  allocator);
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
            var chunkRanges       = new NativeList<int2>(Allocator.TempJob);
            var chunks            = new NativeList<ArchetypeChunk>(Allocator.TempJob);
            var indicesInChunks   = new NativeList<int>(Allocator.TempJob);
            var componentDataPtrs = new NativeList<UnsafeParallelBlockList.ElementPtr>(Allocator.TempJob);
            entityManager.CompleteAllJobs();
            //var eet = entityManager.BeginExclusiveEntityTransaction();
            //Run job that instantiates entities and populates hashmap
            var job0 = new InstantiateAndBuildListsJob
            {
                icb = this,
                //eet               = eet,
                em                = entityManager,
                chunks            = chunks,
                chunkRanges       = chunkRanges,
                indicesInChunks   = indicesInChunks,
                componentDataPtrs = componentDataPtrs
            };
            job0.RunOrExecute();
            //entityManager.EndExclusiveEntityTransaction();
            //Schedule parallel job to populate data
            var chunkJob = new WriteComponentDataJob
            {
                icb               = this,
                chunks            = chunks,
                chunkRanges       = chunkRanges,
                indicesInChunks   = indicesInChunks,
                componentDataPtrs = componentDataPtrs,
                entityHandle      = entityManager.GetEntityTypeHandle(),
                t0                = entityManager.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(m_state->typesWithData[0]))
            };
            if (m_state->typesWithData.Length > 1)
                chunkJob.t1 = entityManager.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(m_state->typesWithData[1]));
            if (m_state->typesWithData.Length > 2)
                chunkJob.t2 = entityManager.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(m_state->typesWithData[2]));
            if (m_state->typesWithData.Length > 3)
                chunkJob.t3 = entityManager.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(m_state->typesWithData[3]));
            if (m_state->typesWithData.Length > 4)
                chunkJob.t4 = entityManager.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(m_state->typesWithData[4]));
            //The remaining types apparently need to be initialized. So set them to the dummy types.
            if (m_state->typesWithData.Length <= 1)
                chunkJob.t1 = entityManager.GetDynamicComponentTypeHandle(typeof(DummyTypeT1));
            if (m_state->typesWithData.Length <= 2)
                chunkJob.t2 = entityManager.GetDynamicComponentTypeHandle(typeof(DummyTypeT2));
            if (m_state->typesWithData.Length <= 3)
                chunkJob.t3 = entityManager.GetDynamicComponentTypeHandle(typeof(DummyTypeT3));
            if (m_state->typesWithData.Length <= 4)
                chunkJob.t4 = entityManager.GetDynamicComponentTypeHandle(typeof(DummyTypeT4));
            //chunkJob.ScheduleParallel(chunks.Length, 1, default).Complete();
            chunkJob.RunOrExecute(chunks.Length);
            m_state->playedBack = true;
            chunks.Dispose();
            chunkRanges.Dispose();
            indicesInChunks.Dispose();
            componentDataPtrs.Dispose();
        }

        public void SetTags(ComponentTypes types)
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
                case 0: m_state->tagsToAdd = new ComponentTypes(type); break;
                case 1: m_state->tagsToAdd = new ComponentTypes(m_state->tagsToAdd.GetComponentType(0), type); break;
                case 2: m_state->tagsToAdd = new ComponentTypes(m_state->tagsToAdd.GetComponentType(0), m_state->tagsToAdd.GetComponentType(1), type); break;
                case 3: m_state->tagsToAdd =
                    new ComponentTypes(m_state->tagsToAdd.GetComponentType(0), m_state->tagsToAdd.GetComponentType(1), m_state->tagsToAdd.GetComponentType(2), type); break;
                case 4: m_state->tagsToAdd =
                    new ComponentTypes(m_state->tagsToAdd.GetComponentType(0),
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
                    m_state->tagsToAdd = new ComponentTypes(types);
                    break;
                }
                default: ThrowTooManyTags(); break;
            }
        }

        public ParallelWriter AsParallelWriter()
        {
            var writer = new ParallelWriter(m_prefabSortkeyBlockList, m_componentDataBlockList, m_state);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            writer.m_Safety = m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref writer.m_Safety);
#endif
            return writer;
        }
        #endregion

        #region Implementation
        [BurstCompile]
        private struct InstantiateAndBuildListsJob : IJob
        {
            [ReadOnly] public InstantiateCommandBufferUntyped icb;
            //public ExclusiveEntityTransaction                     eet;
            public EntityManager em;

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
                    em.AddComponents(firstEntity, typesWithDataToAdd);
                    em.AddComponents(firstEntity, icb.m_state->tagsToAdd);
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
                var locations = new NativeArray<EntityLocationInChunk>(count, Allocator.Temp);
                for (int i = 0; i < count; i++)
                {
                    //locations[i] = eet.EntityManager.GetEntityLocationInChunk(instantiatedEntities[i]);
                    locations[i] = em.GetEntityLocationInChunk(instantiatedEntities[i]);
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
                    indicesInChunks[i]   = loc.indexInChunk;
                    componentDataPtrs[i] = sortedComponentDataPtrs[ranks[i]];
                    if (loc.chunk != lastChunk)
                    {
                        chunks.AddNoResize(loc.chunk);
                        chunkRanges.AddNoResize(new int2(i, 1));
                        lastChunk = loc.chunk;
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
                public EntityLocationInChunk elic;

                public int3 GetKey3()
                {
                    var c = elic.ChunkAddressAsUlong;
                    int x = (int)(c >> 32);
                    int y = (int)(c & 0xFFFFFFFF);
                    int z = elic.indexInChunk;
                    return new int3(x, y, z);
                }
            }

            public void RunOrExecute()
            {
                bool ran = false;
                TryRun(ref ran);
                if (!ran)
                    Execute();
            }

            [BurstDiscard]
            void TryRun(ref bool ran)
            {
                this.Run();
                ran = true;
            }
        }

        [BurstCompile]
        private struct WriteComponentDataJob : IJobFor
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
                switch(icb.m_state->typesSizes.Length)
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
                var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t0, t0Size);
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
                var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t0, t0Size);
                var   t1Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t1, t1Size);
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
                var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t0, t0Size);
                var   t1Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t1, t1Size);
                var   t2Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t2, t2Size);
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
                var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t0, t0Size);
                var   t1Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t1, t1Size);
                var   t2Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t2, t2Size);
                var   t3Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t3, t3Size);
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
                var   t0Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t0, t0Size);
                var   t1Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t1, t1Size);
                var   t2Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t2, t2Size);
                var   t3Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t3, t3Size);
                var   t4Array  = chunk.GetDynamicComponentDataArrayReinterpret<byte>(t4, t4Size);
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

            public void RunOrExecute(int length)
            {
                bool ran = false;
                TryRun(length, ref ran);
                if (!ran)
                {
                    for (int i = 0; i < length; i++)
                        Execute(i);
                }
            }

            [BurstDiscard]
            void TryRun(int length, ref bool ran)
            {
                this.Run(length);
                ran = true;
            }
        }

#pragma warning disable CS0649
        private struct DummyTypeT1 : IComponentData { public int dummy; }
        private struct DummyTypeT2 : IComponentData { public int dummy; }
        private struct DummyTypeT3 : IComponentData { public int dummy; }
        private struct DummyTypeT4 : IComponentData { public int dummy; }
#pragma warning restore CS0649

        static ComponentTypes BuildComponentTypesFromFixedList(FixedList64Bytes<int> types)
        {
            switch (types.Length)
            {
                case 1: return new ComponentTypes(ComponentType.ReadWrite(types[0]));
                case 2: return new ComponentTypes(ComponentType.ReadWrite(types[0]), ComponentType.ReadWrite(types[1]));
                case 3: return new ComponentTypes(ComponentType.ReadWrite(types[0]), ComponentType.ReadWrite(types[1]), ComponentType.ReadWrite(types[2]));
                case 4: return new ComponentTypes(ComponentType.ReadWrite(types[0]), ComponentType.ReadWrite(types[1]), ComponentType.ReadWrite(types[2]),
                                                  ComponentType.ReadWrite(types[3]));
                case 5: return new ComponentTypes(ComponentType.ReadWrite(types[0]), ComponentType.ReadWrite(types[1]), ComponentType.ReadWrite(types[2]),
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
        static void CheckComponentTypesValid(ComponentTypes types)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (types.m_masks.m_ZeroSizedMask != 0)
                throw new InvalidOperationException(
                    "InstantiateCommandBuffer cannot be created with zero-sized component types. You can instead add such types using AddTagComponent() after creation.");
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
                "At least 5 tags have already been added and adding more is not supported. Use SetComponentTags instead.");
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

