using Latios.Authoring;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Input for the ParameterClipSetBlob Smart Blobber.
    /// Kinemation Smart Blobbers only ever read this config, meaning it is safe to share a NativeArray
    /// between multiple configs. Kinemation will copy the data as well, so such NativeArrays may be allocated
    /// with Allocator.Temp.
    /// </summary>
    public struct ParameterClipSetConfig
    {
        /// <summary>
        /// The list of clips and their compression settings which should be baked into the clip set.
        /// </summary>
        public NativeArray<ParameterClipConfig> clips;
        /// <summary>
        /// The list of parameter names which should be baked into the clip set. The length of this array
        /// should either match the number of parameters in each and every clip or be left defaulted (unallocated).
        /// </summary>
        public NativeArray<FixedString128Bytes> parameterNames;
    }

    /// <summary>
    /// Defines a general purpose parameter animation clip as well as its compression settings.
    /// </summary>
    public struct ParameterClipConfig
    {
        /// <summary>
        /// A struct containing the parameter samples that should be compressed and baked for a parameter
        /// </summary>
        public struct Parameter
        {
            /// <summary>
            /// The samples for the parameter sampled at the specified sampleRate.
            /// The first and last sample must match for looping clips or else there will be a "jump".
            /// </summary>
            public NativeArray<float> samples;
            /// <summary>
            /// The maximum error allowed when compressing this parameter in this clip
            /// </summary>
            public float maxError;
        }

        /// <summary>
        /// An array of Parameters which each contain all parameter samples for the clip.
        /// </summary>
        public NativeArray<Parameter> parametersInClip;
        /// <summary>
        /// An array of events to be attached to the clip blob. The order will be sorted during blob creation.
        /// </summary>
        public NativeArray<ClipEvent> events;
        /// <summary>
        /// The name of the clip.
        /// </summary>
        public FixedString128Bytes name;
        /// <summary>
        /// The number of samples per second.
        /// </summary>
        public float sampleRate;
        /// <summary>
        /// Higher levels lead to longer compression time but more compressed clips.
        /// Values range from 0 to 4 or 100 for automatic mode.
        /// </summary>
        public short compressionLevel;

        public static readonly short defaultCompressionLevel = 100;
        public static readonly float defaultMaxError         = 0.00001f;

        /// <summary>
        /// Samples an AnimationCurve repeatedly and stores the samples as the samples for a specified parameter
        /// </summary>
        /// <param name="sampleRate">The sample rate that the curve should be sampled at</param>
        /// <param name="curve">The animation curve that should be sampled</param>
        /// <param name="allocator">An allocator for sampling the clips, usually Allocator.Temp</param>
        /// <returns></returns>
        public static NativeArray<float> SampleCurve(float sampleRate, AnimationCurve curve, AllocatorManager.AllocatorHandle allocator)
        {
            var duration = curve[curve.length - 1].time;

            var samples = CollectionHelper.CreateNativeArray<float>((int)math.round(sampleRate * duration), allocator, NativeArrayOptions.UninitializedMemory);

            // We subtract 1 because we need to capture the samples at t = 0 and t = 1
            float timeStep = math.rcp(sampleRate);
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = curve.Evaluate(timeStep * i);
            }
            return samples;
        }
    }

    public static class ParameterClipSetBlobberAPIExtensions
    {
        /// <summary>
        /// Requests creation of a ParameterClipSetBlob by a Smart Blobber.
        /// This method must be called before the Smart Blobber is executed, such as during IRequestBlobAssets.
        /// </summary>
        /// <param name="config">The inputs used to generate the blob asset</param>
        /// <returns>Returns a handle that can be resolved into a blob asset after the Smart Blobber has executed, such as during IConvertGameObjectToEntity</returns>
        public static SmartBlobberHandle<ParameterClipSetBlob> RequestCreateBlobAsset(this IBaker baker, ParameterClipSetConfig config)
        {
            return baker.RequestCreateBlobAsset<ParameterClipSetBlob, ParameterClipSetBakeData>(new ParameterClipSetBakeData { config = config});
        }
    }

    public static class ParameterClipBlobBuilderExtensions
    {
        /// <summary>
        /// Creates a ParameterClip for a set of parameters, invoking AclUnity compression.
        /// Use this when embedding ParameterClip instances inside of custom blob assets.
        /// </summary>
        /// <param name="destinationClip">This target clip within a blob asset being built</param>
        /// <param name="config">The configuration containing all the input data that should go into the ParameterClip</param>
        /// <param name="scratchCache">An optional scratch NativeList that can be passed in on repeated calls to speed up blob generation and reduce temporary memory usage</param>
        /// <returns>True if the clip was successfully built, false if there was an error with the inputs</returns>
        public static unsafe bool CreateParameterClip(this ref BlobBuilder builder,
                                                      ref ParameterClip destinationClip,
                                                      in ParameterClipConfig config,
                                                      NativeList<float>      scratchCache = default)
        {
            if (config.sampleRate <= 0f)
            {
                Debug.LogError("Kinemation failed to bake the parameter clip. The clip had a sample rate less or equal to 0f. This is not allowed.");
                return false;
            }

            var sampleCount      = config.parametersInClip[0].samples.Length;
            var totalSampleCount = sampleCount * config.parametersInClip.Length;
            if (!scratchCache.IsCreated)
                scratchCache = new NativeList<float>(totalSampleCount, Allocator.Temp);
            scratchCache.Clear();
            foreach (var parameter in config.parametersInClip)
            {
                if (parameter.samples.Length != sampleCount)
                {
                    Debug.LogError("Kinemation failed to bake parameter clip. A clip does not contain the same number of samples for all parameters.");
                    return false;
                }
                scratchCache.Add(parameter.maxError);
            }
            foreach (var parameter in config.parametersInClip)
                scratchCache.AddRange(parameter.samples);
            var scratchArray = scratchCache.AsArray();
            var errors       = scratchArray.GetSubArray(0, config.parametersInClip.Length);
            var samples      = scratchArray.GetSubArray(config.parametersInClip.Length, totalSampleCount);

            destinationClip      = default;
            destinationClip.name = config.name;
            ClipEventsBlobHelpers.Convert(ref destinationClip.events, ref builder, config.events);

            var compressedClip = AclUnity.Compression.CompressScalarsClip(samples, errors, config.sampleRate, config.compressionLevel);
            var compressedData = builder.Allocate(ref destinationClip.compressedClipDataAligned16, compressedClip.sizeInBytes, 16);
            compressedClip.CopyTo((byte*)compressedData.GetUnsafePtr());
            compressedClip.Dispose();

            return true;
        }
    }

    /// <summary>
    /// Input for the ParameterClipSetBlob Smart Blobber
    /// </summary>
    public struct ParameterClipSetBakeData : ISmartBlobberRequestFilter<ParameterClipSetBlob>
    {
        /// <summary>
        /// Full configuration of clips and settings for created a compressed ParameterClipSetBlob
        /// </summary>
        public ParameterClipSetConfig config;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (!config.clips.IsCreated)
            {
                Debug.LogError("Kinemation failed to bake parameter clip set. The configuration contains no clips.");
                return false;
            }

            int totalSampleCount = 0;
            int totalEventCount  = 0;
            var parameterCount   = config.clips[0].parametersInClip.Length;
            foreach (var clip in config.clips)
            {
                if (clip.parametersInClip.Length != parameterCount)
                {
                    Debug.LogError("Kinemation failed to bake parameter clip set. Clips have different parameter counts.");
                    return false;
                }

                var sampleCount   = clip.parametersInClip[0].samples.Length;
                totalSampleCount += sampleCount;
                foreach (var parameter in clip.parametersInClip)
                {
                    if (parameter.samples.Length != sampleCount)
                    {
                        Debug.LogError("Kinemation failed to bake parameter clip set. A clip does not contain the same number of samples for all parameters.");
                        return false;
                    }
                }

                if (clip.sampleRate <= 0f)
                {
                    Debug.LogError("Kinemation failed to bake the parameter clip set. A clip had a sample rate less or equal to 0f. This is not allowed.");
                }

                if (clip.events.IsCreated)
                    totalEventCount += clip.events.Length;
            }
            if (config.parameterNames.IsCreated && config.parameterNames.Length != parameterCount)
            {
                Debug.LogError("Kinemation failed to bake parameter clip set. The number of clip names do not match the number of parameters in the clips.");
                return false;
            }

            var samples = baker.AddBuffer<ParameterSample>(blobBakingEntity).Reinterpret<float>();
            samples.EnsureCapacity(totalSampleCount);
            var errors = baker.AddBuffer<ParameterError>(blobBakingEntity);
            errors.EnsureCapacity(parameterCount * config.clips.Length);
            var settings = baker.AddBuffer<ParameterClipSettings>(blobBakingEntity);
            settings.EnsureCapacity(config.clips.Length);
            var events = baker.AddBuffer<ClipEventToBake>(blobBakingEntity).Reinterpret<ClipEvent>();
            events.EnsureCapacity(totalEventCount);
            if (config.parameterNames.IsCreated)
            {
                var parameterNames = baker.AddBuffer<ParameterName>(blobBakingEntity).Reinterpret<FixedString128Bytes>();
                parameterNames.AddRange(config.parameterNames);
            }

            foreach (var clip in config.clips)
            {
                settings.Add(new ParameterClipSettings
                {
                    clipName         = clip.name,
                    compressionLevel = clip.compressionLevel,
                    sampleCount      = clip.parametersInClip[0].samples.Length,
                    eventCount       = clip.events.Length,
                    sampleRate       = clip.sampleRate
                });
                events.AddRange(clip.events);
                foreach (var parameter in clip.parametersInClip)
                {
                    samples.AddRange(parameter.samples);
                    errors.Add(new ParameterError { maxError = parameter.maxError });
                }
            }
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct ParameterSample : IBufferElementData
    {
        public float sample;
    }

    [TemporaryBakingType]
    internal struct ParameterError : IBufferElementData
    {
        public float maxError;
    }

    [TemporaryBakingType]
    internal struct ParameterClipSettings : IBufferElementData
    {
        public int                 sampleCount;
        public float               sampleRate;
        public int                 eventCount;
        public short               compressionLevel;
        public FixedString128Bytes clipName;
    }

    [TemporaryBakingType]
    internal struct ParameterName : IBufferElementData
    {
        public FixedString128Bytes name;
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ParameterClipSetSmartBlobberSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job { parameterNameLookup = SystemAPI.GetBufferLookup<ParameterName>(true) }.ScheduleParallel();
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct Job : IJobEntity
        {
            [ReadOnly] public BufferLookup<ParameterName> parameterNameLookup;

            public unsafe void Execute(Entity entity,
                                       ref SmartBlobberResult result,
                                       ref DynamicBuffer<ClipEventToBake>      clipEventsBuffer,  // Ref so it can be sorted
                                       in DynamicBuffer<ParameterSample>       samples,
                                       in DynamicBuffer<ParameterError>        errors,
                                       in DynamicBuffer<ParameterClipSettings> settings)
            {
                var     builder = new BlobBuilder(Allocator.Temp);
                ref var root    = ref builder.ConstructRoot<ParameterClipSetBlob>();

                // Build names
                if (parameterNameLookup.TryGetBuffer(entity, out var parameterNames))
                {
                    var names  = builder.Allocate(ref root.parameterNames, parameterNames.Length);
                    var hashes = builder.Allocate(ref root.parameterNameHashes, parameterNames.Length);

                    for (int i = 0; i < parameterNames.Length; i++)
                    {
                        names[i]  = parameterNames[i].name;
                        hashes[i] = parameterNames[i].name.GetHashCode();
                    }
                }
                else
                {
                    builder.Allocate(ref root.parameterNames,      0);
                    builder.Allocate(ref root.parameterNameHashes, 0);
                }

                // Build clips
                int samplesReadSoFar = 0;
                int eventsReadSoFar  = 0;
                var events           = clipEventsBuffer.Reinterpret<ClipEvent>().AsNativeArray();

                int parameterCount = errors.Length / settings.Length;
                var dstClips       = builder.Allocate(ref root.clips, settings.Length);
                for (int i = 0; i < settings.Length; i++)
                {
                    dstClips[i]      = default;
                    dstClips[i].name = settings[i].clipName;
                    ClipEventsBlobHelpers.Convert(ref dstClips[i].events, ref builder, events.GetSubArray(eventsReadSoFar, settings[i].eventCount));
                    eventsReadSoFar += settings[i].eventCount;

                    var srcClipDataPacked = samples.Reinterpret<float>().AsNativeArray().GetSubArray(samplesReadSoFar, parameterCount * settings[i].sampleCount);
                    var srcClipErrors     = errors.Reinterpret<float>().AsNativeArray().GetSubArray(i * parameterCount, parameterCount);

                    var compressedClip = AclUnity.Compression.CompressScalarsClip(srcClipDataPacked, srcClipErrors, settings[i].sampleRate, settings[i].compressionLevel);
                    var compressedData = builder.Allocate(ref dstClips[i].compressedClipDataAligned16, compressedClip.sizeInBytes, 16);
                    compressedClip.CopyTo((byte*)compressedData.GetUnsafePtr());
                    compressedClip.Dispose();
                }

                root.parameterCount = (short)parameterCount;

                result.blob = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<ParameterClipSetBlob>(Allocator.Persistent));
            }
        }
    }
}

