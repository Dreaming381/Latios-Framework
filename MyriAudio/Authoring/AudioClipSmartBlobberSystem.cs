using System;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
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
        /// <param name="codec">The compression codec to use</param>
        /// <param name="numVoices">The number of voices to use (only applies to Looping clips)</param>
        public static SmartBlobberHandle<AudioClipBlob> RequestCreateBlobAsset(this IBaker baker, AudioClip clip, Codec codec = Codec.Uncompressed, int numVoices = 0)
        {
            return baker.RequestCreateBlobAsset<AudioClipBlob, AudioClipBakeData>(new AudioClipBakeData { clip = clip, numVoices = numVoices, codec = codec });
        }
    }

    public struct AudioClipBakeData : ISmartBlobberRequestFilter<AudioClipBlob>
    {
        public AudioClip clip;
        public int       numVoices;
        public Codec     codec;

        public AudioClipBakeData(AudioClip clip, int numVoices, Codec codec)
        {
            this.clip      = clip;
            this.numVoices = numVoices;
            this.codec     = codec;
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
            baker.AddComponent(blobBakingEntity, new AudioClipBlobBakeData { clip = clip, numVoices = numVoices, codec = codec });
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct AudioClipBlobBakeData : IComponentData, IEquatable<AudioClipBlobBakeData>
    {
        public UnityObjectRef<AudioClip> clip;
        public int                       numVoices;
        public Codec                     codec;

        public bool Equals(AudioClipBlobBakeData other)
        {
            return clip.Equals(other.clip) && numVoices == other.numVoices && codec == other.codec;
        }

        public override int GetHashCode()
        {
            return new int3(clip.GetHashCode(), numVoices, (int)codec).GetHashCode();
        }
    }
}

namespace Latios.Myri.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    public unsafe partial struct AudioClipSmartBlobberSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            new SmartBlobberTools<AudioClipBlob>().Register(state.World);
            m_query = state.Fluent().With<AudioClipBlobBakeData>(true).With<SmartBlobberResult>(false).IncludeDisabledEntities().IncludePrefabs().Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            int count   = m_query.CalculateEntityCountWithoutFiltering();
            var hashmap = new NativeParallelHashMap<AudioClipBlobBakeData, BlobAssetReference<AudioClipBlob> >(count * 2, state.WorldUpdateAllocator);

            new CollectJob { hashmap = hashmap.AsParallelWriter() }.ScheduleParallel(m_query);

            var builders     = new NativeList<AudioClipBuilder>(state.WorldUpdateAllocator);
            state.Dependency = new SetupListJob { builders = builders, hashmap = hashmap }.Schedule(state.Dependency);

            state.CompleteDependency();

            for (int i = 0; i < builders.Length; i++)
            {
                var builder        = builders[i];
                var clip           = builder.bakeData.clip.Value;
                builder.isStereo   = clip.channels == 2;
                builder.name       = clip.name;
                builder.sampleRate = clip.frequency;
                ReadClip(ref state, clip, ref builder.samples);
                builders[i] = builder;
            }

            state.Dependency = new BuildBlobsJob
            {
                builders = builders.AsArray()
            }.ScheduleParallel(builders.Length, 1, state.Dependency);

            state.Dependency = new CollectBlobsJob
            {
                builders = builders.AsArray(),
                hashmap  = hashmap
            }.Schedule(state.Dependency);

            new AssignJob { hashmap = hashmap }.ScheduleParallel(m_query);
        }

        void ReadClip(ref SystemState state, AudioClip clip, ref UnsafeList<float> samples)
        {
            int sampleCount = clip.samples * clip.channels;
            samples         = new UnsafeList<float>(sampleCount, state.WorldUpdateAllocator);
            samples.Resize(sampleCount, NativeArrayOptions.UninitializedMemory);
            Span<float> span = new Span<float>(samples.Ptr, sampleCount);
            clip.GetData(span, 0);
        }

        [BurstCompile]
        partial struct CollectJob : IJobEntity
        {
            public NativeParallelHashMap<AudioClipBlobBakeData, BlobAssetReference<AudioClipBlob> >.ParallelWriter hashmap;

            public void Execute(in AudioClipBlobBakeData bakeData) => hashmap.TryAdd(bakeData, default);
        }

        [BurstCompile]
        struct SetupListJob : IJob
        {
            [ReadOnly] public NativeParallelHashMap<AudioClipBlobBakeData, BlobAssetReference<AudioClipBlob> > hashmap;
            public NativeList<AudioClipBuilder>                                                                builders;

            public void Execute()
            {
                var count         = hashmap.Count();
                builders.Capacity = count;

                foreach (var pair in hashmap)
                    builders.Add(new AudioClipBuilder { bakeData = pair.Key });
            }
        }

        struct AudioClipBuilder
        {
            public AudioClipBlobBakeData             bakeData;
            public UnsafeList<float>                 samples;
            public int                               sampleRate;
            public FixedString128Bytes               name;
            public bool                              isStereo;
            public BlobAssetReference<AudioClipBlob> result;

            public void BuildBlob()
            {
                var     builder = new BlobBuilder(Allocator.Temp);
                ref var root    = ref builder.ConstructRoot<AudioClipBlob>();

                var context = new CodecContext
                {
                    sampleRate           = sampleRate,
                    threadStackAllocator = ThreadStackAllocator.GetAllocator()
                };

                if (isStereo)
                {
                    root.sampleCountPerChannel = samples.Length / 2;
                    var left                   = context.threadStackAllocator.AllocateAsSpan<float>(samples.Length / 2);
                    var right                  = context.threadStackAllocator.AllocateAsSpan<float>(samples.Length / 2);

                    for (int i = 0; i < samples.Length; i++)
                    {
                        left[i / 2] = samples[i];
                        i++;
                        right[i / 2] = samples[i];
                    }

                    CodecDispatch.Encode(bakeData.codec, ref builder, ref root.encodedSamples, left, right, ref context);
                }
                else
                {
                    root.sampleCountPerChannel = samples.Length;
                    var mono                   = new Span<float>(samples.Ptr, samples.Length);
                    CodecDispatch.Encode(bakeData.codec, ref builder, ref root.encodedSamples, mono, ref context);
                }

                int offsetCount = math.max(bakeData.numVoices, 1);
                int stride      = root.sampleCountPerChannel / offsetCount;
                var offsets     = builder.Allocate(ref root.loopedOffsets, offsetCount);
                for (int i = 0; i < offsetCount; i++)
                {
                    offsets[i] = i * stride;
                }
                root.sampleRate = sampleRate;
                root.isStereo   = isStereo;
                root.codec      = bakeData.codec;
                root.name       = name;

                result = builder.CreateBlobAssetReference<AudioClipBlob>(Allocator.Persistent);

                context.threadStackAllocator.Dispose();
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

        [BurstCompile]
        struct CollectBlobsJob : IJob
        {
            [ReadOnly] public NativeArray<AudioClipBuilder>                                         builders;
            public NativeParallelHashMap<AudioClipBlobBakeData, BlobAssetReference<AudioClipBlob> > hashmap;

            public void Execute()
            {
                foreach (var builder in builders)
                    hashmap[builder.bakeData] = builder.result;
            }
        }

        [BurstCompile]
        partial struct AssignJob : IJobEntity
        {
            [ReadOnly] public NativeParallelHashMap<AudioClipBlobBakeData, BlobAssetReference<AudioClipBlob> > hashmap;

            public void Execute(ref SmartBlobberResult result, in AudioClipBlobBakeData bakeData)
            {
                result.blob = UnsafeUntypedBlobAssetReference.Create(hashmap[bakeData]);
            }
        }
    }
}

