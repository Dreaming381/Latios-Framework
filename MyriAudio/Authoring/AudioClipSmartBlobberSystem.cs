using System;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
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
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                Debug.LogError($"Myri failed to bake clip {clip.name}. The clip must be imported with \"Decompress On Load\".");
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

            var hashmap   = new NativeParallelHashMap<int, UniqueItem>(count * 2, WorldUpdateAllocator);
            var mapWriter = hashmap.AsParallelWriter();

            Entities.WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ForEach((in AudioClipBlobBakeData data) =>
            {
                mapWriter.TryAdd(data.clip.GetHashCode(), new UniqueItem { bakeData = data });
            }).WithStoreEntityQueryInField(ref m_query).ScheduleParallel();

            var clips    = new NativeList<UnityObjectRef<AudioClip> >(WorldUpdateAllocator);
            var builders = new NativeList<AudioClipBuilder>(WorldUpdateAllocator);

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
        }

        void ReadClip(AudioClip clip, ref UnsafeList<float> samples)
        {
            int sampleCount = clip.samples * clip.channels;
            samples         = new UnsafeList<float>(sampleCount, WorldUpdateAllocator);
            samples.Resize(sampleCount, NativeArrayOptions.UninitializedMemory);
            Span<float> span = new Span<float>(samples.Ptr, sampleCount);
            clip.GetData(span, 0);
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

