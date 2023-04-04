using System;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;

namespace Latios.Authoring.Systems
{
    /// <summary>
    /// This system will generate errors if a SmartBlobberResult is found that was not post-processed.
    /// To prevent memory leaks, this system will dispose found blob assets.
    /// To fix the issue, call SmartBlobberTools<>.Register in OnCreate() of one of your baking systems.
    /// If you encounter errors but did not create a custom Smart Blobber, you make have called RequestCreateBlobAsset
    /// for a type whose feature you did not install inside of ICustomBakingBootstrap.
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(SmartBlobberCleanupBakingGroup), OrderLast = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [BurstCompile]
    public partial struct SmartBlobberCheckForUnregisteredBlobsBakingSystem : ISystem
    {
        EntityQuery                                         m_query;
        ComponentTypeHandle<SmartBlobberTrackingData>       m_trackingDataHandle;
        ComponentTypeHandle<SmartBlobberResult>             m_resultHandle;
        SharedComponentTypeHandle<SmartBlobberBlobTypeHash> m_hashHandle;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = new EntityQueryBuilder(Allocator.Temp).WithAllRW<SmartBlobberResult>().WithOptions(
                EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            state.RequireForUpdate(m_query);

            m_trackingDataHandle = state.GetComponentTypeHandle<SmartBlobberTrackingData>(true);
            m_resultHandle       = state.GetComponentTypeHandle<SmartBlobberResult>(false);
            m_hashHandle         = state.GetSharedComponentTypeHandle<SmartBlobberBlobTypeHash>();
        }
        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_trackingDataHandle.Update(ref state);
            m_resultHandle.Update(ref state);
            m_hashHandle.Update(ref state);

            var blobsToDispose = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<UnsafeUntypedBlobAssetReference>(), 64, Allocator.TempJob);

            state.Dependency = new Job
            {
                hashHandle         = m_hashHandle,
                resultHandle       = m_resultHandle,
                trackingDataHandle = m_trackingDataHandle,
                blobsToDispose     = blobsToDispose
            }.ScheduleParallel(m_query, state.Dependency);

            state.Dependency = new DisposeJob
            {
                blobsToDispose = blobsToDispose
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<SmartBlobberTrackingData>       trackingDataHandle;
            public ComponentTypeHandle<SmartBlobberResult>                        resultHandle;
            [ReadOnly] public SharedComponentTypeHandle<SmartBlobberBlobTypeHash> hashHandle;
            public UnsafeParallelBlockList                                        blobsToDispose;

            [NativeSetThreadIndex]
            int threadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.Has(ref trackingDataHandle))
                {
                    UnityEngine.Debug.LogError(
                        "A SmartBlobberResult was detected without a tracking handle. Do not add SmartBlobberResult manually. Instead, use the RequestCreateBlobAsset API to have it added.");
                    return;
                }

                if (!chunk.Has(hashHandle))
                {
                    UnityEngine.Debug.LogError("Where did the SmartBlobberBlobTypeHash go?");
                }

                var trackingDataArray = chunk.GetNativeArray(ref trackingDataHandle);
                if (trackingDataArray[0].isFinalized)
                    return;

                var hash = chunk.GetSharedComponent(hashHandle).hash;

                UnityEngine.Debug.LogError(
                    $"A SmartBlobberResult was detected that was not post-processed. Please ensure to register the Smart Blobber blob type with SmartBlobberTools.Register(). If you are seeing this error but did not create a custom SmartBlobber, check your ICustomBakingBootstrap to ensure the features you are attempting to use are correctly installed. The offending blobs will be disposed. Blob type hash: BurstRuntime.GetHashCode64() = {hash}.");

                var resultArray = chunk.GetNativeArray(ref resultHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (trackingDataArray[i].isNull)
                        continue;

                    blobsToDispose.Write(resultArray[i].blob, threadIndex);
                    resultArray[i] = default;
                }
            }
        }

        [BurstCompile]
        struct DisposeJob : IJob
        {
            public UnsafeParallelBlockList blobsToDispose;

            public unsafe void Execute()
            {
                var blobCount = blobsToDispose.Count();
                if (blobCount == 0)
                {
                    blobsToDispose.Dispose();
                    return;
                }

                var set = new NativeHashSet<PtrWrapper>(blobCount, Allocator.Temp);

                var enumerator = blobsToDispose.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var blob = enumerator.GetCurrent<UnsafeUntypedBlobAssetReference>();
                    if (set.Contains(blob))
                        continue;
                    set.Add(blob);
                    blob.Dispose();
                }
                blobsToDispose.Dispose();
            }
        }

        unsafe struct PtrWrapper : IEquatable<PtrWrapper>
        {
            void* ptr;

            public bool Equals(PtrWrapper other)
            {
                return ptr == other.ptr;
            }

            public override int GetHashCode()
            {
                return ((ulong)ptr).GetHashCode();
            }

            public static implicit operator PtrWrapper(UnsafeUntypedBlobAssetReference blob)
            {
                return new PtrWrapper { ptr = blob.Reinterpret<int>().GetUnsafePtr() };
            }
        }
    }
}

