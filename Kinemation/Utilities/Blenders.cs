using System;
using Latios.Transforms;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// A struct used to sample and blend a buffer of local transforms.
    /// </summary>
    /// <remarks>
    /// The first pose sampled for a given instance will overwrite the storage.
    /// Additional samples will perform additive blending instead.
    ///
    /// To discard existing sampled poses and begin sampling new poses, simply create a new
    /// instance of BufferPoseBlender using the same input NativeArray.
    ///
    /// To finish sampling, call Normalize(). Prior to this, any transforms in the array
    /// may have unnormalized transforms, which can cause unwanted artifacts if used as is.
    ///
    /// You may also use a BufferPoseBlender to compute root-space transforms.
    /// </remarks>
    public struct BufferPoseBlender
    {
        internal NativeArray<AclUnity.Qvvs> bufferAsQvvs;
        internal bool                       sampledFirst;
        internal bool                       normalized;

        /// <summary>
        /// Creates a blender for blending sampled poses into the buffer. The buffer's Qvvs values are invalidated.
        /// </summary>
        /// <param name="localSpaceBuffer">The buffer used for blending operations.</param>
        public BufferPoseBlender(NativeArray<TransformQvvs> localSpaceBuffer)
        {
            bufferAsQvvs = localSpaceBuffer.Reinterpret<AclUnity.Qvvs>();
            sampledFirst = false;
            normalized   = false;
        }

        /// <summary>
        /// Normalizes the transforms to be valid using the accumulated weights stored in translation.w of each bone.
        /// Call this once after sampling all blended poses.
        /// The buffer passed in will contain valid local-space transforms after calling this method.
        /// </summary>
        public unsafe void Normalize()
        {
            var bufferPtr = (AclUnity.Qvvs*)bufferAsQvvs.GetUnsafePtr();
            for (int i = 0; i < bufferAsQvvs.Length; i++, bufferPtr++)
            {
                if (bufferPtr->translation.w == 1f)
                    continue;
                var weight               = 1f / bufferPtr->translation.w;
                bufferPtr->rotation      = math.normalize(bufferPtr->rotation);
                bufferPtr->translation  *= weight;
                bufferPtr->stretchScale *= weight;
            }
            normalized = true;
        }

        /// <summary>
        /// Computes the Root-Space Transforms given the parent indices and stores the results in rootSpaceBuffer.
        /// The transform at index 0 is assumed to represent root motion, and so all other transforms ignore it,
        /// even if they specify index 0 as a parent.
        /// </summary>
        /// <param name="parentIndices">The parent index of each bone</param>
        /// <param name="rootSpaceBuffer">The buffer to write the destination values. This is allowed to be the same as the local-space buffer.</param>
        public void ComputeRootSpaceTransforms(ReadOnlySpan<short> parentIndices, ref NativeArray<TransformQvvs> rootSpaceBuffer)
        {
            var localSpaceBuffer = bufferAsQvvs.Reinterpret<TransformQvvs>();
            if (!normalized)
            {
                var temp = localSpaceBuffer[0];
                temp.NormalizeBone();
                rootSpaceBuffer[0] = TransformQvvs.identity;
                for (int i = 1; i < localSpaceBuffer.Length; i++)
                {
                    var parent = math.max(0, parentIndices[i]);
                    var local  = localSpaceBuffer[i];
                    local.NormalizeBone();
                    rootSpaceBuffer[i] = qvvs.mul(rootSpaceBuffer[parent], in local);
                }
                {
                    localSpaceBuffer[0] = temp;
                    rootSpaceBuffer[0]  = temp;
                }
            }
            else
            {
                var temp           = localSpaceBuffer[0];
                rootSpaceBuffer[0] = TransformQvvs.identity;
                for (int i = 1; i < localSpaceBuffer.Length; i++)
                {
                    var parent         = math.max(0, parentIndices[i]);
                    rootSpaceBuffer[i] = qvvs.mul(rootSpaceBuffer[parent], localSpaceBuffer[i]);
                }
                rootSpaceBuffer[0] = localSpaceBuffer[0] = temp;
            }
        }

        // Todo: Baked Root space? Custom parent indices? Convert to Root-Space in-place? Convert to World-Space?
    }

    public static class BoneNormalizationExtensions
    {
        public static void NormalizeBone(ref this TransformQvvs localTransform)
        {
            var w            = math.asfloat(localTransform.worldIndex);
            w                = 1f / w;
            ref var t        = ref UnsafeUtility.As<TransformQvvs, AclUnity.Qvvs>(ref localTransform);
            t.rotation       = math.normalize(t.rotation);
            t.translation   *= w;
            t.stretchScale  *= w;
            t.translation.w  = 1f;
        }
    }
}

