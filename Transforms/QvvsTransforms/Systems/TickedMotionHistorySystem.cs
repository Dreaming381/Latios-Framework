#if !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Systems
{
    [UpdateInGroup(typeof(Latios.Systems.TickedUpdateHistorySuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct TickedMotionHistorySystem : ISystem, ILatiosApi
    {
        EntityQuery m_newRootsJobQuery;
        EntityQuery m_newChildrenJobQuery;
        EntityQuery m_allQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);

            m_newRootsJobQuery = state.Fluent().With<TickedWorldTransform, TickedPreviousTransform, EntityInHierarchy>(true).Build();
            m_newRootsJobQuery.AddChangedVersionFilter(ComponentType.ReadOnly<TickedWorldTransform>());
            m_newRootsJobQuery.AddChangedVersionFilter(ComponentType.ReadOnly<TickedPreviousTransform>());
            m_newRootsJobQuery.AddOrderVersionFilter();

            m_newChildrenJobQuery = state.Fluent().With<TickedWorldTransform, TickedPreviousTransform, RootReference>(true).Build();
            m_newChildrenJobQuery.AddChangedVersionFilter(ComponentType.ReadOnly<TickedWorldTransform>());
            m_newChildrenJobQuery.AddChangedVersionFilter(ComponentType.ReadOnly<TickedPreviousTransform>());
            m_newChildrenJobQuery.AddOrderVersionFilter();

            m_allQuery = state.Fluent().With<TickedWorldTransform, TickedPreviousTransform>(true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api          = this.GetApi(ref state);
            var tickingState = api.worldBlackboardEntity.GetComponentData<TickingState>();
            if (tickingState.discardPreviousTick)
            {
                // Revert
                var rootCount = m_newRootsJobQuery.CalculateEntityCountWithoutFiltering();
                var rootsSet  = new NativeParallelHashSet<Entity>(rootCount, state.WorldUpdateAllocator);
                var jh        = new DetectNewRootsJob
                {
                    rootsSet = rootsSet.AsParallelWriter()
                }.Inject(api).ScheduleParallel(m_newRootsJobQuery, state.Dependency);
                var childChunkCount = m_newChildrenJobQuery.CalculateChunkCountWithoutFiltering();
                var stream          = new NativeStream(childChunkCount, state.WorldUpdateAllocator);
                jh                  = new CaptureNewChildrenJob
                {
                    rootsSet = rootsSet,
                    stream   = stream.AsWriter(),
                }.Inject(api).ScheduleParallel(m_newChildrenJobQuery, jh);
                m_allQuery.ResetFilter();
                m_allQuery.AddChangedVersionFilter(ComponentType.ReadOnly<TickedWorldTransform>());
                jh = new RestoreExistingFromHistoryJob().Inject(api).ScheduleParallel(m_allQuery, jh);
                jh = new PropoagateNewChildrenAfterExistingRestoreJob
                {
                    stream = stream.AsReader(),
                }.Inject(api).Schedule(jh);
                m_allQuery.ResetFilter();
                m_allQuery.AddChangedVersionFilter(ComponentType.ReadOnly<TickedWorldTransform>());
                m_allQuery.AddChangedVersionFilter(ComponentType.ReadOnly<TickedPreviousTransform>());
                m_allQuery.AddOrderVersionFilter();
                state.Dependency = new InitUninitializedHistoryAfterRestoreJob().Inject(api).ScheduleParallel(m_allQuery, jh);
            }
            else
            {
                // Advance
                m_allQuery.ResetFilter();
                state.Dependency = new AdvanceHistoryJob
                {
                    lastSystemVersion = state.LastSystemVersion,
                }.Inject(api).ScheduleParallel(m_allQuery, state.Dependency);
            }
        }

        #region Revert to Previous
        struct CapturedChildChunk
        {
            public ArchetypeChunk chunk;
            public int            countInChunk;
        }

        struct CapturedChild
        {
            public int                               indexInChunk;
            public TransformQvvs                     newWorldTransform;
            public TickedPreviousLocalTransformCache newLocalTransform;
        }

        // Requires change version on TickedWorldTransform, change version on TickedPreviousTransform, and order version
        [BurstCompile]
        partial struct DetectNewRootsJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] EntityTypeHandle                             entityHandle;
            [ReadOnly, Inject] ComponentTypeHandle<TickedPreviousTransform> previousTransformHandle;
            public NativeParallelHashSet<Entity>.ParallelWriter             rootsSet;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities   = chunk.GetEntityDataPtrRO(entityHandle);
                var transforms = chunk.GetComponentDataPtrRO(ref previousTransformHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (transforms[i].rotation.value.Equals(float4.zero))
                        rootsSet.Add(entities[i]);
                }
            }
        }

        // Requires change version on TickedWorldTransform, change version on TickedPreviousTransform, and order version
        [BurstCompile]
        partial struct CaptureNewChildrenJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<RootReference>           rootReferenceHandle;
            [ReadOnly, Inject] BufferLookup<EntityInHierarchy>              entityInHierarchyLookup;
            [ReadOnly, Inject] BufferLookup<EntityInHierarchyCleanup>       entityInHierarchyCleanupLookup;
            [ReadOnly, Inject] ComponentTypeHandle<TickedWorldTransform>    worldTransformHandle;
            [ReadOnly, Inject] ComponentTypeHandle<TickedPreviousTransform> previousTransformHandle;
            [ReadOnly, Inject] ComponentLookup<TickedWorldTransform>        worldTransformLookup;
            [ReadOnly, Inject] ComponentLookup<TickedPreviousTransform>     previousTransformLookup;
            [ReadOnly, Inject] EntityStorageInfoLookup                      esil;
            [ReadOnly] public NativeParallelHashSet<Entity>                 rootsSet;
            public NativeStream.Writer                                      stream;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var                transforms           = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                var                prevs                = chunk.GetComponentDataPtrRO(ref previousTransformHandle);
                var                rootRefs             = chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
                CapturedChildChunk capturedChunkDefault = default;
                ref var            capturedChunk        = ref capturedChunkDefault;
                bool               chunkHasWrites       = false;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (prevs[i].rotation.value.Equals(float4.zero))
                    {
                        // Note: Checking the hashset here is fast, but it means we drop support for reassigning the parent outside of ticking.
                        // If needed, we can simply drop this check since we check parent history down below. But that would mean a hierarchy
                        // and previous transform fetch for every descendant entity in a newly-instantiated hierarchy.
                        var rootRef = rootRefs[i];
                        if (rootsSet.Contains(rootRef.rootEntity))
                            continue; // The root is new, meaning the whole hierarchy is new.

                        var handle = rootRef.ToHandle(ref entityInHierarchyLookup, ref entityInHierarchyCleanupLookup);
                        var parent = handle.FindParent(esil);
                        if (parent.isNull)
                            continue; // There's no parent being reverted, so there's nothing that needs to be propagated.

                        var parentPrevious = previousTransformLookup[parent.entity];
                        if (parentPrevious.rotation.value.Equals(float4.zero))
                            continue; // The parent is also new. We'll clean up this entity via propagation.

                        var parentTransform = worldTransformLookup[parent.entity];
                        if (parentTransform.worldTransform.Equals(parentPrevious.worldTransform))
                            continue; // No change to revert.

                        if (!chunkHasWrites)
                        {
                            stream.BeginForEachIndex(unfilteredChunkIndex);
                            capturedChunk              = ref stream.Allocate<CapturedChildChunk>();
                            capturedChunk.chunk        = chunk;
                            capturedChunk.countInChunk = 0;
                            chunkHasWrites             = true;
                        }

                        var localBackup    = WorldLocalOps.CopyTickedLocalToCache(in handle);
                        var worldTransform = transforms[i].worldTransform;
                        WorldLocalOps.PropagateTransform(in parentPrevious.worldTransform, in parentTransform.worldTransform, ref worldTransform, in parent, in handle, true);
                        var localTransform = WorldLocalOps.CopyTickedLocalToCache(in handle);
                        WorldLocalOps.RestoreFromTickedLocalCache(in handle, in localBackup);

                        stream.Write(new CapturedChild
                        {
                            indexInChunk      = i,
                            newLocalTransform = localTransform,
                            newWorldTransform = worldTransform
                        });
                        capturedChunk.countInChunk++;
                    }
                }

                if (chunkHasWrites)
                    stream.EndForEachIndex();
            }
        }

        // Requires change version on TickedWorldTransform
        [BurstCompile]
        partial struct RestoreExistingFromHistoryJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<RootReference>                     rootReferenceHandle;
            [ReadOnly, Inject] BufferLookup<EntityInHierarchy>                        entityInHierarchyLookup;
            [ReadOnly, Inject] BufferLookup<EntityInHierarchyCleanup>                 entityInHierarchyCleanupLookup;
            [ReadOnly, Inject] ComponentTypeHandle<TickedPreviousTransform>           previousTransformHandle;
            [ReadOnly, Inject] ComponentTypeHandle<TickedPreviousLocalTransformCache> previousLocalTransformCacheHandle;
            [Inject] ComponentTypeHandle<TickedWorldTransform>                        worldTransformHandle;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var  rootReferences      = chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
                var  prevWorlds          = chunk.GetComponentDataPtrRO(ref previousTransformHandle);
                var  prevLocals          = rootReferences != null? chunk.GetComponentDataPtrRO(ref previousLocalTransformCacheHandle) : null;
                var  worldTransforms     = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                bool wroteWorldTransform = false;

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (prevWorlds[i].rotation.value.Equals(float4.zero))
                        continue;
                    if (!wroteWorldTransform)
                        wroteWorldTransform           = !prevWorlds[i].worldTransform.Equals(worldTransforms[i].worldTransform);
                    worldTransforms[i].worldTransform = prevWorlds[i].worldTransform;

                    if (prevLocals != null)
                    {
                        var handle = rootReferences[i].ToHandle(ref entityInHierarchyLookup, ref entityInHierarchyCleanupLookup);
                        WorldLocalOps.RestoreFromTickedLocalCache(in handle, in prevLocals[i]);
                    }
                }
                if (wroteWorldTransform)
                    chunk.GetComponentDataPtrRW(ref worldTransformHandle);
            }
        }

        [BurstCompile]
        partial struct PropoagateNewChildrenAfterExistingRestoreJob : IJob, IInjectable
        {
            [ReadOnly] public NativeStream.Reader stream;
            [ReadOnly, Inject] EntityTypeHandle   entityHandle;
            [Inject] TickedTransformAspectLookup  transformAspectLookup;

            public unsafe void Execute()
            {
                NativeList<TickedTransformBatchWriteCommand>  commands = new NativeList<TickedTransformBatchWriteCommand>(Allocator.Temp);
                NativeList<TickedPreviousLocalTransformCache> caches   = new NativeList<TickedPreviousLocalTransformCache>(Allocator.Temp);
                for (int chunkIndex = 0; chunkIndex < stream.ForEachCount; chunkIndex++)
                {
                    stream.BeginForEachIndex(chunkIndex);
                    while (stream.RemainingItemCount > 0)
                    {
                        var capturedChunk = stream.Read<CapturedChildChunk>();
                        var entities      = capturedChunk.chunk.GetEntityDataPtrRO(entityHandle);

                        for (int i = 0; i < capturedChunk.countInChunk; i++)
                        {
                            var captured        = stream.Read<CapturedChild>();
                            var transformAspect = transformAspectLookup[entities[captured.indexInChunk]];
                            commands.Add(TickedTransformBatchWriteCommand.SetWorldTransform(transformAspect, in captured.newWorldTransform));
                            caches.Add(captured.newLocalTransform);
                        }
                    }
                    stream.EndForEachIndex();
                }
                commands.ApplyTransforms();
                for (int i = 0; i < commands.Length; i++)
                {
                    var handle = commands[i].aspect.entityInHierarchyHandle;
                    WorldLocalOps.RestoreFromTickedLocalCache(in handle, caches[i]);
                }
            }
        }

        // Requires change version on TickedWorldTransform, change version on TickedPreviousTransform, and order version
        [BurstCompile]
        partial struct InitUninitializedHistoryAfterRestoreJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<RootReference>           rootReferenceHandle;
            [ReadOnly, Inject] BufferLookup<EntityInHierarchy>              entityInHierarchyLookup;
            [ReadOnly, Inject] BufferLookup<EntityInHierarchyCleanup>       entityInHierarchyCleanupLookup;
            [ReadOnly, Inject] ComponentTypeHandle<TickedWorldTransform>    worldTransformHandle;
            [Inject] ComponentTypeHandle<TickedPreviousTransform>           previousTransformHandle;
            [Inject] ComponentTypeHandle<TickedPreviousLocalTransformCache> previousLocalTransformCacheHandle;
            [Inject] ComponentTypeHandle<TickedTwoAgoTransform>             twoAgoTransformHandle;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var  prevs          = chunk.GetComponentDataPtrRO(ref previousTransformHandle);
                var  twoAgos        = chunk.GetComponentDataPtrRO(ref twoAgoTransformHandle);
                var  rootReferences = chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
                bool initPrevious   = false;
                bool initTwoAgos    = false;
                for (int i = chunk.Count - 1; i >= 0; i--)
                {
                    if (prevs[i].rotation.value.Equals(float4.zero))
                    {
                        initPrevious = true;
                        break;
                    }
                }
                if (initPrevious)
                {
                    var currents = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                    chunk.GetComponentDataPtrRW(ref previousTransformHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (prevs[i].rotation.value.Equals(float4.zero))
                        {
                            prevs[i].worldTransform = currents[i].worldTransform;
                        }
                    }
                    if (rootReferences != null)
                    {
                        var caches = chunk.GetComponentDataPtrRW(ref previousLocalTransformCacheHandle);
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var handle = rootReferences[i].ToHandle(ref entityInHierarchyLookup, ref entityInHierarchyCleanupLookup);
                            caches[i]  = WorldLocalOps.CopyTickedLocalToCache(in handle);
                        }
                    }
                }
                if (twoAgos != null)
                {
                    for (int i = chunk.Count - 1; i >= 0; i--)
                    {
                        if (twoAgos[i].rotation.value.Equals(float4.zero))
                        {
                            initTwoAgos = true;
                            break;
                        }
                    }
                }
                if (initTwoAgos)
                {
                    chunk.GetComponentDataPtrRW(ref twoAgoTransformHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (twoAgos[i].rotation.value.Equals(float4.zero))
                        {
                            twoAgos[i].worldTransform = prevs[i].worldTransform;
                        }
                    }
                }
            }
        }
        #endregion

        [BurstCompile]
        partial struct AdvanceHistoryJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<TickedWorldTransform>    worldTransformHandle;
            [ReadOnly, Inject] ComponentTypeHandle<RootReference>           rootReferenceHandle;
            [ReadOnly, Inject] BufferLookup<EntityInHierarchy>              entityInHierarchyLookup;
            [ReadOnly, Inject] BufferLookup<EntityInHierarchyCleanup>       entityInHierarchyCleanupLookup;
            [Inject] ComponentTypeHandle<TickedPreviousTransform>           previousTransformHandle;
            [Inject] ComponentTypeHandle<TickedPreviousLocalTransformCache> previousLocalTransformCacheHandle;
            [Inject] ComponentTypeHandle<TickedTwoAgoTransform>             twoAgoTransformHandle;
            public uint                                                     lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool updatePrevious = chunk.DidChange(ref worldTransformHandle, lastSystemVersion);
                bool hasTwoAgo      = chunk.Has(ref twoAgoTransformHandle);
                bool updateTwoAgo   = hasTwoAgo && DidChangeLastFrame(chunk.GetChangeVersion(ref previousTransformHandle));
                bool checkForInit   = chunk.DidOrderChange(lastSystemVersion);

                if (!updatePrevious && checkForInit)
                {
                    var prevs = chunk.GetComponentDataPtrRO(ref previousTransformHandle);
                    for (int i = chunk.Count - 1; i >= 0; i--)
                    {
                        if (prevs[i].rotation.value.Equals(float4.zero))
                        {
                            updatePrevious = true;
                            break;
                        }
                    }
                }
                if (hasTwoAgo && !updateTwoAgo && checkForInit)
                {
                    var                   twoAgos = chunk.GetComponentDataPtrRO(ref twoAgoTransformHandle);
                    TickedWorldTransform* current = null;
                    for (int i = chunk.Count - 1; i >= 0; i--)
                    {
                        if (twoAgos[i].rotation.value.Equals(float4.zero))
                        {
                            if (current == null)
                            {
                                chunk.GetComponentDataPtrRW(ref twoAgoTransformHandle);
                                current = chunk.GetComponentDataPtrRO(ref worldTransformHandle);
                            }
                            twoAgos[i].worldTransform = current[i].worldTransform;
                        }
                    }
                }

                if (updatePrevious)
                {
                    var currents = chunk.GetNativeArray(ref worldTransformHandle).Reinterpret<TransformQvvs>();
                    var prevs    = chunk.GetNativeArray(ref previousTransformHandle).Reinterpret<TransformQvvs>();

                    if (updateTwoAgo)
                    {
                        var twoAgos = chunk.GetNativeArray(ref twoAgoTransformHandle).Reinterpret<TransformQvvs>();
                        twoAgos.CopyFrom(prevs);
                    }

                    prevs.CopyFrom(currents);

                    var rootReferences = chunk.GetComponentDataPtrRO(ref rootReferenceHandle);
                    if (rootReferences != null)
                    {
                        var caches = chunk.GetComponentDataPtrRW(ref previousLocalTransformCacheHandle);
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            var handle = rootReferences[i].ToHandle(ref entityInHierarchyLookup, ref entityInHierarchyCleanupLookup);
                            caches[i]  = WorldLocalOps.CopyTickedLocalToCache(in handle);
                        }
                    }
                }
                else if (hasTwoAgo && updateTwoAgo)
                {
                    var prevs   = chunk.GetRequiredComponentDataPtrRO(ref previousTransformHandle);
                    var twoAgos = chunk.GetRequiredComponentDataPtrRW(ref twoAgoTransformHandle);

                    UnsafeUtility.MemCpy(twoAgos, prevs, UnsafeUtility.SizeOf<TransformQvvs>() * chunk.Count);
                }
            }

            bool DidChangeLastFrame(uint storedVersion)
            {
                // When a system runs for the first time, everything is considered changed.
                if (lastSystemVersion == 0)
                    return true;
                // Supporting wrap around for version numbers, change must be bigger than last system run.
                // (Never detect change of something the system itself changed)
                return (int)(storedVersion - lastSystemVersion) >= 0;
            }
        }
    }
}
#endif

