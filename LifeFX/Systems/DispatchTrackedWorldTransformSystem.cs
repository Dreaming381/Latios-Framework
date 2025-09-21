using Latios.Kinemation;
using Latios.Kinemation.Systems;
using Latios.Transforms;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.LifeFX.Systems
{
    [UpdateInGroup(typeof(DispatchRoundRobinLateExtensionsSuperSystem))]
    [UpdateBefore(typeof(GraphicsGlobalBufferBroadcastSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct DispatchTrackedWorldTransformSystem : ISystem, ICullingComputeDispatchSystem<DispatchTrackedWorldTransformSystem.CollectState,
                                                                                                       DispatchTrackedWorldTransformSystem.WriteState>
    {
        LatiosWorldUnmanaged                                 latiosWorld;
        CullingComputeDispatchData<CollectState, WriteState> m_data;

        static GraphicsBufferBroker.StaticID s_persistentID = GraphicsBufferBroker.ReservePersistentBuffer();
        static GraphicsBufferBroker.StaticID s_uploadID     = GraphicsBufferBroker.ReserveUploadPool();

        GraphicsBufferBroker.StaticID m_persistentID;
        GraphicsBufferBroker.StaticID m_uploadID;

        UnityObjectRef<UnityEngine.ComputeShader> m_uploadShader;
        UnityObjectRef<UnityEngine.ComputeShader> m_resizeShader;

        int _src;
        int _dst;
        int _start;
        int _count;
        int _latiosTrackedWorldTransforms;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_data      = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorld);

            m_uploadShader = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<UnityEngine.ComputeShader>("UploadTrackedQvvs");
            m_resizeShader = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<UnityEngine.ComputeShader>("CopyTransformUnions");

            m_persistentID                = s_persistentID;
            m_uploadID                    = s_uploadID;
            _src                          = UnityEngine.Shader.PropertyToID("_src");
            _dst                          = UnityEngine.Shader.PropertyToID("_dst");
            _start                        = UnityEngine.Shader.PropertyToID("_start");
            _count                        = UnityEngine.Shader.PropertyToID("_count");
            _latiosTrackedWorldTransforms = UnityEngine.Shader.PropertyToID("_latiosTrackedWorldTransforms");

            var broker = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();
            broker.InitializePersistentBuffer(m_persistentID, 1024, (uint)UnsafeUtility.SizeOf<TransformQvvs>(), UnityEngine.GraphicsBuffer.Target.Structured, m_resizeShader);
            UnityEngine.Assertions.Assert.AreEqual(UnsafeUtility.SizeOf<UploadQvvs>(), 13 * 4);
            broker.InitializeUploadPool(m_uploadID, (uint)UnsafeUtility.SizeOf<UploadQvvs>(), UnityEngine.GraphicsBuffer.Target.Structured);

            var persistentBuffer = broker.GetPersistentBufferNoResize(m_persistentID);
            latiosWorld.worldBlackboardEntity.GetCollectionComponent<ShaderPropertyToGlobalBufferMap>(true).AddOrReplace(_latiosTrackedWorldTransforms, persistentBuffer);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state) => m_data.DoUpdate(ref state, ref this);

        public CollectState Collect(ref SystemState state)
        {
            if (latiosWorld.worldBlackboardEntity.GetComponentData<DispatchContext>().dispatchIndexThisFrame != 0)
                return default;

            var uploadList   = latiosWorld.worldBlackboardEntity.GetCollectionComponent<TrackedTransformUploadList>(true);
            var threadRanges = CollectionHelper.CreateNativeArray<int2>(JobsUtility.ThreadIndexCount, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

            state.Dependency = new CollectJob
            {
                uploadIndices = uploadList.uploadIndices,
                threadRanges  = threadRanges
            }.Schedule(state.Dependency);

            return new CollectState
            {
                uploadList   = uploadList,
                threadRanges = threadRanges
            };
        }

        public WriteState Write(ref SystemState state, ref CollectState collected)
        {
            if (!collected.threadRanges.IsCreated)
                return default;

            var last       = collected.threadRanges[collected.threadRanges.Length - 1];
            var writeCount = last.x + last.y;
            if (writeCount == 0)
                return default;

            var broker       = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();
            var uploadBuffer = broker.GetUploadBuffer(m_uploadID, (uint)writeCount);
            var uploadArray  = uploadBuffer.LockBufferForWrite<UploadQvvs>(0, writeCount);

            state.Dependency = new WriteJob
            {
                threadRanges      = collected.threadRanges,
                trackedTransforms = collected.uploadList.trackedTransforms.AsDeferredJobArray(),
                uploadIndices     = collected.uploadList.uploadIndices,
                uploadBuffer      = uploadArray
            }.ScheduleParallel(collected.threadRanges.Length, 1, state.Dependency);

            return new WriteState
            {
                uploadBuffer           = uploadBuffer,
                broker                 = broker,
                writeCount             = writeCount,
                requiredPersistentSize = collected.uploadList.trackedTransforms.Length
            };
        }

        public void Dispatch(ref SystemState state, ref WriteState written)
        {
            if (written.writeCount == 0)
                return;

            written.uploadBuffer.UnlockBufferAfterWrite<UploadQvvs>(written.writeCount);
            var persistentBuffer = written.broker.GetPersistentBuffer(m_persistentID, (uint)written.requiredPersistentSize);
            m_uploadShader.SetBuffer(0, _dst, persistentBuffer);
            m_uploadShader.SetBuffer(0, _src, written.uploadBuffer);
            uint copySize = (uint)written.writeCount;
            for (uint countRemaining = copySize, dispatchesRemaining = (copySize + 64 - 1) / 64, start = 0; dispatchesRemaining > 0;)
            {
                uint dispatchCount = math.min(dispatchesRemaining, 65535);
                uint elementCount  = math.min(countRemaining, 65535 * 64);
                m_uploadShader.SetInt(_start, (int)(start * 64));
                m_uploadShader.SetInt(_count, (int)countRemaining);
                m_uploadShader.Dispatch(0, (int)dispatchCount, 1, 1);
                dispatchesRemaining -= dispatchCount;
                countRemaining      -= elementCount;
                start               += dispatchCount;
            }
            latiosWorld.worldBlackboardEntity.GetCollectionComponent<ShaderPropertyToGlobalBufferMap>(true).AddOrReplace(_latiosTrackedWorldTransforms, persistentBuffer);
            GraphicsUnmanaged.SetGlobalBuffer(_latiosTrackedWorldTransforms, persistentBuffer);
        }

        public struct CollectState
        {
            internal TrackedTransformUploadList uploadList;
            internal NativeArray<int2>          threadRanges;
        }

        public struct WriteState
        {
            internal GraphicsBufferUnmanaged uploadBuffer;
            internal GraphicsBufferBroker    broker;
            internal int                     writeCount;
            internal int                     requiredPersistentSize;
        }

        struct UploadQvvs
        {
            public TransformQvvs qvvs;
            public int           dst;
        }

        [BurstCompile]
        struct CollectJob : IJob
        {
            public NativeArray<int2>            threadRanges;
            public UnsafeParallelBlockList<int> uploadIndices;

            public void Execute()
            {
                int counter = 0;
                for (int i = 0; i < threadRanges.Length; i++)
                {
                    var threadCount  = uploadIndices.CountForThreadIndex(i);
                    threadRanges[i]  = new int2(counter, threadCount);
                    counter         += threadCount;
                }
            }
        }

        [BurstCompile]
        struct WriteJob : IJobFor
        {
            [ReadOnly] public NativeArray<int2>                                  threadRanges;
            [ReadOnly] public NativeArray<TransformQvvs>                         trackedTransforms;
            public UnsafeParallelBlockList<int>                                  uploadIndices;
            [NativeDisableParallelForRestriction] public NativeArray<UploadQvvs> uploadBuffer;

            public void Execute(int index)
            {
                var range      = threadRanges[index];
                var enumerator = uploadIndices.GetEnumerator(index);
                for (int i = 0; i < range.y; i++)
                {
                    enumerator.MoveNext();
                    var dst                   = enumerator.Current;
                    uploadBuffer[range.x + i] = new UploadQvvs
                    {
                        dst  = dst,
                        qvvs = trackedTransforms[dst]
                    };
                }
            }
        }
    }
}

