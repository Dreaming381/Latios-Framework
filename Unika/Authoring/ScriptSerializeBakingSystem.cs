using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Unika.Authoring.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    [RequireMatchingQueriesForUpdate]
    public partial class ScriptSerializeBakingSystem : SystemBase
    {
        BakingSystem m_bakingSystemReference;
        EntityQuery  m_missingLiveStateQuery;
        EntityQuery  m_missingPreviousQuery;
        EntityQuery  m_staleLiveTrackingQuery;

        protected override void OnCreate()
        {
            m_bakingSystemReference = World.GetExistingSystemManaged<BakingSystem>();
            AssemblyManager.Initialize();

            m_missingLiveStateQuery  = CheckedStateRef.Fluent().With<UnikaScripts>(true).Without<UnikaLiveBakedScriptsState>().IncludePrefabs().IncludeDisabledEntities().Build();
            m_missingPreviousQuery   = CheckedStateRef.Fluent().With<UnikaScripts>(true).Without<PreviousBakedScripts>().IncludePrefabs().IncludeDisabledEntities().Build();
            m_staleLiveTrackingQuery = CheckedStateRef.Fluent().WithAnyEnabled<UnikaLiveBakedScriptsState, PreviousBakedScripts>(true).Without<UnikaScripts>()
                                       .IncludePrefabs().IncludeDisabledEntities().Build();
        }

        protected override void OnUpdate()
        {
            var types = new TypePack<UnikaLiveBakedScriptsState, PreviousBakedScripts>();
            EntityManager.RemoveComponent(m_staleLiveTrackingQuery, types);
            EntityManager.AddComponent(m_missingLiveStateQuery, types);
            EntityManager.AddComponent(m_missingPreviousQuery,  types);

            var blobTarget = new NativeReference<UnikaSerializedTypeIds>(WorldUpdateAllocator);
            var jh         = new BuildBlobJob
            {
                blobTarget = blobTarget,
                blobStore  = m_bakingSystemReference.BlobAssetStore,
            }.Schedule(Dependency);
            Dependency = new AssignBlobJob
            {
                blobTarget = blobTarget
            }.ScheduleParallel(jh);
            Dependency = new SerializeJob().ScheduleParallel(Dependency);
            Dependency = new TrackLiveChangesJob().ScheduleParallel(Dependency);

            // Todo: Unity doesn't know to complete this job because BlobAssetStore has zero dependency management. Fix this.
            jh.Complete();
        }

        [BurstCompile]
        struct BuildBlobJob : IJob
        {
            public NativeReference<UnikaSerializedTypeIds> blobTarget;
            public BlobAssetStore                          blobStore;
            public uint                                    typeHash;

            public void Execute()
            {
                var     builder = new BlobBuilder(Allocator.Temp);
                ref var root    = ref builder.ConstructRoot<UnikaSerializedTypeIdsBlob>();
                var     count   = ScriptTypeInfoManager.scriptTypeCount;
                var     array   = builder.Allocate(ref root.stableHashBySerializedTypeId, count);
                for (int i = 0; i < count; i++)
                {
                    array[i] = ScriptTypeInfoManager.GetStableHash((short)i);
                }
                var blob = builder.CreateBlobAssetReference<UnikaSerializedTypeIdsBlob>(Allocator.Persistent);
                blobStore.TryAdd(ref blob);
                blobTarget.Value = new UnikaSerializedTypeIds { blob = blob };
            }
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct AssignBlobJob : IJobEntity
        {
            [ReadOnly] public NativeReference<UnikaSerializedTypeIds> blobTarget;

            public void Execute(ref UnikaSerializedTypeIds icd) => icd = blobTarget.Value;
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct TrackLiveChangesJob : IJobEntity
        {
            public unsafe void Execute(ref UnikaLiveBakedScriptsState liveState, ref DynamicBuffer<PreviousBakedScripts> previous, in DynamicBuffer<UnikaScripts> current)
            {
                if (previous.Length != current.Length ||
                    UnsafeUtility.MemCmp(previous.GetUnsafeReadOnlyPtr(), current.GetUnsafeReadOnlyPtr(), (long)current.Length * UnsafeUtility.SizeOf<UnikaScripts>()) != 0)
                {
                    previous.Reinterpret<UnikaScripts>().CopyFrom(current);
                    liveState.ToggleChangeBit();
                }
                liveState.deserializationRequested = true;
            }
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct SerializeJob : IJobEntity
        {
            public void Execute(ref DynamicBuffer<UnikaSerializedEntityReference> entities,
                                ref DynamicBuffer<UnikaSerializedBlobReference>   blobs,
                                ref DynamicBuffer<UnikaSerializedAssetReference>  assets,
                                ref DynamicBuffer<UnikaSerializedObjectReference> objects,
                                in DynamicBuffer<UnikaScripts>                    scriptsBuffer)
            {
                var scripts = scriptsBuffer.AsNativeArray();
                ScriptSerialization.SerializeEntities(scripts, ref entities);
                ScriptSerialization.SerializeBlobs(scripts, ref blobs);
                ScriptSerialization.SerializeAssets(scripts, ref assets);
                ScriptSerialization.SerializeObjects(scripts, ref objects);
            }
        }
    }
}

