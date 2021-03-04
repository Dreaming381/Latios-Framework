using System.Collections.Generic;
using AudioClip = UnityEngine.AudioClip;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri.Authoring.Systems
{
    [ConverterVersion("Latios", 1)]
    public class ClipsAndListenersConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            //Temporary workaround
            var tempList = new NativeList<int>(Allocator.TempJob);
            for (int i = 0; i < 5; i++)
                tempList.Add(i);
            unsafe
            {
                var result = xxHash3.Hash128(tempList.GetUnsafePtr(), tempList.Length * 4);
            }
            tempList.Dispose();

            ConvertBatchedClips();
            ConvertListeners();
        }

        #region Batched Clips
        struct AudioClipComputationData
        {
            public Hash128                           hash;
            public int                               index;
            public BlobAssetReference<AudioClipBlob> blob;
        }

        void ConvertBatchedClips()
        {
            var clipList = new List<AudioClip>();

            using (var computationContext = new BlobAssetComputationContext<AudioClipComputationData, AudioClipBlob>(BlobAssetStore, 128, Allocator.Temp))
            {
                var hashes     = new NativeList<Hash128>(Allocator.Persistent);
                var jobs       = new List<ComputeAudioClipHashesJob>();
                var jobHandles = new NativeList<JobHandle>(Allocator.Persistent);
                Entities.ForEach((AudioSourceAuthoring authoring) =>
                {
                    if (authoring.clip != null)
                    {
                        int frequency = authoring.clip.frequency;
                        var arr       = new float[authoring.clip.samples * authoring.clip.channels];
                        authoring.clip.GetData(arr, 0);
                        var job = new ComputeAudioClipHashesJob
                        {
                            samples   = new NativeArray<float>(arr, Allocator.Persistent),
                            hash      = new NativeReference<uint2>(Allocator.Persistent),
                            frequency = frequency
                        };
                        jobs.Add(job);
                        jobHandles.Add(job.Schedule());
                    }
                });
                JobHandle.CompleteAll(jobHandles);
                jobHandles.Dispose();

                int index = 0;
                Entities.ForEach((AudioSourceAuthoring authoring) =>
                {
                    if (authoring.clip != null)
                    {
                        var hash =
                            new Hash128(jobs[index].hash.Value.x, jobs[index].hash.Value.y, (uint)authoring.clip.channels, (uint)math.select(0, authoring.voices,
                                                                                                                                             authoring.looping));
                        computationContext.AssociateBlobAssetWithUnityObject(hash, authoring.gameObject);
                        if (computationContext.NeedToComputeBlobAsset(hash))
                        {
                            computationContext.AddBlobAssetToCompute(hash, new AudioClipComputationData { hash = hash, index = index });
                        }
                        index++;
                        clipList.Add(authoring.clip);
                        hashes.Add(hash);
                    }
                });
                foreach (var job in jobs)
                {
                    job.samples.Dispose();
                    job.hash.Dispose();
                }
                using (var computationDataArray = computationContext.GetSettings(Allocator.TempJob))
                {
                    var samples       = new NativeList<float>(Allocator.TempJob);
                    var ranges        = new NativeArray<int2>(computationDataArray.Length, Allocator.TempJob);
                    var rates         = new NativeArray<int>(computationDataArray.Length, Allocator.TempJob);
                    var channelCounts = new NativeArray<int>(computationDataArray.Length, Allocator.TempJob);
                    var offsets       = new NativeArray<int>(computationDataArray.Length, Allocator.TempJob);
                    var names         = new NativeArray<FixedString128>(computationDataArray.Length, Allocator.TempJob);
                    for (int i = 0; i < computationDataArray.Length; i++)
                    {
                        var clip           = clipList[computationDataArray[i].index];
                        var samplesManaged = new float[clip.samples * clip.channels];
                        clip.GetData(samplesManaged, 0);
                        var samplesUnmanaged = new NativeArray<float>(samplesManaged, Allocator.Temp);
                        ranges[i]            = new int2(samples.Length, samplesUnmanaged.Length);
                        samples.AddRange(samplesUnmanaged);
                        rates[i]         = clip.frequency;
                        channelCounts[i] = clip.channels;
                        offsets[i]       = (int)computationDataArray[i].hash.Value.w;
                        names[i]         = clip.name;
                    }
                    new ComputeAudioClipBlobsJob
                    {
                        samples              = samples,
                        ranges               = ranges,
                        rates                = rates,
                        channelCounts        = channelCounts,
                        computationDataArray = computationDataArray,
                        offsetCounts         = offsets,
                        names                = names
                    }.ScheduleParallel(ranges.Length, 1, default).Complete();
                    foreach (var data in computationDataArray)
                    {
                        computationContext.AddComputedBlobAsset(data.hash, data.blob);
                    }
                    samples.Dispose();
                    ranges.Dispose();
                    rates.Dispose();
                    channelCounts.Dispose();
                    offsets.Dispose();
                    names.Dispose();

                    index = 0;
                    Entities.ForEach((AudioSourceAuthoring authoring) =>
                    {
                        if (authoring.clip != null)
                        {
                            var hash = hashes[index];
                            computationContext.GetBlobAsset(hash, out var blob);

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
                                    m_clip          = blob,
                                    innerRange      = authoring.innerRange,
                                    outerRange      = authoring.outerRange,
                                    rangeFadeMargin = authoring.rangeFadeMargin,
                                    volume          = authoring.volume,
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
                            index++;
                        }
                    });
                }
                hashes.Dispose();
            }
        }

        [BurstCompile]
        struct ComputeAudioClipHashesJob : IJob
        {
            [ReadOnly] public NativeArray<float> samples;
            public NativeReference<uint2>        hash;
            public int                           frequency;

            public unsafe void Execute()
            {
                var result = xxHash3.Hash64(samples.GetUnsafeReadOnlyPtr(), samples.Length * 4, (ulong)math.asuint(frequency) << 32 & (ulong)samples.Length);
                hash.Value = result;
            }
        }

        [BurstCompile]
        struct ComputeAudioClipBlobsJob : IJobFor
        {
            [ReadOnly] public NativeArray<float>          samples;
            [ReadOnly] public NativeArray<int2>           ranges;
            [ReadOnly] public NativeArray<int>            rates;
            [ReadOnly] public NativeArray<int>            channelCounts;
            [ReadOnly] public NativeArray<int>            offsetCounts;
            [ReadOnly] public NativeArray<FixedString128> names;
            public NativeArray<AudioClipComputationData>  computationDataArray;

            public void Execute(int index)
            {
                var     builder  = new BlobBuilder(Allocator.Temp);
                ref var root     = ref builder.ConstructRoot<AudioClipBlob>();
                var     blobLeft = builder.Allocate(ref root.samplesLeftOrMono, ranges[index].y / channelCounts[index]);
                if (channelCounts[index] == 2)
                {
                    var blobRight = builder.Allocate(ref root.samplesRight, ranges[index].y / 2);
                    for (int i = 0; i < ranges[index].y; i++)
                    {
                        blobLeft[i / 2] = samples[ranges[index].x + i];
                        i++;
                        blobRight[i / 2] = samples[ranges[index].x + i];
                    }
                }
                else
                {
                    var blobRight = builder.Allocate(ref root.samplesRight, 1);
                    blobRight[0]  = 0f;
                    for (int i = 0; i < ranges[index].y; i += channelCounts[index])
                    {
                        blobLeft[i / channelCounts[index]] = samples[ranges[index].x + i];
                    }
                }
                int offsetCount = math.max(offsetCounts[index], 1);
                int stride      = blobLeft.Length / offsetCount;
                var offsets     = builder.Allocate(ref root.loopedOffsets, offsetCount);
                for (int i = 0; i < offsetCount; i++)
                {
                    offsets[i] = i * stride;
                }
                root.sampleRate = rates[index];
                builder.AllocateFixedString(ref root.name, names[index]);
                var computationData         = computationDataArray[index];
                computationData.blob        = builder.CreateBlobAssetReference<AudioClipBlob>(Allocator.Persistent);
                computationDataArray[index] = computationData;
            }
        }
        #endregion

        #region Listeners
        struct IldProfileComputationData
        {
            public Hash128 hash;
            public int     index;
        }

        void ConvertListeners()
        {
            var profileList           = new List<AudioIldProfileBuilder>();
            var profileHashDictionary = new Dictionary<AudioIldProfileBuilder, Hash128>();

            var defaultProfileBuilder = DefaultIldProfileBuilder.CreateInstance<DefaultIldProfileBuilder>();

            using (var computationContext = new BlobAssetComputationContext<IldProfileComputationData, IldProfileBlob>(BlobAssetStore, 128, Allocator.Temp))
            {
                int index = 0;
                Entities.ForEach((AudioListenerAuthoring authoring) =>
                {
                    Hash128 hash = default;
                    if (authoring.listenerResponseProfile == null)
                        authoring.listenerResponseProfile = defaultProfileBuilder;
                    if (profileHashDictionary.TryGetValue(authoring.listenerResponseProfile, out var foundHash))
                        hash = foundHash;
                    else
                        hash = authoring.listenerResponseProfile.ComputeHash();
                    computationContext.AssociateBlobAssetWithUnityObject(hash, authoring.gameObject);
                    if (computationContext.NeedToComputeBlobAsset(hash))
                    {
                        computationContext.AddBlobAssetToCompute(hash, new IldProfileComputationData { hash = hash, index = index });
                    }
                    profileList.Add(authoring.listenerResponseProfile);
                    profileHashDictionary.Add(authoring.listenerResponseProfile, hash);
                    index++;
                });

                using (var computationDataArray = computationContext.GetSettings(Allocator.TempJob))
                {
                    for (int i = 0; i < computationDataArray.Length; i++)
                    {
                        var blob = profileList[computationDataArray[i].index].ComputeBlob();
                        computationContext.AddComputedBlobAsset(computationDataArray[i].hash, blob);
                    }

                    Entities.ForEach((AudioListenerAuthoring authoring) =>
                    {
                        var hash = profileHashDictionary[authoring.listenerResponseProfile];
                        computationContext.GetBlobAsset(hash, out var blob);

                        var entity = GetPrimaryEntity(authoring);
                        DstEntityManager.AddComponentData(entity, new AudioListener
                        {
                            volume        = authoring.volume,
                            itdResolution = authoring.interauralTimeDifferenceResolution,
                            ildProfile    = blob
                        });
                    });
                }
            }
        }
        #endregion
    }
}

