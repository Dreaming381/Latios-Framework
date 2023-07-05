using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace Latios.LifeFX.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CollectVisualEffectMailSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UploadVisualEffectMailSystem : ISystem
    {
        LatiosWorldUnmanaged                   latiosWorld;
        NativeList<EntityQuery>                m_mailboxQueries;
        GCHandle                               m_listOfProcessors;
        NativeHashMap<FixedString64Bytes, int> m_shaderPropertyMap;

        AllocatorHelper<RewindableAllocator> m_mailboxAllocator;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_mailboxAllocator = new AllocatorHelper<RewindableAllocator>(Allocator.Persistent);
            m_mailboxAllocator.Allocator.Initialize(16 * 1024);

            m_mailboxQueries    = new NativeList<EntityQuery>(Allocator.Persistent);
            m_shaderPropertyMap = new NativeHashMap<FixedString64Bytes, int>(64, Allocator.Persistent);

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

            m_listOfProcessors = GCHandle.Alloc(listOfProcessor, GCHandleType.Normal);
        }

        public void OnDestroy(ref SystemState state)
        {
            state.CompleteDependency();
            m_mailboxQueries.Dispose();
            m_shaderPropertyMap.Dispose();
            m_listOfProcessors.Free();
            m_mailboxAllocator.Allocator.Rewind();
            m_mailboxAllocator.Allocator.Dispose();
            m_mailboxAllocator.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            Part1Output part1Output = default;
            DoPart1(ref this, ref state, ref part1Output);

            var mappedBuffers = part1Output.mappedGraphicsBufferComponent.buffers;
            foreach (var mappedBuffer in mappedBuffers)
                mappedBuffer.mappedBuffer.UnlockBufferAfterWrite<byte>(mappedBuffer.length);

            foreach (var upload in part1Output.uploadCommands)
            {
                var visualEffect = latiosWorld.GetManagedStructComponent<ManagedVisualEffect>(upload.visualEffectEntity).effect;
                visualEffect.SetUInt(upload.bufferStartPropertyID, upload.start);
                visualEffect.SetUInt(upload.bufferCountPropertyID, upload.count);
                visualEffect.SetGraphicsBuffer(upload.bufferPropertyID, mappedBuffers[upload.bufferIndex].mappedBuffer);
            }

            var processors = m_listOfProcessors.Target as List<MailboxProcessorBase>;

            int chunksRunningIndex = 0;
            foreach (var mailboxTypeIndex in part1Output.mailboxTypeIndices)
            {
                processors[mailboxTypeIndex].Update(part1Output.chunksByMailbox[chunksRunningIndex], ref state, part1Output.allocator);
                chunksRunningIndex++;
            }
        }

        // Todo: This needs to be reworked, or PropertyToID needs to be Burst-compatible
        //[BurstCompile]
        static void DoPart1(ref UploadVisualEffectMailSystem system, ref SystemState state, ref Part1Output output)
        {
            var uploadCommands        = system.latiosWorld.worldBlackboardEntity.GetCollectionComponent<UploadCommands>(false).commands;
            var mappedGraphicsBuffers = system.latiosWorld.worldBlackboardEntity.GetCollectionComponent<MappedGraphicsBuffers>(false);
            state.CompleteDependency();

            system.m_mailboxAllocator.Allocator.Rewind();

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

            var remappedUploadCommands = new NativeArray<RemappedUploadCommand>(uploadCommands.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < uploadCommands.Length; i++)
            {
                ref var command = ref uploadCommands.ElementAt(i);
                if (!system.m_shaderPropertyMap.TryGetValue(command.bufferPropertyName, out var bufferPropertyID))
                {
                    bufferPropertyID = Shader.PropertyToID($"{new StringWrapper(command.bufferPropertyName)}");
                    system.m_shaderPropertyMap.Add(command.bufferPropertyName, bufferPropertyID);
                }
                if (!system.m_shaderPropertyMap.TryGetValue(command.bufferStartPropertyName, out var bufferStartPropertyID))
                {
                    bufferStartPropertyID = Shader.PropertyToID($"{new StringWrapper(command.bufferStartPropertyName)}");
                    system.m_shaderPropertyMap.Add(command.bufferStartPropertyName, bufferStartPropertyID);
                }
                if (!system.m_shaderPropertyMap.TryGetValue(command.bufferCountPropertyName, out var bufferCountPropertyID))
                {
                    bufferCountPropertyID = Shader.PropertyToID($"{new StringWrapper(command.bufferCountPropertyName)}");
                    system.m_shaderPropertyMap.Add(command.bufferCountPropertyName, bufferCountPropertyID);
                }
                remappedUploadCommands[i] = new RemappedUploadCommand
                {
                    bufferCountPropertyID = bufferCountPropertyID,
                    bufferIndex           = command.bufferIndex,
                    bufferPropertyID      = bufferPropertyID,
                    bufferStartPropertyID = bufferStartPropertyID,
                    count                 = (uint)command.count,
                    start                 = (uint)command.start,
                    visualEffectEntity    = command.visualEffectEntity
                };
            }

            output = new Part1Output
            {
                chunksByMailbox               = chunksByMailbox.AsArray(),
                mailboxTypeIndices            = mailboxTypeIndices.AsArray(),
                mappedGraphicsBufferComponent = mappedGraphicsBuffers,
                uploadCommands                = remappedUploadCommands,
                allocator                     = system.m_mailboxAllocator.Allocator.Handle
            };
        }

        struct Part1Output
        {
            public NativeArray<NativeArray<ArchetypeChunk> > chunksByMailbox;
            public NativeArray<int>                          mailboxTypeIndices;
            public MappedGraphicsBuffers                     mappedGraphicsBufferComponent;
            public NativeArray<RemappedUploadCommand>        uploadCommands;
            public AllocatorManager.AllocatorHandle          allocator;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StringWrapper
        {
            FixedString64Bytes s;

            public StringWrapper(FixedString64Bytes str) {
                s = str;
            }

            public override string ToString()
            {
                return s.ConvertToString();
            }
        }

        struct RemappedUploadCommand
        {
            public Entity visualEffectEntity;
            public int    bufferPropertyID;
            public int    bufferStartPropertyID;
            public int    bufferCountPropertyID;
            public uint   start;
            public uint   count;
            public int    bufferIndex;
        }

        abstract class MailboxProcessorBase
        {
            public abstract void Create(ref SystemState state);
            public abstract void Update(NativeArray<ArchetypeChunk> chunks, ref SystemState state, AllocatorManager.AllocatorHandle allocator);
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

            public override void Update(NativeArray<ArchetypeChunk> chunks, ref SystemState state, AllocatorManager.AllocatorHandle allocator)
            {
                m_mailboxHandle.Update(ref state);

                m_helper.initializeJob.chunks        = chunks;
                m_helper.initializeJob.mailboxHandle = m_mailboxHandle;
                m_helper.initializeJob.allocator     = allocator;
                m_helper.initializeJob.RunByRef();
            }
        }
    }
}

