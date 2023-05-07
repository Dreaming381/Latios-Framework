using System.Collections.Generic;
using System.Linq;
using GameObject = UnityEngine.GameObject;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;

namespace Latios.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    internal partial class SmartBlobberTypedPostProcessBakingSystem<TBlobType> : SystemBase where TBlobType : unmanaged
    {
        EntityQuery  m_query;
        BakingSystem m_bakingSystemReference;

        ComponentTypeHandle<SmartBlobberResult>       m_resultHandle;
        ComponentTypeHandle<SmartBlobberTrackingData> m_trackingDataHandle;
        EntityTypeHandle                              m_entityHandle;

        protected override void OnCreate()
        {
            m_query = new EntityQueryBuilder(Allocator.Temp)
                      .WithAll<SmartBlobberResult>()
                      .WithAll<SmartBlobberTrackingData>()
                      .WithAll<SmartBlobberBlobTypeHash>()
                      .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                      .Build(this);
            m_query.SetSharedComponentFilter(new SmartBlobberBlobTypeHash { hash = BurstRuntime.GetHashCode64<TBlobType>() });
            m_bakingSystemReference                                              = World.GetExistingSystemManaged<BakingSystem>();

            m_resultHandle       = GetComponentTypeHandle<SmartBlobberResult>(false);
            m_trackingDataHandle = GetComponentTypeHandle<SmartBlobberTrackingData>(false);
            m_entityHandle       = GetEntityTypeHandle();
        }

        protected sealed override void OnUpdate()
        {
            // Step 1: Update handles
            var blobAssetStore = m_bakingSystemReference.BlobAssetStore;
            var typeHash       = blobAssetStore.GetTypeHashForBurst<TBlobType>();

            m_resultHandle.Update(this);
            m_trackingDataHandle.Update(this);
            m_entityHandle.Update(this);

            // Step 2: Build blobs and hashes
            Dependency = new ComputeHashesJob
            {
                resultHandle       = m_resultHandle,
                trackingDataHandle = m_trackingDataHandle,
                entityHandle       = m_entityHandle
            }.ScheduleParallel(m_query, Dependency);

            // Step 3: Filter with BlobAssetStore and Deduplicate
            Dependency = new SmartBlobberTools<TBlobType>.DeduplicateBlobsWithBlobAssetStoreJob
            {
                blobAssetStore     = blobAssetStore,
                burstTypeHash      = typeHash,
                resultHandle       = m_resultHandle,
                trackingDataHandle = m_trackingDataHandle,
            }.Schedule(m_query, Dependency);
        }
    }

    [BurstCompile]
    struct ComputeHashesJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<SmartBlobberResult> resultHandle;
        public ComponentTypeHandle<SmartBlobberTrackingData>      trackingDataHandle;
        public EntityTypeHandle                                   entityHandle;

        public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var resultArray       = chunk.GetNativeArray(ref resultHandle);
            var trackingDataArray = chunk.GetNativeArray(ref trackingDataHandle);
            var entityArray       = chunk.GetNativeArray(entityHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var blob = resultArray[i].blob;
                blob.Reinterpret<int>().GetUnsafePtr();  // Invokes ValidateAllowNull()
                if (blob.Reinterpret<int>() == BlobAssetReference<int>.Null)
                {
                    var trackingData         = trackingDataArray[i];
                    trackingData.isNull      = true;
                    trackingData.isFinalized = true;
                    trackingData.thisEntity  = entityArray[i];
                    trackingDataArray[i]     = trackingData;
                }
                else
                {
                    var length               = blob.GetLength();
                    var hash32               = (uint)(blob.GetHash64() & 0xffffffff);
                    var hash64               = xxHash3.Hash64(blob.Reinterpret<int>().GetUnsafePtr(), length);
                    var hash128              = new Hash128(hash64.x, hash64.y, (uint)length, hash32);
                    var trackingData         = trackingDataArray[i];
                    trackingData.hash        = hash128;
                    trackingData.isNull      = false;
                    trackingData.isFinalized = true;
                    trackingData.thisEntity  = entityArray[i];
                    trackingDataArray[i]     = trackingData;
                }
            }
        }
    }
}

namespace Latios.Authoring
{
    [BurstCompile]
    public partial struct SmartBlobberTools<TBlobType> where TBlobType : unmanaged
    {
        [BurstCompile]
        internal struct DeduplicateBlobsWithBlobAssetStoreJob : IJobChunk
        {
            public BlobAssetStore                                blobAssetStore;
            public ComponentTypeHandle<SmartBlobberTrackingData> trackingDataHandle;
            public ComponentTypeHandle<SmartBlobberResult>       resultHandle;
            public uint                                          burstTypeHash;

            [NativeDisableContainerSafetyRestriction] NativeHashSet<Hash128> disposedBlobs;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!disposedBlobs.IsCreated)
                    disposedBlobs = new NativeHashSet<Hash128>(128, Allocator.Temp);

                var trackingDatas = chunk.GetNativeArray(ref trackingDataHandle);
                var blobs         = chunk.GetNativeArray(ref resultHandle).Reinterpret<UnsafeUntypedBlobAssetReference>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    var td = trackingDatas[i];
                    if (td.isNull)
                        continue;

                    if (disposedBlobs.Contains(td.hash))
                    {
                        if (!blobAssetStore.TryGetBlobAssetWithBurstHash<TBlobType>(td.hash, burstTypeHash, out var storedBlob))
                        {
                            UnityEngine.Debug.LogError($"Blob hash {td.hash} was lost in BlobAssetStore. This is likely a Unity bug. Please report!");
                            blobs[i] = default;
                            continue;
                        }
                        blobs[i] = UnsafeUntypedBlobAssetReference.Create(storedBlob);
                    }
                    else
                    {
                        var blob       = blobs[i].Reinterpret<TBlobType>();
                        var backupBlob = blob;
                        if (!blobAssetStore.TryAddBlobAssetWithBurstHash(td.hash, burstTypeHash, ref blob) && backupBlob != blob)
                        {
                            blobs[i] = UnsafeUntypedBlobAssetReference.Create(blob);
                            disposedBlobs.Add(td.hash);
                        }
                    }
                }
            }
        }
    }
}

