using System.Collections.Generic;
using GameObject = UnityEngine.GameObject;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
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

        ComponentLookup<SmartBlobberResult>       m_resultLookup;
        ComponentLookup<SmartBlobberTrackingData> m_trackingDataLookup;

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

            m_resultLookup       = GetComponentLookup<SmartBlobberResult>(true);
            m_trackingDataLookup = GetComponentLookup<SmartBlobberTrackingData>(false);
        }

        protected sealed override void OnUpdate()
        {
            // Step 1: Update handles
            var blobAssetStore = m_bakingSystemReference.BlobAssetStore;

            m_resultHandle.Update(this);
            m_trackingDataHandle.Update(this);
            m_entityHandle.Update(this);
            m_resultLookup.Update(this);
            m_trackingDataLookup.Update(this);

            // Step 2: Build blobs and hashes
            Dependency = new ComputeHashesJob
            {
                resultHandle       = m_resultHandle,
                trackingDataHandle = m_trackingDataHandle,
                entityHandle       = m_entityHandle
            }.ScheduleParallel(m_query, Dependency);
            CompleteDependency();

            // Step 3: filter with BlobAssetComputationContext
            var computationContext = new BlobAssetComputationContext<SmartBlobberTrackingData, TBlobType>(blobAssetStore, 128, Allocator.TempJob);
            new SmartBlobberTools<TBlobType>.FindBlobsThatShouldBeKeptJob
            {
                context            = computationContext,
                trackingDataHandle = m_trackingDataHandle
            }.Run(m_query);

            new SmartBlobberTools<TBlobType>.MarkBlobsToKeep
            {
                context            = computationContext,
                resultLookup       = m_resultLookup,
                trackingDataLookup = m_trackingDataLookup
            }.Run();

            // Step 4: Dispose unused blobs and collect final blobs
            new SmartBlobberTools<TBlobType>.DeduplicateBlobsJob
            {
                context            = computationContext,
                resultHandle       = m_resultHandle,
                trackingDataHandle = m_trackingDataHandle
            }.Run(m_query);
            computationContext.Dispose();
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
                if (blob.Reinterpret<int>() == BlobAssetReference<int>.Null)
                {
                    var trackingData          = trackingDataArray[i];
                    trackingData.isNull       = true;
                    trackingData.isFinalized  = true;
                    trackingData.thisEntity   = entityArray[i];
                    trackingData.shouldBeKept = false;
                    trackingDataArray[i]      = trackingData;
                }
                else
                {
                    var length                = blob.GetLength();
                    var hash32                = (uint)(blob.GetHash64() & 0xffffffff);
                    var hash64                = xxHash3.Hash64(blob.Reinterpret<int>().GetUnsafePtr(), length);
                    var hash128               = new Hash128(hash64.x, hash64.y, (uint)length, hash32);
                    var trackingData          = trackingDataArray[i];
                    trackingData.hash         = hash128;
                    trackingData.isNull       = false;
                    trackingData.isFinalized  = true;
                    trackingData.thisEntity   = entityArray[i];
                    trackingData.shouldBeKept = false;
                    trackingDataArray[i]      = trackingData;
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
        internal struct FindBlobsThatShouldBeKeptJob : IJobChunk
        {
            public BlobAssetComputationContext<SmartBlobberTrackingData, TBlobType> context;
            [ReadOnly] public ComponentTypeHandle<SmartBlobberTrackingData>         trackingDataHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var array = chunk.GetNativeArray(ref trackingDataHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var td = array[i];
                    if (td.isNull)
                        continue;

                    context.AssociateBlobAssetWithUnityObject(td.hash, td.authoringInstanceID);
                    if (context.NeedToComputeBlobAsset(td.hash))
                    {
                        context.AddBlobAssetToCompute(td.hash, td);
                    }
                }
            }
        }

        [BurstCompile]
        internal struct MarkBlobsToKeep : IJob
        {
            public BlobAssetComputationContext<SmartBlobberTrackingData, TBlobType> context;
            public ComponentLookup<SmartBlobberTrackingData>                        trackingDataLookup;
            [ReadOnly] public ComponentLookup<SmartBlobberResult>                   resultLookup;

            public void Execute()
            {
                var filteredTrackingData = context.GetSettings(Allocator.Temp);
                foreach (var td in filteredTrackingData)
                {
                    context.AddComputedBlobAsset(td.hash, resultLookup[td.thisEntity].blob.Reinterpret<TBlobType>());
                    trackingDataLookup.GetRefRW(td.thisEntity, false).ValueRW.shouldBeKept = true;
                }
            }
        }

        [BurstCompile]
        internal struct DeduplicateBlobsJob : IJobChunk
        {
            public BlobAssetComputationContext<SmartBlobberTrackingData, TBlobType> context;
            [ReadOnly] public ComponentTypeHandle<SmartBlobberTrackingData>         trackingDataHandle;
            public ComponentTypeHandle<SmartBlobberResult>                          resultHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var trackingDataArray = chunk.GetNativeArray(ref trackingDataHandle);
                var resultArray       = chunk.GetNativeArray(ref resultHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var td = trackingDataArray[i];
                    if (td.isNull)
                        continue;

                    // The reference is already up-to-date
                    if (td.shouldBeKept)
                        continue;

                    var blob = resultArray[i].blob.Reinterpret<TBlobType>();
                    blob.Dispose();
                    context.GetBlobAsset(td.hash, out blob);
                    resultArray[i] = new SmartBlobberResult { blob = UnsafeUntypedBlobAssetReference.Create(blob) };
                }
            }
        }
    }
}

