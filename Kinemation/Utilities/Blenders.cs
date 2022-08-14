using System;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// A struct used to blend a full OptimizedBoneToRoot buffer. Rotations use nlerp blending.
    /// </summary>
    /// <remarks>
    /// A BufferPoseBlender reinterprets an OptimizedBoneToRoot as temporary storage to sample
    /// and accumulate local space BoneTransforms. The first pose sampled for a given instance
    /// will overwrite the storage. Additional samples will perform additive blending instead.
    ///
    /// To discard existing sampled poses and begin sampling new poses, simply create a new
    /// instance of BufferPoseBlender using the same OptimizedBoneToRoot buffer.
    ///
    /// To finish sampling, call NormalizeRotations(). You can then get a view of the BoneTransforms
    /// using GetLocalTransformsView().
    ///
    /// If you leave the buffer in this state, a new BufferPoseBlender instance can recover this
    /// view by immediately calling GetLocalTransformsView(). This allows you to separate sampling
    /// and IK into separate jobs.
    ///
    /// When you are done performing any sampling or BoneTransform manipulation, call
    /// ApplyBoneHierarchyAndFinish().
    /// </remarks>
    public struct BufferPoseBlender
    {
        internal NativeArray<float4x4>     bufferAs4x4;
        internal NativeArray<AclUnity.Qvv> bufferAsQvv;
        internal bool                      sampledFirst;

        /// <summary>
        /// Creates a blender for blending sampled poses into the buffer. The buffer's matrix values are invalidated.
        /// </summary>
        /// <param name="boneToRootBuffer">The buffer used for blending operations. Its memory is temporarily repurposed for accumulating poses. No resizing is performed.</param>
        public BufferPoseBlender(DynamicBuffer<OptimizedBoneToRoot> boneToRootBuffer)
        {
            bufferAs4x4        = boneToRootBuffer.Reinterpret<float4x4>().AsNativeArray();
            var boneCount      = bufferAs4x4.Length;
            var bufferAsFloat4 = bufferAs4x4.Reinterpret<float4>(64);
            bufferAsQvv        = bufferAsFloat4.GetSubArray(bufferAsFloat4.Length - 3 * boneCount, 3 * boneCount).Reinterpret<AclUnity.Qvv>(16);
            sampledFirst       = false;
        }

        /// <summary>
        /// Gets a view of the buffer as local transforms.
        /// The contents in this view are not valid until the first pose has been sampled.
        /// This is effectively the buffer used to accumulate blended poses, so data may not be normalized.
        /// </summary>
        /// <returns></returns>
        public NativeArray<BoneTransform> GetLocalTransformsView()
        {
            return bufferAsQvv.Reinterpret<BoneTransform>();
        }

        /// <summary>
        /// Normalizes the rotations to be valid quaternion rotations. Call this once after sampling all blended poses.
        /// The result of GetLocalTransformsView() will contain valid quaternion rotations after calling this method.
        /// You can perform IK operations after calling this method but before calling ApplyBoneHierarchyAndFinish().
        /// </summary>
        public unsafe void NormalizeRotations()
        {
            var bufferPtr = (BoneTransform*)bufferAsQvv.GetUnsafePtr();
            for (int i = 0; i < bufferAsQvv.Length; i++, bufferPtr++)
            {
                bufferPtr->rotation = math.normalize(bufferPtr->rotation);
            }
        }

        /// <summary>
        /// Computes a BoneToRoot matrix for a local space BoneTransform. The result can be multiplied with the skeleton's
        /// LocalToWorld to obtain a LocalToWorld for the bone. This method does not modify state.
        ///
        /// Warning: This method is only valid after calling NormalizeRotations().
        /// </summary>
        /// <param name="boneIndex">The bone index to compute a BoneToRoot matrix for</param>
        /// <param name="hierarchy">The skeleton hierarchy used to compute the bone's ancestors</param>
        /// <returns>A skeleton-space matrix representing the transform of boneIndex</returns>
        public float4x4 ComputeBoneToRoot(int boneIndex, BlobAssetReference<OptimizedSkeletonHierarchyBlob> hierarchy)
        {
            if (!hierarchy.Value.hasAnyParentScaleInverseBone)
            {
                // Fast path.
                var parentIndex = hierarchy.Value.parentIndices[boneIndex];
                var transform   = new BoneTransform(bufferAsQvv[boneIndex]);
                var matrix      = float4x4.TRS(transform.translation, transform.rotation, transform.scale);
                while (parentIndex >= 0)
                {
                    transform = new BoneTransform(bufferAsQvv[parentIndex]);
                    matrix    = math.mul(float4x4.TRS(transform.translation, transform.rotation, transform.scale), matrix);

                    Hint.Assume(hierarchy.Value.parentIndices[parentIndex] < parentIndex);
                    parentIndex = hierarchy.Value.parentIndices[parentIndex];
                }

                return matrix;
            }
            else
            {
                var currentIndex = boneIndex;
                var parentIndex  = hierarchy.Value.parentIndices[boneIndex];
                var matrix       = float4x4.identity;

                while (currentIndex >= 0)
                {
                    Hint.Assume(parentIndex < currentIndex);

                    var transform = new BoneTransform(bufferAsQvv[currentIndex]);

                    if (hierarchy.Value.hasParentScaleInverseBitmask[currentIndex / 64].IsSet(currentIndex % 64))
                    {
                        var mat = math.mul(float4x4.Translate(transform.translation), float4x4.Scale(math.rcp(bufferAsQvv[parentIndex].scale.xyz)));
                        mat     = math.mul(mat, new float4x4(transform.rotation, 0f));
                        mat     = math.mul(mat, float4x4.Scale(transform.scale));

                        matrix = math.mul(mat, matrix);
                    }
                    else
                    {
                        matrix = math.mul(float4x4.TRS(transform.translation, transform.rotation, transform.scale), matrix);
                    }

                    parentIndex = hierarchy.Value.parentIndices[parentIndex];
                }

                return matrix;
            }
        }

        /// <summary>
        /// Computes a BoneToRoot matrix for a local space BoneTransform. The result can be multiplied with the skeleton's
        /// LocalToWorld to obtain a LocalToWorld for the bone. This method does not modify state.
        ///
        /// This variant uses an already known boneToRoot of an ancestor to avoid repeating calculations.
        ///
        /// Warning: This method is only valid after calling NormalizeRotations().
        /// </summary>
        /// <param name="boneIndex">The bone index to compute a BoneToRoot matrix for</param>
        /// <param name="hierarchy">The skeleton hierarchy used to compute the bone's ancestors</param>
        /// <param name="cachedAncestorBoneIndex">The bone index of an ancestor whose boneToRoot is already known</param>
        /// <param name="cachedAncestorBoneToRoot">The boneToRoot of the ancestor which is already known.</param>
        /// <returns>A skeleton-space matrix representing the transform of boneIndex</returns>
        public float4x4 ComputeBoneToRoot(int boneIndex,
                                          BlobAssetReference<OptimizedSkeletonHierarchyBlob> hierarchy,
                                          int cachedAncestorBoneIndex,
                                          in float4x4 cachedAncestorBoneToRoot)
        {
            if (!hierarchy.Value.hasAnyParentScaleInverseBone)
            {
                // Fast path.
                var parentIndex = hierarchy.Value.parentIndices[boneIndex];
                var transform   = new BoneTransform(bufferAsQvv[boneIndex]);
                var matrix      = float4x4.TRS(transform.translation, transform.rotation, transform.scale);
                while (parentIndex > cachedAncestorBoneIndex)
                {
                    transform = new BoneTransform(bufferAsQvv[parentIndex]);
                    matrix    = math.mul(float4x4.TRS(transform.translation, transform.rotation, transform.scale), matrix);

                    Hint.Assume(hierarchy.Value.parentIndices[parentIndex] < parentIndex);
                    parentIndex = hierarchy.Value.parentIndices[parentIndex];
                }

                return math.mul(cachedAncestorBoneToRoot, matrix);
            }
            else
            {
                var currentIndex = boneIndex;
                var parentIndex  = hierarchy.Value.parentIndices[boneIndex];
                var matrix       = float4x4.identity;

                while (currentIndex > cachedAncestorBoneIndex)
                {
                    Hint.Assume(parentIndex < currentIndex);

                    var transform = new BoneTransform(bufferAsQvv[currentIndex]);

                    if (hierarchy.Value.hasParentScaleInverseBitmask[currentIndex / 64].IsSet(currentIndex % 64))
                    {
                        var mat = math.mul(float4x4.Translate(transform.translation), float4x4.Scale(math.rcp(bufferAsQvv[parentIndex].scale.xyz)));
                        mat     = math.mul(mat, new float4x4(transform.rotation, 0f));
                        mat     = math.mul(mat, float4x4.Scale(transform.scale));

                        matrix = math.mul(mat, matrix);
                    }
                    else
                    {
                        matrix = math.mul(float4x4.TRS(transform.translation, transform.rotation, transform.scale), matrix);
                    }

                    parentIndex = hierarchy.Value.parentIndices[parentIndex];
                }

                return math.mul(cachedAncestorBoneToRoot, matrix);
            }
        }

        /// <summary>
        /// Applies the hierarchy to the bone transforms and restores the validity of the dynamic buffer.
        /// Call this once after calling NormalizeRotations().
        /// The result of GetLocalTransformsView() is invalidated after calling this method.
        /// </summary>
        /// <param name="hierarchy">The hierarchy to apply. If the hierarchy is shorter than the buffer length, only the subrange will be restored.</param>
        public void ApplyBoneHierarchyAndFinish(BlobAssetReference<OptimizedSkeletonHierarchyBlob> hierarchy)
        {
            int boneCount = math.min(bufferAs4x4.Length, hierarchy.Value.parentIndices.Length);

            if (!hierarchy.Value.hasAnyParentScaleInverseBone)
            {
                // Fast path.
                bufferAs4x4[0] = float4x4.identity;

                for (int i = 1; i < boneCount; i++)
                {
                    var qvv = bufferAsQvv[i];
                    var mat = float4x4.TRS(qvv.translation.xyz, qvv.rotation, qvv.scale.xyz);
                    Hint.Assume(hierarchy.Value.parentIndices[i] < i);
                    mat            = math.mul(bufferAs4x4[hierarchy.Value.parentIndices[i]], mat);
                    bufferAs4x4[i] = mat;
                }
            }
            else
            {
                // Slower path because we pack inverse scale into the fourth row of each matrix.
                // We need to explicitly check for parentScaleInverse for index 0.
                if (hierarchy.Value.hasChildWithParentScaleInverseBitmask[0].IsSet(0))
                {
                    var inverseScale = math.rcp(bufferAsQvv[0].scale);
                    var mat          = float4x4.identity;
                    mat.c0.w         = inverseScale.x;
                    mat.c1.w         = inverseScale.y;
                    mat.c2.w         = inverseScale.z;
                    bufferAs4x4[0]   = mat;
                }

                for (int i = 1; i < boneCount; i++)
                {
                    var qvv = bufferAsQvv[i];
                    Hint.Assume(hierarchy.Value.parentIndices[i] < i);

                    var  parentMat             = bufferAs4x4[hierarchy.Value.parentIndices[i]];
                    bool hasParentScaleInverse = hierarchy.Value.hasParentScaleInverseBitmask[i / 64].IsSet(i % 64);
                    var  psi                   = float4x4.Scale(math.select(1f, new float3(parentMat.c0.w, parentMat.c1.w, parentMat.c2.w), hasParentScaleInverse));
                    parentMat.c0.w             = 0f;
                    parentMat.c1.w             = 0f;
                    parentMat.c2.w             = 0f;
                    var mat                    = math.mul(float4x4.Translate(qvv.translation.xyz), psi);
                    mat                        = math.mul(mat, new float4x4(qvv.rotation, 0f));
                    mat                        = math.mul(mat, float4x4.Scale(qvv.scale.xyz));
                    mat                        = math.mul(parentMat, mat);

                    bool needsInverseScale = hierarchy.Value.hasChildWithParentScaleInverseBitmask[i / 64].IsSet(i % 64);
                    var  inverseScale      = math.select(0f, math.rcp(qvv.scale), needsInverseScale);
                    mat.c0.w               = inverseScale.x;
                    mat.c1.w               = inverseScale.y;
                    mat.c2.w               = inverseScale.z;
                    bufferAs4x4[i]         = mat;
                }

                // Now we need to clean up the inverse scales. We wrote zeros where we didn't need them.
                // So we can do a tzcnt walk.
                for (int maskId = 0; maskId * 64 < boneCount; maskId++)
                {
                    var mask = hierarchy.Value.hasChildWithParentScaleInverseBitmask[maskId];
                    for (int i = mask.CountTrailingZeros(); i < 64 && maskId * 64 + i < boneCount; mask.SetBits(i, false), i = mask.CountTrailingZeros())
                    {
                        var mat                      = bufferAs4x4[maskId * 64 + i];
                        mat.c0.w                     = 0f;
                        mat.c1.w                     = 0f;
                        mat.c2.w                     = 0f;
                        bufferAs4x4[maskId * 64 + i] = mat;
                    }
                }
            }
        }
    }
}

