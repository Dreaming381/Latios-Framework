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
                    var mat = float4x4.TRS(qvv.translation.xyz, qvv.rotation, qvv.scale.xyz);
                    Hint.Assume(hierarchy.Value.parentIndices[i] < i);

                    var  parentMat             = bufferAs4x4[hierarchy.Value.parentIndices[i]];
                    bool hasParentScaleInverse = hierarchy.Value.hasParentScaleInverseBitmask[i / 64].IsSet(i % 64);
                    var  psi                   = float4x4.Scale(math.select(1f, new float3(parentMat.c0.w, parentMat.c1.w, parentMat.c2.w), hasParentScaleInverse));
                    parentMat.c0.w             = 0f;
                    parentMat.c1.w             = 0f;
                    parentMat.c2.w             = 0f;
                    mat                        = math.mul(psi, mat);
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

