using System.Collections.Generic;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Latios.Kinemation.Authoring.Systems
{
    [UpdateInGroup(typeof(GameObjectBeforeConversionGroup))]
    [ConverterVersion("Latios", 2)]
    [DisableAutoCreation]
    public class DiscoverSkeletonsConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var skinnedMeshToAuthoringEntityDictionary = new Dictionary<SkinnedMeshRenderer, Entity>();
            Entities.ForEach((Entity entity, SkinnedMeshRenderer renderer) => skinnedMeshToAuthoringEntityDictionary.Add(renderer, entity));

            Entities.ForEach((Entity entity, Animator animator) =>
            {
                //Debug.Log($"Discovered skeleton on {animator.gameObject.name}");

                var context      = new SkeletonConversionContext();
                context.animator = animator;
                if (EntityManager.HasComponent<SkeletonAuthoring>(entity))
                {
                    context.authoring = EntityManager.GetComponentObject<SkeletonAuthoring>(entity);

                    if (context.authoring.bindingMode == BindingMode.Import)
                    {
                        switch (context.authoring.m_importStatus)
                        {
                            case ImportStatus.AmbiguityError:
                                Debug.LogError($"{animator.gameObject.name} contains a skeleton ambiguity error and will not be converted to an entity correctly.");
                                return;
                            case ImportStatus.Uninitialized:
                                Debug.LogError($"{animator.gameObject.name} is trying to use skeleton BindingMode.Import, but this feature has not been implemented yet.");
                                return;
                            case ImportStatus.UnknownError:
                                Debug.LogError(
                                    $"{animator.gameObject.name} encountered an unexpected error during skeleton import. Try reimporting and pay attention to any errors logged.");
                                return;
                            case ImportStatus.Success:
                                {
                                    context.skeleton = context.authoring.m_importSkeleton;
                                    break;
                                }
                        }
                    }
                    else if (context.authoring.bindingMode == BindingMode.Custom)
                    {
                        if (context.authoring.customSkeleton == null)
                        {
                            Debug.LogError($"{animator.gameObject.name} is trying to use skeleton BindingMode.Custom, but no custom skeleton data was provided.");
                            return;
                        }
                        context.skeleton = context.authoring.customSkeleton.ToArray();
                    }
                }

                var hierarchy       = animator.gameObject;
                context.isOptimized = !animator.hasTransformHierarchy;
                if (context.isOptimized)
                {
                    hierarchy = context.shadowHierarchy;
                }

                if (context.skeleton == null)
                    context.skeleton = AnalyzeHierarchy(hierarchy.transform);

                if (context.isOptimized)
                {
                    for (int i = 0; i < context.skeleton.Length; i++)
                    {
                        var bone = context.skeleton[i];

                        var tracker = bone.gameObjectTransform.GetComponent<HideThis.ShadowCloneTracker>();
                        if (tracker == null)
                        {
                            bone.gameObjectTransform = null;
                        }
                        else
                        {
                            bone.gameObjectTransform = tracker.source.transform;
                        }

                        context.skeleton[i] = bone;
                    }
                }
                else
                {
                    foreach (var bone in context.skeleton)
                    {
                        if (bone.gameObjectTransform != null)
                        {
                            DeclareDependency(animator,            bone.gameObjectTransform);
                            DeclareDependency(animator.gameObject, bone.gameObjectTransform.gameObject);
                        }
                    }
                }

                PostUpdateCommands.AddComponent(entity, context);
                DiscoverSkinnedMeshes(context, skinnedMeshToAuthoringEntityDictionary);
            });
        }

        List<BoneTransformData> m_boneCache          = new List<BoneTransformData>();
        Queue<(Transform, int)> m_breadthQueue       = new Queue<(Transform, int)>();
        StringBuilder           m_stringBuilderCache = new StringBuilder();

        BoneTransformData[] AnalyzeHierarchy(Transform root)
        {
            m_boneCache.Clear();
            m_breadthQueue.Clear();

            m_breadthQueue.Enqueue((root.transform, -1));

            while (m_breadthQueue.Count > 0)
            {
                var (bone, parentIndex) = m_breadthQueue.Dequeue();
                int currentIndex        = m_boneCache.Count;
                m_boneCache.Add(new BoneTransformData
                {
                    gameObjectTransform  = bone,
                    hierarchyReversePath = GetReversePath(root.transform, bone),
                    localPosition        = bone.localPosition,
                    localRotation        = bone.localRotation,
                    localScale           = bone.localScale,
                    parentIndex          = parentIndex
                });

                for (int i = 0; i < bone.childCount; i++)
                {
                    var child = bone.GetChild(i);
                    if (child.GetComponent<SkinnedMeshRenderer>() == null)
                        m_breadthQueue.Enqueue((bone.GetChild(i), currentIndex));
                }
            }

            return m_boneCache.ToArray();
        }

        string GetReversePath(Transform root, Transform pathTarget, bool excludeRootFromPath = false)
        {
            m_stringBuilderCache.Clear();

            var t = pathTarget;

            while (t != root)
            {
                m_stringBuilderCache.Append(t.gameObject.name);
                m_stringBuilderCache.Append('/');
                t = t.parent;
            }

            if (!excludeRootFromPath)
            {
                var unshadowedRoot = t.GetComponent<HideThis.ShadowCloneTracker>();
                if (unshadowedRoot != null)
                    t = unshadowedRoot.source.transform;
                m_stringBuilderCache.Append(t.gameObject.name);
                m_stringBuilderCache.Append('/');
            }

            return m_stringBuilderCache.ToString();
        }

        List<SkinnedMeshRenderer> m_smrCache = new List<SkinnedMeshRenderer>();
        void DiscoverSkinnedMeshes(SkeletonConversionContext skeletonContext, Dictionary<SkinnedMeshRenderer, Entity> smrToAuthoringEntityDictionary)
        {
            m_smrCache.Clear();
            Transform root;
            if (skeletonContext.isOptimized)
            {
                skeletonContext.shadowHierarchy.GetComponentsInChildren(true, m_smrCache);
                root = skeletonContext.shadowHierarchy.transform;
            }
            else
            {
                skeletonContext.animator.GetComponentsInChildren(true, m_smrCache);
                root = skeletonContext.animator.transform;
            }

            foreach (var smr in m_smrCache)
            {
                var authoring = smr.GetComponent<SkinnedMeshSettingsAuthoring>();
                var realSmr   = smr;

                if (skeletonContext.isOptimized)
                {
                    var tracker = smr.GetComponent<HideThis.ShadowCloneTracker>();
                    if (tracker != null)
                    {
                        realSmr   = tracker.source.GetComponent<SkinnedMeshRenderer>();
                        authoring = tracker.source.GetComponent<SkinnedMeshSettingsAuthoring>();
                    }
                }

                if (authoring != null)
                {
                    if (authoring.bindingMode == BindingMode.Import)
                    {
                        if (authoring.m_importBonePathsReversed == null)
                        {
                            Debug.LogError($"Skinned Mesh {smr.gameObject.name} is trying to use skeleton BindingMode.Import, but this feature hasn't been implemented yet.");
                            continue;
                        }
                        var meshContext = new SkinnedMeshConversionContext
                        {
                            authoring         = authoring,
                            bonePathsReversed = authoring.m_importBonePathsReversed,
                            renderer          = realSmr,
                            skeletonContext   = skeletonContext
                        };
                        PostUpdateCommands.AddComponent(GetPrimaryEntity(authoring), meshContext);
                        continue;
                    }
                    else if (authoring.bindingMode == BindingMode.Custom)
                    {
                        if (authoring.customBonePathsReversed == null)
                        {
                            Debug.LogError($"{smr.gameObject.name} is trying to use skinned mesh BindingMode.Custom, but no custom skeleton data was provided.");
                            continue;
                        }
                        var meshContext = new SkinnedMeshConversionContext
                        {
                            authoring         = authoring,
                            bonePathsReversed = authoring.customBonePathsReversed.ToArray(),
                            renderer          = realSmr,
                            skeletonContext   = skeletonContext
                        };
                        PostUpdateCommands.AddComponent(GetPrimaryEntity(authoring), meshContext);
                        continue;
                    }
                }

                if (smr.bones == null)
                {
                    Debug.LogError($"Skinned mesh {smr.gameObject.name} does not have valid bones.");
                    continue;
                }

                var bones = smr.bones;
                var paths = new string[bones.Length];

                for (int i = 0; i < bones.Length; i++)
                {
                    paths[i] = GetReversePath(root, bones[i], true);
                    if (smr == realSmr)
                    {
                        DeclareDependency(realSmr,            bones[i]);
                        DeclareDependency(realSmr.gameObject, bones[i].gameObject);
                    }
                }
                DeclareDependency(realSmr.gameObject, skeletonContext.animator.gameObject);

                {
                    var meshContext = new SkinnedMeshConversionContext
                    {
                        authoring         = authoring,
                        bonePathsReversed = paths,
                        renderer          = realSmr,
                        skeletonContext   = skeletonContext
                    };
                    var smrEntity = smrToAuthoringEntityDictionary[realSmr];
                    PostUpdateCommands.AddComponent(smrEntity, meshContext);
                }
            }
        }
    }
}

