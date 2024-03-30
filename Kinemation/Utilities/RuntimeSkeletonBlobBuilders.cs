using System;
using System.Collections.Generic;
using Latios.Authoring;
using Latios.Kinemation.Authoring;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Latios.Kinemation.RuntimeBlobBuilders
{
    // Main thread only - no Burst
    public struct SkeletonClipSetSampler : IDisposable
    {
        static Queue<(Transform, int)> s_breadthQueue    = new Queue<(Transform, int)>();
        static List<Transform>         s_transformsCache = new List<Transform>();

        internal GameObject           shadowHierarchy;
        internal TransformAccessArray taa;
        internal NativeList<short>    parentIndices;

        public SkeletonClipSetSampler(Animator referenceAnimator)
        {
            shadowHierarchy = ShadowHierarchyBuilder.BuildShadowHierarchy(referenceAnimator.gameObject, !referenceAnimator.hasTransformHierarchy);
            ShadowHierarchyBuilder.DeleteSkinnedMeshPaths(shadowHierarchy);
            parentIndices = new NativeList<short>(Allocator.Persistent);  // TAA doesn't have an allocator, so we use persistent to match its behavior.

            s_breadthQueue.Clear();
            s_transformsCache.Clear();

            s_breadthQueue.Enqueue((shadowHierarchy.transform, -1));

            while (s_breadthQueue.Count > 0)
            {
                var (bone, parentIndex) = s_breadthQueue.Dequeue();
                int currentIndex        = parentIndices.Length;
                parentIndices.Add((short)parentIndex);
                s_transformsCache.Add(bone);

                for (int i = 0; i < bone.childCount; i++)
                {
                    var child = bone.GetChild(i);
                    s_breadthQueue.Enqueue((child, currentIndex));
                }
            }

            taa = new TransformAccessArray(s_transformsCache.Count);
            foreach (var tf in s_transformsCache)
                taa.Add(tf);
            shadowHierarchy.SetActive(false);
        }

        // -1 for no parent
        public short GetBoneParent(int boneIndex) => parentIndices[boneIndex];

        public string GetNameOfBone(int boneIndex) => taa[boneIndex].gameObject.name;

        public unsafe SkeletonClipSetSampleData Sample(ReadOnlySpan<SkeletonClipConfig> clips, AllocatorManager.AllocatorHandle allocator)
        {
            shadowHierarchy.SetActive(true);
            SkeletonClipSetSampleData result = default;
            result.clips                     = new UnsafeList<SkeletonClipToBake>(clips.Length, allocator);
            result.clips.Length              = clips.Length;

            int totalRequiredTransforms = 0;
            int totalRequiredEvents     = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                var clipConfig         = clips[i];
                var clip               = clipConfig.clip.Value;
                int requiredSamples    = Mathf.CeilToInt(clip.frameRate * clip.length) + (clipConfig.settings.copyFirstKeyAtEnd ? 1 : 0);
                var requiredTransforms = requiredSamples * taa.length;

                ref var output = ref result.clips.ElementAt(i);
                output         = new SkeletonClipToBake
                {
                    clip               = clipConfig.clip,
                    settings           = clipConfig.settings,
                    eventsStart        = totalRequiredEvents,
                    eventsCount        = clipConfig.events.Length,
                    clipName           = clip.name,
                    sampleRate         = clip.frameRate,
                    boneTransformStart = totalRequiredTransforms,
                    boneTransformCount = requiredTransforms
                };
                totalRequiredTransforms += requiredTransforms;
                totalRequiredEvents     += clipConfig.events.Length;
            }
            result.boneSamplesBuffer        = new UnsafeList<TransformQvvs>(totalRequiredTransforms, allocator);
            result.boneSamplesBuffer.Length = totalRequiredTransforms;
            result.events                   = new UnsafeList<ClipEvent>(totalRequiredEvents, allocator);

            for (int i = 0; i < clips.Length; i++)
            {
                var clipConfig = clips[i];
                var clip       = result.clips[i];

                SampleClip(ref result.boneSamplesBuffer,
                           clipConfig.clip.Value,
                           clip.boneTransformStart,
                           clipConfig.settings.copyFirstKeyAtEnd,
                           clipConfig.settings.rootMotionOverrideMode);
                if (clipConfig.events.IsCreated && clipConfig.events.Length > 0)
                    result.events.AddRangeNoResize(clipConfig.events.GetUnsafeReadOnlyPtr(), clipConfig.events.Length);
            }
            result.parentIndices = new UnsafeList<short>(parentIndices.Length, allocator);
            result.parentIndices.AddRangeNoResize(*parentIndices.GetUnsafeList());

            shadowHierarchy.SetActive(false);

            return result;
        }

        public void Dispose()
        {
            taa.Dispose();
            shadowHierarchy.DestroySafelyFromAnywhere();
            parentIndices.Dispose();
        }

        void SampleClip(ref UnsafeList<TransformQvvs>                          boneTransforms,
                        AnimationClip clip,
                        int startIndex,
                        bool copyFirstPose,
                        SkeletonClipCompressionSettings.RootMotionOverrideMode rootMotionMode)
        {
            int requiredSamples = Mathf.CeilToInt(clip.frameRate * clip.length) + (copyFirstPose ? 1 : 0);

            var oldWrapMode                   = clip.wrapMode;
            clip.wrapMode                     = WrapMode.Clamp;
            Animator animator                 = null;
            bool     backupRootMotionSettings = false;
            if (rootMotionMode != SkeletonClipCompressionSettings.RootMotionOverrideMode.UseAnimatorSettings)
            {
                animator                 = shadowHierarchy.GetComponent<Animator>();
                backupRootMotionSettings = animator.applyRootMotion;
                animator.applyRootMotion = rootMotionMode == SkeletonClipCompressionSettings.RootMotionOverrideMode.EnableRootMotion;
            }

            float timestep = math.rcp(clip.frameRate);
            var   job      = new CaptureBoneSamplesJob
            {
                boneTransforms = boneTransforms,
                samplesPerBone = requiredSamples,
                currentSample  = 0,
                startOffset    = startIndex,
            };

            if (copyFirstPose)
                requiredSamples--;

            for (int i = 0; i < requiredSamples; i++)
            {
                clip.SampleAnimation(shadowHierarchy, timestep * i);
                job.currentSample = i;
                job.RunReadOnly(taa);
            }

            if (copyFirstPose)
            {
                clip.SampleAnimation(shadowHierarchy, 0f);
                job.currentSample = requiredSamples;
                job.RunReadOnly(taa);
            }

            if (animator != null)
                animator.applyRootMotion = backupRootMotionSettings;
            clip.wrapMode                = oldWrapMode;
        }

        [BurstCompile]
        struct CaptureBoneSamplesJob : IJobParallelForTransform
        {
            public UnsafeList<TransformQvvs> boneTransforms;
            public int                       samplesPerBone;
            public int                       currentSample;
            public int                       startOffset;

            public void Execute(int index, TransformAccess transform)
            {
                int target             = startOffset + index * samplesPerBone + currentSample;
                boneTransforms[target] = new TransformQvvs(transform.localPosition, transform.localRotation, 1f, transform.localScale);
            }
        }
    }

    // Job and Burst compatible
    public struct SkeletonClipSetSampleData : IDisposable
    {
        internal UnsafeList<TransformQvvs>      boneSamplesBuffer;
        internal UnsafeList<short>              parentIndices;
        internal UnsafeList<SkeletonClipToBake> clips;
        internal UnsafeList<ClipEvent>          events;

        public int boneCount => parentIndices.Length;

        // -1 for no parent
        public short GetBoneParent(int boneIndex) => parentIndices[boneIndex];

        public TransformQvvs SampleBone(int clipIndex, int boneIndex, int sampleIndex)
        {
            var clip   = clips[clipIndex];
            var target = clip.boneTransformStart + boneIndex * clip.boneTransformCount + sampleIndex;
            return boneSamplesBuffer[target];
        }

        public unsafe void BuildBlob(ref BlobBuilder builder, ref SkeletonClipSetBlob blob)
        {
            var boneTransforms = CollectionHelper.ConvertExistingDataToNativeArray<AclUnity.Qvvs>(boneSamplesBuffer.Ptr, boneSamplesBuffer.Length, Allocator.None, true);
            var eventsArray    = CollectionHelper.ConvertExistingDataToNativeArray<ClipEvent>(events.Ptr, events.Length, Allocator.None, true);

            blob.boneCount = (short)parentIndices.Length;
            var blobClips  = builder.Allocate(ref blob.clips, clips.Length);

            // Step 1: Patch parent hierarchy for ACL
            var parents = new NativeArray<short>(parentIndices.Length, Allocator.Temp);
            for (short i = 0; i < parentIndices.Length; i++)
            {
                short index = parentIndices[i];
                if (index < 0)
                    index  = i;
                parents[i] = index;
            }

            int clipIndex = 0;
            foreach (var srcClip in clips)
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
                var qvvArray = boneTransforms.GetSubArray(srcClip.boneTransformStart, srcClip.boneTransformCount);

                // Step 4: Compress
                var compressedClip = AclUnity.Compression.CompressSkeletonClip(parents, qvvArray, srcClip.sampleRate, aclSettings);

                // Step 5: Build blob clip
                blobClips[clipIndex]      = default;
                blobClips[clipIndex].name = srcClip.clipName;
                var eventsRange           = eventsArray.GetSubArray(srcClip.eventsStart, srcClip.eventsCount);
                ClipEventsBlobHelpers.Convert(ref blobClips[clipIndex].events, ref builder, eventsRange);

                var compressedData = builder.Allocate(ref blobClips[clipIndex].compressedClipDataAligned16, compressedClip.sizeInBytes, 16);
                compressedClip.CopyTo((byte*)compressedData.GetUnsafePtr());

                // Step 6: Dispose ACL memory and safety
                compressedClip.Dispose();

                clipIndex++;
            }
        }

        public void Dispose()
        {
            boneSamplesBuffer.Dispose();
            parentIndices.Dispose();
            clips.Dispose();
            events.Dispose();
        }
    }
}

