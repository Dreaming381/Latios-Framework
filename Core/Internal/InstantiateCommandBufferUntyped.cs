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

using CommandFunction = Unity.Burst.FunctionPointer<Latios.IInstantiateCommand.OnPlayback>;

namespace Latios
{
    [NativeContainer]
    [BurstCompile]
    internal unsafe struct InstantiateCommandBufferUntyped : INativeDisposable
    {
        #region Structure
        [NativeDisableUnsafePtrRestriction] private UnsafeParallelBlockList<PrefabSortkey>* m_prefabSortkeyBlockList;
        [NativeDisableUnsafePtrRestriction] private UnsafeParallelBlockList*                m_dataBlockList;
        [NativeDisableUnsafePtrRestriction] private State*                                  m_state;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        //Unfortunately this name is hardcoded into Unity. No idea how EntityCommandBuffer gets away with multiple safety handles.
        AtomicSafetyHandle m_Safety;

        static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<InstantiateCommandBufferUntyped>();
#endif

        private struct State
        {
            public ComponentTypeSet                  tagsToAdd;
            public FixedList64Bytes<TypeIndex>       typesWithData;
            public FixedList64Bytes<CommandFunction> commandFunctions;
            public FixedList64Bytes<int>             typesSizes;
            public AllocatorManager.AllocatorHandle  allocator;
            public bool                              playedBack;
        }

        internal struct PrefabSortkey : IRadixSortableInt3, IRadixSortableInt
        {
            public Entity prefab;
            public int    sortKey;

            public int GetKey() => sortKey;
            public int3 GetKey3() => new int3(prefab.Index, prefab.Version, sortKey);
        }

        internal struct CommandMeta
        {
            public CommandFunction function;
            public int             commandSize;
        }
        #endregion

        #region CreateDestroy
        public InstantiateCommandBufferUntyped(AllocatorManager.AllocatorHandle allocator,
                                               FixedList128Bytes<ComponentType> typesWithData,
                                               FixedList64Bytes<CommandMeta>    commands = default) : this(allocator, typesWithData, commands, 1)
        {
        }

        internal InstantiateCommandBufferUntyped(AllocatorManager.AllocatorHandle allocator, FixedList128Bytes<ComponentType> componentTypesWithData,
                                                 FixedList64Bytes<CommandMeta> commands, int disposeSentinalStackDepth)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CheckAllocator(allocator);

            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
            CollectionHelper.SetStaticSafetyId<EntityOperationCommandBuffer>(ref m_Safety, ref s_staticSafetyId.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            int                         dataPayloadSize = 0;
            FixedList64Bytes<int>       typesSizes      = new FixedList64Bytes<int>();
            FixedList64Bytes<TypeIndex> typesWithData   = new FixedList64Bytes<TypeIndex>();
            for (int i = 0; i < componentTypesWithData.Length; i++)
            {
                var size         = TypeManager.GetTypeInfo(componentTypesWithData[i].TypeIndex).ElementSize;
                dataPayloadSize += size;
                typesSizes.Add(size);
                typesWithData.Add(componentTypesWithData[i].TypeIndex);
            }
            CheckComponentTypesValid(BuildComponentTypesFromFixedList(typesWithData));
            FixedList64Bytes<CommandFunction> functions = new FixedList64Bytes<CommandFunction>();
            for (int i = 0; i < commands.Length; i++)
            {
                functions.Add(commands[i].function);
                typesSizes.Add(commands[i].commandSize);
                dataPayloadSize += commands[i].commandSize;
            }
            m_prefabSortkeyBlockList  = AllocatorManager.Allocate<UnsafeParallelBlockList<PrefabSortkey> >(allocator, 1);
            m_dataBlockList           = AllocatorManager.Allocate<UnsafeParallelBlockList>(allocator, 1);
            m_state                   = AllocatorManager.Allocate<State>(allocator, 1);
            *m_prefabSortkeyBlockList = new UnsafeParallelBlockList<PrefabSortkey>(256, allocator);
            *m_dataBlockList          = new UnsafeParallelBlockList(dataPayloadSize, 256, allocator);
            *m_state                  = new State
            {
                typesWithData    = typesWithData,
                commandFunctions = functions,
                tagsToAdd        = default,
                typesSizes       = typesSizes,
                allocator        = allocator,
                playedBack       = false
            };
        }

        [BurstCompile]
        private struct DisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public State* state;

            [NativeDisableUnsafePtrRestriction]
            public UnsafeParallelBlockList<PrefabSortkey>* prefabSortkeyBlockList;
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
                componentDataBlockList = m_dataBlockList,
                state                  = m_state,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = m_Safety
#endif
            }.Schedule(inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            m_state                  = null;
            m_dataBlockList          = null;
            m_prefabSortkeyBlockList = null;
            return jobHandle;
        }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif
            Deallocate(m_state, m_prefabSortkeyBlockList, m_dataBlockList);
        }

        private static void Deallocate(State* state, UnsafeParallelBlockList<PrefabSortkey>* prefabSortkeyBlockList, UnsafeParallelBlockList* componentDataBlockList)
        {
            var allocator = state->allocator;
            prefabSortkeyBlockList->Dispose();
            componentDataBlockList->Dispose();
            AllocatorManager.Free(allocator, prefabSortkeyBlockList, 1);
            AllocatorManager.Free(allocator, componentDataBlockList, 1);
            AllocatorManager.Free(allocator, state,                  1);
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
            byte* ptr                                                                    = (byte*)m_dataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1>(Entity prefab, T0 c0, T1 c1, int sortKey = int.MaxValue) where T0 : unmanaged where T1 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(prefab);
            m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_dataBlockList->Allocate(0);
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
            byte* ptr                                                                    = (byte*)m_dataBlockList->Allocate(0);
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
            byte* ptr                                                                    = (byte*)m_dataBlockList->Allocate(0);
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
            byte* ptr                                                                    = (byte*)m_dataBlockList->Allocate(0);
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
        [WriteAccessRequired]
        public void Add<T0, T1, T2, T3, T4, T5>(Entity prefab, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4, T5 c5,
                                                int sortKey =
                                                    int.MaxValue) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5
        : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(prefab);
            m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_dataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            ptr += m_state->typesSizes[0];
            UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
            ptr += m_state->typesSizes[1];
            UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
            ptr += m_state->typesSizes[2];
            UnsafeUtility.CopyStructureToPtr(ref c3, ptr);
            ptr += m_state->typesSizes[3];
            UnsafeUtility.CopyStructureToPtr(ref c4, ptr);
            ptr += m_state->typesSizes[4];
            UnsafeUtility.CopyStructureToPtr(ref c5, ptr);
        }
        [WriteAccessRequired]
        public void Add<T0, T1, T2, T3, T4, T5, T6>(Entity prefab, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4, T5 c5, T6 c6,
                                                    int sortKey =
                                                        int.MaxValue) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where
        T5 : unmanaged where T6 : unmanaged
        {
            CheckWriteAccess();
            CheckHasNotPlayedBack();
            CheckEntityValid(prefab);
            m_prefabSortkeyBlockList->Write(new PrefabSortkey { prefab = prefab, sortKey = sortKey }, 0);
            byte* ptr                                                                    = (byte*)m_dataBlockList->Allocate(0);
            UnsafeUtility.CopyStructureToPtr(ref c0, ptr);
            ptr += m_state->typesSizes[0];
            UnsafeUtility.CopyStructureToPtr(ref c1, ptr);
            ptr += m_state->typesSizes[1];
            UnsafeUtility.CopyStructureToPtr(ref c2, ptr);
            ptr += m_state->typesSizes[2];
            UnsafeUtility.CopyStructureToPtr(ref c3, ptr);
            ptr += m_state->typesSizes[3];
            UnsafeUtility.CopyStructureToPtr(ref c4, ptr);
            ptr += m_state->typesSizes[4];
            UnsafeUtility.CopyStructureToPtr(ref c5, ptr);
            ptr += m_state->typesSizes[5];
            UnsafeUtility.CopyStructureToPtr(ref c6, ptr);
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
            var writer = new ParallelWriter(m_prefabSortkeyBlockList, m_dataBlockList, m_state);
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
                PlaybackOnThread(*icb, *em);
            }

            static void PlaybackOnThread(InstantiateCommandBufferUntyped icb, EntityManager em)
            {
                // Step 1: Get the prefabs and sort keys
                int count              = icb.Count();
                var prefabSortkeyArray = new NativeArray<PrefabSortkey>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                icb.m_prefabSortkeyBlockList->GetElementValues(prefabSortkeyArray);
                // Step 2: Get the component and command data pointers
                var unsortedDataPtrs = new NativeArray<UnsafeIndexedBlockList.ElementPtr>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                icb.m_dataBlockList->GetElementPtrs(unsortedDataPtrs);
                // Step 3: Sort the arrays by sort key and collapse unique entities
                var ranks = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                RadixSort.RankSortInt(ranks, prefabSortkeyArray);
                var uniquePrefabs   = new UnsafeList<UniquePrefab>(count, Allocator.Temp);
                var uniquePrefabMap = new UnsafeHashMap<Entity, int>(count, Allocator.Temp);
                for (int i = 0; i < count; i++)
                {
                    var entity = prefabSortkeyArray[ranks[i]].prefab;
                    if (uniquePrefabMap.TryGetValue(entity, out var uniqueIndex))
                        uniquePrefabs.ElementAt(uniqueIndex).count++;
                    else
                    {
                        uniquePrefabMap.Add(entity, uniquePrefabs.Length);
                        uniquePrefabs.AddNoResize(new UniquePrefab { prefab = entity, count = 1 });
                    }
                }
                int running = 0;
                for (int i = 0; i < uniquePrefabs.Length; i++)
                {
                    ref var u  = ref uniquePrefabs.ElementAt(i);
                    u.start    = running;
                    running   += u.count;
                    u.count    = 0;
                }

                var sortedDataPtrs = new NativeArray<UnsafeIndexedBlockList.ElementPtr>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < count; i++)
                {
                    ref var u                         = ref uniquePrefabs.ElementAt(uniquePrefabMap[prefabSortkeyArray[ranks[i]].prefab]);
                    sortedDataPtrs[u.start + u.count] = unsortedDataPtrs[ranks[i]];
                    u.count++;
                }

                // Step 4: Instantiate the prefabs
                var instantiatedEntities = new NativeArray<Entity>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var typesWithDataToAdd   = BuildComponentTypesFromFixedList(icb.m_state->typesWithData);
                int startIndex           = 0;
                for (int i = 0; i < uniquePrefabs.Length; i++)
                {
                    var uniquePrefab = uniquePrefabs[i];
                    var firstEntity  = em.Instantiate(uniquePrefab.prefab);
                    em.AddComponent(firstEntity, typesWithDataToAdd);
                    em.AddComponent(firstEntity, icb.m_state->tagsToAdd);
                    instantiatedEntities[startIndex] = firstEntity;
                    startIndex++;

                    if (uniquePrefab.count - 1 > 0)
                    {
                        var subArray = instantiatedEntities.GetSubArray(startIndex, uniquePrefab.count - 1);
                        em.Instantiate(firstEntity, subArray);
                        startIndex += subArray.Length;
                    }
                }

                // Step 5: Write the components
                switch (icb.m_state->typesWithData.Length)
                {
                    case 1:
                        var t0Proc = new ChunkExecuteT0
                        {
                            t0     = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[0])),
                            t0Size = icb.m_state->typesSizes[0]
                        };
                        ProcessEntitiesInChunks(em, uniquePrefabs, instantiatedEntities, sortedDataPtrs, ref t0Proc);
                        break;
                    case 2:
                        var t1Proc = new ChunkExecuteT1
                        {
                            t0     = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[0])),
                            t1     = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[1])),
                            t0Size = icb.m_state->typesSizes[0],
                            t1Size = icb.m_state->typesSizes[1],
                        };
                        ProcessEntitiesInChunks(em, uniquePrefabs, instantiatedEntities, sortedDataPtrs, ref t1Proc);
                        break;
                    case 3:
                        var t2Proc = new ChunkExecuteT2
                        {
                            t0       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[0])),
                            t1       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[1])),
                            t2       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[2])),
                            t0Size   = icb.m_state->typesSizes[0],
                            t1Size   = icb.m_state->typesSizes[1],
                            t2Size   = icb.m_state->typesSizes[2],
                            t2Offset = icb.m_state->typesSizes[0] + icb.m_state->typesSizes[1],
                        };
                        ProcessEntitiesInChunks(em, uniquePrefabs, instantiatedEntities, sortedDataPtrs, ref t2Proc);
                        break;
                    case 4:
                        var t3Proc = new ChunkExecuteT3
                        {
                            t0       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[0])),
                            t1       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[1])),
                            t2       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[2])),
                            t3       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[3])),
                            t0Size   = icb.m_state->typesSizes[0],
                            t1Size   = icb.m_state->typesSizes[1],
                            t2Size   = icb.m_state->typesSizes[2],
                            t3Size   = icb.m_state->typesSizes[3],
                            t2Offset = icb.m_state->typesSizes[0] + icb.m_state->typesSizes[1],
                            t3Offset = icb.m_state->typesSizes[0] + icb.m_state->typesSizes[1] + icb.m_state->typesSizes[2],
                        };
                        ProcessEntitiesInChunks(em, uniquePrefabs, instantiatedEntities, sortedDataPtrs, ref t3Proc);
                        break;
                    case 5:
                        var t4Proc = new ChunkExecuteT4
                        {
                            t0       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[0])),
                            t1       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[1])),
                            t2       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[2])),
                            t3       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[3])),
                            t4       = em.GetDynamicComponentTypeHandle(ComponentType.ReadWrite(icb.m_state->typesWithData[4])),
                            t0Size   = icb.m_state->typesSizes[0],
                            t1Size   = icb.m_state->typesSizes[1],
                            t2Size   = icb.m_state->typesSizes[2],
                            t3Size   = icb.m_state->typesSizes[3],
                            t4Size   = icb.m_state->typesSizes[4],
                            t2Offset = icb.m_state->typesSizes[0] + icb.m_state->typesSizes[1],
                            t3Offset = icb.m_state->typesSizes[0] + icb.m_state->typesSizes[1] + icb.m_state->typesSizes[2],
                            t4Offset = icb.m_state->typesSizes[0] + icb.m_state->typesSizes[1] + icb.m_state->typesSizes[2] + icb.m_state->typesSizes[3],
                        };
                        ProcessEntitiesInChunks(em, uniquePrefabs, instantiatedEntities, sortedDataPtrs, ref t4Proc);
                        break;
                }

                // Step 6: Process the commands
                int commandOffset = 0;
                for (int i = 0; i < icb.m_state->typesWithData.Length; i++)
                    commandOffset += icb.m_state->typesSizes[i];
                for (int i = 0; i < icb.m_state->commandFunctions.Length; i++)
                {
                    var context = new IInstantiateCommand.Context
                    {
                        entityManager = em,
                        entities      = instantiatedEntities,
                        commandOffset = commandOffset,
                        dataPtrs      = sortedDataPtrs,
                        expectedSize  = icb.m_state->typesSizes[icb.m_state->typesWithData.Length + i],
                    };
                    icb.m_state->commandFunctions[i].Invoke(ref context);
                }

                icb.m_state->playedBack = true;
            }

            static void ProcessEntitiesInChunks<T>(EntityManager em, UnsafeList<UniquePrefab> uniquePrefabs, NativeArray<Entity> entities,
                                                   NativeArray<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs, ref T processor) where T : unmanaged, IChunkProcessor
            {
                int offset = 0;
                for (int uniquePrefabIndex = 0; uniquePrefabIndex < uniquePrefabs.Length; uniquePrefabIndex++)
                {
                    int countRemaining = uniquePrefabs[uniquePrefabIndex].count;
                    while (countRemaining > 0)
                    {
                        var info           = em.GetStorageInfo(entities[offset]);
                        var countToProcess = math.min(countRemaining, info.Chunk.Count - info.IndexInChunk);
                        var subArray       = componentDataPtrs.GetSubArray(offset, countToProcess);
                        processor.Execute(in info.Chunk, subArray, info.IndexInChunk);
                        offset         += countToProcess;
                        countRemaining -= countToProcess;
                    }
                }
            }

            struct UniquePrefab
            {
                public Entity prefab;
                public int    start;
                public int    count;
            }

            interface IChunkProcessor
            {
                void Execute(in ArchetypeChunk chunk, NativeArray<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs, int chunkStart);
            }

            struct ChunkExecuteT0 : IChunkProcessor
            {
                public DynamicComponentTypeHandle t0;
                public int                        t0Size;

                public void Execute(in ArchetypeChunk chunk, NativeArray<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs, int chunkStart)
                {
                    var t0Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size).GetUnsafePtr();
                    t0Ptr     += chunkStart * t0Size;
                    for (int i = 0; i < componentDataPtrs.Length; i++)
                    {
                        UnsafeUtility.MemCpy(t0Ptr + i * t0Size, componentDataPtrs[i].ptr, t0Size);
                    }
                }
            }

            struct ChunkExecuteT1 : IChunkProcessor
            {
                public DynamicComponentTypeHandle t0;
                public DynamicComponentTypeHandle t1;
                public int                        t0Size;
                public int                        t1Size;

                public void Execute(in ArchetypeChunk chunk, NativeArray<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs, int chunkStart)
                {
                    var t0Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size).GetUnsafePtr();
                    var t1Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t1, t1Size).GetUnsafePtr();
                    t0Ptr     += chunkStart * t0Size;
                    t1Ptr     += chunkStart * t1Size;
                    for (int i = 0; i < componentDataPtrs.Length; i++)
                    {
                        UnsafeUtility.MemCpy(t0Ptr + i * t0Size, componentDataPtrs[i].ptr,          t0Size);
                        UnsafeUtility.MemCpy(t1Ptr + i * t1Size, componentDataPtrs[i].ptr + t0Size, t1Size);
                    }
                }
            }

            struct ChunkExecuteT2 : IChunkProcessor
            {
                public DynamicComponentTypeHandle t0;
                public DynamicComponentTypeHandle t1;
                public DynamicComponentTypeHandle t2;
                public int                        t0Size;
                public int                        t1Size;
                public int                        t2Size;
                public int                        t2Offset;

                public void Execute(in ArchetypeChunk chunk, NativeArray<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs, int chunkStart)
                {
                    var t0Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size).GetUnsafePtr();
                    var t1Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t1, t1Size).GetUnsafePtr();
                    var t2Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t2, t2Size).GetUnsafePtr();
                    t0Ptr     += chunkStart * t0Size;
                    t1Ptr     += chunkStart * t1Size;
                    t2Ptr     += chunkStart * t2Size;
                    for (int i = 0; i < componentDataPtrs.Length; i++)
                    {
                        UnsafeUtility.MemCpy(t0Ptr + i * t0Size, componentDataPtrs[i].ptr,            t0Size);
                        UnsafeUtility.MemCpy(t1Ptr + i * t1Size, componentDataPtrs[i].ptr + t0Size,   t1Size);
                        UnsafeUtility.MemCpy(t2Ptr + i * t2Size, componentDataPtrs[i].ptr + t2Offset, t2Size);
                    }
                }
            }

            struct ChunkExecuteT3 : IChunkProcessor
            {
                public DynamicComponentTypeHandle t0;
                public DynamicComponentTypeHandle t1;
                public DynamicComponentTypeHandle t2;
                public DynamicComponentTypeHandle t3;
                public int                        t0Size;
                public int                        t1Size;
                public int                        t2Size;
                public int                        t3Size;
                public int                        t2Offset;
                public int                        t3Offset;

                public void Execute(in ArchetypeChunk chunk, NativeArray<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs, int chunkStart)
                {
                    var t0Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size).GetUnsafePtr();
                    var t1Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t1, t1Size).GetUnsafePtr();
                    var t2Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t2, t2Size).GetUnsafePtr();
                    var t3Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t3, t3Size).GetUnsafePtr();
                    t0Ptr     += chunkStart * t0Size;
                    t1Ptr     += chunkStart * t1Size;
                    t2Ptr     += chunkStart * t2Size;
                    t3Ptr     += chunkStart * t3Size;
                    for (int i = 0; i < componentDataPtrs.Length; i++)
                    {
                        UnsafeUtility.MemCpy(t0Ptr + i * t0Size, componentDataPtrs[i].ptr,            t0Size);
                        UnsafeUtility.MemCpy(t1Ptr + i * t1Size, componentDataPtrs[i].ptr + t0Size,   t1Size);
                        UnsafeUtility.MemCpy(t2Ptr + i * t2Size, componentDataPtrs[i].ptr + t2Offset, t2Size);
                        UnsafeUtility.MemCpy(t3Ptr + i * t3Size, componentDataPtrs[i].ptr + t3Offset, t3Size);
                    }
                }
            }

            struct ChunkExecuteT4 : IChunkProcessor
            {
                public DynamicComponentTypeHandle t0;
                public DynamicComponentTypeHandle t1;
                public DynamicComponentTypeHandle t2;
                public DynamicComponentTypeHandle t3;
                public DynamicComponentTypeHandle t4;
                public int                        t0Size;
                public int                        t1Size;
                public int                        t2Size;
                public int                        t3Size;
                public int                        t4Size;
                public int                        t2Offset;
                public int                        t3Offset;
                public int                        t4Offset;

                public void Execute(in ArchetypeChunk chunk, NativeArray<UnsafeIndexedBlockList.ElementPtr> componentDataPtrs, int chunkStart)
                {
                    var t0Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t0, t0Size).GetUnsafePtr();
                    var t1Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t1, t1Size).GetUnsafePtr();
                    var t2Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t2, t2Size).GetUnsafePtr();
                    var t3Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t3, t3Size).GetUnsafePtr();
                    var t4Ptr  = (byte*)chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref t4, t4Size).GetUnsafePtr();
                    t0Ptr     += chunkStart * t0Size;
                    t1Ptr     += chunkStart * t1Size;
                    t2Ptr     += chunkStart * t2Size;
                    t3Ptr     += chunkStart * t3Size;
                    t4Ptr     += chunkStart * t4Size;
                    for (int i = 0; i < componentDataPtrs.Length; i++)
                    {
                        UnsafeUtility.MemCpy(t0Ptr + i * t0Size, componentDataPtrs[i].ptr,            t0Size);
                        UnsafeUtility.MemCpy(t1Ptr + i * t1Size, componentDataPtrs[i].ptr + t0Size,   t1Size);
                        UnsafeUtility.MemCpy(t2Ptr + i * t2Size, componentDataPtrs[i].ptr + t2Offset, t2Size);
                        UnsafeUtility.MemCpy(t3Ptr + i * t3Size, componentDataPtrs[i].ptr + t3Offset, t3Size);
                        UnsafeUtility.MemCpy(t4Ptr + i * t4Size, componentDataPtrs[i].ptr + t4Offset, t4Size);
                    }
                }
            }
        }

        static ComponentTypeSet BuildComponentTypesFromFixedList(FixedList64Bytes<TypeIndex> types)
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
            private UnsafeParallelBlockList<PrefabSortkey>* m_prefabSortkeyBlockList;
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

            internal ParallelWriter(UnsafeParallelBlockList<PrefabSortkey>* prefabSortkeyBlockList, UnsafeParallelBlockList* componentDataBlockList, void* state)
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
            public void Add<T0, T1, T2, T3, T4, T5>(Entity prefab, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4, T5 c5,
                                                    int sortKey) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 :
            unmanaged
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
                ptr += m_state->typesSizes[4];
                UnsafeUtility.CopyStructureToPtr(ref c5, ptr);
            }
            public void Add<T0, T1, T2, T3, T4, T5, T6>(Entity prefab, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4, T5 c5, T6 c6,
                                                        int sortKey) where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where
            T5 : unmanaged where T6 : unmanaged
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
                ptr += m_state->typesSizes[4];
                UnsafeUtility.CopyStructureToPtr(ref c5, ptr);
                ptr += m_state->typesSizes[5];
                UnsafeUtility.CopyStructureToPtr(ref c6, ptr);
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

