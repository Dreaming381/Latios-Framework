using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Specifies additional customizations to be made for baking a SkinnedMeshRenderer.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/Skinned Mesh Settings")]
    public class SkinnedMeshSettingsAuthoring : MonoBehaviour
    {
        /// <summary>
        /// Specifies how the SkinnedMeshRenderer is generated.
        /// </summary>
        public BindingMode bindingMode = BindingMode.BakeTime;

        /// <summary>
        /// Enables Dual Quaternion Skinning instead of Linear Blend (Matrix) Skinning.
        /// </summary>
        public bool useDualQuaternionSkinning = false;

        /// <summary>
        /// Assign a list of bone name hierarchy paths (leaf to root delineated with '/') corresponding to each bone index in the mesh
        /// (which also corresponds to the BindPoses array). Each path will be matched to a bone in the skeleton whose skeleton path
        /// starts with the mesh path. If all bone names are unique, then theoretically, parent paths and '/' delineation are unnecessary.
        /// Use this to correct binding issues with custom workflows.
        /// </summary>
        public List<string> customBonePathsReversed;

        public enum BindingMode
        {
            /// <summary>
            /// Skips processing of the SkinnedMeshRenderer entirely.
            /// </summary>
            DoNotGenerate,
            /// <summary>
            /// Processes the SkinnedMeshRenderer based on the GameObject hierarchy, Animator Avatar, and SkinnedMeshRenderer bone bindings at bake time.
            /// </summary>
            BakeTime,
            /// <summary>
            /// Processes the SkinnedMeshRenderer, but uses customBonePathsReversed for generating the MeshBindingPathsBlobReference component,
            /// which is used to deform the mesh by the skeleton at runtime.
            /// </summary>
            OverridePaths
        }
    }

    public class SkinnedMeshSettingsBaker : Baker<SkinnedMeshSettingsAuthoring>
    {
        public override void Bake(SkinnedMeshSettingsAuthoring authoring)
        {
            if (authoring.useDualQuaternionSkinning)
            {
                AddComponent<DualQuaternionSkinningDeformTag>(GetEntity(TransformUsageFlags.Renderable));
            }
        }
    }
}

