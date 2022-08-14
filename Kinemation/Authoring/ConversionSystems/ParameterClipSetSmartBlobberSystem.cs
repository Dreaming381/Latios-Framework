using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Defines a general purpose parameter animation clip as well as its compression settings.
    /// All values must be allocated with the conversion world's UpdateAllocator.
    /// Kinemation Smart Blobbers only ever read this config, meaning it is safe to share an UnsafeList between multiple configs.
    /// </summary>
    public struct ParameterClipConfig
    {
        /// <summary>
        /// An array of arrays of samples per parameter.
        /// The first and last sample of each parameter must match for looping clips or else there will be a "jump".
        /// </summary>
        public UnsafeList<UnsafeList<float> > clipData;
        /// <summary>
        /// The error threshold allowed for each parameter relative to the input values.
        /// If left defaulted, globalMaxError will be used instead.
        /// </summary>
        public UnsafeList<float> maxErrorsByParameter;
        /// <summary>
        /// An array of events to be attached to the clip blob. The order will be sorted during blob creation.
        /// </summary>
        public UnsafeList<ClipEvent> events;
        /// <summary>
        /// If maxErrorsByParamemeter.IsCreated is false, this value will be used for all parameters.
        /// </summary>
        public float globalMaxError;
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
        /// Values range from 0 to 4. Typical default is 2.
        /// </summary>
        public short compressionLevel;

        public static readonly short defaultCompressionLevel = 2;
        public static readonly float defaultMaxError         = 0.00001f;

        /// <summary>
        /// Samples an AnimationCurve repeatedly and stores the samples as the samples for a specified parameter
        /// </summary>
        /// <param name="samples">A destination buffer which must be preallocated with enough memory for all samples for all parameters</param>
        /// <param name="parameterIndex">The index of the parameter to sample the curve for</param>
        /// <param name="sampleCountPerParameter">The number of samples to use when sampling the curve.
        /// This value should match for all parameters or else the result will be undefined.</param>
        /// <param name="curve">The curve to be sampled</param>
        public static UnsafeList<float> SampleCurve(int sampleCountPerParameter, AnimationCurve curve, ref RewindableAllocator worldUpdateAllocator)
        {
            var samples = new UnsafeList<float>(sampleCountPerParameter, worldUpdateAllocator.ToAllocator);
            samples.Resize(sampleCountPerParameter);

            // We subtract 1 because we need to capture the samples at t = 0 and t = 1
            float timeStep = math.rcp(sampleCountPerParameter - 1);
            for (int i = 0; i < sampleCountPerParameter; i++)
            {
                samples[i] = curve.Evaluate(timeStep * i);
            }
            return samples;
        }
    }

    /// <summary>
    /// Input for the ParameterClipSetBlob Smart Blobber.
    /// All values must be allocated with the conversion world's UpdateAllocator.
    /// Kinemation Smart Blobbers only ever read this bake data, meaning it is safe to share an UnsafeList between multiple bake data instances.
    /// </summary>
    public struct ParameterClipSetBakeData
    {
        /// <summary>
        /// The list of clips and their compression settings which should be baked into the clip set.
        /// </summary>
        public UnsafeList<ParameterClipConfig> clips;
        /// <summary>
        /// The list of parameter names which should be baked into the clip set. The length of this array
        /// should either match the number of parameters in each and every clip or be left defaulted.
        /// </summary>
        public UnsafeList<FixedString64Bytes> parameterNames;
    }

    public static class ParameterClipBlobberAPIExtensions
    {
        /// <summary>
        /// Requests creation of a ParameterClipSetBlob by a Smart Blobber.
        /// This method must be called before the Smart Blobber is executed, such as during IRequestBlobAssets.
        /// </summary>
        /// <param name="gameObject">
        /// The Game Object to be converted that this blob should primarily be associated with.
        /// It is usually okay if this isn't quite accurate, such as if the blob will be added to multiple entities.
        /// </param>
        /// <param name="bakeData">The inputs used to generate the blob asset</param>
        /// <returns>Returns a handle that can be resolved into a blob asset after the Smart Blobber has executed, such as during IConvertGameObjectToEntity</returns>
        public static SmartBlobberHandle<ParameterClipSetBlob> CreateBlob(this GameObjectConversionSystem conversionSystem,
                                                                          GameObject gameObject,
                                                                          ParameterClipSetBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.ParameterClipSetSmartBlobberSystem>().AddToConvert(gameObject, bakeData);
        }

        /// <summary>
        /// Requests creation of a ParameterClipSetBlob by a Smart Blobber.
        /// This method must be called before the Smart Blobber is executed, such as during IRequestBlobAssets.
        /// </summary>
        /// <param name="gameObject">
        /// The Game Object to be converted that this blob should primarily be associated with.
        /// It is usually okay if this isn't quite accurate, such as if the blob will be added to multiple entities.
        /// </param>
        /// <param name="bakeData">The inputs used to generate the blob asset</param>
        /// <returns>Returns a handle that can be resolved into an untyped blob asset after the Smart Blobber has executed, such as during IConvertGameObjectToEntity</returns>
        public static SmartBlobberHandleUntyped CreateBlobUntyped(this GameObjectConversionSystem conversionSystem,
                                                                  GameObject gameObject,
                                                                  ParameterClipSetBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.ParameterClipSetSmartBlobberSystem>().AddToConvertUntyped(gameObject, bakeData);
        }
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [ConverterVersion("Latios", 1)]
    [DisableAutoCreation]
    [BurstCompile]
    public class ParameterClipSetSmartBlobberSystem : SmartBlobberConversionSystem<ParameterClipSetBlob, ParameterClipSetBakeData, ParameterClipSetConverter>
    {
        protected override bool Filter(in ParameterClipSetBakeData input, GameObject gameObject, out ParameterClipSetConverter converter)
        {
            converter.bakeData = input;

            return FilterBursted(input, gameObject.name);
        }

        [BurstCompile]
        private static bool FilterBursted(in ParameterClipSetBakeData input, in FixedString128Bytes gameObjectName)
        {
            if (!input.clips.IsCreated)
                return false;

            int parameterCount = -1;
            foreach (var clip in input.clips)
            {
                if (!clip.clipData.IsCreated)
                {
                    Debug.LogError($"Kinemation failed to convert parameter clip \"{clip.name}\" on GameObject {gameObjectName} because the clipData was unitialized.");
                    return false;
                }
                if (parameterCount < 0)
                    parameterCount = clip.clipData.Length;
                else if (parameterCount != clip.clipData.Length)
                {
                    Debug.LogError(
                        $"Kinemation failed to convert parameter clip \"{clip.name}\" on GameObject {gameObjectName} because the length of clipData ({clip.clipData.Length}) did not match previous clip parameter count of {parameterCount}.");
                    return false;
                }

                var sampleCount = -1;
                foreach (var samples in clip.clipData)
                {
                    if (!samples.IsCreated)
                    {
                        Debug.LogError($"Kinemation failed to convert parameter clip \"{clip.name}\" on GameObject {gameObjectName} because some of its samples were unitialized.");
                        return false;
                    }

                    if (sampleCount < 0)
                        sampleCount = samples.Length;
                    else if (sampleCount != samples.Length)
                    {
                        Debug.LogError(
                            $"Kinemation failed to convert parameter clip \"{clip.name}\" on GameObject {gameObjectName} because the number of samples is not the same for all parameters.");
                        return false;
                    }
                }

                if (clip.maxErrorsByParameter.IsCreated)
                {
                    if (clip.maxErrorsByParameter.Length != parameterCount)
                    {
                        Debug.LogError(
                            $"Kinemation failed to convert parameter clip \"{clip.name}\" on GameObject {gameObjectName} because the length of maxErrorsByParameter ({clip.maxErrorsByParameter.Length}) did not match the clip parameter count of {parameterCount}.");
                        return false;
                    }
                }
            }

            if (input.parameterNames.IsCreated)
            {
                if (input.parameterNames.Length != parameterCount)
                {
                    Debug.LogError(
                        $"Kinemation failed to convert parameter clip set on GameObject {gameObjectName} because the length of parameterNames ({input.parameterNames.Length}) did not match the parameter count of {parameterCount}.");
                    return false;
                }
            }

            return true;
        }
    }

    public struct ParameterClipSetConverter : ISmartBlobberSimpleBuilder<ParameterClipSetBlob>
    {
        public ParameterClipSetBakeData bakeData;

        public unsafe BlobAssetReference<ParameterClipSetBlob> BuildBlob()
        {
            var     builder = new BlobBuilder(Allocator.Temp);
            ref var root    = ref builder.ConstructRoot<ParameterClipSetBlob>();

            // Build names
            if (bakeData.parameterNames.IsCreated)
            {
                var names  = builder.Allocate(ref root.parameterNames, bakeData.parameterNames.Length);
                var hashes = builder.Allocate(ref root.parameterNameHashes, bakeData.parameterNames.Length);

                for (int i = 0; i < bakeData.parameterNames.Length; i++)
                {
                    names[i]  = bakeData.parameterNames[i];
                    hashes[i] = bakeData.parameterNames[i].GetHashCode();
                }
            }
            else
            {
                builder.Allocate(ref root.parameterNames,      0);
                builder.Allocate(ref root.parameterNameHashes, 0);
            }

            // Build clips
            var dstClips = builder.Allocate(ref root.clips, bakeData.clips.Length);
            for (int i = 0; i < bakeData.clips.Length; i++)
            {
                dstClips[i]                = default;
                dstClips[i].name           = bakeData.clips[i].name;
                dstClips[i].parameterCount = (short)bakeData.clips[i].clipData.Length;
                dstClips[i].sampleRate     = bakeData.clips[i].sampleRate;
                dstClips[i].duration       = math.rcp(bakeData.clips[i].sampleRate) * (bakeData.clips[i].clipData[0].Length - 1);
                ClipEventsBlobHelpers.Convert(ref dstClips[i].events, ref builder, bakeData.clips[i].events);

                var srcClipDataPacked = new NativeArray<float>(bakeData.clips[i].clipData.Length * bakeData.clips[i].clipData[0].Length, Allocator.Temp);
                var srcClipErrors     = new NativeArray<float>(bakeData.clips[i].clipData.Length, Allocator.Temp);

                float* packedPtr = (float*)srcClipDataPacked.GetUnsafePtr();
                for (int j = 0; j < bakeData.clips[i].clipData.Length; i++)
                {
                    UnsafeUtility.MemCpy(packedPtr, bakeData.clips[i].clipData[j].Ptr, bakeData.clips[i].clipData[j].Length);
                    packedPtr += bakeData.clips[i].clipData[j].Length;

                    srcClipErrors[j] = bakeData.clips[i].maxErrorsByParameter.IsCreated ? bakeData.clips[i].maxErrorsByParameter[j] : bakeData.clips[i].globalMaxError;
                }

                var compressedClip = AclUnity.Compression.CompressScalarsClip(srcClipDataPacked, srcClipErrors, bakeData.clips[i].sampleRate, bakeData.clips[i].compressionLevel);
                var compressedData = builder.Allocate(ref dstClips[i].compressedClipDataAligned16, compressedClip.compressedDataToCopyFrom.Length, 16);
                UnsafeUtility.MemCpy(compressedData.GetUnsafePtr(), compressedClip.compressedDataToCopyFrom.GetUnsafeReadOnlyPtr(), compressedClip.compressedDataToCopyFrom.Length);
                compressedClip.Dispose();
            }

            root.parameterCount = dstClips[0].parameterCount;

            return builder.CreateBlobAssetReference<ParameterClipSetBlob>(Allocator.Persistent);
        }
    }
}

