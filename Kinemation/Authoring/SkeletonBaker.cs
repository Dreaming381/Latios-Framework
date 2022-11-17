using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// This baker provides the default baking implementation for converting Animator to a Skeleton.
    /// See Kinemation's documentation for details.
    /// </summary>
    [DisableAutoCreation]
    public class SkeletonBaker : Baker<Animator>
    {
        List<SkinnedMeshRenderer> m_skinnedMeshRenderersCache = new List<SkinnedMeshRenderer>();
        Queue<(GameObject, int)>  m_breadthQueue              = new Queue<(GameObject, int)>();

        public override void Bake(Animator authoring)
        {
            var settings = GetComponent<SkeletonSettingsAuthoring>();

            if (settings != null)
            {
                if (settings.bindingMode == SkeletonSettingsAuthoring.BindingMode.DoNotGenerate)
                    return;
            }

            m_skinnedMeshRenderersCache.Clear();
            GetComponentsInChildren(m_skinnedMeshRenderersCache);
            var skinnedMeshesToBind = AddBuffer<AutoBindSkinnedMeshToSkeleton>().Reinterpret<Entity>();
            foreach (var skinnedMesh in m_skinnedMeshRenderersCache)
            {
                var skinnedMeshSettings = GetComponent<SkinnedMeshSettingsAuthoring>(skinnedMesh);
                if (skinnedMeshSettings != null && skinnedMeshSettings.bindingMode == SkinnedMeshSettingsAuthoring.BindingMode.DoNotGenerate)
                    continue;

                // Another Animator has priority (like hair bones)
                if (GetComponentInParent<Animator>(skinnedMesh) != authoring)
                    continue;

                skinnedMeshesToBind.Add(GetEntity(skinnedMesh));
            }

            AddComponent<SkeletonRootTag>();

            // For now we just always assume we have the whole hierarchy to fetch.
            if (authoring.hasTransformHierarchy)
            {
                // Analyze the hierarchy and build the BoneReference buffer here.
                // A baking system will add the culling components.
                var boneBuffer = AddBuffer<BoneReference>().Reinterpret<Entity>();
                var boneNames  = new NativeList<SkeletonBoneNameInHierarchy>(Allocator.Temp);

                m_breadthQueue.Clear();

                m_breadthQueue.Enqueue((authoring.gameObject, -1));

                while (m_breadthQueue.Count > 0)
                {
                    var (bone, parentIndex) = m_breadthQueue.Dequeue();
                    int currentIndex        = boneNames.Length;
                    boneBuffer.Add(GetEntity(bone));
                    boneNames.Add(new SkeletonBoneNameInHierarchy
                    {
                        boneName    = GetName(bone),
                        parentIndex = parentIndex
                    });

                    for (int i = 0; i < GetChildCount(bone); i++)
                    {
                        var child = GetChild(bone, i);
                        if (GetComponent<SkinnedMeshRenderer>(child) == null && GetComponentInParent<Animator>(child) == authoring)
                            m_breadthQueue.Enqueue((child, currentIndex));
                    }
                }

                if (boneNames.Length > 0)
                {
                    AddComponent(new PendingSkeletonBindingPathsBlob
                    {
                        blobHandle = this.RequestCreateBlobAsset(boneNames.AsArray())
                    });
                    AddComponent<SkeletonBindingPathsBlobReference>();
                }
            }
            else
            {
                AddComponent(new PendingSkeletonBindingPathsBlob
                {
                    blobHandle = this.RequestCreateBlobAsset(authoring, GetName())
                });
                AddComponent<SkeletonBindingPathsBlobReference>();
                AddComponent(new PendingOptimizedSkeletonHierarchyBlob
                {
                    blobHandle = this.RequestCreateBlobAsset(authoring)
                });
                AddComponent<OptimizedSkeletonHierarchyBlobReference>();

                var boneEntityBuffer = AddBuffer<OptimizedSkeletonExportedBone>();
                var boneGoBuffer     = AddBuffer<ExportedBoneGameObjectRef>();
                for (int i = 0; i < GetChildCount(); i++)
                {
                    var child = GetChild(i);
                    if (GetComponent<SkinnedMeshRenderer>(child) != null || GetComponent<Animator>(child) != null)
                        continue;

                    boneGoBuffer.Add(new ExportedBoneGameObjectRef { authoringGameObjectForBone = child });
                    boneEntityBuffer.Add(new OptimizedSkeletonExportedBone { boneEntity         = GetEntity(child, TransformUsageFlags.ManualOverride) });
                }
                this.RequestAddAndPopulateOptimizedBoneToRootForAnimator(GetEntity(), authoring);
            }

            m_breadthQueue.Clear();
            m_skinnedMeshRenderersCache.Clear();
        }
    }

    public static class SkeletonBakerExtensions
    {
        public static void RequestAddAndPopulateOptimizedBoneToRootForAnimator(this IBaker baker, Entity target, Animator animator)
        {
            if (animator == null)
                return;

            baker.DependsOn(animator.avatar);
            baker.AddComponent(target, new ShadowHierarchyRequest { animatorToBuildShadowFor = animator });
            baker.AddBuffer<OptimizedBoneToRoot>(target);
        }
    }
}

