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
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Jobs;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Defines a skeleton animation clip as well as its compression settings
    /// </summary>
    public struct SkeletonClipConfig
    {
        public AnimationClip                   clip;
        public SkeletonClipCompressionSettings settings;
    }

    /// <summary>
    /// Input for the SkeletonClipSetBlob Smart Blobber
    /// </summary>
    public struct SkeletonClipSetBakeData
    {
        /// <summary>
        /// The UnityEngine.Animator that should sample this clip.
        /// The animator's GameObject must be a target for conversion.
        /// The converted clip will only work correctly with that GameObject's converted skeleton entity
        /// or another skeleton entity with an identical hierarchy.
        /// </summary>
        public Animator animator;
        /// <summary>
        /// The list of clips and their compression settings which should be baked into the clip set.
        /// </summary>
        public SkeletonClipConfig[] clips;
    }

    /// <summary>
    /// Compression settings for a skeleton animation clip.
    /// </summary>
    public struct SkeletonClipCompressionSettings
    {
        /// <summary>
        /// Higher levels lead to longer compression time but more compressed clips.
        /// Values range from 0 to 4. Typical default is 2.
        /// </summary>
        public short compressionLevel;
        /// <summary>
        /// The maximum distance a point sampled some distance away from the bone can
        /// deviate from the original authored animation due to lossy compression.
        /// Typical default is one ten-thousandth of a Unity unit.
        /// </summary>
        public float maxDistanceError;
        /// <summary>
        /// How far away from the bone points are sampled when evaluating distance error.
        /// Defaults to 3% of a Unity unit.
        /// </summary>
        public float sampledErrorDistanceFromBone;
        /// <summary>
        /// The noise threshold for scale animation to be considered constant for a brief time range.
        /// Defaults to 1 / 100_000.
        /// </summary>
        public float maxNegligibleTranslationDrift;
        /// <summary>
        /// The noise threshold for translation animation to be considered constant for a brief time range.
        /// Defaults to 1 / 100_000.
        /// </summary>
        public float maxNegligibleScaleDrift;

        /// <summary>
        /// Default animation clip compression settings. These provide relative fast compression,
        /// decently small clip sizes, and typically acceptable accuracy.
        /// (The accuracy is way higher than Unity's default animation compression)
        /// </summary>
        public static readonly SkeletonClipCompressionSettings kDefaultSettings = new SkeletonClipCompressionSettings
        {
            compressionLevel              = 2,
            maxDistanceError              = 0.0001f,
            sampledErrorDistanceFromBone  = 0.03f,
            maxNegligibleScaleDrift       = 0.00001f,
            maxNegligibleTranslationDrift = 0.00001f
        };
    }

    public static class SkeletonClipBlobberAPIExtensions
    {
        /// <summary>
        /// Requests creation of a SkeletonClipSetBlob by a Smart Blobber.
        /// This method must be called before the Smart Blobber is executed, such as during IRequestBlobAssets.
        /// </summary>
        /// <param name="gameObject">
        /// The Game Object to be converted that this blob should primarily be associated with.
        /// It is usually okay if this isn't quite accurate, such as if the blob will be added to multiple entities.
        /// </param>
        /// <param name="bakeData">The inputs used to generate the blob asset</param>
        /// <returns>Returns a handle that can be resolved into a blob asset after the Smart Blobber has executed, such as during IConvertGameObjectToEntity</returns>
        public static SmartBlobberHandle<SkeletonClipSetBlob> CreateBlob(this GameObjectConversionSystem conversionSystem,
                                                                         GameObject gameObject,
                                                                         SkeletonClipSetBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.SkeletonClipSetSmartBlobberSystem>().AddToConvert(gameObject, bakeData);
        }

        /// <summary>
        /// Requests creation of a SkeletonClipSetBlob by a Smart Blobber.
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
                                                                  SkeletonClipSetBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.SkeletonClipSetSmartBlobberSystem>().AddToConvertUntyped(gameObject, bakeData);
        }
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [ConverterVersion("Latios", 2)]
    [DisableAutoCreation]
    public class SkeletonClipSetSmartBlobberSystem : SmartBlobberConversionSystem<SkeletonClipSetBlob, SkeletonClipSetBakeData, SkeletonClipSetConverter>
    {
        Dictionary<Animator, SkeletonConversionContext> animatorToContextLookup = new Dictionary<Animator, SkeletonConversionContext>();

        protected override void GatherInputs()
        {
            animatorToContextLookup.Clear();
            Entities.ForEach((Animator animator, SkeletonConversionContext context) => animatorToContextLookup.Add(animator, context));
        }

        protected override bool Filter(in SkeletonClipSetBakeData input, GameObject gameObject, out SkeletonClipSetConverter converter)
        {
            converter = default;

            if (input.clips == null || input.animator == null)
                return false;
            foreach (var clip in input.clips)
            {
                if (clip.clip == null)
                {
                    Debug.LogError($"Kinemation failed to convert clip set on animator {input.animator.gameObject.name} due to one fo the clips being null.");
                    return false;
                }
                DeclareAssetDependency(gameObject, clip.clip);
            }

            if (!animatorToContextLookup.TryGetValue(input.animator, out SkeletonConversionContext context))
            {
                Debug.LogError($"Kinemation failed to convert clip set on animator {input.animator.gameObject.name} because the passed-in animator is not marked for conversion.");
                return false;
            }

            // Todo: Need to fix this for squash and stretch on optimized hierarchies.
            if (context.authoring != null && context.authoring.bindingMode != BindingMode.ConversionTime)
            {
                Debug.LogError(
                    $"Conversion of animation clips is not currently supported for a BindingMode other than ConversionTime. If you need this feature, let me know! Failed to convert clip set on animator {input.animator.gameObject.name}");
                return false;
            }

            var shadowHierarchy = BuildHierarchyFromShadow(context);
            if (!shadowHierarchy.isCreated)
            {
                // Assume error has already been logged.
                return false;
            }

            var allocator                    = World.UpdateAllocator.ToAllocator;
            converter.parents                = new UnsafeList<int>(context.skeleton.Length, allocator);
            converter.hasParentScaleInverses = new UnsafeList<bool>(context.skeleton.Length, allocator);
            converter.parents.Resize(context.skeleton.Length);
            converter.hasParentScaleInverses.Resize(context.skeleton.Length);
            for (int i = 0; i < context.skeleton.Length; i++)
            {
                converter.parents[i]                = context.skeleton[i].parentIndex;
                converter.hasParentScaleInverses[i] = context.skeleton[i].ignoreParentScale;
            }

            converter.clipsToConvert = new UnsafeList<SkeletonClipSetConverter.SkeletonClipConversionData>(input.clips.Length, allocator);
            converter.clipsToConvert.Resize(input.clips.Length);
            int targetClip = 0;
            foreach (var clip in input.clips)
            {
                converter.clipsToConvert[targetClip] = new SkeletonClipSetConverter.SkeletonClipConversionData
                {
                    clipName               = clip.clip.name,
                    sampleRate             = clip.clip.frameRate,
                    settings               = clip.settings,
                    sampledLocalTransforms = SampleClip(shadowHierarchy, clip.clip, allocator)
                };
                targetClip++;
            }
            shadowHierarchy.Dispose();

            return true;
        }

        Queue<Transform>                               m_breadthQueeue  = new Queue<Transform>();
        List<(Transform, HideThis.ShadowCloneTracker)> m_hierarchyCache = new List<(Transform, HideThis.ShadowCloneTracker)>();

        // Todo: If a single bone doesn't match, this currently fails entirely. Is that the correct solution?
        // The only failure case is if an optimized bone's path gets altered.
        // In that case, it might be best to log a warning and assign the skeleton definition's transform
        // to all samples for that bone.
        unsafe TransformAccessArray BuildHierarchyFromShadow(SkeletonConversionContext context)
        {
            var boneCount = context.skeleton.Length;
            var result    = new TransformAccessArray(boneCount);
            m_breadthQueeue.Clear();
            m_hierarchyCache.Clear();

            var root = context.shadowHierarchy.transform;
            m_breadthQueeue.Enqueue(root);
            while (m_breadthQueeue.Count > 0)
            {
                var bone    = m_breadthQueeue.Dequeue();
                var tracker = bone.GetComponent<HideThis.ShadowCloneTracker>();
                m_hierarchyCache.Add((bone, tracker));

                for (int i = 0; i < bone.childCount; i++)
                {
                    var child = bone.GetChild(i);
                    if (child.GetComponent<SkinnedMeshRenderer>() == null)
                        m_breadthQueeue.Enqueue(bone.GetChild(i));
                }
            }

            int boneIndex = 0;
            foreach (var registeredBone in context.skeleton)
            {
                bool found = false;

                if (registeredBone.gameObjectTransform != null)
                {
                    // The shadow hierarchy is an exact replica of the current hierarchy.
                    // And while the skeleton structure may have been modified, this reference
                    // persists through the current hierarchy at conversion time.
                    foreach ((var shadowBone, var tracker) in m_hierarchyCache)
                    {
                        if (tracker != null && tracker.source.transform == registeredBone.gameObjectTransform)
                        {
                            result.Add(shadowBone);
                            found = true;
                            break;
                        }
                    }
                }
                else
                {
                    // The bone is optimized, meaning that its name in the shadow hierarchy
                    // should match the name in the skeleton definition and the ancestry up
                    // to the first exposed or exported bone.
                    FixedString4096Bytes registeredPath      = registeredBone.hierarchyReversePath;
                    int                  ancestorsToTraverse = 0;
                    int                  currentParentIndex  = registeredBone.parentIndex;
                    for (var t = registeredBone.gameObjectTransform; t == null; ancestorsToTraverse++)
                    {
                        t                  = context.skeleton[currentParentIndex].gameObjectTransform;
                        currentParentIndex = context.skeleton[currentParentIndex].parentIndex;
                    }

                    foreach ((var shadowBone, var _) in m_hierarchyCache)
                    {
                        FixedString4096Bytes shadowPath      = default;
                        var                  shadowTransform = shadowBone;
                        bool                 skip            = false;
                        for (int i = 0; i < ancestorsToTraverse; i++)
                        {
                            shadowPath.Append(shadowTransform.gameObject.name);
                            shadowPath.Append('/');
                            shadowTransform = shadowTransform.parent;
                            if (shadowTransform == null)
                            {
                                skip = true;
                                break;
                            }
                        }
                        if (!skip && UnsafeUtility.MemCmp(shadowPath.GetUnsafePtr(), registeredPath.GetUnsafePtr(), shadowPath.Length) == 0)
                        {
                            found = true;
                            result.Add(shadowBone);
                            break;
                        }
                    }
                }

                if (!found)
                {
                    // We didn't find a match. Log and return an uncreated array so that the filter can further error handle.
                    Debug.LogError(
                        $"Conversion of an animation clip failed for animator {context.animator.gameObject.name} because no matching bone data was found for bone index {boneIndex} with path: {registeredBone.hierarchyReversePath}\nPlease ensure the gameObjectTransform is a valid descendent, or in the case of an optimized out bone, that its hierarchyReversedPath is correct.");
                    result.Dispose();
                    return default;
                }

                boneIndex++;
            }

            return result;
        }

        unsafe UnsafeList<BoneTransform> SampleClip(TransformAccessArray shadowHierarchy, AnimationClip clip, Allocator allocator)
        {
            int requiredSamples    = Mathf.CeilToInt(clip.frameRate * clip.length);
            int requiredTransforms = requiredSamples * shadowHierarchy.length;
            var result             = new UnsafeList<BoneTransform>(requiredTransforms, allocator);
            result.Resize(requiredTransforms);

            var oldWrapMode = clip.wrapMode;
            clip.wrapMode   = WrapMode.Clamp;
            var   root      = shadowHierarchy[0].gameObject;
            float timestep  = math.rcp(clip.frameRate);
            var   job       = new CaptureSampledBonesJob
            {
                boneTransforms = result,
                samplesPerBone = requiredSamples,
                currentSample  = 0
            };

            for (int i = 0; i < requiredSamples; i++)
            {
                clip.SampleAnimation(root, timestep * i);
                job.currentSample = i;
                job.RunReadOnly(shadowHierarchy);
            }

            clip.wrapMode = oldWrapMode;

            return result;
        }

        [BurstCompile]
        struct CaptureSampledBonesJob : IJobParallelForTransform
        {
            // Todo: This throws not created error on job schedule.
            public UnsafeList<BoneTransform> boneTransforms;
            public int                       samplesPerBone;
            public int                       currentSample;

            public void Execute(int index, TransformAccess transform)
            {
                int target             = index * samplesPerBone + currentSample;
                boneTransforms[target] = new BoneTransform(transform.localRotation, transform.localPosition, transform.localScale);
            }
        }
    }

    public struct SkeletonClipSetConverter : ISmartBlobberSimpleBuilder<SkeletonClipSetBlob>
    {
        internal struct SkeletonClipConversionData
        {
            public UnsafeList<BoneTransform>       sampledLocalTransforms;
            public FixedString128Bytes             clipName;
            public SkeletonClipCompressionSettings settings;
            public float                           sampleRate;
        }

        internal UnsafeList<SkeletonClipConversionData> clipsToConvert;
        internal UnsafeList<int>                        parents;
        public UnsafeList<bool>                         hasParentScaleInverses;

        public unsafe BlobAssetReference<SkeletonClipSetBlob> BuildBlob()
        {
            var     builder = new BlobBuilder(Allocator.Temp);
            ref var root    = ref builder.ConstructRoot<SkeletonClipSetBlob>();
            root.boneCount  = (short)parents.Length;
            var blobClips   = builder.Allocate(ref root.clips, clipsToConvert.Length);

            // Step 1: Patch parent hierarchy for ACL
            var parentIndices = new NativeArray<short>(parents.Length, Allocator.Temp);
            for (short i = 0; i < parents.Length; i++)
            {
                short index = (short)parents[i];
                if (index < 0)
                    index = i;
                if (hasParentScaleInverses[i])
                    index        *= -1;
                parentIndices[i]  = index;
            }

            int targetClip = 0;
            foreach (var clip in clipsToConvert)
            {
                // Step 2: Convert settings
                var aclSettings = new AclUnity.Compression.SkeletonCompressionSettings
                {
                    compressionLevel              = clip.settings.compressionLevel,
                    maxDistanceError              = clip.settings.maxDistanceError,
                    maxNegligibleScaleDrift       = clip.settings.maxNegligibleScaleDrift,
                    maxNegligibleTranslationDrift = clip.settings.maxNegligibleTranslationDrift,
                    sampledErrorDistanceFromBone  = clip.settings.sampledErrorDistanceFromBone
                };

                // Step 3: Encode bone samples into QVV array
                var qvvArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<AclUnity.Qvv>(clip.sampledLocalTransforms.Ptr,
                                                                                                       clip.sampledLocalTransforms.Length,
                                                                                                       Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref qvvArray, safety);
#endif

                // Step 4: Compress
                var compressedClip = AclUnity.Compression.CompressSkeletonClip(parentIndices, qvvArray, clip.sampleRate, aclSettings);

                // Step 5: Build blob clip
                blobClips[targetClip]            = default;
                blobClips[targetClip].name       = clip.clipName;
                blobClips[targetClip].duration   = math.rcp(clip.sampleRate) * (qvvArray.Length / parents.Length);
                blobClips[targetClip].boneCount  = root.boneCount;
                blobClips[targetClip].sampleRate = clip.sampleRate;

                var compressedData = builder.Allocate(ref blobClips[targetClip].compressedClipDataAligned16, compressedClip.compressedDataToCopyFrom.Length, 16);
                UnsafeUtility.MemCpy(compressedData.GetUnsafePtr(), compressedClip.compressedDataToCopyFrom.GetUnsafeReadOnlyPtr(), compressedClip.compressedDataToCopyFrom.Length);

                // Step 6: Dispose ACL memory and safety
                compressedClip.Dispose();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.Release(safety);
#endif

                targetClip++;
            }

            return builder.CreateBlobAssetReference<SkeletonClipSetBlob>(Allocator.Persistent);
        }
    }
}

;

