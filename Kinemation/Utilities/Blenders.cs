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
    /// To finish sampling, call NormalizeRotations(). Prior to this, any transforms in the array
    /// may have unnormalized rotations, which can cause unwanted artifacts if used as is.
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
            normalized   = true;
        }

        /// <summary>
        /// Normalizes the rotations to be valid quaternion rotations. Call this once after sampling all blended poses.
        /// The result of GetLocalTransformsView() will contain valid quaternion rotations after calling this method.
        /// You can perform IK operations after calling this method but before calling ApplyBoneHierarchyAndFinish().
        /// </summary>
        public unsafe void NormalizeRotations()
        {
            var bufferPtr = (TransformQvvs*)bufferAsQvvs.GetUnsafePtr();
            for (int i = 0; i < bufferAsQvvs.Length; i++, bufferPtr++)
            {
                bufferPtr->rotation = math.normalize(bufferPtr->rotation);
            }
            normalized = true;
        }

        /// <summary>
        /// Computes the Root-Space Transforms given the parent indices and stores the results in rootSpaceBuffer.
        /// The transform at index 0 is assumed to represent root motion, and so all other transforms ignore it,
        /// even if they specify index 0 as a parent.
        /// </summary>
        /// <param name="parentIndices"></param>
        /// <param name="rootSpaceBuffer"></param>
        public void ComputeRootSpaceTransforms(ref BlobArray<short> parentIndices, ref NativeArray<TransformQvvs> rootSpaceBuffer)
        {
            var localSpaceBuffer = bufferAsQvvs.Reinterpret<TransformQvvs>();
            if (!normalized)
            {
                rootSpaceBuffer[0] = TransformQvvs.identity;
                for (int i = 1; i < localSpaceBuffer.Length; i++)
                {
                    var parent           = math.max(0, parentIndices[i]);
                    var local            = localSpaceBuffer[i];
                    local.rotation.value = math.normalize(local.rotation.value);
                    rootSpaceBuffer[i]   = qvvs.mul(rootSpaceBuffer[parent], in local);
                }
                {
                    var local            = localSpaceBuffer[0];
                    local.rotation.value = math.normalize(local.rotation.value);
                    localSpaceBuffer[0]  = local;
                    rootSpaceBuffer[0]   = local;
                }
            }
            else
            {
                rootSpaceBuffer[0] = TransformQvvs.identity;
                for (int i = 1; i < localSpaceBuffer.Length; i++)
                {
                    var parent         = math.max(0, parentIndices[i]);
                    rootSpaceBuffer[i] = qvvs.mul(rootSpaceBuffer[parent], localSpaceBuffer[i]);
                }
                rootSpaceBuffer[0] = localSpaceBuffer[0];
            }
        }

        // Todo: Baked Root space? Custom parent indices? Convert to Root-Space in-place? Convert to World-Space?
    }
}

