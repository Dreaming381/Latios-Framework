using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    public struct AudioClipBakeData
    {
        public AudioClip clip;
        public int       numVoices;
    }

    public static class AudioClipBlobberAPIExtensions
    {
        public static SmartBlobberHandle<AudioClipBlob> CreateBlob(this GameObjectConversionSystem conversionSystem,
                                                                   GameObject gameObject,
                                                                   AudioClipBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.AudioClipSmartBlobberSystem>().AddToConvert(gameObject, bakeData);
        }

        public static SmartBlobberHandleUntyped CreateBlobUntyped(this GameObjectConversionSystem conversionSystem,
                                                                  GameObject gameObject,
                                                                  AudioClipBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.AudioClipSmartBlobberSystem>().AddToConvertUntyped(gameObject, bakeData);
        }
    }
}

namespace Latios.Myri.Authoring.Systems
{
    [ConverterVersion("Latios", 4)]
    public sealed class AudioClipSmartBlobberSystem : SmartBlobberConversionSystem<AudioClipBlob, AudioClipBakeData, AudioClipConverter, AudioClipContext>
    {
        struct AuthoringHandlePair
        {
            public AudioSourceAuthoring              authoring;
            public SmartBlobberHandle<AudioClipBlob> blobHandle;
        }

        List<AuthoringHandlePair> m_sourceList = new List<AuthoringHandlePair>();

        protected override void GatherInputs()
        {
            m_sourceList.Clear();
            Entities.ForEach((AudioSourceAuthoring authoring) =>
            {
                var pair = new AuthoringHandlePair { authoring = authoring };
                if (authoring.clip != null)
                {
                    pair.blobHandle = AddToConvert(authoring.gameObject, new AudioClipBakeData { clip = authoring.clip, numVoices = authoring.voices });
                }
                m_sourceList.Add(pair);
            });
        }

        protected override void FinalizeOutputs()
        {
            foreach (var pair in m_sourceList)
            {
                var authoring = pair.authoring;
                var blob      = pair.blobHandle.IsValid ? pair.blobHandle.Resolve() : default;

                var entity = GetPrimaryEntity(authoring);
                if (!authoring.looping)
                {
                    DstEntityManager.AddComponentData(entity, new AudioSourceOneShot
                    {
                        clip            = blob,
                        innerRange      = authoring.innerRange,
                        outerRange      = authoring.outerRange,
                        rangeFadeMargin = authoring.rangeFadeMargin,
                        volume          = authoring.volume
                    });
                    if (authoring.autoDestroyOnFinish)
                    {
                        DstEntityManager.AddComponent<AudioSourceDestroyOneShotWhenFinished>(entity);
                    }
                }
                else
                {
                    DstEntityManager.AddComponentData(entity, new AudioSourceLooped
                    {
                        m_clip               = blob,
                        innerRange           = authoring.innerRange,
                        outerRange           = authoring.outerRange,
                        rangeFadeMargin      = authoring.rangeFadeMargin,
                        volume               = authoring.volume,
                        offsetIsBasedOnSpawn = authoring.playFromBeginningAtSpawn
                    });
                }
                if (authoring.useCone)
                {
                    DstEntityManager.AddComponentData(entity, new AudioSourceEmitterCone
                    {
                        cosInnerAngle         = math.cos(math.radians(authoring.innerAngle)),
                        cosOuterAngle         = math.cos(math.radians(authoring.outerAngle)),
                        outerAngleAttenuation = authoring.outerAngleVolume
                    });
                }
            }
        }

        protected override void Filter(FilterBlobberData blobberData, ref AudioClipContext context, NativeArray<int> inputToFilteredMapping)
        {
            var hashes = new NativeArray<int2>(blobberData.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < blobberData.Count; i++)
            {
                var input = blobberData.input[i];
                if (input.clip == null || input.clip.channels > 2)
                {
                    if (input.clip != null && input.clip.channels > 2)
                        Debug.LogError($"Myri failed to convert clip {input.clip.name}. Only mono and stereo clips are supported.");

                    hashes[i]                 = default;
                    inputToFilteredMapping[i] = -1;
                }
                else
                {
                    DeclareAssetDependency(blobberData.associatedObject[i], input.clip);
                    hashes[i] = new int2(input.clip.GetInstanceID(), input.numVoices);
                }
            }

            new DeduplicateJob { hashes = hashes, inputToFilteredMapping = inputToFilteredMapping }.Run();
            hashes.Dispose();
        }

        protected override void PostFilter(PostFilterBlobberData blobberData, ref AudioClipContext context)
        {
            int sampleCount = 0;
            for (int i = 0; i < blobberData.Count; i++)
            {
                var clip     = blobberData.input[i].clip;
                sampleCount += clip.samples * clip.channels;
            }

            context.samples = new NativeArray<float>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var cache       = new float[1024 * 8];

            var converters = blobberData.converters;

            int sampleStart = 0;
            for (int i = 0; i < blobberData.Count; i++)
            {
                var clip  = blobberData.input[i].clip;
                int count = clip.samples * clip.channels;
                ReadClip(clip, context.samples.GetSubArray(sampleStart, count), cache);

                converters[i] = new AudioClipConverter
                {
                    count      = count,
                    isStereo   = clip.channels == 2,
                    name       = clip.name,
                    numVoices  = blobberData.input[i].numVoices,
                    sampleRate = clip.frequency,
                    start      = sampleStart
                };

                sampleStart += count;
            }
        }

        void ReadClip(AudioClip clip, NativeArray<float> data, float[] cache)
        {
            int channels = clip.channels;
            int stride   = cache.Length / channels;
            for (int i = 0; i < data.Length; i += stride)
            {
                int count = math.min(data.Length - i * channels, stride);

                // Todo: This suppresses a warning but is not ideal
                if (count < cache.Length)
                {
                    var tempCache = new float[count];
                    clip.GetData(tempCache, i);
                    NativeArray<float>.Copy(tempCache, data.GetSubArray(i * channels, count), count);
                }
                else
                {
                    clip.GetData(cache, i);
                    NativeArray<float>.Copy(cache, data.GetSubArray(i * channels, count), count);
                }
            }
        }

        [BurstCompile]
        struct DeduplicateJob : IJob
        {
            [ReadOnly] public NativeArray<int2> hashes;
            public NativeArray<int>             inputToFilteredMapping;

            public void Execute()
            {
                var map = new NativeHashMap<int2, int>(hashes.Length, Allocator.Temp);
                for (int i = 0; i < hashes.Length; i++)
                {
                    if (inputToFilteredMapping[i] < 0)
                        continue;

                    if (map.TryGetValue(hashes[i], out int index))
                        inputToFilteredMapping[i] = index;
                    else
                        map.Add(hashes[i], i);
                }
            }
        }
    }

    public struct AudioClipConverter : ISmartBlobberContextBuilder<AudioClipBlob, AudioClipContext>
    {
        internal int                 start;
        internal int                 count;
        internal int                 sampleRate;
        internal int                 numVoices;
        internal FixedString128Bytes name;
        internal bool                isStereo;

        public BlobAssetReference<AudioClipBlob> BuildBlob(int _, int index, ref AudioClipContext context)
        {
            var     builder  = new BlobBuilder(Allocator.Temp);
            ref var root     = ref builder.ConstructRoot<AudioClipBlob>();
            var     blobLeft = builder.Allocate(ref root.samplesLeftOrMono, count / math.select(1, 2, isStereo));
            if (isStereo)
            {
                var blobRight = builder.Allocate(ref root.samplesRight, count / 2);
                for (int i = 0; i < count; i++)
                {
                    blobLeft[i / 2] = context.samples[start + i];
                    i++;
                    blobRight[i / 2] = context.samples[start + i];
                }
            }
            else
            {
                var blobRight = builder.Allocate(ref root.samplesRight, 1);
                blobRight[0]  = 0f;
                for (int i = 0; i < count; i++)
                {
                    blobLeft[i] = context.samples[start + i];
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

            return builder.CreateBlobAssetReference<AudioClipBlob>(Allocator.Persistent);
        }
    }

    public struct AudioClipContext : System.IDisposable
    {
        [ReadOnly] internal NativeArray<float> samples;
        public void Dispose() => samples.Dispose();
    }
}

