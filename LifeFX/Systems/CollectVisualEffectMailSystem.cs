using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.LifeFX.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public unsafe partial struct CollectVisualEffectMailSystem : ISystem
    {
        LatiosWorldUnmanaged     latiosWorld;
        NativeList<EntityQuery>  m_mailboxQueries;
        GCHandle                 m_listOfProcessors;
        GCHandle                 m_listOfBufferRotations;
        NativeHashMap<long, int> m_typeHashToMessageIndexMap;

        MappedGraphicsBuffers m_mappedBuffersToFree;

        int m_rotation;
        int m_rotationCount;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_mailboxQueries            = new NativeList<EntityQuery>(Allocator.Persistent);
            m_typeHashToMessageIndexMap = new NativeHashMap<long, int>(16, Allocator.Persistent);

            var targetInterfaceType  = typeof(IVisualEffectMailboxType);
            var processorGenericType = typeof(MailboxProcessorTyped<,>);

            var queryTypes = new NativeList<ComponentType>(Allocator.Temp);

            var builder              = new EntityQueryBuilder(Allocator.Temp);
            var mailboxQueryCache    = new FixedList32Bytes<ComponentType>();
            mailboxQueryCache.Length = 2;
            mailboxQueryCache[1]     = ComponentType.ReadOnly<MailboxInitializedTag>();

            var listOfProcessor = new List<MailboxProcessorBase>();

            foreach (var type in TypeManager.AllTypes)
            {
                if (type.Category != TypeManager.TypeCategory.ComponentData)
                    continue;
                if (type.IsZeroSized)
                    continue;
                if (type.BakingOnlyType)
                    continue;
                if (type.TemporaryBakingType)
                    continue;
                if (type.TypeIndex.IsManagedType)
                    continue;
                if (type.TypeIndex.IsCleanupComponent)
                    continue;

                if (targetInterfaceType.IsAssignableFrom(type.Type))
                {
                    var compType = ComponentType.FromTypeIndex(type.TypeIndex);
                    queryTypes.Add(compType);

                    mailboxQueryCache[0] = compType;

                    var eventType = (Activator.CreateInstance(type.Type) as IVisualEffectMailboxType).messageType;
                    var processor = Activator.CreateInstance(processorGenericType.MakeGenericType(type.Type, eventType)) as MailboxProcessorBase;
                    processor.Create(ref state);

                    m_mailboxQueries.Add(builder.WithAll(ref mailboxQueryCache).WithOptions(EntityQueryOptions.IncludePrefab).Build(ref state));
                    builder.Reset();
                    listOfProcessor.Add(processor);
                }
            }

            m_listOfProcessors      = GCHandle.Alloc(listOfProcessor, GCHandleType.Normal);
            m_listOfBufferRotations = GCHandle.Alloc(new List<GraphicsBufferRotation>(), GCHandleType.Normal);

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new UploadCommands {
                commands = new NativeList<UploadCommands.Command>(Allocator.Persistent)
            });
            m_mappedBuffersToFree = MappedGraphicsBuffers.Create();
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(m_mappedBuffersToFree);

            m_rotation      = -1;
            m_rotationCount = math.max(QualitySettings.maxQueuedFrames, math.select(3, 4, SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal ||
                                                                                    SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Switch));
        }

        public void OnDestroy(ref SystemState state)
        {
            m_mailboxQueries.Dispose();
            var buffers = m_listOfBufferRotations.Target as List<GraphicsBufferRotation>;
            foreach (var buffer in buffers)
                buffer.Dispose();
            m_listOfBufferRotations.Free();
            m_listOfProcessors.Free();
            m_typeHashToMessageIndexMap.Dispose();
            m_mappedBuffersToFree.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            Part1Output part1Output = default;
            DoPart1(ref this, ref state, ref part1Output);

            var processors = m_listOfProcessors.Target as List<MailboxProcessorBase>;

            int chunksRunningIndex = 0;
            foreach (var mailboxTypeIndex in part1Output.mailboxTypeIndices)
            {
                processors[mailboxTypeIndex].Update(part1Output.chunksByMailbox[chunksRunningIndex], ref state, part1Output.context);
                chunksRunningIndex++;
            }

            var bufferRotations = m_listOfBufferRotations.Target as List<GraphicsBufferRotation>;
            foreach (var newBuffer in part1Output.context.newBufferElementSizes)
            {
                bufferRotations.Add(new GraphicsBufferRotation(m_rotationCount, newBuffer));
            }

            var mappedGraphicsBufferList = part1Output.mappedGraphicsBufferComponent.buffers;
            mappedGraphicsBufferList.Clear();
            var bufferPtrs = CollectionHelper.CreateNativeArray<GraphicsBufferPtr>(m_typeHashToMessageIndexMap.Count,
                                                                                   state.WorldUpdateAllocator,
                                                                                   NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < m_typeHashToMessageIndexMap.Count; i++)
            {
                var mapped = bufferRotations[i].Acquire(m_rotation,
                                                        part1Output.context.requiredBufferSizesByBufferIndex[i]);
                mappedGraphicsBufferList.Add(new MappedGraphicsBuffers.MappedBuffer { length = mapped.mapLength, mappedBuffer = mapped.buffer });
                bufferPtrs[i]                                                                                                 = mapped.ptr;
            }

            state.Dependency = new Job
            {
                buffers        = bufferPtrs,
                copyOperations = part1Output.context.copyOperations.AsArray(),
                uploadCommands = part1Output.context.uploadCommands.AsArray()
            }.ScheduleParallel(part1Output.context.copyOperations.Length, 1, default);
        }

        [BurstCompile]
        static void DoPart1(ref CollectVisualEffectMailSystem system, ref SystemState state, ref Part1Output output)
        {
            system.m_rotation++;
            system.m_rotation %= system.m_rotationCount;

            var uploadCommands        = system.latiosWorld.worldBlackboardEntity.GetCollectionComponent<UploadCommands>(false).commands;
            var mappedGraphicsBuffers = system.latiosWorld.worldBlackboardEntity.GetCollectionComponent<MappedGraphicsBuffers>(false);
            state.CompleteDependency();

            uploadCommands.Clear();

            var chunksByMailbox    = new NativeList<NativeArray<ArchetypeChunk> >(Allocator.Temp);
            var mailboxTypeIndices = new NativeList<int>(Allocator.Temp);

            for (int typeIndex = 0; typeIndex < system.m_mailboxQueries.Length; typeIndex++)
            {
                ref var query = ref system.m_mailboxQueries.ElementAt(typeIndex);
                if (query.IsEmptyIgnoreFilter)
                    continue;

                mailboxTypeIndices.Add(typeIndex);
                chunksByMailbox.Add(query.ToArchetypeChunkArray(state.WorldUpdateAllocator));
            }

            var bufferSizes = new NativeList<int>(system.m_typeHashToMessageIndexMap.Count, state.WorldUpdateAllocator);
            bufferSizes.Resize(system.m_typeHashToMessageIndexMap.Count, NativeArrayOptions.ClearMemory);

            output = new Part1Output
            {
                chunksByMailbox               = chunksByMailbox.AsArray(),
                mailboxTypeIndices            = mailboxTypeIndices.AsArray(),
                mappedGraphicsBufferComponent = mappedGraphicsBuffers,
                context                       = new CollectHelperContext
                {
                    copyOperations                   = new NativeList<CopyOperation>(state.WorldUpdateAllocator),
                    uploadCommands                   = uploadCommands,
                    entityHandle                     = state.GetEntityTypeHandle(),
                    newBufferElementSizes            = new NativeList<int>(state.WorldUpdateAllocator),
                    requiredBufferSizesByBufferIndex = bufferSizes,
                    typeHashToMessageIndexMap        = system.m_typeHashToMessageIndexMap
                }
            };
        }

        struct Part1Output
        {
            public NativeArray<NativeArray<ArchetypeChunk> > chunksByMailbox;
            public NativeArray<int>                          mailboxTypeIndices;
            public MappedGraphicsBuffers                     mappedGraphicsBufferComponent;
            public CollectHelperContext                      context;
        }

        [BurstCompile]
        struct Job : IJobFor
        {
            public NativeArray<GraphicsBufferPtr>      buffers;
            public NativeArray<UploadCommands.Command> uploadCommands;
            public NativeArray<CopyOperation>          copyOperations;

            public unsafe void Execute(int i)
            {
                var uploadCommand  = uploadCommands[i];
                var copyOperation  = copyOperations[i];
                var buffer         = (byte*)buffers[uploadCommand.bufferIndex].ptr;
                buffer            += copyOperation.elementSize * uploadCommand.start;
                copyOperation.messages.CopyElementsRaw(buffer);
            }
        }

        unsafe struct GraphicsBufferPtr
        {
            [NativeDisableUnsafePtrRestriction] public void* ptr;
        }

        class GraphicsBufferRotation : IDisposable
        {
            GraphicsBuffer[] m_buffers;
            int              m_elementSize;

            public GraphicsBufferRotation(int rotationCount, int elementSize)
            {
                m_buffers     = new GraphicsBuffer[rotationCount];
                m_elementSize = elementSize;

                for (int i = 0; i < rotationCount; i++)
                {
                    m_buffers[i] = null;
                }
            }

            public unsafe Mapped Acquire(int index, int count)
            {
                count = math.max(count, 16);
                if (m_buffers[index] == null)
                {
                    m_buffers[index] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, math.ceilpow2(count), m_elementSize);
                }
                else if (m_buffers[index].count < count)
                {
                    m_buffers[index].Dispose();
                    m_buffers[index] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, math.ceilpow2(count), m_elementSize);
                }
                return new Mapped
                {
                    buffer    = m_buffers[index],
                    ptr       = new GraphicsBufferPtr { ptr = m_buffers[index].LockBufferForWrite<byte>(0, count * m_elementSize).GetUnsafePtr() },
                    mapLength = count * m_elementSize
                };
            }

            public void Dispose()
            {
                foreach (var buffer in m_buffers)
                {
                    buffer?.Dispose();
                }
            }

            public struct Mapped
            {
                public GraphicsBuffer    buffer;
                public GraphicsBufferPtr ptr;
                public int               mapLength;
            }
        }

        abstract class MailboxProcessorBase
        {
            public abstract void Create(ref SystemState state);
            public abstract void Update(NativeArray<ArchetypeChunk> chunks, ref SystemState state, in CollectHelperContext context);
        }

        class MailboxProcessorTyped<T, U> : MailboxProcessorBase where T : unmanaged, IVisualEffectMailbox<U> where U : unmanaged
        {
            VisualEffectMailboxHelper<T, U> m_helper;
            ComponentTypeHandle<T>          m_mailboxHandle;

            public override void Create(ref SystemState state)
            {
                m_mailboxHandle = state.GetComponentTypeHandle<T>();
                if (new T().Register().GetMailboxType() != typeof(T))
                    throw new InvalidOperationException($"Visual Effect Mailbox type {typeof(T).FullName} uses the wrong type in Register()");
            }

            public override void Update(NativeArray<ArchetypeChunk> chunks, ref SystemState state, in CollectHelperContext context)
            {
                m_mailboxHandle.Update(ref state);

                m_helper.collectJob.chunks        = chunks;
                m_helper.collectJob.mailboxHandle = m_mailboxHandle;
                m_helper.collectJob.context       = context;
                m_helper.collectJob.RunByRef();
            }
        }

        internal struct CollectHelperContext
        {
            public EntityTypeHandle                   entityHandle;
            public NativeHashMap<long, int>           typeHashToMessageIndexMap;
            public NativeList<UploadCommands.Command> uploadCommands;
            public NativeList<CopyOperation>          copyOperations;
            public NativeList<int>                    requiredBufferSizesByBufferIndex;
            public NativeList<int>                    newBufferElementSizes;
        }

        internal struct CopyOperation
        {
            public UnsafeParallelBlockList messages;
            public int                     elementSize;
        }
    }
}

namespace Latios.LifeFX
{
    public partial struct VisualEffectMailboxHelper<TMailboxType, TMessageType> where TMailboxType : unmanaged,
           IVisualEffectMailbox<TMessageType> where TMessageType : unmanaged
    {
        [BurstCompile]
        internal struct CollectJob : IJob
        {
            public NativeArray<ArchetypeChunk>       chunks;
            public ComponentTypeHandle<TMailboxType> mailboxHandle;

            public Systems.CollectVisualEffectMailSystem.CollectHelperContext context;

            public unsafe void Execute()
            {
                var  messageSize    = UnsafeUtility.SizeOf<TMessageType>();
                bool needsNewBuffer = !context.typeHashToMessageIndexMap.TryGetValue(BurstRuntime.GetHashCode64<TMessageType>(), out var bufferIndex);
                if (needsNewBuffer)
                {
                    bufferIndex = context.typeHashToMessageIndexMap.Count;
                    context.newBufferElementSizes.Add(messageSize);
                    context.requiredBufferSizesByBufferIndex.Add(0);
                    context.typeHashToMessageIndexMap.Add(BurstRuntime.GetHashCode64<TMessageType>(), bufferIndex);
                }

                foreach (var chunk in chunks)
                {
                    var mailboxes = chunk.GetComponentDataPtrRW(ref mailboxHandle);
                    var entities  = chunk.GetEntityDataPtrRO(context.entityHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var messages                = mailboxes[i].mailboxStorage.storage;
                        mailboxes[i].mailboxStorage = default;
                        var messageCount            = messages.Count();

                        ref var bufferCounter = ref context.requiredBufferSizesByBufferIndex.ElementAt(bufferIndex);
                        context.uploadCommands.Add(new UploadCommands.Command
                        {
                            bufferCountPropertyName = mailboxes[i].bufferCountPropertyName,
                            bufferIndex             = bufferIndex,
                            bufferPropertyName      = mailboxes[i].bufferPropertyName,
                            bufferStartPropertyName = mailboxes[i].bufferStartPropertyName,
                            count                   = messageCount,
                            start                   = bufferCounter,
                            visualEffectEntity      = entities[i]
                        });
                        context.copyOperations.Add(new Systems.CollectVisualEffectMailSystem.CopyOperation
                        {
                            elementSize = messageSize,
                            messages    = messages
                        });
                        bufferCounter += messageCount;
                    }
                }
            }
        }

        internal CollectJob collectJob;
    }
}

