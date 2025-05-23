#if !LATIOS_DISABLE_ACL
using System.Collections.Generic;
using Latios.Authoring;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Defines a skeleton animation clip as well as its compression settings
    /// </summary>
    public struct SkeletonClipConfig
    {
        /// <summary>
        /// The clip which should be played on the animator and baked into the blob
        /// </summary>
        public UnityObjectRef<AnimationClip> clip;
        /// <summary>
        /// The compression settings used for compressing the clip.
        /// </summary>
        public SkeletonClipCompressionSettings settings;
        /// <summary>
        /// An array of events to be attached to the clip blob. The order will be sorted during blob creation.
        /// An array can be auto-generated by calling AnimationClip.ExtractKinemationClipEvents().
        /// </summary>
        public NativeArray<ClipEvent> events;
    }

    /// <summary>
    /// Compression settings for a skeleton animation clip.
    /// </summary>
    public struct SkeletonClipCompressionSettings
    {
        /// <summary>
        /// Higher levels lead to longer compression time but more compressed clips.
        /// Values range from 0 to 4 or 100 for automatic mode.
        /// </summary>
        public short compressionLevel;
        /// <summary>
        /// The maximum distance a point sampled some distance away from the bone can
        /// deviate from the original authored animation due to lossy compression.
        /// Typical default is one ten-thousandth of a Unity unit.
        /// Warning! This is measured when the character root is animated with initial
        /// local scale of 1f!
        /// </summary>
        public float maxDistanceError;
        /// <summary>
        /// How far away from the bone points are sampled when evaluating distance error.
        /// Defaults to 3% of a Unity unit.
        /// Warning! This is measured when the character root is animated with initial
        /// local scale of 1f!
        /// </summary>
        public float sampledErrorDistanceFromBone;
        /// <summary>
        /// The max uniform scale value error. The underlying compression library requires
        /// the uniform scale values to be compressed first independently currently.
        /// Defaults to 1 / 100_000.
        /// </summary>
        public float maxUniformScaleError;

        /// <summary>
        /// Looping clips must have matching start and end poses.
        /// If the source clip does not have this, setting this value to true can correct clip.
        /// This setting is typically not compatible with root motion.
        /// </summary>
        public bool copyFirstKeyAtEnd;

        public enum RootMotionOverrideMode
        {
            /// <summary>
            /// Use whatever settings the animator is configured with
            /// </summary>
            UseAnimatorSettings = 0,
            /// <summary>
            /// Force exclusion of root motion for the source clip
            /// </summary>
            DisableRootMotion = 1,
            /// <summary>
            /// Force usage of root motion for the source clip
            /// </summary>
            EnableRootMotion = 2
        }

        /// <summary>
        /// Allows changing the root motion mode when sampling the source clip.
        /// This affects which bone inherits root motion data and in what form.
        /// Normally, if the clip contains root motion, you want to bake it with
        /// root motion enabled, even if you intend to not use root motion at
        /// runtime. Doing so avoids the artifact of characters teleporting.
        /// </summary>
        public RootMotionOverrideMode rootMotionOverrideMode;

        /// <summary>
        /// Default animation clip compression settings. These provide relative fast compression,
        /// decently small clip sizes, and typically acceptable accuracy.
        /// (The accuracy is way higher than Unity's default animation compression)
        /// </summary>
        public static readonly SkeletonClipCompressionSettings kDefaultSettings = new SkeletonClipCompressionSettings
        {
            compressionLevel             = 100,
            maxDistanceError             = 0.0001f,
            sampledErrorDistanceFromBone = 0.03f,
            maxUniformScaleError         = 0.00001f,
            copyFirstKeyAtEnd            = false,
            rootMotionOverrideMode       = RootMotionOverrideMode.EnableRootMotion
        };

        /// <summary>
        /// Overrides the root motion mode for these settings. You would typically call this per clip on kDefaultSettings or some other universal settings instance.
        /// </summary>
        /// <param name="useRootMotion">If true, root motion will be enabled when sampling the source clip. Otherwise it will be disabled.</param>
        /// <returns>A new settings instance with the override applied</returns>
        public SkeletonClipCompressionSettings WithRootMotionOverride(bool useRootMotion)
        {
            rootMotionOverrideMode = useRootMotion ? RootMotionOverrideMode.EnableRootMotion : RootMotionOverrideMode.DisableRootMotion;
            return this;
        }
    }

    public static class SkeletonClipSetBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a SkeletonClipSetBlob Blob Asset
        /// </summary>
        /// <param name="animator">An animator on which to sample the animations (a clone will be temporarily created).
        /// If the animator is not structurally identical to the one used to generate a skeleton
        /// that will play this clip at runtime, results are undefined.</param>
        /// <param name="clips">An array of clips along with their events and compression settings which should be compressed into the blob asset.
        /// This array can be temp-allocated.</param>
        public static SmartBlobberHandle<SkeletonClipSetBlob> RequestCreateBlobAsset(this IBaker baker, Animator animator, NativeArray<SkeletonClipConfig> clips)
        {
            return baker.RequestCreateBlobAsset<SkeletonClipSetBlob, SkeletonClipSetBakeData>(new SkeletonClipSetBakeData
            {
                animator = animator,
                clips    = clips
            });
        }
    }
    /// <summary>
    /// Input for the SkeletonClipSetBlob Smart Blobber
    /// </summary>
    public struct SkeletonClipSetBakeData : ISmartBlobberRequestFilter<SkeletonClipSetBlob>
    {
        /// <summary>
        /// The UnityEngine.Animator that should sample this clip.
        /// The converted clip will only work correctly with that GameObject's converted skeleton entity
        /// or another skeleton entity with an identical hierarchy.
        /// </summary>
        public Animator animator;
        /// <summary>
        /// The list of clips and their compression settings which should be baked into the clip set.
        /// </summary>
        public NativeArray<SkeletonClipConfig> clips;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (animator == null)
            {
                Debug.LogError($"Kinemation failed to bake clip set requested by a baker of {baker.GetAuthoringObjectForDebugDiagnostics().name}. The Animator was null.");
                return false;
            }
            if (!clips.IsCreated)
            {
                Debug.LogError($"Kinemation failed to bake clip set on animator {animator.gameObject.name}. The clips array was not allocated.");
                return false;
            }

            int i = 0;
            foreach (var clip in clips)
            {
                if (clip.clip.GetHashCode() == 0)
                {
                    Debug.LogError($"Kinemation failed to bake clip set on animator {animator.gameObject.name}. Clip at index {i} was null.");
                    return false;
                }
                i++;
            }

            baker.DependsOn(animator.avatar);
            baker.AddComponent(blobBakingEntity, new ShadowHierarchyRequest
            {
                animatorToBuildShadowFor = animator
            });
            var clipEventsBuffer = baker.AddBuffer<ClipEventToBake>(blobBakingEntity).Reinterpret<ClipEvent>();
            var clipsBuffer      = baker.AddBuffer<SkeletonClipToBake>(blobBakingEntity);
            baker.AddBuffer<SampledBoneTransform>(          blobBakingEntity);
            baker.AddBuffer<SkeletonClipSetBoneParentIndex>(blobBakingEntity);
            foreach (var clip in clips)
            {
                var clipValue = clip.clip.Value;
                baker.DependsOn(clipValue);
                clipsBuffer.Add(new SkeletonClipToBake
                {
                    clip        = clip.clip,
                    settings    = clip.settings,
                    eventsStart = clipEventsBuffer.Length,
                    eventsCount = clip.events.Length,
                    clipName    = clipValue.name,
                    sampleRate  = clipValue.frameRate
                });
                if (clip.events.Length > 0)
                    clipEventsBuffer.AddRange(clip.events);
            }

            return true;
        }
    }

    [TemporaryBakingType]
    internal struct SampledBoneTransform : IBufferElementData
    {
        public TransformQvvs boneTransform;
    }

    [TemporaryBakingType]
    internal struct SkeletonClipSetBoneParentIndex : IBufferElementData
    {
        public int parentIndex;
    }

    [TemporaryBakingType]
    internal struct SkeletonClipToBake : IBufferElementData
    {
        public UnityObjectRef<AnimationClip>   clip;
        public FixedString128Bytes             clipName;
        public float                           sampleRate;
        public SkeletonClipCompressionSettings settings;
        public int                             eventsStart;
        public int                             eventsCount;
        public int                             boneTransformStart;
        public int                             boneTransformCount;
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class SkeletonClipSetSmartBlobberSystem : SystemBase
    {
        Queue<(Transform, int)> m_breadthQueue;
        List<Transform>         m_transformsCache;

        protected override void OnCreate()
        {
            new SmartBlobberTools<SkeletonClipSetBlob>().Register(World);
        }

        protected override void OnUpdate()
        {
            if (m_breadthQueue == null)
                m_breadthQueue = new Queue<(Transform, int)>();
            if (m_transformsCache == null)
                m_transformsCache = new List<Transform>();

            foreach ((var parentIndices, var sampledBoneTransforms, var clipsToBake, var shadowRef) in
                     SystemAPI.Query<DynamicBuffer<SkeletonClipSetBoneParentIndex>, DynamicBuffer<SampledBoneTransform>, DynamicBuffer<SkeletonClipToBake>,
                                     RefRO<ShadowHierarchyReference> >()
                     .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
            {
                var shadow = shadowRef.ValueRO.shadowHierarchyRoot.Value;
                var taa    = FetchParentsAndTransformAccessArray(parentIndices, shadow);

                for (int i = 0; i < clipsToBake.Length; i++)
                {
                    ref var clip            = ref clipsToBake.ElementAt(i);
                    var     startAndCount   = SampleClip(sampledBoneTransforms, taa, clip.clip, clip.settings.copyFirstKeyAtEnd, clip.settings.rootMotionOverrideMode);
                    clip.boneTransformStart = startAndCount.x;
                    clip.boneTransformCount = startAndCount.y;
                }

                taa.Dispose();
            }

            m_breadthQueue.Clear();
            m_transformsCache.Clear();

            new CompressClipsAndBuildBlobJob().ScheduleParallel();
        }

        // Unlike in 0.5, this version assumes the breadth-first layout skeleton that the shadow hierarchy builds matches the runtime skeleton layout.
        TransformAccessArray FetchParentsAndTransformAccessArray(DynamicBuffer<SkeletonClipSetBoneParentIndex> parentIndices, GameObject shadow)
        {
            m_breadthQueue.Clear();
            m_transformsCache.Clear();

            m_breadthQueue.Enqueue((shadow.transform, -1));

            while (m_breadthQueue.Count > 0)
            {
                var (bone, parentIndex)                                            = m_breadthQueue.Dequeue();
                int currentIndex                                                   = parentIndices.Length;
                parentIndices.Add(new SkeletonClipSetBoneParentIndex { parentIndex = parentIndex });
                m_transformsCache.Add(bone);

                for (int i = 0; i < bone.childCount; i++)
                {
                    var child = bone.GetChild(i);
                    m_breadthQueue.Enqueue((child, currentIndex));
                }
            }

            var taa = new TransformAccessArray(m_transformsCache.Count);
            foreach (var tf in m_transformsCache)
                taa.Add(tf);
            return taa;
        }

        int2 SampleClip(DynamicBuffer<SampledBoneTransform>                    appendNewSamplesToThis,
                        TransformAccessArray shadowHierarchy,
                        AnimationClip clip,
                        bool copyFirstPose,
                        SkeletonClipCompressionSettings.RootMotionOverrideMode rootMotionMode)
        {
            int requiredSamples    = Mathf.CeilToInt(clip.frameRate * clip.length + 0.1f) + (copyFirstPose ? 1 : 0);
            int requiredTransforms = requiredSamples * shadowHierarchy.length;
            int startIndex         = appendNewSamplesToThis.Length;
            appendNewSamplesToThis.ResizeUninitialized(requiredTransforms + appendNewSamplesToThis.Length);

            var boneTransforms = appendNewSamplesToThis.Reinterpret<TransformQvvs>().AsNativeArray().GetSubArray(startIndex, requiredTransforms);

            var oldWrapMode                   = clip.wrapMode;
            clip.wrapMode                     = WrapMode.Clamp;
            var      root                     = shadowHierarchy[0].gameObject;
            Animator animator                 = null;
            bool     backupRootMotionSettings = false;
            if (rootMotionMode != SkeletonClipCompressionSettings.RootMotionOverrideMode.UseAnimatorSettings)
            {
                animator                 = root.GetComponent<Animator>();
                backupRootMotionSettings = animator.applyRootMotion;
                animator.applyRootMotion = rootMotionMode == SkeletonClipCompressionSettings.RootMotionOverrideMode.EnableRootMotion;
            }

            float timestep = math.rcp(clip.frameRate);
            var   job      = new CaptureBoneSamplesJob
            {
                boneTransforms = boneTransforms,
                samplesPerBone = requiredSamples,
                currentSample  = 0
            };

            if (copyFirstPose)
                requiredSamples--;

            for (int i = 0; i < requiredSamples; i++)
            {
                clip.SampleAnimation(root, timestep * i);
                job.currentSample = i;
                job.RunReadOnly(shadowHierarchy);
            }

            if (copyFirstPose)
            {
                clip.SampleAnimation(root, 0f);
                job.currentSample = requiredSamples;
                job.RunReadOnly(shadowHierarchy);
            }

            if (animator != null)
                animator.applyRootMotion = backupRootMotionSettings;
            clip.wrapMode                = oldWrapMode;

            return new int2(startIndex, requiredTransforms);
        }

        [BurstCompile]
        partial struct CaptureBoneSamplesJob : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction]  // Why is this necessary when we are using RunReadOnly()?
            public NativeArray<TransformQvvs> boneTransforms;
            public int                        samplesPerBone;
            public int                        currentSample;

            public void Execute(int index, TransformAccess transform)
            {
                int target             = index * samplesPerBone + currentSample;
                boneTransforms[target] = new TransformQvvs(transform.localPosition, transform.localRotation, 1f, transform.localScale);
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct CompressClipsAndBuildBlobJob : IJobEntity
        {
            public unsafe void Execute(ref SmartBlobberResult result,
                                       ref DynamicBuffer<ClipEventToBake>               clipEventsBuffer,  // Ref so it can be sorted
                                       in DynamicBuffer<SkeletonClipToBake>             clipsBuffer,
                                       in DynamicBuffer<SkeletonClipSetBoneParentIndex> parentsBuffer,
                                       in DynamicBuffer<SampledBoneTransform>           boneSamplesBuffer)
            {
                var parents = parentsBuffer.Reinterpret<int>().AsNativeArray();

                var     builder = new BlobBuilder(Allocator.Temp);
                ref var root    = ref builder.ConstructRoot<SkeletonClipSetBlob>();
                root.boneCount  = (short)parents.Length;
                var blobClips   = builder.Allocate(ref root.clips, clipsBuffer.Length);

                // Step 1: Patch parent hierarchy for ACL
                var parentIndices = new NativeArray<short>(parents.Length, Allocator.Temp);
                for (short i = 0; i < parents.Length; i++)
                {
                    short index = (short)parents[i];
                    if (index < 0)
                        index        = i;
                    parentIndices[i] = index;
                }

                int clipIndex = 0;
                foreach (var srcClip in clipsBuffer)
                {
                    // Step 2: Convert settings
                    var aclSettings = new AclUnity.Compression.SkeletonCompressionSettings
                    {
                        compressionLevel             = srcClip.settings.compressionLevel,
                        maxDistanceError             = srcClip.settings.maxDistanceError,
                        maxUniformScaleError         = srcClip.settings.maxUniformScaleError,
                        sampledErrorDistanceFromBone = srcClip.settings.sampledErrorDistanceFromBone
                    };

                    // Step 3: Encode bone samples into QVV array
                    var qvvArray = boneSamplesBuffer.Reinterpret<AclUnity.Qvvs>().AsNativeArray().GetSubArray(srcClip.boneTransformStart, srcClip.boneTransformCount);

                    // Step 4: Compress
                    var compressedClip = AclUnity.Compression.CompressSkeletonClip(parentIndices, qvvArray, srcClip.sampleRate, aclSettings);

                    // Step 5: Build blob clip
                    blobClips[clipIndex]      = default;
                    blobClips[clipIndex].name = srcClip.clipName;
                    var events                = clipEventsBuffer.Reinterpret<ClipEvent>().AsNativeArray().GetSubArray(srcClip.eventsStart, srcClip.eventsCount);
                    ClipEventsBlobHelpers.Convert(ref blobClips[clipIndex].events, ref builder, events);

                    var compressedData = builder.Allocate(ref blobClips[clipIndex].compressedClipDataAligned16, compressedClip.sizeInBytes, 16);
                    compressedClip.CopyTo((byte*)compressedData.GetUnsafePtr());

                    // Step 6: Dispose ACL memory and safety
                    compressedClip.Dispose();

                    clipIndex++;
                }

                result.blob = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<SkeletonClipSetBlob>(Allocator.Persistent));
            }
        }
    }
}
#endif

