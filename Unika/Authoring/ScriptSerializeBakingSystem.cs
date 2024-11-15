using Unity.Burst;
using Unity.Collections;
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

        protected override void OnCreate()
        {
            m_bakingSystemReference = World.GetExistingSystemManaged<BakingSystem>();
            AssemblyManager.Initialize();
        }

        protected override void OnUpdate()
        {
            var blobTarget = new NativeReference<UnikaSerializedTypeIds>(WorldUpdateAllocator);
            var jh         = new BuildBlobJob
            {
                blobTarget = blobTarget,
                blobStore  = m_bakingSystemReference.BlobAssetStore,
                typeHash   = m_bakingSystemReference.BlobAssetStore.GetTypeHashForBurst<UnikaSerializedTypeIdsBlob>()
            }.Schedule(Dependency);
            Dependency = new AssignBlobJob
            {
                blobTarget = blobTarget
            }.ScheduleParallel(jh);
            Dependency = new SerializeJob().ScheduleParallel(Dependency);

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
                blobStore.TryAddBlobAssetWithBurstHash(typeHash, ref blob);
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

