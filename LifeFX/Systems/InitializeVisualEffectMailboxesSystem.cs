using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Latios.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace Latios.LifeFX.Systems
{
    [UpdateInGroup(typeof(LatiosWorldSyncGroup))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public unsafe partial struct InitializeVisualEffectMailboxesSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_allNewPrefabQuery;
        EntityQuery          m_allNewSceneQuery;
        EntityQuery          m_allDeadQuery;

        NativeList<EntityQuery> m_mailboxQueries;
        GCHandle                m_listOfProcessors;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_mailboxQueries = new NativeList<EntityQuery>(Allocator.Persistent);

            var targetInterfaceType  = typeof(IVisualEffectMailboxType);
            var processorGenericType = typeof(MailboxProcessorTyped<,>);

            var queryTypes = new NativeList<ComponentType>(Allocator.Temp);

            var builder              = new EntityQueryBuilder(Allocator.Temp);
            var mailboxQueryCache    = new FixedList32Bytes<ComponentType>();
            mailboxQueryCache.Length = 1;
            //mailboxQueryCache[1]     = ComponentType.Exclude<MailboxInitializedTag>();

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

                    m_mailboxQueries.Add(builder.WithNone<MailboxInitializedTag>().WithAll(ref mailboxQueryCache).WithOptions(EntityQueryOptions.IncludePrefab).Build(ref state));
                    builder.Reset();
                    listOfProcessor.Add(processor);
                }
            }

            m_listOfProcessors = GCHandle.Alloc(listOfProcessor, GCHandleType.Normal);

            m_allNewPrefabQuery = builder.WithAll<VisualEffect, Prefab>().WithNone<MailboxInitializedTag>().WithAll(ref queryTypes).Build(ref state);
            m_allNewSceneQuery  = builder.WithAll<VisualEffect>().WithNone<MailboxInitializedTag>().WithAll(ref queryTypes).Build(ref state);
            builder.Reset();
            m_allDeadQuery = builder.WithAll<MailboxInitializedTag>().WithNone(ref queryTypes).WithOptions(EntityQueryOptions.IncludePrefab).Build(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            m_mailboxQueries.Dispose();
            m_listOfProcessors.Free();
        }

        public void OnUpdate(ref SystemState state)
        {
            Part1Output part1Output = default;
            DoPart1(ref this, ref state, ref part1Output);

            var processors = m_listOfProcessors.Target as List<MailboxProcessorBase>;

            int chunksRunningIndex = 0;
            foreach (var mailboxTypeIndex in part1Output.mailboxTypeIndices)
            {
                processors[mailboxTypeIndex].Update(part1Output.chunksByMailbox[chunksRunningIndex], ref state, part1Output.allocator);
                chunksRunningIndex++;
            }

            NativeArray<Entity> prefabs = default;
            NativeArray<Entity> scenes  = default;
            DoPart3(ref this, ref state, ref prefabs, ref scenes);

            foreach (var entity in prefabs)
            {
                var baseEffect = state.EntityManager.GetComponentObject<VisualEffect>(entity);
                latiosWorld.AddManagedStructComponent(entity, new ManagedVisualEffect
                {
                    effect      = GameObject.Instantiate(baseEffect),
                    isFromScene = false
                });
            }

            foreach (var entity in scenes)
            {
                latiosWorld.AddManagedStructComponent(entity, new ManagedVisualEffect
                {
                    effect      = state.EntityManager.GetComponentObject<VisualEffect>(entity),
                    isFromScene = true
                });
            }
        }

        [BurstCompile]
        static void DoPart1(ref InitializeVisualEffectMailboxesSystem system, ref SystemState state, ref Part1Output output)
        {
            state.CompleteDependency();

            var chunksByMailbox    = new NativeList<NativeArray<ArchetypeChunk> >(Allocator.Temp);
            var mailboxTypeIndices = new NativeList<int>(Allocator.Temp);
            var allocator          = system.latiosWorld.worldBlackboardEntity.GetComponentData<MailboxAllocator>().allocator;

            for (int typeIndex = 0; typeIndex < system.m_mailboxQueries.Length; typeIndex++)
            {
                ref var query = ref system.m_mailboxQueries.ElementAt(typeIndex);
                if (query.IsEmptyIgnoreFilter)
                    continue;

                mailboxTypeIndices.Add(typeIndex);
                chunksByMailbox.Add(query.ToArchetypeChunkArray(state.WorldUpdateAllocator));
            }

            output = new Part1Output
            {
                chunksByMailbox    = chunksByMailbox.AsArray(),
                mailboxTypeIndices = mailboxTypeIndices.AsArray(),
                allocator          = allocator
            };
        }

        [BurstCompile]
        static void DoPart3(ref InitializeVisualEffectMailboxesSystem system, ref SystemState state, ref NativeArray<Entity> outputPrefab, ref NativeArray<Entity> outputScene)
        {
            state.EntityManager.RemoveComponent<MailboxInitializedTag>(system.m_allDeadQuery);
            outputPrefab = system.m_allNewPrefabQuery.ToEntityArray(Allocator.Temp);
            outputScene  = system.m_allNewSceneQuery.ToEntityArray(Allocator.Temp);
            state.EntityManager.AddComponent<MailboxInitializedTag>(system.m_allNewPrefabQuery);
            state.EntityManager.AddComponent<MailboxInitializedTag>(system.m_allNewSceneQuery);
        }

        struct Part1Output
        {
            public NativeArray<NativeArray<ArchetypeChunk> > chunksByMailbox;
            public NativeArray<int>                          mailboxTypeIndices;
            public AllocatorManager.AllocatorHandle          allocator;
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

namespace Latios.LifeFX
{
    public partial struct VisualEffectMailboxHelper<TMailboxType, TMessageType> where TMailboxType : unmanaged,
           IVisualEffectMailbox<TMessageType> where TMessageType : unmanaged
    {
        [BurstCompile]
        internal struct InitializeJob : IJob
        {
            public NativeArray<ArchetypeChunk>       chunks;
            public ComponentTypeHandle<TMailboxType> mailboxHandle;
            public AllocatorManager.AllocatorHandle  allocator;

            public unsafe void Execute()
            {
                var messageSize      = UnsafeUtility.SizeOf<TMessageType>();
                var messagesPerBlock = math.max(4096 / messageSize, 64);
                foreach (var chunk in chunks)
                {
                    var mailboxes = chunk.GetComponentDataPtrRW(ref mailboxHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        mailboxes[i].mailboxStorage = new MailboxStorage { storage = new UnsafeParallelBlockList(messageSize, messagesPerBlock, allocator) };
                    }
                }
            }
        }

        internal InitializeJob initializeJob;
    }
}

