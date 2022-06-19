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

            Recurse(source.transform, shadow.transform);

            if (sourceIsOptimized)
            {
                AnimatorUtility.DeoptimizeTransformHierarchy(shadow);
            }

            return shadow;
        }

        static void Recurse(Transform source, Transform shadow)
        {
            var tracker    = shadow.gameObject.AddComponent<HideThis.ShadowCloneTracker>();
            tracker.source = source.gameObject;

            var sourceChildCount = source.childCount;
            if (sourceChildCount != shadow.childCount)
                Debug.LogError("Instantiate did not preserve hierarchy. This is an internal bug between Kinemation and Unity. Please report!");

            for (int i = 0; i < sourceChildCount; i++)
            {
                Recurse(source.GetChild(i), shadow.GetChild(i));
            }
        }
    }
}

