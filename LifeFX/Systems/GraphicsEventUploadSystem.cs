using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
using Latios.Kinemation;
using Latios.Kinemation.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.LifeFX.Systems
{
    [UpdateInGroup(typeof(DispatchRoundRobinLateExtensionsSuperSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct GraphicsEventUploadSystem : ISystem, ICullingComputeDispatchSystem<GraphicsEventUploadSystem.CollectState, GraphicsEventUploadSystem.WriteState>
    {
        LatiosWorldUnmanaged latiosWorld;

        CullingComputeDispatchData<CollectState, WriteState> m_data;
        EntityQuery                                          m_destinationsQuery;
        AllocatorHelper<RewindableAllocator>                 m_allocator;

        delegate void DispatchDestinationDelegate(IntPtr dispatchData);
        static DispatchDestinationDelegate                                          s_managedDelegate;
        static bool                                                                 s_initialized;
        static readonly SharedStatic<FunctionPointer<DispatchDestinationDelegate> > s_functionPointer = SharedStatic<FunctionPointer<DispatchDestinationDelegate> >.GetOrCreate<GraphicsEventUploadSystem>();

        #region System Methods
        public void OnCreate(ref SystemState state)
        {
            Initialize();
            latiosWorld         = state.GetLatiosWorldUnmanaged();
            m_data              = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorld);
            m_destinationsQuery = state.Fluent().With<GraphicsEventTunnelDestination>(true).Build();
            m_allocator         = new AllocatorHelper<RewindableAllocator>(Allocator.Persistent);
            m_allocator.Allocator.Initialize(16 * 1024);

            GraphicsEventTypeRegistry.Init();
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new GraphicsEventPostal(m_allocator.Allocator.Handle));
            var broker = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();
            foreach (var meta in GraphicsEventTypeRegistry.s_eventMetadataList.Data)
            {
                broker.InitializeUploadPool(meta.brokerId, (uint)meta.size, UnityEngine.GraphicsBuffer.Target.Structured);
            }
        }

        public void OnDestroy(ref SystemState state)
        {
            state.CompleteDependency();
            m_allocator.Allocator.Rewind();
            m_allocator.Allocator.Dispose();
            m_allocator.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_data.DoUpdate(ref state, ref this);
        }

        public CollectState Collect(ref SystemState state)
        {
            if (latiosWorld.worldBlackboardEntity.GetComponentData<DispatchContext>().dispatchIndexThisFrame != 0)
                return default;

            if (m_destinationsQuery.IsEmptyIgnoreFilter)
            {
                // Force jobs to complete and rewind so that we don't have an infinite memory leak if we have events but no destinations.
                latiosWorld.GetCollectionComponent<GraphicsEventPostal>(latiosWorld.worldBlackboardEntity, out var jobsToComplete);
                jobsToComplete.Complete();
                m_allocator.Allocator.Rewind();
                latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new GraphicsEventPostal(m_allocator.Allocator.Handle));
                return default;
            }

            GraphicsEventTypeRegistry.s_eventHashManager.Data.SetLock(true);

            // Normally, we'd want to use our custom allocator. However, we need some of these arrays to stay valid while we are
            // dispatching to Mono, because user code could interact with entities and produce new events within that time, which
            // means we need to rewind before Mono dispatch.
            var allocator               = state.WorldUpdateAllocator;
            var eventTypeCount          = GraphicsEventTypeRegistry.s_eventMetadataList.Data.Length;
            var destinations            = new NativeList<GraphicsEventTunnelDestination>(allocator);
            var tunnels                 = new NativeList<UnityObjectRef<GraphicsEventTunnelBase> >(allocator);
            var tunnelRangesByTypeIndex = CollectionHelper.CreateNativeArray<int2>(eventTypeCount, allocator, NativeArrayOptions.UninitializedMemory);

            var job = new CollectDestinationsJob
            {
                chunks                  = m_destinationsQuery.ToArchetypeChunkListAsync(allocator, out var jh).AsDeferredJobArray(),
                destinationHandle       = GetBufferTypeHandle<GraphicsEventTunnelDestination>(true),
                destinations            = destinations,
                tunnels                 = tunnels,
                tunnelRangesByTypeIndex = tunnelRangesByTypeIndex
            };
            state.Dependency = job.Schedule(JobHandle.CombineDependencies(state.Dependency, jh));

            var postal                         = latiosWorld.GetCollectionComponent<GraphicsEventPostal>(latiosWorld.worldBlackboardEntity, false);
            var eventCountByTypeIndex          = CollectionHelper.CreateNativeArray<int>(eventTypeCount, allocator, NativeArrayOptions.UninitializedMemory);
            var eventRangesByTunnelByTypeIndex = CollectionHelper.CreateNativeArray<UnsafeList<int2> >(eventTypeCount, allocator, NativeArrayOptions.UninitializedMemory);
            state.Dependency                   = new GroupAndCountEventsJob
            {
                tunnels                        = tunnels.AsDeferredJobArray(),
                tunnelRangesByTypeIndex        = tunnelRangesByTypeIndex,
                postal                         = postal,
                eventCountByTypeIndex          = eventCountByTypeIndex,
                eventRangesByTunnelByTypeIndex = eventRangesByTunnelByTypeIndex,
                allocator                      = allocator,
            }.ScheduleParallel(eventTypeCount, 1, state.Dependency);

            return new CollectState
            {
                broker                         = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>(),
                destinations                   = destinations,
                tunnels                        = tunnels,
                tunnelRangesByTypeIndex        = tunnelRangesByTypeIndex,
                postal                         = postal,
                eventCountByTypeIndex          = eventCountByTypeIndex,
                eventRangesByTunnelByTypeIndex = eventRangesByTunnelByTypeIndex
            };
        }

        public unsafe WriteState Write(ref SystemState state, ref CollectState collected)
        {
            if (!collected.broker.isCreated)
                return default;

            var allocator       = state.WorldUpdateAllocator;
            var graphicsBuffers = CollectionHelper.CreateNativeArray<GraphicsBufferUnmanaged>(collected.eventCountByTypeIndex.Length,
                                                                                              allocator,
                                                                                              NativeArrayOptions.UninitializedMemory);
            var buffers = CollectionHelper.CreateNativeArray<UnsafeList<byte> >(collected.eventCountByTypeIndex.Length,
                                                                                allocator,
                                                                                NativeArrayOptions.UninitializedMemory);
            ref var metas = ref GraphicsEventTypeRegistry.s_eventMetadataList.Data;
            for (int i = 0; i < buffers.Length; i++)
            {
                var count = collected.eventCountByTypeIndex[i];
                if (count == 0)
                {
                    graphicsBuffers[i] = default;
                    buffers[i]         = default;
                }
                else
                {
                    var meta           = metas[i];
                    var gb             = collected.broker.GetUploadBuffer(meta.brokerId, (uint)math.max(count, 16));
                    graphicsBuffers[i] = gb;
                    var mapped         = gb.LockBufferForWrite<byte>(0, count * meta.size);
                    buffers[i]         = new UnsafeList<byte>((byte*)mapped.GetUnsafePtr(), mapped.Length);
                }
            }

            state.Dependency = new WriteEventsJob
            {
                tunnels                        = collected.tunnels.AsArray(),
                tunnelRangesByTypeIndex        = collected.tunnelRangesByTypeIndex,
                postal                         = collected.postal,
                eventRangesByTunnelByTypeIndex = collected.eventRangesByTunnelByTypeIndex,
                buffers                        = buffers
            }.ScheduleParallel(buffers.Length, 1, state.Dependency);

            return new WriteState
            {
                broker                         = collected.broker,
                destinations                   = collected.destinations,
                eventCountByTypeIndex          = collected.eventCountByTypeIndex,
                eventRangesByTunnelByTypeIndex = collected.eventRangesByTunnelByTypeIndex,
                graphicsBuffers                = graphicsBuffers
            };
        }

        public void Dispatch(ref SystemState state, ref WriteState written)
        {
            // It is safe to unlock here because map resizing does not impact this logic.
            GraphicsEventTypeRegistry.s_eventHashManager.Data.SetLock(false);

            if (!written.broker.isCreated)
                return;

            ref var metas = ref GraphicsEventTypeRegistry.s_eventMetadataList.Data;
            for (int typeIndex = 0; typeIndex < written.eventCountByTypeIndex.Length; typeIndex++)
            {
                if (written.eventCountByTypeIndex[typeIndex] > 0)
                    written.graphicsBuffers[typeIndex].UnlockBufferAfterWrite<byte>(written.eventCountByTypeIndex[typeIndex] * metas[typeIndex].size);
            }

            m_allocator.Allocator.Rewind();
            latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new GraphicsEventPostal(m_allocator.Allocator.Handle));

            int destinationIndex = 0;
            for (int typeIndex = 0; typeIndex < written.eventCountByTypeIndex.Length; typeIndex++)
            {
                var eventRanges = written.eventRangesByTunnelByTypeIndex[typeIndex];
                if (eventRanges.Length == 0)
                    continue;
                var buffer = written.graphicsBuffers[typeIndex];
                foreach (var eventRange in eventRanges)
                {
                    int start    = eventRange.x;
                    int count    = eventRange.y;
                    var previous = written.destinations[destinationIndex].tunnel;
                    while (destinationIndex < written.destinations.Length && written.destinations[destinationIndex].tunnel == previous)
                    {
                        UnityEngine.Assertions.Assert.AreEqual(typeIndex, written.destinations[destinationIndex].eventTypeIndex);
                        DispatchManaged(written.destinations[destinationIndex].requestor, buffer, start, count);
                        destinationIndex++;
                    }
                }
            }
        }
        #endregion

        #region Mono Interop
        static unsafe void DispatchManaged(UnityObjectRef<GraphicsEventBufferReceptor> requestor, GraphicsBufferUnmanaged buffer, int start, int count)
        {
            bool isBurst = true;
            DispatchManagedFromManaged(requestor, buffer, start, count, ref isBurst);
            if (isBurst)
            {
                var dispatchData = new DispatchData
                {
                    requestor = requestor,
                    buffer    = buffer,
                    start     = start,
                    count     = count
                };
                s_functionPointer.Data.Invoke((IntPtr)(&dispatchData));
            }
        }

        [BurstDiscard]
        static void DispatchManagedFromManaged(UnityObjectRef<GraphicsEventBufferReceptor> requestor, GraphicsBufferUnmanaged buffer, int start, int count, ref bool isBurst)
        {
            isBurst = false;
            requestor.Value.PublishInternal(buffer.ToManaged(), start, count);
        }

        [MonoPInvokeCallback(typeof(DispatchDestinationDelegate))]
        static unsafe void DispatchManaged(IntPtr dispatchData)
        {
            ref var data = ref *(DispatchData*)dispatchData;
            data.requestor.Value.PublishInternal(data.buffer.ToManaged(), data.start, data.count);
        }

        static void Initialize()
        {
            if (s_initialized)
                return;

            s_managedDelegate      = DispatchManaged;
            s_functionPointer.Data = new FunctionPointer<DispatchDestinationDelegate>(Marshal.GetFunctionPointerForDelegate<DispatchDestinationDelegate>(DispatchManaged));
            s_initialized          = true;

            // important: this will always be called from a special unload thread (main thread will be blocking on this)
            AppDomain.CurrentDomain.DomainUnload += (_, __) => { Shutdown(); };

            // There is no domain unload in player builds, so we must be sure to shutdown when the process exits.
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { Shutdown(); };
        }

        static void Shutdown()
        {
            if (!s_initialized)
                return;
            s_managedDelegate = null;
            s_initialized     = false;
        }
        #endregion

        #region Structs
        public struct CollectState
        {
            internal GraphicsBufferBroker                                 broker;
            internal NativeList<GraphicsEventTunnelDestination>           destinations;
            internal NativeList<UnityObjectRef<GraphicsEventTunnelBase> > tunnels;
            internal NativeArray<int2>                                    tunnelRangesByTypeIndex;
            internal GraphicsEventPostal                                  postal;
            internal NativeArray<int>                                     eventCountByTypeIndex;
            internal NativeArray<UnsafeList<int2> >                       eventRangesByTunnelByTypeIndex;
        }

        public struct WriteState
        {
            internal GraphicsBufferBroker                       broker;
            internal NativeList<GraphicsEventTunnelDestination> destinations;
            internal NativeArray<int>                           eventCountByTypeIndex;
            internal NativeArray<UnsafeList<int2> >             eventRangesByTunnelByTypeIndex;
            internal NativeArray<GraphicsBufferUnmanaged>       graphicsBuffers;
        }

        struct DispatchData
        {
            public UnityObjectRef<GraphicsEventBufferReceptor> requestor;
            public GraphicsBufferUnmanaged                     buffer;
            public int                                         start;
            public int                                         count;
        }
        #endregion

        #region Jobs
        [BurstCompile]
        struct CollectDestinationsJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>                      chunks;
            [ReadOnly] public BufferTypeHandle<GraphicsEventTunnelDestination> destinationHandle;

            public NativeList<GraphicsEventTunnelDestination>           destinations;
            public NativeList<UnityObjectRef<GraphicsEventTunnelBase> > tunnels;
            public NativeArray<int2>                                    tunnelRangesByTypeIndex;

            public void Execute()
            {
                var uniqueDestinations = new NativeHashSet<GraphicsEventTunnelDestination>(chunks.Length * 32, Allocator.Temp);
                foreach (var chunk in chunks)
                {
                    var destinationAccessor = chunk.GetBufferAccessor(ref destinationHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var buffer = destinationAccessor[i];
                        foreach (var d in buffer)
                            uniqueDestinations.Add(d);
                    }
                }

                {
                    destinations.ResizeUninitialized(uniqueDestinations.Count);
                    int i = 0;
                    foreach (var d in uniqueDestinations)
                    {
                        destinations[i] = d;
                        i++;
                    }
                }

                destinations.Sort(new Comparer());
                tunnels.Capacity = destinations.Length;
                tunnelRangesByTypeIndex.AsSpan().Clear();
                var previousTunnel = default(UnityObjectRef<GraphicsEventTunnelBase>);
                foreach (var d in destinations)
                {
                    if (d.tunnel == previousTunnel)
                        continue;
                    previousTunnel    = d.tunnel;
                    var startAndCount = tunnelRangesByTypeIndex[d.eventTypeIndex];
                    if (startAndCount.y == 0)
                        tunnelRangesByTypeIndex[d.eventTypeIndex] = new int2(tunnels.Length, 1);
                    else
                        tunnelRangesByTypeIndex[d.eventTypeIndex] = new int2(startAndCount.x, startAndCount.y + 1);
                    tunnels.Add(d.tunnel);
                }
            }

            struct Comparer : IComparer<GraphicsEventTunnelDestination>
            {
                public int Compare(GraphicsEventTunnelDestination x, GraphicsEventTunnelDestination y)
                {
                    var result = x.eventTypeIndex.CompareTo(y.eventTypeIndex);
                    if (result == 0)
                        result = x.tunnel.GetHashCode().CompareTo(y.tunnel.GetHashCode());
                    if (result == 0)
                        result = x.requestor.GetHashCode().CompareTo(y.requestor.GetHashCode());
                    return result;
                }
            }
        }

        [BurstCompile]
        struct GroupAndCountEventsJob : IJobFor
        {
            [ReadOnly] public NativeArray<UnityObjectRef<GraphicsEventTunnelBase> > tunnels;
            [ReadOnly] public NativeArray<int2>                                     tunnelRangesByTypeIndex;
            [ReadOnly] public GraphicsEventPostal                                   postal;

            public NativeArray<int>                 eventCountByTypeIndex;
            public NativeArray<UnsafeList<int2> >   eventRangesByTunnelByTypeIndex;
            public AllocatorManager.AllocatorHandle allocator;

            public void Execute(int typeIndex)
            {
                var tunnelsRange = tunnelRangesByTypeIndex[typeIndex];
                if (tunnelsRange.y == 0)
                {
                    eventCountByTypeIndex[typeIndex]          = 0;
                    eventRangesByTunnelByTypeIndex[typeIndex] = default;
                    return;
                }

                var tunnelsOfType = tunnels.GetSubArray(tunnelsRange.x, tunnelsRange.y);
                var eventRanges   = new UnsafeList<int2>(tunnelsOfType.Length, allocator);
                eventRanges.Resize(tunnelsOfType.Length, NativeArrayOptions.ClearMemory);
                var tunnelToIndexMap = new NativeHashMap<UnityObjectRef<GraphicsEventTunnelBase>, int>(eventRanges.Length, Allocator.Temp);
                int tunnelIndex      = 0;
                foreach (var tunnel in tunnelsOfType)
                {
                    tunnelToIndexMap.Add(tunnel, tunnelIndex);
                    tunnelIndex++;
                }

                ref var hashRemapper = ref GraphicsEventTypeRegistry.s_eventHashManager.Data;
                var     blocklist    = postal.blocklistPairArray[typeIndex].tunnelTargets;
                var     enumerator   = blocklist.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var target = enumerator.GetCurrent<UnityObjectRef<GraphicsEventTunnelBase> >();
                    if (tunnelToIndexMap.TryGetValue(hashRemapper[target], out var index))
                    {
                        eventRanges.ElementAt(index).y++;
                    }
                }

                int total = 0;
                for (int i = 0; i < eventRanges.Length; i++)
                {
                    ref var range  = ref eventRanges.ElementAt(i);
                    range.x        = total;
                    total         += range.y;
                    range.y        = 0;
                }

                eventCountByTypeIndex[typeIndex]          = total;
                eventRangesByTunnelByTypeIndex[typeIndex] = eventRanges;
            }
        }

        [BurstCompile]
        struct WriteEventsJob : IJobFor
        {
            [ReadOnly] public NativeArray<UnityObjectRef<GraphicsEventTunnelBase> > tunnels;
            [ReadOnly] public NativeArray<int2>                                     tunnelRangesByTypeIndex;
            [ReadOnly] public GraphicsEventPostal                                   postal;

            // Technically could be ReadOnly, but that might confuse Burst's alias checker. Something to maybe experiment with later.
            public NativeArray<UnsafeList<int2> > eventRangesByTunnelByTypeIndex;
            public NativeArray<UnsafeList<byte> > buffers;

            public unsafe void Execute(int typeIndex)
            {
                var tunnelsRange = tunnelRangesByTypeIndex[typeIndex];
                if (tunnelsRange.y == 0)
                    return;

                var tunnelsOfType    = tunnels.GetSubArray(tunnelsRange.x, tunnelsRange.y);
                var eventRanges      = eventRangesByTunnelByTypeIndex[typeIndex];
                var tunnelToIndexMap = new NativeHashMap<UnityObjectRef<GraphicsEventTunnelBase>, int>(eventRanges.Length, Allocator.Temp);
                int tunnelIndex      = 0;
                foreach (var tunnel in tunnelsOfType)
                {
                    tunnelToIndexMap.Add(tunnel, tunnelIndex);
                    tunnelIndex++;
                }

                ref var hashRemapper     = ref GraphicsEventTypeRegistry.s_eventHashManager.Data;
                var     blocklistPair    = postal.blocklistPairArray[typeIndex];
                var     targetEnumerator = blocklistPair.tunnelTargets.GetEnumerator();
                var     eventEnumerator  = blocklistPair.events.GetEnumerator();
                var     eventSize        = blocklistPair.events.elementSize;
                var     buffer           = buffers[typeIndex];
                while (targetEnumerator.MoveNext())
                {
                    eventEnumerator.MoveNext();
                    var target = targetEnumerator.GetCurrent<UnityObjectRef<GraphicsEventTunnelBase> >();
                    if (tunnelToIndexMap.TryGetValue(hashRemapper[target], out var index))
                    {
                        ref var range = ref eventRanges.ElementAt(index);
                        var     src   = eventEnumerator.GetCurrentPtr();
                        var     dst   = UnsafeUtility.AddressOf(ref buffer.ElementAt((range.x + range.y) * eventSize));
                        UnsafeUtility.MemCpy(dst, src, eventSize);
                        range.y++;
                    }
                }
            }
        }
        #endregion
    }
}

