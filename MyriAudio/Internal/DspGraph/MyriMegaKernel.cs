using System;
using Latios.Myri.DSP;
using Latios.Myri.Interop;
using Unity.Audio;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Myri.DSP
{
    [BurstCompile(CompileSynchronously = true)]
    internal unsafe partial struct MyriMegaKernel : IAudioKernel<MyriMegaKernel.Parameters, MyriMegaKernel.SampleProviders>
    {
        [NativeDisableUnsafePtrRestriction]
        internal long* m_packedFrameCounterBufferId;

        bool                        m_hasFirstDspBuffer;
        bool                        m_hasValidDspBuffer;
        int                         m_currentFrame;
        int                         m_nextUpdateFrame;
        int                         m_lastProcessedBufferID;
        int                         m_frameSize;
        int                         m_sampleRate;
        DspUpdateBuffer             m_dspUpdateBuffer;
        UnsafeList<DspUpdateBuffer> m_queuedDspUpdateBuffers;

        ChunkedList128<EffectMetadata.Ptr>            m_effectIdToPtrMap;
        ChunkedList128<SpatialEffectMetadata.Ptr>     m_spatialEffectIdToPtrMap;
        ChunkedList128<SourceStackMetadata.Ptr>       m_sourceStackIdToPtrMap;
        ChunkedList128<ListenerState>                 m_listenerStackIdToStateMap;
        ChunkedList128<ResourceComponentMetadata.Ptr> m_resourceComponentIdToPtrMap;
        ChunkedList128<ResourceBufferMetadata.Ptr>    m_resourceBufferIdToPtrMap;

        NativeHashMap<ResourceKey, ResourceValue>      m_resourceKeyToPtrMap;
        NativeHashMap<Entity, VirtualOutputEffect.Ptr> m_entityToVirtualOutputMap;

        BrickwallLimiter m_masterLimiter;

        SampleFramePool m_framePool;

        internal UnsafeList<UnsafeList<ListenerStackMetadata.Ptr> > m_listenersByLayer;
        bool                                                        m_listenersDirty;
        SamplingCache                                               m_samplingCache;

        // Todo: I don't think this allocator will actually work here, because it prevents rewinding from within a job.
        // Will likely need a modified ScratchpadAllocator that can support growing.
        //AllocatorHelper<RewindableAllocator> m_rewindableAllocator;

        ProfilerMarker m_profilingReceivedBuffers;
        ProfilerMarker m_profilingSourceStacks;
        ProfilerMarker m_profilingSpatializers;
        ProfilerMarker m_profilingListenerStacks;
        ProfilerMarker m_profilingMixdown;

        public void Initialize()
        {
            // We start on frame 1 so that a buffer ID and frame of both 0 means uninitialized.
            // The audio components use this at the time of writing this comment.
            m_currentFrame                = 1;
            m_nextUpdateFrame             = 0;
            m_lastProcessedBufferID       = -1;
            m_listenersDirty              = false;
            m_hasFirstDspBuffer           = false;
            m_hasValidDspBuffer           = false;
            m_queuedDspUpdateBuffers      = new UnsafeList<DspUpdateBuffer>(16, Allocator.AudioKernel);
            m_effectIdToPtrMap            = new ChunkedList128<EffectMetadata.Ptr>(Allocator.AudioKernel);
            m_spatialEffectIdToPtrMap     = new ChunkedList128<SpatialEffectMetadata.Ptr>(Allocator.AudioKernel);
            m_sourceStackIdToPtrMap       = new ChunkedList128<SourceStackMetadata.Ptr>(Allocator.AudioKernel);
            m_listenerStackIdToStateMap   = new ChunkedList128<ListenerState>(Allocator.AudioKernel);
            m_resourceComponentIdToPtrMap = new ChunkedList128<ResourceComponentMetadata.Ptr>(Allocator.AudioKernel);
            m_resourceBufferIdToPtrMap    = new ChunkedList128<ResourceBufferMetadata.Ptr>(Allocator.AudioKernel);
            m_resourceKeyToPtrMap         = new NativeHashMap<ResourceKey, ResourceValue>(256, Allocator.AudioKernel);
            m_entityToVirtualOutputMap    = new NativeHashMap<Entity, VirtualOutputEffect.Ptr>(64, Allocator.AudioKernel);
            m_framePool                   = new SampleFramePool(Allocator.AudioKernel);
            m_listenersByLayer            = new UnsafeList<UnsafeList<ListenerStackMetadata.Ptr> >(32, Allocator.AudioKernel);
            for (int i = 0; i < 32; i++)
                m_listenersByLayer.Add(new UnsafeList<ListenerStackMetadata.Ptr>(8, Allocator.AudioKernel));
            m_samplingCache = new SamplingCache(Allocator.AudioKernel);
            //m_rewindableAllocator = new AllocatorHelper<RewindableAllocator>(Allocator.AudioKernel);
            //m_rewindableAllocator.Allocator.Initialize(64 * 1024);

            m_masterLimiter = new BrickwallLimiter(BrickwallLimiter.kDefaultPreGain,
                                                   BrickwallLimiter.kDefaultLimitDB,
                                                   BrickwallLimiter.kDefaultReleaseDBPerSample,
                                                   BrickwallLimiter.kDefaultLookaheadSampleCount,
                                                   Allocator.AudioKernel);

            m_profilingReceivedBuffers = new ProfilerMarker("ProcessReceivedBuffers");
            m_profilingSourceStacks    = new ProfilerMarker("SourceStacks");
            m_profilingSpatializers    = new ProfilerMarker("Spatialization");
            m_profilingListenerStacks  = new ProfilerMarker("ListenerStacks");
            m_profilingMixdown         = new ProfilerMarker("Mixdown");
        }

        public void Execute(ref ExecuteContext<Parameters, SampleProviders> context)
        {
            m_frameSize  = context.DSPBufferSize;
            m_sampleRate = context.SampleRate;

            ProcessReceivedBuffers();
            SampleSources();
            ProcessPresampledChannels();
            SampleListeners();
        }

        public void Dispose()
        {
            // Todo: Destroy all non-null effects

            for (int i = 0; i < m_listenerStackIdToStateMap.length; i++)
            {
                ref var listener = ref m_listenerStackIdToStateMap[i];
                if (listener.listenerMetadataPtr != null)
                {
                    listener.Dispose(ref m_framePool);
                }
            }
            for (int i = 0; i < m_listenersByLayer.Length; i++)
            {
                m_listenersByLayer[i].Dispose();
            }

            m_queuedDspUpdateBuffers.Dispose();
            m_effectIdToPtrMap.Dispose();
            m_spatialEffectIdToPtrMap.Dispose();
            m_sourceStackIdToPtrMap.Dispose();
            m_listenerStackIdToStateMap.Dispose();
            m_entityToVirtualOutputMap.Dispose();
            m_framePool.Dispose();
            m_listenersByLayer.Dispose();
            m_samplingCache.Dispose();
            m_masterLimiter.Dispose();
            //m_rewindableAllocator.Dispose();
        }

        public enum Parameters
        {
            Unused
        }
        public enum SampleProviders
        {
            Unused
        }

        unsafe struct ListenerState
        {
            public ListenerStackMetadata* listenerMetadataPtr;
            public int                    presampledStart;
            public int                    presampledCount;
            public BrickwallLimiter       limiter;
            public SampleFrame            sampleFrame;

            public void Dispose(ref SampleFramePool sampleFramePool)
            {
                if (sampleFrame.left.IsCreated)
                    sampleFramePool.Release(sampleFrame);
                if (limiter.isCreated)
                    limiter.Dispose();
                listenerMetadataPtr = default;
                sampleFrame         = default;
            }
        }
    }

    internal struct ResourceKey : IEquatable<ResourceKey>
    {
        public Entity        entity;
        public ComponentType componentType;

        public bool Equals(ResourceKey other)
        {
            return entity.Equals(other.entity) && componentType.Equals(other.componentType);
        }

        public override int GetHashCode()
        {
            return new int2(entity.GetHashCode(), componentType.GetHashCode()).GetHashCode();
        }
    }

    internal unsafe struct ResourceValue
    {
        public void* metadataPtr;  // Determine which from key's componentType
    }

    internal unsafe struct ChunkedList128<T> : IDisposable where T : unmanaged
    {
        UnsafeList<Chunk128>             m_chunks;
        AllocatorManager.AllocatorHandle m_allocator;
        int                              m_count;

        struct Chunk128
        {
            public T* ptr;
        }

        public ChunkedList128(AllocatorManager.AllocatorHandle allocator)
        {
            m_allocator = allocator;
            m_chunks    = new UnsafeList<Chunk128>(1, m_allocator);
            m_count     = 0;
        }

        public ref T this[int index]
        {
            get
            {
                m_count          = math.max(m_count, index);
                var chunkIndex   = index / 128;
                var elementIndex = index % 128;

                if (Hint.Unlikely(m_chunks.Length <= chunkIndex))
                {
                    GrowChunks(chunkIndex);
                }

                return ref m_chunks[chunkIndex].ptr[elementIndex];
            }
        }

        public int length => m_count;

        public void Dispose()
        {
            foreach (var chunk in m_chunks)
            {
                AllocatorManager.Free(m_allocator, chunk.ptr, 128);
            }
            m_chunks.Dispose();
        }

        void GrowChunks(int newDesiredChunkIndex)
        {
            for (int i = 0; i < newDesiredChunkIndex - m_chunks.Length + 1; i++)
            {
                var ptr = AllocatorManager.Allocate<T>(m_allocator, 128);
                UnsafeUtility.MemClear(ptr, 128 * UnsafeUtility.SizeOf<T>());
                m_chunks.Add(new Chunk128
                {
                    ptr = ptr
                });
            }
        }
    }
}

