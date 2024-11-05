using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Latios.Kinemation;
using Latios.Kinemation.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

using static Unity.Entities.SystemAPI;

namespace Latios.LifeFX.Systems
{
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(DispatchRoundRobinLateExtensionsSuperSystem))]
    [UpdateAfter(typeof(GraphicsEventUploadSystem))]  // This only exists to improve job scheduling, but is not a strict requirement
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct GraphicsGlobalBufferBroadcastSystem : ISystem, ICullingComputeDispatchSystem<GraphicsGlobalBufferBroadcastSystem.CollectState,
                                                                                                       GraphicsGlobalBufferBroadcastSystem.WriteState>
    {
        LatiosWorldUnmanaged latiosWorld;

        CullingComputeDispatchData<CollectState, WriteState> m_data;
        EntityQuery                                          m_destinationsQuery;
        int                                                  _latiosDeformBuffer;
        int                                                  _latiosBoneTransforms;

        delegate void DispatchDestinationDelegate(IntPtr dispatchData);
        static DispatchDestinationDelegate                                          s_managedDelegate;
        static bool                                                                 s_initialized;
        static readonly SharedStatic<FunctionPointer<DispatchDestinationDelegate> > s_functionPointer = SharedStatic<FunctionPointer<DispatchDestinationDelegate> >.GetOrCreate<GraphicsGlobalBufferBroadcastSystem>();

        #region System Methods
        public void OnCreate(ref SystemState state)
        {
            Initialize();
            latiosWorld         = state.GetLatiosWorldUnmanaged();
            m_data              = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorld);
            m_destinationsQuery = state.Fluent().With<GraphicsGlobalBufferDestination>(true).Build();

            var propertyBufferMap = new NativeHashMap<int,
                                                      GraphicsBufferUnmanaged>(
                32,
                Allocator.Persistent);
            _latiosDeformBuffer =
                UnityEngine.Shader.PropertyToID("_latiosDeformBuffer");
            _latiosBoneTransforms =
                UnityEngine.Shader.PropertyToID("_latiosBoneTransforms");
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new ShaderPropertyToGlobalBufferMap { shaderPropertyToGlobalBufferMap = propertyBufferMap });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_data.DoUpdate(ref state, ref this);
        }

        public CollectState Collect(ref SystemState state)
        {
            if (latiosWorld.worldBlackboardEntity.GetComponentData<DispatchContext>().dispatchIndexThisFrame != 0)
            {
                return default;
            }

            // Normally, we'd want to use our custom allocator. However, we need some of these arrays to stay valid while we are
            // dispatching to Mono, because user code could interact with entities and produce new events within that time, which
            // means we need to rewind before Mono dispatch.
            var allocator    = state.WorldUpdateAllocator;
            var destinations = new NativeList<GraphicsGlobalBufferDestination>(allocator);

            var job = new CollectDestinationsJob
            {
                chunks            = m_destinationsQuery.ToArchetypeChunkListAsync(allocator, out var jh).AsDeferredJobArray(),
                destinationHandle = GetBufferTypeHandle<GraphicsGlobalBufferDestination>(true),
                destinations      = destinations,
            };
            state.Dependency = job.Schedule(JobHandle.CombineDependencies(state.Dependency, jh));

            return new CollectState
            {
                destinations = destinations,
            };
        }

        public unsafe WriteState Write(ref SystemState state, ref CollectState collected)
        {
            return new WriteState
            {
                destinations = collected.destinations,
                broker       = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>()
            };
        }

        public void Dispatch(ref SystemState state, ref WriteState written)
        {
            if (!written.destinations.IsCreated)
                return;

            var                     propertyMap      = latiosWorld.worldBlackboardEntity.GetCollectionComponent<ShaderPropertyToGlobalBufferMap>(false);
            var                     broker           = written.broker;
            var                     previousProperty = 0;
            GraphicsBufferUnmanaged currentBuffer    = default;
            foreach (var destination in written.destinations)
            {
                if (destination.shaderPropertyId != previousProperty)
                {
                    previousProperty = destination.shaderPropertyId;
                    if (previousProperty == _latiosDeformBuffer)
                        currentBuffer = broker.GetDeformedVerticesBuffer();
                    else if (previousProperty == _latiosBoneTransforms)
                        currentBuffer = broker.GetSkinningTransformsBuffer();
                    else
                        propertyMap.shaderPropertyToGlobalBufferMap.TryGetValue(previousProperty, out currentBuffer);
                }
                DispatchManaged(destination.requestor, currentBuffer);
            }
        }
        #endregion

        #region Mono Interop
        static unsafe void DispatchManaged(UnityObjectRef<GraphicsGlobalBufferReceptor> requestor, GraphicsBufferUnmanaged buffer)
        {
            bool isBurst = true;
            DispatchManagedFromManaged(requestor, buffer, ref isBurst);
            if (isBurst)
            {
                var dispatchData = new DispatchData
                {
                    requestor = requestor,
                    buffer    = buffer,
                };
                s_functionPointer.Data.Invoke((IntPtr)(&dispatchData));
            }
        }

        [BurstDiscard]
        static void DispatchManagedFromManaged(UnityObjectRef<GraphicsGlobalBufferReceptor> requestor, GraphicsBufferUnmanaged buffer, ref bool isBurst)
        {
            isBurst = false;
            requestor.Value.PublishInternal(buffer.ToManaged());
        }

        static unsafe void DispatchManaged(IntPtr dispatchData)
        {
            ref var data = ref *(DispatchData*)dispatchData;
            data.requestor.Value.PublishInternal(data.buffer.ToManaged());
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
            internal NativeList<GraphicsGlobalBufferDestination> destinations;
        }

        public struct WriteState
        {
            internal NativeList<GraphicsGlobalBufferDestination> destinations;
            internal GraphicsBufferBroker                        broker;
        }

        struct DispatchData
        {
            public UnityObjectRef<GraphicsGlobalBufferReceptor> requestor;
            public GraphicsBufferUnmanaged                      buffer;
        }
        #endregion

        #region Jobs
        [BurstCompile]
        struct CollectDestinationsJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>                       chunks;
            [ReadOnly] public BufferTypeHandle<GraphicsGlobalBufferDestination> destinationHandle;

            public NativeList<GraphicsGlobalBufferDestination> destinations;

            public void Execute()
            {
                var uniqueDestinations = new NativeHashSet<GraphicsGlobalBufferDestination>(chunks.Length * 32, Allocator.Temp);
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
            }

            struct Comparer : IComparer<GraphicsGlobalBufferDestination>
            {
                public int Compare(GraphicsGlobalBufferDestination x, GraphicsGlobalBufferDestination y)
                {
                    var result = x.shaderPropertyId.CompareTo(y.shaderPropertyId);
                    if (result == 0)
                        result = x.requestor.GetHashCode().CompareTo(y.requestor.GetHashCode());
                    return result;
                }
            }
        }
        #endregion
    }
}

