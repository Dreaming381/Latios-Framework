using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    public static class AudioClipBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a AudioClipBlob Blob Asset
        /// </summary>
        /// <param name="clip">The audio clip to bake</param>
        /// <param name="numVoices">The number of voices to use (only applies to Looping clips)</param>
        public static SmartBlobberHandle<AudioClipBlob> RequestCreateBlobAsset(this IBaker baker, AudioClip clip, int numVoices = 0)
        {
            return baker.RequestCreateBlobAsset<AudioClipBlob, AudioClipBakeData>(new AudioClipBakeData { clip = clip, numVoices = numVoices });
        }
    }

    public struct AudioClipBakeData : ISmartBlobberRequestFilter<AudioClipBlob>
    {
        public AudioClip clip;
        public int       numVoices;

        public AudioClipBakeData(AudioClip clip, int numVoices)
        {
            this.clip      = clip;
            this.numVoices = numVoices;
        }

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (clip == null)
                return false;

            baker.DependsOn(clip);
            if (clip.samples == 0)
                return false;
            if (clip.channels > 2)
            {
                Debug.LogError($"Myri failed to bake clip {clip.name}. Only mono and stereo clips are supported.");
                return false;
            }
            baker.AddComponent(blobBakingEntity, new AudioClipBlobBakeData { clip = clip, numVoices = numVoices });
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct AudioClipBlobBakeData : IComponentData
    {
        public UnityObjectRef<AudioClip> clip;
        public int                       numVoices;
    }
}

namespace Latios.Myri.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    public unsafe sealed partial class AudioClipSmartBlobberSystem : SystemBase
    {
        EntityQuery m_query;

        struct UniqueItem
        {
            public AudioClipBlobBakeData             bakeData;
            public BlobAssetReference<AudioClipBlob> blob;
        }

        protected override void OnCreate()
        {
            new SmartBlobberTools<AudioClipBlob>().Register(World);
        }

        protected override void OnUpdate()
        {
            int count = m_query.CalculateEntityCountWithoutFiltering();

            var hashmap   = new NativeParallelHashMap<int, UniqueItem>(count * 2, Allocator.TempJob);
            var mapWriter = hashmap.AsParallelWriter();

            Entities.WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ForEach((in AudioClipBlobBakeData data) =>
            {
                mapWriter.TryAdd(data.clip.GetHashCode(), new UniqueItem { bakeData = data });
            }).WithStoreEntityQueryInField(ref m_query).ScheduleParallel();

            var clips    = new NativeList<UnityObjectRef<AudioClip> >(Allocator.TempJob);
            var builders = new NativeList<AudioClipBuilder>(Allocator.TempJob);

            Job.WithCode(() =>
            {
                int count = hashmap.Count();
                if (count == 0)
                    return;

                clips.ResizeUninitialized(count);
                builders.ResizeUninitialized(count);

                int i = 0;
                foreach (var pair in hashmap)
                {
                    clips[i]    = pair.Value.bakeData.clip;
                    builders[i] = new AudioClipBuilder { numVoices = pair.Value.bakeData.numVoices };
                    i++;
                }
            }).Schedule();

            CompleteDependency();

            for (int i = 0; i < clips.Length; i++)
            {
                var clip           = clips[i].Value;
                var builder        = builders[i];
                builder.isStereo   = clip.channels == 2;
                builder.name       = clip.name;
                builder.sampleRate = clip.frequency;
                ReadClip(clip, ref builder.samples);
                builders[i] = builder;
            }

            Dependency = new BuildBlobsJob
            {
                builders = builders.AsArray()
            }.ScheduleParallel(builders.Length, 1, Dependency);

            Job.WithCode(() =>
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    var element                     = hashmap[clips[i].GetHashCode()];
                    element.blob                    = builders[i].result;
                    hashmap[clips[i].GetHashCode()] = element;
                }
            }).Schedule();

            Entities.ForEach((ref SmartBlobberResult result, in AudioClipBlobBakeData data) =>
            {
                result.blob = UnsafeUntypedBlobAssetReference.Create(hashmap[data.clip.GetHashCode()].blob);
            }).WithReadOnly(hashmap).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ScheduleParallel();

            Dependency = hashmap.Dispose(Dependency);
            Dependency = clips.Dispose(Dependency);
            Dependency = builders.Dispose(Dependency);
        }

        #region Cache
        float[] m_cache1;
        float[] m_cache2;
        float[] m_cache16;
        float[] m_cache64;
        float[] m_cache256;
        float[] m_cache1024;
        float[] m_cache4096;
        float[] m_cache16384;
        float[] m_cache65536;

        void* m_cache1Ptr;
        void* m_cache2Ptr;
        void* m_cache16Ptr;
        void* m_cache64Ptr;
        void* m_cache256Ptr;
        void* m_cache1024Ptr;
        void* m_cache4096Ptr;
        void* m_cache16384Ptr;
        void* m_cache65536Ptr;

        ulong m_cache1Handle;
        ulong m_cache2Handle;
        ulong m_cache16Handle;
        ulong m_cache64Handle;
        ulong m_cache256Handle;
        ulong m_cache1024Handle;
        ulong m_cache4096Handle;
        ulong m_cache16384Handle;
        ulong m_cache65536Handle;
        #endregion

        void ReadClip(AudioClip clip, ref UnsafeList<float> samples)
        {
            int sampleCount = clip.samples * clip.channels;
            samples         = new UnsafeList<float>(sampleCount, Allocator.TempJob);
            //samples.Resize(sampleCount, NativeArrayOptions.UninitializedMemory);

            int sampleCountRemaining     = sampleCount;
            int mergedSamplesAccumulated = 0;

            if (sampleCountRemaining / 65536 > 0)
            {
                if (m_cache65536 == null)
                {
                    m_cache65536    = new float[65536];
                    m_cache65536Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache65536, out m_cache65536Handle);
                }

                int stride = 65536 / clip.channels;

                while (sampleCountRemaining / 65536 > 0)
                {
                    clip.GetData(m_cache65536, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache65536Ptr, 65536);
                    sampleCountRemaining     -= 65536;
                    mergedSamplesAccumulated += stride;
                }
            }
            if (sampleCountRemaining / 16384 > 0)
            {
                if (m_cache16384 == null)
                {
                    m_cache16384    = new float[16384];
                    m_cache16384Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache16384, out m_cache16384Handle);
                }

                int stride = 16384 / clip.channels;

                while (sampleCountRemaining / 16384 > 0)
                {
                    clip.GetData(m_cache16384, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache16384Ptr, 16384);
                    sampleCountRemaining     -= 16384;
                    mergedSamplesAccumulated += stride;
                }
            }
            if (sampleCountRemaining / 4096 > 0)
            {
                if (m_cache4096 == null)
                {
                    m_cache4096    = new float[4096];
                    m_cache4096Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache4096, out m_cache4096Handle);
                }

                int stride = 4096 / clip.channels;

                while (sampleCountRemaining / 4096 > 0)
                {
                    clip.GetData(m_cache4096, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache4096Ptr, 4096);
                    sampleCountRemaining     -= 4096;
                    mergedSamplesAccumulated += stride;
                }
            }
            if (sampleCountRemaining / 1024 > 0)
            {
                if (m_cache1024 == null)
                {
                    m_cache1024    = new float[1024];
                    m_cache1024Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache1024, out m_cache1024Handle);
                }

                int stride = 1024 / clip.channels;

                while (sampleCountRemaining / 1024 > 0)
                {
                    clip.GetData(m_cache1024, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache1024Ptr, 1024);
                    sampleCountRemaining     -= 1024;
                    mergedSamplesAccumulated += stride;
                }
            }
            if (sampleCountRemaining / 256 > 0)
            {
                if (m_cache256 == null)
                {
                    m_cache256    = new float[256];
                    m_cache256Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache256, out m_cache256Handle);
                }

                int stride = 256 / clip.channels;

                while (sampleCountRemaining / 256 > 0)
                {
                    clip.GetData(m_cache256, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache256Ptr, 256);
                    sampleCountRemaining     -= 256;
                    mergedSamplesAccumulated += stride;
                }
            }
            if (sampleCountRemaining / 64 > 0)
            {
                if (m_cache64 == null)
                {
                    m_cache64    = new float[64];
                    m_cache64Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache64, out m_cache64Handle);
                }

                int stride = 64 / clip.channels;

                while (sampleCountRemaining / 64 > 0)
                {
                    clip.GetData(m_cache64, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache64Ptr, 64);
                    sampleCountRemaining     -= 64;
                    mergedSamplesAccumulated += stride;
                }
            }
            if (sampleCountRemaining / 16 > 0)
            {
                if (m_cache16 == null)
                {
                    m_cache16    = new float[16];
                    m_cache16Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache16, out m_cache16Handle);
                }

                int stride = 16 / clip.channels;

                while (sampleCountRemaining / 16 > 0)
                {
                    clip.GetData(m_cache16, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache16Ptr, 16);
                    sampleCountRemaining     -= 16;
                    mergedSamplesAccumulated += stride;
                }
            }
            if (sampleCountRemaining / 2 > 0)
            {
                // We break pattern here and do 2 samples instead of 4 because otherwise
                // we run into an issue where stride becomes 0 if there's exactly 2 stereo
                // samples remaining.
                if (m_cache2 == null)
                {
                    m_cache2    = new float[2];
                    m_cache2Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache2, out m_cache2Handle);
                }

                int stride = 2 / clip.channels;

                while (sampleCountRemaining / 2 > 0)
                {
                    clip.GetData(m_cache2, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache2Ptr, 2);
                    sampleCountRemaining     -= 2;
                    mergedSamplesAccumulated += stride;
                }
            }
            if (sampleCountRemaining / 1 > 0)
            {
                if (m_cache1 == null)
                {
                    m_cache1    = new float[1];
                    m_cache1Ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(m_cache1, out m_cache1Handle);
                }

                int stride = 1 / clip.channels;

                while (sampleCountRemaining / 1 > 0)
                {
                    clip.GetData(m_cache1, mergedSamplesAccumulated);
                    samples.AddRangeNoResize(m_cache1Ptr, 1);
                    sampleCountRemaining     -= 1;
                    mergedSamplesAccumulated += stride;
                }
            }
        }

        protected override void OnDestroy()
        {
            if (m_cache1Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache1Handle);
            if (m_cache2Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache2Handle);
            if (m_cache16Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache16Handle);
            if (m_cache64Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache64Handle);
            if (m_cache256Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache256Handle);
            if (m_cache1024Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache1024Handle);
            if (m_cache4096Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache4096Handle);
            if (m_cache16384Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache16384Handle);
            if (m_cache65536Ptr != null)
                UnsafeUtility.ReleaseGCObject(m_cache65536Handle);
        }

        struct AudioClipBuilder
        {
            public UnsafeList<float>                 samples;
            public int                               sampleRate;
            public int                               numVoices;
            public FixedString128Bytes               name;
            public bool                              isStereo;
            public BlobAssetReference<AudioClipBlob> result;

            public void BuildBlob()
            {
                var     builder  = new BlobBuilder(Allocator.Temp);
                ref var root     = ref builder.ConstructRoot<AudioClipBlob>();
                var     blobLeft = builder.Allocate(ref root.samplesLeftOrMono, samples.Length / math.select(1, 2, isStereo));
                if (isStereo)
                {
                    var blobRight = builder.Allocate(ref root.samplesRight, samples.Length / 2);
                    for (int i = 0; i < samples.Length; i++)
                    {
                        blobLeft[i / 2] = samples[i];
                        i++;
                        blobRight[i / 2] = samples[i];
                    }
                }
                else
                {
                    var blobRight = builder.Allocate(ref root.samplesRight, 1);
                    blobRight[0]  = 0f;
                    for (int i = 0; i < samples.Length; i++)
                    {
                        blobLeft[i] = samples[i];
                    }
                }
                int offsetCount = math.max(numVoices, 1);
                int stride      = blobLeft.Length / offsetCount;
                var offsets     = builder.Allocate(ref root.loopedOffsets, offsetCount);
                for (int i = 0; i < offsetCount; i++)
                {
                    offsets[i] = i * stride;
                }
                root.sampleRate = sampleRate;
                root.name       = name;

                samples.Dispose();

                result = builder.CreateBlobAssetReference<AudioClipBlob>(Allocator.Persistent);
            }
        }

        [BurstCompile]
        struct BuildBlobsJob : IJobFor
        {
            public NativeArray<AudioClipBuilder> builders;

            public void Execute(int i)
            {
                var builder = builders[i];
                builder.BuildBlob();
                builders[i] = builder;
            }
        }
    }
}

