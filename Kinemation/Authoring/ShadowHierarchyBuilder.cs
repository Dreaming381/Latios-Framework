using System;
using System.Collections.Generic;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    internal static class ShadowHierarchyBuilder
    {
        public static GameObject BuildShadowHierarchy(GameObject source, bool sourceIsOptimized)
        {
            var shadow                     = GameObject.Instantiate(source);
            shadow.transform.localPosition = Vector3.zero;
            shadow.transform.localRotation = Quaternion.identity;
            shadow.transform.localScale    = Vector3.one;

            if (!sourceIsOptimized)
            {
                s_immediateChildrenToDestroy.Clear();
                RecurseExposedChildren(source.transform, shadow.transform);
                foreach (var toDestroy in s_immediateChildrenToDestroy)
                {
                    if (toDestroy != null)
                        UnityEngine.Object.DestroyImmediate(toDestroy.gameObject);
                }
                s_immediateChildrenToDestroy.Clear();
            }
            else
            {
                TagAndPruneOptimizedChildren(source.transform, shadow.transform);
                AnimatorUtility.DeoptimizeTransformHierarchy(shadow);
            }

            return shadow;
        }

        public static void DeleteSkinnedMeshPaths(GameObject shadowHierarchy)
        {
            var tracker = shadowHierarchy.GetComponentInChildren<HideThis.ShadowCloneSkinnedMeshTracker>(true);

            while (tracker != null)
            {
                UnityEngine.Object.DestroyImmediate(tracker.gameObject);

                tracker = shadowHierarchy.GetComponentInChildren<HideThis.ShadowCloneSkinnedMeshTracker>(true);
            }
        }

        static List<Transform> s_immediateChildrenToDestroy = new List<Transform>();

        static void RecurseExposedChildren(Transform source, Transform shadow)
        {
            var tracker    = shadow.gameObject.AddComponent<HideThis.ShadowCloneTracker>();
            tracker.source = source.gameObject;

            var sourceChildCount = source.childCount;
            if (sourceChildCount != shadow.childCount)
                Debug.LogError("Instantiate did not preserve hierarchy. This is an internal bug between Kinemation and Unity. Please report!");

            for (int i = 0; i < sourceChildCount; i++)
            {
                var sourceChild = source.GetChild(i);
                var shadowChild = shadow.GetChild(i);

                // If the child has an Animator or SkinnedMeshRenderer, it shouldn't be animated, so delete it.
                if (shadowChild.GetComponent<SkinnedMeshRenderer>() != null || shadowChild.GetComponent<Animator>() != null ||
                    shadowChild.GetComponent<ExcludeFromSkeletonAuthoring>() != null)
                {
                    s_immediateChildrenToDestroy.Add(shadowChild);
                    continue;
                }

                RecurseExposedChildren(sourceChild, shadowChild);
            }
        }

        static void TagAndPruneOptimizedChildren(Transform sourceRoot, Transform shadowRoot)
        {
            s_immediateChildrenToDestroy.Clear();
            int childCount = shadowRoot.childCount;
            if (sourceRoot.childCount != childCount)
                Debug.LogError("Instantiate did not preserve hierarchy. This is an internal bug between Kinemation and Unity. Please report!");

            {
                var tracker    = shadowRoot.gameObject.AddComponent<HideThis.ShadowCloneTracker>();
                tracker.source = sourceRoot.gameObject;
            }

            for (int i = 0; i < childCount; i++)
            {
                var sourceChild = sourceRoot.GetChild(i);
                var shadowChild = shadowRoot.GetChild(i);

                // If the child has an Animator or SkinnedMeshRenderer, it shouldn't be animated, so delete it.
                if (shadowChild.GetComponent<SkinnedMeshRenderer>() != null || shadowChild.GetComponent<Animator>() != null ||
                    shadowChild.GetComponent<ExcludeFromSkeletonAuthoring>() != null)
                {
                    RecurseTagSkinnedOrDelete(sourceChild, shadowChild);
                    continue;
                }

                var tracker    = shadowChild.gameObject.AddComponent<HideThis.ShadowCloneTracker>();
                tracker.source = sourceChild.gameObject;

                // In an optimized hierarchy, only the first layer of children are valid, as they are the exported bones.
                // We delete the rest in the shadow hierarchy, so that what remains are just the bones we care about.
                // But before we delete them, we need to extract any SkinnedMeshRenderers to get the bones array.
                // So any child that has a SkinnedMeshRenderer descendent will get that path tagged for later deletion instead.
                if (sourceChild.childCount != shadowChild.childCount)
                    Debug.LogError("Instantiate did not preserve hierarchy. This is an internal bug between Kinemation and Unity. Please report!");
                for (int j = 0; j < shadowChild.childCount; j++)
                {
                    RecurseTagSkinnedOrDelete(sourceChild.GetChild(j), shadowChild.GetChild(j));
                }
            }

            foreach (var toDestroy in s_immediateChildrenToDestroy)
            {
                if (toDestroy != null)
                    UnityEngine.Object.DestroyImmediate(toDestroy.gameObject);
            }
            s_immediateChildrenToDestroy.Clear();

            static void RecurseTagSkinnedOrDelete(Transform source, Transform shadow)
            {
                if (shadow.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                {
                    var tracker    = shadow.gameObject.AddComponent<HideThis.ShadowCloneSkinnedMeshTracker>();
                    tracker.source = source.gameObject;

                    if (source.childCount != shadow.childCount)
                        Debug.LogError("Instantiate did not preserve hierarchy. This is an internal bug between Kinemation and Unity. Please report!");
                    for (int i = 0; i < shadow.childCount; i++)
                    {
                        RecurseTagSkinnedOrDelete(source.GetChild(i), shadow.GetChild(i));
                    }
                }
                else
                {
                    s_immediateChildrenToDestroy.Add(shadow);
                }
            }
        }
    }
}

