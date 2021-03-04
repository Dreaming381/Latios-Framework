using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Profiling;

namespace Latios.Systems
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosInitializationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
    public class SyncPointPlaybackSystem : SubSystem
    {
        enum PlaybackType
        {
            Entity,
            Enable,
            Disable,
            Destroy,
            InstantiateNoData,
            InstantiateUntyped
        }

        struct PlaybackInstance
        {
            public PlaybackType type;
            public Type         requestingSystemType;
        }

        List<PlaybackInstance>                m_playbackInstances                    = new List<PlaybackInstance>();
        List<EntityCommandBuffer>             m_entityCommandBuffers                 = new List<EntityCommandBuffer>();
        List<EnableCommandBuffer>             m_enableCommandBuffers                 = new List<EnableCommandBuffer>();
        List<DisableCommandBuffer>            m_disableCommandBuffers                = new List<DisableCommandBuffer>();
        List<DestroyCommandBuffer>            m_destroyCommandBuffers                = new List<DestroyCommandBuffer>();
        List<InstantiateCommandBuffer>        m_instantiateCommandBuffersWithoutData = new List<InstantiateCommandBuffer>();
        List<InstantiateCommandBufferUntyped> m_instantiateCommandBuffersUntyped     = new List<InstantiateCommandBufferUntyped>();

        NativeList<JobHandle> m_jobHandles;

        protected override void OnCreate()
        {
            m_jobHandles = new NativeList<JobHandle>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_playbackInstances.Clear();
            JobHandle.CompleteAll(m_jobHandles);
            m_jobHandles.Dispose();
            foreach (var ecb in m_entityCommandBuffers)
                ecb.Dispose();
            foreach (var ecb in m_enableCommandBuffers)
                ecb.Dispose();
            foreach (var dcb in m_disableCommandBuffers)
                dcb.Dispose();
            foreach (var dcb in m_destroyCommandBuffers)
                dcb.Dispose();
            foreach (var icb in m_instantiateCommandBuffersWithoutData)
                icb.Dispose();
            foreach (var icb in m_instantiateCommandBuffersUntyped)
                icb.Dispose();
        }

        public override bool ShouldUpdateSystem()
        {
            return m_playbackInstances.Count > 0;
        }

        protected override void OnUpdate()
        {
            JobHandle.CompleteAll(m_jobHandles);
            m_jobHandles.Clear();
            CompleteDependency();

            int entityIndex             = 0;
            int enableIndex             = 0;
            int disableIndex            = 0;
            int destroyIndex            = 0;
            int instantiateNoDataIndex  = 0;
            int instantiateUntypedIndex = 0;
            foreach (var instance in m_playbackInstances)
            {
                //Todo: We don't fail as gracefully as EntityCommandBufferSystem, but I'm not sure what is exactly required to meet that. There's way too much magic there.
                Profiler.BeginSample(instance.requestingSystemType == null ? "Unknown" : instance.requestingSystemType.Name);
                switch (instance.type)
                {
                    case PlaybackType.Entity:
                    {
                        var ecb = m_entityCommandBuffers[entityIndex];
                        ecb.Playback(EntityManager);
                        ecb.Dispose();
                        entityIndex++;
                        break;
                    }
                    case PlaybackType.Enable:
                    {
                        var ecb = m_enableCommandBuffers[enableIndex];
                        ecb.Playback(EntityManager, GetBufferFromEntity<LinkedEntityGroup>(true));
                        ecb.Dispose();
                        enableIndex++;
                        break;
                    }
                    case PlaybackType.Disable:
                    {
                        var dcb = m_disableCommandBuffers[disableIndex];
                        dcb.Playback(EntityManager, GetBufferFromEntity<LinkedEntityGroup>(true));
                        dcb.Dispose();
                        disableIndex++;
                        break;
                    }
                    case PlaybackType.Destroy:
                    {
                        var dcb = m_destroyCommandBuffers[destroyIndex];
                        dcb.Playback(EntityManager);
                        dcb.Dispose();
                        destroyIndex++;
                        break;
                    }
                    case PlaybackType.InstantiateNoData:
                    {
                        var icb = m_instantiateCommandBuffersWithoutData[instantiateNoDataIndex];
                        icb.Playback(EntityManager);
                        icb.Dispose();
                        instantiateNoDataIndex++;
                        break;
                    }
                    case PlaybackType.InstantiateUntyped:
                    {
                        var icb = m_instantiateCommandBuffersUntyped[instantiateUntypedIndex];
                        try
                        {
                            icb.Playback(EntityManager);
                        }
                        catch (Exception e)
                        {
                            UnityEngine.Debug.LogError(e.Message + e.StackTrace);
                            throw e;
                        }
                        icb.Dispose();
                        instantiateUntypedIndex++;
                        break;
                    }
                }
                Profiler.EndSample();
            }
            m_playbackInstances.Clear();
            m_entityCommandBuffers.Clear();
            m_enableCommandBuffers.Clear();
            m_disableCommandBuffers.Clear();
            m_destroyCommandBuffers.Clear();
            m_instantiateCommandBuffersWithoutData.Clear();
            m_instantiateCommandBuffersUntyped.Clear();
        }

        public EntityCommandBuffer CreateEntityCommandBuffer()
        {
            //Todo: Expose variant of ECB constructor which allows us to set DisposeSentinal stack depth to -1 and use TempJob.
            var ecb      = new EntityCommandBuffer(Allocator.Persistent, PlaybackPolicy.SinglePlayback);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.Entity,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_entityCommandBuffers.Add(ecb);
            return ecb;
        }

        public EnableCommandBuffer CreateEnableCommandBuffer()
        {
            //Todo: We use Persistent allocator here because of the NativeReference. This recreates the DisposeSentinal stuff except with the slower allocator.
            var ecb      = new EnableCommandBuffer(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.Enable,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_enableCommandBuffers.Add(ecb);
            return ecb;
        }

        public DisableCommandBuffer CreateDisableCommandBuffer()
        {
            //Todo: We use Persistent allocator here because of the NativeReference. This recreates the DisposeSentinal stuff except with the slower allocator.
            var dcb      = new DisableCommandBuffer(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.Disable,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_disableCommandBuffers.Add(dcb);
            return dcb;
        }

        public DestroyCommandBuffer CreateDestroyCommandBuffer()
        {
            //Todo: We use Persistent allocator here because of the NativeReference. This recreates the DisposeSentinal stuff except with the slower allocator.
            var dcb      = new DestroyCommandBuffer(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.Destroy,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_destroyCommandBuffers.Add(dcb);
            return dcb;
        }

        public InstantiateCommandBuffer CreateInstantiateCommandBuffer()
        {
            //Todo: We use Persistent allocator here for consistency, though I suspect it might be possible to improve this.
            var icb      = new InstantiateCommandBuffer(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.InstantiateNoData,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersWithoutData.Add(icb);
            return icb;
        }

        public InstantiateCommandBuffer<T0> CreateInstantiateCommandBuffer<T0>() where T0 : unmanaged, IComponentData
        {
            //Todo: We use Persistent allocator here for consistency, though I suspect it might be possible to improve this.
            var icb      = new InstantiateCommandBuffer<T0>(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.InstantiateUntyped,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        public InstantiateCommandBuffer<T0, T1> CreateInstantiateCommandBuffer<T0, T1>() where T0 : unmanaged, IComponentData where T1 : unmanaged, IComponentData
        {
            //Todo: We use Persistent allocator here for consistency, though I suspect it might be possible to improve this.
            var icb      = new InstantiateCommandBuffer<T0, T1>(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.InstantiateUntyped,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        public InstantiateCommandBuffer<T0, T1, T2> CreateInstantiateCommandBuffer<T0, T1, T2>() where T0 : unmanaged, IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData
        {
            //Todo: We use Persistent allocator here for consistency, though I suspect it might be possible to improve this.
            var icb      = new InstantiateCommandBuffer<T0, T1, T2>(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.InstantiateUntyped,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        public InstantiateCommandBuffer<T0, T1, T2, T3> CreateInstantiateCommandBuffer<T0, T1, T2, T3>() where T0 : unmanaged, IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData where T3 : unmanaged, IComponentData
        {
            //Todo: We use Persistent allocator here for consistency, though I suspect it might be possible to improve this.
            var icb      = new InstantiateCommandBuffer<T0, T1, T2, T3>(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.InstantiateUntyped,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        public InstantiateCommandBuffer<T0, T1, T2, T3, T4> CreateInstantiateCommandBuffer<T0, T1, T2, T3, T4>() where T0 : unmanaged, IComponentData where T1 : unmanaged,
        IComponentData where T2 : unmanaged, IComponentData where T3 : unmanaged, IComponentData where T4 : unmanaged, IComponentData
        {
            //Todo: We use Persistent allocator here for consistency, though I suspect it might be possible to improve this.
            var icb      = new InstantiateCommandBuffer<T0, T1, T2, T3, T4>(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.InstantiateUntyped,
                requestingSystemType = World.ExecutingSystemType(),
            };
            m_playbackInstances.Add(instance);
            m_instantiateCommandBuffersUntyped.Add(icb.m_instantiateCommandBufferUntyped);
            return icb;
        }

        public void AddJobHandleForProducer(JobHandle handle)
        {
            //Todo, maybe we could reason about this better and get better job scheduling, but this seems fine for now.
            //We will always need this if a request comes from a MonoBehaviour or something.
            m_jobHandles.Add(handle);
        }
    }
}

