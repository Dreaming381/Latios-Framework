using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    /// <summary>
    /// A collection component that lives on the worldBlackboardEntity.
    /// Retrieve it with Read-Only access to send GPU events. It is safe to send events from multiple jobs at once.
    /// </summary>
    public partial struct GraphicsEventPostal : ICollectionComponent
    {
        /// <summary>
        /// A mailbox which allows for sending GPU events of a specific type. Fetch this from the GraphicsEventPostal
        /// either within a job or at schedule time.
        /// </summary>
        /// <typeparam name="T">The type of the GPU event that can be sent with this mailbox</typeparam>
        public struct Mailbox<T> where T : unmanaged
        {
            internal NativeArray<BlocklistPair> blocklistPair;
            [NativeSetThreadIndex] internal int threadIndex;

            /// <summary>
            /// Send an event to the GPU at the specific tunnel address. Any GameObject that consumes the tunnel
            /// will have access to a buffer with all events for that tunnel.
            /// </summary>
            /// <typeparam name="TunnelType">The type of tunnel ScriptableObject capable of consuming the event type</typeparam>
            /// <param name="graphicsEvent">The event payload</param>
            /// <param name="tunnel">An unmanaged reference to the tunnel ScriptableObject that a GameObject has access to</param>
            public unsafe void Send<TunnelType>(in T graphicsEvent, UnityObjectRef<TunnelType> tunnel) where TunnelType : GraphicsEventTunnel<T>
            {
                var bl = blocklistPair[0];
                bl.events.Write(graphicsEvent, threadIndex);
                bl.tunnelTargets.Write(tunnel, threadIndex);
            }
        }

        /// <summary>
        /// Returns a mailbox which can receive events of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of GPU event to send</typeparam>
        public Mailbox<T> GetMailbox<T>() where T : unmanaged
        {
            return new Mailbox<T>
            {
                threadIndex   = threadIndex,
                blocklistPair = blocklistPairArray.GetSubArray(GraphicsEventTypeRegistry.TypeToIndex<T>.typeToIndex, 1)
            };
        }

        /// <summary>
        /// Send an event to the GPU at the specific tunnel address. Any GameObject that consumes the tunnel
        /// will have access to a buffer with all events for that tunnel.
        /// </summary>
        /// <typeparam name="TTunnelType">The type of tunnel ScriptableObject capable of consuming the event type</typeparam>
        /// <typeparam name="TEventType">The type of the event to send</typeparam>
        /// <param name="graphicsEvent">The event payload</param>
        /// <param name="tunnel">An unmanaged reference to the tunnel ScriptableObject that a GameObject has access to</param>
        public void Send<TTunnelType, TEventType>(in TEventType graphicsEvent,
                                                  UnityObjectRef<TTunnelType> tunnel) where TEventType : unmanaged where TTunnelType : GraphicsEventTunnel<TEventType>
        {
            GetMailbox<TEventType>().Send(in graphicsEvent, tunnel);
        }

        public JobHandle TryDispose(JobHandle inputDeps) => inputDeps;  // Allocated with custom rewindable allocator

        internal struct BlocklistPair
        {
            public UnsafeParallelBlockList tunnelTargets;
            public UnsafeParallelBlockList events;
        }

        internal NativeArray<BlocklistPair> blocklistPairArray;
        [NativeSetThreadIndex] internal int threadIndex;

        internal GraphicsEventPostal(AllocatorManager.AllocatorHandle allocator)
        {
            var typeCount      = GraphicsEventTypeRegistry.s_eventMetadataList.Data.Length;
            blocklistPairArray = CollectionHelper.CreateNativeArray<BlocklistPair>(typeCount, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < blocklistPairArray.Length; i++)
            {
                var eventSize         = GraphicsEventTypeRegistry.s_eventMetadataList.Data[i].size;
                blocklistPairArray[i] = new BlocklistPair
                {
                    tunnelTargets = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<UnityObjectRef<GraphicsEventTunnelBase> >(), 1024, allocator),
                    events        = new UnsafeParallelBlockList(eventSize, 1024, allocator)
                };
            }
            threadIndex = 0;
        }
    }

    // Wanted to check to ensure Burst could resolve managed inheritance type constraints in generics.
    // Seems it can handle it just fine!
    //[Unity.Burst.BurstCompile]
    //internal static class BurstManagedGenericTestClass
    //{
    //    [Unity.Burst.BurstCompile]
    //    public static unsafe void TryThis(GraphicsEventPostal* postal)
    //    {
    //        postal->GetMailbox<int>().Send<SpawnEventTunnel>(0, default);
    //        postal->GetMailbox<int>().Send<GraphicsEventTunnel<int> >(1, default);
    //        postal->Send(0, default(UnityObjectRef<SpawnEventTunnel>));
    //    }
    //
    //    public static unsafe void TryThisFromManaged(GraphicsEventPostal* postal) => TryThis(postal);
    //}
}

